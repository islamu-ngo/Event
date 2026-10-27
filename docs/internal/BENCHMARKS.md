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
| API endpoints | Six retained read operations: public event list minimal/HAL, category types with categories, system onboarding status, native authenticated my-events and event-creation context. The former InMemory API profile is unsupported by current transaction and human-authority contracts; retirement is conditional on all six native PostgreSQL operations succeeding. | `tests/Event.Benchmarks/Benchmarks/ApiEndpointBenchmarks.cs` |
| API endpoints with PostgreSQL | Native redesign awaiting execution admission: canonical migrations, ten purpose accounts, genuine Local authentication and authorization, an HTTP-created dataset, and a no-op output-cache store. | `tests/Event.Benchmarks/Benchmarks/ApiEndpointPostgreSqlBenchmarks.cs` |
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
dotnet run --configuration Release --no-build --project tests/Event.Benchmarks -- --filter '*ApiEndpointPostgreSqlBenchmarks*' --job Dry --inProcess --stopOnFirstError
dotnet run --configuration Release --project Event.Benchmarks -- --filter "*Serialization*"
dotnet run --configuration Release --project Event.Benchmarks -- --filter "*Caching*"
```

The shared benchmark config enables memory, threading, and exception diagnosers plus GitHub Markdown, HTML, and full JSON exporters. It also fails benchmark validation on execution errors or missing JIT optimizations. No custom artifact path is configured, so BenchmarkDotNet writes to its default artifact directory for the run.

### API Endpoint Benchmark Harness

The API catalog preserves six meaningful operations through `Microsoft.AspNetCore.Mvc.Testing` and ASP.NET Core `TestServer`: anonymous event list minimal/HAL responses, category types with categories, system onboarding status, authenticated my-events, and event creation context. The InMemory API factory cannot supply current physical transactions or original native human authority. Its removal is approved only after all six PostgreSQL successors physically execute successfully. The three existing EF query-construction benchmarks remain independent; they construct Npgsql query shapes without executing SQL.

The native redesign prepares the configured administrator in an original instance-policy scope, performs mandatory first-use password replacement through the Local HTTP endpoints, and uses an ordinary validated Local JWT. It creates an active tenant and 24 personal InformationOnly events through authorized HTTP creation and publication. Warmup checks the expected counts, returned event identities and titles, HAL self link, completed MultiTenant onboarding, private event ownership, and personal publishing capability. Separate anonymous and authenticated clients disable cookies and redirects; only the two private requests carry a per-request Bearer token.

All setup and session refresh run outside the timed request. Each iteration obtains a fresh native session for the same subject, checks its remaining lifetime, and performs one measured request. Timed work includes response materialization and bounded JSON validation: nonempty body, JSON media type, expected object/array root and one typed route-specific field (or a nonempty category array). Invalid HTTP 200 placeholders cannot become fast successful samples. Full six-route semantic/dataset checks remain in setup. The measured operation is GET plus JSON body validation, not pure handler latency; comparisons must use this same boundary. Login remains excluded; token lifetime and native human authority are unchanged.

### PostgreSQL API Endpoint Benchmark Harness

The replacement `ApiEndpointPostgreSqlBenchmarks` owns one labelled `postgres:18-alpine` container per setup. It binds all ten native database purposes from fresh credentials injected into the controlled child Environment authority, provisions the existing purpose roles, and calls the canonical `ExploreDatabaseMigrator.MigrateAndSeedAsync` path before hosting the API. `Testing` skips automatic administrator preparation, so the benchmark explicitly invokes the existing configured-administrator startup sequence. Existing native context owners, purpose interceptors and enrollment guards remain active. Execution admission remains pending until all six Dry cases complete successfully; prior setup failures produced no admitted benchmark measurements.

Functional calibration selects the whole class and its six Scenario values with the Dry command above. Installed BenchmarkDotNet 0.15.8 help confirms `--stopOnFirstError`; a flat/tree listing identifies the method and does not prove a single Scenario selection. No parameter-filter claim or separate selector is required. Stop at the first error and retain partial results as failures, not as six-case success. The existing four-minute GlobalSetup cancellation bound is unchanged. For this bounded calibration, the supervising parent allows at most 30 minutes, then sends SIGINT with a 60-second grace period before enforcing termination and exact owned cleanup. Setup timing, cancellation, secret-canary checks and cleanup outcomes must be retained; no automatic retry or deadline extension is implied.

Native setup requires the existing parent ownership context: paired `ISLAMU_TEST_RUN_ID` and `ISLAMU_TEST_ROOT`, actual private child TMPDIR and an explicit canonical Unix `DOCKER_HOST`. The shared validator requires private directories, the existing mode-600 ownership marker and exact parent identity; the benchmark rejects ordinary absent-pair mode before constructing a container builder. The parent prepares this authority before child launch and preserves its selected Docker endpoint when narrowing TMPDIR. Setup selects the existing cached image with pull disabled, a known unique container name and additive parent/resource/fixture labels while retaining the separate UUIDv7 benchmark run label.

Before Build/Start, setup persists private lifecycle intent under the validated root with CreateNew, mode 600, WriteThrough and a completed disk flush. It retains the writable file handles with FileShare.Read through terminal factory/container cleanup so the supervising parent can independently inspect the actual writer and file identity. The existing stage envelope includes `containerId`: null until successful Start returns an actual ID validated as 64 lowercase hexadecimal characters, then retained through cleanup. Failed disposal keeps the dependent context and handles; independent cleanup faults remain failures. Validation and witness I/O occur only in GlobalSetup/GlobalCleanup, outside both measured requests and per-iteration session refresh. These files contain no credentials or raw endpoint and remain private. Intent, the recorded ID and SDK return stages establish neither creation nor successful cases: the supervising parent must independently correlate actual child identity, selected daemon and cached image, observed container/session/mount identities and absent-before resources before admitting ownership or exact cleanup. Existing unattributed resources remain protected; the benchmark never deletes the parent marker or witnesses. Compilation alone establishes neither native execution nor cleanup success.

The PostgreSQL profile replaces the output-cache store with a no-op store and distributed caching with process memory. Output-cache middleware still executes, but responses are not replayed between iterations. The supported hosted-services-disabled test profile prevents background workers from adding uncontrolled activity; one benchmark startup service performs administrator preparation and lookup-cache refresh.

The former `EnsureCreatedAsync` plus core-primitives profile is superseded because it cannot satisfy real API purpose enrollment and ownership contracts. Credentials belong only to the child process Environment authority: never pass them through BenchmarkDotNet job environment metadata, arguments, source, or reports. Native API and container logging are disabled for this harness; actual artifacts must pass a secret-canary scan before admission. Fixed failure codes retain original setup and cleanup causes privately in memory. Cleanup always attempts both host and exact owned container disposal, including setup failure, and any cleanup failure rejects the run.

The controlled child declares Local authentication, Local authorization, MultiTenant deployment and colocated identity storage through the existing flat Environment keys. Read-only preflight rejects conflicting structured selectors and inherited bootstrap, blocklist, privacy-store or external identity connector aliases before creating a container. The factory owns those generated bootstrap identifiers and file paths; it never mutates process environment. Both clients use buffered completion with a ten-second request timeout and a 4 MiB response limit, including response bodies.

Use this suite when the performance question involves EF Core, Npgsql, SQL translation, PostgreSQL constraints, indexes, query execution, or transaction behavior. It still runs through ASP.NET Core `TestServer`, so it is database-faithful but not network-faithful; use deployed load tests when the question includes sockets, reverse proxies, TLS, Redis, Keycloak, Cerbos latency, or multi-instance traffic.

Dry is functional calibration. Statistical comparison uses three sequential comparable quiet runs of both retained types: `ApiEndpointPostgreSqlBenchmarks` (six native API cases) and `EfCoreQueryBenchmarks` (three database-free query-construction cases). The fixed selection is one `--filter` followed by `'*ApiEndpointPostgreSqlBenchmarks*' '*EfCoreQueryBenchmarks*'`. Omit `--job` to use the existing default .NET 10 job with adaptive measurement targeting 1% relative error, and omit Dry, in-process and CLI warmup/iteration/launch-count overrides. Execute the same owned parent operation three times sequentially in `baseline` mode; each invocation is one whole-nine repetition, not an internal loop or three-iteration substitute. Record actual runtime, hardware, candidate, dataset, warmup and statistical results; the error target is not a latency limit. Missing required physical pressure metrics or quiet-host evidence leaves capacity inference ineligible, and this TestServer benchmark supplies no deployed-capacity claim. The separate load collector publishes bounded live pressure snapshots with the actual scrape URI and selected exporter facts as untrusted leads. Its parent must corroborate the exporter and admitted API process while live; a proxy URI, declared units or aggregate values alone supply no process/reset or capacity proof.

The existing ReleaseEngineering operation requires `execute-api-benchmarks <fresh-output> <candidate> <run-id> <attempt> <event> <smoke|baseline>`; the final mode is mandatory, with no implicit compatibility default. The scheduled/manual workflow explicitly selects `smoke`: one native six-case Dry/in-process invocation. Each `baseline` invocation selects one whole-nine default-job run and admits its own summary after all nine cases, credential-canary and creator-cleanup checks pass. The coordinating hub executes three such invocations sequentially with separate fresh private ownership/output roots, then checks all three for comparable configuration and observed quiet-host evidence before statistical-baseline acceptance. No repetition/count option is added. Default-job benchmark workers are out-of-process descendants: the parent must bind their actual writer PID/start, live file descriptors and candidate inputs, and verify scoped credential inheritance rather than assume the smoke leader. Credentials remain process-environment-only, never BenchmarkDotNet job metadata or arguments.

The workflow builds the existing tool and benchmark candidate and caches exactly `postgres:18-alpine`; it contains no second shell custody implementation. Only the successful parent's nonempty `benchmark-summary.json` is uploaded; raw logs, credentials, environment, lifecycle witnesses and full BenchmarkDotNet reports remain private. Every `smoke` or `baseline` invocation has an explicit 30-minute cap followed by the existing 60-second SIGINT grace. The baseline cap is a deliberate bounded execution choice, not a prediction from smoke timing or proof that default-job measurement will finish. Three coordinator calls do not multiply or extend the per-call limit. Timeout or partial results cannot substitute for a completed repetition; there is no automatic extension, internal retry or Dry substitution. Command/control and actual execution admission remain pending; this source contract is not a statistical-baseline claim.

The separate event load programme uses the selected API's configured direct HTTP target through the existing fixture, while the lookup foundation retains its controlled endpoint. Parent qualification must join the actual child's `LOAD_ORIGIN` and the operations listener to the same admitted API PID, kernel start, network namespace and exclusive listening sockets. Neither loopback spelling nor an Aspire proxy supplies that proof.

## Interpreting Runtime Results

Use runtime benchmark results to compare implementation choices under controlled microbenchmark conditions:

- Compare relative means, allocations, and diagnoser output between benchmark methods in the same suite.
- Keep hardware, runtime, branch, environment variables, and local machine load stable when comparing runs.
- Re-run a benchmark after changing the hot path and compare against a baseline from the same machine or CI runner.
- Treat large regressions as investigation triggers, not automatic release blockers, unless the change has an agreed performance budget.
- Admit API execution only when all six result records show completed benchmark bodies and successful correctness checks, artifacts pass the credential scan, and exact creator-owned container cleanup succeeds. A zero CLI exit code or a Dry job alone establishes neither successful execution nor a timing baseline. Comparable measurements require repeated quiet runs on the same candidate, dataset recipe, runtime and declared profile.

### What Runtime Benchmarks Do Not Prove

Runtime microbenchmarks are not production load tests:

- They do not prove API P95/P99 latency, throughput, or production SLO compliance.
- They do not model multi-instance traffic, reverse proxies, Keycloak, Redis, MinIO, browser behavior, or real tenant concurrency.
- API endpoint benchmarks use ASP.NET Core `TestServer`, not a real socket, reverse proxy, or deployed container.
- The unsupported InMemory API profile supplies no valid current API measurements. Native PostgreSQL API execution remains pending; its eventual successful Dry run will be a functional smoke check, with no latency, capacity or production SLA claim.
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
