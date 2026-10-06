# PROJECTS.md — Project Boundaries & Verification Map

> **Audience:** Contributors | Developers | AI Agents  
> **Status:** Authoritative  
> **Primary Solution:** [`Explore.slnx`](Explore.slnx)  
> **Source Anchors:** [`AGENTS.md`](AGENTS.md), [`AI_REVIEW.md`](AI_REVIEW.md), [`docs/internal/QUICK_REFERENCE.md`](docs/internal/QUICK_REFERENCE.md), [`docs/internal/OPERATIONS.md`](docs/internal/OPERATIONS.md)

---

## 1. First-Use Project Map Workflow

`PROJECTS.md` is the authoritative map of verified solution structure, project responsibilities, protected paths, and targeted verification commands.

1. **Targeted Verification Default**: AI agents and developers must target verification against the specific affected project(s) by default. Broader solution-level builds or multi-provider test sweeps are justified only by shared build settings, cross-project dependencies, solution wiring changes, or workstream Plan Exit gates (Ring 3).
2. **Never Invent Placeholders**: Every entry below corresponds to an actual `.csproj` in `Explore.slnx`. Never create or modify projects merely to satisfy a placeholder.
3. **No Secrets or Credentials**: Never record passwords, tokens, API keys, or real connection strings in this document or any commit. Secrets originate strictly from **Infisical** or `.env`.

---

## 2. Solution Structure & Project Roles

### Core Domain & Application Layers
| Project Path | Role & Responsibilities | Key Dependencies |
|---|---|---|
| `src/Explore.Domain/Explore.Domain.csproj` | Pure domain entities, aggregate roots, domain events, value objects, enumeration types, and business invariants. Entities double as EF Core entities. Zero outward dependencies. | None |
| `src/Explore.Application/Explore.Application.csproj` | CQRS commands and queries, native CQS handlers, FluentValidation validators (manually instantiated), specification builders (`EventQuerySpecification`), and mapping logic. | `Explore.Domain` |
| `src/Event.Wire.Contracts/Event.Wire.Contracts.csproj` | Public DTO contracts, serialization wire models, and shared wire primitives. | None |

### Presentation & API Layers
| Project Path | Role & Responsibilities | Key Dependencies |
|---|---|---|
| `src/Explore.API/Explore.API.csproj` | HTTP controllers (`EventControllerBase`), HAL link policies, RFC 7807 problem details error mapping, rate limiting, and output caching. Thin controllers invoking native CQS handlers. | `Explore.Application`, `Explore.Persistence`, `Explore.Infrastructure` |
| `src/Event.Web.BffHosting/Event.Web.BffHosting.csproj` | Backend-for-Frontend (BFF) server hosting, YARP reverse proxy, cookie session management, and tenant route rewriting (`/{slug}`). | `Explore.Diagnostic` |
| `src/Event.Standalone/Event.Standalone.csproj` | Standalone single-container hosting entrypoint. | `Explore.API`, `Explore.Blazor`, existing startup migration artifacts for primary data, Data Protection, and embedded privacy authority |
| `src/Event.MigrationService/Event.MigrationService.csproj` | Database migration runner and initial seed orchestrator (Aspire resource). | `Explore.Persistence` |
| `src/Explore.AppHost/Explore.AppHost.csproj` | .NET Aspire orchestration host (Development/Testing only). | Aspire hosting packages |

### User Interface (Blazor)
| Project Path | Role & Responsibilities | Key Dependencies |
|---|---|---|
| `src/Explore.Blazor/Explore.Blazor.csproj` | Blazor server-side host project, SSR components, and static asset serving. | `Explore.Blazor.Client`, `Event.Web.BffHosting` |
| `src/Explore.Blazor.Client/Explore.Blazor.Client.csproj` | Interactive WebAssembly UI components, MudBlazor v9 views, scoped CSS (`.razor.css`), and generated API client consumption. **Strictly isolated**: forbidden from referencing Domain, Application, or Persistence. | `Event.Wire.Contracts`, Generated API Clients |

### Infrastructure & Persistence Layers
| Project Path | Role & Responsibilities | Key Dependencies |
|---|---|---|
| `src/Explore.Persistence/Explore.Persistence.csproj` | EF Core `ExploreDbContext`, tenant global query filters, repository implementations, entity configurations, and PostgreSQL session interceptor. | `Explore.Domain`, `Explore.Application` |
| `src/Explore.Infrastructure/Explore.Infrastructure.csproj` | Quartz.NET scheduled jobs, transactional outbox dispatch, external email/messaging adapters, and Redis caching. | `Explore.Application` |
| `src/Explore.Secrets/Explore.Secrets.csproj` | Infisical and local secret resolution abstractions (`ISecretResolver`). | None |
| `src/Explore.Diagnostic/Explore.Diagnostic.csproj` | OpenTelemetry tracing, Prometheus metrics, and structured logging. | `Explore.ServiceDefaults` |
| `src/Explore.Atproto.Transport/Explore.Atproto.Transport.csproj` | ATProtocol / CarpaNet transport and lexicon models. | None |

### EF Core Migration Assemblies
| Project Path | Target Database Provider |
|---|---|
| `src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj` | SQLite provider migrations |
| `src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj` | MySQL / MariaDB provider migrations |
| `src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj` | SQL Server provider migrations |
| `src/Explore.Persistence.DataProtection.Migrations.*/...` | Data Protection key repository provider migrations |
| `src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/...` | Privacy erasure authority audit store migrations |

### Setup Assistant Ecosystem
| Project Path | Interface Type |
|---|---|
| `src/Event.Setup.Core/Event.Setup.Core.csproj` | Setup domain logic, configuration manifest parsing, and validation |
| `src/Event.Setup.Artifacts/Event.Setup.Artifacts.csproj` | Setup artifact extraction and bundling |
| `src/Event.SetupAssistant/Event.SetupAssistant.csproj` | Shared Setup Assistant component library |
| `src/Event.SetupAssistant.Cli/Event.SetupAssistant.Cli.csproj` | Headless CLI setup tool (`event-setup`) |
| `src/Event.SetupAssistant.Terminal/Event.SetupAssistant.Terminal.csproj` | Terminal GUI setup tool (Terminal.Gui v2) |
| `src/Event.SetupAssistant.Browser/Event.SetupAssistant.Browser.csproj` | Web-based setup assistant |
| `src/Event.SetupAssistant.Desktop/Event.SetupAssistant.Desktop.csproj` | Desktop (Photino) setup assistant |

---

## 3. Test Projects & Coverage Targets

| Test Project Path | Layer Under Test | Typical Latency | Verification Ring |
|---|---|---|---|
| `tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj` | Pure domain invariants, entities, value objects, domain events | < 50ms | **Ring 1** (Inner Loop) |
| `tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj` | CQRS handlers, command responses, validator rules, mapping | < 2s | **Ring 1** (Inner Loop) |
| `tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj` | Clean Architecture layer directions, naming rules, HAL policies | < 2s | **Ring 2** (Phase Exit) |
| `tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj` | bUnit component render, parameter updates, HAL affordance gating | < 5s | **Ring 2** (Phase Exit) |
| `tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj` | HTTP controllers, route naming, RFC 7807 error responses, auth | < 10s | **Ring 2** (Phase Exit) |
| `tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj` | EF Core multi-provider queries, tenant RLS, concurrency, outbox | < 30s | **Ring 3** (Plan Exit) |
| `tests/Event.Benchmarks/Event.Benchmarks.csproj` | BenchmarkDotNet microbenchmarks | Manual | On demand |
| `tests/Event.Setup.*.Tests/...` | Setup Assistant unit and integration tests | < 5s | On change |

---

## 4. Protected Paths (Explicit Approval Required)

The following paths represent sensitive boundaries or generated artifacts. **Agents must never modify these files without explicit developer direction:**

| Protected Path | Nature of Protection | Authorized Procedure |
|---|---|---|
| `src/Explore.Persistence.Migrations.*/**` | EF Core migrations & model snapshots | Generated artifacts. Fix entity/configuration or generator, then delete unapplied migration and rerun `dotnet ef migrations add`. Never hand-edit. |
| `src/Explore.Blazor.Client/Clients/EventApiClient.g.cs` | Generated NSwag API client | Generated artifact. Update API controller/DTO and regenerate via the NSwag Roslyn build target. Never hand-edit. |
| `.ci/**` | CI/CD pipeline workflows & scripts | Infrastructure automation. Only modify when explicitly assigned CI/CD or release engineering tasks. |
| `eng/release/**` | Release engineering & packaging | Protected release automation. Changes require explicit release engineering intent. |
| `legal/**`, `docs/legal/**` | Outbound licensing, CLA, IP governance | Project Steward authority only. Changes require formal legal/governance review. |

---

## 5. Targeted Verification Commands

All commands run from the repository root (`/home/amir/ISLAMU/Github/Event`) unless specified otherwise.

### Inner Loop — Fast Subtask Slicing (Ring 1, < 2s)
```bash
# Sliced Domain Unit Test (fastest inner loop):
dotnet run --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*<TestClassName>/*"

# Sliced Application Unit Test:
dotnet run --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*<TestClassName>/*"
```

### Phase Exit — Target Project Build & Test (Ring 2, < 15s)
```bash
# 1. Clean build of affected project only:
dotnet build <TargetProject.csproj> --configuration Release --verbosity quiet --nologo

# 2. Run affected test project with Release configuration:
dotnet test <TestProject.csproj> --configuration Release --verbosity quiet --nologo --no-build

# 3. Targeted test execution by method/class filter:
dotnet test <TestProject.csproj> --configuration Release --verbosity quiet --nologo --no-build --filter "FullyQualifiedName~<TestClassName>"

# 4. Architecture guardrail check:
dotnet test tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet --nologo --no-build
```

### Plan Exit & PR Readiness (Ring 3 Gate)
```bash
# 1. Full solution build:
dotnet build Explore.slnx --configuration Release --verbosity quiet

# 2. Code formatting verification on changed files:
dotnet format Explore.slnx --verify-no-changes --verbosity quiet

# 3. Full multi-provider persistence tests (Testcontainers required):
dotnet test tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release
```

---

## 6. Verification Evidence Report Contract

Every task completion, milestone report, or PR description MUST provide a concrete **Verification Evidence Report**:

1. **Commands Executed & Exact Results**: State each command run, exit code (`0` / non-zero), test counts (passed, failed, skipped), and execution duration.
2. **Checks Not Executed & Technical Justification**: Explicitly list relevant checks that were omitted and why (e.g., *"Ring 3 multi-database matrix skipped because changes were strictly confined to Blazor Client CSS; verified via Explore.Blazor.Client.Tests"*).
3. **Zero Fictitious Evidence**: Never state, imply, or suggest that a build or test suite passed if the command was not actually executed in this session.
