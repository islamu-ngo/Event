# Native moderation measurement launcher

This Linux/.NET 10 engineering host runs the approved Domain, API and Persistence
bundles exactly once each. It issues a nonce-bound ES256 assertion only after
reconciling its own process exit, lifecycle journal, TUnit report and MTP TRX.
It does not accept a report, executable, filter, case verdict or signing request
from the CLI. The approved measurement host is trusted; this is not hostile-host
execution attestation.

## Publication boundary

`catalogue.schema.json` is the version 1 machine contract. **There is deliberately
no `catalogue.v1.json` in this change.** Publishing the actual complete programme
cases is still required. An absent catalogue returns `catalogue-unpublished`;
bytes not matching the selected native issuer enrollment return
`catalogue-unapproved`. Neither a schema nor historical passing reports authorize
execution or substitute for the final catalogue.

Launcher and deployed runtime now share the strict immutable Application
`ModerationMeasurementCatalogue` parser. Deploy the exact approved bytes separately
at `<application-base>/moderation/catalogues/<sha256>/catalogue.v1.json`; the
runtime reader selects only the digest from freshly qualified native enrollment.
It rejects missing, linked, altered or malformed files and supplies no approval
or passing verdict. Do not embed the catalogue in the product assemblies whose
byte digest it declares; that would create a circular digest dependency.

Publish `eng/moderation/catalogue.v1.json` only after programme source completion
and final native identity discovery. Independently approve its exact SHA-256 bytes
and the runner/bundle digests through the existing native issuer-enrollment writer.
The runtime parser rejects duplicates, unknown fields, null/missing required
members, duplicate native/case identities and more than 256 total receipt cases.
Job order and identities are fixed:

1. `Event.Domain.UnitTests`
2. `Event.API.IntegrationTests`
3. `Event.Persistence.IntegrationTests`

Each executable is fixed to
`tests/<bundle>/bin/Release/net10.0/<bundle>.dll`. The host never builds tests.
The catalogue's enrolled `treeFilter` is passed as one argument and its complete
case set must exactly equal executed cases. The catalogue maps stable
`journalTestId` and `nodeUid` to one `caseId`, plus exact TUnit class/method/display
names and MTP class/method/test names. Do not infer equality between journal
`Context.Id`, `Identity.TestId`, activity `tunit.test.id` and
`tunit.test.node_uid`. Invocation-specific context, session, span, MTP test and
execution IDs are retained in `evidence.json`.

Lifecycle journal schema 2 additionally requires each API and Persistence case
to retain a database witness from its native database owner after actual use.
The terminal record carries the public configuration digest, native provider
code, actual server version and observation time. The launcher compares those
facts with the selected isolated test database and requires the observation to
fall within that case's own TUnit test-body span. A setup/preflight observation
or a passing body that used a different database cannot substitute for this
witness. Domain cases must carry no database witnesses.

The audience detail membership-loss, page continuation-loss and six
action-withdrawal bodies in `EventResourceDiscoveryQueryTests` now retain their
actual SQLite observations after exercising the native workflow. Their
measurement-only context uses the fixture's same live connection and native
structured public database composition; it does not invent a configuration digest.
The existing assembly receiver supplies native journal identity, not a new
method-level measurement annotation. Catalogue case IDs must be mapped from
final native identity discovery. When the launcher enables the journal,
the fixture binds its explicitly injected `Database` environment projection
through native provider composition and uses that selected test database for
ordinary, measured and independent contexts. It requires Testing with the
Environment secret authority, serializes shared schema/lookup initialization,
and records actual provider/version/public identity within each exercised body.
Ordinary nonmeasurement runs retain the isolated named-memory SQLite fixture.
These bodies remain unrun, and complete provider/race/moderation-overlay assurance
is still required. Do not waive a configuration mismatch or publish them as a
complete conformance pack from this source alone.

The writer and strict join are authored; complete per-case fixture
instrumentation and provider portability remain open. Missing witnesses fail
closed. Do not publish a catalogue or issue a measurement assertion until its
actual native case owners supply the complete required evidence.

Original-recipe challenges retain a complete immutable effect/snapshot/effect-
binding anchor, authenticated by their original native preparation receipt.
Preflight and postflight reconstruct that exact request and require the retained
notice metadata, original published contribution, live actors and hard deadline.
They do not read the private notice snapshot or select an arbitrary old publication.
The published-candidate definition must still match its original publication;
candidate preparation expiry cannot renew or replace original custody.
The accepting writer independently reacquires retained privacy and primary fences.
This path creates no activation permission, late completion or extended signature.

## Configuration and identity

The only public arguments, in this order, are:

```text
--tenant <existing UUIDv7>
--challenge <existing UUIDv7>
--issuer-enrollment <existing UUIDv7>
--output <existing absolute directory>
```

Compile and invoke the Release host from its repository-native location:

```bash
dotnet build eng/moderation/MeasurementLauncher.csproj --configuration Release
dotnet eng/moderation/bin/Release/net10.0/ISLAMU.Moderation.MeasurementLauncher.dll \
  --tenant "$TENANT_ID" --challenge "$CHALLENGE_ID" \
  --issuer-enrollment "$ISSUER_ENROLLMENT_ID" --output "$MEASUREMENT_OUTPUT"
```

These commands are operational instructions, not evidence of execution in this
change. The whole-programme no-test-execution override still applies.

`SecretProvider:Provider` or `SECRET_PROVIDER` must explicitly select
Environment, Infisical or UserSecrets. All configuration and private material
come from `SecretAuthorityBootstrap.OpenAsync` and the selected owner's
`PrepareConfigurationGroupAsync`, loading `/database` and `/moderation` with the
compiled `ModerationMeasurement` consumer. Shared UserSecrets remain Development/Testing-only and never act
as fallback. The signer definition is
`moderation.measurement_signing_private_key_pem`, with environment/vault alias
`MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM`. The existing Infisical mapping
for `/moderation` normalizes that alias to
`Moderation:MeasurementSigningPrivateKeyPem`. The host reads the normalized key
and environment alias, rejecting conflicting values. Public SPKI must exactly
equal the native live enrollment; only P-256 is accepted.

Receipts separately sign `PrimaryConfigurationDigest` and
`TestConfigurationDigest` from the shared public coordinate grammar. The
existing combined `ConfigurationDigest` identifies the whole launcher recipe;
it is not a runtime-only fingerprint. Credentials and credential hashes appear
in none of these identities. Actual exercised-provider witnesses must match
the test identity, while deployed-runtime comparison matches the primary one.

The primary database uses the existing structured `Database` runtime
configuration. An independently selected, **different** test database is required
under `Moderation:MeasurementTestDatabase`. Its fields are the existing
`PrimaryDatabaseConfiguration` fields (`Provider`, `Host`, `Port`, `Database`,
`Schema`, `Username`, `Password`, `TlsMode`, `TrustServerCertificate`,
`ServerFlavor`, `ServerVersion` as applicable). Environment injection uses
`MODERATION__MEASUREMENTTESTDATABASE__<UPPERCASEFIELD>`. In `/moderation`, the existing
Infisical mapping accepts `MODERATION_MEASUREMENT_TEST_DATABASE__<FIELD>`.
All required credentials must already be supplied by the selected authority.
There is no database creation, migration, authority inference or provider fallback.

The launcher uses the native `ModerationMeasurement` consumer, whose roots are
only `/database` and `/moderation`. Its intake and final revalidation each own
their prepared configuration and authority lifetime. Native token maintenance
belongs to each selected session and ends when that selection is disposed.
Serving and migration consumers cannot acquire the measurement root.
SQLite files must already exist at distinct regular paths. Server database names
must differ even across endpoints; DNS spelling or a default-port difference is
not evidence of database isolation.

The child environment is cleared, then receives only that test database's
`Database__*` inputs, explicit Environment/Testing selection, isolated HOME/TMPDIR,
runtime/locale controls and the three nonsecret journal variables. It receives
neither signer material, vault bootstrap credentials nor inherited configuration,
profiler/startup-hook or telemetry variables. Primary-database configuration is
not copied; only the separately selected test-provider inputs are projected.
The current contract does not inject Docker, external API, Cerbos or other
provider credentials. Catalogue cases requiring them cannot qualify under version
1 until an explicitly approved bounded provider-input contract is added.

## Native source checks and digest contracts

For a not-yet-active revision, nonce preparation binds an immutable native
publication candidate's reserved future identity. The committed definition has
its own source ID and is never a selected policy. Preflight/postflight verify
the exact original preparation digest, preparer role, unchanged predecessor,
canonical intended content and finite window. Final native publication must
use those same reserved coordinates; signing never creates an active pointer.

Before and after execution, short serializable, non-retrying read transactions
independently load the primary host, active tenant, live maintenance principal,
qualification epoch, latest consumer installation, challenge, selected publication
and full consumer contract, signer enrollment, current human roles, and original
kind 15/16/17 approval receipts. PostgreSQL binds `app.current_tenant_id` with the
native connection interceptor and rejects superuser/bypass-RLS sessions.
Original approval digests are reconstructed using the native request grammar,
including their actual operation IDs, principals, generations and timestamps.
The runtime's trust-binding setter is assembly-internal. External-host reads
replace only the named `ModerationAuthority` filter with explicit independently
read host/trust/tenant predicates on every query; publication descendants remain
joined to that qualified root. Other filters, ambient tenant binding and RLS stay
enabled. No reflection, internal-visibility change or global filter bypass is used.
Changed source provenance or epoch rejects the run. No database transaction or
fence survives across child execution.

SHA-256 digests are lowercase hexadecimal, except native existing publication
digests retain their native spelling during comparisons. Directory digest input
is default `System.Text.Json` UTF-8 serialization, in this property order:

```text
{"format":"moderation-directory-v1","files":[
  {"path":<slash relative path>,"length":<byte count>,"sha256":<file digest>}
]}
```

Every file is included, sorted by ordinal full path, including dependency/native
libraries and configuration. Symlink/reparse-point paths are rejected. Maximum
directory inventory is 20,000 files, 512 MiB per file, 4 GiB total.

`RunnerDigest` hashes `{"format":"moderation-runner-v1","directory":...,
"dotnet":...,"sessionLauncher":...,"runtime":...}`: the launcher output-directory
digest, fixed dotnet host digest, `/usr/bin/setsid` digest, and loaded core runtime
assembly digest. `TestBundleDigest` hashes
`{"format":"moderation-bundles-v1","bundles":[{"bundle":...,"digest":...}]}`
in the fixed three-job order. `ProductArtifactDigest` hashes
`{"format":"moderation-products-v1","products":[{"assembly":...,"sha256":...}]}`
for the fixed assembly order in `ModerationMeasurementProductManifest.AssemblyNames`. Every present
product assembly copy across bundles must equal the API bundle; launcher copies
must also match. Launcher and deployment use the shared
`ModerationMeasurementProductManifest.RuntimeGeneration` recipe: SHA-256 of
default `System.Text.Json` UTF-8 serialization with ordered fields
`format = "moderation-consumer-runtime-v1"`, `consumerId`, numeric `mode`,
`inventoryFingerprint` and `productArtifactDigest`. Product bytes include the
explicit concrete owner mappings compiled into the API. The deployed source
separately requires exact current service implementations and verifies loaded
module identities against their on-disk product assemblies. The launcher
reproduces the artifact identity, not service availability or passing behavior.
Catalogue consumer identity must equal the original native challenge consumer;
one consumer's measurement cannot be relabeled for another. This deliberately
fails closed on mismatched deployment artifacts, not merely source revision labels.

`ConfigurationDigest` hashes the ordered versioned public projection in
`MeasurementConfiguration`: selected authority/environment, primary/test provider,
role, endpoint/database/schema/TLS/version coordinates and the fixed child
environment contract. It excludes usernames, passwords, connection strings,
signer material and vault bootstrap credentials. Never hash plaintext credentials
to create a public configuration identity. Test provider version is read from
the actual opened provider connection before and after execution.

All bytes/configuration/source coordinates are checked again after execution.
Catalogue/config/product digests and runtime version are independently approved
by the catalogue, while runner/catalogue/bundle digests are independently approved
by native enrollment.

## Owned evidence and acceptance

A fresh UUIDv7 names a private mode-0700 run directory. Each bundle gets exact
private stdout/stderr, process exit and closed-ownership records, a lifecycle
JSONL file, HTML plus `.tunit-report.json`, and TRX. The fixed options include
`--report-html --report-html-filename <absolute-run-directory>/<bundle>.html`
(the supported reporter emits sibling `<bundle>.tunit-report.json`),
`--report-trx --report-trx-filename <bundle>.trx`, `--results-directory`,
`--minimum-expected-tests` and the enrolled filter. Unknown/unsupported options
must fail through nonzero exit or absent evidence, never through fallback.

Journal sequence must be contiguous: one header, exactly one start, early-end,
late-end and terminal per expected case, then one completed footer. All retries,
prior attempts, original failures/exceptions, overrides, missing or repeated
events, and unsuccessful final states reject. TUnit schema 1/version 1.63.0.0
is parsed through `summary`, `groups[].tests` and `spans`, **not top-level
`tests` or `attempts` fields**. Exact test-case tags, session/filter/count,
test-assembly identity and one body span joined by trace/parentSpanId are required.
Every MTP definition/result/entry is unique and must join its enrolled exact
identity, with successful counters and no error/run-info records. All UTC times
and numeric durations are finite and bounded by the actual child interval.

Each child has a 15-minute maximum, further shortened by source expiry.
Cancellation/SIGTERM subscribes before start. The fixed `/usr/bin/setsid` execs
dotnet in a private process group; cancellation closes that group as well as the
exact process tree. Surviving descendants after leader exit reject the run.
The exact process/pipes are drained, with a bounded 30-second cleanup.
Report/output files are bounded to 32 MiB, execution directory events enforce
512 MiB/4,096-file limits and watcher failures abort. Raw output and exception
text are never printed or sent to telemetry.

The combined result-artifact digest covers `evidence.json`, which retains all
artifact hashes, native case IDs, counts, process outcomes and source/configuration
coordinates. Signing is inline only after this owned successful flow, using the
native closed canonical receipt converter and ES256 P1363 signature format.
The native verifier checks the just-signed token before publication.

The final `acceptance.json` contains a complete
`AcceptModerationConsumerMeasurementCommand`, atomically renamed from a partial
file only after all checks. Submit it through the existing authenticated native
acceptance flow. The launcher does not manufacture an authenticated human context,
write acceptance directly, activate policy, or declare conformance. Native
acceptance/requalification must reacquire current authority and commit its own
immutable receipt. A stale epoch, revoked approver or expired deadline can still
reject this request.

Exit 0 means **assertion issued, acceptance required**; 64 means CLI misuse,
65 means a bounded contract rejection, 130 means cancellation, and 70 means
unavailable provider/framework/IO infrastructure. Diagnostics expose only machine
categories. Partial directories are retained privately as failed-run evidence;
they are not reusable inputs and no resume/sign-existing-results command exists.

## Remaining final-assurance obligations

Publish and independently enroll the real catalogue and immutable artifacts.
Execute the launcher only once the whole programme permits tests. Verify installed
TUnit report filenames, MTP TRX identity/time serialization, journal-to-span joins
including fixtures/parameterized tests, and provider-specific environment needs.
Exercise interrupted/partial writes, cancellation/drain, every retry and override,
tampered/extra/missing cases, expiry and concurrent native revocation/role changes.
Run all approved provider lanes and native acceptance/requalification. Historical
`TestResults` files supply schema evidence only, never qualification.
