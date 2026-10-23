# Local password policy ownership

> **Audience:** Identity and deployment maintainers
> **Status:** Source authored; execution deferred by programme override
> **Source anchors:** `Explore.Domain/Authentication/LocalPasswordPolicy.cs`,
> `Explore.Persistence/Identity/LocalIdentityPasswordRegistration.cs`

## Canonical credential identity

The approved D08 contract required a single normalization convention but did not
fix one. This source unit selects NFC (`NormalizationForm.FormC`) for every
Local hash and verification path. Case, whitespace and compatibility distinctions
are retained: NFC is not NFKC, trimming or case folding. Canonically equivalent
inputs identify the same credential, including when checking whether a proposed
replacement equals the current credential. No raw verification fallback is
introduced for pre-release data.

`LocalPasswordPolicy` first bounds input to 1,024 UTF-16 code units, then decodes
every scalar with `Rune.DecodeFromUtf16`, rejecting malformed input without
replacement characters. It normalizes and counts scalars, stopping above 128.
Creation requires 15–128 normalized scalars; verification requires 1–128. This
avoids `string.Length` as a password-policy shortcut and admits supplementary
characters consistently. The raw input bound limits normalization work, not
password composition.

The Application's manually constructed validators and `LocalPasswordAttribute`
delegate to those two Domain admissions. The attribute replaces UTF-16
`StringLength` metadata for change, recovery and replacement. Onboarding and
agent-persona bootstrap gates use the same creation shape. Native request/result
constructors reject null/empty values without banning preserved whitespace.
OpenAPI/client regeneration must follow the authored attribute change; generated
files are not manually edited here.

## Enforcement before mutation

The platform catalogue, API alias mapper and Compose Local Identity projection
own `AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_PATH`,
`AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_SHA256` and
`AUTHENTICATION_LOCAL_PASSWORD_HASH_ITERATIONS`. These public policy controls
are startup-owned with process restart semantics. AppHost's final public
catalogue projection forwards them only to API/Combined processes; no BFF
credential acquisition is added. Canonical keys override friendly aliases.
An operator supplies a read-only mount separately; no corpus or guessed
host/container path is bundled. The native Staging combined-host test fixture
generates and pins its own synthetic snapshot through environment authority.

`PersistenceServicesRegistration` calls `AddLocalPasswordPolicy` after Identity
composition. It replaces Identity's default composition/UTF-16 password validator
with `LocalIdentityPasswordValidator` and replaces the hasher with
`NormalizedLocalIdentityPasswordHasher`. The wrapper normalizes both
`HashPassword` and `VerifyHashedPassword`, delegating cryptography and format
upgrade decisions to native `PasswordHasher<LocalIdentityUser>`.
`UserManager.CheckPasswordAsync` therefore normalizes login and persists native
rehash without modifying Phase 12's public mapper, dummy-work paths, lockout,
durable ownership latch or precommit failure classification.

`LocalIdentityCredentialStateStore` uses the creation admission for bootstrap and
restricted replacement. Generated administrator create/reset credentials still
pass the same native validators before persistence. Bootstrap prevalidation
occurs before application-side admission. `LocalIdentityLifecycleStore` keeps
current-password shape separate from new-password validation. Change and recovery
run the native validator before producing a hash or executing conditional writes.
The blocklist never affects ordinary verification of a currently stored
credential, and a rejected proposed mutation cannot consume its proof.

Existing serializable transactions, conditional writes, authority/deadline
rechecks and owned-entity detachment remain the mutation boundary. No new
cross-database atomicity claim, background retry or destructive credential reset
is introduced. Policy rejection retains password hash, stamps, state token,
operation stage and unconsumed lifecycle authority.

## Snapshot custody and publication

`LocalPasswordBlocklistSnapshot.Load` captures one operator-owned file during
composition. The capture is bounded to 16 MiB and 100,000 entries, verifies the
configured SHA-256 pin, parses strict version-1 JSON, rejects unknown/duplicate
members and validates bounded revision/source/license metadata. Each complete
entry passes the verification-shaped Domain normalization; duplicate normalized
entries invalidate the capture. An ordinal frozen set is published only after
the complete manifest succeeds. Invalid input publishes an unusable, empty
snapshot, not a partial dataset.

Manifest member names are exactly `Version`, `Revision`, `Source`, `License`,
`Passwords`. SHA-256 pins file integrity, not origin authenticity: deployment
custody must protect both the read-only mounted file and its configuration pin.
The metadata records provenance/terms references; it cannot certify legal
approval, corpus quality or compromise coverage. No corpus, dependency, imported
third-party source or password-dependent remote lookup is introduced.

`LocalPasswordPolicyHealthCheck` is tagged `ready`. It rejects unusable snapshots
for configured/default Local-primary deployments; all Local credential mutations
reject unusable snapshots even under a different primary provider. Liveness is
independent. Status contains a fixed machine field set with effective native
hasher options, not paths, manifest strings, passwords or hashes.
There is no reload protocol: the operator atomically publishes complete bytes
and a corresponding digest pin, then deliberately restarts consumers. A running
process retains its validated immutable capture.

Canonical configuration keys are
`Authentication:Local:PasswordBlocklistPath`,
`Authentication:Local:PasswordBlocklistSha256`,
`Authentication:Local:PasswordHashIterations`. Phase 10/parent owns friendly
catalogue aliases, host/environment projection and shared environment guides.
This source unit does not edit those owned artifacts.

## Adaptive hash and operational decisions

The effective hasher is pinned to IdentityV3 with 100,000–2,000,000 iterations
(default 210,000). Native Identity uses PBKDF2-HMAC-SHA512, 128-bit independently
generated salts and 256-bit subkeys. Options validation rejects unsupported mode
or work factor. The status reads the same `IOptions<PasswordHasherOptions>` as
the wrapper, not a separate hard-coded configuration estimate.

`eng/tools/local-password-calibration` is an offline, database-free native
Identity calibration source. It uses runtime-generated synthetic input, bounds
iteration/sample arguments, warms hashing/verification and prints only parameters
and sample percentiles. It is not executed by tests, startup or this source unit.
The deployment operator must choose and record a calibrated value on documented
hardware and representative concurrency, rather than interpreting the default
as a benchmark receipt. Raising cost lets successful verification request native
rehash; compromise-driven replacement remains an explicit authorized operation,
not scheduled expiry.

## Authored evidence and deferred gate

No test, build, benchmark, calibration, generator, validator, package operation
or server was executed for this source unit. Automatic editor diagnostics were
stale or timed out and do not establish compiler success.

Authored cases:

- `LocalPasswordPolicyTests`: ASCII/supplementary 14/15/128/129 boundaries,
  canonical equivalents, case/whitespace retention, passphrases, malformed UTF-16
  and verification admission distinct from creation minimum.
- `LocalPasswordAdmissionTests`: native manual/HTTP attribute admission and
  supplementary bounds without a UTF-16 annotation shortcut.
- `LocalPasswordBlocklistSnapshotTests`: full blocked long password and canonical
  equivalents; allowed substrings/extensions; immutable capture; invalid/missing
  pins, versions, metadata, unknown fields, canonical duplicates, count/byte
  bounds; required readiness denial and safe effective status fields.
- `LocalPasswordMutationPolicyTests`: bootstrap/replacement/change/recovery
  denial preserving durable state and original proof, generated create/reset
  denial with an unusable real validator snapshot, 15/128 supplementary acceptance
  across mutations, NFC creation and verification, independently salted persisted
  native hashes and successful persisted work-factor rehash. Both colocated and
  external Identity topology cases use native SQLite stores, not mocked mutation.

The existing `LocalCredentialFirstUseTests.Fixture` now composes the production
password registration with a runtime-generated, digest-pinned test snapshot.
There are no inline credential/hash fixtures, source-scraping assertions,
mock-call mirrors or sleeps in the new cases.

Final programme assurance must compile the affected Domain, Application,
Persistence, API and calibration source; run the four new classes and the
existing Local creation, reset, first-use, lifecycle, bootstrap, authentication,
token-purpose and Phase 12 recovery/delivery regressions; cover the approved
real-provider/topology union and existing architecture gates once. Queue
OpenAPI/client regeneration, calibration-project restore lock generation,
environment/catalogue projection, operator snapshot license/custody evidence,
deployment calibration and D08 I-VSD revalidation with the parent. No pass,
release readiness, provider result or generated artifact is claimed here.

The operator-facing procedure lives in
[Local password policy](../public/documentation/readme/configuration-and-operations/local-password-policy.md).
