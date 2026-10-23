# Offline composition evidence ownership

The offline composition engine and its workload/publish evidence belong to
[ISLAMU Configurator](https://github.com/ISLAMU/Configurator). Event's former
setup composition generator and generated scale profile are removed with the
bundled runtime. Historical Event measurements are not relabelled as benchmarks
of the generic definition engine.

Configurator maintains bounded definition admission, typed answer composition,
deterministic projection, confidential output and native-host evidence in its
own repository. Timing/allocation measurements are advisory evidence, not
flaky pass/fail thresholds.

Event retains [definition export and host-parser reference vectors](CONFIGURATOR_DEFINITION_EXPORT.md).
These bind family/revision/digest, environment metadata, wire formats and actual
CLI-produced artifacts to Event's acceptance boundary. They do not replace
backend/bootstrap/tenant/secret-provider invariants or change backend limits.

For a new shape, export a bounded Event definition revision and verify its
actual Configurator-produced artifact against Event's parser. Run performance
measurements in the engine-owning repository; source cutover does not require
recreating the old benchmark, adding a compatibility parser or running the
database-provider matrix.
