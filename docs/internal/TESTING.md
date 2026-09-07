ABOUTME: Test strategy, framework conventions, host profiles, and per-project roles for TUnit-based testing.
ABOUTME: Covers 3 host profiles, fixture model, builders/seeds, database lifecycle, and CI integration.

# Testing

> **Audience:** Contributors | Architects | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-03
> **Source Anchors:** `tests/`, `eng/agent-workflow/tests/`, `eng/release/tests/`, `.github/workflows/`

## Codebase Test Metrics & Guardrail Scale (2026-09-03 Checkpoint)

| Metric / Dimension | Value as of 2026-09-03 | Architectural Context |
|---|---|---|
| **Total Test Scale** | **32 projects / 1,783 test files** | **455,176 total test lines of code** |
| **Backend Test LOC** | **338,497 LOC** | Pure backend test surface (excludes UI/client tests) |
| **Backend Test:Code Ratio** | **≈ 0.68** (338.5k test / 500.8k prod) | Exceptionally dense test coverage across all layers |
| **API Integration Tests** | 347 files / 93.8k LOC | `Event.API.IntegrationTests` (full stack HTTP, auth, HATEOAS) |
| **Persistence Integration Tests** | 239 files / 90.6k LOC | `Event.Persistence.IntegrationTests` (real engine providers) |
| **Application Unit Tests** | 282 files / 52.1k LOC | `Event.Application.UnitTests` (CQRS handlers, validation, mapping) |
| **Infrastructure Tests** | 191 files / 54.0k LOC | `Explore.Infrastructure.Tests` (external adapters, storage, auth fallback) |
| **Domain Unit Tests** | 125 files / 25.1k LOC | `Event.Domain.UnitTests` (pure domain logic, lifecycle rules) |
| **Architecture Rule Tests** | **89 files / 22,792 LOC** | `Event.Architecture.Tests` (NetArchTest compile/structure guardrails) |
| **CI/CD Security Workflows** | 21 GitHub Actions workflows | CodeQL, Scorecard, secret-scanning, dep-review, contract checks |
| **Supply Chain Posture** | Locked NuGet Restore | Commit SHA-pinned actions, `RestoreLockedMode`, 150 pinned packages |
| **Code Hygiene** | **0 TODO / 0 FIXME / 0 HACK** | 0 `[Obsolete]`, 0 `NotImplementedException`, ~95% ABOUTME coverage |

---

## Framework

The project uses [TUnit](https://github.com/thomhurst/TUnit) — a modern, fast, parallel .NET testing framework.

Key TUnit features used:

| Feature | Usage |
|---------|-------|
| `[Test]` | Test method marker |
| `[Before(Test)]` / `[After(Test)]` | Per-test setup/teardown |
| `[Before(Class)]` / `[After(Class)]` | Per-class lifecycle hooks |
| `[NotInParallel]` | Serialize tests sharing resources |
| `Assert.That(x).IsEqualTo(y)` | Fluent async assertions |
| `Assert.Multiple()` | Group multiple assertions |

## The 3-Ring Progressive Verification Model

To eliminate the 50% test diagnosis and 15% container troubleshooting bottleneck during agentic and contributor workflows, the repository strictly enforces a **3-Ring Progressive Verification** hierarchy:

| Ring | Scope & Cadence | Budget | Permitted Suites & Infrastructure | Purpose |
|---|---|---|---|---|
| **Ring 1: Inner Loop** | Subtask level (during active coding) | **< 2s** | In-memory TUnit slicing (`--treenode-filter`) in `Event.Domain.UnitTests` or `Event.Application.UnitTests`. **0 containers, 0 network, 0 DB lag**. | Instant Red/Green validation of business logic, state machines, and invariants. |
| **Ring 2: Phase Exit Gate** | Phase boundary (before phase commit) | **< 15s** | Release build (`dotnet build -c Release -v q`) + at most **one** selected project test against **one canonical provider** (e.g. SQLite in-memory or single PostgreSQL container). | Ensure project-level integrity without matrix delays. |
| **Ring 3: Plan Exit Gate** | Workstream boundary (before PR) | Minutes | Full 5-database matrix (PostgreSQL, SQLite, SQL Server, MySQL), EF Core migrations, and `Event.Architecture.Tests`. | Catch multi-dialect edge cases and architecture drift once before PR submission. |

### Fast-Loop In-Memory Slicing vs Containerized Persistence Testing

- **In-Memory Domain Invariants (Ring 1)**: Pure algorithmic, normalization, validation, and state-machine tests (e.g., Unicode FormC normalization, string trimming, case-folding, regex matching, entity status transitions) MUST run in `Event.Domain.UnitTests` in **< 50ms**. Never write database integration tests to verify pure in-memory business logic.
- **Containerized Integration Tests (Ring 2/3)**: `Event.Persistence.IntegrationTests` and `Event.API.IntegrationTests` are reserved strictly for EF Core mapping annotations, SQL dialect translation, transactions, RLS filters, and HTTP middleware pipelines.

### The Yak-Shaving Quarantine Rule

When working on a feature or bug fix:
1. Agents and contributors are **strictly forbidden** from fixing pre-existing test suite rot or unrelated fixture failures encountered during execution.
2. If an existing test fails outside the task's path, verify whether it reproduces on a clean worktree of `develop`.
3. If pre-existing, document it in `*-context.md` under `Validation Baseline / Pre-Existing Technical Debt` (and optionally `dev/backlog/<slug>.md`), quarantine it, and proceed with the assigned scope.

## Test Projects

Each project has a specific role. Run individually — never use solution-level `dotnet test`. The primary projects are listed below.

| Project | Layer | Role | Requires Infra |
|---------|-------|------|----------------|
| `Event.Domain.UnitTests` | Domain | Entity invariants, value objects, domain logic | No |
| `Event.Application.UnitTests` | Application | Handler logic, validation, mapping | No |
| `Event.Architecture.Tests` | Cross-cutting | Convention enforcement via reflection | No |
| `Explore.GeneratedContracts.Tests` | Tooling | Roslyn transformer classification, reversibility, privacy, and deterministic output | No |
| `Explore.Secrets.UnitTests` | Infrastructure | Secret provider logic, encryption, restart-based credential rotation | No |
| `Explore.Infrastructure.Tests` | Infrastructure | Provider adapters, configuration resolvers, authorization fallback behavior, and focused provider runtime checks | No for `Category!=Runtime`; Docker/Mailpit/RabbitMQ for runtime lanes |
| `Event.Persistence.IntegrationTests` | Persistence | EF Core queries, repository behavior, provider migrations | PostgreSQL plus the real-engine provider matrix |
| `Event.API.IntegrationTests` | API | HTTP endpoints, middleware, auth flows | Full stack |
| `Explore.Blazor.IntegrationTests` | BFF | Middleware pipeline, auth endpoints, delegating handlers | No |
| `Explore.Blazor.Client.Tests` | UI | Component rendering, service behavior | No |
| `Event.Wire.Contracts.UnitTests` | Wire contracts | Parsed/serialized protocol values and redacted bearer semantics | No |
| `Explore.Diagnostic.UnitTests` | Diagnostic tooling | Doctor exit codes, bounded command evidence, and redaction | No |
| `ISLAMU.AgentWorkflow.Tests` | Repository tooling | Intents YAML syntax and literal-file commit guard | No |
| `Event.Standalone.IntegrationTests` | Standalone host | In-process transport, combined host graph, configuration, and API parity | No |
| `Event.SetupAssistant.Terminal.Tests` | Setup terminal | Terminal.Gui adapter, canonical Core output parity, localization, and secret-boundary invariants | No |
| `ISLAMU.ReleaseEngineering.Tests` | Release engineering | Commit/scope policy, canonical artifacts, Git trust, renderer, tag-anchored attestation, provider adapter plans | No (spawns `git`, `ssh-keygen`, and disposable repositories under the temp directory) |
| `Event.Benchmarks` | Benchmarks | Advisory BenchmarkDotNet performance scenarios; build-only in PRs and executed by `performance-smoke.yml` | Optional PostgreSQL only for the dedicated provider benchmark |

### Run Commands

Host-lifetime regressions in `ApiHostLifetimeTests` and
`GracefulShutdownLifetimeTests` distinguish stopped/never-started host disposal
from process-global callback retention using non-inlined helpers and weak
references. `HostProcessSignalSubscriptions` is DI-created through
`AddServiceDefaults`; disposal removes each exact Console/ProcessExit delegate.
Minimal hosts calling the public startup extensions must include those service
defaults. All executable roots lexically dispose their application if startup
fails before `Run`. Callback behavior is unchanged, and detaching an event does
not wait for a callback already dispatched. These focused checks do not replace
full-project acceptance or prove the cause of an entire process-memory failure.

```bash
# Unit tests (no infrastructure needed)
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.GeneratedContracts.Tests/Explore.GeneratedContracts.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.Secrets.UnitTests/Explore.Secrets.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category!=Runtime]" --minimum-expected-tests 1
dotnet test --project tests/Event.Wire.Contracts.UnitTests/Event.Wire.Contracts.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.Diagnostic.UnitTests/Explore.Diagnostic.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Standalone.IntegrationTests/Event.Standalone.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.SetupAssistant.Terminal.Tests/Event.SetupAssistant.Terminal.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project eng/agent-workflow/tests/ISLAMU.AgentWorkflow.Tests/ISLAMU.AgentWorkflow.Tests.csproj --configuration Release --verbosity quiet

# Release engineering (no infrastructure needed — builds disposable Git repositories)
dotnet test --project eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj --configuration Release --verbosity quiet

# Integration tests (requires Docker infrastructure running)
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet

# Runtime provider tests (requires Docker/Testcontainers)
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=RabbitMQ]" --minimum-expected-tests 1

# BFF integration tests (no infrastructure needed — uses WebApplicationFactory with in-memory services)
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --verbosity quiet

# UI tests
dotnet test --project tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet

# Benchmarks are non-gating and run only in the scheduled/manual performance workflow.
dotnet build tests/Event.Benchmarks/Event.Benchmarks.csproj --configuration Release --verbosity quiet

```

### Privacy-erasure DBML schema maintenance

`schemas/islamu-event.md` is a maintained DBML reference. Update its
privacy-erasure lifecycle tables and relationships in the same change as their
EF Core model or migration, then run the focused architecture contract:

```bash
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/PrivacyErasureContractArchitectureTests/*" --minimum-expected-tests 1
```

The contract requires the three lifecycle tables to remain documented; EF Core
migrations and their model snapshot remain the authoritative database shape.

### Event Lifecycle Focused Verification

Use these focused commands when changing nullable event-session scheduling, lifecycle transition endpoints, HAL lifecycle affordances, or generated lifecycle API contracts:

```bash
dotnet build src/Explore.API/Explore.API.csproj --configuration Release --verbosity minimal --no-restore -maxcpucount:1

# Domain rules (2 Event + 8 EventSession tests)
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventLifecycleRulesTests/*" --minimum-expected-tests 2
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventSessionLifecycleRulesTests/*" --minimum-expected-tests 8

# Domain entities (3 Event + 7 EventSession tests)
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventLifecycleTests/*" --minimum-expected-tests 3
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventSessionLifecycleTests/*" --minimum-expected-tests 7

# Application handlers and readiness (10 + 9 + 41 + 14 + 4 tests)
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventLifecycleTransitionCommandHandlerTests/*" --minimum-expected-tests 10
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/PublishEventCommandHandlerTests/*" --minimum-expected-tests 9
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventSessionLifecycleCommandHandlerTests/*" --minimum-expected-tests 41
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventLifecycleReadinessEvaluatorTests/*" --minimum-expected-tests 14
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/GetEventPublishReadinessRequestHandlerTests/*" --minimum-expected-tests 4

# Moderation, unmoderation, and import handlers (9 + 7 + 11 tests)
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/ModerateEventCommandHandlerTests/*" --minimum-expected-tests 9
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/UnmoderateEventCommandHandlerTests/*" --minimum-expected-tests 7
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/ImportEventCommandHandlerTests/*" --minimum-expected-tests 11
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release -- --treenode-filter "/*/*/EventSessionLifecycleCommandHandlerTests/Publish_WhenSessionIsReadyAndParentPublished_TransitionsToPublished" --minimum-expected-tests 1

# API lifecycle surfaces (11 + 16 + 4 + 6 tests)
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EventsControllerTests/*" --minimum-expected-tests 11
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EventSessionControllerTests/*" --minimum-expected-tests 16
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EventLifecycleHateoasPolicyTests/*" --minimum-expected-tests 4
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EventSessionVisibilityContractTests/*" --minimum-expected-tests 6

# Persistence resilience and provider materialization (3 + 3 + 1 + 1 tests)
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release -- --treenode-filter "/*/*/DomainLifecycleArchitectureTests/*" --minimum-expected-tests 3
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EfCoreUnitOfWorkRetryTests/*" --minimum-expected-tests 3 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EventLifecycleStatusMaterializationTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EventLifecycleStatusSqliteMaterializationTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1

# PostgreSQL import, federation, seeding, and session constraints (1 + 6 + 1 + 10 + 4 + 8 tests)
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/ImportEventAmbiguousCommitPersistenceTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/AtprotoInboundEventImportPersistenceTests/JetstreamApply_MaterializesNewEventAndSessionWithApprovedStatusMatrix" --minimum-expected-tests 6 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/AtprotoInboundEventImportPersistenceTests/JetstreamApply_RescheduledRefreshPublishesCancelledSessionBeforeRefreshingSchedule" --minimum-expected-tests 1 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/DatabaseSeederTests/SeedAsync_InDevelopment_RepairsOnlyPublishableSessionStatuses" --minimum-expected-tests 10 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/DatabaseSeederTests/SeedAsync_InDevelopment_PreservesPublishableStatusWhenScheduleIsInvalid" --minimum-expected-tests 4 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EventSessionLifecycleConstraintTests/*" --minimum-expected-tests 8 --maximum-parallel-tests 1

# Pending-model-change contract
Database__Provider=PostgreSql \
Database__Host=localhost \
Database__Port=5432 \
Database__Database=islamu_event_db \
Database__Schema=islamu_event \
Database__TlsMode=Prefer \
Database__TrustServerCertificate=false \
Database__Migrator__Username=explore \
Database__Migrator__Password=explore \
dotnet ef migrations has-pending-model-changes \
  --context ExploreDbContext \
  --project src/Explore.Persistence/Explore.Persistence.csproj \
  --startup-project src/Explore.API/Explore.API.csproj \
  --configuration Release
```

Do not combine lifecycle selectors with `|`: each focused command has its own exact minimum count, so a skipped selector cannot make the command appear green. The SQLite materialization command above is separate from the PostgreSQL round-trip and uses the dedicated `EventLifecycleStatusSqliteMaterializationTests` test.

The governed API-contract commands remain separate from lifecycle selectors:

```bash
dotnet build src/Explore.API/Explore.API.csproj --configuration Release --no-restore --verbosity minimal
dotnet build tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --no-restore --verbosity minimal
dotnet run --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter "/*/*/*/OpenApiDocument_*" --minimum-expected-tests 5 --no-progress
dotnet run --project eng/tools/Explore.ApiContractInventory/Explore.ApiContractInventory.csproj --configuration Release
dotnet build src/Explore.Blazor.Client/Explore.Blazor.Client.csproj --configuration Release --no-restore --verbosity quiet
```

The determinism pass repeats the API build, inventory command, and Blazor client build, then requires zero diff in `schemas/openapi_islamu-event.json`, `docs/API_CONTRACT_INVENTORY.md`, and `src/Explore.Blazor.Client/Clients/EventApiClient.g.cs`.

The solution-level build can be blocked by unrelated Blazor WebAssembly task-host issues on local SDK/tooling states where the WebAssembly workload is not installed. For the pinned .NET SDK `10.0.302`, verify the workload with `dotnet workload list` and install the official ASP.NET Core Blazor WebAssembly prerequisite with `dotnet workload install wasm-tools` when Release builds fail in `ComputeWasmBuildAssets`, `Microsoft.NETCore.App.Runtime.Mono.browser-wasm`, or `Microsoft.NET.Sdk.WebAssembly.Pack` resolution. When the change is API/Application/HAL-only, prefer the API project build plus focused tests above and report any broader build blocker separately instead of weakening lifecycle tests.

### Support Access Focused Verification

Use these focused commands when changing support-access domain/session rules, trusted BFF forwarding, HAL affordances, operator console UX, tenant evidence UX, or support-access docs:

```bash
dotnet build src/Explore.Blazor/Explore.Blazor.csproj --configuration Release --no-restore -clp:ErrorsOnly
dotnet build tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --no-restore -clp:ErrorsOnly
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --no-build --treenode-filter "/*/*/BffSupportAccessEndpointsTests/*|/*/*/SupportAccessForwardingHandlerTests/*|/*/*/BffProxyHeaderSanitizerTests/*" --minimum-expected-tests 1
dotnet test --project tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --no-build --treenode-filter "/*/*/SupportAccessClientServiceTests/*|/*/*/TenantSupportAccessEvidenceSectionTests/*" --minimum-expected-tests 1
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --no-build --treenode-filter "/*/*/SupportAccessApiTests/*" --minimum-expected-tests 1
```

Support-access UI tests must assert affordances from HAL `_links`, not local role or claim checks. The tenant evidence view is intentionally read-only; tests should prove it does not render start or force-stop controls and only renders audit drill-in when the session resource contains an `audit-events` link.

### Test Taxonomy And CI Lanes

Use TUnit metadata to route tests into the smallest lane that proves the behavior. TUnit uses `--treenode-filter` for metadata filtering; do not use VSTest-style `--filter` examples for TUnit projects.

| Lane | TUnit Metadata | Projects | Purpose | Default Frequency |
|------|----------------|----------|---------|-------------------|
| Unit | `[Category("Unit")]` or project-level unit suite excluding `[Category("Runtime")]` | `Event.Domain.UnitTests`, `Event.Application.UnitTests`, `Explore.Secrets.UnitTests`, `Explore.Infrastructure.Tests` | Fast domain, handler, validator, mapping, service, provider, configuration, and fallback behavior | Every PR |
| Architecture | project-level architecture suite | `Event.Architecture.Tests` | Clean Architecture, naming, accessibility structure, authorization parity, and test-suite governance | Every PR |
| Component | `[Category("Component")]` or project-level UI suite | `Explore.Blazor.Client.Tests` | bUnit component, service, accessibility, wrapper, and design-system behavior | Every PR |
| API Contract | `[Category(TestCategories.Fast)]`, `[Category(TestCategories.Security)]`, `[Category(TestCategories.PolicyContract)]` | `Event.API.IntegrationTests` | HTTP serialization, HAL, ProblemDetails, auth matrix, Cerbos contract, and API surface rules | Every PR where possible |
| Real Runtime | `[NotInParallel("RealRuntimeDb")]` / provider fixtures | `Event.Persistence.IntegrationTests`, real-runtime API tests | Provider-specific EF Core, migrations, query filters, tenant isolation, and repository behavior | Merge/nightly |
| Email | `[Category("Email")]` | `Explore.Infrastructure.Tests`, `Event.API.IntegrationTests` | SMTP config, EmailDispatch outbox drain, Mailpit delivery, and operator replay | Deterministic tests in PR; full runtime in nightly/manual |
| RabbitMQ | `[Category("RabbitMQ")]` | `Explore.Infrastructure.Tests`, targeted runtime tests | Optional EmailDispatch pointer transport, topology, publish confirms, consumer settlement, DLQ replay/parking, and broker fixture readiness | Nightly/manual until reliability is proven |
| Runtime | `[Category("Runtime")]` | Provider-backed integration tests | Tests requiring Docker, a relational engine, broker, Mailpit, or Keycloak | Merge/nightly/manual by cost |
| Stress | stress fixture/category | `Event.API.IntegrationTests` | Rate limiting, retry headers, timeout, and high-volume middleware behavior | Nightly/manual |
| BFF Integration | BFF integration suite/categories | `Explore.Blazor.IntegrationTests` | Cookie auth, token refresh, YARP forwarding, tenant hints, and BFF middleware | Every PR for no-infra tests; explicit runtime lane for Keycloak/Redis-backed tests |
| Manual | `[Category("Manual")]` | Runtime and visual suites | Expensive, operator-reviewed, or artifact-heavy checks that should not block the normal PR lane | Manual/approved baseline lane |

Example TUnit filters:

```bash
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Security]"
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category!=Runtime]" --minimum-expected-tests 1
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=RabbitMQ]" --minimum-expected-tests 1
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
```

### Multi-provider database verification

The CI database matrix uses real engines for migration/runtime evidence; the
provider-model tests alone are not portability proof.

| Provider lane | CI-tested engine | CI database TLS | Required structured extras |
|---|---|---|---|
| PostgreSQL | 16.14 | `Disabled`, trust false (isolated CI only) | none |
| SQLite | runner-local persisted file | transport-neutral | single instance, local file, WAL, 30-second busy timeout |
| SQL Server | 2022 CU21 on Ubuntu 22.04 | `Required`, trust true (ephemeral CI certificate only) | none |
| MariaDB | 11.4.12 | `Disabled`, trust false (isolated CI only) | `ServerFlavor=MariaDb`, `ServerVersion=11.4.12` |
| MySQL | 8.4.6 | `Disabled`, trust false (isolated CI only) | `ServerFlavor=MySql`, `ServerVersion=8.4.6` |

These are CI-tested baselines, not a promise that every patch/minor engine
version is supported. Production server deployments use verified TLS; the CI
trust bypass/disabled settings are limited to ephemeral isolated services.

The provider contract also verifies physical namespaces: PostgreSQL and SQL
Server use a configured schema with unprefixed application names, while
SQLite, MariaDB, and MySQL use the fixed `ie_` prefix for application and
migration-history tables. Prefix overrides are rejected.

Every lane must:

1. start with a clean database/file and run `Event.MigrationService`;
2. run it again and prove migration/seeding idempotency;
3. exercise the shared provider behavioral contract for CRUD, tenant filters,
   soft delete, transactions, optimistic concurrency, outbox/idempotency,
   paging, provider locks/conflict classification, and Data Protection;
4. start the minimal runtime surface with `Database:Runtime` credentials;
5. use `HostedService` email dispatch on every non-PostgreSQL provider; and
6. retain provider-specific failure logs without connection strings or secrets.

Architecture tests also prove each non-PostgreSQL application/Data Protection
migration project owns generated migrations and the expected provider package.
Generated files are never patched to make a matrix lane pass.

### Privacy-erasure authority and restore lane

`EmbeddedPrivacyErasureRecoveryTests` uses a dedicated temporary local file,
not the primary database. It proves private-cache/WAL/busy-timeout storage
policy, restrictive permissions and symlink rejection, authority-first commit,
primary-only restore replay convergence, idempotent restart, dry-run/apply
retention, legal-hold pseudonymization/release, atomic floor movement, and
concurrent append/compaction serialization. Configuration
tests separately prove the one-writer and local-path bounds.

`ExternalDatabasePrivacyErasureAuthorityTests` and
`ExternalDatabasePrivacyErasureRestoreTests` use an explicit application
container plus a distinct authority container. They prove function-only
runtime ACLs, fresh-context concurrent allocation, and the real application-only restore path.
The authority class also proves state/evaluate execution and compact denial
under the runtime role, migrator-only hold-aware atomic floor advancement, and
denial of direct table deletion.
The restore fixture applies application migrations, seeds PII, executes
`pg_dump --format=custom` inside the application container, creates a unique
fixture-owned database from `template0`, and runs `pg_restore --exit-on-error`
without `--clean`. The test observes PII in the restored database before
replay, then verifies re-erasure, one local mirror/checkpoint/outbox convergence,
repeat idempotency, and an exact-field-equivalent authority fact snapshot.
The fixture disposes only its own Testcontainers; it never drops an operator or
user database, volume, container, or backup. This is the proven external application-only restore drill, and the authority database remains untouched throughout it.

The `ExternalDatabase` lane uses distinct primary and authority PostgreSQL
containers and preserves the existing function-only ACL and application-only
restore proof. Run all three focused selectors with a nonzero count; topology
tests remain serialized because they share restore fixtures:

```bash
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/EmbeddedPrivacyErasureRecoveryTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/ExternalDatabasePrivacyErasureAuthorityTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release -- --treenode-filter "/*/*/ExternalDatabasePrivacyErasureRestoreTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1
```

`Explore.Infrastructure.Tests` intentionally has both fast no-infrastructure tests and Docker-backed runtime tests. Docker-backed classes are `[Explicit]`, so an unfiltered developer run stays fast; a positive `Runtime`, `Email`, or `RabbitMQ` category filter opts into the matching container lane. Use `Category!=Runtime` for the fast local/PR lane. Use the `Email` category when changing SMTP or EmailDispatch behavior; it includes no-container configuration failure tests, Mailpit-backed SMTP/drain tests, and RabbitMQ consumer tests that deliver to Mailpit. Use the `RabbitMQ` category when changing optional broker transport, topology, publish-confirm, consumer settlement, DLQ replay, or parking behavior.

Credential rotation coverage is restart-based today: the owning service reloads on restart or redeploy, not through live in-process refresh.

### Disabled Test Governance

Disabled tests are allowed only when the test still expresses required future behavior and cannot run in the current lane. Do not comment out `[Test]`; either keep the test active, mark it with `[Skip("Category: ... Removal: ...")]`, or delete it when the behavior is obsolete or unnecessary.

Use `[Explicit]` for valid, intentionally opt-in runtime or release-rehearsal tests whose infrastructure or duration makes them unsuitable for an unfiltered developer run. Keep a positive category filter documented so the evidence remains runnable. Below-floor rejection and SQLite compaction are covered in the focused lane; the external PostgreSQL runtime-role scenario remains a container-backed CI/rehearsal check.

Skip reason requirements:

- `Category:` names the owning suite or lane (`Runtime`, `Stress`, `API contract`, `Component accessibility`, `Manual`, etc.).
- `Removal:` states the concrete condition for re-enabling or deleting the skip.
- No permanent skips. Infrastructure-gated tests move to a nightly/manual lane through category filters; they do not remain hidden from governance.
- No backward-compatibility preservation tests while the project is in development mode. If a test only protects an obsolete API, DTO shape, route alias, or UI behavior, delete it instead of skipping it.

`Event.Architecture.Tests.CodeHygieneTests` enforces that test source files do not contain commented-out `[Test]` markers and that every `[Skip]` includes both `Category:` and `Removal:`.

### Critical Risk Traceability Matrix

| Risk | Primary Test Project(s) | Required Coverage |
|------|--------------------------|-------------------|
| Tenant isolation leaks | `Event.Persistence.IntegrationTests`, `Event.API.IntegrationTests` | EF named filters, repository queries, and API tenant binding |
| Authorization drift | `Event.Application.UnitTests`, `Event.API.IntegrationTests`, `Event.Architecture.Tests` | Handler authorization outcomes, endpoint 401/403/ProblemDetails, Cerbos/fallback parity |
| Infrastructure provider/config drift | `Explore.Infrastructure.Tests`, `Event.Architecture.Tests` | Provider adapters, deployment-mode settings, configuration resolvers, fallback authorization behavior, and governance keys |
| BFF token/header boundary failure | `Explore.Blazor.IntegrationTests` | Server-side token forwarding, setup-secret stripping/replacement, and trusted tenant hint forwarding |
| HAL/UI action mismatch | `Event.API.IntegrationTests`, `Explore.Blazor.Client.Tests` | HATEOAS link policies, response contracts, UI affordances gated by `_links` |
| Relational persistence regression | `Event.Persistence.IntegrationTests` | Provider-specific migrations, query translation, constraints, soft delete, tenant filters, and clean reset |
| Rate limiting/timeout/idempotency regressions | `Event.API.IntegrationTests` | Stress host policies, Retry-After metadata, ProblemDetails, idempotency and request-timeout middleware behavior |
| Accessibility/design-system drift | `Explore.Blazor.Client.Tests`, `Event.Architecture.Tests` | bUnit semantic component checks, structural accessibility guardrails, wrapper behavior |

### Email And Messaging Scenario Matrix

Email tests prove durable state and provider behavior at the lowest layer that can observe each risk. Unit tests can fake SMTP only when the behavior is pure decision logic; integration and runtime tests use real infrastructure such as PostgreSQL, Mailpit, RabbitMQ, and Keycloak.

The approved lifecycle-email workstream retains a stricter reviewed phase gate than the implementation-plan skill default. Each runtime phase runs one Release build and every directly affected full project named in its task ledger. Phases 1 and 7 include `Event.API.IntegrationTests`; they also run the explicit positive `Email` Infrastructure/Mailpit lane and record the exact non-zero test count. A broad OR filter or `--minimum-expected-tests 1` alone is not release evidence for new lifecycle behavior.

| Planned phase | Full project additions beyond the owning lower layers |
|---|---|
| Phase 0B Coop | Infrastructure and API integration plus Architecture |
| Phase 1 recipient delivery | Infrastructure, API integration, Architecture, and explicit Mailpit |
| Phase 4 event/session triggers | Infrastructure, API integration, and Architecture |
| Phase 5 reporter communication | Infrastructure, API, Blazor Client, Blazor BFF, and Architecture |
| Phase 7 reminders | Infrastructure, API integration, Architecture, and explicit Mailpit |

| Scenario | Primary Test Project | Required Evidence |
|---|---|---|
| Direct SMTP to local capture service | `Explore.Infrastructure.Tests` | `SmtpEmailServiceMailpitTests` connects to Mailpit and sends one message; `SmtpEmailServiceConfigurationTests` proves missing SMTP config fails before provider handoff without leaking secrets. |
| SMTP settings resolution | `Explore.Infrastructure.Tests` | `SmtpConfigResolverTests` proves active tenant context reaches the hierarchical settings resolver, system-default and tenant-override values do not share cache entries, required settings fail closed, and defaults are bounded. |
| Basic EmailDispatch drain | `Explore.Infrastructure.Tests`, `Event.Persistence.IntegrationTests` | `EmailDispatchDrainMailpitTests` proves a pending outbox row is claimed, sent through Mailpit, marked `Sent`, records succeeded attempt/completed receipt state, and duplicate consumers produce one Mailpit message; persistence tests cover PostgreSQL state transitions. |
| Tenant pause, retry, dead-letter, unknown, and replay | `Explore.Infrastructure.Tests`, `Event.Persistence.IntegrationTests`, `Event.API.IntegrationTests` | `EmailDispatchDrainServiceTests` covers retry, exhausted dead-letter, timeout-like unknown, and stale-processing recovery behavior; repository/API tests prevent sends while paused, keep failures inspectable, and expose operator actions through authorized HAL links. |
| Quartz and hosted-service triggers | `Event.API.IntegrationTests` | `EmailDispatchQuartzJobsTests` and `EmailDispatchProcessorTests` prove trigger wrappers delegate to the shared drain service and do not own SMTP, RabbitMQ, or payload logic; `EmailDispatchHealthCheckTests` proves Quartz, HostedService, Disabled, and scheduler-disabled readiness states; `QuartzSchemaInitializerTests` and `QuartzSqliteDurableSchedulingTests` prove the embedded DDL is complete, idempotent, non-destructive, and that scheduled state survives a scheduler restart on SQLite. |
| RabbitMQ fixture, topology, and pointer publish | `Explore.Infrastructure.Tests` | `RabbitMqContainerFixtureTests` starts a real broker with AMQP and management diagnostics; `RabbitMqEmailDispatchTransportLiveTests` proves enabled topology declaration, healthy readiness, confirmed publish, mandatory-return outcomes, and pointer-only broker payloads against a real broker; publish metadata is persisted, nack/timeout paths fail safely, and `EmailDispatchRabbitMqHealthCheckTests` proves disabled, healthy-enabled, and unhealthy transport readiness states without leaking secrets. |
| RabbitMQ consume and DLQ replay | `Explore.Infrastructure.Tests` or runtime lane | `RabbitMqEmailDispatchConsumerMailpitTests` proves a valid pointer drains the durable row through real SMTP to Mailpit and ACKs after the durable outcome; malformed and missing-outbox pointers reject to DLQ without sending mail; `RabbitMqEmailDispatchDeadLetterReplayLiveTests` proves replayable rows reset durable state before republish and unsafe payloads park. |

### Generating TRX Reports

When debugging failures, generate detailed reports:

```bash
dotnet test --project <ProjectPath> --configuration Release -- --report-trx --report-trx-filename results.trx
```

## Persistence Hardening Verification

Run provider classes separately. Combining long-lived container classes in one
tree filter can bypass the intended TUnit isolation and obscure which engine
failed.

```bash
# SQL Server, MariaDB, and MySQL generated-initial/runtime behavior
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj \
  --configuration Release --no-build -- \
  --treenode-filter "/*/*/*ContainerizedPrimaryDatabaseProviderBehaviorContractTests/*" \
  --minimum-expected-tests 3

# Real row fences and transaction/session named locks
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj \
  --configuration Release --no-build -- \
  --treenode-filter "/*/*/*AdmissionAuthorityRowFenceProviderTests/*" \
  --minimum-expected-tests 6
```

The PostgreSQL performance lane sets
`PERSISTENCE_QUERY_BASELINE_OUTPUT` to a temporary file and executes these
classes one at a time:

- `EmailDispatchOutboxTransitionRepositoryTests`
- `NotificationFanoutOccurrenceRepositoryTests`
- `RegistrationInventoryHoldConcurrencyTests`
- `WebhookPublicationClaimRepositoryTests`
- `PaymentReconciliationPostgreSqlClaimTests`

Each recorded line contains only operation code, command count, maximum
projection/parameter count, duration, elapsed time, and cardinality. SQL,
parameter values, connection strings, and entity identifiers are never
retained.

## Architecture Tests

### Executable Architecture Contracts

Architecture tests enforce rules through compiled assembly/type relationships,
reflection, runtime endpoint and dependency-injection metadata, HTTP/HAL
behavior, or parsed machine-consumed schemas. They must not freeze raw C#,
Razor, CSS, Markdown prose, class-name inventories, line counts, or historical
debt allowlists.

Structured manifests are appropriate only when they are shipped
machine-consumed contracts or generator inputs and the test derives the
complete current surface independently. A stale historical exemption is not a
contract. When no executable seam can prove a style preference, use an analyzer
or review guidance instead of a source-text test.

Product tests never consume `AGENTS.md`, `.agents/**`, Markdown under `docs/`,
active plans/tasks/evidence, journals, or skill prose. If automation consumes a
machine-readable artifact, validate it through the consuming parser or an
explicit tool under `eng/`. Documentation generators are invoked directly and
must not masquerade as TUnit tests.

Blazor component tests use generic bUnit rendering and typed parameter builders
for every compile-time-known public component. `DynamicComponent` is reserved
for product behavior that genuinely chooses a component descriptor at runtime;
those tests provide the descriptor with `typeof` and typed parameter names, or
exercise the owning parent behavior. Tests invoke non-public load, toggle, and
save actions through rendered controls instead of reflection.

The repository-owned Roslyn assurance audit is the recurrence guard for changed
test code. It reports category and bounded syntax location only—never source
excerpts or values—and carries no historical file allowlist:

```bash
dotnet run --project eng/tools/Explore.AssuranceAudit/Explore.AssuranceAudit.csproj \
  --configuration Release -- . --changed-from HEAD
```

The audit rejects runtime-selected reflective behavior, reflective construction
that bypasses a compile-time contract, and token assurance over product source
or documentation. Compiled endpoint/type/EF metadata and parsed JSON, YAML,
schema, and project artifacts remain valid assurance seams.


`Event.Architecture.Tests` enforces project-wide conventions through reflection-based tests. These are not optional — they are CI gates.

### Convention Categories

| Category | What It Enforces |
|----------|-----------------|
| **Layer Dependencies** | Clean Architecture rules: Domain has no upstream references, Application references only Domain |
| **Naming Conventions** | Handler suffixes, validator suffixes, specification naming |
| **Accessibility** | Routable pages contain `<h1>`, MainLayout has skip-link, landmarks, ARIA live regions |
| **CSS Direction** | Scoped CSS avoids physical direction properties (`margin-left` → `margin-inline-start`) — advisory |
| **Authorization Parity** | Every resource kind has a Cerbos policy and fallback case |
| **ABOUTME Headers** | All C# files start with `ABOUTME:` comments |
| **Test Suite Governance** | Disabled tests use explicit skip metadata, never commented-out `[Test]` markers |

### Accessibility Convention Tests

These tests verify structural accessibility requirements:

- `RoutablePages_MustContainH1Heading` — every routable page has an `<h1>` (excludes settings wrapper pages where `<h1>` is in the active tab)
- `MainLayout_MustContainSkipLink` — skip navigation link present
- `MainLayout_MustContain_MainLandmark` — `<main>` element exists
- `MainLayout_MustContain_HeaderLandmark` — `<header>` element exists
- `MainLayout_MustContain_NavigationLandmark` — `<nav>` with `aria-label` exists
- `MainLayout_MustContain_AriaLiveRegions` — polite and assertive live regions present
- `ScopedCss_MustNotUsePhysicalDirectionProperties` — advisory, tracks RTL readiness
- `ScopedCss_MustNotUsePhysicalPositionProperties` — advisory, tracks RTL readiness

### Authorization Parity Tests

- `RegisteredResourceKinds_ShouldHave_FallbackCase` — every resource kind handles unknown permissions
- `RegisteredResourceKinds_ShouldHave_CerbosPolicy` — policy file exists per resource
- `CerbosPolicies_ShouldHave_FallbackCase` — Cerbos YAML includes default deny

## TDD Workflow

TDD is the default unless explicitly allowed to skip.

1. **Write a failing test** — define expected behavior
2. **Run to confirm failure** — test must fail for the right reason
3. **Write minimal code** — just enough to pass
4. **Run tests** — all must pass
5. **Refactor** — improve code with tests green

## Test Conventions

### Do

- Test one behavior per test method
- Use descriptive test names: `MethodName_Condition_ExpectedResult`
- Use real data and APIs in integration tests — avoid mocks in end-to-end flows
- Run all required project-level test projects before submitting a PR; never use solution-level `dotnet test`
- Keep test output pristine — no unexpected warnings or stack traces
- Prefer deleting unnecessary or backward-compatibility-only tests over preserving obsolete behavior

### Do Not

- Delete failing tests to make the suite pass
- Commit with broken tests
- Use mocks when the test targets an in-process integration seam; use explicit runtime tests for provider behavior
- Create ad-hoc test scripts — use the test projects
- Skip architecture tests — they are CI gates
- Comment out `[Test]` attributes to hide failing tests
- Add backward-compatibility tests for obsolete behavior while the project is in development mode

## Test Data And Fixtures

- **Domain unit tests**: construct entities directly with valid state
- **Application unit tests**: use in-memory fakes or builder patterns for repositories
- **Integration tests**: use the real database with test containers or Docker infrastructure
- **API integration tests**: use `WebApplicationFactory` with the full middleware pipeline

## CI Pipeline Integration

The standard CI pipeline runs the fast test projects on every PR; integration-enabled callers run PostgreSQL-backed suites separately. GitHub Actions restore steps use `dotnet restore --locked-mode`, so package input changes must include matching `packages.lock.json` updates. The pipeline:

1. Restores dependencies
2. Builds in Release configuration
3. Runs each fast test project sequentially (not solution-level). `Explore.Infrastructure.Tests` uses `Category!=Runtime` in the fast lane so Docker-backed provider tests do not become an implicit required gate.
4. Publishes TRX evidence for CI troubleshooting
5. Fails the PR if any test project reports failures
6. Architecture tests run alongside unit tests (no infrastructure needed)

Integration-enabled callers additionally run focused infrastructure runtime categories as evidence. The `Email` lane starts Mailpit through Testcontainers and publishes `Explore.Infrastructure.Tests.Email.trx`; the `RabbitMQ` lane starts the RabbitMQ management image, may also start Mailpit for consumer delivery assertions, and should publish `Explore.Infrastructure.Tests.RabbitMQ.trx` with container diagnostics.

Nightly/manual runtime lanes are intentionally advisory until reliability data proves they can be merge-blocking:

- `.github/workflows/security-tests.yml` and `.github/workflows/cerbos-policy-check.yml` also run on schedule so auth, Keycloak, Cerbos, and policy contracts are exercised even when no matching path changes occur.
- Runtime-lane failures retain artifacts for debugging rather than forcing an immediate local rerun.

Required vs advisory gate policy, branch-protection guidance, and artifact retention expectations are maintained in [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md).

Rate limiting is automatically disabled in the `Testing` environment — all rate limit policies are replaced with `NoLimiter`.

## Related

- [GETTING_STARTED.md](GETTING_STARTED.md) — setup and first run
- [ARCHITECTURE.md](ARCHITECTURE.md) — layer rules enforced by architecture tests
- [ACCESSIBILITY.md](ACCESSIBILITY.md) — WCAG requirements tested by convention tests
- [CONTRIBUTING.md](CONTRIBUTING.md) — PR validation checklist
- [QUICK_REFERENCE.md](QUICK_REFERENCE.md) — constraints tested by architecture tests
- [DOCUMENTATION_ARCHITECTURE.md](DOCUMENTATION_ARCHITECTURE.md) — documentation metadata, source anchors, and quality gates

## API Integration Test Host Profiles

The API integration tests use a **three-host-profile model** to balance speed, fidelity, and isolation.

| Profile | Database | Rate Limiting | Purpose |
|---|---|---|---|
| **Contract** | EF InMemory | Disabled | Fast API surface validation: serialization, HAL structure, ProblemDetails, content-type, Prefer headers |
| **RealRuntime** | PostgreSQL (Testcontainers) | Disabled | Production-faithful behavior: persistence, tenant isolation, auth families, migrations |
| **Stress** | PostgreSQL (Testcontainers) | Enabled (low thresholds) | Timing-sensitive: rate limiting enforcement, 429 response format |

These profiles are correctness tests, not performance benchmarks. Runtime benchmark runs live in [BENCHMARKS.md](BENCHMARKS.md) and use BenchmarkDotNet so contributors can compare relative endpoint cost, allocations, and diagnoser output under controlled runs.

### Fixture Architecture

Onboarding HTTP tests use `OnboardingWebApplicationFactory` with a unique SQLite
file under the test process's temporary directory. It reuses production provider
composition, including transaction-completion interceptors that release named
locks, and deletes its own file after disposing its unpooled connection. An
in-memory replacement cannot prove this transaction lifecycle. Fixtures seed
UUIDv7 user identities and issuer-bound external-login keys; a session ID or
unlinked internal-user claim is not account authority.

Inject disposable `SETUP_SECRET` and, for Keycloak rotation tests,
`KEYCLOAK_BLAZOR_CLIENT_SECRET` through the environment keys documented in
`.env.example`. These fixtures do not embed credentials or mutate process-wide
secret values. Keep real operator credentials out of test runs and logs.
The reusable API integration CI step generates and masks both disposable values
for its test process; it does not need repository or operator secret access.

```
Event.API.IntegrationTests/
├── Builders/           # Fluent entity builders (TenantBuilder, UserBuilder, ActorBuilder, EventBuilder)
├── Fixtures/           # WebApplicationFactory + TUnit fixtures
│   ├── ContractApiFixture.cs             (InMemory, fast)
│   ├── RealRuntimeApiFixture.cs          (PostgreSQL, production-faithful)
│   ├── StressApiFixture.cs               (PostgreSQL, rate limiting enabled)
│   ├── PostgreSqlApiFixtureBase.cs       (abstract base for PG fixtures)
│   ├── PostgreSqlApiWebApplicationFactory.cs
│   ├── TestDatabaseReset.cs              (Respawn wrapper)
│   └── TestHostProfile.cs               (profile constants)
├── Features/           # Test classes organized by feature
│   ├── Hateoas/        # HAL contract + scenario tests
│   └── ...
├── Helpers/            # Assertion helpers (ProblemDetailsAssertions)
└── Seeds/              # Business-readable scenario seeds
```

### Database Lifecycle (PostgreSQL Fixtures)

```
Container Start → MigrateAsync → LookupTableSeeder (27 tables) → Respawner.CreateAsync

Per Test: ResetAsync (Respawn) → Seed scenario data → Execute test
```

Respawn resets ALL tables except `__EFMigrationsHistory` and 27 lookup tables.

### Testcontainers Benchmarks Versus Tests

`Event.Benchmarks` has a PostgreSQL/Testcontainers API benchmark lane that intentionally borrows the integration-test infrastructure shape without becoming a test project:

| Concern | API Integration Tests | PostgreSQL API Benchmarks |
|---|---|---|
| Goal | Prove correctness: status codes, HAL contracts, auth behavior, tenant isolation, migrations, and persistence rules. | Compare endpoint cost through ASP.NET Core `TestServer`, EF Core, Npgsql, and PostgreSQL for performance work. |
| Runner | TUnit via `dotnet test --project ...`; never solution-level `dotnet test`. | BenchmarkDotNet via `dotnet run --configuration Release --project tests/Event.Benchmarks/Event.Benchmarks.csproj -- --filter "*ApiEndpointPostgreSqlBenchmarks*"`. |
| Database setup | PostgreSQL Testcontainer, `MigrateAsync()`, lookup seeding, Respawn reset per scenario. | PostgreSQL Testcontainer in BenchmarkDotNet `GlobalSetup`, current EF model schema via `EnsureCreatedAsync()`, PostgreSQL model constraints, lookup and benchmark-owned event seed data. |
| Measured body | Assertions and scenario behavior; setup and assertions are part of the test. | Timed method only sends the HTTP request and reads the response; container start, schema creation, and seeding are outside measured iterations. |
| Caching/auth | Uses profile-specific auth, Cerbos, rate-limit, and reset conventions according to the test purpose. | Uses benchmark auth and allow-all authorization; PostgreSQL suite disables output-cache replay with a no-op store so controller/MediatR/EF/Npgsql/PostgreSQL work remains visible. |

Use integration tests when behavior must be proven. Use the PostgreSQL API benchmark when the question is whether an implementation change makes representative read endpoints faster, slower, or more allocation-heavy. Benchmark numbers are relative evidence from one controlled run, not production SLOs or load-test proof.

### Builders vs Seeds

| Concept | Purpose | Location |
|---|---|---|
| **Builders** | Low-level entity construction with fluent API and sensible defaults | `Builders/` |
| **Seeds** | Business-readable named scenarios that compose builders and persist to DB | `Seeds/` |

Tests call seeds (not builders directly) for readability. Seeds handle circular dependency resolution (e.g., User → Actor → User.ActorId).

### Writing New API Integration Tests

```csharp
[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public class MyFeatureTests(RealRuntimeApiFixture fixture)
{
    [Test]
    public async Task MyScenario()
    {
        await fixture.ResetDatabaseAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);

        var response = await fixture.Client.GetAsync("/api/event");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
```

Key conventions:
- **`[NotInParallel]`** — Required for PostgreSQL tests. Use `"RealRuntimeDb"` or `"StressDb"`.
- **Primary constructors** — Prefer `public class MyTests(FixtureType fixture)`.
- **Reset before seed** — Every test calls `ResetDatabaseAsync()` then seeds its own data.
- **Auth helpers** — `CreateAuthenticatedRequest()`, `CreateInstanceAdminRequest()`, `CreateTenantAdminRequest()` on all fixtures.

### Anti-Patterns

- Do not share data across tests — each test resets and seeds its own.
- Do not assert `IsNotEqualTo(InternalServerError)` — assert the exact expected status code.
- Do not guard assertions with `if (items.Length > 0)` — seed data to guarantee items exist.
- Do not use `EnsureCreated()` for PostgreSQL test fixtures — use `MigrateAsync()`. The BenchmarkDotNet PostgreSQL harness is the intentional exception because it measures active-development current-model API performance rather than migration correctness.
- Do not test non-existent endpoints.
- Do not test auth gates in isolation when covered by auth-family matrix tests.

### API Contract Snapshot Tests

Contract snapshots live in the **Contract** host profile and use `Verify.TUnit` against the EF InMemory API surface. They are for stable HTTP response contracts such as HAL links, collection envelopes, and RFC 7807 ProblemDetails shapes.

Snapshot rules:

- Commit `.verified.*` files under the feature snapshot directory; these are the reviewed API contract baselines.
- Never commit `.received.*` files. Treat them as review artifacts generated when a response contract changes.
- Scrub volatile fields before verification (`traceId`, `timestamp`, `correlationId`, generated cache-busters, and similar request-specific values).
- Update snapshots only when the API contract change is intentional and reviewed alongside the endpoint implementation.
- Keep Docker/PostgreSQL-dependent snapshots separate from Contract-profile snapshots; real-runtime snapshot coverage is optional and should be added only when the environment can run the required infrastructure.

Run focused snapshot tests with TUnit tree filters. Example:

```bash
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj \
  --configuration Release --no-build \
  --treenode-filter "/*/*/*/EventList_AnonymousContract_MatchesSnapshot" \
  --minimum-expected-tests 1
```

## Blazor Client Tests (Explore.Blazor.Client.Tests)

Tests Blazor component rendering, service behavior, accessibility compliance, and UI interaction patterns using [bUnit](https://bunit.dev/).

### Test Harness Architecture

The shared `BlazorTestContext` extends `Bunit.TestContext` with:

| Feature | Description |
|---------|-------------|
| **Strict JSInterop** | bUnit default — throws on unconfigured JS invocations. Only approved MudBlazor and app-specific handlers are pre-configured. |
| **MudBlazor service mocks** | Concrete mock classes (`MudBlazorTestMocks.cs`) registered BEFORE `AddMudServices()` so `TryAdd*` skips JS-dependent services. |
| **Opt-in service groups** | `AddShellStateMocks()`, `AddGroupServiceMock()`, `AddAllDefaultMocks()` — test files opt in to services they need. |
| **Auth builders** | `AuthenticationTestBuilder` (fluent), `AuthenticationScenarios` (factory), `AuthenticationTestConstants` (IDs/roles). |
| **Settings builder** | `PublicExperienceSettingsBuilder` for feature flags, branding, analytics, and module configuration. |

### Choosing the Right Test Layer

| Question | If Yes → | If No → |
|----------|----------|---------|
| Does the behavior require a real browser (JS execution, cookies, redirects)? | Perform focused manual browser QA and cover the server/component seams below | ↓ |
| Does the test need the full BFF middleware pipeline (auth, tenant routing)? | `Explore.Blazor.IntegrationTests` | ↓ |
| Does the test exercise component rendering, service behavior, or accessibility? | `Explore.Blazor.Client.Tests` | ↓ |
| Does the test verify domain logic or handler behavior? | `Event.Application.UnitTests` or `Event.Domain.UnitTests` | Reconsider if a test is needed |

### Writing New Blazor Component Tests

```csharp
public class MyComponentTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public MyComponentTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.AddShellStateMocks();  // opt in to what you need
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_WhenAuthenticated_ShowsUserName()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Jane Doe");
        var cut = _ctx.RenderMudComponent<MyComponent>();

        await Assert.That(cut.Markup).Contains("Jane Doe");
    }
}
```

### Approved Patterns

- **Interaction-driven tests** — click buttons, fill inputs, trigger callbacks via rendered elements
- **Public API verification** — assert via `cut.Instance.PublicMethod()` or rendered markup
- **Behavioral assertions** — test what the user sees, not implementation details
- **`WaitForAssertion`** — for async lifecycle (service calls in `OnAfterRenderAsync`)
- **`SetParametersAndRender`** — re-render with new parameter values to test reactivity
- **Fluent auth builders** — `AuthenticationScenarios.Admin().Build(ctx)` for auth setup
- **Settings builder** — `new PublicExperienceSettingsBuilder().WithBranding("My Brand").WithIslamicModule(true)`
- **`NavMenuTestServices.Register(ctx)`** — for tests rendering components that depend on NavMenu services

### Anti-Patterns (Disallowed)

| Pattern | Why It's Disallowed | What To Do Instead |
|---------|--------------------|--------------------|
| `JSInterop.Mode = JSRuntimeMode.Loose` | Masks missing JS call handlers; failures appear only at runtime | Keep strict mode; add explicit `SetupVoid` / `Setup<T>` handlers |
| `GetMethod("Private", BindingFlags.NonPublic)` | Couples tests to implementation; breaks on refactoring | Test through UI interaction or public API |
| `GetField("_privateField", BindingFlags.NonPublic)` | Same as above | Verify via rendered markup or public state |
| Ambient service registration in constructor | Hides dependencies; tests pass for wrong reasons | Use opt-in helpers (`AddShellStateMocks()`) |
| `if (condition) throw` assertions | Not TUnit-native; no structured failure reporting | Use `await Assert.That(x).Contains(y)` |
| Snapshot-style markup equality | Brittle; breaks on any MudBlazor version update | Assert specific elements, classes, or text content |

## BFF Integration Tests (Explore.Blazor.IntegrationTests)

Tests the Blazor Server BFF layer: middleware pipeline, auth endpoints, and delegating handler chain.

### Architecture

```
Explore.Blazor.IntegrationTests/
├── Fixtures/           # WebApplicationFactory + test auth handler
│   ├── BlazorBffWebApplicationFactory.cs  (InMemory DB, mock auth, mock resolver)
│   ├── TestAuthHandler.cs                 (X-Test-Auth header → ClaimsPrincipal)
│   └── TenantTestController.cs            (test-only controller for middleware assertions)
├── Endpoints/          # BFF auth endpoint tests
│   └── BffAuthStatusTests.cs
├── Handlers/           # DelegatingHandler unit-style tests
│   ├── AccessTokenForwardingHandlerTests.cs
│   ├── CorrelationIdDelegatingHandlerTests.cs
│   ├── SetupSecretForwardingHandlerTests.cs
│   ├── TenantHeaderForwardingHandlerTests.cs
│   └── CapturingHandler.cs               (shared test double)
└── Middleware/          # Middleware pipeline tests
    └── PathTenantResolverMiddlewareTests.cs
```

### Test Host

`BlazorBffWebApplicationFactory` uses `WebApplicationFactory<Program>` with:

- **Environment**: `Testing` (disables rate limiting)
- **Auth**: Cookie + OIDC replaced with `TestAuthHandler` (header-based claims)
- **Database**: EF InMemory replacing Npgsql
- **Cache**: `DistributedMemoryCache` replacing Redis
- **Auth schemes**: Mock `IDynamicAuthSchemeManager` (empty provider list)
- **Tenant resolver**: Mock `IResolverConfigService` (PathEnabled=true, PathPrefix="/t")

### Handler Tests

Handler tests use `CapturingHandler` — a test double that captures outgoing requests and returns `200 OK`. Each handler is tested in isolation with `HttpMessageInvoker`:

```csharp
var handler = new TenantHeaderForwardingHandler(mockAccessor, mockHttpContextAccessor)
{
    InnerHandler = new CapturingHandler()
};
var invoker = new HttpMessageInvoker(handler);
var response = await invoker.SendAsync(request, CancellationToken.None);
// Assert captured request headers
```

### Writing New BFF Integration Tests

```csharp
public class MyBffTests
{
    private readonly BlazorBffWebApplicationFactory _factory = new();

    [Test]
    public async Task MyEndpoint_WhenAuthenticated_ReturnsExpected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth",
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid(), "Test User"));

        var response = await client.GetAsync("/auth/status");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
```

### Ticket Purchase Public-Surface Slices

```bash
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*TicketPurchaseGovernanceApiTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1

dotnet test --project tests/Explore.GeneratedContracts.Tests/Explore.GeneratedContracts.Tests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*TicketPurchaseGeneratedContractTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1

dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*TicketPurchaseGovernanceBffTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1

dotnet test --project tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*TicketPurchaseGovernanceComponentTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1
```

These slices jointly verify server-owned public input, HAL/OpenAPI shape, generated-client headers and mutability, antiforgery/cookie/capability behavior, tenant-spoof exclusion, private caching, accessible scope selection, and HAL-only UI affordances.

### Participant Readiness Public-Surface Slices

```bash
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*ParticipantReadinessApiTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1

dotnet test --project tests/Explore.GeneratedContracts.Tests/Explore.GeneratedContracts.Tests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*ParticipantReadinessGeneratedContractTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1

dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*ParticipantReadinessBffTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1

dotnet test --project tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj \
  --configuration Release --verbosity quiet -- \
  --treenode-filter "/*/*/*ParticipantReadinessComponentTests/*" \
  --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1
```

These slices jointly prove exact-resource access, generic capability failure, PII-minimal OpenAPI/generated shapes, private caching, antiforgery and cookie authority, generated-client-only BFF forwarding, HAL-only actions, deterministic pending disabling, accessible error focus, and logical RTL-safe styling.
