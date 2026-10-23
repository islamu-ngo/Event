# Run isolated distributed tests

The distributed test project starts the real LocalDefault application, including
PostgreSQL, Redis, RabbitMQ, Mailpit, Keycloak, Cerbos, and object storage. It
requires a Docker-compatible runtime and sufficient memory for that graph.
Fast ownership tests and discovery require no containers.

```bash
dotnet test --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj -c Release -- --treenode-filter '/*/*/AppHostOwnershipTests/*' --minimum-expected-tests 30
dotnet test --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj -c Release -- --treenode-filter '/*/*/AppHostRuntimeTests/*' --minimum-expected-tests 7 --maximum-parallel-tests 1
```

Use your explicitly selected Docker-compatible endpoint. For rootless Podman,
set `DOCKER_HOST` to its live Unix socket and set
`TESTCONTAINERS_RYUK_DISABLED=true` and
`TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock` when running other
Testcontainers cases alongside these tests. Never restart or prune somebody
else's runtime to prepare a test. Set `TMPDIR` to a disk-backed scratch location
with sufficient free space.

Each case creates its own run ID, root directory, marker, data volumes, and
dynamic ports. The fixture supplies `ISLAMU_TEST_RUN_ID` and `ISLAMU_TEST_ROOT`;
do not point them at existing storage. The directory basename must match the
run ID, and `.islamu-test-owner` must contain that ID. Populated, foreign, and
symbolic directories are rejected before any application builder runs.

Automated runs explicitly select the Environment secret authority and the
`Assurance` environment. They ignore repository dotenv and shared User Secrets.
Inherited application configuration is discarded; payment-provider mode, storage
paths and identity credentials must come from the fixture's explicit inputs.
Aspire's own local store also stays beneath the run's owned directory.
Each fixture uses a private in-memory bootstrap test provider and injects its
own cryptographically generated database/provider credentials into child environments,
and real Data Protection ring/admission documents using the documented
`DATABASE_*`, `CERBOS_POSTGRES_PASSWORD`, `KEYCLOAK_ADMIN*`,
`STORAGE_S3_*`, `AUTHENTICATION_LOCAL_JWT_KEY`, and `EVENT_DP_*` keys.
Browser cases additionally generate `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` and
test-only `ISLAMU_TEST_REPLACEMENT_PASSWORD` for the required first-use password
replacement, plus an owned password-blocklist file and its SHA-256.
The test process environment is unchanged. Credentials are not test artifacts;
never upload them or copy them into logs. Database initialization files contain
only environment-variable references, not credential values.

Split and Standalone cases wait for the migration service and actual private
HTTP readiness. Workers and rate limiting remain enabled. Cancellation and
concurrent-owner cases exercise cleanup; disposal removes only resources named
by the owning fixture and carrying its ownership labels. A foreign container
attached to an owned volume is preserved, and cleanup reports the conflict while
retaining the volume and root. Changed or symbolic ownership markers/paths also
prevent directory cleanup. If cleanup fails, retain the error and inspect the exact
run ID before removing anything. A compilation or discovery pass alone is not
evidence that the application became healthy.

On startup failure, cancellation reaches the actual host and cleanup waits for
its execution to finish. If execution cannot be observed or does not settle
within the cleanup deadline, the run's stores remain available for investigation.
The startup error and any stop/disposal error remain part of the reported failure.

The delayed-startup, build-without-start, and missing/duplicate execution
observation checks pass. They prove cancellation and safe retention when execution
cannot be established. The distributed lane is not yet green: fresh migrations
currently stop at the discovery source-catalog guard. Passing ownership or discovery checks
must not be reported as successful full application execution.
