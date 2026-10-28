# Private Learning State And Authored Practice

> **Audience:** Developers and operators
> **Status:** Backend implemented; scoped assurance and baseline exclusions below
> **Last Verified:** 2026-10-28
> **Source Anchors:** `src/Explore.Domain/Learning/`, `ILearningRepository`, `ILearningPrivacyErasureStore`, `PrivacyErasureApplier`

## Delivery Boundary

This workstream adds a private learning backend, not rendered tours, a learning
workspace or a live AI mode. Required successors first compose isolated practice
in the actual event/chat presentation, then add contextual invitations and
guidance. Completing this backend cannot discharge those successors'
accessibility, comprehension or package-admission release gates.

Learning is optional. An authenticated, active tenant member may learn in self
context without publishing, organization administration or event-creation
authority. An actor context requires current membership in that actor's actual
tenant placement. Learning never grants production authority, changes provider
readiness or completes tenant provisioning.

## Domain Ownership And Bounds

`TenantLearningSpace` is the tenant's single `learning-core` material-version-1
source. Its unique tenant key resolves concurrent first starts. Removal is a
durable tombstone: a read must not reconstruct the source, and recreation must
be an explicitly authorized operation. `RemovedAtUtc` retains the last removal
even after explicit recreation.

`UserLearningState` belongs to one tenant and one resolved internal `User.Id`,
not a provider subject, device identifier or administrator-supplied owner. It
owns private preferences, tour progress and practice. Tenant identifiers reject
reassignment. No practice-to-production user/actor/event foreign key is added;
actor context is a canonical scalar key.

There are four tour families: Shell, Studio, EventManagement and NormalAi. Each
retains only the current and previous material versions, at most eight progress
rows. One learner has at most one active claim across tabs. The inactivity
deadline is 120 server seconds. Only an advancing explicit progress step renews
it; retries, unchanged steps, preference writes and practice activity do not.
An invitation may be skipped without claiming. Other outcomes are claim-bound.
Manual replay preserves the earlier completed/skipped outcome.

Practice uses canonical `self` or `actor:{guid:D}` context keys, at most 20
contexts, six recipe keys per context, 1 KiB per record and 128 KiB per learner.
State consists of bounded action/choice identifiers and integers, never arbitrary
prompts or production payloads. An owner can clear a practice in an orphaned
context without regaining preview authority.

Caller-supplied UUIDv7 identifiers and explicit UTC times are retained across the
one original attempt. Exact retained operation receipts are idempotent; changed
payloads conflict. Receipt retention is bounded to the aggregate's latest intent
and each retained child's latest intent, not an unlimited request journal.

## EF Persistence And Isolation

All four entity types carry the named tenant filter. Repository reads include
explicit tenant and internal-user predicates; knowing a practice UUID does not
authorize its retrieval. Queries do not create either root.

Root `Revision` values and practice `Revision` are application-managed `long`
concurrency tokens. Insert uniqueness is a separate conflict path from EF
update/delete token mismatches. `LearningWriteConflict` contains the narrow
provider exception classification; handlers must receive a typed conflict,
not a provider-specific error or a silent retry.

PostgreSQL's existing migration composition installs fixed learning storage
rights through `PostgresLearningPermissions`. Runtime has CRUD on the four
learning relations under the existing forced tenant RLS. The established Global
erasure purpose receives only SELECT/DELETE on learner roots, with a
purpose-specific all-tenant RLS policy; child disposal follows the foreign-key
cascade. It receives neither practice-detail access nor insert/update authority.
Because the declared Global login is NOINHERIT, the installer grants those two
fixed root rights directly as well as to the existing erasure role. The root RLS
policy still requires current membership in that erasure purpose; direct table
rights do not preserve row visibility after purpose membership is revoked.
The committed erasure fact, exact subject and retained exclusion remain C#
authority. Compromise of that erasure database credential can read/delete learner
roots across tenants; this is the documented application-owned persistence
trade-off, not a second SQL workflow or a subject grant supplied by the client.

Both roots cascade from the tenant. Children cascade only from their learner
through the composite `(TenantId, UserLearningStateId)` key. This enforces child
tenant consistency without adding a second production-owner cascade route,
including on SQL Server. No actor or production user is deleted by learning.

The generated `AddTenantLearningState` migration cohorts belong to the primary,
SQLite, SQL Server and MySQL migration owners. MariaDB consumes the MySQL cohort
but requires independent execution evidence. Migrations and snapshots are
generated artifacts; edit entities/configurations and regenerate instead.

## Common Erasure Boundary

`ILearningPrivacyErasureStore` does not grant erasure authority or record
coverage. Its caller is the existing protected erasure workflow.
`PrivacyErasureApplier` first verifies retained recognition and fences the
internal subject. It then purges learning across tenants and proves absence
inside that original transaction, before either its coverage return or native
PostgreSQL return.

Covered retained replay must enter this common applier too. Skipping directly
from retained coverage would leave later private learning rows unexamined.
An absence failure aborts settlement; a rollback retains both the prior learning
rows and the prior coverage state. Provider/native-role and both race-order
evidence remain mandatory before backend completion.

## Closed Simulation Contract

The immutable wire contracts live in `Event.Wire.Contracts/Learning`, namespace
`ISLAMU.Wire.Contracts.Learning`. This follows the project's `ISLAMU` root;
introducing a root `Event` namespace shadows the existing Domain `Event` type.
Enums accept exact named strings only, not numbers or numeric strings. Requests
reject unmapped ownership/effect fields, and published collections snapshot
their inputs. HAL extension data remains available.

`LearningLessonCatalog` owns finite transitions and reconstructs authored
presentation from the persisted machine state:

| Recipe | Allowed choices |
| --- | --- |
| `event-details` | `in-person`, `online` |
| `sessions-program` | `single-session`, `two-sessions` |
| `ticket-capacity` | `capacity-12`, `capacity-24` |
| `attendees-team` | `attendee`, `helper` |
| `publication-lifecycle` | `keep-draft`, `preview-publication` |
| `ai-example-review` | `accept-example`, `reject-example` |

Start/reset persist step 0, selection persists step 1, and confirmation persists
the authored result step 2. Receipts explicitly identify simulation and the
absence of production effects. No live provider, conversation/history, outbox,
production event/order/attendee repository or external-effect dependency belongs
in this execution path.

## Original Human Settlement And Exposure

`HumanLearningMutationPreparation` retains one privately issued intent reference,
original request/principal/credential, selected tenant and stable candidate
identifiers/time. It observes native principal, support and optional event
sources, then acquires and validates them under the store's physical owner.
Event management uses the native owner/manager and explicit update,
registration-management or check-in-management projection, not attendance or
event-creation permission. Support and machine authority cannot replace self.

`HumanLearningMutationStore` uses the existing
`ExecuteOriginalOwnerIdentityMutationAsync` recognition scope and
`IOriginalOwnerSettlementBoundary`. Co-located SQLite retained authority is the
physical owner; separate authority encloses the selected primary owner. This
retains the single-attempt semantics while adding original retained-gate custody,
disposal and opaque receipt consumption. It does not widen the instance tenant
preference port or introduce a generic caller-supplied transaction delegate.

The expected EF scalar image is captured before saving and compared against
no-tracking persisted roots/children during final admission. Native source
validation and terminal credential, event-assignment and claim clocks run at the
actual commit. Nonlearning changed entities are rejected. Exceptions after a
settled receipt remain unknown outcomes instead of becoming a safe-to-retry
business rejection.

Preparation truncates candidate transition time to microsecond precision shared
by the supported providers. Image comparison canonicalizes UTC kind but does not
discard timestamp values. The expected tracked-entity inventory is also retained
before saving, so an additional owner or entity introduced by a save extension
cannot become an unnoticed `Unchanged` entry after persistence.

Private reads hold the original retained-authority gate through settlement and
reject authority-first user-erasure facts even while local account, membership
and learning rows still exist. Principal facts include organization/group
placement suspension; final source comparison therefore fences that revocation
without requiring event publishing authority.

The native shell's Studio setting/managed actors and normal-AI configuration
readiness supply contextual tour availability. Credential readiness resolves
before physical ownership, never by opening an AI conversation under a database
transaction. Synthetic AI review is independent of live provider readiness.

Before exposing `/api/learning`, the native Application/API slice must prove
current human and tenant authority, exact selected-context/original-transaction
custody, retained erasure exclusion, completion-image validation and terminal
credential/claim deadlines. Closed commands must not accept caller delegates or
a reusable authorization grant.

Private GET handlers must require the current human despite controller-level
`[AllowAnonymous]`, return `no-store`, and expose only server-authorized HAL
actions. Writes use `[Authorize]`; support sessions, machine credentials,
foreign/stale scopes and stale revisions must fail closed. Generated OpenAPI and
clients follow those verified routes, not hand-maintained parallel DTOs.

The route and retry details are documented in
[API Architecture](API.md#private-contextual-learning). Scope stamps partition
late responses; they are not grants. A mutation receipt includes affected `Id`,
scope stamp, learner/resource revisions and only its authorized private state.

The [operator guide](../public/documentation/readme/getting-started/contextual-learning.md)
describes the adopter boundary. The task context records actual commands,
evidence and open gates; source structure alone is not acceptance.

## Verification And Release Readiness

The backend has real HTTP, database and restricted-role evidence. This is not a
green whole-solution release: independently reproduced baseline failures still
block the complete provider and architecture gates. No warning, assertion or
native authority check was suppressed to obtain the scoped results.

Fresh Release builds of the Persistence integration, API integration and
Architecture test projects passed with project references enabled:

```bash
dotnet build tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --no-restore --verbosity quiet --nologo --tl:off
dotnet build tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --no-restore --verbosity quiet --nologo --tl:off
dotnet build tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --no-restore --verbosity quiet --nologo --tl:off
```

| Surface | Observed evidence | Boundary |
| --- | --- | --- |
| Domain | 27 learning cases passed, including the observed confirmation-step Red and its repair. | Pure in-memory state and bounds, not native authorization. |
| HTTP | All 30 distinct learning cases passed. After the privacy-owner move, all 4 actual race/pool cases and both retained-erasure order cases passed again. | The native HTTP fixture uses SQLite; this does not prove the full human writer on every engine. |
| Generated contracts | Final rebuilt-capture suite passed 27/27 in one run, including the 3 learning schema cases. An earlier missing-assets setup failure was corrected by the required restore. | Exact generated JSON shapes, operation IDs and closed enum tokens; no hand-edited schema/client. |
| Architecture | All 5 new learning boundary cases passed. Affected authorization, naming and provider-boundary reruns contain no learning or `TenantRepository` violation. | The full suite is still blocked by the 7 baseline failures described below. |
| SQLite | 9 provider cases, 4 erasure cases and the generated-cohort round trip passed; final erasure rerun passed 4/4. | The full historical migration chain remains blocked before learning. |
| PostgreSQL 16 | 8 provider cases, 4 erasure cases and both full-history and generated-cohort migration cases passed. | Tenant retirement uses the existing fully enrolled PostgreSQL 18 fixture and is a separate storage-owner check. |
| PostgreSQL 18 restricted roles | All 3 Runtime/Global cases passed: actual tenant RLS, retained-fact root purge, and denied child-detail/insert authority. | Native retained-role setup uses the current model; it does not certify the retained context's historical migrations. |
| PostgreSQL 18 tenant retirement | The actual repository path fails `discovery_traversal_runtime_table_access` before learning deletion. The unchanged positive traversal case fails identically on untouched `develop`. | Blocked existing native discovery contract, not a passing learning cascade or a reason to change credentials. |
| SQL Server | All 9 provider cases, 4 erasure cases and the generated-cohort round trip passed. After primitive extraction, final erasure 4/4 and tenant retirement 1/1 passed again. | The full historical migration gate remains baseline-blocked. |
| MySQL / MariaDB | Both 15-case gates were physically attempted; both stop at the same pre-existing schema failure. | No learning runtime or migration pass is claimed for either engine. |

The existing anonymous adversarial review identified admission ordering, retained
read exclusion, placement suspension, provider timestamp precision, save-time
write inventory and affordance consistency concerns. The implemented repairs
retain the native owners rather than bypassing their guards. HTTP regression
cases prove successful settlement and deterministic rejection/rollback; they do
not turn unexecuted provider scenarios into passing evidence. The complete
release review remains blocked while the baseline gates below are red.

### Reproduce The Targeted Runtime Gates

Use the freshly built test executable, a private results directory and exact
TUnit filters. Native provider fixtures require the explicitly selected
`Database__Provider` and approved environment-injected Migrator configuration.
SQLite also requires a persisted local path in `Database__Database`; the fixture
creates its own unique disposable database instead of modifying that source.
Never put credentials into a command committed to the repository.

```bash
dotnet tests/Event.API.IntegrationTests/bin/Release/net10.0/Event.API.IntegrationTests.dll --treenode-filter '/*/*/*Learning*/*'
dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll --treenode-filter '/*/*/*LearningProviderContractTests/*' --minimum-expected-tests 9 --maximum-parallel-tests 2
dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll --treenode-filter '/*/*/*LearningErasureTests/*' --minimum-expected-tests 4 --maximum-parallel-tests 2
dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll --treenode-filter '/*/*/*LearningMigrationRoundTripTests/*' --minimum-expected-tests 2 --maximum-parallel-tests 2
dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll --treenode-filter '/*/*/*LearningPostgresPermissionsTests/*' --minimum-expected-tests 3 --maximum-parallel-tests 2
dotnet tests/Event.Architecture.Tests/bin/Release/net10.0/Event.Architecture.Tests.dll --treenode-filter '/*/*/*LearningDependencyBoundaryTests/*' --minimum-expected-tests 5
```

Native fixture startup and fresh compilation took minutes, exceeding the nominal
fast-loop targets. Preserve independent passing slices and rerun only cases
affected by a subsequent change or a corrected setup; do not hide cold-start
costs or label a generated-cohort pass as a full migration-chain pass.

### Quarantined Baseline Gates

Each failure below was physically reproduced on untouched `develop` at
`20737e450db0df748b3e5737bcb3405f54c37cd8`. Repair belongs in a separate
workstream; the learning backend does not weaken these gates.

| Existing failure | Effect on acceptance |
| --- | --- |
| `tests/Event.Application.UnitTests/Features/SetupLive/SetupLiveApplyContractTests.cs:6` repeats a using directive (`CS0105`). | Application tests cannot compile; the authored learning unit slices remain unrun. |
| SQLite migration `20261019145153_AddNativeModerationSourceOwners` has an `ef_temp_ie_event_report_signals` / `ie_event_report_external_links` foreign-key mismatch. | Fresh historical migration cannot reach the learning cohort. |
| SQL Server `ExploreDbContext` reports pending model changes. | Full historical SQL Server migration acceptance is blocked. |
| MySQL and MariaDB reject `CREATE TABLE ie_moderation_child_age_policy_sources` with `ERROR 1118` (row too large). | Common schema setup blocks all 15 learning cases on each engine. |
| Existing Global retained-subject workflow fixture fails `tenant_lease_not_bound` before learning assertions. | Its two integrated workflow cases are not certified by the separate passing restricted-role learning tests. |
| `CoLocatedPrivacyErasureAuthorityDbContext` reports pending model changes. | Current-model native-role setup is not historical migration proof for retained authority. |
| Untouched architecture suite passes 682/689, with 7 moderation/legacy authorization-inventory, naming, raw-EF/direct-ADO/provider and PII-inventory failures. | The complete architecture gate remains red. Initial feature run passed 686/694; subsequent scoped repairs remove the learning-specific deltas without rewriting baseline inventories. |
| Existing positive `PostgresDiscoveryTraversalTests.OrdinaryRuntimeCapturesReferencesAndReadsThemWithoutPrivateTableAccess` fails `55000: discovery_traversal_runtime_table_access`. | Native PostgreSQL tenant retirement cannot reach learning deletion. Its reservation primitive requires the enrolled Runtime login and forbids direct table rights, while the existing repository subsequently performs direct discovery-row deletes. Switching to Migrator cannot satisfy that Runtime-only contract. |

The enrolled native PostgreSQL fixture uses version 18. Its existing `MAINTAIN`
privilege contract fails on version 16 (`22023`); successful primary-model
PostgreSQL 16 migrations must not be described as native-role acceptance on 16.
The provider storage contract uses the selected Migrator and real
`TenantRepository.Delete`; only the separate Runtime/Global tests establish
restricted-login authority.
