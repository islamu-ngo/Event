# Bounded Test Execution Profiles & Container Resource Governance

> **Audience:** Contributors | Architects | Operators | AI agents
> **Status:** Accepted
> **Owner:** Contributor Experience & Testing Infrastructure
> **Last Verified:** 2026-11-12
> **Source Anchors:** `Directory.Build.props`, `.agents/hooks/BuildCheck.cs`, `tests/Shared/Execution/TestExecutionProfile.cs`, `tests/Shared/Execution/BoundedTestDiscoveryHook.cs`, `tests/Shared/Containers/ContainerResourceBounds.cs`, `tests/Shared/Containers/TestcontainersBuilderExtensions.cs`, `tests/Event.Architecture.Tests/TestExecutionGovernanceArchitectureTests.cs`

---

## 1. Problem Context

Without explicit resource bounds, running `.NET 10` builds and TUnit integration suites on laptops, shared Linux desktops, or constrained CI/remote VMs triggers overlapping multi-gigabyte resource spikes:

1. **Unconstrained TUnit parallelism**: TUnit defaults `MaximumParallelTests` to `4 * Environment.ProcessorCount` unless overridden. On an 8–16 logical core workstation, integration projects spin up 32–64 parallel test contexts and multiple heavyweight Docker containers (`WebApplicationFactory`, PostgreSQL, Keycloak, RabbitMQ, Cerbos, SQL Server, MySQL/MariaDB) simultaneously.
2. **Eager multi-engine fixtures & inline container duplication**: `AdmissionAuthorityProviderFixture` previously started SQL Server, MariaDB, and MySQL containers eagerly in `InitializeAsync()` via `Task.WhenAll(...)` even when a test executed only one engine, while individual tests constructed inline `PostgreSqlBuilder` instances or legacy static `[assembly: ParallelLimiter<T>]` attributes ignored CLI `--maximum-parallel-tests` overrides.
3. **Unbounded Docker container RAM / JVM heap**: Testcontainers defaults left container memory unlimited, allowing Keycloak JVM heaps and database buffers to balloon or hit Docker Desktop VM limits unpredictably. Conversely, capping PostgreSQL too aggressively at `512 MiB` caused the Linux OOM killer to terminate `postgres` during EF Core `MigrateAsync()` and native security contract installation.
4. **MSBuild / Roslyn daemon persistence & `/tmp` `tmpfs` exhaustion**: Background compiler servers (`VBCSCompiler`), MSBuild server/node reuse daemons, and full-solution `BuildCheck.cs` hook runs kept gigabytes resident after commands finished, while RAM-backed Linux `/tmp` `tmpfs` mounts exhausted host RAM during artifact/container scratch writes.

---

## 2. Decision

We adopt **adaptive memory-aware C# execution profiles paired with native opt-in MSBuild throttling, container resource ceilings, and strict sequential project execution**—requiring zero custom shell wrapper scripts in `eng/`:

### A. TUnit Adaptive Concurrency (`BoundedTestDiscoveryHook` + `TestExecutionProfile`)

All **16** TUnit projects across `tests/` and `eng/` share `tests/Shared/Execution/TestExecutionProfile.cs` and `tests/Shared/Execution/BoundedTestDiscoveryHook.cs` (`[assembly: Hook]`), replacing static `[assembly: ParallelLimiter<T>]` attributes (`ApiTestParallelLimit.cs` and `ClientTestParallelLimit.cs` deleted). `Event.Architecture.Tests` (`TestExecutionGovernanceArchitectureTests`) fails closed if any TUnit assembly lacks `BoundedTestDiscoveryHook` or reintroduces static `ParallelLimiterAttribute`s.

`MaximumParallelTests` resolution follows a strict override chain:
`--maximum-parallel-tests` CLI flag `>` `TUNIT_MAX_PARALLEL_TESTS` `>` `ISLAMU_TEST_PARALLEL_LIMIT` `>` `ISLAMU_EXECUTION_PROFILE` (`constrained` | `balanced` | `performance`) `>` adaptive memory probe (Linux `/proc/meminfo` `MemAvailable` + ancestor-walking cgroup v2 `memory.max` - `memory.current` across systemd/container slices, or cross-platform `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` on Windows/macOS, bounded optionally by `ISLAMU_CONTAINER_BUDGET_GIB` for Docker Desktop/WSL2/VM hosts).

| Profile (`ISLAMU_EXECUTION_PROFILE`) | `UnitPure` | `IntegrationContainer` | `Distributed` | Intended Host |
|---|---|---|---|---|
| `constrained` | `2` (`1` when RAM `< 12 GiB`) | `1` | `1` | Low-RAM laptops, concurrent multi-tasking, AI agents |
| `balanced` *(default)* | `2`–`6` scaled by available RAM | `1` (`< 24 GiB`), `2` (`< 48 GiB`), `3` (`>= 48 GiB`) | `1` | Everyday contributor workstations & CI |
| `performance` | Up to `Environment.ProcessorCount` | Up to `4` | `2` | Dedicated high-memory (48+ GiB) remote build hosts |

### B. Container Memory Ceilings, Lazy Provider Boot & Isolated Logical Databases

Every Testcontainers builder chains `.ApplyBoundedResourcePolicy(...)` (`tests/Shared/Containers/TestcontainersBuilderExtensions.cs` + dependency-free `tests/Shared/Containers/ContainerResourceBounds.cs`) enforcing Docker `HostConfig.Memory` ceilings and optional Keycloak JVM caps (`JAVA_OPTS_APPEND`), plus safe reuse suppression:

- **Default memory ceilings (`ContainerServiceRole`)**: `Redis` / `Mailpit` (`128 MiB`), `Cerbos` / `Svix` (`256 MiB`), `RabbitMq` / `Generic` (`512 MiB`), `MySql` / `MariaDb` (`768 MiB`), `PostgreSql` / `PostGis` / `Keycloak` (`1024 MiB`), `SqlServer` (`2048 MiB`).
- **Environment overrides**: `ISLAMU_CONTAINER_MEMORY_MB` and `ISLAMU_KEYCLOAK_HEAP_MB`.
- **Worst Break #2 isolation guard**: Opt-in `ISLAMU_TEST_CONTAINER_REUSE=true` is unconditionally forced to `false` when `disableReuse`, `hasAssuranceOwner`, or custom container names are active so `AssuranceOwnershipContext` ownership checks never trip on pre-existing containers.
- **Lazy provider boot**: `AdmissionAuthorityProviderFixture` and `SemanticValueNonTransactionalProviderFixture` follow `MySqlMigrationGuardFixture`'s `IAsyncDisposable` + `SemaphoreSlim(1, 1)` lazy startup + async `CreateOptionsAsync(...)` contract, starting only the single engine requested by a test.
- **Logical DB isolation**: `PostgreSqlContainerFixture.CreateIsolatedDatabaseAsync()` provisions unique `test_<guid>` databases inside a shared `PerAssembly` container and drops them (`WITH (FORCE)`) on fixture disposal.

### C. Opt-In MSBuild Constrained Mode & Scoped C# `BuildCheck` Hook

- `Directory.Build.props` defines `<PropertyGroup Condition="'$(ISLAMU_EXECUTION_PROFILE)' == 'constrained'">` setting `<UseSharedCompilation>false</UseSharedCompilation>` and `<BuildInParallel>false</BuildInParallel>`, leaving default, CI, and `performance` builds completely unchanged.
- `.agents/hooks/BuildCheck.cs` reads `.claude/build-cache` session entries, filters paths strictly to the active repository root, compiles only touched `.csproj` files sequentially in `Release` mode (falling back to `Explore.slnx` when cache has no project hints), injects `-m:1 -nr:false -p:UseSharedCompilation=false -p:BuildInParallel=false` + SDK env vars when `constrained`, and exposes `--dry-run`.
- Instead of maintaining a custom `eng/scripts/*.sh` CLI wrapper script across fast repository changes, contributors and agents pair `ISLAMU_EXECUTION_PROFILE=constrained` with standard `.NET` SDK variables (`DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, `MSBUILDDISABLENODEREUSE=1`, `DOTNET_GCConserveMemory=5`) and a disk-backed `0700` `TMPDIR`.

---

## 3. Alternatives Considered

1. **Checked-in Bash wrapper script (`eng/scripts/dotnet-bounded.sh`) with mandatory `flock`**: Rejected after maintainer review because a bespoke shell wrapper introduces maintenance drag across CLI evolution and duplicates native `.NET` SDK flags (`-m:1 -nr:false`) and conditional MSBuild properties. Machine-local `systemd-run --user --scope` / `flock` remains documented externally in `docs/internal/OPERATIONS.md` as an optional operator snippet.
2. **Global static `[assembly: ParallelLimiter<T>]` attributes**: Rejected because compile-time attributes cannot inspect host RAM, distinguish `constrained` vs `performance` workstations, or yield cleanly to `--maximum-parallel-tests`.
3. **Uniform `512 MiB` PostgreSQL container cap**: Rejected after empirical OOM failure (`EndOfStreamException` during `PostgreSqlContainerFixture.MigrateAsync()` + security contract installation); raised `PostgreSql` / `PostGis` floor to `1024 MiB` and re-verified green.

---

## 4. Consequences

- **Low-RAM & concurrent workstations**: Builds and container tests run predictably inside tight RAM budgets without orphaned `VBCSCompiler` processes or `/tmp` exhaustion, while passing `--no-build` avoids redundant MSBuild graph evaluations.
- **High-RAM remote hosts & CI**: Preserve full throughput with `balanced`/`performance` defaults or explicit CLI/env overrides without code changes.
- **Architectural enforcement**: Removing `BoundedTestDiscoveryHook` or adding static `ParallelLimiter` attributes fails `TestExecutionGovernanceArchitectureTests` immediately.
