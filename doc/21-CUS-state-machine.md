# Component 21 — State Machine

**Library**: `KSquare.StateMachine`  
**Layer**: Cross-Cutting / Domain Lifecycle  
**Default Provider**: Stateless.NET (in-process, SQL-persisted)  
**Alternate Providers**: MassTransit State Machine (saga), Mock  
**Language**: C# / .NET 8  
**Depends On**: Component 02 (EventBus), Component 04 (AuditTrail)

---

## Why This Is a Pluggable Component

Three services each own a stateful lifecycle:

| Service | Entity | States |
|---|---|---|
| `ue-uw-submission-api` | Submission | Draft → Submitted → InReview → Referred → Approved → Declined → Withdrawn |
| `ue-uw-quote-api` | Quote | Draft → PricingRequested → Priced → ProposalGenerated → Presented → Accepted → Expired |
| `ue-uw-underwriting-api` | Referral | Open → UnderReview → PendingInfo → Approved → Declined → Withdrawn |

Without a shared library, every service:
- Implements its own raw string comparisons (`if (submission.Status == "InReview")`)
- Forgets to write audit trail entries on transitions
- Forgets to publish domain events
- Allows invalid transitions (jumping from Draft directly to Approved)

`KSquare.StateMachine` wraps **Stateless.NET** to:
1. Define valid state/trigger graphs declaratively in YAML or code
2. Guard invalid transitions at compile time (via typed enums)
3. Auto-write an `AuditTrail` entry on every transition (actor, from-state, to-state, trigger, entity ID)
4. Auto-publish a domain event via `IEventPublisher` on every transition
5. Persist current state to SQL via a generic `StateRecord` table
6. Support entry/exit actions and guard conditions per transition

---

## Interface Contract

```csharp
namespace KSquare.StateMachine.Contracts;

public interface IEntityStateMachine<TState, TTrigger>
    where TState  : struct, Enum
    where TTrigger : struct, Enum
{
    TState CurrentState { get; }

    // Check whether a trigger is allowed from the current state.
    bool CanFire(TTrigger trigger);

    // Fire a trigger, transitioning to the next state.
    // Throws InvalidTransitionException if the trigger is not permitted.
    Task FireAsync(
        TTrigger trigger,
        StateMachineContext context,
        CancellationToken ct = default);

    // Returns all permitted triggers from the current state.
    IReadOnlyList<TTrigger> PermittedTriggers { get; }
}

public interface IStateMachineFactory
{
    // Load or create a state machine for a specific entity instance.
    // Loads persisted current state from DB; creates with initial state if first call.
    Task<IEntityStateMachine<TState, TTrigger>> LoadAsync<TState, TTrigger>(
        string entityType,    // "Submission", "Quote", "Referral"
        string entityId,
        TState initialState,
        CancellationToken ct = default)
        where TState  : struct, Enum
        where TTrigger : struct, Enum;
}
```

---

## Models

```csharp
namespace KSquare.StateMachine.Models;

// Passed with every FireAsync call — carries actor identity and context metadata.
public record StateMachineContext
{
    public required string ActorId { get; init; }       // user ID or service principal
    public required string ActorName { get; init; }
    public string? Reason { get; init; }                // free-text reason for transition
    public string? CorrelationId { get; init; }
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

// Persisted to DB; one row per entity instance.
public record StateRecord
{
    public required string EntityType { get; init; }    // "Submission", "Quote", "Referral"
    public required string EntityId { get; init; }
    public required string CurrentState { get; init; }  // stored as string for schema agnosticism
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string UpdatedByActorId { get; init; }
    public int Version { get; init; } = 0;              // optimistic concurrency
}

// Domain event published on every transition (generic).
public record StateTransitionedEvent
{
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string FromState { get; init; }
    public required string ToState { get; init; }
    public required string Trigger { get; init; }
    public required string ActorId { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? CorrelationId { get; init; }
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public class InvalidTransitionException : Exception
{
    public string EntityType { get; }
    public string EntityId { get; }
    public string CurrentState { get; }
    public string AttemptedTrigger { get; }

    public InvalidTransitionException(
        string entityType, string entityId,
        string currentState, string trigger)
        : base($"Trigger '{trigger}' is not permitted from state '{currentState}' for {entityType}/{entityId}")
    {
        EntityType      = entityType;
        EntityId        = entityId;
        CurrentState    = currentState;
        AttemptedTrigger = trigger;
    }
}
```

---

## Pre-built Machine Definitions

### Submission State Machine

```csharp
namespace KSquare.StateMachine.Definitions;

public enum SubmissionState
{
    Draft,
    Submitted,
    InReview,
    Referred,
    Approved,
    Declined,
    Withdrawn
}

public enum SubmissionTrigger
{
    Submit,
    BeginReview,
    Refer,
    Approve,
    Decline,
    Withdraw,
    ReturnToReview    // from Referred back to InReview if referral resolved
}

public class SubmissionStateMachineDefinition : IStateMachineDefinition<SubmissionState, SubmissionTrigger>
{
    public void Configure(StateMachineBuilder<SubmissionState, SubmissionTrigger> builder)
    {
        builder.State(SubmissionState.Draft)
            .Permit(SubmissionTrigger.Submit,   SubmissionState.Submitted)
            .Permit(SubmissionTrigger.Withdraw, SubmissionState.Withdrawn);

        builder.State(SubmissionState.Submitted)
            .Permit(SubmissionTrigger.BeginReview, SubmissionState.InReview)
            .Permit(SubmissionTrigger.Withdraw,    SubmissionState.Withdrawn);

        builder.State(SubmissionState.InReview)
            .Permit(SubmissionTrigger.Refer,    SubmissionState.Referred)
            .Permit(SubmissionTrigger.Approve,  SubmissionState.Approved)
            .Permit(SubmissionTrigger.Decline,  SubmissionState.Declined)
            .Permit(SubmissionTrigger.Withdraw, SubmissionState.Withdrawn);

        builder.State(SubmissionState.Referred)
            .Permit(SubmissionTrigger.ReturnToReview, SubmissionState.InReview)
            .Permit(SubmissionTrigger.Approve,         SubmissionState.Approved)
            .Permit(SubmissionTrigger.Decline,         SubmissionState.Declined)
            .Permit(SubmissionTrigger.Withdraw,        SubmissionState.Withdrawn);

        // Terminal states — no outgoing transitions
        builder.State(SubmissionState.Approved).IsTerminal();
        builder.State(SubmissionState.Declined).IsTerminal();
        builder.State(SubmissionState.Withdrawn).IsTerminal();
    }
}
```

### Quote State Machine

```csharp
public enum QuoteState
{
    Draft,
    PricingRequested,
    Priced,
    ProposalGenerated,
    Presented,
    Accepted,
    Expired,
    Voided
}

public enum QuoteTrigger
{
    RequestPricing,
    PricingComplete,
    GenerateProposal,
    ProposalReady,
    Present,
    Accept,
    Expire,
    Void
}

public class QuoteStateMachineDefinition : IStateMachineDefinition<QuoteState, QuoteTrigger>
{
    public void Configure(StateMachineBuilder<QuoteState, QuoteTrigger> builder)
    {
        builder.State(QuoteState.Draft)
            .Permit(QuoteTrigger.RequestPricing, QuoteState.PricingRequested)
            .Permit(QuoteTrigger.Void,           QuoteState.Voided);

        builder.State(QuoteState.PricingRequested)
            .Permit(QuoteTrigger.PricingComplete, QuoteState.Priced)
            .Permit(QuoteTrigger.Void,            QuoteState.Voided);

        builder.State(QuoteState.Priced)
            .Permit(QuoteTrigger.GenerateProposal, QuoteState.ProposalGenerated)
            .Permit(QuoteTrigger.Void,             QuoteState.Voided);

        builder.State(QuoteState.ProposalGenerated)
            .Permit(QuoteTrigger.ProposalReady, QuoteState.Presented)
            .Permit(QuoteTrigger.Void,          QuoteState.Voided);

        builder.State(QuoteState.Presented)
            .Permit(QuoteTrigger.Accept, QuoteState.Accepted)
            .Permit(QuoteTrigger.Expire, QuoteState.Expired)
            .Permit(QuoteTrigger.Void,   QuoteState.Voided);

        builder.State(QuoteState.Accepted).IsTerminal();
        builder.State(QuoteState.Expired).IsTerminal();
        builder.State(QuoteState.Voided).IsTerminal();
    }
}
```

### Referral State Machine

```csharp
public enum ReferralState
{
    Open,
    UnderReview,
    PendingInfo,
    Approved,
    Declined,
    Withdrawn
}

public enum ReferralTrigger
{
    BeginReview,
    RequestInfo,
    InfoReceived,
    Approve,
    Decline,
    Withdraw
}

public class ReferralStateMachineDefinition : IStateMachineDefinition<ReferralState, ReferralTrigger>
{
    public void Configure(StateMachineBuilder<ReferralState, ReferralTrigger> builder)
    {
        builder.State(ReferralState.Open)
            .Permit(ReferralTrigger.BeginReview, ReferralState.UnderReview)
            .Permit(ReferralTrigger.Withdraw,    ReferralState.Withdrawn);

        builder.State(ReferralState.UnderReview)
            .Permit(ReferralTrigger.RequestInfo, ReferralState.PendingInfo)
            .Permit(ReferralTrigger.Approve,     ReferralState.Approved)
            .Permit(ReferralTrigger.Decline,     ReferralState.Declined)
            .Permit(ReferralTrigger.Withdraw,    ReferralState.Withdrawn);

        builder.State(ReferralState.PendingInfo)
            .Permit(ReferralTrigger.InfoReceived, ReferralState.UnderReview)
            .Permit(ReferralTrigger.Withdraw,     ReferralState.Withdrawn);

        builder.State(ReferralState.Approved).IsTerminal();
        builder.State(ReferralState.Declined).IsTerminal();
        builder.State(ReferralState.Withdrawn).IsTerminal();
    }
}
```

---

## Configuration

```csharp
public class StateMachineOptions
{
    public StateMachineProvider Provider { get; set; } = StateMachineProvider.Stateless;

    // Whether to publish a StateTransitionedEvent on every transition.
    public bool PublishTransitionEvents { get; set; } = true;

    // Whether to write an AuditTrail entry on every transition.
    public bool WriteAuditTrail { get; set; } = true;

    // Service Bus topic for transition events.
    public string TransitionEventTopic { get; set; } = "state-transitions";

    // Optimistic concurrency: retry up to N times on version conflict.
    public int ConcurrencyRetryAttempts { get; set; } = 3;
}

public enum StateMachineProvider { Stateless, Mock }
```

---

## DI Registration

```csharp
builder.Services.AddKsStateMachine(options =>
{
    builder.Configuration.GetSection("KSquare:StateMachine").Bind(options);
})
// Register built-in machine definitions
.AddStateMachineDefinition<SubmissionState, SubmissionTrigger, SubmissionStateMachineDefinition>()
.AddStateMachineDefinition<QuoteState, QuoteTrigger, QuoteStateMachineDefinition>()
.AddStateMachineDefinition<ReferralState, ReferralTrigger, ReferralStateMachineDefinition>();
// Requires KSquare.EventBus and KSquare.AuditTrail to be registered.
```

---

## Processing Flow

```
1. Service calls IStateMachineFactory.LoadAsync<SubmissionState, SubmissionTrigger>("Submission", submissionId, SubmissionState.Draft)

2. StatelessStateMachineFactory:
   a. SELECT current_state FROM state_records WHERE entity_type = 'Submission' AND entity_id = @id
   b. If no row → INSERT with initial_state = 'Draft'
   c. Build Stateless StateMachine<SubmissionState, SubmissionTrigger> from SubmissionStateMachineDefinition
   d. Set CurrentState to loaded DB state
   e. Return IEntityStateMachine<SubmissionState, SubmissionTrigger>

3. Service calls machine.FireAsync(SubmissionTrigger.Submit, context)

4. StatelessEntityStateMachine.FireAsync:
   a. Check CanFire(trigger) → throw InvalidTransitionException if false
   b. Record fromState = CurrentState
   c. machine.Fire(trigger) → transitions to new state (Stateless.NET)
   d. UPDATE state_records SET current_state = @newState, version = version + 1, updated_at = ...
      WHERE entity_id = @id AND version = @expectedVersion
      → if 0 rows affected (version conflict) → retry up to ConcurrencyRetryAttempts
   e. Write audit entry via IAuditTrailWriter:
      { Action = "StateTransition", Actor = context.ActorId,
        ResourceType = "Submission", ResourceId = entityId,
        Before = fromState, After = CurrentState,
        Reason = context.Reason }
   f. Publish StateTransitionedEvent via IEventPublisher
      to topic "state-transitions"

5. Consuming service receives StateTransitionedEvent:
   a. submission-api → update Submission.Status column from event
   b. notification-api → trigger broker/UW notification on key transitions
      (e.g., Approved → notify broker; Referred → notify senior UW)
```

---

## Consuming Service Usage

```csharp
// In SubmissionCommandService
public class SubmitSubmissionHandler(IStateMachineFactory smFactory)
{
    public async Task HandleAsync(SubmitSubmissionCommand cmd, CancellationToken ct)
    {
        var machine = await smFactory.LoadAsync<SubmissionState, SubmissionTrigger>(
            "Submission", cmd.SubmissionId, SubmissionState.Draft, ct);

        await machine.FireAsync(SubmissionTrigger.Submit, new StateMachineContext
        {
            ActorId   = cmd.UserId,
            ActorName = cmd.UserName,
            Reason    = "Broker submitted via portal",
            CorrelationId = cmd.CorrelationId
        }, ct);

        // machine.CurrentState is now SubmissionState.Submitted
    }
}

// In QuoteCommandService — chain pricing + proposal transitions
public async Task HandlePricingCompleteAsync(string quoteId, string actorId, CancellationToken ct)
{
    var machine = await smFactory.LoadAsync<QuoteState, QuoteTrigger>(
        "Quote", quoteId, QuoteState.Draft, ct);

    await machine.FireAsync(QuoteTrigger.PricingComplete, new StateMachineContext
    {
        ActorId   = actorId,
        ActorName = "RatingAdapter",
        Reason    = "Pricing returned from UE Rating Engine"
    }, ct);
}
```

---

## SQL Schema

```sql
CREATE TABLE state_records (
    entity_type     NVARCHAR(100) NOT NULL,
    entity_id       NVARCHAR(64)  NOT NULL,
    current_state   NVARCHAR(100) NOT NULL,
    version         INT NOT NULL DEFAULT 0,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_by      NVARCHAR(200) NULL,
    CONSTRAINT PK_state_records PRIMARY KEY (entity_type, entity_id)
);

-- Index for bulk status queries (e.g., fetch all In-Review submissions)
CREATE INDEX IX_state_records_type_state ON state_records (entity_type, current_state);
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Trigger not permitted from current state | Throw `InvalidTransitionException` immediately — never silently ignore |
| DB version conflict (concurrent update) | Reload state from DB, retry up to `ConcurrencyRetryAttempts` |
| Audit trail write fails | Log error; do NOT block or roll back the state transition — audit is best-effort |
| Event publish fails | Log error; state is already persisted — Service Bus outbox pattern retries delivery |
| Entity not found in DB | Create new `StateRecord` with provided `initialState` — idempotent first-call behavior |
| Stateless.NET throws on configure | Fail fast at startup (DI validation) — invalid graph is a programming error, not a runtime error |

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.StateMachine at path: shared/KSquare.StateMachine/

This library wraps Stateless.NET to provide a persistent, auditable, event-publishing state machine
for the three core entities in the UE Underwriting Workbench: Submission, Quote, and Referral.
Every state transition auto-writes an AuditTrail entry and publishes a StateTransitionedEvent.

Project structure:
  shared/KSquare.StateMachine/
  ├── KSquare.StateMachine.csproj
  ├── Contracts/
  │   ├── IEntityStateMachine.cs
  │   ├── IStateMachineFactory.cs
  │   └── IStateMachineDefinition.cs
  ├── Models/
  │   ├── StateMachineContext.cs
  │   ├── StateRecord.cs
  │   ├── StateTransitionedEvent.cs
  │   └── InvalidTransitionException.cs
  ├── Configuration/
  │   └── StateMachineOptions.cs
  ├── Definitions/
  │   ├── SubmissionStateMachine.cs   ← SubmissionState + SubmissionTrigger enums + definition class
  │   ├── QuoteStateMachine.cs        ← QuoteState + QuoteTrigger enums + definition class
  │   └── ReferralStateMachine.cs     ← ReferralState + ReferralTrigger enums + definition class
  ├── Core/
  │   ├── StatelessEntityStateMachine.cs   ← IEntityStateMachine impl; wraps Stateless StateMachine<,>
  │   └── StatelessStateMachineFactory.cs  ← IStateMachineFactory; loads/persists StateRecord
  ├── Mock/
  │   └── MockStateMachineFactory.cs       ← in-memory state; no DB; no events; for unit tests
  ├── Database/
  │   ├── StateMachineDbContext.cs
  │   └── Migrations/
  └── Extensions/
      └── ServiceCollectionExtensions.cs

StatelessEntityStateMachine.FireAsync implementation:
  - Check machine.CanFire(trigger) → throw InvalidTransitionException if false
  - Record fromState
  - Call machine.Fire(trigger) (Stateless.NET sync — wrap in Task.Run if needed)
  - UPDATE state_records with optimistic concurrency on version column
    - If 0 rows affected: reload, retry up to options.ConcurrencyRetryAttempts, then throw
  - If options.WriteAuditTrail: call IAuditTrailWriter.WriteAsync with Action="StateTransition"
    - Never let audit failure throw — catch and log
  - If options.PublishTransitionEvents: call IEventPublisher.PublishAsync(StateTransitionedEvent)
    - Never let event failure throw — catch and log

StatelessStateMachineFactory.LoadAsync:
  - SELECT state_record for (entityType, entityId)
  - If not found: INSERT new record with current_state = initialState.ToString()
  - Resolve IStateMachineDefinition<TState, TTrigger> from DI
  - Build Stateless StateMachine<TState, TTrigger>, call definition.Configure(builder)
  - Set machine state to loaded/initial state
  - Return new StatelessEntityStateMachine wrapping the built machine

MockStateMachineFactory:
  - Stores state in ConcurrentDictionary<(string entityType, string entityId), string>
  - FireAsync: sets state in dict; does NOT write audit; does NOT publish events
  - Useful for unit tests that want to transition state without infrastructure

ServiceCollectionExtensions.AddKsStateMachine:
  - Register IStateMachineFactory
  - Register StateMachineDbContext
  - Expose .AddStateMachineDefinition<TState, TTrigger, TDefinition>() fluent method
    that registers IStateMachineDefinition<TState, TTrigger> as TDefinition
  - Requires IAuditTrailWriter and IEventPublisher registered (from KSquare.AuditTrail + KSquare.EventBus)

NuGet packages:
  - Stateless 5.x
  - Microsoft.EntityFrameworkCore.SqlServer 8.x

Tests at shared/KSquare.StateMachine.Tests/:
  - SubmissionStateMachine: Submit trigger transitions Draft → Submitted
  - SubmissionStateMachine: FireAsync on terminal Approved state throws InvalidTransitionException
  - SubmissionStateMachine: invalid trigger (e.g., Approve from Draft) throws InvalidTransitionException
  - FireAsync writes AuditTrail entry with correct fromState, toState, actorId
  - FireAsync publishes StateTransitionedEvent with correct EntityType and Trigger
  - LoadAsync: first call creates StateRecord with initialState
  - LoadAsync: subsequent call restores persisted CurrentState (not initialState)
  - QuoteStateMachine: RequestPricing → PricingComplete → GenerateProposal transitions valid
  - ReferralStateMachine: RequestInfo transitions UnderReview → PendingInfo → UnderReview round-trip
  Use xUnit + Moq + FluentAssertions + EF Core InMemory provider.
```
