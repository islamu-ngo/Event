# ISLAMU Event — Bounded Test Execution & Resource Governance — I-VSD Planning Report

Last Updated: 2026-11-12 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: Hardware-aware, profile-driven concurrency and memory governance across .NET builds, TUnit test suites, Testcontainers, and multi-agent context engineering
- Workstream: bounded-test-execution-and-resource-governance
- Report kind: provider-responsibility planning report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-12
- Reviewed input: `develop` (`8b890d71da`), `TUnit.Core` (`1.63.0` XML contract), resolved `/grill-me` Option B decision, CTO execution-readiness audit (`2026-11-11`), scriptless simplification (`2026-11-12`), and completed triad `dev/active/bounded-test-execution-and-resource-governance/`
- Supersedes: none (complements `islamic-value-sensitive-design/workstreams/i-vsd-agentic-testing-acceleration.md`)

---

## Executive Summary

When AI agents or contributors run builds and integration tests in ISLAMU Event on a workstation with constrained free RAM (for example, 14 GiB available alongside an IDE, browser, and Docker Desktop VM), multiple independent defaults compound simultaneously to drive memory above 99% and trigger OS out-of-memory (OOM) kills across unrelated user applications. Repository inspection confirms this is not caused by one single command: `TUnit` (`1.63.0`) defaults to `4× CPU cores` (`64` concurrent slots on a 16-core host) in every test assembly lacking a `ParallelLimiter`; `105` `[ClassDataSource<...>(Shared = SharedType.PerClass)]` usages, inline per-test `PostgreSqlBuilder` allocations (`RegistrationFinalizationEffectPersistenceTests`), and eager 3-database startup (`AdmissionAuthorityProviderFixture` starting SQL Server + MariaDB + MySQL via `Task.WhenAll`) spawn dozens of containers concurrently; 47-project full-solution MSBuild invocations run across all cores with persistent Roslyn/MSBuild server daemons; `implement-tasks/SKILL.md` permits up to `3–5` parallel spoke worktrees plus bare solution-wide `dotnet test`; and `/tmp` on Linux hosts using RAM-backed `tmpfs` absorbs `Path.GetTempPath()` test artifacts whenever subagents omit `TMPDIR`.

Following `/grill-me` alignment, the maintainer approved **Option B (Memory-Adaptive Auto-Sizing) with conservative lower defaults while preserving full overridability and configurability**, and subsequently requested a **scriptless C# + native MSBuild architecture** (dropping `eng/scripts/dotnet-bounded.sh`). The plan triad in [`bounded-test-execution-and-resource-governance-plan.md`](../../dev/active/bounded-test-execution-and-resource-governance/bounded-test-execution-and-resource-governance-plan.md) replaces TUnit's `4× CPU cores` default across all `17` TUnit assemblies with `TestExecutionProfile` (`1` for container suites below `24 GiB` available RAM, capped `1–3` even on large hosts unless overridden; `1` unconditional default for AppHost `Distributed`; `2–6` for pure unit suites) yielding cleanly to `--maximum-parallel-tests`, `TUNIT_MAX_PARALLEL_TESTS`, `ISLAMU_TEST_PARALLEL_LIMIT`, and `ISLAMU_EXECUTION_PROFILE` (`constrained`, `balanced`, `performance`). Combined with universal container memory ceilings, lazy provider initialization, opt-in `Directory.Build.props` MSBuild bounds, targeted C# `BuildCheck.cs` hook execution, native SDK env vars / NVMe `TMPDIR`, and synchronized sequential multi-harness instructions, all four I-VSD findings (`IVSD-F001`–`IVSD-F004`) map directly to verifiable tasks (`current` | `plan-aligned`).

---

## Scope

In scope for provider-responsibility evaluation during planning intake:
1. **Universal Container & Fixture Efficiency (`tests/`, `eng/*/tests/`)**: Eliminating unnecessary per-test and multi-provider simultaneous container launches, bounding container/JVM memory (`HostConfig.Memory`, Keycloak heap flags), and normalizing `TUnit` concurrency hooks (`Before(HookType.TestDiscovery)`, retiring static assembly `ParallelLimiter` shims) with strict override precedence (`--maximum-parallel-tests` → `TUNIT_MAX_PARALLEL_TESTS` → `ISLAMU_TEST_PARALLEL_LIMIT` → `ISLAMU_EXECUTION_PROFILE` → adaptive lower fallback).
2. **Scriptless Profile-Driven Build & Hook Controls (`Directory.Build.props`, `.agents/hooks/BuildCheck.cs`)**: Providing opt-in sequential/memory-capped execution (`constrained` profile: `UseSharedCompilation=false`, `BuildInParallel=false`, `-m:1 -nr:false`, `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, `MSBUILDDISABLENODEREUSE=1`, `DOTNET_GCConserveMemory=5`, NVMe `TMPDIR`, targeted Release hook builds, plus optional local session `systemd-run`/`flock` guidance) without introducing custom repo wrapper scripts, while keeping fast parallel builds and tests on high-RAM remote machines (`performance` profile) and CI.
3. **Agentic Context & Multi-Harness Alignment (`AGENTS.md`, `AGENTS.local.md`, twin rules `.agents/rules/tests.md` / `.omo/rules/tests.md`, `implement-tasks`, `intents.yaml`, `PROJECTS.md`, `CONTRIBUTING.md`, `docs/`)**: Removing conflicting bare `dotnet test` / solution-wide build instructions, gating concurrent spoke builds/tests, and keeping machine-local constraints in gitignored `AGENTS.local.md` using portable paths.

Out of scope: modifying product runtime semantics in `src/**`, weakening or deleting security/persistence/privacy test coverage, relaxing CI gates, or issuing religious-legal rulings.

---

## Claim Boundary

All repository claims reflect branch `develop` at `8b890d71da` (plus observed local `AGENTS.local.md` and uncommitted local SDK docs diffs left untouched), inspected `TUnit.Core` `1.63.0` XML documentation, and official Microsoft Learn / Testcontainers / systemd documentation reviewed on `2026-11-11`. Host measurements (`16` cores, `61 GiB` RAM, `31 GiB` `tmpfs` `/tmp`, Docker Desktop `MemTotal=8320741376`, `DefaultMemoryAccounting=yes`) reflect Amir's local Linux workstation only; other contributors' hardware varies and is explicitly handled through cross-platform fallback (`GC.GetGCMemoryInfo()`) and explicit overrides. `plan-aligned` status confirms complete mapping in the planning triad; it is not runtime execution proof, which awaits implementation Phases 1–5.

---

## Findings

### `IVSD-F001`: Workstation Resource Exhaustion & Collateral App Termination (`Raf' al-Haraj` / `Non-Harm` / `Israf`)
- **Concrete situation:** When an AI agent investigates or refactors tests (`test-suite-rationalization` or ordinary integration work), running `dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj` without `--maximum-parallel-tests 1` triggers TUnit's built-in `4× CPU cores` concurrency ceiling (`64` slots on a 16-core machine) with zero assembly-level `ParallelLimiter`. Concurrent `SharedType.PerClass` fixtures (`105` usages repo-wide), inline per-test `PostgreSqlBuilder` allocations (`RegistrationFinalizationEffectPersistenceTests`), eager 3-database startup (`AdmissionAuthorityProviderFixture` launching SQL Server `1024 MB` + MariaDB + MySQL via `Task.WhenAll`), multi-container `SvixConformanceContainerFixture`, `KeycloakContainerFixture` JVMs, 47-project MSBuild worker nodes (`-m:16`), Debug `BuildCheck.cs` hook runs, and un-redirected `tmpfs` `/tmp` spills quickly saturate 14+ GiB of free RAM, forcing the kernel OOM killer to terminate the developer's desktop apps, browser, and IDE.
- **Provider-controlled choice:** Maintainers control test assembly concurrency defaults, fixture container sharing lifecycles, MSBuild hook behavior, agent orchestration instructions, and contributor runner scripts.
- **Mechanism:** TUnit `1.63.0` resolves `MaximumParallelTests` in strict precedence (`--maximum-parallel-tests` → `TUNIT_MAX_PARALLEL_TESTS` → `TUnitSettings` in `[Before(HookType.TestDiscovery)]` → built-in `4× CPU cores`), while `[assembly: ParallelLimiter<T>]` enforces a separate semaphore (`ClientTestParallelLimit` = `8`, `ApiTestParallelLimit` = `8`, absent elsewhere). Combining unthrottled assemblies with per-class/per-test container creation multiplies Docker VM and .NET host memory linearly with CPU core count.
- **Ethical reasoning:** Software governance carries a duty to prevent foreseeable operational harm (`Non-Harm`) and remove avoidable developer hardship (`Raf' al-Haraj`) and computational waste (`Israf`). Shipping defaults where an ordinary contributor test command can freeze or crash a workstation violates responsible stewardship (`Amanah`) of shared engineering infrastructure.
- **Mitigation:** `IVSD-M001` (Option B lower memory-aware defaults, container consolidation/caps, lazy provider startup, serialized local wrapper + cgroup containment, and NVMe `TMPDIR`).

### `IVSD-F002`: Unfair Slowdown & Rigidity for Contributors and High-RAM Remote Environments (`Adl` / `Amanah`)
- **Concrete situation:** Hardcoding `-m:1`, `UseSharedCompilation=false`, `Limit => 1`, or mandatory sequential locking directly into universal repo settings without an override mechanism would degrade build and test velocity for contributors on 64–128 GiB workstations, remote cloud machines, and multi-core CI agents. Conversely, memory-only auto-sizing without explicit overrides or lower caps could misread Docker Desktop's separate VM limit or behave unpredictably across runs.
- **Provider-controlled choice:** Maintainers choose how auto-sizing balances conservative safety with explicit remote/CI override flexibility.
- **Mechanism:** Using `TestExecutionProfile` inside `[Before(HookType.TestDiscovery)]` across all `17` TUnit assemblies (retiring static `ApiTestParallelLimit`/`ClientTestParallelLimit` classes; lower `balanced` defaults capped at `1` below `24 GiB` `MemAvailable` and max `3` at `>= 48 GiB`, `1` for `Distributed`), keeping MSBuild throttling opt-in in `Directory.Build.props` (`ISLAMU_EXECUTION_PROFILE=constrained`) and `.agents/hooks/BuildCheck.cs`, and preserving `--maximum-parallel-tests` / `TUNIT_MAX_PARALLEL_TESTS` / `ISLAMU_EXECUTION_PROFILE=performance` precedence protects constrained hosts while keeping high-RAM remote machines fast and fully configurable.
- **Ethical reasoning:** Justice (`Adl`) toward contributors with diverse hardware requires neither crashing modest laptops nor crippling high-capacity workstations and remote runners; transparent, reversible profiles honor all contributors fairly.
- **Mitigation:** `IVSD-M002` (Option B lower conservative defaults + three-tier `ISLAMU_EXECUTION_PROFILE` override contract + `AGENTS.local.md` per-machine setup).

### `IVSD-F003`: Deceptive Verification Risk Under Resource Pressure or Disabled Reapers (`Sidq` / `Amanah`)
- **Concrete situation:** Under RAM pressure or slow container startup, an agent or contributor could be tempted to weaken coverage (skipping Docker-backed suites, replacing container tests with in-memory mocks, or leaving `TESTCONTAINERS_RYUK_DISABLED=true` without deterministic cleanup, which already left exited/created `postgres-*` containers in `docker ps -a`).
- **Provider-controlled choice:** Maintainers define the verification boundary in `.agents/rules/tests.md`, `TESTING.md`, and `intents.yaml`.
- **Mechanism:** Separating *how many suites/containers run concurrently* from *which invariants must pass* preserves every Domain, Application, Architecture, API, Persistence, and Ring 3 multi-provider check intact, while explicit `--minimum-expected-tests` floors, `ContainerResourceBounds`, and clean lifecycle disposal prevent silent skips or mock-mirroring from passing as green.
- **Ethical reasoning:** `Sidq` (truthfulness in engineering evidence) forbids trading real provider assurance for faster runs via mock-mirroring or silent skips; every profile must execute the same assertions, differing only in concurrency and memory envelope.
- **Mitigation:** `IVSD-M003` (zero test weakening; invariant parity across profiles + deterministic Testcontainers cleanup).

### `IVSD-F004`: Multi-Harness Drift & Local Environment Leakage (`Amanah` / `Sirr`)
- **Concrete situation:** Several repo instructions already diverge: `.github/workflows/_build-test.yml` passes `--maximum-parallel-tests 1` and `-p:RunAnalyzers=false` on fast builds, whereas `AGENTS.md`, `PROJECTS.md`, `TESTING.md` lines `246–251`, `intents.yaml`, `cold-start-tasks.yaml`, and `implement-tasks/SKILL.md` line `190` (`dotnet test`) emit unbounded commands; meanwhile `AGENTS.local.md` sets workstation-specific paths (`$HOME/.cache/agent-tmp`) that subagents miss when not mirrored or if hardcoded into tracked files.
- **Provider-controlled choice:** Maintainers govern twin rules (`.agents/rules/tests.md` and `.omo/rules/tests.md`), `AGENTS.md`, skills, and contributor/hook portability.
- **Mechanism:** Resolving contradictions across `AGENTS.md`, `implement-tasks`, `intents.yaml`, `BuildCheck.cs`, `local-development.md`, and twin rules ensures every AI harness (Claude Code, OmO, Cursor, Copilot, Antigravity) follows the same profile contract, while portable environment variables (`${TMPDIR:-${XDG_CACHE_HOME:-$HOME/.cache}/agent-tmp}`) prevent personal host paths from entering Git.
- **Ethical reasoning:** Clear, consistent instructions across all supported harnesses prevent accidental violations and avoid exposing personal host layout (`Sirr`) in shared commits.
- **Mitigation:** `IVSD-M004` (cross-harness alignment, portable hook/env guidance, and `ISLAMU.AgentWorkflow` validation).

### Finding Index

| ID | Lifecycle | Severity | Claim type | Principle / Domain | Stakeholder | Provider-controlled decision | Evidence | Validation | Mitigation |
|---|---|---|---|---|---|---|---|---|---|
| `IVSD-F001` | open | High | Operational Harm / Waste | `Raf' al-Haraj`, `Non-Harm`, `Israf`, `Amanah` | Local contributors, workstation users, AI agents | Concurrency defaults, container lifecycle sharing, MSBuild/tmpfs bounds | `TUnit.Core.xml` (`4×` default), `tests/` audit (`105` `PerClass`, `AdmissionAuthorityProviderFixture`, `RegistrationFinalizationEffectPersistenceTests`), `df -h /tmp`, `docker info` | Plan Phase 1–3 | `IVSD-M001` |
| `IVSD-F002` | open | High | Contributor Fairness / Velocity | `Adl`, `Amanah` | All contributors, remote/CI environments | Option B conservative adaptive defaults + `constrained`/`balanced`/`performance` overrides | Resolved `/grill-me` Option B; `ApiTestParallelLimit.cs`, `ClientTestParallelLimit.cs`, `_build-test.yml` | Plan Phase 1, 3, 4 | `IVSD-M002` |
| `IVSD-F003` | open | Medium | Evidence Truthfulness | `Sidq`, `Amanah` | Maintainers, reviewers, self-hosters | Maintaining test/provider parity across profiles & container cleanup | `tests.md`, `docker ps -a` orphan containers, `TESTING.md` | Plan Phase 2, 5 | `IVSD-M003` |
| `IVSD-F004` | open | Medium | Governance Drift / Portability | `Amanah`, `Sirr` | Multi-harness AI agents, contributors | Aligning `AGENTS.md`, `implement-tasks`, `BuildCheck.cs`, twin rules & portable `TMPDIR` | `implement-tasks/SKILL.md#L190`, `BuildCheck.cs`, `intents.yaml`, `.omo/rules/tests.md`, `local-development.md#L57` | Plan Phase 3, 4 | `IVSD-M004` |

---

## Recommendations

- **`IVSD-M001` (Bounded Execution & Container Hygiene):** Implement `TestExecutionProfile` and `BoundedTestDiscoveryHook` across all `17` TUnit test assemblies (`14` in `tests/` + `3` in `eng/`), retire legacy static `ApiTestParallelLimit` and `ClientTestParallelLimit` shims, eliminate per-test container duplication (`RegistrationFinalizationEffectPersistenceTests`, `RegistrationConsentRecordPersistenceTests`, `PostgresModelConstraintApplierTests` via `CreateIsolatedDatabaseAsync`), make `AdmissionAuthorityProviderFixture` lazy async per-provider matching `MySqlMigrationGuardFixture`, enforce Docker container `HostConfig.Memory` + Keycloak JVM `-Xmx` caps, apply opt-in constrained MSBuild props (`UseSharedCompilation=false`, `BuildInParallel=false`) in `Directory.Build.props`, scope C# `BuildCheck.cs` cleanly to changed Release projects, and enforce sequential `-m:1 -nr:false` + `--no-build` + NVMe `TMPDIR` via agent rules without repo wrapper scripts.
- **`IVSD-M002` (Option B Lower Adaptive Defaults + Full Profile Configurability):** Use conservative available-memory tiers (`1` for container suites `< 24 GiB`, max `3` at `>= 48 GiB`; `1` unconditional default for `Distributed`; `2–6` for `UnitPure`) that yield strictly to `--maximum-parallel-tests` → `TUNIT_MAX_PARALLEL_TESTS` → `ISLAMU_TEST_PARALLEL_LIMIT` → `ISLAMU_EXECUTION_PROFILE`, so constrained local runs serialize reliably while remote high-RAM servers unlock fast multi-slot parallelism with one env var or CLI flag.
- **`IVSD-M003` (Invariant Parity & Clean Reaper Semantics):** Keep every Ring 1/2/3 assertion, `--minimum-expected-tests` floor, and real-provider check unchanged across profiles; support opt-in `ISLAMU_TEST_CONTAINER_REUSE=true` (blocked under `AssuranceOwnershipContext` named containers) with explicit clean disposal by default.
- **`IVSD-M004` (Multi-Harness Context Alignment & Portability):** Synchronize `AGENTS.md`, gitignored local `AGENTS.local.md`, twin rules (`.agents/rules/tests.md` + `.omo/rules/tests.md`), `implement-tasks/SKILL.md`, `PROJECTS.md`, `CONTRIBUTING.md`, `docs/internal/TESTING.md`, `docs/internal/OPERATIONS.md`, `docs/public/documentation/readme/contributing/local-development.md`, `cold-start-tasks.yaml`, and `intents.yaml` using portable paths.

*Rejected alternatives:*
- Unconditionally forcing `-m:1` and `Limit => 1` everywhere with no adaptive scaling (Option A: unnecessarily slows well-resourced workstations without overrides).
- Leaving integration defaults parallel in Git and enforcing bounds only locally (Option C: leaves unconfigured contributor laptops exposed to `4× CPU cores` OOM spikes).
- Querying `docker info` synchronously inside every `TestDiscovery` run (adds subprocess latency and risks violating zero-Docker fast unit-lane rules).

---

## Stakeholders

- **Primary Maintainer (`Amir Akrari`) — Constrained Local & Remote Profiles:** Protected from 14+ GiB RAM exhaustion on his 16-core Linux workstation (`tmpfs` `/tmp`, Docker Desktop VM) via `ISLAMU_EXECUTION_PROFILE=constrained` + `Directory.Build.props` + native C# TUnit/container caps + NVMe `TMPDIR` (plus optional terminal-session `systemd-run --user --scope` if desired), while switching seamlessly to `performance` on high-RAM remote hosts.
- **External Contributors:** Protected out of the box from TUnit's 64-way container flood on modest laptops while getting clean adaptive scaling and zero build slowdown on strong workstations.
- **AI Coding Agents & Multi-Agent Orchestrators (Antigravity, Claude Code, OmO, Cursor, Copilot):** Guided by unified sequential rules (`-m:1`, `--no-build`, one project at a time, portable `TMPDIR`) across twin files, skills, `BuildCheck.cs`, and `intents.yaml`.
- **CI/CD Runners (GitHub Actions):** Retain explicit `--maximum-parallel-tests 1` and `-p:RunAnalyzers=false` behavior without disruption.

---

## I-VSD Principles And Domains

- **`Raf' al-Haraj` (Removal of Avoidable Hardship) & `Non-Harm`:** Prevents OOM freezes that kill desktop applications and disrupt contributors.
- **`Israf` (Avoidance of Resource Waste):** Eliminates duplicate Debug/Release compiler graphs, idle Roslyn/MSBuild daemons, and redundant per-test database containers.
- **`Adl` (Fairness Across Contributor Contexts):** Balances conservative safety on constrained machines with configurable multi-core speed on remote/CI environments.
- **`Sidq` (Truthfulness of Verification Evidence):** Preserves real provider assertions without skipping tests or reverting to mocks.
- **`Amanah` & `Sirr` (Stewardship & Boundary Hygiene):** Separates shared repository engineering contracts from gitignored local workstation configuration (`AGENTS.local.md`).

---

## Validation Gaps

- Exact measured peak RSS and Docker VM memory consumption across `Event.Persistence.IntegrationTests`, `Event.API.IntegrationTests`, and `Explore.Infrastructure.Tests` under `constrained` vs `balanced` vs `performance` remain to be measured during Phase 5 execution.
- Remaining `105` `SharedType.PerClass` fixture sites require bounded audit in Phase 2 Task 2.3 before converting compatible classes to `SharedType.PerAssembly`.

---

## Escalation Needed

1. **Planning approval (`user`):** Implementation triad awaits user review (`/senior-cto-feedback` or direct approval to begin `/implement-tasks` Phase 1).
2. **Religious-legal escalation:** Not applicable (software engineering resource governance and developer tooling; no fiqh ruling requested or issued).

---

## Evidence Reviewed

- `TUnit.Core` `1.63.0` XML documentation (`~/.nuget/packages/tunit.core/1.63.0/lib/net10.0/TUnit.Core.xml` lines `8783–8803`, `9767–9783`): confirmed `BeforeTestDiscoveryContext.Settings`, `ParallelismSettings.MaximumParallelTests` default `4× CPU cores`, and precedence `--maximum-parallel-tests` → `TUNIT_MAX_PARALLEL_TESTS` → `TUnitSettings` → built-in default.
- Test assemblies & fixtures: `tests/Explore.Blazor.Client.Tests/ClientTestParallelLimit.cs`, `tests/Event.API.IntegrationTests/ApiTestParallelLimit.cs`, `tests/Event.API.IntegrationTests/Fixtures/ApiIntegrationTestEnvironment.cs`, `tests/Event.API.IntegrationTests/Fixtures/KeycloakContainerFixture.cs`, `tests/Event.API.IntegrationTests/Fixtures/CerbosContainerFixture.cs`, `tests/Event.Persistence.IntegrationTests/Fixtures/PostgreSqlContainerFixture.cs`, `tests/Event.Persistence.IntegrationTests/Fixtures/AdmissionAuthorityProviderFixture.cs`, `tests/Event.Persistence.IntegrationTests/RegistrationFinalizationEffectPersistenceTests.cs`, `tests/Explore.Infrastructure.Tests/Fixtures/SvixConformanceContainerFixture.cs`, `tests/Shared/AssuranceOwnershipContext.cs`.
- Build, CI & Agent Context: `Directory.Build.props`, `Explore.slnx` (`47` projects), `AGENTS.md`, `AGENTS.local.md`, `PROJECTS.md`, `CONTRIBUTING.md`, `docs/internal/QUICK_REFERENCE.md`, `docs/internal/TESTING.md`, `docs/internal/OPERATIONS.md`, `docs/public/documentation/readme/contributing/local-development.md`, `.agents/CONTEXT_ENGINEERING.md`, `.agents/rules/tests.md`, `.omo/rules/tests.md`, `.agents/agents/quality-verifier-agent.md`, `.agents/hooks/BuildCheck.cs`, `.agents/skills/implement-tasks/SKILL.md`, `.agents/contract/intents.yaml`, `.github/workflows/_build-test.yml`, `.github/workflows/grant-settlement-tests.yml`, `.github/workflows/performance-smoke.yml`.
- Host diagnostics: `nproc` (`16`), `free -h` (`61 GiB` RAM, `61 GiB` Swap), `df -h /tmp` (`31 GiB` `tmpfs`), `docker info` (`docker-desktop`, `MemTotal=8320741376`, `NCPU=16`), `systemctl --user show --property=DefaultMemoryAccounting` (`yes`), `git worktree list` (`23` worktrees).
- External official documentation via web search: TUnit parallelism & discovery hooks (`tunit.dev`), MSBuild `Directory.Build.rsp` / `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER` / `UseSharedCompilation` / `DOTNET_GCConserveMemory` / `DOTNET_GCHeapHardLimit` (`learn.microsoft.com`), Testcontainers for .NET `HostConfig.Memory` / `WithReuse` / Ryuk (`testcontainers.org`), and Linux `systemd-run --user --scope` memory limits.
- Completed planning triad: [`bounded-test-execution-and-resource-governance-plan.md`](../../dev/active/bounded-test-execution-and-resource-governance/bounded-test-execution-and-resource-governance-plan.md), [`bounded-test-execution-and-resource-governance-context.md`](../../dev/active/bounded-test-execution-and-resource-governance/bounded-test-execution-and-resource-governance-context.md), [`bounded-test-execution-and-resource-governance-tasks.md`](../../dev/active/bounded-test-execution-and-resource-governance/bounded-test-execution-and-resource-governance-tasks.md).

---

## Missing Evidence

- Live memory telemetry benchmarks comparing baseline parallel runs vs. profile-throttled runs on this workstation (collected in Phase 5 Task 5.2).
- Complete classification of all `105` `SharedType.PerClass` fixture sites (executed in Phase 2 Task 2.3).

---

## Context Inventory

- Sibling active workstream: `dev/active/testing-configuration-assurance/` + `islamic-value-sensitive-design/workstreams/i-vsd-testing-configuration-assurance.md` (configuration toggle parity and split identity database topology — distinct scope; zero file/design conflict).
- Historical sibling I-VSD report: `islamic-value-sensitive-design/workstreams/i-vsd-agentic-testing-acceleration.md` (established the 3-Ring progressive verification model and schema-per-run isolation).

---

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-11-11 | none | draft | Planning intake for `bounded-test-execution-and-resource-governance` | Repository, TUnit `1.63.0`, CI, and host evidence packet at `8b890d71da` |
| 2026-11-11 | draft | current | `/grill-me` resolved Option B (lower conservative defaults + full override precedence) and mapped all `IVSD-*` IDs 1:1 into completed triad | `dev/active/bounded-test-execution-and-resource-governance/` (`plan-aligned`) |
| 2026-11-12 | current | current | CTO audit (`2026-11-11`) & maintainer scriptless simplification (`2026-11-12`): dropped `eng/scripts/dotnet-bounded.sh` and `BoundedRunnerScriptTests` in favor of native C#, `Directory.Build.props`, `.agents/hooks/BuildCheck.cs`, SDK env vars, and sequential rules | `dev/active/bounded-test-execution-and-resource-governance/` + `event/work/active-bounded-test-execution-and-resource-governance.yaml` (`plan-aligned`) |

---

## Planning Handoff

- Workstream: `bounded-test-execution-and-resource-governance`
- Status: `current` (`plan-aligned`)
- Reviewed input: `develop` (`8b890d71da`) + `/grill-me` Option B decision + CTO refinement & scriptless simplification (`2026-11-12`) + `dev/active/bounded-test-execution-and-resource-governance/`
- Findings and mitigations: `IVSD-F001 -> IVSD-M001`, `IVSD-F002 -> IVSD-M002`, `IVSD-F003 -> IVSD-M003`, `IVSD-F004 -> IVSD-M004`
- Required plan mappings:
  - `IVSD-F001 / IVSD-M001` -> Scenarios 1.1, 2.1, 2.2, 3.1, 3.2, 3.3; Phase 1 Tasks `1.1`–`1.3`, Phase 2 Tasks `2.1`–`2.4`, Phase 3 Tasks `3.0`–`3.2` (`plan.md` §9)
  - `IVSD-F002 / IVSD-M002` -> Scenarios 1.2, 1.3, 3.1; Phase 1 Tasks `1.2`–`1.3`, Phase 3 Tasks `3.1`–`3.2`, Phase 4 Tasks `4.1`–`4.3` (`plan.md` §9)
  - `IVSD-F003 / IVSD-M003` -> Scenarios 2.1, 2.2; Phase 2 Tasks `2.2`–`2.4`, Phase 5 Task `5.2` (`plan.md` §9)
  - `IVSD-F004 / IVSD-M004` -> Scenario 4.1; Phase 3 Task `3.2`, Phase 4 Tasks `4.0`–`4.3` (`plan.md` §9)
- Escalations required before: `implementation` (user approval of the simplified plan triad).
- Refresh triggers: Changes to Option B adaptive defaults, override precedence, supported execution profiles, container memory bounds, or introduction of custom non-Bash/non-C# tooling.
