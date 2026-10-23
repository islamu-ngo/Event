# Run the owned load checks

The load checks run the real local application with disposable dependencies and
exercise its public language lookup. They run for both split hosting and the
combined standalone host. The runner starts its own environment; it does not
accept a URL for an existing developer instance or deployed service.

Install Linux x64 k6 **2.3.0** separately. Verify the release archive's SHA-256
`39c3117b6af817592dcd0ce4242105c0a7af10948c2a425306f0be8f7a8a8ab1`
and the extracted executable's SHA-256
`e001bbf39ba67424a85c9c8516440ba5c73ecf7a6021be96c76194380c15f260`, then set
`ISLAMU_K6_PATH` to the executable's absolute path. k6 is an AGPL-3.0 external
tool admitted for this contributor workflow. It is not included in ISLAMU
build artifacts. Do not add it to redistributed packages without a separate
license/distribution review.
The runner rejects any other executable digest before starting a process;
matching version output does not authorize an alternate binary or platform.

Use an approved secret authority and the distributed test project's documented
local runtime prerequisites. Never put credentials into the workload or its
arguments. Keep other builds and benchmarks idle while gathering measurements.
On machines with a RAM-backed temporary directory, select disk-backed scratch
storage through `TMPDIR` before running.

```bash
dotnet build tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet run --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter '/*/*/K6LoadTests/*' --minimum-expected-tests 2
```

Each topology receives three warmup reads followed by two five-second profiles:
four concurrent users, then eight offered requests per second. Redirects and
external traffic are forbidden. Every response must preserve the successful
lookup response. Missing requests, failed checks, dropped iterations, process
failure, cancellation or load-generator pressure fail the run.

Sanitized aggregate evidence is saved beneath
`$TMPDIR/testing-assurance-modernization/continuation/load/<run-id>/`.
It includes request counts, p95 latency, workload/tool digests, topology,
provider, runtime and resource measurements. It excludes response bodies,
credentials and application/process logs. Disposable application resources and
raw summaries are removed by the fixture after execution.

These results describe a warmed public lookup and its output cache. They do not
prove application capacity, a production latency promise, write correctness or
completed registration effects. Latencies are baseline candidates only. Review
repeatability on matching hardware and workload settings before adopting any
regression threshold; an incomplete or saturated run cannot become a baseline.

For the exact admission rules, evidence schema, integration requirements and
remaining coverage, see the [technical load assurance guide](../../../../internal/testing/LOAD_ASSURANCE.md).
