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

Before any version or workload process starts, the owner requires the admitted
Linux x64 k6 2.3.0 executable's SHA-256:
`e001bbf39ba67424a85c9c8516440ba5c73ecf7a6021be96c76194380c15f260`.
This executable pin was independently derived from the admitted Linux AMD64
archive, whose published SHA-256 is
`39c3117b6af817592dcd0ce4242105c0a7af10948c2a425306f0be8f7a8a8ab1`.
Matching version text alone cannot admit another executable. The process
harness proves this with an owned child that emits accepted version text and
creates a sentinel when launched: checksum rejection must leave that sentinel
absent. No alternate platform executable or automatic fallback is admitted.

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
five seconds to finish in-flight iterations. The C# process deadline is fifteen
seconds for these five-second profiles, linked to test cancellation. Cancellation kills the process tree and
drains both output streams without retaining or printing them.

k6 thresholds require all response checks to pass, zero HTTP failures, at least
one response, and zero dropped iterations in the open model. A failed `check()`
alone would not fail k6; thresholds and process exit status make it enforceable.
The independent C# parser also requires schema 2, the exact admitted profile,
all expected fields exactly once, finite ordered p50/p95/p99 latency, and matching attempted, received, correct, HTTP,
and completed iteration counts. The open lookup run must complete exactly forty
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
concurrency or offered rate, counts, p50/p95/p99 latency, generator measurements, and
the test assembly's informational build version (including source revision when
supplied by the SDK).
They exclude response bodies, application logs, target URLs, credentials,
tokens, stdout, stderr, user identifiers, and hostname.

Rejected runs still fail the test, but their safe numeric observations can
survive cleanup in `rejected-*.json`. The failure wrapper runs after the
runner completes its bounded cleanup attempt and reads at most 4,097 bytes to enforce a
4,096-byte summary ceiling. It retains only the exact expected schema, journey,
profile and numeric fields, with finite bounded counters/rates/percentiles.
Unknown fields, unavailable files and oversized/malformed summaries produce a
finite status and a null summary; no missing metric becomes zero. Every such
record has `Admission="Rejected"` and `CapacityEvidenceEligible=false`.

The independent three-second capture budget also applies when the original run
was cancelled. The failure returned by the runner is rethrown; a capture failure
preserves both causes. A provided observation of 38 completed attempts and two drops can
therefore explain a rejected forty-offered run, while the normal parser still
rejects drops, errors, count mismatches or nonzero child exits. This diagnostic
projection does not validate the selected k6 binary's unobserved runtime shape
or turn a failed run into a baseline.

Every record is explicitly a **candidate**. No guessed absolute latency
threshold is applied. Promote a baseline only after repeatable clean runs on a
quiet host, with a recorded revision and hardware identity. Compare like for
like: tool and workload digests, topology, provider, cache/warmup profile,
duration, concurrency/rate, runtime, hardware and available processor/memory
budget must match. A changed workload, incomplete response, dropped iteration,
generator pressure, runtime/provider change, or resource-budget change requires
a new baseline. This implementation captures candidates; it does not implement
baseline promotion or a latency regression gate.

The separate event programme below adds representative anonymous reads/searches
and serial authenticated title updates with independent public-seam settlement.
Its source and harness are separate from actual application execution evidence.
Passing the lookup foundation must not close the full programme.

## Native event control prerequisite

`K6NativeControlTests` is a separate explicit `LoadControl` lane. It starts an
owned MultiTenant host, completes first-use Local credential replacement and a
fresh ordinary login, then creates a dedicated active tenant with real
administrator membership. It creates three InformationOnly events with no
registration workflow: one published search target, one draft write target,
and an unchanged draft control.

The scenario requires exact public discovery results, applies a grouped title
PATCH using the write event's current quoted GUID `If-Match`, requires a stale
stamp to fail with HTTP 409, and verifies the predetermined final title and a
new stamp through a fresh management GET. Separate GETs must preserve the
control title/stamp and the published read event. It launches no k6 process
and runs only after the native startup prerequisite is healthy. Authoring or
compiling this scenario does not establish passing runtime or load evidence.

## Execution and integration

The broader programme has fixed envelopes, with no caller-supplied rate,
duration or concurrency knobs:

| Profile | Offered iterations/second | Duration | Maximum users | Exact offered iterations | Process deadline |
| --- | ---: | ---: | ---: | ---: | ---: |
| Low rate | 1 | 10 s | 4 | 10 | 20 s |
| Steady | 4 | 30 s | 4 | 120 | 40 s |
| Spike | 8 | 5 s | 4 | 40 | 15 s |
| Recovery | 1 | 10 s | 4 | 10 | 20 s |
| Soak | 2 | 120 s | 4 | 240 | 130 s |
| Constrained write | 1 | 20 s | 1 | 20 | 30 s |

The harness validates these envelopes and rejects a child deadline beyond its
profile's duration plus ten seconds before starting the child. Spike and
recovery must run as sequential children, named
`spike_then_recovery_with_observed_gap`, with their actual monotonic transition
gap recorded and pressure sampling continuous across that gap. They must not
be described as an instantaneous rate change. `K6LoadTests` remains the cached
lookup smoke profile. `K6EventProgrammeTests` first performs the native control,
then runs each read/search regime separately, twenty serial write iterations,
and three steady candidates per read/search journey. Its fixed twenty-minute
outer cancellation budget covers startup and all finite child profiles; it is
not a latency target. No event-programme execution is claimed by harness tests.

Read/search requests carry the owned tenant slug and no Bearer token. The
writer obtains a fresh real Local token immediately before its twenty-second
profile; the configured five-minute lifetime is unchanged. Each iteration
performs management GET, grouped title PATCH with the fresh quoted GUID stamp
and unique idempotency key, then another GET. Thus twenty offered iterations
require sixty completed HTTP requests and exactly twenty PATCH attempts. Any
unexpected conflict fails correctness without retry. Percentiles cover these
individual HTTP requests, not transaction latency. Independent C# reads require
the predetermined twentieth title, the unchanged separate control title/stamp,
and unchanged published event/search results. Setup/control requests and warmup
are outside measured iteration counts; no order or payment flow is involved.

The owned workload origin is obtained only from the fixture model, pinned to
literal loopback and independently checked in C# and JavaScript against the
verified event IDs and exact query. Native clients disable proxies and
redirects, bound response buffering to four MiB, and use finite HTTP timeouts.
The executable digest is checked again before each workload child starts.
The workload rejects response strings longer than 4,194,304 UTF-16 code units
before JSON parsing. This is an accepted-result size limit, not a transport
receive-byte cap: the public k6 interface does not document such a cap. The
parent samples working set every 100 ms and kills the child when an observed
sample exceeds 512 MiB. Allocation can occur between samples; this is not a
hard operating-system memory limit. The native C# warmup has a separate enforced
four-MiB response-buffer bound.

## Pressure observations and comparable candidates

The existing private operations `/metrics` endpoint is the only API metrics
source. Diagnostic capture reads headers first and bounds the body to four MiB
under a separate three-second deadline. Only six finite candidate family names,
finite metric kinds and admitted `seconds`/`bytes` units can survive projection;
HELP, labels and samples are never retained. Unknown units remain unavailable.
The candidate names are not proof that the live exporter emits them. Actual
owned series, units and reset identity must be verified before replacing this
temporary metadata inventory with a finite numeric reader.

The event programme continuously observes its own primary database through the
existing Global factory and original native login admission. One fixed
read-only `pg_stat_database` query checks the expected current database and
returns backend gauge, cumulative transaction/block/temp/deadlock counters and
`stats_reset`. This provider statistics view has no EF entity equivalent.
Queries never enumerate another database; observed counters include observer
queries and have PostgreSQL statistics flush lag, so they are not per-request
attribution. Counter deltas require an unchanged, present reset identity.
Queue/email observations use the existing native health readers and retain
only nineteen aggregate counts, excluding their bounded tenant sample and
authority settings. Denial, timeout and missing values remain explicit and
never become zero. Sampling remains active between load children; actual
monotonic start/completion offsets expose collection spans, with at most 900
samples. Each profile retains process start/exit offsets on that same origin,
so observations can be assigned to the workload and transition windows.

Candidate comparison digests bind the compiled harness, workload/executable,
topology/provider profile, dataset-generation and warmup policies, journey,
budget, runtime, OS, architecture and visible CPU/memory budget. The three
repetitions must have matching keys. Moving seed-clock bounds are retained
separately. Matching keys do not prove a quiet host or a valid capacity sample.
API numeric CPU/working set/reset observations are still unpinned, so all
programme records explicitly remain ineligible for capacity inference. Actual
statistics privileges, metric availability and repeated quiescent-host timing
remain runtime acceptance requirements.

The explicit read-only owned-PostgreSQL outage profile remains unimplemented
until native control establishes its precise opaque unavailability contract.
No arbitrary 5xx acceptance or guessed error status is admitted. Owned-resource
stop/start acknowledgement, health, native database query, public recovery and
unchanged independent settlement must all be proven before closing that gate.

## Tool execution

Summary trends explicitly request `p(50)`, `p(95)` and `p(99)`; `med` is a
separate selector. Missing dropped-iteration observations remain unavailable,
never an assumed zero. The selected binary's actual summary shape and metric
availability must pass the owned runtime check before any measurement claim.

`ISLAMU_K6_PATH` must name an absolute, separately installed k6 **2.3.0**
executable. It is not a secret. The test verifies the fixed admitted executable
SHA-256 before starting version inspection, then verifies its reported version
and records the digest. Verify archive release integrity during installation;
a version string alone cannot admit another binary. The external
executable is not downloaded, vendored, packaged, or distributed by this lane.
The runner clears the child environment, supplies only workload parameters and
the selected temporary directory,
disables usage reporting, provides an empty owned configuration file, starts no
k6 management listener, and permits no remote imports, cloud execution, metric
export, or external target traffic.

The owning project copies the reviewed workload as content:

```xml
<Content Include="../../eng/performance/k6/owned-runtime.js"
         Link="Workloads/owned-runtime.js"
         CopyToOutputDirectory="PreserveNewest" />
```

After building `tests/Event.Distributed.IntegrationTests` in Release, the
parser/admission and owned child-process tests require no network, Docker,
k6 executable or AppHost:

```bash
dotnet run --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter '/*/*/K6Load*HarnessTests/*' --minimum-expected-tests 73
```

The explicit runtime category requires the real owned environment and external
k6 executable. It runs both topologies, two workload modes each:

```bash
dotnet run --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter '/*/*/K6LoadTests/*' --minimum-expected-tests 2
```

The Harness category also starts controlled copies of its own executable to
exercise the actual process boundary: full stdout/stderr pipes, cancellation
and deadline cleanup of a parent and descendant, nonzero exit, absent or
oversized summaries, and an inherited secret canary. The controlled child mode
accepts only eight fixed scenarios and an owned temporary directory with a
matching marker; it cannot select arbitrary commands or delete files. A guarded
module initializer intercepts only the exact first child flag, preserving
TUnit's generated entry point. Ordinary discovery allocates no child process,
temporary directory, network connection or application resource.

PID files support read-only termination assertions only; cleanup never kills a
process selected from a PID file. Synthetic descendants expire after fifteen
seconds if a runner regression leaks them. Cancellation assertions must finish
within ten seconds, so self-expiry cannot substitute for the runner's actual
process-tree termination.

Harness cases require no AppHost, k6 executable, network or Docker. See
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
- [k6 response body handling](https://grafana.com/docs/k6/v2.3.x/using-k6/k6-options/reference/#discard-response-bodies): discarding bodies reduces retention; it does not document a receive-byte cap.
- [Checks](https://grafana.com/docs/k6/latest/using-k6/checks/): checks need thresholds for failing process status; source-free handoff.
- [Executors](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/): closed concurrency and open arrival-rate semantics; source-free handoff.
- [Dropped iterations](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/dropped-iterations/): drops invalidate offered-load evidence; source-free handoff.
- [k6 2.3 summary trend statistics](https://grafana.com/docs/k6/v2.3.x/using-k6/k6-options/reference/#summary-trend-stats) and [custom summary reference](https://grafana.com/docs/k6/v2.3.x/results-output/end-of-test/custom-summary/#summary-data-reference): exact percentile selectors and documented aggregate keys; source-free handoff. Actual selected-binary output still requires runtime verification.
- Local `k6 run --help` and `k6 inspect --help`: CLI interface facts only; no external source inspected.
