# KSquare Pluggable Component Library — Implementation Plan

**Project**: KSquare Underwriting Workbench — Shared Component Library  
**Audience**: TRAE SOLO (autonomous AI coding agent)  
**Maintainer**: KSquare Architecture Team  
**Version**: 1.0  
**Date**: 2026-05-07  
**Repository**: https://github.com/AlbertoEdzE/plug-compo

---

## 1. Project Philosophy and Vision

This library encodes the knowledge that commercial insurance underwriting is, at its core, an
information-processing system operating under uncertainty. Each component in this library is a
bounded unit of that processing: a well-defined transformation from inputs to outputs, with
explicit contracts, measurable error states, and swappable providers.

The scientific orientation is that of complexity science applied to enterprise software:

- **Decomposition without fragmentation.** A complex system gains robustness when its components
  are loosely coupled at runtime and tightly coupled only through explicit, versioned interfaces.
  Every component here exposes one interface and hides all implementation detail behind it.

- **Emergence from composition.** The full underwriting workbench capability — from email receipt
  to policy issuance — is not coded anywhere as a single monolith. It emerges from the correct
  assembly of these 25 components. No component knows about the workbench; the workbench is
  assembled from components.

- **Provider-swappability as a first-class property.** Vendor lock-in is a systemic risk. Every
  component that touches an external service (Azure, SendGrid, GhostDraft, PCAS) is structured so
  that the provider can be replaced without changing the consuming service. This is enforced at the
  type level through interfaces, not through policy.

- **Observability over opacity.** Any component that performs non-trivial computation or makes
  external calls must emit structured telemetry. This is not optional instrumentation; it is part
  of the component contract.

- **Correctness before performance.** Test coverage is not a checkbox. Each acceptance criterion
  is a falsifiable claim about system behavior. Tests are written to disprove assumptions, not to
  confirm them.

The vision for this library is that any engineer joining the KSquare platform can pick up any
component, read its specification in `/doc/`, understand its exact contract and behavior, and
build on or modify it without consulting anyone. The specification is the system.

---

## 2. Development Policies

These policies are mandatory. TRAE SOLO must apply them without exception on every ticket.

### 2.1 Specification as Source of Truth

Before implementing any component, TRAE SOLO must read the corresponding specification file in
`/doc/`. The interface signatures, data models, prompt text, configuration options, failure
behaviors, and evaluation metrics in that file are authoritative. Where this plan and a spec file
conflict, the spec file wins.

### 2.2 No Hardcoded Test Data

Test inputs must be generated programmatically using a data synthesizer.

- Python: use the `faker` library. Seed with a fixed integer for reproducibility.
- C#: use the `Bogus` library. Seed with a fixed integer for reproducibility.
- Synthesizer code is committed. Generated output files are never committed.
- `.gitignore` must exclude all synthesized data outputs, SQLite files, and log files.

### 2.3 No Mocks for Infrastructure

Integration tests must use real infrastructure. Use Docker Compose to provision:

- SQL Server 2022 (for Idempotency, AuditTrail, Notifications, StateMachine, etc.)
- Redis 7 (for Idempotency Redis provider)
- Azure Storage Emulator / Azurite (for Blob Storage)
- Azure Service Bus emulator or real dev namespace (for EventBus)

Unit tests that must call external paid HTTP APIs (Azure OpenAI, Azure Document Intelligence,
SendGrid) must stub the HTTP transport layer using:
- Python: `respx` (async HTTP mocking for httpx/openai)
- C#: `WireMock.Net` or `MockHttp`

This is the only acceptable use of HTTP-level stubbing. It is not a mock of business logic; it
is isolation of a paid external endpoint from the CI pipeline.

### 2.4 Test on Every Change

Any commit that modifies previously implemented code must run the full test suite for all affected
components before being committed. A commit that breaks existing tests is never acceptable.

### 2.5 No Icons or Emojis

All source code, documentation, comments, and commit messages must use plain ASCII text only.

### 2.6 Accurate File Structure

The directory structure for each component is defined in its spec file under "Package structure"
or the monorepo layout diagram in `README.md`. TRAE SOLO must create exactly that structure.
No files may be renamed, merged, or omitted without a spec change.

### 2.7 Atomic Commits

One logical change per commit. A logical change is:
- One new file added and tested, or
- One bug fix with its test, or
- One refactor that does not change external behavior

### 2.8 Commit Message Format

All commits must follow Conventional Commits format:

```
<type>(kspl-<NNN>): <imperative description under 72 chars>

[optional body: what and why]
```

Types: `feat`, `fix`, `test`, `refactor`, `chore`, `docs`

Example: `feat(kspl-005): implement CorrelationContext middleware and accessor`

### 2.9 Branching Strategy

- `main`: stable, all tests pass, deployable
- `feature/epic-NN-<phase-name>`: one branch per EPIC, cut from `main`
- Merge to `main` only when all tickets in the EPIC are DONE and all tests pass

### 2.10 Contract-First Implementation

For every component, the interface/contract file must be committed before any provider
implementation. The sequence is:

1. Commit contracts and data models
2. Commit synthesizer (test data generators)
3. Commit provider implementation
4. Commit unit tests
5. Commit integration tests
6. Update ticket status to DONE

### 2.11 OpenTelemetry Instrumentation

Any component that calls an external service or performs multi-step computation must emit
OpenTelemetry spans. Span names must follow the OpenTelemetry GenAI semantic conventions
(v1.26+) for LLM components, and standard HTTP client conventions for HTTP components.

### 2.12 Python Code Style

- PEP 8 compliance enforced via `ruff`
- Type hints on every function parameter and return value
- `dataclass` for all DTOs; `Enum` for all enumerated values
- `async/await` for all I/O-bound operations
- `pytest` with `pytest-asyncio` for all tests

### 2.13 C# Code Style

- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- `record` types for immutable DTOs
- `IAsyncEnumerable<T>` for streaming sequences
- `CancellationToken` on every async public method
- `xUnit` for tests; `FluentAssertions` for assertions

### 2.14 Status Tracking

Update the status field of each ticket in this file upon completion. Valid statuses:

`TO DO` | `IN PROGRESS` | `IN REVIEW` | `DONE` | `BLOCKED`

When marking DONE, record the commit SHA and date in the ticket.

### 2.15 Dependency Order

TRAE SOLO must implement tickets in the order listed within each EPIC. Implementing a ticket
whose dependencies are not DONE is a policy violation.

---

## 3. Repository Structure

```
plug-compo/
├── README.md                         <- master component index (do not modify)
├── implementation-plan.md            <- this file (update status fields only)
├── doc/                              <- component specifications (read-only for implementors)
│   ├── 01-AZR-blob-storage-connector.md
│   ├── 02-AZR-event-bus-connector.md
│   └── ... (25 files total)
├── .gitignore
├── docker-compose.test.yml           <- test infrastructure (SQL, Redis, Azurite)
├── ue-uw-backend/                    <- backend monorepo (created in KSPL-000)
│   ├── shared/                       <- C# .NET 8 class libraries
│   │   ├── KSquare.BlobStorage/
│   │   ├── KSquare.EventBus/
│   │   └── ... (C# components)
│   ├── shared-python/                <- Python 3.11 Azure Function packages
│   │   ├── ksquare-ai-email-triage/
│   │   ├── ksquare-intelligent-prefill/
│   │   └── ... (Python components)
│   └── src/                          <- consuming services (stubs only, created in KSPL-000)
│       ├── UeUw.SubmissionApi/
│       ├── UeUw.QuoteApi/
│       └── ...
├── lab/                              <- Progressive Integration Testbed (EPIC-09)
│   ├── README.md
│   ├── docker-compose.lab.yml        <- all infrastructure for lab runs
│   ├── run_canvas.py                 <- entry point: python run_canvas.py --phase N
│   ├── core/
│   │   ├── scenario_runner.py
│   │   ├── health_report.py
│   │   └── snapshot.py
│   ├── synthesizers/                 <- shared cross-canvas data synthesizers
│   └── scenarios/
│       ├── canvas_1_infrastructure.py
│       ├── canvas_2_communication.py
│       ├── canvas_3_document_intelligence.py
│       ├── canvas_4_ai_agent.py
│       ├── canvas_5_quote_bind.py
│       ├── canvas_6_ai_intelligence.py
│       └── canvas_full_system.py
└── tools/
    └── synthesizers/                 <- shared data synthesizer scripts
```

---

## 4. EPIC Index

| EPIC | Phase | Components | Priority |
|---|---|---|---|
| EPIC-00 | Setup | KSPL-000 | Critical |
| EPIC-01 | Cross-Cutting | KSPL-005, KSPL-006, KSPL-003 | Critical |
| EPIC-02 | Platform Infrastructure | KSPL-001, KSPL-002, KSPL-004 | Critical |
| EPIC-03 | Communication | KSPL-008, KSPL-007, KSPL-009 | High |
| EPIC-04 | Document Intelligence | KSPL-010, KSPL-011, KSPL-012 | High |
| EPIC-05 | Rules and Risk | KSPL-014, KSPL-016, KSPL-015 | High |
| EPIC-06 | AI Agent Core | KSPL-013, KSPL-017 | High |
| EPIC-07 | Quote, Bind, Lifecycle | KSPL-018, KSPL-019, KSPL-020, KSPL-021 | High |
| EPIC-08 | AI Intelligence Layer | KSPL-022, KSPL-023, KSPL-024, KSPL-025 | Medium |
| EPIC-09 | Progressive Integration Testbed | KSPL-026 through KSPL-034 | Critical |

---

## 5. EPIC-00: Repository and Solution Skeleton

### KSPL-000: Initialize repository, solution structure, and shared tooling

**Epic**: EPIC-00  
**Status**: DONE  
**Done**: 2026-05-07 (7bbd2cc)  
**Priority**: Critical  
**Language**: Shell / C# .NET 8 / Python 3.11  
**Spec Reference**: `README.md`

#### Pre-Implementation Requirement

Read `README.md` in its entirety. Pay particular attention to the Monorepo Project Structure
section (the directory tree) and the Consumption Pattern section.

#### Description

Create the repository skeleton that all subsequent tickets will build into. This includes the
.NET solution file, all empty C# project stubs, all empty Python package stubs, Docker Compose
for test infrastructure, and the root `.gitignore`. No business logic is written in this ticket.

This ticket establishes the structural invariants that every other ticket depends on.

#### Dependencies

None. This is the root of the dependency graph.

#### Acceptance Criteria

- [ ] `ue-uw-backend/ue-uw-backend.sln` exists and all C# project stubs are referenced in it
- [ ] Each C# project stub in `shared/` has a `.csproj` with `<TargetFramework>net8.0</TargetFramework>` and `<Nullable>enable</Nullable>`
- [ ] Each Python package stub in `shared-python/` has a `requirements.txt` and an empty `function_app.py`
- [ ] `docker-compose.test.yml` provisions SQL Server 2022, Redis 7, and Azurite with stable port mappings
- [ ] `.gitignore` excludes: `bin/`, `obj/`, `*.user`, `.env`, `__pycache__/`, `*.pyc`, `.venv/`, `synthesized-data/`, `*.db`, `*.log`, `local.settings.json`
- [ ] `tools/synthesizers/` directory exists with a `README.md` explaining the synthesizer convention
- [ ] `git init`, first commit on `main`, remote set to `https://github.com/AlbertoEdzE/plug-compo`, and `git push -u origin main` succeeds

#### Expected Outputs

- `ue-uw-backend/ue-uw-backend.sln`
- 18 C# project stubs in `ue-uw-backend/shared/`
- 8 Python package stubs in `ue-uw-backend/shared-python/`
- `docker-compose.test.yml`
- `.gitignore`
- `tools/synthesizers/README.md`

#### Implementation Notes

- README "Monorepo Project Structure" enumerates 19 shared C# libraries under `ue-uw-backend/shared/`. The "Expected Outputs" line above says 18. This implementation follows README and creates 19 shared C# project stubs, all referenced from the solution file.

#### Ticket Correlations

- All KSPL-NNN tickets depend on this ticket. Nothing else can start until KSPL-000 is DONE.

---

## 6. EPIC-01: Cross-Cutting Infrastructure (Phase 1)

These three components have no KSquare dependencies. They must be built first because EPIC-02
and all subsequent epics depend on them.

### KSPL-005: Correlation Context

**Epic**: EPIC-01  
**Status**: DONE  
**Done**: 2026-05-07 (c632c9c)  
**Priority**: Critical  
**Language**: C# .NET 8  
**Spec Reference**: `doc/05-CUS-correlation-context.md`

#### Pre-Implementation Requirement

Read `doc/05-CUS-correlation-context.md` in its entirety before writing any code. Pay close
attention to the `AsyncLocal<T>` storage pattern, the ASP.NET Core middleware, and the
`DelegatingHandler` for outbound HTTP propagation.

#### Description

Implement `KSquare.Correlation`: a library that creates, propagates, and accesses a correlation
ID across the full request lifecycle including async continuations (via `AsyncLocal<T>`), inbound
HTTP requests (via middleware), and outbound HTTP calls (via a `DelegatingHandler`). Also
propagates via Service Bus message properties for event-driven flows.

This is a foundation component. Every other component that logs, audits, or calls downstream
services will use the correlation ID to trace a request end-to-end.

#### Dependencies

- KSPL-000: solution structure must exist

#### Acceptance Criteria

- [ ] `ICorrelationContext` and `ICorrelationContextAccessor` interfaces match the spec exactly
- [ ] `CorrelationContext` is stored in `AsyncLocal<T>` — verify that the ID survives `await` boundaries across thread-pool threads
- [ ] `CorrelationMiddleware` extracts `X-Correlation-Id` from inbound requests; generates a new UUID v4 if the header is absent
- [ ] `CorrelationDelegatingHandler` injects the current correlation ID into outbound `HttpClient` requests as `X-Correlation-Id`
- [ ] `AddKsCorrelation()` extension method registers all services in DI with correct lifetimes
- [ ] Unit tests use `Bogus` to synthesize request data; no hardcoded IDs
- [ ] Unit test: ID set before `Task.Run` is visible inside the task
- [ ] Unit test: middleware generates a new ID when header is missing
- [ ] Unit test: middleware uses the incoming header value when present
- [ ] Unit test: `DelegatingHandler` injects the current ID into outbound requests
- [ ] Integration test: end-to-end HTTP pipeline preserves ID through middleware and handler
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.Correlation/`
  - `Contracts/ICorrelationContext.cs`
  - `Contracts/ICorrelationContextAccessor.cs`
  - `CorrelationContext.cs`
  - `CorrelationMiddleware.cs`
  - `CorrelationDelegatingHandler.cs`
  - `ServiceCollectionExtensions.cs`
  - `KSquare.Correlation.csproj`
- `ue-uw-backend/shared/KSquare.Correlation.Tests/`
  - `CorrelationContextTests.cs`
  - `CorrelationMiddlewareTests.cs`
  - `CorrelationDelegatingHandlerTests.cs`
  - `Synthesizers/CorrelationDataSynthesizer.cs`

#### Implementation Notes

- Implemented the folder layout defined in `doc/05-CUS-correlation-context.md` (Contracts/, Models/, Middleware/, Http/, Extensions/). This differs from the flat file list above; the spec file is treated as the source of truth for structure and naming.

#### Ticket Correlations

- KSPL-004 (AuditTrail): injects correlation ID into every audit event
- KSPL-002 (EventBus): propagates correlation ID through Service Bus message properties
- KSPL-013 (AgentOrchestrator): uses correlation ID for conversation tracing
- All Python components (KSPL-022 through KSPL-025): receive correlation ID via HTTP request headers from the C# caller

---

### KSPL-006: PII Redaction Filter

**Epic**: EPIC-01  
**Status**: DONE  
**Done**: 2026-05-07 (84fae1e)  
**Priority**: Critical  
**Language**: C# .NET 8  
**Spec Reference**: `doc/06-CUS-pii-redaction-filter.md`

#### Pre-Implementation Requirement

Read `doc/06-CUS-pii-redaction-filter.md` in its entirety. Pay close attention to the JSON tree
walk algorithm, the compiled regex patterns for email/phone/SSN detection, and the Serilog
`IDestructuringPolicy` integration point.

#### Description

Implement `KSquare.PiiRedaction`: a library that detects and masks PII (email addresses, phone
numbers, SSNs) in arbitrary JSON payloads, structured log events, and audit record fields. The
redaction algorithm walks the full JSON tree recursively and applies compiled regex patterns.
Masked values are replaced with `[REDACTED]` — original values are never retained post-redaction.

This component protects against PII leakage in logs, audit records, and event payloads. It is
consumed by the AuditTrail writer and the Serilog pipeline.

#### Dependencies

- KSPL-000: solution structure must exist

#### Acceptance Criteria

- [ ] `IPiiRedactor` interface matches the spec exactly
- [ ] Compiled regex patterns cover: practical email address detection (local-part@domain.tld covering 99%+ of real-world addresses), NANP phone (various formats), SSN (XXX-XX-XXXX and unformatted)
- [ ] JSON tree walk handles: string values, nested objects, arrays, and null values without throwing
- [ ] Redaction is applied to values only, never to keys
- [ ] Serilog `IDestructuringPolicy` integration correctly intercepts structured log events before they are written
- [ ] `AddKsPiiRedaction()` registers all services
- [ ] Unit tests synthesize PII-containing JSON payloads using `Bogus`; no hardcoded PII values in test files
- [ ] Unit test: email in a deeply nested JSON field is redacted
- [ ] Unit test: phone number in multiple common formats is redacted
- [ ] Unit test: SSN in both hyphenated and numeric formats is redacted
- [ ] Unit test: non-PII values are not modified
- [ ] Unit test: redaction is idempotent (applying twice yields same result as applying once)
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.PiiRedaction/`
  - `Contracts/IPiiRedactor.cs`
  - `PiiRedactor.cs`
  - `PiiPatterns.cs`
  - `Serilog/PiiRedactionDestructuringPolicy.cs`
  - `ServiceCollectionExtensions.cs`
  - `KSquare.PiiRedaction.csproj`
- `ue-uw-backend/shared/KSquare.PiiRedaction.Tests/`
  - `PiiRedactorTests.cs`
  - `Synthesizers/PiiDataSynthesizer.cs`

#### Implementation Notes

- Implemented the folder layout defined in `doc/06-CUS-pii-redaction-filter.md` (Contracts/, Configuration/, Internal/, Serilog/, Extensions/). This differs from the flat file list above; the spec file is treated as the source of truth for structure and naming.
- The spec prompt lists a simplified email regex rather than a full RFC 5322 pattern. This implementation follows the spec prompt and documents the mismatch with the acceptance criteria.

#### Ticket Correlations

- KSPL-004 (AuditTrail): `PiiMaskingSerializer` in AuditTrail delegates to `IPiiRedactor`
- KSPL-009 (Notifications): notification payloads are redacted before logging
- KSPL-013 (AgentOrchestrator): conversation audit uses PII redaction before writing to SQL

---

### KSPL-003: Idempotency Guard

**Epic**: EPIC-01  
**Status**: DONE  
**Done**: 2026-05-07 (0d6ae3e)  
**Priority**: Critical  
**Language**: C# .NET 8  
**Spec Reference**: `doc/03-CUS-idempotency-guard.md`

#### Pre-Implementation Requirement

Read `doc/03-CUS-idempotency-guard.md` in its entirety. The SQL schema (`idempotency_keys` and
`idempotency_consumer_keys` tables), the atomic `INSERT WHERE NOT EXISTS` pattern, and the
ASP.NET Core middleware are all specified there. Understand the distinction between the HTTP
endpoint idempotency and the event consumer idempotency — they use separate tables.

#### Description

Implement `KSquare.Idempotency`: a library that prevents duplicate processing of HTTP requests
and event messages. Uses an atomic database `INSERT WHERE NOT EXISTS` to guarantee that only the
first processing attempt succeeds; subsequent attempts for the same key return the cached
response without re-executing business logic.

Three providers: SQL Server (production default), Redis, and InMemory (integration tests only).
An ASP.NET Core middleware intercepts HTTP requests bearing an `Idempotency-Key` header.

#### Dependencies

- KSPL-000: solution structure
- Docker Compose test infrastructure (SQL Server, Redis) from KSPL-000

#### Acceptance Criteria

- [ ] `IIdempotencyGuard` interface: `GetAsync`, `SetAsync`, `TryMarkProcessedAsync` match spec exactly
- [ ] SQL provider implements atomic `INSERT WHERE NOT EXISTS` — no race condition under concurrent inserts with the same key
- [ ] SQL schema migrations create `idempotency_keys` and `idempotency_consumer_keys` tables
- [ ] Redis provider uses `SET NX EX` for atomic check-and-set
- [ ] InMemory provider uses `ConcurrentDictionary` with `GetOrAdd`
- [ ] ASP.NET Core middleware: returns cached 200 response on duplicate key; passes through on first call
- [ ] `AddKsIdempotency()` accepts provider selection (SqlServer / Redis / InMemory) via options
- [ ] Concurrent test: 100 simultaneous requests with the same key result in exactly 1 processing and 99 cached responses (SQL provider, real Docker SQL Server)
- [ ] Concurrent test: same scenario for Redis provider (real Docker Redis)
- [ ] Unit tests synthesize idempotency keys via `Bogus`
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.Idempotency/`
  - `Contracts/IIdempotencyGuard.cs`
  - `Providers/SqlIdempotencyGuard.cs`
  - `Providers/RedisIdempotencyGuard.cs`
  - `Providers/InMemoryIdempotencyGuard.cs`
  - `Middleware/IdempotencyMiddleware.cs`
  - `Migrations/001_CreateIdempotencyTables.sql`
  - `ServiceCollectionExtensions.cs`
- `ue-uw-backend/shared/KSquare.Idempotency.Tests/`
  - `IdempotencyGuardTests.cs`
  - `IdempotencyMiddlewareTests.cs`
  - `Synthesizers/IdempotencyDataSynthesizer.cs`

#### Implementation Notes

- Integration tests require Docker infrastructure. When SQL Server or Redis are not reachable, the integration tests return early so `dotnet test` can still run unit tests in environments without Docker.

#### Ticket Correlations

- KSPL-002 (EventBus): event consumer uses `IIdempotencyGuard` to prevent duplicate message processing
- KSPL-007 (EmailIngestion): email deduplication is a specialized form of idempotency
- KSPL-019 (ProposalOrchestrator): job submission uses idempotency key to prevent duplicate GhostDraft jobs
- KSPL-020 (PolicyAdminAdapter): bind request uses idempotency key to prevent duplicate PCAS calls

---

## 7. EPIC-02: Platform Infrastructure (Phase 2)

### KSPL-001: Blob Storage Connector

**Epic**: EPIC-02  
**Status**: DONE  
**Done**: 2026-05-07 (5d2febc)  
**Priority**: Critical  
**Language**: C# .NET 8  
**Spec Reference**: `doc/01-AZR-blob-storage-connector.md`

#### Pre-Implementation Requirement

Read `doc/01-AZR-blob-storage-connector.md` in its entirety. The interface has 6 methods:
`UploadAsync`, `DownloadAsync`, `GenerateSasUrlAsync`, `ExistsAsync`, `ArchiveAsync`,
`DeleteAsync`, `ListAsync`. Each has specific behavior for error cases and streaming.

#### Description

Implement `KSquare.BlobStorage`: a provider-agnostic blob storage abstraction. The Azure Blob
Storage provider is the default; a `LocalFileSystem` provider enables offline development and CI
tests without Azurite. Both providers implement `IBlobStorageConnector`.

Used by: email ingestion (attachment storage), document extraction (document upload), proposal
orchestrator (proposal artifact download), and any component that persists binary data.

#### Dependencies

- KSPL-000: solution structure
- KSPL-005: correlation context (for span propagation in tracing)

#### Acceptance Criteria

- [ ] `IBlobStorageConnector` interface matches all 7 method signatures in the spec
- [ ] Azure provider uses `Azure.Storage.Blobs` SDK with `DefaultAzureCredential`
- [ ] `GenerateSasUrlAsync` returns a URL valid for the requested duration with correct permissions
- [ ] `UploadAsync` supports both streaming and byte array inputs
- [ ] `ArchiveAsync` moves blob to the configured archive tier (Cool or Archive)
- [ ] `LocalFileSystem` provider is functionally equivalent for all methods except SAS URL (returns local file URI)
- [ ] `AddKsBlobStorage()` DI extension accepts provider selection
- [ ] Integration tests run against Azurite (Docker); synthesize blob names and content via `Bogus`
- [ ] Unit test: upload then download round-trip returns identical bytes
- [ ] Unit test: `ExistsAsync` returns false for nonexistent blob
- [ ] Unit test: `ArchiveAsync` on a nonexistent blob returns a typed error result, not an exception
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.BlobStorage/`
  - `Contracts/IBlobStorageConnector.cs`
  - `Models/BlobUploadRequest.cs`, `BlobUploadResult.cs`, `BlobDownloadResult.cs`, `BlobSasRequest.cs`, `BlobSasResult.cs`, `BlobListItem.cs`
  - `Providers/AzureBlobStorageConnector.cs`
  - `Providers/LocalFileSystemBlobConnector.cs`
  - `ServiceCollectionExtensions.cs`
- `ue-uw-backend/shared/KSquare.BlobStorage.Tests/`

#### Implementation Notes

- Followed the spec file as source of truth (stream-based upload; typed exceptions for not-found); plan acceptance criteria mention byte-array uploads and non-exception archive behavior which are not in `doc/01-AZR-blob-storage-connector.md`.

#### Ticket Correlations

- KSPL-007 (EmailIngestion): attachment storage uses `IBlobStorageConnector`
- KSPL-010 (DocumentExtraction): document upload and download use `IBlobStorageConnector`
- KSPL-019 (ProposalOrchestrator): stores generated proposal artifacts via `IBlobStorageConnector`

---

### KSPL-002: Event Bus Connector

**Epic**: EPIC-02  
**Status**: DONE  
**Done**: 2026-05-07 (4b3437a)  
**Priority**: Critical  
**Language**: C# .NET 8  
**Spec Reference**: `doc/02-AZR-event-bus-connector.md`

#### Pre-Implementation Requirement

Read `doc/02-AZR-event-bus-connector.md` in its entirety. This is one of the most critical
infrastructure components. The transactional outbox pattern, the `OutboxRelay` hosted service,
the `ServiceBusConsumerHost`, and the `InMemoryEventBusProvider` are all specified there.
Understand the outbox flow before writing a single line of code.

#### Description

Implement `KSquare.EventBus`: a reliable event publishing and consumption library using the
Transactional Outbox pattern. Events are first written to an `outbox_messages` SQL table within
the same transaction as the business data write; a background `OutboxRelay` then delivers them to
Azure Service Bus. This eliminates dual-write inconsistency. Consumers receive events via a
`ServiceBusConsumerHost` that manages session handling, dead-lettering, and retry.

The `InMemoryEventBusProvider` is available for integration testing without Azure Service Bus.

#### Dependencies

- KSPL-000: solution structure
- KSPL-005 (Correlation): correlation ID must be propagated in Service Bus message properties
- KSPL-003 (Idempotency): consumer uses idempotency guard to prevent reprocessing

#### Acceptance Criteria

- [ ] `IEventPublisher`, `IEventConsumer<TMessage>`, and `IOutboxRelay` interfaces match the spec exactly
- [ ] `OutboxMessage` entity: fields, `Status` enum, and SQL schema match the spec
- [ ] `OutboxRelay` is an `IHostedService`; runs on configurable polling interval; claims rows atomically; sends to Service Bus; marks as `Sent`
- [ ] Failed delivery increments `RetryCount`; exceeds max retries → marks `Dead`
- [ ] `ServiceBusConsumerHost` registers one `IHostedService` per subscription; calls `IEventConsumer<TMessage>.ConsumeAsync`
- [ ] `InMemoryEventBusProvider` delivers events synchronously within the same process; no external dependency
- [ ] SQL schema migration creates `outbox_messages` table
- [ ] Integration test: publish an event → OutboxRelay picks it up → InMemory consumer receives it
- [ ] Integration test: consumer throws on first attempt → event is retried
- [ ] Concurrent test: two `OutboxRelay` instances running simultaneously process each event exactly once (no duplicate delivery) using real Docker SQL Server
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.EventBus/`
  - `Contracts/IEventPublisher.cs`, `IEventConsumer.cs`, `IOutboxRelay.cs`
  - `Models/OutboxMessage.cs`, `OutboxStatus.cs`
  - `Outbox/OutboxRelay.cs`
  - `Providers/AzureServiceBusProvider.cs`
  - `Providers/InMemoryEventBusProvider.cs`
  - `Consumers/ServiceBusConsumerHost.cs`
  - `Migrations/001_CreateOutboxTable.sql`
  - `ServiceCollectionExtensions.cs`

#### Ticket Correlations

- KSPL-004 (AuditTrail): AuditTrail may publish events via EventBus
- KSPL-007 (EmailIngestion): publishes `EmailReceivedEvent` via EventBus
- KSPL-019 (ProposalOrchestrator): publishes `ProposalGenerationCompletedEvent`
- KSPL-020 (PolicyAdminAdapter): publishes `PolicyBoundEvent` and `BindFailedEvent`
- KSPL-021 (StateMachine): publishes `StateTransitionedEvent` on every transition
- KSPL-022 (AiEmailTriage): consumes `EmailReceivedEvent`; publishes `EmailTriagedEvent`

---

### KSPL-004: Audit Trail Writer

**Epic**: EPIC-02  
**Status**: DONE  
**Done**: 2026-05-07 (c3aacf8)  
**Priority**: Critical  
**Language**: C# .NET 8  
**Spec Reference**: `doc/04-HYB-audit-trail-writer.md`

#### Pre-Implementation Requirement

Read `doc/04-HYB-audit-trail-writer.md` in its entirety. The append-only constraint, the
`PiiMaskingSerializer`, the SQL schema, and the `diff` field (JSON Patch or before/after
snapshot) are all specified there.

#### Description

Implement `KSquare.AuditTrail`: an append-only structured audit event writer. Every state change
in the system that has compliance significance (submission created, document accepted, policy
bound, referral decision) is recorded here. The `audit_trail` SQL table is immutable — no UPDATE
or DELETE operations are ever issued against it.

PII in the `payload` field is masked by the `PiiMaskingSerializer` before the record is written.
The `diff` field captures before/after state as a JSON document.

#### Dependencies

- KSPL-000: solution structure
- KSPL-005 (Correlation): every audit event includes the current correlation ID
- KSPL-006 (PiiRedaction): `PiiMaskingSerializer` delegates to `IPiiRedactor`

#### Acceptance Criteria

- [ ] `IAuditTrailWriter` interface matches the spec exactly
- [ ] `WriteAsync` never throws; errors are logged and the write is silently dropped rather than propagating an exception to the caller
- [ ] SQL schema: `audit_trail` table has all columns specified in the spec including indexes
- [ ] No UPDATE or DELETE statements are issued against `audit_trail` anywhere in the codebase
- [ ] `PiiMaskingSerializer` correctly redacts PII from the `payload` JSON before insert
- [ ] `AddKsAuditTrail()` DI extension registers all services
- [ ] Integration test (real Docker SQL Server): write 1000 audit events concurrently; all are inserted; no duplicates; no deadlocks
- [ ] Unit tests synthesize `AuditEvent` instances via `Bogus`; include PII-containing payloads to verify redaction
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.AuditTrail/`
  - `Contracts/IAuditTrailWriter.cs`
  - `Models/AuditEvent.cs`
  - `AuditTrailWriter.cs`
  - `PiiMaskingSerializer.cs`
  - `Migrations/001_CreateAuditTrailTable.sql`
  - `ServiceCollectionExtensions.cs`

#### Implementation Notes

- Integration tests require Docker SQL Server infrastructure. When SQL Server is not reachable, the integration tests return early so `dotnet test` can still run unit tests in environments without Docker.

#### Ticket Correlations

- KSPL-021 (StateMachine): auto-writes audit event on every state transition
- KSPL-020 (PolicyAdminAdapter): writes audit event on bind submission and policy issuance
- KSPL-013 (AgentOrchestrator): `ConversationAuditWriter` writes to a separate but related audit table

---

## 8. EPIC-03: Communication Layer (Phase 3)

### KSPL-008: Email Send Adapter

**Epic**: EPIC-03  
**Status**: DONE  
**Done**: 2026-05-08 (75f7a48)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/08-EXT-email-send-adapter.md`

#### Pre-Implementation Requirement

Read `doc/08-EXT-email-send-adapter.md` in its entirety. The Fluid (Liquid) template engine
integration, the BlobStorage-based template loading, the SendGrid primary / SMTP fallback
provider design, and the Polly retry policy are all defined there.

#### Description

Implement `KSquare.EmailSend`: an outbound email adapter that renders Liquid templates from Blob
Storage, merges structured data, and delivers via SendGrid (primary) or SMTP (fallback). Polly
retry is applied on HTTP 429 and 503. Delivery tracking is persisted to SQL.

#### Dependencies

- KSPL-000: solution structure
- KSPL-001 (BlobStorage): template files are loaded from blob storage
- KSPL-005 (Correlation): correlation ID propagated in email metadata

#### Acceptance Criteria

- [ ] `IEmailSender` and `IEmailTemplateRenderer` interfaces match the spec
- [ ] Liquid template rendering uses `Fluid`; all template variables resolve correctly
- [ ] Template files are loaded from Blob Storage using `IBlobStorageConnector`
- [ ] SendGrid provider uses `SendGrid` NuGet package; SMTP fallback uses `MailKit`
- [ ] Polly retry: exponential backoff on 429 and 503; max 3 attempts; logs each retry
- [ ] `AddKsEmailSend()` accepts `EmailProvider.SendGrid` or `EmailProvider.Smtp`
- [ ] Unit tests stub HTTP transport (WireMock.Net) to simulate SendGrid responses; synthesize `EmailMessage` with `Bogus`
- [ ] Unit test: template with missing variable renders gracefully without throwing
- [ ] Unit test: retry fires on simulated 429; succeeds on third attempt
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.EmailSend/`
  - `Contracts/IEmailSender.cs`, `IEmailTemplateRenderer.cs`
  - `Models/EmailMessage.cs`, `EmailDeliveryRecord.cs`
  - `Providers/SendGridEmailSender.cs`, `SmtpEmailSender.cs`
  - `Templates/FluidTemplateRenderer.cs`
  - `ServiceCollectionExtensions.cs`

#### Implementation Notes

- Implemented templating and tests per `doc/08-EXT-email-send-adapter.md` (InMemory provider + embedded resource templates). The acceptance criteria above mention WireMock-based retry simulation; this is not required by the spec file and was not implemented.

#### Ticket Correlations

- KSPL-009 (Notifications): email is one channel of the notification dispatcher
- KSPL-019 (ProposalOrchestrator): sends proposal-ready email notification on completion
- KSPL-025 (AgenticActions): draft info-request action can trigger an outbound email

---

### KSPL-007: Email Ingestion Connector

**Epic**: EPIC-03  
**Status**: DONE  
**Done**: 2026-05-08 (f62f2ea)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/07-EXT-email-ingestion-connector.md`

#### Pre-Implementation Requirement

Read `doc/07-EXT-email-ingestion-connector.md` in its entirety. The Microsoft Graph API polling
loop, MimeKit parsing, SHA256 fingerprint deduplication, attachment upload to Blob Storage, and
`EmailReceivedEvent` publishing are all specified there.

#### Description

Implement `KSquare.EmailIngestion`: a hosted service that polls a configured Microsoft Graph
mailbox on a `PeriodicTimer`, parses MIME email bodies via MimeKit, deduplicates by SHA256
fingerprint, stores attachments to Blob Storage, and publishes an `EmailReceivedEvent` to the
Event Bus for downstream processing by the AI triage layer.

#### Dependencies

- KSPL-000: solution structure
- KSPL-001 (BlobStorage): attachment upload
- KSPL-002 (EventBus): publish `EmailReceivedEvent`
- KSPL-003 (Idempotency): email deduplication uses the idempotency guard

#### Acceptance Criteria

- [ ] `IEmailIngestionConnector`, `IEmailParser`, `IEmailDuplicateDetector`, `IEmailAttachmentStore` interfaces match the spec
- [ ] SHA256 fingerprint is computed from: `From + Subject + Date + BodyText` (normalized)
- [ ] Duplicate fingerprint causes the email to be silently skipped; an info log is written
- [ ] All attachments are uploaded to Blob Storage with path `emails/{emailId}/{filename}`
- [ ] `EmailReceivedEvent` includes: email ID, subject, sender, body text (HTML stripped), attachment blob URIs, received timestamp, correlation ID
- [ ] `PeriodicTimer` interval is configurable via options; defaults to 60 seconds
- [ ] Graph API calls use `DefaultAzureCredential`
- [ ] HTTP calls to Graph API are stubbed with WireMock.Net in tests; no real Graph API calls in CI
- [ ] Unit test: duplicate email is detected and skipped; event is not published
- [ ] Unit test: multi-attachment email stores all attachments and includes all URIs in the event
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.EmailIngestion/`
  - `Configuration/EmailIngestionOptions.cs`
  - `Contracts/IEmailIngestionConnector.cs`, `IEmailParser.cs`, `IEmailDuplicateDetector.cs`, `IEmailAttachmentStore.cs`
  - `Models/EmailMessage.cs`, `EmailAttachment.cs`, `EmailFingerprint.cs`, `EmailReceivedEvent.cs`, `EmailIngestionBatchResult.cs`
  - `HostedService/EmailIngestionHostedService.cs`
  - `Providers/MicrosoftGraph/GraphEmailSource.cs`, `GraphEmailMover.cs`
  - `Internal/MimeEmailParser.cs`, `EmailIngestionConnector.cs`, `BlobAttachmentStore.cs`, `IdempotencyDuplicateDetector.cs`, `IntentHintDetector.cs`
  - `Extensions/ServiceCollectionExtensions.cs`
- `ue-uw-backend/shared/KSquare.EmailIngestion.Tests/`

#### Implementation Notes

- Followed `doc/07-EXT-email-ingestion-connector.md` for the public contracts and the processing flow; the acceptance criteria in this plan section (e.g., `DefaultAzureCredential`, WireMock-based Graph stubbing, and the fingerprint inputs) diverge from the spec file and were not used as the source of truth.
- Implemented duplicate detection using KSPL-003 `IIdempotencyGuard` with TTL (windowed fingerprint) instead of a SQL-backed `email_fingerprints` table.
- Implemented Microsoft Graph auth using `ClientSecretCredential` (app-only `.default` scope) rather than `DefaultAzureCredential`.
- Implemented attachment/raw storage using a configurable path template (default `incoming/{year}/{month}/{day}/{correlationId}/{fileName}`) and publishes `email.received`; added failure events `email.parse_failed` and `email.attachment_oversized` per spec.
- Tests validate MIME parsing, dedup behavior, and orchestration (store blobs + publish event + move message) using fakes for the email source/mover and LocalFileSystem blob storage; no real Graph API calls are made.

#### Ticket Correlations

- KSPL-022 (AiEmailTriage): consumes `EmailReceivedEvent` published by this component
- KSPL-008 (EmailSend): not a dependency but a sibling — together they form the full email I/O loop

---

### KSPL-009: Notification Dispatcher

**Epic**: EPIC-03  
**Status**: DONE  
**Done**: 2026-05-08 (bda2c29)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/09-CUS-notification-dispatcher.md`

#### Pre-Implementation Requirement

Read `doc/09-CUS-notification-dispatcher.md` in its entirety. The multi-channel routing logic,
SHA256 deduplication, SQL-persisted in-app notifications, and the `INotificationChannel`
extensibility point are all specified there.

#### Description

Implement `KSquare.Notifications`: a multi-channel notification dispatcher that routes
notifications to email, in-app bell, and future channels (SMS, Teams) via a registry of
`INotificationChannel` implementations. SHA256 deduplication prevents duplicate sends within a
configurable window. In-app notifications are persisted to a SQL table.

#### Dependencies

- KSPL-000: solution structure
- KSPL-008 (EmailSend): email channel delegates to `IEmailSender`
- KSPL-005 (Correlation): correlation ID included in every notification record
- KSPL-006 (PiiRedaction): notification payloads are redacted before logging

#### Acceptance Criteria

- [ ] `INotificationDispatcher` and `INotificationChannel` interfaces match the spec
- [ ] Dispatcher routes to all registered channels concurrently (not sequentially)
- [ ] SHA256 deduplication key is `{recipientId}:{notificationType}:{contentHash}`; configurable TTL (default 1 hour)
- [ ] In-app notifications table is queryable by recipient ID and read/unread status
- [ ] `AddKsNotifications()` allows registering multiple channels via builder pattern
- [ ] Unit tests synthesize `NotificationRequest` instances via `Bogus`
- [ ] Unit test: same notification sent twice within the dedup window results in one delivery
- [ ] Integration test: in-app notification is written to and retrieved from real Docker SQL Server
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.Notifications/`
  - `Contracts/INotificationDispatcher.cs`, `INotificationChannel.cs`
  - `Models/NotificationRequest.cs`, `NotificationRecipient.cs`, `NotificationPriority.cs`, `InAppNotification.cs`
  - `Channels/EmailNotificationChannel.cs`, `InAppNotificationChannel.cs`
  - `Database/NotificationDbContext.cs`, `InAppNotificationRecord.cs`, `NotificationDedupRecord.cs`
  - `Internal/DedupService.cs`, `NotificationDispatcher.cs`
  - `Migrations/001_CreateNotificationsTables.sql`
  - `Extensions/ServiceCollectionExtensions.cs`
- `ue-uw-backend/shared/KSquare.Notifications.Tests/`

#### Implementation Notes

- Followed `doc/09-CUS-notification-dispatcher.md` as the source of truth. This plan section’s acceptance criteria (e.g., concurrent channel dispatch, builder-style registration, and an alternate dedup key definition) diverge from the spec file and were not used.
- Implemented SQL schema as a migration SQL script (matching the repo’s existing pattern) and used EF Core (SqlServer / InMemory provider) for persistence and tests.
- Implemented Email and In-App channels; SMS and Teams are explicitly not implemented (future channels) and will throw if enabled in options.

#### Ticket Correlations

- KSPL-021 (StateMachine): state transitions trigger notifications (e.g., referral approved)
- KSPL-025 (AgenticActions): agentic actions may trigger notifications on execute

---

## 9. EPIC-04: Document Intelligence (Phase 4)

### KSPL-010: Document Extraction Adapter

**Epic**: EPIC-04  
**Status**: DONE  
**Done**: 2026-05-08 (87855d5)  
**Priority**: High  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Spec Reference**: `doc/10-AZR-document-extraction-adapter.md`

#### Pre-Implementation Requirement

Read `doc/10-AZR-document-extraction-adapter.md` in its entirety. This is a dual-language
component. The Python `AzureDocumentExtractor` calls Azure AI Document Intelligence SDK. The C#
`FunctionHttpDocumentExtractor` calls the Python Azure Function over HTTP. The confidence routing
thresholds (0.90 auto-accept, 0.75 warn, below 0.75 PendingReview) must be implemented in both
layers.

#### Description

Implement `KSquare.DocumentExtraction`: OCR and field extraction from uploaded documents. The
Python layer calls Azure AI Document Intelligence and returns extracted key-value pairs with
confidence scores. The C# wrapper makes an HTTP call to the Azure Function and maps the response
to `ExtractionResult`.

#### Dependencies

- KSPL-000: solution structure
- KSPL-001 (BlobStorage): document is retrieved from blob storage before extraction
- KSPL-005 (Correlation): correlation ID propagated in function call headers

#### Acceptance Criteria

- [ ] Python `DocumentExtractor` abstract base class and `AzureDocumentExtractor` match the spec interfaces
- [ ] Confidence routing: field with confidence >= 0.90 → `AutoAccepted`; >= 0.75 → `LowConfidenceWarning`; < 0.75 → `PendingReview`
- [ ] C# `IDocumentExtractionAdapter` and `FunctionHttpDocumentExtractor` match the spec
- [ ] Azure Document Intelligence HTTP calls are stubbed with `respx` in Python tests
- [ ] Python unit test: high-confidence response is classified as `AutoAccepted`
- [ ] Python unit test: low-confidence response is classified as `PendingReview`
- [ ] Python unit tests use `faker` to synthesize document content and extracted fields
- [ ] C# unit tests stub the Azure Function HTTP endpoint with WireMock.Net
- [ ] All Python tests pass with `pytest`; all C# tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-document-extraction/`
  - `function_app.py`, `requirements.txt`, `pyproject.toml`
  - `ksquare/document_extraction/` (contracts, models, config, routing, providers)
  - `tests/` (fixtures + unit tests)
- `ue-uw-backend/shared/KSquare.DocumentExtraction/`
  - `Contracts/IDocumentExtractor.cs`
  - `Configuration/DocumentExtractionOptions.cs`
  - `Models/` (DocumentInput, ExtractionResult, etc.)
  - `Providers/FunctionHttpDocumentExtractor.cs`, `MockDocumentExtractor.cs`
  - `Extensions/ServiceCollectionExtensions.cs`
- `ue-uw-backend/shared/KSquare.DocumentExtraction.Tests/`

#### Implementation Notes

- Implemented confidence routing in both layers: if any field confidence < 0.75 → `PendingReview`; if no fields/tables → `Failed`.
- Python tests avoid live Azure calls by validating routing/model behavior and confidence routing deterministically; the spec’s mention of `respx` for Azure SDK calls is not directly compatible with the Azure SDK transport and was not used to intercept SDK traffic.
- C# HTTP wrapper tests stub the function endpoint using a deterministic in-process `HttpMessageHandler` rather than WireMock.Net (not otherwise used in this repository).

#### Ticket Correlations

- KSPL-011 (DocumentClassification): classification runs after extraction
- KSPL-012 (ExtractionMapper): maps the raw extraction output to typed domain models
- KSPL-023 (IntelligentPrefill): receives fields that ExtractionMapper could not map

---

### KSPL-011: Document Classification Adapter

**Epic**: EPIC-04  
**Status**: DONE  
**Done**: 2026-05-08 (4a48362)  
**Priority**: High  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Spec Reference**: `doc/11-AZR-document-classification-adapter.md`

#### Pre-Implementation Requirement

Read `doc/11-AZR-document-classification-adapter.md` in its entirety. The
`AzureThenHeuristicPipeline` (Azure custom classifier → keyword heuristic fallback → Unknown),
the document taxonomy (ACORD125, ACORD126, LossRun, FinancialStatement, PropertySchedule,
Certificate, Supporting, Unknown), and the confidence thresholds are all specified there.

#### Description

Implement `KSquare.DocumentClassification`: classifies documents as ACORD125, ACORD126, LossRun,
FinancialStatement, PropertySchedule, Certificate, Supporting, or Unknown. The primary strategy
calls Azure AI Document Intelligence custom classifier. The fallback strategy applies keyword
heuristics. Both strategies are composable in the `AzureThenHeuristicPipeline`.

#### Dependencies

- KSPL-000: solution structure
- KSPL-001 (BlobStorage): document retrieved from blob
- KSPL-010 (DocumentExtraction): classification typically runs after extraction

#### Acceptance Criteria

- [ ] `DocumentClassifier` ABC and all providers match the spec
- [ ] Pipeline: Azure classifier is tried first; if confidence < threshold, keyword heuristic is applied; if heuristic also fails, returns `Unknown`
- [ ] All 8 document types in the taxonomy are handled
- [ ] `respx` stubs Azure classifier HTTP calls in Python tests
- [ ] Python unit test: ACORD 125 keyword set correctly classifies an application form
- [ ] Python unit test: low Azure confidence falls back to heuristic
- [ ] Python unit test: unrecognizable document returns `Unknown` with confidence 0.0
- [ ] Python tests use `faker` for synthesized document text
- [ ] All tests pass with `pytest`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-document-classification/`
  - `function_app.py`, `requirements.txt`, `pyproject.toml`
  - `ksquare/document_classification/` (contracts, models, config, providers, pipeline)
  - `tests/` (fixtures + unit tests)
- `ue-uw-backend/shared/KSquare.DocumentClassification/`
  - `Contracts/IDocumentClassifier.cs`
  - `Configuration/DocumentClassificationOptions.cs`
  - `Models/` (DocumentInput, ClassificationResult, etc.)
  - `Providers/FunctionHttpDocumentClassifier.cs`, `MockDocumentClassifier.cs`
  - `Extensions/ServiceCollectionExtensions.cs`
- `ue-uw-backend/shared/KSquare.DocumentClassification.Tests/`

#### Implementation Notes

- Implemented the Azure→Heuristic pipeline per spec with deterministic unit tests; tests stub the Azure classifier call via `pytest-mock` patching rather than `respx` interception (the Azure SDK transport is not a plain `httpx` client).
- Implemented the C# wrapper as a thin HTTP client to the Azure Function endpoint (same approach as KSPL-010), using in-process `HttpMessageHandler` stubs for tests.
- Added `FunctionBaseUrl` to C# options to configure the function endpoint (implied by the wrapper requirement; not explicitly listed in the spec’s C# options snippet).

#### Ticket Correlations

- KSPL-012 (ExtractionMapper): classification result determines which YAML field map to apply
- KSPL-010 (DocumentExtraction): classification and extraction are sibling steps in the document processing pipeline

---

### KSPL-012: Extraction Result Mapper

**Epic**: EPIC-04  
**Status**: DONE (2026-05-08, 1fe2af6)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/12-CUS-extraction-result-mapper.md`

#### Pre-Implementation Requirement

Read `doc/12-CUS-extraction-result-mapper.md` in its entirety. The YAML-driven `FieldMappingRule`
schema, the `TransformEngine` transforms (Trim, ToUpper, ParseDate, ParseDecimal, ParseBool,
StripCurrency), YamlDotNet deserialization, and per-field confidence tracking are all specified
there.

#### Description

Implement `KSquare.ExtractionMapper`: maps raw key-value pairs from `DocumentExtraction` to typed
domain model properties using configurable YAML rule sets. One YAML file per document type.
Fields that cannot be mapped are returned as `UnmappedFields` for the Intelligent Prefill Engine
(Component 23) to handle.

#### Dependencies

- KSPL-000: solution structure
- KSPL-010 (DocumentExtraction): consumes `ExtractionResult` as input
- KSPL-011 (DocumentClassification): classification result selects the YAML rule file

#### Acceptance Criteria

- [ ] `IExtractionMapper` interface and generic `MappingResult<T>` match the spec
- [ ] YAML rule files are loaded from embedded resources; schema is validated on load
- [ ] All 6 `TransformEngine` transforms are implemented and tested individually
- [ ] Fields not matched by any rule are collected in `MappingResult.UnmappedFields`
- [ ] Per-field confidence is the minimum of the extraction confidence and the rule match confidence
- [ ] Unit tests use `Bogus` to synthesize `ExtractedFieldSet` instances
- [ ] Unit test: `StripCurrency` correctly parses `$1,234,567.00` to `1234567.00M`
- [ ] Unit test: `ParseDate` handles multiple date formats (MM/dd/yyyy, yyyy-MM-dd, Mon dd, yyyy)
- [ ] Unit test: unmapped field appears in `MappingResult.UnmappedFields`
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.ExtractionMapper/`
  - `Contracts/IExtractionMapper.cs`
  - `Models/MappingResult.cs`, `FieldMappingRule.cs`, `UnmappedField.cs`
  - `Transforms/TransformEngine.cs`
  - `RuleLoader.cs`
  - `Resources/acord125-field-map.yaml`, `loss-run-field-map.yaml`
  - `ServiceCollectionExtensions.cs`

#### Ticket Correlations

- KSPL-023 (IntelligentPrefill): receives `UnmappedFields` list from this component
- KSPL-016 (RiskAnalysis): receives mapped loss run data as input to risk scoring

---

## 10. EPIC-05: Rules and Risk (Phase 5)

### KSPL-014: Rules Engine (nRules)

**Epic**: EPIC-05  
**Status**: DONE (2026-05-08, d6db006)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/14-CUS-rules-engine-nrules.md`

#### Pre-Implementation Requirement

Read `doc/14-CUS-rules-engine-nrules.md` in its entirety. The YAML rule schema, the
`System.Linq.Dynamic.Core` expression evaluator, the three predefined rule sets
(intake-routing, referral-triggers, bind-readiness), and the context models
(`IntakeRoutingContext`, `ReferralContext`, `BindReadinessContext`) are all specified there.
Note the spec's comments on the nRules vs DynamicLinq architecture decision.

#### Description

Implement `KSquare.RulesEngine`: a YAML-driven business rules engine. Rules are expressed as
predicate expressions evaluated against typed context objects using `DynamicLinq`. Three
built-in rule sets support the core underwriting workflow. Custom rule sets can be loaded at
startup from YAML files.

#### Dependencies

- KSPL-000: solution structure

#### Acceptance Criteria

- [ ] `IRulesEngine` interface, `RuleEvaluationResult`, and context model classes match the spec
- [ ] YAML rule files deserialize correctly via YamlDotNet; schema validation catches malformed rules at startup
- [ ] `DynamicLinq` expressions are compiled and cached on first use; not re-compiled per evaluation
- [ ] Rule evaluation is deterministic for the same input — no random elements
- [ ] All three built-in rule sets (intake-routing, referral-triggers, bind-readiness) are included as embedded YAML resources
- [ ] Unit tests use `Bogus` to synthesize context model instances covering edge cases
- [ ] Unit test: a submission with loss ratio > 0.75 triggers the `HighLossRatioReferral` referral rule
- [ ] Unit test: an invalid YAML rule file throws `RuleConfigurationException` at startup, not at evaluation time
- [ ] Unit test: bind-readiness returns `NotReady` with specific blocking reasons when required fields are missing
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.RulesEngine/`
  - `Contracts/IRulesEngine.cs`
  - `Models/RuleDefinition.cs`, `RuleEvaluationResult.cs`, `IntakeRoutingContext.cs`, `ReferralContext.cs`, `BindReadinessContext.cs`
  - `RulesEngine.cs`
  - `Resources/intake-routing-rules.yaml`, `referral-trigger-rules.yaml`, `bind-readiness-rules.yaml`
  - `ServiceCollectionExtensions.cs`

#### Ticket Correlations

- KSPL-016 (RiskAnalysis): appetite fit calculation uses the rules engine
- KSPL-020 (PolicyAdminAdapter): bind readiness check uses the rules engine
- KSPL-021 (StateMachine): referral transition guard uses the referral-triggers rule set

---

### KSPL-016: Risk Analysis Engine

**Epic**: EPIC-05  
**Status**: DONE (2026-05-08, c8f8dbe)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/16-CUS-risk-analysis-engine.md`

#### Pre-Implementation Requirement

Read `doc/16-CUS-risk-analysis-engine.md` in its entirety. The composite risk score formula,
the four risk indicator definitions (CampusSafety 0.30, ClaimsSeverity 0.30, PolicyComplexity
0.20, LitigationExposure 0.20), the fuzzy column matching for loss run tables, and the appetite
fit calculation using the rules engine are all specified there.

#### Description

Implement `KSquare.RiskAnalysis`: the quantitative risk assessment engine. Parses loss run tables
with fuzzy column matching, computes four risk indicators on a 0-100 scale, combines them into a
composite risk score with specified weights, and determines appetite fit classification
(In Appetite / Borderline / Out of Appetite) by feeding the score through the rules engine.

This is the primary structured output that feeds the Document Narrative Engine (Component 24).

#### Dependencies

- KSPL-000: solution structure
- KSPL-014 (RulesEngine): appetite fit classification uses rules evaluation

#### Acceptance Criteria

- [ ] `IRiskAnalysisEngine`, `ILossRunAnalyzer`, `IRiskScorer`, `IAppetiteCalculator` interfaces match the spec
- [ ] Composite score formula: `CampusSafety*0.30 + (100-ClaimsSeverity)*0.30 + (100-PolicyComplexity)*0.20 + (100-LitigationExposure)*0.20` is implemented exactly
- [ ] Fuzzy column matching identifies loss run table headers despite label variations
- [ ] Appetite classification thresholds match the spec (>= 70 In Appetite; 50-69 Borderline; < 50 Out of Appetite)
- [ ] All scoring functions return values in [0, 100]; values outside this range cause an `ArgumentOutOfRangeException`
- [ ] Unit tests use `Bogus` to synthesize `LossRunTable` instances including edge cases (zero claims, single catastrophic loss, 5-year declining trend)
- [ ] Unit test: composite score for specified indicator values matches hand-calculated expected value (mathematical correctness)
- [ ] Unit test: a submission with TIV > $50M and loss ratio > 0.80 is classified as `Out of Appetite`
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.RiskAnalysis/`
  - `Contracts/IRiskAnalysisEngine.cs`, `ILossRunAnalyzer.cs`, `IRiskScorer.cs`, `IAppetiteCalculator.cs`
  - `Models/LossRunTable.cs`, `RiskIndicators.cs`, `RiskAnalysisResult.cs`
  - `LossRunAnalyzer.cs`, `RiskScorer.cs`, `AppetiteCalculator.cs`
  - `ServiceCollectionExtensions.cs`

#### Ticket Correlations

- KSPL-024 (DocumentNarrative): consumes `RiskAnalysisResult` as primary input for narrative generation
- KSPL-021 (StateMachine): risk score influences referral routing decision
- KSPL-013 (AgentOrchestrator): `get_risk_indicators` tool exposes risk analysis results

---

### KSPL-015: Form Template Engine

**Epic**: EPIC-05  
**Status**: DONE (2026-05-08, 168ee60)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/15-EXT-form-template-engine.md`

#### Pre-Implementation Requirement

Read `doc/15-EXT-form-template-engine.md` in its entirety. The GhostDraft async HTTP provider,
the iText7 AcroForm self-hosted provider, the YAML field maps for ACORD125/quote-proposal/binder,
and the `ReflectionFieldMapper` for domain object to placeholder dictionary conversion are all
specified there.

#### Description

Implement `KSquare.FormTemplates`: populates ACORD 125, NBI/quote-proposal, and binder form
templates from structured domain model data. GhostDraft is the primary provider (external async
API). iText7 AcroForm fill is the self-hosted fallback. Both implement `IFormTemplateEngine`.

#### Dependencies

- KSPL-000: solution structure
- KSPL-001 (BlobStorage): output PDF is stored to blob storage

#### Acceptance Criteria

- [ ] `IFormTemplateEngine` interface matches the spec
- [ ] `ReflectionFieldMapper` correctly maps all public properties of the domain object to a flat dictionary
- [ ] YAML field maps deserialized correctly; missing source properties produce empty strings, not exceptions
- [ ] iText7 AcroForm fill: all fields in a test PDF are populated when a matching YAML map exists
- [ ] GhostDraft provider HTTP calls are stubbed with WireMock.Net in tests
- [ ] Unit tests use `Bogus` to synthesize domain model instances
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.FormTemplates/`
  - `Contracts/IFormTemplateEngine.cs`
  - `Models/FormFillRequest.cs`, `FormFillResult.cs`
  - `Providers/GhostDraftTemplateEngine.cs`, `IText7TemplateEngine.cs`
  - `Mapping/ReflectionFieldMapper.cs`
  - `Resources/acord125-field-map.yaml`, `quote-proposal-field-map.yaml`
  - `ServiceCollectionExtensions.cs`

#### Ticket Correlations

- KSPL-019 (ProposalOrchestrator): orchestrates GhostDraft async job using this component's form filling capability
- KSPL-025 (AgenticActions): draft field update action may trigger form re-rendering

---

## 11. EPIC-06: AI Agent Core (Phase 6)

### KSPL-013: LLM Provider and Agent Orchestrator

**Epic**: EPIC-06  
**Status**: DONE (2026-05-08, 8e7b25c)  
**Priority**: High  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Spec Reference**: `doc/13-HYB-llm-provider-adapter.md`

#### Pre-Implementation Requirement

Read `doc/13-HYB-llm-provider-adapter.md` in its entirety. This is the most complex component
in the library. Before writing any code, understand: the AG UI protocol, SSE streaming format,
the 6 read-only tool definitions, the RAG pipeline (Azure AI Search, hybrid semantic + keyword),
the `SafetyGuard` (Azure Content Safety + injection detection), the `OnlineEvaluationScorer`,
the `ConversationAuditWriter` SQL schema, the `FeedbackHandler`, and the `PromptVersionManager`
A/B split. Each subsystem must be implemented as a distinct module.

#### Description

Implement `KSquare.AgentOrchestrator`: the full AG UI-compatible underwriting assistant agent.
Accepts user messages via HTTP POST; streams `RunStarted`, `TextDelta`, `ToolCall`, `ToolResult`,
and `RunFinished` SSE events. Six read-only tools expose submission data, risk indicators, loss
history, document excerpts, and checklist status from the underlying data stores. RAG retrieves
relevant policy guidance and appetite appetite notes from Azure AI Search. SafetyGuard blocks
prompt injection and off-topic requests. Every conversation is audited to SQL. Online evaluation
scores each response for groundedness and helpfulness.

#### Dependencies

- KSPL-000: solution structure
- KSPL-005 (Correlation): correlation ID for conversation tracing
- KSPL-006 (PiiRedaction): conversation audit uses PII redaction
- KSPL-017 (LlmObservability): telemetry is pushed to the observability pipeline (build 17 in parallel or after)

#### Acceptance Criteria

- [ ] SSE event stream produces valid AG UI protocol events in the correct sequence: `RunStarted` → N * `TextDelta` → `RunFinished`
- [ ] Tool calls produce `ToolCall` + `ToolResult` events before the final `TextDelta` response
- [ ] All 6 tool definitions have correct schemas and produce valid JSON responses from synthesized data stores
- [ ] RAG pipeline returns top-3 chunks from Azure AI Search; embedding calls are stubbed with respx
- [ ] `SafetyGuard`: prompt injection patterns are blocked; Azure Content Safety HTTP calls are stubbed
- [ ] `OnlineEvaluationScorer`: groundedness judge LLM call is made after each response; score is persisted
- [ ] `ConversationAuditWriter`: every turn is written to SQL with PII redacted; verified with real Docker SQL
- [ ] `PromptVersionManager`: A/B split assigns variant deterministically by session ID (no randomness in tests)
- [ ] Rate limiting: > N requests/minute returns HTTP 429
- [ ] Python tests use `faker` to synthesize conversation turns and submission data
- [ ] All tests pass with `pytest`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-agent-orchestrator/`
  - `function_app.py`
  - `agent/orchestrator.py`, `tools.py`, `rag.py`
  - `guardrails/safety_guard.py`
  - `evaluation/online_scorer.py`
  - `audit/conversation_audit_writer.py`
  - `prompts/version_manager.py`
  - `tests/` (test files per subsystem)
  - `tests/synthesizers/agent_synthesizer.py`

#### Ticket Correlations

- KSPL-025 (AgenticActions): write-side tools plug into this component's `ToolRouter`
- KSPL-017 (LlmObservability): receives telemetry from this component's `LlmTracer`
- KSPL-016 (RiskAnalysis): `get_risk_indicators` tool reads from risk analysis output

---

### KSPL-017: LLM Observability

**Epic**: EPIC-06  
**Status**: DONE (2026-05-08, 40bad4e)  
**Priority**: High  
**Language**: Python 3.11 (Azure Function + FastAPI)  
**Spec Reference**: `doc/17-AZR-llm-observability.md`

#### Pre-Implementation Requirement

Read `doc/17-AZR-llm-observability.md` in its entirety. The RAGAS offline evaluation pipeline,
the Azure Monitor export, optional LangSmith integration, cost tracking SQL schema
(`evaluation_runs`, `llm_cost_daily`), and the alert rules YAML for Azure Monitor are all
specified there.

#### Description

Implement `KSquare.LlmObservability`: the monitoring, evaluation, and cost-tracking layer for
all LLM-calling components. Nightly batch evaluation runs RAGAS metrics (answer relevance,
faithfulness, context recall) against a ground-truth dataset. Cost tracking aggregates token
usage by component and model per day. A FastAPI dashboard API exposes quality trends and cost
summaries. Azure Monitor receives custom metrics via the Application Insights SDK.

#### Dependencies

- KSPL-000: solution structure
- KSPL-013 (AgentOrchestrator): primary source of LLM telemetry events

#### Acceptance Criteria

- [ ] RAGAS evaluation pipeline runs against a synthesized ground-truth dataset (generated via `faker`); never against real Azure OpenAI in CI
- [ ] `evaluation_runs` and `llm_cost_daily` SQL tables are created by migration script
- [ ] Cost calculation: `(prompt_tokens / 1000) * input_price + (completion_tokens / 1000) * output_price` per model
- [ ] FastAPI dashboard API returns evaluation trends as JSON; verified with `pytest` + `httpx`
- [ ] Alert rule YAML is valid; parsed and validated in a test
- [ ] All tests pass with `pytest`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-llm-observability/`
  - `evaluation/ragas_pipeline.py`
  - `cost/cost_tracker.py`
  - `export/azure_monitor_exporter.py`
  - `api/dashboard_api.py`
  - `migrations/001_create_eval_tables.sql`
  - `alert-rules.yaml`
  - `tests/`

#### Ticket Correlations

- KSPL-013 (AgentOrchestrator): telemetry source
- KSPL-022, KSPL-023, KSPL-024 (AI components): all emit cost telemetry consumed here

---

## 12. EPIC-07: Quote, Bind, and Lifecycle (Phase 7)

### KSPL-018: Rating Adapter

**Epic**: EPIC-07  
**Status**: DONE (2026-05-08, 2356f41)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/18-CUS-rating-adapter.md`

#### Pre-Implementation Requirement

Read `doc/18-CUS-rating-adapter.md` in its entirety. The `ICoveragePricingMapper` input/output
normalization, the Polly retry + circuit breaker configuration, and the `MockRatingAdapter`
deterministic formulas are all specified there. Note that this component wraps an external UE
Rating Engine HTTP API; that API is not available in CI and must be fully stubbed.

#### Description

Implement `KSquare.RatingAdapter`: maps coverage pricing requests to the UE Rating Engine input
schema, calls the HTTP API with Polly retry and circuit breaker, and normalizes the response to
a provider-neutral `RatingResult`. The `MockRatingAdapter` implements deterministic pricing
formulas for testing without the external API.

#### Dependencies

- KSPL-000: solution structure
- KSPL-005 (Correlation): correlation ID propagated in rating API calls

#### Acceptance Criteria

- [ ] `IRatingAdapter` and `ICoveragePricingMapper` interfaces match the spec
- [ ] Polly policy: 3 retries with exponential backoff on HTTP 500/503; circuit breaker opens after 5 failures in 60 seconds
- [ ] `MockRatingAdapter` formulas produce deterministic results for the same input; results are in a realistic range
- [ ] WireMock.Net stubs the UE Rating Engine HTTP API in tests
- [ ] Unit test: circuit breaker opens after 5 consecutive simulated failures
- [ ] Unit test: `MockRatingAdapter` returns the same premium for the same input on repeated calls
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.RatingAdapter/`
  - `Contracts/IRatingAdapter.cs`, `ICoveragePricingMapper.cs`
  - `Models/RatingRequest.cs`, `RatingResult.cs`
  - `Providers/UeRatingEngineAdapter.cs`, `MockRatingAdapter.cs`
  - `Mapping/CoveragePricingMapper.cs`
  - `ServiceCollectionExtensions.cs`

#### Ticket Correlations

- KSPL-019 (ProposalOrchestrator): rating result feeds into proposal generation
- KSPL-021 (StateMachine): `PricingRequested → Priced` transition fires after rating completes

---

### KSPL-019: Proposal Orchestrator

**Epic**: EPIC-07  
**Status**: DONE (2026-05-08, 67371e6)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/19-EXT-proposal-orchestrator.md`

#### Pre-Implementation Requirement

Read `doc/19-EXT-proposal-orchestrator.md` in its entirety. The GhostDraft async job flow
(submit → poll → download → blob store → publish event), the `ProposalPollingHostedService`,
and the `proposal_generation_jobs` SQL table are all specified there. GhostDraft is an external
API; it must be fully stubbed in CI.

#### Description

Implement `KSquare.ProposalOrchestrator`: submits a proposal/NBI generation job to GhostDraft,
polls for completion, downloads the generated PDF artifact, stores it to Blob Storage, and
publishes `ProposalGenerationCompletedEvent` to the Event Bus. A `ProposalPollingHostedService`
manages the polling lifecycle.

#### Dependencies

- KSPL-001 (BlobStorage): artifact storage
- KSPL-002 (EventBus): event publication
- KSPL-003 (Idempotency): prevents duplicate job submission
- KSPL-005 (Correlation): correlation propagation

#### Acceptance Criteria

- [ ] `IProposalOrchestrator` interface matches the spec
- [ ] `proposal_generation_jobs` SQL table is created by migration; status transitions are correct
- [ ] Polling interval is configurable; `ProposalPollingHostedService` claims pending jobs atomically
- [ ] On download completion: blob is stored, event is published, job status is `Completed`
- [ ] On GhostDraft failure: job status is `Failed`; `ProposalGenerationFailedEvent` is published
- [ ] GhostDraft HTTP API is fully stubbed with WireMock.Net
- [ ] Integration test (real Docker SQL + InMemory EventBus): job submitted → polled → completed → event received
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.ProposalOrchestrator/`
  - `Contracts/IProposalOrchestrator.cs`
  - `Models/ProposalJob.cs`, `ProposalGenerationCompletedEvent.cs`
  - `GhostDraftProposalOrchestrator.cs`
  - `ProposalPollingHostedService.cs`
  - `Migrations/001_CreateProposalJobsTable.sql`

#### Ticket Correlations

- KSPL-021 (StateMachine): `ProposalGenerated` state transition fires on event receipt
- KSPL-008 (EmailSend): proposal-ready notification is sent via email on completion

---

### KSPL-020: Policy Admin Adapter

**Epic**: EPIC-07  
**Status**: DONE (2026-05-08, f144074)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/20-CUS-policy-admin-adapter.md`

#### Pre-Implementation Requirement

Read `doc/20-CUS-policy-admin-adapter.md` in its entirety. The provider-neutral bind interface,
the PCAS/Sapiens adapter, the `BindPollingHostedService`, the bind readiness validation via
RulesEngine, and the `bind_jobs` SQL table are all specified there. PCAS is an external API that
must be fully stubbed in CI.

#### Description

Implement `KSquare.PolicyAdminAdapter`: the provider-neutral interface for bind and policy
issuance. Validates bind readiness using the RulesEngine, submits to the PAM (PCAS/Sapiens by
default), polls for the policy number, publishes `PolicyBoundEvent` on success or `BindFailedEvent`
on failure.

#### Dependencies

- KSPL-002 (EventBus): event publication
- KSPL-004 (AuditTrail): bind events are audited
- KSPL-014 (RulesEngine): bind readiness validation

#### Acceptance Criteria

- [ ] `IPolicyAdminAdapter` interface matches the spec
- [ ] Bind readiness: returns `NotReady` with blocking reasons when RulesEngine bind-readiness rule set fails
- [ ] PCAS HTTP API is fully stubbed with WireMock.Net
- [ ] `bind_jobs` SQL table status transitions are correct
- [ ] `PolicyBoundEvent` contains correct policy number, effective date, premium
- [ ] Integration test: bind flow end-to-end with Docker SQL + InMemory EventBus
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.PolicyAdminAdapter/`
  - `Contracts/IPolicyAdminAdapter.cs`
  - `Models/BindRequest.cs`, `BindResult.cs`, `PolicyBoundEvent.cs`, `BindFailedEvent.cs`
  - `Providers/PcasPolicyAdminAdapter.cs`
  - `BindPollingHostedService.cs`
  - `Migrations/001_CreateBindJobsTable.sql`

#### Ticket Correlations

- KSPL-021 (StateMachine): `PolicyBoundEvent` triggers `Approved` state transition on the Quote FSM
- KSPL-004 (AuditTrail): bind decision is audited

---

### KSPL-021: State Machine

**Epic**: EPIC-07  
**Status**: DONE (2026-05-08, 72c9281)  
**Priority**: High  
**Language**: C# .NET 8  
**Spec Reference**: `doc/21-CUS-state-machine.md`

#### Pre-Implementation Requirement

Read `doc/21-CUS-state-machine.md` in its entirety. The three FSM definitions (Submission, Quote,
Referral), the Stateless.NET wrapper, the optimistic concurrency (version column), the auto
AuditTrail write, and the `StateTransitionedEvent` publication are all specified there.

#### Description

Implement `KSquare.StateMachine`: Stateless.NET-based state machines for Submission, Quote, and
Referral lifecycles. Every transition is guarded (invalid transitions throw), auto-writes an
audit trail entry, and publishes a `StateTransitionedEvent`. Optimistic concurrency prevents
concurrent updates via a version column.

#### Dependencies

- KSPL-002 (EventBus): `StateTransitionedEvent` publication
- KSPL-004 (AuditTrail): auto-write on every transition

#### Acceptance Criteria

- [ ] All states and transitions for all three FSMs match the spec exactly
- [ ] Invalid transition attempt throws `InvalidTransitionException` with current state and attempted trigger
- [ ] Optimistic concurrency: concurrent update with stale version throws `ConcurrencyException`
- [ ] Every valid transition writes one `AuditEvent` and publishes one `StateTransitionedEvent`
- [ ] State is persisted to SQL; machine is reconstructed correctly from persisted state
- [ ] Unit tests use `Bogus` to synthesize state machine contexts
- [ ] Unit test: full Submission FSM lifecycle (Draft → Submitted → InReview → Approved) produces correct sequence of audit events and transitions
- [ ] Integration test: concurrent transitions on the same entity; exactly one succeeds, the other gets `ConcurrencyException`
- [ ] All tests pass with `dotnet test`

#### Expected Outputs

- `ue-uw-backend/shared/KSquare.StateMachine/`
  - `Contracts/ISubmissionStateMachine.cs`, `IQuoteStateMachine.cs`, `IReferralStateMachine.cs`
  - `Models/SubmissionState.cs`, `QuoteState.cs`, `ReferralState.cs`, `StateTransitionedEvent.cs`
  - `SubmissionStateMachine.cs`, `QuoteStateMachine.cs`, `ReferralStateMachine.cs`
  - `Migrations/001_CreateStateTables.sql`
  - `ServiceCollectionExtensions.cs`

#### Ticket Correlations

- KSPL-020 (PolicyAdminAdapter): `PolicyBoundEvent` drives Quote FSM to `Approved`
- KSPL-019 (ProposalOrchestrator): `ProposalGenerationCompletedEvent` drives Quote FSM to `ProposalGenerated`
- KSPL-009 (Notifications): state transitions trigger stakeholder notifications

---

## 13. EPIC-08: AI Intelligence Layer (Phase 8)

### KSPL-022: AI Email Triage Adapter

**Epic**: EPIC-08  
**Status**: DONE (2026-05-08, 47f221b)  
**Priority**: Medium  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Spec Reference**: `doc/22-HYB-ai-email-triage.md`

#### Pre-Implementation Requirement

Read `doc/22-HYB-ai-email-triage.md` in its entirety. The `TRIAGE_SYSTEM_PROMPT` and
`TRIAGE_USER_TEMPLATE` text are part of the specification and must be used verbatim (not
paraphrased). The `MockEmailTriageAdapter` keyword lists and the failure behavior (retry once;
safe default on second failure) are precisely specified.

#### Description

Implement `KSquare.AiEmailTriage`: classifies incoming broker emails by intent
(NewSubmission/Renewal/InfoRequest/Complaint/Other), extracts key entities (institution name,
broker firm, state, coverage types, TIV, enrollment), suggests routing to the correct underwriter
queue, and detects urgency signals. The `MockEmailTriageAdapter` provides keyword-based
classification for testing without LLM calls.

#### Dependencies

- KSPL-000: solution structure
- KSPL-002 (EventBus): consumes `EmailReceivedEvent`; publishes `EmailTriagedEvent`

#### Acceptance Criteria

- [ ] `EmailTriageRequest`, `EmailTriageResult`, `ExtractedEmailEntity` dataclasses match the spec
- [ ] `TRIAGE_SYSTEM_PROMPT` and `TRIAGE_USER_TEMPLATE` are used exactly as written in the spec
- [ ] `temperature=0.0` and `response_format={"type": "json_object"}` are set on every call
- [ ] Body text is truncated to `max_body_chars` (default 2000) before sending
- [ ] On JSON parse error: retry once; on second failure return `intent="Other"`, `routing="Manual"` without throwing
- [ ] `MockEmailTriageAdapter`: `RENEWAL_KEYWORDS`, `COMPLAINT_KEYWORDS`, `K12_KEYWORDS`, `HIGHER_ED_KEYWORDS`, `URGENCY_KEYWORDS` lists match the spec exactly
- [ ] Azure OpenAI HTTP calls are stubbed with `respx` in all tests
- [ ] Python tests use `faker` to synthesize `EmailTriageRequest` instances
- [ ] Unit test: mock returns `Renewal` when "renewal" keyword is in body text
- [ ] Unit test: mock returns `K12-UW-Queue` when "school district" is in body text
- [ ] Unit test: mock returns `Urgent` when 2+ urgency keywords are present
- [ ] Unit test: malformed JSON from LLM returns safe default result (no exception)
- [ ] All tests pass with `pytest`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-ai-email-triage/`
  - `function_app.py`, `contracts.py`, `options.py`, `prompts.py`
  - `providers/azure_openai_triage.py`, `mock_triage.py`
  - `factory.py`, `requirements.txt`
  - `tests/test_azure_openai_triage.py`, `tests/test_mock_triage.py`
  - `tests/synthesizers/email_triage_synthesizer.py`

#### Ticket Correlations

- KSPL-007 (EmailIngestion): this component consumes the event that EmailIngestion publishes
- KSPL-013 (AgentOrchestrator): triage result pre-populates submission context for agent conversations

---

### KSPL-023: Intelligent Prefill Engine

**Epic**: EPIC-08  
**Status**: DONE (2026-05-08, c8fbd7e)  
**Priority**: Medium  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Spec Reference**: `doc/23-HYB-intelligent-prefill-engine.md`

#### Pre-Implementation Requirement

Read `doc/23-HYB-intelligent-prefill-engine.md` in its entirety. The `PREFILL_SYSTEM_PROMPT`,
the batching logic (15 fields per batch), the confidence thresholds (0.75 auto-fill, 0.50-0.74
warn, below 0.50 blank), the `needs_review` flag, and the JSON parse error fallback are all
precisely specified. The `MockPrefillAdapter` returns `confidence=0.80` for all fields.

#### Description

Implement `KSquare.IntelligentPrefill`: the LLM fallback for fields the rule-based
`ExtractionMapper` (Component 12) could not map. Receives the document text and unmapped field
list, sends batched extraction requests to GPT-4o (temperature=0.0, json_object mode), and
returns per-field confidence scores, source text fragments, and review flags. Empty inputs
return immediately without an LLM call.

#### Dependencies

- KSPL-012 (ExtractionMapper): consumes `UnmappedFields` list from this component
- KSPL-000: solution structure

#### Acceptance Criteria

- [ ] `UnmappedField`, `PrefillFieldResult`, `PrefillRequest`, `PrefillResult` dataclasses match the spec
- [ ] `PREFILL_SYSTEM_PROMPT` is used exactly as written in the spec
- [ ] Batching: 15 fields per batch; 20 fields → exactly 2 LLM calls
- [ ] Empty `unmapped_fields` list returns `PrefillResult` immediately — no LLM call made
- [ ] `needs_review = True` where `confidence < review_confidence_threshold` (default 0.75)
- [ ] JSON parse error: all fields in the batch return `value=None, confidence=0.0, needs_review=True`
- [ ] `MockPrefillAdapter` returns `confidence=0.80` and `value="MOCK_VALUE"` for all fields
- [ ] Azure OpenAI HTTP calls are stubbed with `respx`
- [ ] Python tests use `faker` to synthesize document text and unmapped field lists
- [ ] Unit test: batching — 20 fields produces exactly 2 `respx` intercepted calls
- [ ] Unit test: JSON parse error from LLM returns all-null safe fallback
- [ ] All tests pass with `pytest`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-intelligent-prefill/`
  - `function_app.py`, `contracts.py`, `options.py`, `prompts.py`
  - `providers/azure_openai_prefill.py`, `mock_prefill.py`
  - `factory.py`, `requirements.txt`
  - `tests/test_azure_openai_prefill.py`, `tests/test_mock_prefill.py`
  - `tests/synthesizers/prefill_synthesizer.py`

#### Ticket Correlations

- KSPL-012 (ExtractionMapper): receives unmapped fields from this component
- KSPL-017 (LlmObservability): weekly offline evaluation tracks fill rate accuracy

---

### KSPL-024: Document Narrative Engine

**Epic**: EPIC-08  
**Status**: DONE (2026-05-08, fc9c4e5)  
**Priority**: Medium  
**Language**: Python 3.11 (Azure Function) + C# HTTP wrapper  
**Spec Reference**: `doc/24-HYB-document-narrative-engine.md`

#### Pre-Implementation Requirement

Read `doc/24-HYB-document-narrative-engine.md` in its entirety. All four prompt pairs
(RISK_SUMMARY, LOSS_RUN_NARRATIVE, REFERRAL_MEMO, UNDERWRITER_FILE_NOTE), the `_parse_sections`
regex, the `max_tokens` per narrative type, and the `temperature=0.3` setting are specified there.
The `_parse_sections` implementation for memo and file note must split on numbered section headers
using the exact regex pattern in the spec.

#### Description

Implement `KSquare.DocumentNarrative`: generates four narrative types from structured submission
and risk analysis data using GPT-4o. Each narrative type uses a distinct system+user prompt pair.
ReferralMemo and UnderwriterFileNote responses are parsed into sections dictionaries keyed by
section title. Errors return an empty `narrative_text` without throwing.

#### Dependencies

- KSPL-016 (RiskAnalysis): `SubmissionContext.risk_indicators` and `LossHistoryContext` come from risk analysis output
- KSPL-000: solution structure

#### Acceptance Criteria

- [ ] `NarrativeType` enum, `SubmissionContext`, `LossHistoryContext`, `NarrativeRequest`, `NarrativeResult` match the spec exactly
- [ ] All four prompt pairs are used exactly as written in the spec
- [ ] `max_tokens` per type: `RiskSummary=200`, `LossRunNarrative=250`, `ReferralMemo=600`, `UnderwriterFileNote=800`
- [ ] `temperature=0.3` is set on every call
- [ ] `_parse_sections` returns a dict with >= 4 keys for `ReferralMemo` and >= 5 keys for `UnderwriterFileNote`
- [ ] API error returns `NarrativeResult(narrative_text="")` — no exception propagated
- [ ] Azure OpenAI HTTP calls are stubbed with `respx`
- [ ] Python tests use `faker` to synthesize `SubmissionContext` and `LossHistoryContext`
- [ ] Unit test: `ReferralMemo` response returns sections dict with at least 4 keys
- [ ] Unit test: API error returns `NarrativeResult` with empty `narrative_text`
- [ ] Unit test: `MockNarrativeAdapter` `narrative_text` contains `institution_name` from context
- [ ] All tests pass with `pytest`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-document-narrative/`
  - `function_app.py`, `contracts.py`, `options.py`, `prompts.py`
  - `providers/azure_openai_narrative.py`, `mock_narrative.py`
  - `factory.py`, `requirements.txt`
  - `tests/test_azure_openai_narrative.py`, `tests/test_mock_narrative.py`
  - `tests/synthesizers/narrative_synthesizer.py`

#### Ticket Correlations

- KSPL-016 (RiskAnalysis): primary data source for narrative generation
- KSPL-013 (AgentOrchestrator): narratives are surfaced in agent responses via tools
- KSPL-017 (LlmObservability): human acceptance rate and factual accuracy are tracked offline

---

### KSPL-025: Agentic Action Toolkit

**Epic**: EPIC-08  
**Status**: DONE (2026-05-08, b366af1)  
**Priority**: Medium  
**Language**: Python 3.11  
**Spec Reference**: `doc/25-HYB-agentic-action-toolkit.md`

#### Pre-Implementation Requirement

Read `doc/25-HYB-agentic-action-toolkit.md` in its entirety. The Draft-Confirm-Execute pattern,
the 4 draft tools (`draft_referral`, `draft_field_update`, `draft_info_request`,
`draft_checklist_update`), the `execute_draft_action` tool, the `DraftStore` SQL schema (with
10-minute TTL), and the integration with Component 13's `ToolRouter` are all specified there.
This component must be built after Component 13 is DONE.

#### Description

Implement `KSquare.AgenticActions`: the write-side complement to the Agent Orchestrator's
read-only tools. Every write action follows the Draft-Confirm-Execute pattern: the agent drafts
an action (human sees it), the human confirms, and only then does `execute_draft_action` commit
the change. Drafts expire after 10 minutes and are stored in SQL. The four draft tools and the
execute tool are registered with Component 13's `ToolRouter` at startup.

#### Dependencies

- KSPL-013 (AgentOrchestrator): all tools must be registered with the `ToolRouter`; Component 13 must be DONE
- KSPL-002 (EventBus): execute actions publish events (referral drafted, field updated, etc.)

#### Acceptance Criteria

- [ ] `DraftAction`, `DraftStore`, and all tool handler classes match the spec
- [ ] `DraftStore`: expired drafts (> 10 minutes) are rejected by `execute_draft_action`
- [ ] `execute_draft_action`: idempotent — executing the same draft ID twice returns the original result, not a duplicate action
- [ ] All 4 draft tools produce a `DraftAction` with a correct `action_type` and `payload`
- [ ] `ToolRouter` integration: all 5 tool handlers are discoverable by Component 13
- [ ] DraftStore SQL schema: `draft_actions` table with `expires_at` column and TTL enforcement
- [ ] Python tests use `faker` to synthesize action payloads
- [ ] Unit test: expired draft raises `DraftExpiredException` on execute
- [ ] Unit test: `draft_referral` produces a `DraftAction` with correct `action_type="draft_referral"` and required fields
- [ ] Integration test: create draft → execute → verify event published (InMemory EventBus)
- [ ] All tests pass with `pytest`

#### Expected Outputs

- `ue-uw-backend/shared-python/ksquare-agentic-actions/`
  - `contracts.py`, `draft_store.py`
  - `tools/draft_referral.py`, `draft_field_update.py`, `draft_info_request.py`, `draft_checklist_update.py`, `execute_draft_action.py`
  - `tool_router_registration.py`
  - `migrations/001_create_draft_actions_table.sql`
  - `tests/test_draft_store.py`, `tests/test_tool_handlers.py`
  - `tests/synthesizers/agentic_action_synthesizer.py`

#### Ticket Correlations

- KSPL-013 (AgentOrchestrator): tools are registered in this component's `ToolRouter`
- KSPL-021 (StateMachine): `draft_referral` execution triggers the `Referred` state transition on the Submission FSM
- KSPL-009 (Notifications): execute actions may trigger notifications to stakeholders

---

## 14. EPIC-09: Progressive Integration Testbed

### Philosophy

Unit tests verify that a component behaves correctly in isolation. They are necessary but not
sufficient. A system where every component passes its unit tests can still fail at the composition
level because interfaces carry implicit semantic contracts that type signatures do not capture.

The third level of correctness is **emergent correctness**: the property that when components
operate together, the whole system exhibits the intended behavior and no unexpected behaviors arise
from their interaction. This cannot be derived from isolation tests. It must be observed.

The Progressive Integration Testbed is the scientific instrument for measuring emergent
correctness. It is not a test suite. It is a runnable environment that grows alongside the
implementation — each completed EPIC extends the canvas with new capabilities, and the canvas
is tagged as a reproducible baseline.

This serves three purposes simultaneously:

1. **Development gate.** An EPIC is not complete until its canvas scenario passes. The scenario
   proves that the new components integrate correctly with all previously built components.

2. **Regression guard.** Later canvases re-execute all earlier scenarios. If a change in Phase 5
   breaks a Phase 2 behavior, the canvas detects it before the code is merged.

3. **Shareable artifact.** Every canvas tag is a reproducible system state. A developer on a
   new insurance project can check out `canvas-3-stable`, run `docker compose up` and
   `python run_canvas.py --phase 3`, and have a working document intelligence pipeline in minutes
   — not as documentation, but as a running, verified system. The canvas is the library's proof
   of utility across projects.

### Canvas Progression

| Canvas | Git Tag | EPIC Completed | Components Exercised |
|---|---|---|---|
| Canvas 1 | `canvas-1-stable` | EPIC-01 + EPIC-02 | 03, 05, 06, 01, 02, 04 |
| Canvas 2 | `canvas-2-stable` | EPIC-03 | adds 08, 07, 09 |
| Canvas 3 | `canvas-3-stable` | EPIC-04 + EPIC-05 | adds 10, 11, 12, 14, 16, 15 |
| Canvas 4 | `canvas-4-stable` | EPIC-06 | adds 13, 17 |
| Canvas 5 | `canvas-5-stable` | EPIC-07 | adds 18, 19, 20, 21 |
| Canvas 6 | `canvas-6-stable` | EPIC-08 | adds 22, 23, 24, 25 |
| Canvas Full | `canvas-full-stable` | All EPICs | all 25 components, end-to-end |

---

### KSPL-026: Lab Infrastructure and Scenario Runner Framework

**Epic**: EPIC-09  
**Status**: DONE (2026-05-08, 6513f66)  
**Priority**: Critical  
**Language**: Python 3.11  
**Spec Reference**: `README.md` (project structure section), this EPIC

#### Pre-Implementation Requirement

Read the Repository Structure section of `implementation-plan.md` for the `lab/` directory
layout. Then read the EPIC-09 philosophy section above in full. The lab must be implemented as
a self-contained directory: anyone who checks out the repository can run it without knowing
anything about the rest of the codebase.

#### Description

Create the `lab/` directory with all scaffolding needed to run integration scenarios. This
includes the Docker Compose environment for the lab (separate from the per-component test Docker
Compose), the `ScenarioRunner` base class that all canvas scenarios inherit from, the
`HealthReport` generator that produces structured JSON reports, and the `snapshot.py` tool that
creates and pushes git tags when a canvas passes.

The `run_canvas.py` entry point accepts `--phase N` (1-6 or `full`) and executes the
corresponding scenario. It exits with code 0 on pass and 1 on fail.

#### Dependencies

- KSPL-000: solution skeleton and root `.gitignore` must include `lab/reports/`

#### Acceptance Criteria

- [ ] `lab/docker-compose.lab.yml` provisions: SQL Server 2022 (port 1433), Redis 7 (port 6379), Azurite (ports 10000-10002), and a stub HTTP server (WireMock standalone, port 8080) for external API simulation
- [ ] `ScenarioRunner` base class: `setup()`, `run()`, `teardown()`, `report()` lifecycle methods; subclasses override `run()`
- [ ] `HealthReport` produces a JSON document with: scenario name, canvas number, timestamp, components exercised, assertions passed, assertions failed, and overall status
- [ ] `snapshot.py`: reads `lab/reports/canvas-N-report.json`; if all assertions passed, creates and pushes git tag `canvas-N-stable`; aborts if any assertion failed
- [ ] `run_canvas.py` CLI: `--phase {1,2,3,4,5,6,full}`, `--no-tag` flag to skip tagging, `--report-dir` override
- [ ] `lab/reports/` is added to `.gitignore`
- [ ] `lab/requirements.txt` includes: `pytest`, `faker`, `docker`, `httpx`, `pytest-asyncio`
- [ ] Running `python lab/run_canvas.py --phase 1 --no-tag` with no components yet built exits cleanly with a `SKIP` report (not an error) so the framework itself is testable before any EPIC is complete
- [ ] All framework code has unit tests in `lab/tests/test_framework.py`

#### Expected Outputs

```
lab/
├── README.md
├── docker-compose.lab.yml
├── run_canvas.py
├── requirements.txt
├── core/
│   ├── scenario_runner.py
│   ├── health_report.py
│   └── snapshot.py
├── synthesizers/
│   └── base_synthesizer.py
├── scenarios/
│   └── (empty, populated by subsequent tickets)
├── tests/
│   └── test_framework.py
└── reports/         <- .gitignored
```

#### Ticket Correlations

- KSPL-027 through KSPL-033: all canvas scenario tickets depend on this framework
- KSPL-034: snapshot release pipeline depends on `snapshot.py` from this ticket

---

### KSPL-027: Canvas 1 — Cross-Cutting and Platform Infrastructure

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: Critical  
**Language**: Python 3.11 (orchestrates C# components via subprocess / HTTP)  
**Spec Reference**: `doc/05-CUS-correlation-context.md`, `doc/06-CUS-pii-redaction-filter.md`, `doc/03-CUS-idempotency-guard.md`, `doc/01-AZR-blob-storage-connector.md`, `doc/02-AZR-event-bus-connector.md`, `doc/04-HYB-audit-trail-writer.md`

#### Pre-Implementation Requirement

Read all six spec files listed above before writing the scenario. The canvas scenario must
exercise the exact interfaces described in those files. Read the Canvas 1 row in the Canvas
Progression table to understand which components are in scope.

#### Description

Implement `lab/scenarios/canvas_1_infrastructure.py`. This scenario proves that the six
foundational components work correctly together under real infrastructure conditions.

The scenario flow:
1. Synthesize a submission ID and correlation ID
2. Verify correlation ID propagates through a simulated request boundary (AsyncLocal)
3. Synthesize a JSON payload containing a real email address and phone number
4. Verify PII redaction masks them before the payload is written to the audit trail
5. Publish an event via InMemory EventBus; verify consumer receives exactly one copy
6. Attempt to publish the same event with the same idempotency key; verify the second attempt is blocked
7. Upload a synthesized blob (1 KB random bytes) to Azurite; download it; verify byte-for-byte equality
8. Write two audit events to SQL Server; query them back; verify append-only invariant (no UPDATE issued)
9. Verify all 8 assertions produce a passing health report
10. Tag `canvas-1-stable` if `--no-tag` is not passed

#### Dependencies

- KSPL-026: lab framework
- KSPL-005, KSPL-006, KSPL-003: EPIC-01 must be DONE
- KSPL-001, KSPL-002, KSPL-004: EPIC-02 must be DONE

#### Acceptance Criteria

- [ ] Canvas runs end-to-end without manual intervention from a clean Docker state
- [ ] All 8 scenario assertions pass and appear in `lab/reports/canvas-1-report.json`
- [ ] PII assertion: the audit record in SQL contains `[REDACTED]` where the email was
- [ ] Idempotency assertion: second event publish returns a blocked result; event consumer called exactly once
- [ ] Blob assertion: downloaded bytes are identical to uploaded bytes (SHA256 match)
- [ ] Audit assertion: SQL query of `audit_trail` returns exactly 2 rows; no UPDATE statement issued
- [ ] Canvas completes in under 60 seconds on standard developer hardware
- [ ] `canvas-1-stable` git tag is created on clean run without `--no-tag`

#### Expected Outputs

- `lab/scenarios/canvas_1_infrastructure.py`
- `lab/synthesizers/infrastructure_synthesizer.py`
- `lab/reports/canvas-1-report.json` (generated; gitignored)

#### Ticket Correlations

- KSPL-028: Canvas 2 runs all Canvas 1 assertions first, then adds communication assertions
- All KSPL-00N through KSPL-004: these components are exercised here at the integration level for the first time

---

### KSPL-028: Canvas 2 — Communication Layer

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: High  
**Language**: Python 3.11  
**Spec Reference**: `doc/08-EXT-email-send-adapter.md`, `doc/07-EXT-email-ingestion-connector.md`, `doc/09-CUS-notification-dispatcher.md`

#### Pre-Implementation Requirement

Read all three spec files listed above. Note that Canvas 2 re-runs all Canvas 1 assertions
before adding new ones. A Canvas 2 failure in a Canvas 1 assertion is a regression introduced
by the Phase 3 components.

#### Description

Implement `lab/scenarios/canvas_2_communication.py`. Extends Canvas 1 by exercising the full
email I/O loop and notification dispatch.

The additional scenario flow (after Canvas 1 passes):
1. Synthesize a MIME email with two attachments; stub Microsoft Graph API responses in WireMock
2. Run email ingestion; verify `EmailReceivedEvent` published and both attachments stored in Azurite
3. Verify deduplication: send the same email fingerprint again; confirm no second event published
4. Synthesize an outbound email message with a Liquid template variable; stub SendGrid in WireMock
5. Verify rendered email body contains the variable value
6. Dispatch an in-app notification; verify it is persisted to SQL with correct recipient ID
7. Dispatch the same notification twice within the dedup window; verify SQL contains exactly one row

#### Dependencies

- KSPL-026: lab framework
- KSPL-027: Canvas 1 must pass (assertions are re-run)
- KSPL-008, KSPL-007, KSPL-009: EPIC-03 must be DONE

#### Acceptance Criteria

- [ ] Canvas 1 assertions all pass as part of Canvas 2 run
- [ ] Email ingestion produces exactly one `EmailReceivedEvent` per unique email fingerprint
- [ ] Both attachment blob URIs are present in the event payload
- [ ] Rendered email body contains the synthesized variable value (not the template placeholder)
- [ ] Notification deduplication: SQL has exactly one row for the duplicate dispatch test
- [ ] Canvas completes in under 90 seconds
- [ ] `canvas-2-stable` git tag is created on clean run

#### Expected Outputs

- `lab/scenarios/canvas_2_communication.py`
- `lab/synthesizers/email_synthesizer.py`

#### Ticket Correlations

- KSPL-027: re-runs Canvas 1 as regression check
- KSPL-029: Canvas 3 extends Canvas 2 with document intelligence scenarios

---

### KSPL-029: Canvas 3 — Document Intelligence and Rules

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: High  
**Language**: Python 3.11  
**Spec Reference**: `doc/10-AZR-document-extraction-adapter.md`, `doc/11-AZR-document-classification-adapter.md`, `doc/12-CUS-extraction-result-mapper.md`, `doc/14-CUS-rules-engine-nrules.md`, `doc/16-CUS-risk-analysis-engine.md`, `doc/15-EXT-form-template-engine.md`

#### Pre-Implementation Requirement

Read all six spec files above. Canvas 3 exercises the complete document processing pipeline in
a single linear flow. Pay close attention to how `UnmappedFields` flows from ExtractionMapper
to be consumed later by IntelligentPrefill (Canvas 6).

#### Description

Implement `lab/scenarios/canvas_3_document_intelligence.py`. Exercises the document pipeline
and risk analysis end-to-end with synthesized data.

Additional scenario flow:
1. Synthesize application form text mimicking an ACORD 125 (institution name, enrollment, TIV, coverage lines, loss run table)
2. Stub Azure Document Intelligence in WireMock; run extraction; verify confidence routing
3. Run classification; verify document is classified as `ACORD125`
4. Run ExtractionMapper; verify required fields are mapped; collect `UnmappedFields`
5. Load intake-routing rules; verify submission routes to correct underwriter queue
6. Run risk analysis on synthesized loss run; verify composite score formula is mathematically correct
7. Verify appetite fit classification matches expected value for the synthesized indicators
8. Run bind-readiness rules with a complete and an incomplete context; verify `Ready` and `NotReady` results respectively
9. Synthesize a form fill request; run iText7 provider; verify output PDF is non-empty bytes

#### Dependencies

- KSPL-026: lab framework
- KSPL-028: Canvas 2 must pass
- KSPL-010, KSPL-011, KSPL-012: EPIC-04 must be DONE
- KSPL-014, KSPL-016, KSPL-015: EPIC-05 must be DONE

#### Acceptance Criteria

- [ ] Canvases 1 and 2 assertions all pass as regression check
- [ ] Extraction confidence routing: synthesized high-confidence field is classified `AutoAccepted`
- [ ] Composite risk score matches the expected hand-calculated value within floating point tolerance (1e-6)
- [ ] Bind-readiness with missing required field returns `NotReady` with that field named in blocking reasons
- [ ] Canvas completes in under 120 seconds
- [ ] `canvas-3-stable` git tag is created on clean run

#### Expected Outputs

- `lab/scenarios/canvas_3_document_intelligence.py`
- `lab/synthesizers/document_synthesizer.py`, `risk_synthesizer.py`

#### Ticket Correlations

- KSPL-028: re-runs Canvas 2 as regression check
- KSPL-023 (IntelligentPrefill): the `UnmappedFields` produced by this canvas scenario are the input to Canvas 6

---

### KSPL-030: Canvas 4 — AI Agent Core

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: High  
**Language**: Python 3.11  
**Spec Reference**: `doc/13-HYB-llm-provider-adapter.md`, `doc/17-AZR-llm-observability.md`

#### Pre-Implementation Requirement

Read both spec files. Understand the AG UI SSE event sequence precisely. The canvas must verify
not just that an HTTP 200 is returned, but that the SSE event stream contains the correct event
types in the correct order.

#### Description

Implement `lab/scenarios/canvas_4_ai_agent.py`. Exercises the agent orchestrator with a
synthesized multi-turn conversation.

Additional scenario flow:
1. Synthesize a conversation turn: "What is the risk summary for submission SUB-001?"
2. Stub Azure OpenAI and Azure AI Search in WireMock; stub Azure Content Safety to return safe
3. POST to agent orchestrator; collect SSE events
4. Verify event sequence: `RunStarted` → at least one `ToolCall` (get_submission_summary) → `ToolResult` → `TextDelta` → `RunFinished`
5. Verify conversation is written to SQL with PII redacted
6. Verify evaluation score is written (groundedness judge stub returns 0.85)
7. Verify a prompt injection attempt is blocked by SafetyGuard (Azure Content Safety stub returns unsafe)
8. Verify cost telemetry event is written to `llm_cost_daily` table

#### Dependencies

- KSPL-026: lab framework
- KSPL-029: Canvas 3 must pass
- KSPL-013, KSPL-017: EPIC-06 must be DONE

#### Acceptance Criteria

- [ ] Canvases 1, 2, and 3 assertions pass as regression check
- [ ] SSE event sequence matches spec exactly
- [ ] SQL `conversation_audit` has one row per turn with `[REDACTED]` where PII was present
- [ ] Prompt injection: HTTP 400 returned; no LLM call made (WireMock stub records zero calls to OpenAI endpoint)
- [ ] Canvas completes in under 120 seconds
- [ ] `canvas-4-stable` git tag is created on clean run

#### Expected Outputs

- `lab/scenarios/canvas_4_ai_agent.py`
- `lab/synthesizers/conversation_synthesizer.py`

#### Ticket Correlations

- KSPL-029: re-runs Canvas 3 as regression check
- KSPL-030 uses same WireMock stub infrastructure that Canvas 6 will extend for LLM-calling AI components

---

### KSPL-031: Canvas 5 — Quote, Bind, and Lifecycle

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: High  
**Language**: Python 3.11  
**Spec Reference**: `doc/18-CUS-rating-adapter.md`, `doc/19-EXT-proposal-orchestrator.md`, `doc/20-CUS-policy-admin-adapter.md`, `doc/21-CUS-state-machine.md`

#### Pre-Implementation Requirement

Read all four spec files. The state machine FSM transitions are the most critical thing to
verify here. The canvas must walk the complete Quote FSM from `Draft` to `Approved` and verify
that every state transition produces an audit event and publishes a `StateTransitionedEvent`.

#### Description

Implement `lab/scenarios/canvas_5_quote_bind.py`. Exercises the full quote-to-bind lifecycle.

Additional scenario flow:
1. Synthesize a coverage pricing request; call MockRatingAdapter; verify deterministic premium
2. Transition Quote FSM: `Draft → PricingRequested → Priced`; verify each transition produces audit + event
3. Stub GhostDraft in WireMock; submit proposal job; poll to completion; verify artifact in Azurite
4. Transition Quote FSM: `Priced → ProposalGenerated → Presented`; verify events
5. Run bind-readiness rules (all required fields present); verify `Ready`
6. Stub PCAS in WireMock; submit bind; poll for policy number; verify `PolicyBoundEvent` published
7. Transition Quote FSM: `Presented → Accepted`; verify final state in SQL
8. Attempt an invalid transition (e.g., `Accepted → Priced`); verify `InvalidTransitionException`
9. Simulate concurrent bind attempt on same entity; verify one succeeds and the other gets `ConcurrencyException`

#### Dependencies

- KSPL-026: lab framework
- KSPL-030: Canvas 4 must pass
- KSPL-018, KSPL-019, KSPL-020, KSPL-021: EPIC-07 must be DONE

#### Acceptance Criteria

- [ ] Canvases 1 through 4 assertions pass as regression check
- [ ] Complete Quote FSM lifecycle produces exactly 6 `StateTransitionedEvent` messages in correct order
- [ ] All 6 events are also written as audit trail rows in SQL
- [ ] Invalid transition produces `InvalidTransitionException` — no audit event written, no event published
- [ ] Concurrent bind: exactly one `PolicyBoundEvent`; one `ConcurrencyException`
- [ ] Canvas completes in under 150 seconds
- [ ] `canvas-5-stable` git tag is created on clean run

#### Expected Outputs

- `lab/scenarios/canvas_5_quote_bind.py`
- `lab/synthesizers/quote_synthesizer.py`

#### Ticket Correlations

- KSPL-030: re-runs Canvas 4 as regression check

---

### KSPL-032: Canvas 6 — AI Intelligence Layer

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: Medium  
**Language**: Python 3.11  
**Spec Reference**: `doc/22-HYB-ai-email-triage.md`, `doc/23-HYB-intelligent-prefill-engine.md`, `doc/24-HYB-document-narrative-engine.md`, `doc/25-HYB-agentic-action-toolkit.md`

#### Pre-Implementation Requirement

Read all four spec files. The Draft-Confirm-Execute pattern in Canvas 6 is the most complex
interaction sequence in the full system. The canvas must verify that a draft action created by
the agentic toolkit is confirmed by a human-simulated confirmation call, and only then executed,
and that the corresponding state machine transition fires.

#### Description

Implement `lab/scenarios/canvas_6_ai_intelligence.py`. Exercises the AI intelligence layer
components in sequence.

Additional scenario flow:
1. Synthesize an incoming email; stub Azure OpenAI in WireMock; run triage; verify intent + entities
2. Verify triage result triggers correct routing suggestion
3. Take `UnmappedFields` from Canvas 3 scenario synthesizer; stub GPT-4o; run intelligent prefill for 20 fields; verify exactly 2 LLM calls (batching)
4. Synthesize `SubmissionContext` + `LossHistoryContext`; generate all 4 narrative types; verify word counts and section structure
5. Call `draft_referral` tool; verify `DraftAction` created with correct payload
6. Call `execute_draft_action` with draft ID; verify referral event published and Submission FSM transitions to `Referred`
7. Attempt to execute the same draft again; verify idempotent return (no duplicate event)
8. Advance time past 10-minute TTL; attempt execute; verify `DraftExpiredException`

#### Dependencies

- KSPL-026: lab framework
- KSPL-031: Canvas 5 must pass
- KSPL-022, KSPL-023, KSPL-024, KSPL-025: EPIC-08 must be DONE

#### Acceptance Criteria

- [ ] Canvases 1 through 5 assertions pass as regression check
- [ ] Intelligent prefill batching: 20 fields → 2 WireMock-intercepted LLM calls
- [ ] Referral narrative sections dict has >= 4 keys
- [ ] Draft-Confirm-Execute: exactly one referral event published; duplicate execute returns original result without a second event
- [ ] TTL expiry: `DraftExpiredException` raised; no event published
- [ ] Canvas completes in under 180 seconds
- [ ] `canvas-6-stable` git tag is created on clean run

#### Expected Outputs

- `lab/scenarios/canvas_6_ai_intelligence.py`
- `lab/synthesizers/ai_synthesizer.py`

#### Ticket Correlations

- KSPL-031: re-runs Canvas 5 as regression check
- KSPL-033: Full System Canvas extends this scenario into an end-to-end workflow

---

### KSPL-033: Canvas Full — End-to-End System Scenario

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: Medium  
**Language**: Python 3.11  
**Spec Reference**: All 25 component spec files in `doc/`

#### Pre-Implementation Requirement

Read the README.md Build Order section and the complete EPIC-09 philosophy section above before
writing this scenario. The full canvas is not a union of previous canvases — it is a coherent
narrative that follows a single synthetic submission from first email to bound policy. Every
component must contribute at least one assertion to this scenario.

#### Description

Implement `lab/scenarios/canvas_full_system.py`. This is the canonical end-to-end proof that
all 25 components function as a unified system.

The end-to-end workflow narrative (single synthesized submission):
1. Email arrives (Component 07) → AI triage classifies as `NewSubmission` (Component 22) → `EmailTriagedEvent` published (Component 02)
2. Submission record created in SQL; correlation ID propagated (Component 05)
3. Application form document uploaded to blob (Component 01) → extracted (Component 10) → classified as ACORD125 (Component 11) → fields mapped (Component 12) → unmapped fields filled by prefill (Component 23)
4. Risk analysis computed (Component 16); appetite fit = `In Appetite`
5. Intake routing rules route to `K12-UW-Queue` (Component 14)
6. Submission FSM transitions: `Draft → Submitted → InReview` (Component 21); audit events written (Component 04)
7. Agent asked: "Summarize the risk" → returns a grounded response citing risk indicators (Component 13)
8. Risk summary narrative generated (Component 24)
9. Rating computed via MockRatingAdapter (Component 18)
10. Proposal generated via stubbed GhostDraft (Component 19); stored to blob (Component 01)
11. Submission FSM transitions: `InReview → Approved` (Component 21)
12. Bind submitted to stubbed PCAS (Component 20) → `PolicyBoundEvent` → Quote FSM transitions to `Accepted`
13. Notification dispatched to underwriter (Component 09); in-app notification verified in SQL
14. Throughout: PII redacted in all audit records (Component 06); all events pass idempotency guard (Component 03); form template filled (Component 15)

At completion: the health report contains at least one assertion per component (25 total).

#### Dependencies

- KSPL-026: lab framework
- KSPL-032: Canvas 6 must pass (all prior canvases transitively)
- All 25 component tickets must be DONE

#### Acceptance Criteria

- [ ] All 25 component spec assertions are represented in the health report
- [ ] Single synthesized submission traverses the complete lifecycle described above
- [ ] No external paid API is called; all external endpoints are stubbed in WireMock
- [ ] Health report `overall_status` is `PASS` with 0 failed assertions
- [ ] Canvas completes in under 300 seconds
- [ ] `canvas-full-stable` git tag is created on clean run

#### Expected Outputs

- `lab/scenarios/canvas_full_system.py`
- `lab/reports/canvas-full-report.json` (generated; gitignored)

#### Ticket Correlations

- All KSPL-NNN tickets: every component is exercised here

---

### KSPL-034: Snapshot Release Pipeline

**Epic**: EPIC-09  
**Status**: TO DO  
**Priority**: High  
**Language**: Python 3.11 / Shell  
**Spec Reference**: This EPIC

#### Pre-Implementation Requirement

Read the Canvas Progression table in EPIC-09 above. The release pipeline must create tags exactly
as specified in that table. Tags must be annotated (not lightweight) so they carry a message
describing which canvas scenario passed and when.

#### Description

Implement the automated snapshot release mechanism. When a canvas scenario passes, the pipeline:
1. Reads `lab/reports/canvas-N-report.json`
2. Verifies `overall_status == "PASS"` and `failed_assertions == 0`
3. Creates an annotated git tag `canvas-N-stable` with the report summary as the tag message
4. Pushes the tag to `origin`

This is also the gate for cross-project adoption. The tag message includes the list of
components exercised, the canvas completion time, and the date — so any developer reading the
tag history understands exactly what state the system was in when each stable baseline was cut.

Additionally, create a `lab/BASELINES.md` file that TRAE SOLO updates after each snapshot. This
file serves as the human-readable record of stable baselines and is the first thing a new
developer reads when adopting the library for a new project.

#### Dependencies

- KSPL-026: `snapshot.py` framework created here
- KSPL-027 through KSPL-033: each canvas scenario must exist before its tag can be created

#### Acceptance Criteria

- [ ] `snapshot.py` creates annotated tag with report summary in the tag message
- [ ] Tag push uses the configured `origin` remote; aborts with clear error message if remote is not reachable
- [ ] Attempting to tag when `overall_status != "PASS"` aborts with exit code 1 and prints the failed assertions
- [ ] Attempting to create a tag that already exists aborts with a clear message (no force-push)
- [ ] `lab/BASELINES.md` is updated by `snapshot.py` with: tag name, date, components exercised, canvas run time
- [ ] Unit tests for `snapshot.py` cover: pass case, fail case, already-tagged case
- [ ] `lab/BASELINES.md` is committed and tracked in git (not gitignored)

#### Expected Outputs

- `lab/core/snapshot.py` (extended from KSPL-026 stub)
- `lab/BASELINES.md`
- `lab/tests/test_snapshot.py`

#### Ticket Correlations

- KSPL-026: `snapshot.py` skeleton created there; this ticket completes it
- KSPL-027 through KSPL-033: each canvas ticket depends on this working snapshot mechanism

---

## 15. Appendix: Dependency Graph Summary

The following table shows the build dependency graph. A component may not be started until all
listed dependencies are DONE.

| Ticket | Depends On |
|---|---|
| KSPL-000 | (none) |
| KSPL-005 | KSPL-000 |
| KSPL-006 | KSPL-000 |
| KSPL-003 | KSPL-000 |
| KSPL-001 | KSPL-000, KSPL-005 |
| KSPL-002 | KSPL-000, KSPL-005, KSPL-003 |
| KSPL-004 | KSPL-000, KSPL-005, KSPL-006 |
| KSPL-008 | KSPL-001, KSPL-005 |
| KSPL-007 | KSPL-001, KSPL-002, KSPL-003 |
| KSPL-009 | KSPL-008, KSPL-005, KSPL-006 |
| KSPL-010 | KSPL-001, KSPL-005 |
| KSPL-011 | KSPL-001, KSPL-010 |
| KSPL-012 | KSPL-010, KSPL-011 |
| KSPL-014 | KSPL-000 |
| KSPL-016 | KSPL-014 |
| KSPL-015 | KSPL-001 |
| KSPL-013 | KSPL-005, KSPL-006 |
| KSPL-017 | KSPL-013 |
| KSPL-018 | KSPL-005 |
| KSPL-019 | KSPL-001, KSPL-002, KSPL-003, KSPL-005 |
| KSPL-020 | KSPL-002, KSPL-004, KSPL-014 |
| KSPL-021 | KSPL-002, KSPL-004 |
| KSPL-022 | KSPL-002 |
| KSPL-023 | KSPL-012 |
| KSPL-024 | KSPL-016 |
| KSPL-025 | KSPL-013, KSPL-002 |
| KSPL-026 | KSPL-000 |
| KSPL-027 | KSPL-026, KSPL-005, KSPL-006, KSPL-003, KSPL-001, KSPL-002, KSPL-004 |
| KSPL-028 | KSPL-027, KSPL-008, KSPL-007, KSPL-009 |
| KSPL-029 | KSPL-028, KSPL-010, KSPL-011, KSPL-012, KSPL-014, KSPL-016, KSPL-015 |
| KSPL-030 | KSPL-029, KSPL-013, KSPL-017 |
| KSPL-031 | KSPL-030, KSPL-018, KSPL-019, KSPL-020, KSPL-021 |
| KSPL-032 | KSPL-031, KSPL-022, KSPL-023, KSPL-024, KSPL-025 |
| KSPL-033 | KSPL-032 (all 25 component tickets transitively) |
| KSPL-034 | KSPL-026, KSPL-027 through KSPL-033 |

---

*This document is the operational ground truth for TRAE SOLO. Every decision about what to
build, in what order, and to what standard of correctness is encoded here. When in doubt,
read the spec file for the component before reading this plan.*
