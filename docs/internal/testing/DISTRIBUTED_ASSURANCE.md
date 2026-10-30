# Owned distributed assurance

> Audience: maintainers of the AppHost and runtime tests.

`Event.Distributed.IntegrationTests` executes the real AppHost using
`Aspire.Hosting.Testing` 13.4.6 and TUnit. `Harness` tests exercise admission,
directory ownership, concurrent disposal, and cryptographic separation;
discovery creates no application, container, socket, or credential authority.
`Distributed` tests start the selected LocalDefault Split or Standalone graph.
They require migration completion and HTTP readiness on the private operational
listener. The environment is `Assurance`, so production worker registration and
rate limiters retain their normal enabled behavior rather than the `Testing`
environment's substitutions.

## Ownership admission

`TestingResourceIsolation.Admit` runs before dotenv loading and before the Aspire
builder. Both `ISLAMU_TEST_RUN_ID` (32 hexadecimal UUID digits) and
`ISLAMU_TEST_ROOT` (absolute canonical directory ending in that run ID) are
required together. The fixture creates an otherwise empty directory containing
`.islamu-test-owner`, whose entire contents equal the run ID. Admission rejects
symbolic path components, symbolic markers, mismatched markers, populated roots,
interactive profiles, and authority/environment conflicts. An exclusive
`.apphost-claim` creation prevents a second AppHost from claiming that directory.

Admission requires `DOTNET_ENVIRONMENT=Assurance`,
`ASPNETCORE_ENVIRONMENT=Assurance` when supplied, `SECRET_PROVIDER=Environment`,
`SecretProvider__Provider=Environment` when supplied,
`ISLAMU_ASPIRE_MODE=local-default`, and `Hosting__Topology=Split` or `Standalone`.
The default `ISLAMU_TEST_PROFILE=Standard` retains CoLocated identity and the
existing Split/Standalone graphs. `ExternalIdentitySharedBff` requires Split,
External identity, Sqlite, and the exact derived `<owned-root>/identity.sqlite`
filename. Flat identity coordinates are required; canonical aliases may only
repeat those exact values. Other nonempty identity settings, including remote
coordinates and credentials, are rejected. Symbolic stores and symbolic parents
are rejected before resource construction. Explicit profile selection without
ownership, unknown profiles, and undefined fixture enums are rejected.
Erasure remains EmbeddedSqlite, webhook delivery Local, the configuration
manifest Off, and AgentBrowser seeding disabled. These are explicit fixture
inputs, not new ordinary hosting defaults.

The immutable admitted profile is checked again after owned configuration is
rebuilt. The existing BFF configuration is one local `AddBff` function; Standard
calls it once, while the external profile calls it for `explore-blazor` and
`explore-blazor-peer`. Both reference the same real API, migrations, cache, and
selected platform resources. They are separate named project processes, with
their own dynamic HTTP/HTTPS/operations endpoints. Readiness includes the peer.

Before reading application settings, the owned builder discards ambient
configuration providers. It retains the fixture's dedicated in-memory authority
source only when that source carries the matching run ID, plus explicit fixture
arguments and five framework metadata keys: `AppHost:Directory`, `AppHost:Path`,
`AppHost:Sha256`, `DcpPublisher:CliPath`, and `DcpPublisher:RandomizePorts`.
The fixture sets `Aspire:Store:Path` beneath its owned root. Inherited payment,
storage, identity, or credential settings cannot enter the effective application
configuration, including settings placed under a lookalike `TestingEnvironment`
prefix in another provider. Harness cases evaluate the real project environment
callbacks for both topologies with synthetic live-provider canaries.

Every selected named volume receives the `islamu-test-<run-id>-` prefix. Fixed
host ports are removed; Aspire allocates endpoints. Storage and embedded erasure
files are rooted under the owned directory. Repository configuration/policy
mounts remain read-only. Foreign writable binds are rejected. Owned containers
have an explicit process limit because a zero limit through the Podman Docker
API can prevent their first fork.

The fixture disposes its actual `DistributedApplication` before removing its
exact volume names and then its marked directory. It never prunes containers,
resets an interactive database, or removes resources discovered by broad name
search. A failed cleanup is a failure, not a successful test.
Container removal additionally requires both the exact run label and a resource
name from the constructed application. A foreign consumer of an owned volume
causes cleanup to fail and retain the volume and root. Directory components and
the marker are revalidated before store removal and again before deleting the
root. Concurrent callers await one disposal task. Cancellation terminates and
reaps only the fixture's Docker CLI child and drains its output readers;
the actual AppHost execution is observed before store cleanup. In owned mode,
the fixture registers a single-assignment `TestingApplicationExecution` observer.
AppHost gives it the task from its existing `RunAsync()` call and awaits that same
task. A failure requests termination through `IHostApplicationLifetime.StopApplication()`;
the fixture then joins the observed task, with a 30-second startup-failure deadline
and a 60-second cleanup deadline. A missing observation or unsettled execution
retains stores, and creation/stop/disposal errors retain their original causes.
Duplicate observation permanently invalidates completion proof, and AppHost still
awaits its actual execution if attachment fails. A task faulted with an
`OperationCanceledException` remains a failure; only an observed task whose state
is actually Canceled is accepted as completed cancellation.
The testing facade's startup/stop tasks and lifecycle notification tokens alone
are not used as proof that a pending hosted startup callback has finished.

Current verification is incomplete: fresh Split and Standalone runs stop at
`discovery_traversal_source_catalog_drift` during migration. The delayed
host-lifecycle witness now passes: cancellation reaches the real callback,
startup remains pending until its gate is released, and later disposal preserves
both the original resource failure and the injected disposal failure. Actual
build-without-start cleanup also settles execution before removing its root;
missing and duplicate observations retain roots, including a duplicate while
startup is still held. The 34-case Harness passes, including the faulted versus
canceled task controls. A separate healthy-host witness is intended to verify
errors from an actually started service's stop callback. Full readiness, cancelled
application startup, and concurrent healthy application owners remain open gates.
The passing ownership/projection, foreign-volume-consumer, Docker-child
cancellation, and builder-failure checks do not replace those gates.

## Explicit ephemeral authority

Each fixture generates an independent immutable authority snapshot. Aspire's
testing builder injects that snapshot into the AppHost as a test-only bootstrap
provider mock in memory; it is not a new production secret provider. Ordinary
AppHost profiles retain their existing authority selection. Actual child
services explicitly select Environment and receive only their own consumer's
values through resource environment projection. The test process environment
is never mutated. Concurrent owners have different passwords, JWT keys, and
Data Protection key rings. Nothing reads repository dotenv or shared User
Secrets. Credential values are never files, command-line arguments, or evidence.
Within the external profile, the two BFFs normally receive the same generated BFF
ring/admission; the API retains its independent partition. Neither BFF receives
identity database coordinates. The closed `OwnedBffPeerProtection` fixture enum
also admits an independent valid BFF authority for the peer: both providers must
accept their own protected payload and reject the other provider's payload.
That variant changes only the peer's Data Protection input. Both peer variants
use the same dedicated session backend, generated session password, and stable
owner run-ID namespace. Independent owners generate different session credentials
and namespaces; process restart does not change them within an owner. AppHost
supplies the dedicated resource endpoint and owns its labelled, prefixed volume.
The enum can select a wrong partition, generation below the admitted floor, expired
admission, or revoked active key for the peer only. These are fixture fault
inputs, not AppHost configuration hooks. Revocation imports the same owned ring
into the existing ephemeral test repository, invokes real `IKeyManager.RevokeKey`,
and refreshes native ring/admission hashes and inventory without generating a
different active key. The shared authority's default constructor is unchanged.

Existing documented environment keys used are:

- `BFF_SESSION_REDIS_PASSWORD` and `BFF_SESSION_NAMESPACE`; the dedicated session
  resource supplies `BFF_SESSION_REDIS_ENDPOINT`, without sharing API/cache credentials.

- `DATABASE_{PURPOSE}_USERNAME` and `DATABASE_{PURPOSE}_PASSWORD`, where PURPOSE
  is `MIGRATOR`, `RUNTIME`, `GLOBAL`, `LOCAL_IDENTITY_WRITER`, `INSTANCE_KEY_READ`,
  `INSTANCE_KEY_WRITE`, `INSTANCE_KEY_ISSUANCE`, `PLATFORM_KEY_REPORT`,
  `EMAIL_POLICY_INSTANCE_MUTATION`, or `EMAIL_POLICY_TENANT_MUTATION`.
- `CERBOS_POSTGRES_PASSWORD`, `KEYCLOAK_ADMIN`, `KEYCLOAK_ADMIN_PASSWORD`,
  `STORAGE_S3_ACCESS_KEY_ID`, `STORAGE_S3_SECRET_ACCESS_KEY`, and
  `AUTHENTICATION_LOCAL_JWT_KEY`.
- `EVENT_DP_{PARTITION}_RING`, `EVENT_DP_{PARTITION}_ADMISSION`,
  `EVENT_DP_{PARTITION}_ADMISSION_SHA256`, and
  `EVENT_DP_{PARTITION}_MINIMUM_GENERATION`, for `API`, `BFF`, and `COMBINED`.
- `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` and test-only
  `ISLAMU_TEST_REPLACEMENT_PASSWORD` for the genuine first-use password-change
  flow. `BootstrapIdentifier`, `BootstrapPassword`, and
  `BootstrapReplacementPassword` are test inputs, never report fields.

The optional `configuredAdministrator` fixture argument selects the actual Local
configured-administrator bootstrap with a UUIDv7 subject, generated temporary
credential, and synthetic email. `deploymentMode` selects SingleTenant or
MultiTenant for browser flows. A generated canary password blocklist and its
SHA-256 are injected through `AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_PATH` and
`AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_SHA256`. These inputs exercise the
normal password policy and bootstrap; they do not bypass authentication.

The existing `ExternalDataProtectionTestAuthority` creates real key rings and
admission documents. A temporary SQL initialization file references container
environment variables using psql `\getenv` and escaped psql literal parameters;
it contains no credential values. It creates isolated PostgreSQL purpose logins
with `NOSUPERUSER` and `NOBYPASSRLS`; role creation is a provider bootstrap
primitive that EF migrations do not supply. The migration service then applies
the actual schema and enrollment contracts in the migrator-owned `assurance`
schema. The private initialization file is deleted with its owner.

## API and provenance

`OwnedAppHostFixture.CreateAsync(topology, cancellationToken)` constructs the
owner; `StartAsync(cancellationToken)` waits for migration and HTTP readiness;
`CreateHttpClient(resourceName, endpointName)` exposes Aspire's assigned endpoint.
Split resources are `explore-api` and `explore-blazor`; Standalone is
`event-standalone`. `operations` is the private readiness endpoint and `http`
is the public endpoint. Browser/load cases use this same project and fixture.
The optional `profile` argument selects `OwnedAppHostProfile`; the external
profile additionally exposes `explore-blazor-peer`. `peerProtection` defaults to
Shared and is valid only with that external profile when selecting a fault.
The new profile's initial Harness Red run was 10 intended failures and 40 passes;
the expanded post-implementation Harness passes all 60 cases, including actual
resource environment callbacks and native Data Protection admission for the
shared, independent-valid, and four rejected peer authorities, plus generated
session credential/namespace isolation between owners. The new session-resource
graph has its own callback checks; these fixture checks do not establish backend
availability. Actual cross-process/worker gates
remain pending. A projection or provider check does not prove successful peer startup,
cookie reuse, worker restart, or delivery recovery.

`CancelledStartupDisposesOnlyItsOwnedRoot(false)` retains cancellation after
PostgreSQL health. The `true` variant arms the native resource notification wait
before startup and cancels when the owned `postgres` resource reaches `Starting`.
That state is an orchestration observation, not proof that the PostgreSQL binary
or server is running. Both variants require the actual AppHost execution to
settle and the owned root to disappear after fixture cleanup. Their actual
runtime results remain pending.

Public Aspire API facts were checked against the official
[testing](https://aspire.dev/testing/overview/),
[resource model](https://aspire.dev/architecture/resource-model/), and
[volume](https://aspire.dev/fundamentals/persist-data-volumes/) documentation
on 2026-10-23. No external source
or example implementation was copied. Admission, ownership lifecycle, and test
arrangement were independently designed from this repository's authority and
operational-listener contracts. The hub owns the package/license register,
central pin, solution/CI routing, and `.env.example` parity.
