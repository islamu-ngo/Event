# Owned runtime load assurance

The distributed assurance project owns a small k6 read workload against the real
AppHost public API. It exercises `/api/language` after three stable warmup reads.
This endpoint returns public lookup data and uses the production output cache;
the result therefore describes a **warm cached lookup profile**. It does not
establish uncached database throughput, authenticated mutation correctness,
settled business effects, a production latency SLO, or application capacity.

`K6LoadTests` creates the shared `OwnedAppHostFixture`, waits for its real
dependencies and health checks, then runs both workload modes. `Split` targets
`explore-api`; `Standalone` targets `event-standalone`. Only the fixture supplies
the origin. Neither a developer URL nor a deployed environment can be selected.
The C# owner converts the owned endpoint to literal IPv4 loopback, requires HTTP
and a dynamic port, and admits exactly `/api/language` with no query, fragment,
credentials, or redirects. JavaScript independently checks the same allowlist.

## What fails the gate

Warmup requires HTTP 200, JSON, a nonempty lookup list, positive unique IDs, and
nonempty `masterCode` and `fullName`. Three identical response digests establish
the read-only comparison value. Every measured request must return the same
status, content type, and SHA-256 digest. Bodies and the comparison digest are
never persisted. This detects changed or missing read results under load; it
does not independently establish that all seeded language values are correct.

The closed model uses four concurrent virtual users for five seconds. Each
iteration sleeps for 100 ms after its request. The open model offers eight
iterations per second for five seconds, with four preallocated and maximum
virtual users. Requests time out after two seconds and scenarios allow at most
five seconds to finish in-flight iterations. The C# process deadline is thirty
seconds, linked to test cancellation. Cancellation kills the process tree and
drains both output streams without retaining or printing them.

k6 thresholds require all response checks to pass, zero HTTP failures, at least
one response, and zero dropped iterations in the open model. A failed `check()`
alone would not fail k6; thresholds and process exit status make it enforceable.
The independent C# parser also requires a recognized schema, all expected fields
exactly once, finite latency, and matching attempted, received, correct, HTTP,
and completed iteration counts. The open run must complete exactly forty
requests. Missing summaries, interrupted iterations, unknown fields, and drops
invalidate the run.

The owner samples generator working set and CPU every 100 ms. Missing samples,
more than 512 MiB resident memory, or a peak sampled CPU ratio above 80% of the
.NET-visible processor budget invalidate measurements. These are generator
admission limits, not application performance targets. Other processes and host
contention can still distort latency; run baseline candidates on an otherwise
quiet host. The lane records sampled pressure, wall time, logical processors,
available memory, architecture, operating system, and .NET runtime.

## Candidate evidence and latency policy

Validated aggregate JSON survives fixture cleanup under
`$TMPDIR/testing-assurance-modernization/continuation/load/<run-id>/`.
Raw k6 summaries stay under the fixture-owned root and are removed by its
cleanup. Persistent records contain tool version and executable digest,
workload identity and digest, topology, provider, warmup durations, duration,
concurrency or offered rate, counts, p95 latency, generator measurements, and
the test assembly's informational build version (including source revision when
supplied by the SDK).
They exclude response bodies, application logs, target URLs, credentials,
tokens, stdout, stderr, user identifiers, and hostname.

Every record is explicitly a **candidate**. No guessed absolute latency
threshold is applied. Promote a baseline only after repeatable clean runs on a
quiet host, with a recorded revision and hardware identity. Compare like for
like: tool and workload digests, topology, provider, cache/warmup profile,
duration, concurrency/rate, runtime, hardware and available processor/memory
budget must match. A changed workload, incomplete response, dropped iteration,
generator pressure, runtime/provider change, or resource-budget change requires
a new baseline. This implementation captures candidates; it does not implement
baseline promotion or a latency regression gate.

The remaining full programme acceptance requires a representative authenticated
mutation journey, invariant validation and settled effects through public
seams. Passing this read foundation must not close that outstanding work.

## Execution and integration

`ISLAMU_K6_PATH` must name an absolute, separately installed k6 **2.3.0**
executable. It is not a secret. The test verifies its reported version and
records its executable SHA-256. Release integrity must be verified during
installation; a version string alone is not a signature check. The external
executable is not downloaded, vendored, packaged, or distributed by this lane.
The runner clears the child environment, supplies only workload parameters,
disables usage reporting, provides an empty owned configuration file, starts no
k6 management listener, and permits no remote imports, cloud execution, metric
export, or external target traffic.

The owning project must copy the reviewed workload as content:

```xml
<Content Include="../../eng/performance/k6/owned-runtime.js"
         Link="Workloads/owned-runtime.js"
         CopyToOutputDirectory="PreserveNewest" />
```

After building `tests/Event.Distributed.IntegrationTests` in Release, the pure
parser/admission tests require no network, Docker, k6 executable or AppHost:

```bash
dotnet run --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter '/*/*/K6LoadHarnessTests/*' --minimum-expected-tests 29
```

The explicit runtime category requires the real owned environment and external
k6 executable. It runs both topologies, two workload modes each:

```bash
dotnet run --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter '/*/*/K6LoadTests/*' --minimum-expected-tests 2
```

No resource is started during test discovery or by the Harness category. See
the [contributor guide](../../public/documentation/readme/contributing/load-testing.md)
for operator expectations.

## Provenance and approved execution boundary

The Project Steward expressly admitted the standalone AGPL-3.0 k6 executable
and the narrow exception for reviewed workload JavaScript in
`eng/performance/k6/`. This does not admit Node/npm tooling, arbitrary helper
scripts, vendored k6 source, a Go/.NET library dependency, cloud use, or
redistribution. Distribution needs its own legal review.

The implementation was independently designed from repository-native endpoint
contracts and a source-free functional handoff. No third-party source, snippet,
test, prose, or distinctive implementation organization was copied. Built-in
k6 module names, CLI flags, metric semantics and executor names are functional
interface facts. AFC/SSO review: commonplace bounded scenario configuration,
project-owned origin admission, aggregate schema, privacy filtering and C#
process ownership; no external structure, sequence or organization retained.

Source register (functional facts only; checked 2026-10-23):

- [k6 release v2.3.0](https://github.com/grafana/k6/releases/tag/v2.3.0): version and installation integrity supplied by the lead task.
- [Checks](https://grafana.com/docs/k6/latest/using-k6/checks/): checks need thresholds for failing process status; source-free handoff.
- [Executors](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/): closed concurrency and open arrival-rate semantics; source-free handoff.
- [Dropped iterations](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/dropped-iterations/): drops invalidate offered-load evidence; source-free handoff.
- Local `k6 run --help` and `k6 inspect --help`: CLI interface facts only; no external source inspected.
