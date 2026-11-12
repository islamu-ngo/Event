---
description: Set up your local developer workstation to contribute code to ISLAMU Event.
---

# Local Development Guide

This guide helps developers set up a local development workstation to contribute to ISLAMU Event.

---

## 1. Prerequisites

To build and run the complete solution from source, install:

- **.NET 10 SDK** (v10.0.403 or compatible)
- **Docker Engine / Docker Desktop** (v24+ with Compose v2)
- **.NET Aspire CLI**:
  ```bash
  dotnet tool install -g Aspire.Cli
  ```
- **Git**

---

## 2. Clone & Run with .NET Aspire

We use [.NET Aspire](../self-hosting/dotnet-aspire-and-cloud.md) for local orchestration, automatically launching PostgreSQL, Keycloak, Mailpit, `Explore.API`, and `Explore.Blazor`:

```bash
# 1. Clone repository
git clone https://github.com/islamu-ngo/Event.git
cd Event

# 2. Copy configuration
cp .env.example .env

# 3. Launch with Aspire AppHost
aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
```

Open the **Aspire Dashboard** URL displayed in your terminal (typically `http://localhost:18888`) to view running resources, inspect logs, and navigate to the application UI.

---

## 3. Building and Testing

Compile a target project (or the solution) once in Release configuration before executing tests sequentially one project at a time:

```bash
dotnet build tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj \
  --configuration Release \
  -m:1 -nr:false \
  --verbosity quiet
```

Run tests using [TUnit](tunit.md) with `--no-build` so TUnit reuses the compiled binaries without triggering redundant MSBuild graphs:

```bash
# Run unit tests for the Application layer
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj \
  --configuration Release \
  --no-build \
  --verbosity quiet \
  -- --treenode-filter "/*/*/*<TestClass>/*"
```

### Execution Profiles & Low-Memory Workstations

All test suites automatically adapt TUnit parallelism and Testcontainers Docker memory limits based on host memory and optional environment settings:

* **`ISLAMU_EXECUTION_PROFILE`**: `constrained` (strictly serial builds/containers for laptops, SSH hosts, or concurrent workloads), `balanced` (default adaptive scaling), or `performance` (higher unit-test concurrency on dedicated multi-core hosts; explicit `--maximum-parallel-tests`, `TUNIT_MAX_PARALLEL_TESTS`, or `ISLAMU_TEST_PARALLEL_LIMIT` always win).
* **Low-RAM MSBuild & Roslyn caps** (when `ISLAMU_EXECUTION_PROFILE=constrained`): `Directory.Build.props` sets `UseSharedCompilation=false` and `BuildInParallel=false`; pair with `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, `MSBUILDDISABLENODEREUSE=1`, and `DOTNET_GCConserveMemory=5`.
* **Container memory & reuse controls**: Testcontainers default ceilings (`PostgreSql`/`PostGis`/`Keycloak` 1024 MiB, `SqlServer` 2048 MiB, `MySql`/`MariaDb` 768 MiB, `RabbitMq` 512 MiB, `Cerbos`/`Svix` 256 MiB, `Redis`/`Mailpit` 128 MiB) can be overridden via `ISLAMU_CONTAINER_MEMORY_MB` and `ISLAMU_KEYCLOAK_HEAP_MB`. Opt-in reuse (`ISLAMU_TEST_CONTAINER_REUSE=true`) is automatically suppressed on security-isolated fixtures.
* **Linux `tmpfs` `/tmp` protection**: On Linux systems where `/tmp` is RAM-backed (`tmpfs`), point `TMPDIR` at a disk-backed `0700` directory (for example `mkdir -m 700 -p "$HOME/.cache/agent-tmp" && export TMPDIR="$HOME/.cache/agent-tmp"`).

---

## 4. Architectural Rules & Specifications

Before authoring code, review our core architecture and clean-room policies:

* **[Clean Architecture Conventions](clean-architecture.md)** — Inward dependency rules and application operation boundaries.
* **[TUnit Testing Conventions](tunit.md)** — Writing invariant-breaker tests instead of tautological mocks.
* **[Clean-Room IP & Licensing](clean-room-ip-and-licensing.md)** — Independent design and outbound AGPLv3 protection.

For complete internal developer specifications in GitHub:
* 📖 [`docs/internal/DEVELOPER_GUIDE.md`](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/DEVELOPER_GUIDE.md) — 5-minute mental model and invariants.
* 📖 [`docs/internal/ARCHITECTURE_OVERVIEW.md`](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/ARCHITECTURE_OVERVIEW.md) — C4 diagrams and component interactions.

---

## Related Guides & Next Steps

* **[Clean Architecture Conventions](clean-architecture.md)** — Layer boundaries and CQRS handlers.
* **[TUnit Testing Conventions](tunit.md)** — Test slicing and execution.
* **[.NET Aspire & Cloud Deployment](../self-hosting/dotnet-aspire-and-cloud.md)** — Aspire AppHost orchestration details.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Run the split container topology locally.
