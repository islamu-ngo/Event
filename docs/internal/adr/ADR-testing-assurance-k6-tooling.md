# Use Standalone k6 For Engineering Load Evidence

Status: Accepted by the Project Steward through explicit user direction on 2026-10-23: “We go with k6!” following the separate tooling/licensing boundary and workload-language decision brief.

## Decision And Boundary

Use an unmodified, version-pinned k6 binary as a separately obtained developer/CI tool. Do not vendor its implementation or binaries, link it into Event, or include it in Event product images or deployment bundles. Project-authored reviewed JavaScript workload assets are the narrow approved exception to the repository's C# scripting convention. This exception does not permit ad-hoc JavaScript/Node helpers, npm projects, remote workload modules or copying third-party implementations.

C# and TUnit retain authority over owned environment setup, approved secret injection, target admission, bounded process launch/cancellation, result validation and settled business-effect checks. k6 owns HTTP offered-load scheduling and load metrics; BenchmarkDotNet retains controlled CPU/allocation/provider measurements. No load orchestration is embedded in benchmark methods.

## Licensing And Provenance

The [official repository metadata](https://api.github.com/repos/grafana/k6) declares AGPL-3.0. The Steward's approval is for this external engineering-tool use and distribution boundary; it is not a claim that separation removes licence obligations or changes k6's licence. Modified, embedded, vendored or redistributed use needs a new recorded decision. Retain upstream licence/provenance and release checksum information alongside the selected local/CI tooling record. Project-authored workload assets remain governed by the repository's contribution and outbound licensing policy.

No external k6 implementation source or workload expression is incorporated. Workloads derive from Event's public contracts, independent test cases and the programme's source-free requirements. Dependencies/extensions/remote imports are excluded unless separately reviewed.

The selected release is [k6 v2.3.0](https://github.com/grafana/k6/releases/tag/v2.3.0). The Linux AMD64 archive `k6-v2.3.0-linux-amd64.tar.gz` has SHA-256 `39c3117b6af817592dcd0ce4242105c0a7af10948c2a425306f0be8f7a8a8ab1`; it was verified against that release's published checksums before execution. The observed binary reports commit `e088784614`, Go `1.26.8`, Linux AMD64. This records provenance, not a redistribution of the archive or a capacity result.

## Required Evidence

Pin the selected release and verify its published checksum before executing the binary. Launch only against a uniquely owned admitted test endpoint, never an arbitrary or production target. Inject credentials only through the selected approved authority; exclude them from arguments, output and retained artifacts. Cap rate, duration and virtual users and cancel the entire owned child process on timeout/cancellation.

Treat absent/malformed summary, nonzero exit, failed correctness thresholds, unexpected dropped work, generator overload and sensitive artifact leakage as invalid or failed evidence. Checks must have correctness thresholds because checks alone do not produce a failing process status. Derive latency regression limits from repeated comparable baselines; no invented SLA or automatic baseline acceptance. Record offered/achieved work, latency, errors, dropped iterations, generator/application/DB/queue pressure, topology/dataset/hardware and independent settled effects.

This decision authorizes tool adoption, not a successful load or capacity claim. Actual runtime/browser/provider results remain required by their owning programme packages.
