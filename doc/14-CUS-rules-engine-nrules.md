# Component 14 — Rules Engine (nRules)

**Library**: `KSquare.RulesEngine`  
**Layer**: Intelligence / Domain  
**Default Provider**: nRules  
**Alternate Providers**: Microsoft RulesEngine (JSON-defined rules), Drools via gRPC (future)  
**Language**: C# / .NET 8

---

## Why This Is a Pluggable Component

The UW workbench contains multiple rule-driven decision points that cannot be hard-coded:
- **Intake routing**: should this submission be auto-assigned or queued for manual triage?
- **Referral triggers**: does this risk profile require senior underwriter review?
- **Appetite scoring**: does this risk fall within underwriting appetite?
- **Bind readiness checks**: are all required fields, documents, and approvals present?
- **Clearance rules**: has this risk been seen before? Is there a conflict of interest?

These rules change frequently — new appetite guidelines, new referral thresholds, regulatory
changes — without requiring code deployment. A shared library ensures:
- Rules are defined outside application code (YAML/JSON or C# fluent DSL via nRules)
- All services evaluate the same rules with the same context model
- Rule evaluation results include which rules fired and why (audit trail)
- Rules can be unit-tested in isolation

---

## Interface Contract

```csharp
namespace KSquare.RulesEngine.Contracts;

public interface IRulesEngine
{
    // Evaluate a named rule set against a context object.
    // Returns all rule results (fired + not fired).
    Task<RuleEvaluationResult> EvaluateAsync<TContext>(
        string ruleSetName,
        TContext context,
        CancellationToken ct = default) where TContext : class;

    // Convenience: evaluate and return first matching action string.
    Task<string?> GetFirstMatchedActionAsync<TContext>(
        string ruleSetName,
        TContext context,
        CancellationToken ct = default) where TContext : class;
}

public interface IRuleSetProvider
{
    // Load a named rule set (from embedded resources, blob, or DB).
    Task<RuleSet> GetRuleSetAsync(string ruleSetName, CancellationToken ct = default);
}
```

---

## Models

```csharp
namespace KSquare.RulesEngine.Models;

public record RuleEvaluationResult
{
    public required string RuleSetName { get; init; }
    public required IReadOnlyList<RuleResult> Results { get; init; }
    public required bool AnyFired => Results.Any(r => r.Fired);
    public required IReadOnlyList<string> FiredActions { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}

public record RuleResult
{
    public required string RuleName { get; init; }
    public required bool Fired { get; init; }
    public required string? Action { get; init; }       // e.g. "RouteToManualTriage", "TriggerReferral"
    public string? Reason { get; init; }               // human-readable explanation
    public IDictionary<string, object?> MatchedFacts { get; init; } = new Dictionary<string, object?>();
}

// A rule set definition — can be loaded from YAML or defined in code
public record RuleSet
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<Rule> Rules { get; init; }
}

public record Rule
{
    public required string RuleName { get; init; }
    public required int Priority { get; init; }        // higher = evaluated first
    public string? Description { get; init; }
    public required string Condition { get; init; }    // C# expression string for nRules
    public required string Action { get; init; }       // action string when condition matches
    public string? Reason { get; init; }               // explanation shown in audit
}
```

---

## Predefined Rule Sets

### intake-routing Rules

```yaml
# rules/intake-routing.yml
name: intake-routing
version: "1.0"
rules:
  - rule_name: HighValueAutoRoute
    priority: 100
    description: High TIV submissions need senior UW
    condition: "context.TotalInsuredValue > 10000000"
    action: RouteToSeniorUnderwriter
    reason: Total insured value exceeds $10M threshold

  - rule_name: NewBrokerManualReview
    priority: 90
    condition: "context.BrokerTenureMonths < 6"
    action: RouteToManualTriage
    reason: Broker has less than 6 months relationship history

  - rule_name: HighRiskIndustryReferral
    priority: 80
    condition: "context.NaicsCode.StartsWith(\"238\") || context.NaicsCode.StartsWith(\"484\")"
    action: TriggerReferral
    reason: NAICS code falls in high-risk construction or trucking industry

  - rule_name: IncompleteApplicationManual
    priority: 70
    condition: "context.MissingRequiredFields.Count > 2"
    action: RouteToManualTriage
    reason: More than 2 required fields missing from application

  - rule_name: DefaultAutoAssign
    priority: 1
    condition: "true"
    action: AutoAssign
    reason: Standard submission — auto-assign to available underwriter
```

### referral-triggers Rules

```yaml
# rules/referral-triggers.yml
name: referral-triggers
version: "1.0"
rules:
  - rule_name: LargePriorLoss
    priority: 100
    condition: "context.LargestSingleLoss > 500000"
    action: ReferralRequired
    reason: Single loss exceeds $500K referral threshold

  - rule_name: HighLossRatio
    priority: 90
    condition: "context.FiveYearLossRatio > 0.65"
    action: ReferralRequired
    reason: 5-year loss ratio exceeds 65%

  - rule_name: MultipleLocations
    priority: 80
    condition: "context.NumberOfLocations > 10"
    action: ReferralRequired
    reason: More than 10 locations requires referral review

  - rule_name: OutOfAppetiteNaics
    priority: 100
    condition: "context.OutOfAppetiteNaicsCodes.Contains(context.NaicsCode)"
    action: Decline
    reason: NAICS code is outside current underwriting appetite
```

### bind-readiness Rules

```yaml
# rules/bind-readiness.yml
name: bind-readiness
version: "1.0"
rules:
  - rule_name: QuoteNotApproved
    priority: 100
    condition: "context.QuoteStatus != \"Approved\""
    action: BlockBind
    reason: Quote must be in Approved status before binding

  - rule_name: SignedApplicationMissing
    priority: 90
    condition: "!context.HasSignedApplication"
    action: BlockBind
    reason: Signed application document is required before binding

  - rule_name: PremiumNotAgreed
    priority: 80
    condition: "!context.PremiumAgreedByBroker"
    action: BlockBind
    reason: Broker must confirm premium before binding

  - rule_name: ReadyToBind
    priority: 1
    condition: "true"
    action: AllowBind
    reason: All bind readiness checks passed
```

---

## Rule Context Models

```csharp
// Context for intake-routing rule set
public class IntakeRoutingContext
{
    public decimal TotalInsuredValue { get; init; }
    public int BrokerTenureMonths { get; init; }
    public string NaicsCode { get; init; } = "";
    public IReadOnlyList<string> MissingRequiredFields { get; init; } = [];
    public int NumberOfLocations { get; init; }
    public string? SubmissionSource { get; init; }   // "email", "portal", "api"
}

// Context for referral-triggers rule set
public class ReferralContext
{
    public decimal LargestSingleLoss { get; init; }
    public decimal FiveYearLossRatio { get; init; }
    public int NumberOfLocations { get; init; }
    public string NaicsCode { get; init; } = "";
    public IReadOnlyList<string> OutOfAppetiteNaicsCodes { get; init; } = [];
    public decimal TotalInsuredValue { get; init; }
}

// Context for bind-readiness rule set
public class BindReadinessContext
{
    public string QuoteStatus { get; init; } = "";
    public bool HasSignedApplication { get; init; }
    public bool PremiumAgreedByBroker { get; init; }
    public bool ComplianceCheckPassed { get; init; }
    public bool ReferralApproved { get; init; }
}
```

---

## DI Registration

```csharp
builder.Services.AddKsRulesEngine(options =>
{
    options.RuleSource = RuleSetSource.EmbeddedYaml;  // or BlobStorage
    options.RulesBlobContainerName = "rules";
    options.CacheTtl = TimeSpan.FromMinutes(5);
})
.AddRuleSet("intake-routing")
.AddRuleSet("referral-triggers")
.AddRuleSet("bind-readiness");
```

---

## Usage Examples

```csharp
// Intake routing decision
var routingResult = await rulesEngine.EvaluateAsync("intake-routing", new IntakeRoutingContext
{
    TotalInsuredValue = 15_000_000m,
    BrokerTenureMonths = 24,
    NaicsCode = "5311",
    MissingRequiredFields = [],
    NumberOfLocations = 3
});

var action = routingResult.FiredActions.First();  // "RouteToSeniorUnderwriter"
log.LogInformation("Routing decision: {Action}. Rules fired: {Rules}",
    action,
    string.Join(", ", routingResult.Results.Where(r => r.Fired).Select(r => r.RuleName)));

// Bind readiness check
var bindResult = await rulesEngine.EvaluateAsync("bind-readiness", new BindReadinessContext
{
    QuoteStatus = "Approved",
    HasSignedApplication = true,
    PremiumAgreedByBroker = true,
    ComplianceCheckPassed = true,
    ReferralApproved = true
});

var canBind = bindResult.FiredActions.First() == "AllowBind";

// Write all fired rules to audit trail
foreach (var rule in bindResult.Results.Where(r => r.Fired))
{
    await audit.WriteAsync(new AuditEntry
    {
        ResourceType = "BindDecision",
        ResourceId = quoteId.ToString(),
        Action = $"RuleFired:{rule.RuleName}",
        Actor = systemActor,
        After = JsonSerializer.Serialize(new { rule.Action, rule.Reason })
    });
}
```

---

## Claude Code Build Prompt

```
Build a .NET 8 class library called KSquare.RulesEngine at path: shared/KSquare.RulesEngine/

This library wraps the nRules rules engine to evaluate YAML-defined business rules
against typed context objects. Rules fire actions (strings) that drive routing,
referral, and bind decisions in the UW workbench.

Project structure:
  shared/KSquare.RulesEngine/
  ├── KSquare.RulesEngine.csproj
  ├── Contracts/
  │   ├── IRulesEngine.cs
  │   └── IRuleSetProvider.cs
  ├── Models/
  │   ├── RuleEvaluationResult.cs
  │   ├── RuleResult.cs
  │   ├── RuleSet.cs
  │   └── Rule.cs
  ├── Context/
  │   ├── IntakeRoutingContext.cs
  │   ├── ReferralContext.cs
  │   └── BindReadinessContext.cs
  ├── Configuration/
  │   └── RulesEngineOptions.cs
  ├── Providers/
  │   ├── EmbeddedYamlRuleSetProvider.cs   ← loads rules/*.yml from embedded resources
  │   └── BlobRuleSetProvider.cs           ← loads from Blob Storage with caching
  ├── Resources/
  │   ├── rules/
  │   │   ├── intake-routing.yml
  │   │   ├── referral-triggers.yml
  │   │   └── bind-readiness.yml
  ├── Internal/
  │   └── NRulesEngineAdapter.cs           ← wraps nRules session creation and firing
  └── Extensions/
      └── ServiceCollectionExtensions.cs

NRulesEngineAdapter:
  - Use NRules.Fluent or NRules.RuleModel to define rules
  - For each Rule in RuleSet:
    - Build NRules IRule programmatically from Rule.Condition (expression string)
    - Use NRules RuleCompiler + Session
  - On EvaluateAsync:
    - Create new ISession per evaluation (stateless, thread-safe pattern)
    - Insert context object as a fact
    - Call session.Fire()
    - Collect which rules fired by subscribing to session.Events.RuleFiredEvent
    - Return RuleEvaluationResult with all results
  - Note: nRules condition expressions are compiled LINQ expressions — use
    NRules.Fluent DSL with When().Match<TContext>(ctx => expression) pattern
    rather than eval'ing condition strings at runtime

  Since nRules uses a compiled DSL (not string eval), represent each YAML rule as a
  programmatic rule definition:
  - Generate a named IRule class per rule using the NRules.Fluent attributes or builder API
  - For the YAML rules, create a RuleFactory that produces IRule objects from Rule records
    by using compiled Func<TContext, bool> from the rule's Condition using
    System.Linq.Expressions or a simple expression interpreter

  Simpler alternative approach (recommended for this implementation):
  - Skip nRules for YAML rules; use a simple engine:
    - Deserialize YAML rules into Rule records
    - For each rule, compile the Condition as a Predicate<TContext> using
      System.Linq.Dynamic.Core (DynamicLinq): 
        var pred = DynamicExpressionParser.ParseLambda<TContext, bool>(parsingConfig, false, rule.Condition)
    - Evaluate predicates in priority order; collect fired rules and their actions
  - Add AddRulesEngine using nRules ONLY for the built-in hardcoded rules
    (bind-readiness, referral-triggers, intake-routing)
  - Use DynamicLinq for YAML-loaded rule evaluation

EmbeddedYamlRuleSetProvider:
  - Use YamlDotNet to deserialize rules/*.yml embedded resources
  - Cache in ConcurrentDictionary<string, RuleSet>

ServiceCollectionExtensions.AddKsRulesEngine:
  - Register IRulesEngine as scoped
  - Register IRuleSetProvider (Embedded or Blob based on options)
  - Fluent .AddRuleSet(name) validates that the rule set file exists on startup

NuGet packages:
  - YamlDotNet 13.x
  - System.Linq.Dynamic.Core 1.3.x (for dynamic expression compilation)
  - Microsoft.Extensions.Caching.Memory

Tests at shared/KSquare.RulesEngine.Tests/:
  - HighValueAutoRoute fires when TotalInsuredValue > 10_000_000
  - DefaultAutoAssign fires when no other rule matches
  - ReferralRequired fires when LargestSingleLoss > 500_000
  - Decline fires when NaicsCode is in OutOfAppetiteNaicsCodes
  - BlockBind fires when QuoteStatus != "Approved"
  - AllowBind fires when all bind readiness conditions are true
  - Multiple rules fire in priority order; highest priority action returned first
  - Rule evaluation result includes which rules fired and their Reason text
  Use xUnit + FluentAssertions.
```
