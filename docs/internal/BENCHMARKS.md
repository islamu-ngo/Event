ABOUTME: Documents BenchmarkDotNet performance benchmarks and separates them from cold-start agent benchmarks.
ABOUTME: Defines run commands, interpretation rules, and limits so benchmark results are not treated as production SLO proof.

# Benchmarks

> **Audience:** Contributors | Operators | AI agents
> **Status:** Mixed
> **Owner:** Contributor Experience
> **Last Verified:** 2026-05-28
> **Source Anchors:** `Event.Benchmarks/`, `Event.Benchmarks/Configuration/BenchmarkConfig.cs`, `Event.Benchmarks/Program.cs`, `Directory.Packages.props`, `.agents/benchmarks/README.md`, `dev/pause/performance-optimization/performance-optimization-context.md`

This repository has two benchmark families with different goals:

| Benchmark Family | Purpose | Primary Source |
|---|---|---|
| Runtime microbenchmarks | Compare hot-path implementation choices inside the product codebase. | `Event.Benchmarks/` |
| Cold-start agent benchmarks | Evaluate whether a fresh AI agent can make architecture-compliant changes using only repo context. | `.agents/benchmarks/` |

Do not mix these result types. Runtime benchmark numbers are controlled-run microbenchmark evidence. Cold-start benchmark results are contribution-context evidence.

## Runtime Benchmark Project

`Event.Benchmarks` is a `net10.0` BenchmarkDotNet console project. It references `Explore.Application`, `Explore.Domain`, `Explore.Persistence`, and `Explore.API` so benchmark scenarios can exercise both isolated application hot paths and representative API requests through an in-process ASP.NET Core host.

BenchmarkDotNet is centrally pinned in `Directory.Packages.props` and the project uses `BenchmarkSwitcher` from `Event.Benchmarks/Program.cs`, so filters are passed through BenchmarkDotNet arguments.

### Current Suites

| Suite | What It Compares | Source |
|---|---|---|
| API endpoints | Representative anonymous and authenticated `GET` requests through `WebApplicationFactory<Program>`, including middleware, routing, authorization substitution, output caching, HAL/minimal response behavior, and JSON serialization. | `Event.Benchmarks/Benchmarks/ApiEndpointBenchmarks.cs` |
| API endpoints with PostgreSQL | Same representative `GET` scenarios backed by PostgreSQL/Testcontainers, the current EF Core model schema, Npgsql, PostgreSQL constraints, benchmark seed data, and a no-op output-cache store so repeated iterations continue through controller, EF Core, Npgsql, and PostgreSQL work. | `Event.Benchmarks/Benchmarks/ApiEndpointPostgreSqlBenchmarks.cs` |
| Serialization | Source-generated `System.Text.Json` serialization/deserialization vs reflection-based serialization for `EventListDto`. | `Event.Benchmarks/Benchmarks/SerializationBenchmarks.cs` |
| EF Core query construction | Tracked query construction, no-tracking query construction, and compiled query invocation. | `Event.Benchmarks/Benchmarks/EfCoreQueryBenchmarks.cs` |
| Caching collections | Lookup behavior for `FrozenDictionary`, `Dictionary`, and `ConcurrentDictionary`, plus enumeration behavior for `FrozenDictionary` and `Dictionary`, at fixed sizes. | `Event.Benchmarks/Benchmarks/CachingBenchmarks.cs` |
| Collection processing | List/span/array lookup, LINQ vs manual loops, and `FrozenSet.Contains`. | `Event.Benchmarks/Benchmarks/CollectionBenchmarks.cs` |
| String processing | Substring/span slicing, string concatenation, `StringBuilder`, contains, and GUID formatting. | `Event.Benchmarks/Benchmarks/StringProcessingBenchmarks.cs` |

## How To Run Runtime Benchmarks

Run benchmarks from the repository root and use Release configuration:

```bash
dotnet build --configuration Release --verbosity quiet
dotnet run --configuration Release --project Event.Benchmarks -- --filter "*"
```

Useful BenchmarkDotNet filters:

```bash
dotnet run --configuration Release --project Event.Benchmarks -- --list flat
dotnet run --configuration Release --project Event.Benchmarks -- --filter "*ApiEndpointBenchmarks*"
dotnet run --configuration Release --project Event.Benchmarks -- --filter "*ApiEndpointPostgreSqlBenchmarks*"
dotnet run --configuration Release --project Event.Benchmarks -- --filter "*Serialization*"
dotnet run --configuration Release --project Event.Benchmarks -- --filter "*Caching*"
```

The shared benchmark config enables memory, threading, and exception diagnosers plus GitHub Markdown, HTML, and full JSON exporters. It also fails benchmark validation on execution errors or missing JIT optimizations. No custom artifact path is configured, so BenchmarkDotNet writes to its default artifact directory for the run.

### API Endpoint Benchmark Harness

`ApiEndpointBenchmarks` hosts the real API with `Microsoft.AspNetCore.Mvc.Testing` and the `Testing` environment. The benchmark host intentionally mirrors integration-test startup overrides while staying self-contained inside `Event.Benchmarks`:

- Replaces production PostgreSQL registration with an EF Core InMemory database seeded through `DatabaseSeeder`, benchmark-owned API seed data, and lookup-cache refresh.
- Replaces distributed cache with in-memory distributed cache so output-cache and lookup-cache code paths remain active without Redis.
- Replaces external Cerbos authorization with an allow-all `IAuthorizationProvider` so endpoint measurements focus on API pipeline, serialization, caching, and data-access overhead rather than network policy latency.
- Uses a benchmark-only `X-Benchmark-Auth` header for authenticated scenarios and leaves requests anonymous when the header is absent.
- Measures both `Prefer: return=minimal` responses and HAL-rich responses where the API supports that distinction.

The benchmark-owned seed path creates the default tenant, one benchmark user/actor pair, and 24 published public events with stable slugs (`benchmark-api-event-*`). This keeps `/api/event` and `/api/event/my` measurements representative enough to serialize non-empty payloads without depending on Development seed data or changing product seeding behavior.

Current API scenarios target high-value read paths: event list minimal/HAL responses, category lookup with categories, system onboarding status, authenticated "my events", and event creation context. Add new scenarios by extending the `Scenarios` collection; keep write endpoints in a separate benchmark class because validation, idempotency, auth, and persistence side effects need different setup and cleanup.

### PostgreSQL API Endpoint Benchmark Harness

`ApiEndpointPostgreSqlBenchmarks` runs the same read scenario catalog against a PostgreSQL container created with Testcontainers. Its `GlobalSetup` starts the pinned `postgres:18-alpine` container, creates the API host with the container connection string, creates the schema from the current EF Core model, applies PostgreSQL model constraints, seeds lookup tables plus benchmark-owned events, and creates the reusable `HttpClient`. The benchmark method only sends the HTTP request and reads the response, so container startup, schema creation, and seeding are not included in measured endpoint timings.

Unlike the default in-memory API suite, this PostgreSQL suite replaces the output-cache store with a benchmark-only no-op store. The API middleware and `[OutputCache]` attributes still execute, but responses are not replayed from cache between measured iterations; this keeps the suite useful for EF Core, Npgsql, and PostgreSQL query-path comparisons instead of measuring warmed output-cache hits.

The benchmark uses `EnsureCreatedAsync` instead of migration application so active-development model changes can be measured before a migration file exists. Use API or persistence integration tests when the question is migration correctness; use this benchmark when the question is current-model PostgreSQL query execution and API response cost.

Use this suite when the performance question involves EF Core, Npgsql, SQL translation, PostgreSQL constraints, indexes, query execution, or transaction behavior. It still runs through ASP.NET Core `TestServer`, so it is database-faithful but not network-faithful; use deployed load tests when the question includes sockets, reverse proxies, TLS, Redis, Keycloak, Cerbos latency, or multi-instance traffic.

## Interpreting Runtime Results

Use runtime benchmark results to compare implementation choices under controlled microbenchmark conditions:

- Compare relative means, allocations, and diagnoser output between benchmark methods in the same suite.
- Keep hardware, runtime, branch, environment variables, and local machine load stable when comparing runs.
- Re-run a benchmark after changing the hot path and compare against a baseline from the same machine or CI runner.
- Treat large regressions as investigation triggers, not automatic release blockers, unless the change has an agreed performance budget.

### What Runtime Benchmarks Do Not Prove

Runtime microbenchmarks are not production load tests:

- They do not prove API P95/P99 latency, throughput, or production SLO compliance.
- They do not model multi-instance traffic, reverse proxies, Keycloak, Redis, MinIO, browser behavior, or real tenant concurrency.
- API endpoint benchmarks use ASP.NET Core `TestServer`, not a real socket, reverse proxy, or deployed container.
- The default API endpoint harness uses EF Core InMemory for deterministic local runs; use `ApiEndpointPostgreSqlBenchmarks` when the performance question is database execution, query plans, indexes, or transaction behavior.
- EF Core query benchmarks in this project build or invoke query shapes; they do not execute a representative live database workload.
- Synthetic collection/string/cache inputs are implementation-relative and should not be treated as user workload evidence.
- BenchmarkDotNet reports are evidence for a run, not permanent claims about future runtime versions or infrastructure.

Use [OPERATIONS.md](OPERATIONS.md), [TROUBLESHOOTING.md](TROUBLESHOOTING.md), and production telemetry for runtime incident analysis. Use dedicated load or end-to-end tests when the question is capacity, concurrency, or service-level behavior.

## Cold-Start Agent Benchmarks

`.agents/benchmarks/README.md` documents a separate manual benchmark suite for AI-agent contribution quality. Those scenarios test whether a fresh agent can classify intent, read required context, stay in scope, run expected verification, and satisfy acceptance criteria.

Cold-start benchmark results should be recorded under `dev/_journal/benchmark-reports/` as described by `.agents/benchmarks/README.md`. They are not runtime performance measurements and should not be cited as product throughput or latency evidence.

## Related Documentation

- [OPERATIONS.md](OPERATIONS.md) - runtime operation and observability context.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - issue diagnosis and practical recovery.
- [TESTING.md](TESTING.md) - test project and verification command policy.
- [GOVERNANCE.md](GOVERNANCE.md) - engineering governance and context-system benchmark references.
- [../../.agents/benchmarks/README.md](../../.agents/benchmarks/README.md) - cold-start agent benchmark procedure.
