# KSquare Pluggable Component Library — Fix Plan

**Purpose**: Resolve all gaps identified by the post-implementation review, clean the full
system canvas, and establish the minimum API surface that unblocks frontend development.  
**Audience**: TRAE SOLO  
**Date**: 2026-05-10  
**Authority**: All policies in `first-prompt.md` remain in effect without exception.

---

## Pre-Fix Verification — Run This First

Before writing a single line of code, establish the current baseline:

```
python lab/run_canvas.py --phase 1 --no-tag
python lab/run_canvas.py --phase 2 --no-tag
python lab/run_canvas.py --phase 3 --no-tag
python lab/run_canvas.py --phase 4 --no-tag
python lab/run_canvas.py --phase 5 --no-tag
python lab/run_canvas.py --phase 6 --no-tag
python lab/run_canvas.py --phase full --no-tag
```

Canvases 1–6 must pass (they do, based on current reports). Canvas-full must show 51 passed /
18 failed. If the numbers differ from this baseline, stop and investigate before proceeding.

Create a branch for all fix work:

```
git checkout -b fix/gaps-and-api-readiness
```

---

## Ticket Index

| Ticket | Category | Severity | Root Cause |
|---|---|---|---|
| KSFIX-001 | Schema initialization — StateMachine | Critical (production) | `AddKsStateMachine` never applies schema |
| KSFIX-002 | Schema initialization — ProposalOrchestrator | Critical (production) | `AddKsProposalOrchestrator` never applies schema |
| KSFIX-003 | Schema initialization — PolicyAdminAdapter | Critical (production) | `AddKsPolicyAdminAdapter` never applies schema |
| KSFIX-004 | Canvas-full — WireMock stub ordering | Test configuration | OpenAI stub setup resets Graph stub before canvas-2 harness runs |
| KSFIX-005 | Canvas-full — Prefill LLM call counter | Test configuration | Counter captures cross-component calls across the full run |
| KSFIX-006 | Canvas-full — Narrative section regex | Test configuration | WireMock stub section header contains `/` not matched by `[A-Z\s]+` |
| KSFIX-007 | Canvas-full — Triage entity JSON key | Test configuration | Stub writes `extracted_entities` but LLM response schema uses `entities` |
| KSFIX-008 | Lab — Full canvas verification and tagging | Verification | Run clean canvas-full; create `canvas-full-stable` tag |
| KSFIX-009 | API surface — Wire up SubmissionApi | API readiness | `src/UeUw.SubmissionApi` is an empty stub with no endpoints or Swagger |

Execute tickets in this order. Do not begin a ticket until all preceding tickets are DONE.

---

## KSFIX-001: Schema Auto-Initialization for StateMachine

**Status**: TO DO  
**Severity**: Critical — affects production deployments, not just tests

### Root Cause

`ue-uw-backend/shared/KSquare.StateMachine/Extensions/ServiceCollectionExtensions.cs` calls
`services.AddDbContext<StateMachineDbContext>()` but never applies the database schema. The
migration file `Database/Migrations/001_CreateStateTables.sql` exists on disk but nothing
executes it.

In the standalone canvas-5, the C# harness calls `context.Database.EnsureCreated()` in its own
setup code, which is why it passes. In the full canvas, the harnesses share a single SQL Server
instance. The full canvas runs the canvas-5 harness with a fresh connection, and the schema is
not present because no hosting service has applied it.

In a real production deployment (`UeUw.SubmissionApi`), calling `AddKsStateMachine()` and
starting the application would fail on the first state machine operation with
`Invalid object name 'state_records'` unless a DBA manually ran the SQL. This is a production
gap, not a test gap.

The correct pattern is already established in `KSquare.Idempotency`: the SQL schema is applied
inside the provider itself, not delegated to the caller. Apply the same pattern here via an
`IHostedService` that runs `EnsureCreated()` on startup.

### Files to Read Before Implementing

1. `ue-uw-backend/shared/KSquare.StateMachine/Extensions/ServiceCollectionExtensions.cs` — this
   is where the `AddHostedService` call must be added
2. `ue-uw-backend/shared/KSquare.StateMachine/Database/StateMachineDbContext.cs` — this is the
   DbContext the initializer will call
3. `ue-uw-backend/shared/KSquare.StateMachine/Database/Migrations/001_CreateStateTables.sql` —
   verify it is embedded as a resource or accessible; `EnsureCreated` covers the EF Core mapping
   so the SQL file is a fallback reference, not what runs

### What to Build

Create `ue-uw-backend/shared/KSquare.StateMachine/Database/StateMachineSchemaInitializer.cs`:

```csharp
// Applies StateMachineDbContext schema on host startup.
// Runs EnsureCreated so the state_records table exists before any state machine
// operation is attempted. Safe to call on every startup: EnsureCreated is idempotent.
internal sealed class StateMachineSchemaInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public StateMachineSchemaInitializer(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StateMachineDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

In `ServiceCollectionExtensions.cs`, after `services.AddDbContext<StateMachineDbContext>(...)`,
add:

```csharp
services.AddHostedService<StateMachineSchemaInitializer>();
```

### Acceptance Criteria

- [ ] `StateMachineSchemaInitializer` implements `IHostedService` and calls `EnsureCreatedAsync`
- [ ] `AddKsStateMachine()` registers `StateMachineSchemaInitializer` via `AddHostedService`
- [ ] Unit test: a test host that calls `AddKsStateMachine()` with an InMemory connection string
  and starts the host results in the `state_records` table existing (verify via EF Core query
  on `StateMachineDbContext.StateRecords`)
- [ ] All existing `KSquare.StateMachine.Tests` continue to pass with `dotnet test`

### Commit Sequence

```
feat(ksfix-001): add StateMachineSchemaInitializer hosted service
feat(ksfix-001): register StateMachineSchemaInitializer in AddKsStateMachine
test(ksfix-001): verify EnsureCreated runs on host startup
chore(ksfix-001): mark DONE
```

---

## KSFIX-002: Schema Auto-Initialization for ProposalOrchestrator

**Status**: TO DO  
**Severity**: Critical — same pattern, same production risk as KSFIX-001

### Root Cause

`ue-uw-backend/shared/KSquare.ProposalOrchestrator/Extensions/ServiceCollectionExtensions.cs`
calls `services.AddDbContext<ProposalDbContext>()` but never applies the schema. The migration
file `Migrations/001_CreateProposalJobsTable.sql` exists but is never executed. The full canvas
fails with `An error occurred while saving the entity changes` because the `proposal_generation_jobs`
table does not exist at the time the GhostDraftProposalOrchestrator tries to insert a job record.

### Files to Read Before Implementing

1. `ue-uw-backend/shared/KSquare.ProposalOrchestrator/Extensions/ServiceCollectionExtensions.cs`
2. `ue-uw-backend/shared/KSquare.ProposalOrchestrator/Database/ProposalDbContext.cs`

### What to Build

Create `ue-uw-backend/shared/KSquare.ProposalOrchestrator/Database/ProposalSchemaInitializer.cs`
using the identical pattern as `StateMachineSchemaInitializer` in KSFIX-001, substituting
`ProposalDbContext`. Register it in `AddKsProposalOrchestrator()` with `AddHostedService`.

Only register the hosted service when the provider is not `Mock` (the Mock provider has no
database). The condition is already present in `ServiceCollectionExtensions.cs` — add the
`AddHostedService` call inside the non-mock branch, after `AddDbContext`.

### Acceptance Criteria

- [ ] `ProposalSchemaInitializer` implements `IHostedService` and calls `EnsureCreatedAsync` on `ProposalDbContext`
- [ ] `AddKsProposalOrchestrator()` registers it only for the non-Mock provider path
- [ ] Unit test: host startup with InMemory connection creates `proposal_generation_jobs` table
- [ ] All existing `KSquare.ProposalOrchestrator.Tests` continue to pass

### Commit Sequence

```
feat(ksfix-002): add ProposalSchemaInitializer hosted service
feat(ksfix-002): register ProposalSchemaInitializer in AddKsProposalOrchestrator
test(ksfix-002): verify proposal schema created on host startup
chore(ksfix-002): mark DONE
```

---

## KSFIX-003: Schema Auto-Initialization for PolicyAdminAdapter

**Status**: TO DO  
**Severity**: Critical — same pattern, same production risk

### Root Cause

`ue-uw-backend/shared/KSquare.PolicyAdminAdapter/Extensions/ServiceCollectionExtensions.cs`
calls `services.AddDbContext<PolicyAdminDbContext>()` but never applies the schema. Full canvas
fails with `An error occurred while saving the entity changes` because `bind_jobs` table does
not exist. Same structural gap as KSFIX-001 and KSFIX-002.

### Files to Read Before Implementing

1. `ue-uw-backend/shared/KSquare.PolicyAdminAdapter/Extensions/ServiceCollectionExtensions.cs`
2. `ue-uw-backend/shared/KSquare.PolicyAdminAdapter/Database/PolicyAdminDbContext.cs`

### What to Build

Create `ue-uw-backend/shared/KSquare.PolicyAdminAdapter/Database/PolicyAdminSchemaInitializer.cs`
using the same `IHostedService` + `EnsureCreatedAsync` pattern. Register it only in the PCAS
provider branch (not Mock, not Guidewire which has no local DB). The PCAS branch is the block
starting at `services.AddDbContext<PolicyAdminDbContext>()` in `ServiceCollectionExtensions.cs`.

### Acceptance Criteria

- [ ] `PolicyAdminSchemaInitializer` implements `IHostedService` and calls `EnsureCreatedAsync` on `PolicyAdminDbContext`
- [ ] Registered only for the PCAS provider path
- [ ] Unit test: host startup with InMemory connection creates `bind_jobs` table
- [ ] All existing `KSquare.PolicyAdminAdapter.Tests` continue to pass

### Commit Sequence

```
feat(ksfix-003): add PolicyAdminSchemaInitializer hosted service
feat(ksfix-003): register PolicyAdminSchemaInitializer in AddKsPolicyAdminAdapter
test(ksfix-003): verify policy admin schema created on host startup
chore(ksfix-003): mark DONE
```

---

## KSFIX-004: WireMock Stub Ordering Causes Email Ingestion to Return 0 Events

**Status**: TO DO  
**Severity**: Test configuration — no production code change needed

### Root Cause

File: `lab/scenarios/canvas_full_system.py`, function `_wiremock_stub_openai` (approximately
line 474).

This function begins with:

```python
await http.post("/__admin/reset")
```

This call resets ALL WireMock stubs — including the Graph API and SendGrid stubs that were
registered by `wiremock_stub_graph_and_sendgrid` earlier in the scenario. The canvas-2 harness
(`_run_harness("canvas_2_communication_harness/Canvas2.csproj", env=env)`) runs at line 90,
which is BEFORE `_wiremock_stub_openai` is called, so canvas-2 should have the Graph stub.

However, the `_wiremock_stub_openai` function is called at approximately line 462 inside
the `run()` method, and the wiremock_stub_graph_and_sendgrid assertion (`"wiremock_stub_graph_and_sendgrid"`) appears in `CANVAS_2_ASSERTIONS` at line 827. The full canvas
re-emits canvas-2 assertions from the harness results, and the harness runs BEFORE the OpenAI
stub setup. So the Graph stub should be present when canvas-2 runs.

The actual failure is different: look at the canvas-2 harness's result for
`email_ingestion_publishes_one_event_and_stores_two_attachments` — it returns `false`. This
means the Graph stub is present but the email ingestion is failing inside the C# harness itself.

Investigate: Open `lab/scenarios/canvas_2_communication_harness/Canvas2.csproj` and its test
code. Find where it sets up the WireMock URL for Microsoft Graph. Verify the WireMock URL
environment variable passed via `env=env` in `_run_harness` matches what the C# harness expects.

The most likely cause: the C# harness reads a WireMock base URL from an environment variable
(e.g., `WIREMOCK_BASE_URL`). In standalone canvas-2, this is set correctly. In the full canvas,
the `env` dictionary may have a different key or value for the WireMock URL. Compare the
environment variable setup in `canvas_full_system.py`'s `run()` method against the standalone
`canvas_2_communication.py` scenario.

### What to Do

1. Read `lab/scenarios/canvas_2_communication.py` to see how it sets up the WireMock URL for
   the C# harness and what environment variable name it uses.
2. Read `lab/scenarios/canvas_2_communication_harness/` to see what environment variable the
   C# harness reads.
3. Read `lab/scenarios/canvas_full_system.py` around lines 70–100 to see what `env` dict is
   passed to `_run_harness` for canvas-2.
4. Identify the mismatch and fix it. The fix is a one-line correction in the `env` dict
   construction, not a structural change.

### Acceptance Criteria

- [ ] `email_ingestion_publishes_one_event_and_stores_two_attachments` passes in canvas-full run
- [ ] Canvas-2 standalone still passes with `python lab/run_canvas.py --phase 2 --no-tag`
- [ ] No production C# code is changed

### Commit Sequence

```
fix(ksfix-004): correct wiremock url env var for canvas-2 harness in full canvas
chore(ksfix-004): mark DONE
```

---

## KSFIX-005: WireMock Request Counter Captures Cross-Component Calls

**Status**: TO DO  
**Severity**: Test configuration — no production code change needed

### Root Cause

File: `lab/scenarios/canvas_full_system.py`, line 181:

```python
count_prefill_calls = await _wiremock_count_requests(wiremock, "/openai/deployments/gpt-4o/chat/completions")
```

This counts ALL requests to the `gpt-4o` deployment endpoint since WireMock started — not only
the prefill requests. By the time prefill runs, the agent orchestrator (canvas-4 harness) has
already made multiple calls to the same `gpt-4o` endpoint for conversation turns, online
evaluation scoring, and RAG. These are counted together with the 2 prefill calls, producing 6
total.

The fix is to clear the WireMock request log immediately before calling the prefill adapter,
then count only the requests made after that reset. Use the WireMock admin API endpoint
`DELETE /__admin/requests` to clear the log without affecting registered stubs.

### What to Change

In `canvas_full_system.py`, find the block where `prefill_out` is computed (around line 176).
Immediately before the `_python_call(...)` for prefill, add:

```python
async with httpx.AsyncClient(base_url=wiremock, timeout=5.0) as http:
    await http.delete("/__admin/requests")
```

Then the count at line 181 will reflect only the prefill requests.

Important: Use `DELETE /__admin/requests` (clears the journal) not `POST /__admin/reset`
(which also deletes registered stubs). The stubs must remain.

### Acceptance Criteria

- [ ] `prefill_batching_20_fields_to_2_llm_calls` passes: `llm_calls=2, total_fields_requested=20`
- [ ] `component_23_intelligent_prefill_engine` passes
- [ ] Canvas-6 standalone still passes
- [ ] No production code is changed

### Commit Sequence

```
fix(ksfix-005): clear wiremock request journal before counting prefill calls
chore(ksfix-005): mark DONE
```

---

## KSFIX-006: Narrative Section Header Contains '/' Not Matched by Regex

**Status**: TO DO  
**Severity**: Test configuration — no production code change needed

### Root Cause

File: `lab/scenarios/canvas_full_system.py`, function `_wiremock_stub_openai`, approximately
line 510:

```python
referral_text = (
    "1. EXECUTIVE SUMMARY:\n"
    ...
    "4. ROUTING / NEXT STEPS:\n"
    "Route to SeniorUW for review.\n"
)
```

The production `_parse_sections` regex in
`ue-uw-backend/shared-python/ksquare-document-narrative/providers/azure_openai_narrative.py`
is:

```python
re.split(r"\n\d+\.\s+([A-Z\s]+):\n", "\n" + text)
```

The character class `[A-Z\s]` matches uppercase letters and whitespace only. The `/` in
`ROUTING / NEXT STEPS` is not in this class, so section 4 fails to parse. The result is only
3 sections (EXECUTIVE SUMMARY, KEY RISK FACTORS, LOSS HISTORY), causing the assertion
`len(referral_sections.keys()) >= 4` to fail.

The production regex is correct — it matches the spec-defined section header format. The stub
text is wrong.

### What to Change

In `canvas_full_system.py`, replace section 4 in `referral_text`:

```python
# Before
"4. ROUTING / NEXT STEPS:\n"
"Route to SeniorUW for review.\n"

# After
"4. RECOMMENDED ACTION:\n"
"Route to SeniorUW for review.\n"
```

Verify the full `referral_text` now produces exactly 4 sections when run through the regex.
All four headers must use only `[A-Z\s]` characters (uppercase letters and spaces, no symbols).

### Acceptance Criteria

- [ ] `referral_narrative_sections_ge_4` passes: `len(section_keys) >= 4`
- [ ] `component_24_document_narrative_engine` passes
- [ ] Canvas-6 standalone still passes
- [ ] No production code is changed

### Commit Sequence

```
fix(ksfix-006): fix referral memo stub section 4 header to match parse regex
chore(ksfix-006): mark DONE
```

---

## KSFIX-007: Triage Entity Stub Uses Wrong JSON Key

**Status**: TO DO  
**Severity**: Test configuration — no production code change needed

### Root Cause

File: `lab/scenarios/canvas_full_system.py`, function `_wiremock_stub_openai`, the triage stub
response body:

```python
"content": json.dumps({
    ...
    "extracted_entities": triage_expected["entities"],   # <-- wrong key
})
```

The production code in
`ue-uw-backend/shared-python/ksquare-ai-email-triage/providers/azure_openai_triage.py`
reads:

```python
for raw in data.get("entities", []) or []:
```

The LLM response schema (defined in `prompts.py`) uses `"entities"` as the key. The stub
writes `"extracted_entities"`. The production code reads `"entities"` and finds nothing, so
`extracted_entities=[]` in the result. Intent classification passes because `"intent"` is
correctly keyed; entity extraction silently returns empty because the key is wrong.

### What to Change

In `canvas_full_system.py`, find the triage stub response construction and replace:

```python
"extracted_entities": triage_expected["entities"],
```

with:

```python
"entities": triage_expected["entities"],
```

### Acceptance Criteria

- [ ] `triage_intent_and_entities` passes: both intent and entities are non-empty
- [ ] `component_22_ai_email_triage` passes
- [ ] Canvas-6 standalone still passes
- [ ] No production code is changed

### Commit Sequence

```
fix(ksfix-007): correct triage stub json key from extracted_entities to entities
chore(ksfix-007): mark DONE
```

---

## KSFIX-008: Re-Run All Canvases and Create canvas-full-stable Tag

**Status**: TO DO  
**Severity**: Verification — this is the correctness gate for all preceding fixes

### What to Do

Run every canvas in order, verifying 0 failures at each stage before proceeding:

```bash
python lab/run_canvas.py --phase 1 --no-tag
python lab/run_canvas.py --phase 2 --no-tag
python lab/run_canvas.py --phase 3 --no-tag
python lab/run_canvas.py --phase 4 --no-tag
python lab/run_canvas.py --phase 5 --no-tag
python lab/run_canvas.py --phase 6 --no-tag
```

All six must show `PASS` with 0 failed assertions. If any individual canvas regresses (it
previously passed and now fails), stop. The fix for KSFIX-001 through KSFIX-007 may have
inadvertently broken something. Investigate and fix before proceeding.

Only after all six canvases pass, run:

```bash
python lab/run_canvas.py --phase full
```

The full canvas must show `PASS` with 0 failed assertions and all 69 assertions passed.

The `snapshot.py` tool will automatically create the `canvas-full-stable` tag and update
`lab/BASELINES.md`. Verify the tag was pushed to origin:

```bash
git tag -l | grep canvas-full
```

### Acceptance Criteria

- [ ] Canvases 1 through 6 all pass with 0 regressions
- [ ] Canvas-full passes: 69/69 assertions pass, `overall_status = "PASS"`
- [ ] `canvas-full-stable` tag exists on origin
- [ ] `lab/BASELINES.md` contains a row for `canvas-full-stable`

### Commit Sequence

```
chore(ksfix-008): update BASELINES.md after canvas-full-stable tag
chore(ksfix-008): mark DONE
```

---

## KSFIX-009: Wire Up SubmissionApi — Minimum Viable API Surface

**Status**: TO DO  
**Severity**: API readiness — required before frontend integration can begin

### Context

`ue-uw-backend/src/UeUw.SubmissionApi/` is an empty stub created in KSPL-000. It has no
endpoints, no DI registrations, and no OpenAPI spec. The frontend team has no surface to call.

The minimum viable surface that unblocks frontend integration is:
1. DI registration of all components the SubmissionApi owns
2. Five core endpoints covering the intake-to-risk-analysis workflow
3. OpenAPI/Swagger UI available at `/swagger`
4. A health check endpoint at `/health`

### Components to Register in SubmissionApi

Read `README.md` (the monorepo structure section) to confirm which components belong to
`UeUw.SubmissionApi`. Based on the spec, it owns:

- `KSquare.EventBus` (InMemory provider for local dev; Azure Service Bus for prod)
- `KSquare.AuditTrail` (SqlServer provider)
- `KSquare.RiskAnalysis`
- `KSquare.StateMachine` + `SubmissionStateMachineDefinition`
- `KSquare.Correlation` (middleware)
- `KSquare.PiiRedaction`
- `KSquare.BlobStorage` (LocalFileSystem for local dev)
- `KSquare.DocumentExtraction` (HTTP client pointing to the Azure Function URL)
- `KSquare.ExtractionMapper`
- `KSquare.IntelligentPrefill` (HTTP client pointing to the Azure Function URL)
- `KSquare.DocumentNarrative` (HTTP client pointing to the Azure Function URL)
- `KSquare.RulesEngine`

### Five Endpoints to Implement

#### POST /submissions
Create a new submission draft from an email triage result or manual entry.

Request body:
```json
{
  "institutionName": "string",
  "state": "string",
  "effectiveDate": "string (ISO 8601)",
  "coverageLines": [{ "product": "string", "limitAmount": 0 }],
  "correlationId": "string (optional)"
}
```

Response `201 Created`:
```json
{
  "submissionId": "string",
  "status": "Draft",
  "createdAt": "string (ISO 8601)"
}
```

#### GET /submissions/{submissionId}
Return the current state of a submission including its FSM status and risk analysis if complete.

Response `200 OK`: full `SubmissionDetail` object (define the shape based on the data available
from `StateMachine` state + `RiskAnalysis` result).

#### POST /submissions/{submissionId}/documents
Upload a document for extraction and classification.

Request: `multipart/form-data` with one file field.

Workflow:
1. Upload file bytes to `IBlobStorageConnector`
2. Call `IDocumentExtractionAdapter`
3. Call `IExtractionMapper`
4. Call `IIntelligentPrefillAdapter` for any unmapped fields
5. Return the merged mapping result

Response `202 Accepted` with a `documentId`.

#### GET /submissions/{submissionId}/risk-analysis
Return the `RiskAnalysisResult` for a submission that has completed document processing.

Response `200 OK`: `RiskAnalysisResult` serialized as JSON.

#### POST /submissions/{submissionId}/transitions
Trigger a state machine transition on the submission FSM.

Request body:
```json
{ "trigger": "Submit" }
```

Valid triggers: `Submit`, `StartReview`, `Refer`, `Approve`, `Decline`, `Withdraw`.

Response `200 OK`: new state and the `StateTransitionedEvent` payload.
Response `409 Conflict` if the transition is invalid for the current state.
Response `409 Conflict` with a specific error code if a `ConcurrencyException` is thrown.

### OpenAPI and Health Check

- Add `Swashbuckle.AspNetCore` to the `.csproj`
- Register Swagger in `Program.cs`:
  ```csharp
  builder.Services.AddEndpointsApiExplorer();
  builder.Services.AddSwaggerGen();
  app.UseSwagger();
  app.UseSwaggerUI();
  ```
- Add a health check at `GET /health` that returns `{ "status": "healthy", "timestamp": "..." }`

### Data Synthesizer

Create `ue-uw-backend/src/UeUw.SubmissionApi.Tests/Synthesizers/SubmissionApiSynthesizer.cs`
using `Bogus` to generate valid `CreateSubmissionRequest` instances and document byte payloads.

### Acceptance Criteria

- [ ] `UeUw.SubmissionApi` starts up without exception when all components are registered with
  their local-dev providers (InMemory EventBus, LocalFileSystem Blob, InMemory AuditTrail)
- [ ] All five endpoints return the correct HTTP status codes for the happy path
- [ ] `POST /submissions/{id}/transitions` returns `409` for an invalid transition (not `500`)
- [ ] `GET /swagger` returns HTTP 200 and renders the OpenAPI UI
- [ ] `GET /health` returns `200` with `{ "status": "healthy" }`
- [ ] All endpoints include `X-Correlation-Id` in the response header (via `CorrelationMiddleware`)
- [ ] `dotnet test` passes for `UeUw.SubmissionApi.Tests`

### Commit Sequence

```
feat(ksfix-009): register all components in SubmissionApi Program.cs
feat(ksfix-009): implement POST /submissions endpoint
feat(ksfix-009): implement GET /submissions/{id} endpoint
feat(ksfix-009): implement POST /submissions/{id}/documents endpoint
feat(ksfix-009): implement GET /submissions/{id}/risk-analysis endpoint
feat(ksfix-009): implement POST /submissions/{id}/transitions endpoint
feat(ksfix-009): add swagger and health check
test(ksfix-009): add submission api integration tests
chore(ksfix-009): mark DONE
```

---

## Completion Criteria

This fix plan is complete when ALL of the following are true:

1. `git tag -l | grep canvas-full` returns `canvas-full-stable`
2. `cat lab/reports/canvas-full-report.json | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['overall_status'], d['passed_assertions'], 'passed', d['failed_assertions'], 'failed')"` prints `PASS 69 passed 0 failed`
3. `dotnet test ue-uw-backend/ue-uw-backend.sln -c Release` passes with 0 failures
4. `curl http://localhost:5000/health` returns `200` with `{"status":"healthy",...}` when `UeUw.SubmissionApi` is running locally
5. `curl http://localhost:5000/swagger` returns `200`
6. The fix branch is merged to `main` and pushed

Do not mark this plan complete until all five checks pass.

---

*The same scientific protocol that governed the initial implementation governs these fixes.
One fix per commit. Push after every commit. No production code is changed for a test-only
failure. No test is changed when the production code is the bug. Understand the root cause
before writing a single line.*
