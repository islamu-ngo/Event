ABOUTME: Primary entrypoint for authentication, authorization, and trust-boundary documentation.
ABOUTME: Points readers to the maintained security model while preserving documentation index links.

# Security

> **Audience:** Operators | Contributors | AI agents
> **Status:** Mixed
> **Owner:** Security
> **Last Verified:** 2026-08-30
> **Source Anchors:** `docs/SECURITY-MODEL.md`, `src/Explore.Application/Features/ConfigurationManifest/Importing/`, `src/Explore.Application/Features/ConfigurationManifest/Managed/`, `Explore.Blazor/Extensions/YarpProxyExtensions.cs`, `Explore.Blazor/Services/CircuitTokenStore.cs`

The maintained security model lives in [SECURITY-MODEL.md](SECURITY-MODEL.md).

Use that document for the current BFF trust boundaries, token-forwarding model, antiforgery contract, auth diagnostic safety, support-access boundary, upload-session binding, and circuit token lifecycle notes.

Configuration portability is a Tier 1 administration boundary. Upload tokens,
direct-transfer nonces, and destination proofs are header-only capabilities;
responses are private/no-store and bounded by size, rate, and timeout policies.
The authenticated route selects instance or tenant authority, HAL controls UI
affordances, and authorization is rechecked on every write. Apply recomputes the
preview under ordered locks and one serializable transaction. Secrets, PII,
application data, provider bindings, operational state, and deployment topology
are excluded by the closed registry. Direct transfer accepts only public
HTTPS/443 destinations, distinct mutual approvals, digest-bound chunks, and
promotion into the ordinary preview/apply workflow; a signature or transfer
proof never grants configuration authority.

The approved lifecycle-email plan adds dispatch-time privacy authorization without changing SMTP into an authority. Email may target only a current verified tenant member or an authorization-bound same-tenant managed tenant-administrator invitation. Incompatible pre-1.0 delivery ledgers are reset instead of receiving a synthetic legacy authority; inbox notifications and unrelated business/audit/settings data remain preserved. Consent, preference, disclosure, deletion, and supersession may narrow a snapshotted policy before provider handoff, never broaden it.

## Human Event-provider checkpoint admission

`RegistrationProviderDeferredCheckpointJournal` retains credential-refresh and
access-validation observations until the original authenticated operation owns
its Serializable transaction. Provider transport does not open that transaction
or replay itself during settlement. The selected human, tenant, command,
physical context and transaction remain part of admission.

`ISerializableAttemptOwner.ExecuteWithOriginalPrivacyAsync` retains the original
privacy identity fence through that owner's final admissions and physical
settlement. A co-located SQLite authority joins the existing transaction; it
does not create a second transaction or commit independently. A separate
authority retains its own original gate around the complete primary attempt.
This lets imported-form publication recheck subject erasure at the owning
commit. Authority setup failures prevent business execution, and failures
after acknowledged primary commit retain committed-cleanup semantics rather
than becoming rollback evidence.

External-form import also acquires the selected context's discovery-enrollment
session gate before the primary transaction begins and retains it through
settlement. Existing human-source custody precedes that acquisition. The
transaction's form-creation save therefore uses the same original connector
and enrollment lease rather than acquiring a missing gate after `BEGIN`.

Provider checkpoint and form mutations also advance Event classification
revisions. `HumanEventProviderSourceFence` captures persisted Event scalar images
for the target and the observed connections' existing form/version ancestry
before business mutation. `HumanEventProviderSourceWriteGuard` accepts a revision
successor only from native classification and ordinary concurrency preparation,
then publishes its prepared image only after `SavedChanges`. Multiple successful
saves extend that private chain. Failed or incomplete saves cannot supply
commit evidence, and newly encountered roots cannot become replacement
baselines. Shared-connection fanout advances sibling revisions without granting
permission to change their business fields.

Resetting the owning human attempt detaches its write guard and discards that
transaction's evidence. A later command in the same application scope must
acquire its own guard. Final authority comparison precedes business-image
validation, preserving authorization refusal for changed Event ownership,
routes and unproven Event revisions.

The context-installed save interceptor requires a native prepared image for
every changed Event, including mutations introduced by `SavingChanges`
callbacks. Callback registration order does not select the guard's position.
Unobserved classification carriers reject. Final admission first applies the
existing command-specific business-source transition rules and then compares
fresh persisted Event images against successful-save evidence. Preparation and
acquisition still require the original Event revision; final admission compares
the fresh revision against the proven successor while retaining the original
principal, route, support and lifetime checks. A lost commit acknowledgment
remains an unknown outcome and does not authorize transport or mutation replay.

## Authentication-provider reference visibility

`PostgresLookupCatalogueReadPermissions` owns Runtime reads of the fixed
`AuthenticationProvider` lookup columns: `Id`, `MasterCode`, `FullName` and
`Description`. EF foreign-key preflight uses that reference visibility when
persisting a provider binding. It does not authorize provider selection, account
linking or tenant enrollment, and it grants no provider configuration writes.
Those decisions remain with the original authenticated application operation.

## Tenant parent authority fence

`PostgresTenantMembershipMutationPermissions` owns Runtime `UPDATE(Tenant.Id)`
for the existing `RelationalEntityRowFence` statement
`UPDATE tenants SET id = id WHERE id = ...`. The selected application's
transaction retains the parent fence before membership insertion and disclosure
settlement. The existing API-key parent inventory retains ownership of the
`Tenant.Id` read; counting it as a new membership grant would misclassify an
otherwise valid bootstrap as a partial installation.

The API-key storage manifest recognizes the membership owner's declared read
and update columns only on intersecting relations. Its existing fresh checks
attest the exact Runtime grantee, Migrator grantor, non-grantable column rights,
relation shape and policies alongside the external-enrollment catalogue proof.
Support administration retains its existing membership-or-actor completeness
boundary and its zero Runtime scalar authority on private session and audit
relations. This preserves the supported actor-only profile without coupling
API-key storage to unrelated membership grants.
No installer grant list, required API-key permission or policy is expanded.

Discovery metadata attestation checks the enrolled Global and Runtime accounts
across both revision storage and the tenant parent relation. Its permitted
inventory also includes existing mapped Runtime membership and external
enrollment columns on intersecting relations. These are declaration-only
intersections; no composed admission predicate or additional grant is introduced.
Global tenant creation retains its original open connector, bound null tenant
and Serializable transaction. The same connector must reject subsequent
privilege drift, including an undeclared Runtime tenant-description update.

The grant permits a column update, rather than enforcing no-op SQL in the
database. A compromised Runtime credential can therefore attempt primary-key
changes; credential isolation and the application's original-owner, tenant,
transaction and concurrency checks remain necessary. No tenant-status write,
provider configuration write, or additional `User` grant is introduced.

## Native external-enrollment graph

`ExternalTenantEnrollmentInventory` declares the fixed Runtime storage needed
to insert a new user, personal actor, provider binding, active tenant membership
and verified identity-email evidence. The production database migrator and
native test fixture invoke the same
`PostgresExternalTenantEnrollmentPermissions` installer. Application handlers
still own authentication, explicit tenant consent and admission; the storage
grant does not select an enrollment execution purpose.

Installation accepts an absent or complete declared addition and refuses a
partial, inherited, table-wide, grantable, PUBLIC or foreign-grantor authority
image. Catalogue owners recognize overlapping declared columns only alongside
the complete package proof. Existing-identity mutation, private PII reads,
identity-claim deletion and role-grant insertion remain outside this addition.
The installer retains its original context, connector, backend and transaction
through savepoint rollback and settlement.

Composed external and worker catalogue proofs are evaluated through
statement-local materialized scalars. Repeated consumers share that statement's
proof, while later statements, transactions and execution owners evaluate it
again. This bounds planner duplication without retaining observed permission
results. Native witnesses cover complete graph commit and rollback, unverified
email, tenant isolation, denied authority drift and same-connector recovery.

The anonymous instance resolver GET is an exact `TenantReadBootstrap` action.
Its existing path exemption already allows routing before tenant binding and
lifecycle selection; the marker also keeps the visitor-content gate from
requiring a tenant or applying a selected tenant's private/malformed read mode.
The unchanged query returns non-sensitive instance routing configuration.
This grants no tenant-content authority and does not apply to the authenticated
resolver PATCH or other actions on `InstanceMessagingSettingsController`.

Provisioning Policy projection permissions have one installation owner:
`PostgresProvisioningPolicyPermissions`. Migration and native fixture
composition install that complete contract before Visitor Settings storage.
Visitor storage grants no projection permissions itself. This prevents its
former partial Global grant from violating the projection owner's strictly
absent-or-complete admission rule. The canonical nineteen Global/Email column
privileges, forced RLS, policy predicates and drift rejection remain unchanged;
the later migration check verifies the complete installation again.

Local administrator lifecycle guards recognize this same absent-or-complete
package through one fresh catalogue proof per guard execution. The three local
credential relations retain their blanket denial of Runtime scalar access.
Recognition grants no new privilege and does not waive original-source,
transaction, row, User, trigger or routine checks. Executable lifecycle routines
must reject privilege drift even after an earlier successful readiness check.

Instance SMTP PATCH is marked as an exact `InstanceManagement` action. Tenant
resolution, public lifecycle and visitor admission defer to that method metadata;
the marker grants no mutation authority. Class authentication and the handler's
fresh actor/instance-administrator checks remain mandatory, followed by the
selected email writer's original authority. Empty patches and direct delivery
disabling remain rejected. Unmarked messaging actions and sibling paths receive
no exemption. Six real Local HTTP cases pass across both deployment modes,
including administrator writes, ordinary/anonymous refusals and unchanged
public-content/sibling-path denials. Full native worker recovery is a separate
qualification requirement.

## Private PostgreSQL catalogue assertions

The principal-source contract stores its complete catalogue assertion in
`human_principal_source_assert_catalogue()`. Same-owner stored
entries verify its source-derived SHA-256, stamp, exact routine metadata and
owner-only execution ACL before each fresh invocation. The full original
catalogue predicate remains in the helper; target, caller and epoch fences stay
in their original entries. PostgreSQL can compile a statement trigger separately
for each target relation, so moving fixed assertions out of the trigger avoids
replicating those assertion sites in each trigger instance.

Independent C# admissions, cross-contract source assertions and installer checks
retain inline catalogue evaluation and exact-body verification. They receive no
private helper execution grant. The helper is part of the authoritative routine
inventory and ownership closure. Owner grant options are permitted; PUBLIC and
other nonowner executors are rejected. No database observation is cached.
Fresh development-native initialization installs this routine inventory; existing
drift is rejected instead of silently repaired.

Local Administration uses the same representation in
`local_administration_assert_catalogue()`. Its stored lifecycle entries and
statement triggers authenticate the helper against the source-derived body,
metadata and owner-only ACL, then evaluate the complete catalogue predicate.
Independent `RequireReadyAsync` and installer checks remain inline with exact
body verification. The routine inventory includes the void helper, and the
installer excludes it from the Runtime and temporary Migrator execution grants.
Source, privacy, index, foreign-key, policy, attempt, epoch and settlement checks
remain part of their original contracts.

## Administrative scope enumeration

`AdministrativeAuthorityRepository` owns an independent Global context for each
read. `AdministrativeAuthorityQuery` uses EF Core on every provider: exact user
and platform role for instance administration; active, nondeleted membership and
unrevoked grant for tenant administration; exact member and optional tenant for
organization/group administration. Membership and participation tenant keys
must match. Named Tenant-filter removal retains SoftDelete; projections remain
primitive and distinct. Tenant lifecycle and participation approval remain the
responsibility of their existing application decisions.

`PostgresAdministrativeAuthorityReadPermissions` installs 26 fixed SELECT
columns across seven grant/member/participation/root relations and five exact
Global SELECT policies on model-declared tenant relations. Existing
PlatformUserRole, Role and TenantUser reads are reused. It adds no database
helper role, execution grant, application write, role switch or RLS bypass.
Creator, worker, actor and physical-open readiness proofs recognize only these
source-owned descriptors; complete catalogue proofs remain independent of their
composed callers. The installer pins its original connection/transaction and
accepts only fresh or complete declarations, rejecting partial, broadened or
obsolete installations rather than repairing them.

The four former administrative decision routines and their roles are retired
on fresh development enrollment. Startup refuses their presence; no live DROP
or automatic conversion is performed. The selected Global application
credential can read the declared metadata across tenants; C# selects the exact
subject and owns human authorization. This is the existing shared-credential
trust boundary, not a human authorization claim granted by storage permissions.

Native qualification covers 25 administrative behavior cases, eight installation
drift controls and the original actual-Global repository witness. That witness
bounds only its dedicated Migrator seed commands with `MaxBatchSize(1)` after
canonical provider configuration: all seven graphs remain in one Serializable
`SaveChangesAsync`, and the Global factory, 30-second command timeout and security
guards remain unchanged. This qualifies reader semantics; it does not qualify
default-batch production write throughput. Rollback and broader workstream
assurance remain separate gates.

## Email admission state transitions

PostgreSQL email admission retains its existing transaction advisory lock and
only issues processor-state INSERT or UPDATE statements for initialization or
a real high/low watermark deferral transition. An unchanged poll must not advance
the MixedSettings source revision: its BEFORE STATEMENT trigger also runs for
conflicted inserts and zero-row updates. Processor `UpdatedAt` records a state
change rather than an idle poll heartbeat. Real transitions still update it.
The original creator User key fence, source trigger and Serializable transaction
remain intact; no retry or privilege change replaces those controls. Three native
controls pass: before/after-snapshot original creator fencing and a real
low-watermark deferral transition. The original worker recovery witness remains
pending.

## Private settings and provisioning catalogue assertions

`PostgresMixedSettingsContract` stores its complete catalogue assertion once in
`mixed_settings_assert_catalogue()`. Existing runtime guard sites first verify
that function's owner, execution flags, search path, language, private ACL and
body digest. The expected SHA-256 digest is generated from the C# declaration
and embedded in each caller; a replacement function cannot establish its own
identity by changing its body comment. The caller then executes the complete
assertion again. PostgreSQL can reuse the compiled assertion without caching an
authorization result or retaining another large assertion in every routine.

Only the existing MixedSettings owner can execute this helper. Installation
and runtime entry share the same metadata witness: the owner must retain
effective `EXECUTE`, and no nonowner may hold an execution ACL. Owner grant
options follow the canonical privilege contract rather than a stricter local
ACL representation. A null ACL is expanded to PostgreSQL defaults, so default
public execution cannot pass as a private definition. Installation
derives permanent function grants from the privilege manifest, excluding the
implicit owner grant; temporary Migrator grants for trigger creation are still
revoked. Original-session installer and Migrator readiness checks remain inline,
and exact generated-body admission stays outside runtime body generation.
Source triggers still attest before acquiring their existing lock and changing
the revision. Terminal settlement still checks only its private receipt.

Missing, replaced, restamped or broadened helper definitions fail before protected
effects. Reinstallation diagnoses drift without repairing it. Trusted schema
administration retains the existing deployment trust boundary; this refactor
adds no runtime DDL permission, role, pool policy or resource-limit exception.
