# Moderation authority and persistence

The reusable `ISLAMU.Moderation.Domain` owns qualified jurisdictions, independent
approvals, decision transitions and half-open restriction intervals. Its entities
store scalar ancestry coordinates and reconstruct checked semantic values.
`Explore.Persistence` maps those same entities; it does not keep a second
serialized interpretation of their state.

The shared scope contract distinguishes `Event = 7` from Resource and preserves
its eighth `EventId` coordinate through every scalar aggregate and optional
subject carrier. Event is an exact Tenant child, never a Resource alias; malformed
or mixed leaf ancestry is rejected. Catalogue revision 2 adds Event only to CFG29
classification correction. These definitions do not grant native authority or
prove deployment, provider schema, disclosure or completed allegation intake.

The native `ModerationEffectIntent.Create` and `ModerationOperationReceipt.Complete`
factories also retain Event's scope tag `7`, consistent with the existing database
constraints and the store's qualified scope projection. Undefined tags still
reject. Eleven Domain cases cover the seven accepted tags and four undefined
values across both factories; they are authored UNRUN/unbuilt. This carrier fix
does not add Event target qualification, consequence execution or native authority.

The portable configuration vocabulary also preserves the exact named `Event`
scope through manifest instance, manifest tenant and tenant-package round trips.
Both configuration schemas are regenerated from that vocabulary. Numeric tokens,
numeric strings, case aliases and unknown scopes remain invalid. Five new wire
cases and the extended schema contract are authored UNRUN/unbuilt; wire and schema
generator compilation is not native publication, activation or import assurance.

### Native membership caller identity

The GroupMember and OrganizationMember add-member, change-role and remove-member commands contain operation intent,
not a requester identity. Their handlers derive the authenticated, nonempty Human
identifier from `IUserContext` before accessing any repository. Direct native callers
therefore cannot select an administrator by supplying `RequesterUserId`; that field
has been removed, with no compatibility fallback. HTTP DTOs and routes are unchanged.
Existing tenant-participation, permission, duplicate and last-administrator checks
remain in place. Two stateful operation scenarios now vary the actual caller while
retaining the same commands; three additional invalid-identity cases exercise all
three real handlers before repository access. All are UNRUN/unbuilt. This identity
correction does not establish the still-open membership moderation overlay,
provider concurrency or terminal writer-fence conformance.

The three OrganizationMember handlers also retain their existing secure-request
resource authorization and exact organization/tenant participation checks. Three
additional UNRUN/unbuilt identity cases invoke those real handlers with unavailable
repositories, so a missing authenticated Human must be rejected before data access.
The separate secure-context test still checks organization and tenant ownership;
the transport no longer supplies requester authority. No positive organization
membership transition, database race or moderation fence is certified by these cases.

### Tenant-role mutation and qualification invalidation

Tenant-role grant and revoke handlers now run their existing business checks inside
`ITenantUserRoleGrantMutationBoundary`. Its native Persistence owner resolves the
authenticated Human through `IAdminContext`, rejects machine callers and foreign or
bypassed tenant contexts, and retains the selected authorization-provider ceiling
even for direct handler calls. Provider evaluation occurs outside the primary
transaction; current native tenant-administrator authority is independently checked
under source fences. A platform-administrator claim alone is not a tenant grant.

The protected unit acquires privacy serialization before the installation lock,
ordered Human identity/membership/role sources, host, service, tenant and existing
qualification epoch. Successful role changes and epoch advancement commit together;
validation failures, duplicate grants and already-revoked no-ops do not advance it.
Taking the privacy gate is not proof that a subject is eligible: the owner rejects
an already-fenced actor and rejects a fenced target before creating a new grant,
even when primary identity cleanup has not yet removed their live rows. Revoke
does not borrow an erased target's identity to create new authority.
The owner rechecks identity and tenant state after flushing and at a co-located
privacy-owned commit. It does not create missing moderation authority or epochs.
An initially authorized self-revocation is allowed to commit: its own removal is not
mistaken for a concurrent loss of the authority already acquired under fences.

An ambiguous commit is reconciled against the invocation's complete original grant
or revocation state and exact qualification revision. EF success verification,
the pre-replay check and explicit recovery after an escaping transport failure
retain the original successful result, including a committed
self-revocation, without requiring the permission that operation removed. Only an
unchanged pre-attempt state can be replayed; a conflicting successor remains an
explicit concurrency failure. This invocation-local evidence is not a durable
cross-request idempotency receipt or a grant of new authority. The explicit escape
path does not depend on provider retries; if nothing committed, it preserves the
original transport failure rather than inventing a successful change.

Twenty real-store cases are authored UNRUN/unbuilt for successful changes, no-op
invalidation, provider/native-authority rejection, caller-change rollback and
self-revocation, retained-erasure rejection, lost-acknowledgement reconciliation
and replay after an actual rollback, including non-retrying success and failure.
The rollback case names separate and actual co-located privacy
ownership and asserts the observed transaction owner; neither case has been run.
The acknowledgement faults occur after native SQLite commits with either a
fault-only retry policy or the non-retrying SQLite strategy; they are authored
business-outcome tests, not executed PostgreSQL
provider-contention or co-located-commit evidence.
The canonical sanction overlay and the complete membership-writer inventory remain
open; `native.membership` remains Partial. Compilation is recorded separately from
runtime assurance in the task receipts.

### Organization membership and material-review independence

`IOrganizationMemberMutationBoundary` now owns add, role change, removal and
recipient-bound invitation decline. The native owner retains ordinary organization
administrator checks and the selected provider ceiling for administrative writes,
resolves the actual Human, and checks exact tenant/organization participation. A
recipient may decline their own invitation without acquiring administrator authority;
another caller cannot supply the recipient identity, and the last administrator
cannot escape that protection through decline.

Read-only invitation validation likewise resolves the authenticated, non-machine
Human through `IAdminContext`; `ValidateOrganizationInvitationQuery` carries only
the invitation identifier. Its no-tracking repository read retains the tenant and
soft-delete filters instead of accepting a previously tracked foreign membership.
The invitation list resolves the same authenticated, non-machine provider-bound
Human. `GetMyInvitationsRequest` has no email or recipient field; the repository
selects exact persisted `UserId` under the existing tenant/soft-delete filters and
propagates cancellation. A matching email claim cannot identify another recipient,
and a missing email claim no longer prevents an otherwise identified caller from
listing their own invitations. Mapping remains in the handler. The native HTTP
scenario now covers a different caller claiming the recipient's email and direct
queries under changing actual principals; these assertions remain UNRUN/unbuilt.
An outsider's decline, an absent invitation and an empty identifier retain the
same unsuccessful "Invitation not found" result without mutation or epoch
advancement. Neither validation nor a failed decline creates membership authority.

Privacy serialization precedes ordered Human/login/Role sources and the shared host
and maintenance service. The exact resolved Organization and its sorted Actor rows
are then fenced before Tenant/status/qualification epoch, followed by participation
and membership anchors. This matches ordinary Organization/Pii disclosure-source
saves, rather than holding Tenant while waiting for their Organization/Actor roots.
Actor discovery disables only the named SoftDelete filter and selects IDs under the
exact Organization predicate; it does not bypass Tenant or grant another target.
Initial creation passes no existing root and retains its separate six-row commit.
Successful membership changes and advancement of an existing qualification
epoch commit together; unchanged roles do not advance it. This matters to current
classification review: administrators of an Event's publisher/organizer organization
are interested parties, including the canonical controller of a nominated reviewer.
Membership changes therefore participate in the same native serialization protocol
as material acceptance, rather than merely changing an earlier authorization result.
No missing host/epoch is fabricated and no policy is activated by this boundary.

Current identity and tenant state are rechecked after flushing and at an actual
co-located privacy commit. An authorized self-demotion/removal uses its initially
fenced permission; it cannot authorize another operation. Lost acknowledgements use
the invocation's exact persisted before/after membership, audit state and epoch to
recover only the original result, including self-demotion. This is not durable
cross-request idempotency or permission to replay a conflicting successor.

Fifteen real-store membership cases and two connected material-review cases are authored
UNRUN/unbuilt. They cover transitions/no-ops, provider and ordinary-member rejection,
invitation creation/recipient decline, separate/co-located caller-change rollback,
retained-erasure rejection, self-demotion acknowledgement faults with retrying and
non-retrying strategies, and preservation of an actual non-retrying rollback failure.
The two additional normal/co-located role-change cases witness Organization and Actor
anchors before the first Tenant anchor. All remain UNRUN/unbuilt, not provider-cycle
proof. This source-order correction compiled in Persistence with 62 unsuppressed
warnings and no errors (2:24.18); focused read-only review found no attributable
material defect. Scoped diagnostics remained incomplete, not a clean result or MAD.
The material cases establish the organization Actor and independent controller
before capturing material, then use the real promotion handler and the updated
epoch before attempting acceptance. They are not executed writer/reader races or
multi-provider evidence. Source compilation/review outcomes are recorded separately;
the canonical sanction overlay and complete membership writer inventory remain open,
and `native.membership` remains Partial.

The existing native HTTP invitation scenario now changes the actual server principal
while retaining the same query. A new unauthenticated concrete-query case and the
existing three missing-identity command cases (extended to decline) are authored
UNRUN/unbuilt. These do not establish hosted HTTP or provider-runtime assurance.

### Initial Organization administration and approval commit

`CreateOrganizationCommand` no longer accepts `CreatorUserId`. The HTTP controller
keeps its existing identity check; the concrete handler obtains its actual
provider-qualified Human through `IOrganizationCreationMutationBoundary`, implemented
by the same scoped `OrganizationMemberMutationBoundary` as ordinary membership.
The selected provider retains the Organization **pre-create** contract, with the
actual Tenant and Human, before any primary transaction or creation write.

Privacy enrollment, creator/login/grant and Role sources, Host/maintenance,
Tenant/status and any existing qualification epoch remain fenced through actual
commit. The owner derives Tenant-admin approval under those sources and rechecks it
after disclosure flush and at the actual co-located privacy commit. Existing behavior
is preserved: an ordinary creator gets Pending, invisible, non-organizer participation;
a current Tenant administrator gets Approved, visible, organizer-eligible participation
with the actual approver recorded. Retained erasure, a changed creator or lost approval
authority rolls back the entire creation rather than leaving its initial OrgAdmin.

Stable invocation IDs bind six persisted rows: Organization and its PII, Organization
Actor and its PII, tenant participation and initial membership. They and an already
installed epoch advance share one native Serializable owner; the handler no longer
opens a second transaction. No Host or epoch is installed for ordinary creation.
Metrics and cache invalidation run only after confirmed success. Commit-acknowledgement
recovery compares the complete database-normalized scalar graph and epoch; it returns
the original result only for that invocation's exact committed graph. Pristine rollback
and successor/partial-state conflict remain distinct, without a durable cross-request
receipt or permission to recreate erased identity.

Sixteen real-store cases are authored UNRUN/unbuilt. Fourteen were authored before the
implementation; two additional normal/co-located cases revoke the creator's actual
Tenant-admin grant at the final save and require graph, grant and epoch rollback.
The old internal-store creation scenario was replaced only after its real-store
business counterparts existed. Application/Persistence/API product compilation passed
with 182/62/2 unsuppressed warnings and zero errors (34.52s / 2:27.84 / 11.04s).
Focused source review found no concrete material defect in the scoped owner and its
ordinary-membership helper extraction. Neither compilation nor that review is MAD,
runtime or provider-conformance proof. This scope does not certify Organization contact-data collection or age
admission, managed provisioning, canonical sanctions, provider contention or deployment
readiness. `native.membership` remains Partial.

### Initial Group administration and creation commit

Native Group creation no longer accepts `CreateGroupCommand.CreatorUserId`. The
HTTP controller retains its identity check, but the concrete native handler now
delegates initial administration to `IGroupCreationMutationBoundary`, implemented
by the same scoped `GroupMemberMutationBoundary` as ordinary membership changes.
The owner resolves the actual provider-qualified Human, checks the selected Group
creation ceiling outside the primary transaction, and holds privacy enrollment and
the transaction-owned tenant hierarchy namespace before installation and the ordered
native creator/role/host/tenant sources through commit.
Retained erasure or changed creator identity rejects the whole creation.

Group, Group Actor/Pii, Pending participation and its initial GroupAdmin membership
share stable invocation identifiers and one native Serializable commit. An already
installed qualification epoch advances with successful creation; an absent host does
not cause moderation installation. Pending approval, hierarchy validation, image
eligibility and lack of organizer eligibility retain their existing meaning. The
hierarchy repository borrows an existing Serializable transaction without flushing,
committing or disposing it; standalone Group update retains its previous transaction
owner. Creation metrics and cache invalidation occur only after confirmed success.
Taking the hierarchy namespace before those source anchors is essential: ordinary
updates already hold it when their native disclosure save reaches Tenant. Creation
must not hold Tenant while waiting for that update's hierarchy lock. The handler's
later hierarchy acquisition is reentrant on the same original transaction.

Normal post-disclosure-flush and actual co-located outer privacy commit callbacks
recheck the native creator and expected epoch. Invocation-local commit reconciliation
compares all persisted scalar values of the five created rows and the epoch, returning
the original result only for the exact committed graph. A pristine rollback may be
replayed; partial or successor state is a conflict. This is not a durable cross-request
idempotency receipt or permission to recreate erased identity.

Fifteen real-store cases are authored UNRUN/unbuilt, including retained erasure,
normal/co-located caller change, lost commit acknowledgement, absent-host behavior
and caller-owned hierarchy rollback. Two additional normal/co-located creation cases
observe the actual held hierarchy namespace at the first native Tenant anchor; they
are not executed retaining-provider contention evidence. These replace the old internal-store Group
creation scenario rather than adding an internal boundary mock. Application compilation
passed with 182 warnings and no errors. The first Persistence build exposed one incorrect
provider-Tenant method invocation, also identified by focused source review; the code
now uses the existing native instance check, without suppression. That correction
compiled Persistence/API with 62/2 warnings and no errors (2:24.73/10.99s). The later
hierarchy-order correction compiled Persistence with 62 warnings and no errors
(2:26.11). Focused source-review follow-up found no additional attributable defect;
neither compilation nor that review is MAD or runtime proof. Managed provisioning,
the canonical sanction overlay and provider contention remain separate open work;
`native.membership` remains Partial.

### Group membership and material-review independence

`GroupMemberMutationBoundary` now owns the ordinary add, role-change and removal
transactions. The three handlers retain their manual validation, exact participation,
duplicate, position and sole-administrator checks. The boundary independently resolves
the current provider-linked Human, applies the selected provider ceiling outside the
primary transaction, then holds retained-identity and native source fences through
the actual commit. A successful changed membership advances an existing qualification
epoch atomically; an unchanged role does not. It neither creates moderation adoption
nor turns organization administration into group authority.

Local single checks and batches use current tenant/group/action authority: tenant
administration, group administration or the corresponding active persisted role
permission. Cerbos receives action-specific `tenantId/groupId` scopes from filtered
native repositories. Bound Group-owned keys and User-owned group-administrator keys
receive scopes only for their explicit membership tenant; the machine scope ceiling
still applies. Authenticated view remains unchanged, but authentication alone no
longer grants membership creation. These projections are not execution evidence for
the bundled Cerbos policy.

The ordinary mutation owner fences current Humans, role/permission sources and host,
then the exact Group and its Actor rows before Tenant/status and qualification epoch.
This matches ordinary Group/Actor disclosure source saves rather than waiting on a
Group or Actor while already holding Tenant. Actor discovery retains the exact Group
predicate and disables only SoftDelete, so historical Actor rows cannot introduce a
new root behind Tenant. Participation and membership anchors follow. Initial creation
still takes hierarchy ownership first and has no pre-existing Group root to acquire.
Both normal
post-flush and actual co-located privacy commit callbacks recheck identity and the
expected epoch. Self-demotion/removal uses only the original fenced permission.
Invocation-local reconciliation compares the complete persisted membership and epoch
after ambiguous commit acknowledgement; it is not cross-request idempotency.

Fifteen real-store membership cases and two connected material-reviewer/controller
promotion cases are authored UNRUN/unbuilt. The latter establish the group Actor
before capture and attempt acceptance with the updated epoch, rather than merely
testing a stale-revision rejection. Twelve Local/principal-projection cases and three
Cerbos policy cases are also authored but unrun. Product compilation is recorded
separately; none of these source receipts proves provider contention or runtime parity.
The two latest membership cases witness actual normal/co-located Group and Actor
anchors at the first Tenant write, without executing a provider contention test.
The source-order correction compiled in `Explore.Persistence` with 62 warnings and
no errors (2:31.12). Focused source review found no additional attributable defect;
scoped diagnostics remained incomplete because Roslyn was unavailable.
The canonical sanction overlay and runtime assurance remain required work. `native.membership`
therefore remains Partial; no deployment or policy activation is implied.

`RoleRepository` now coordinates assignment, replacement and removal of role-permission
joins with that same Role lookup write fence before reading the set, including first
assignment when no join row exists. It then acquires sorted Permission lookup fences.
An existing primary transaction retains commit/rollback ownership; otherwise the
repository uses its existing serializable unit. Caller-owned transactions must also be
serializable: a lookup write fence does not refresh an older MySQL repeatable-read
snapshot. Repository-owned replay rejects pending caller changes instead of risking
their loss when a failed attempt clears tracking. Replacement applies only the set delta:
retained grants keep their original `GrantedAt`/`GrantedBy`, duplicate identifiers do
not create duplicate joins, and unknown permissions cannot partially replace a set.
Role usage checks now include live group memberships under the existing tenant filters.
These permission-set operations do not themselves delete a Role or add caller
authorization, a sanction overlay, or moderation adoption. Eight real-store invariant
cases are authored UNRUN/unbuilt; provider contention remains deferred. The group
boundary also fences each discovered Permission key separately and accepts an absent
optional permission lookup when current group-administrator authority independently
permits the action; its additional real-store case remains UNRUN/unbuilt.

### Atomic global custom-role deletion

`DeleteCustomRoleCommandHandler` no longer commits permission removal before deleting
the Role. It calls `IRoleRepository.DeleteUnusedCustomRoleAsync`, which returns the
actual deleted entity or null. The handler retains its system-role checks and runs
policy synchronization and permission-cache invalidation only after successful deletion.
This is transaction ownership, not a new caller-authorization grant.

`RoleRepository` takes the same Role lookup write anchor used by cooperating permission
and membership writers, rereads the current non-system definition, and holds sorted
Permission anchors through deletion. Because the Role definition is global, exact
RoleId existence checks cover PlatformUserRole, OrganizationMember, GroupMember,
TenantUserRoleGrant, EventRoleAssignment and TenantInvitation across tenants. Only the
named Tenant and SoftDelete filters are disabled on those bounded existence queries;
the ambient tenant is not bypassed and no foreign rows are returned. Deleted memberships,
revoked grants, past Event assignments and expired invitations still veto physical
deletion, preserving their historical Role references.

One Role DELETE lets the existing foreign key cascade remove only its permission joins
in the same Serializable transaction. No earlier permission-removal commit can survive
a failed Role delete. A borrowed Serializable transaction remains caller-owned; weaker
isolation or pending caller changes are rejected. Only the deleted Role's saved tracked
entities are detached, without clearing unrelated caller work.

Fifteen real-store source cases are authored UNRUN/unbuilt: nine cross-tenant/current-or-
retained reference variants, unused-only graph removal, a transport failure after the
actual delete, explicit caller rollback, a changed system flag, pending caller work and
weak isolation. Focused read-only source review identified no further concrete material
finding; that is not runtime or provider-contention proof. Final product compilation
passed: Application 182 warnings / 0 errors (39.06s), Persistence 62 / 0 (5:54.91),
and the incremental API build 0 / 0 (1.19s). Existing warnings are not claimed fixed.
Expected system-role/reference vetoes use `BadRequestException`; the handler maps only
those business failures, leaving transaction/programming failures to propagate.
Native Role-administration caller authorization, the
canonical sanction overlay and programme assurance remain open; `native.membership`
is still Partial. No schema or HTTP contract change or policy activation is implied.

### Primary migration artifacts and the dependency security repair

The accumulated moderation/profile model now has native EF-generated migration source
for PostgreSQL, SQLite, SQL Server and the shared MySQL/MariaDB assembly. The first
SQLite scaffolding attempt through `eng/tools/ScaffoldPrimaryMigration.cs` failed
before EF execution: its restore reports NU1902 and NU1903 for
`System.Security.Cryptography.Pkcs` 9.0.4. This is the previously quarantined dependency
graph. The Project Steward subsequently approved a narrowly scoped security repair.
Pkcs/Xml now resolve to 10.0.13 at their actual product owners. Downstream restores
also exposed existing WebAssembly Server, Redis Data Protection, MessagePack and
SignalR advisories; their compatible patched families are recorded in the
[dependency decision](legal/dependencies/dotnet-october-2026-security-patches.md).
Audit settings and warning severity remain unchanged; no SDK/runtime upgrade or
global transitive pinning was introduced. Affected dependency restores and final
AppHost/API-test locked-mode checks passed without vulnerability warnings. Test
dependency restoration did not build or execute tests.

The eight affected product builds passed with zero errors (Secrets 12 warnings,
Persistence 62, Infrastructure 45, API 2, Blazor Client 230, Blazor server 13,
Standalone 0 and AppHost 6). The dependency-license metadata check passed for 531
unique package/version pairs with its existing exceptions still visible; it is not
assembled-distribution legal certification. The native scaffolder generated
`AddNativeModerationSourceOwners` in PostgreSQL (`20261019145245`), SQLite
(`20261019145153`), SQL Server (`20261019145818`) and MySQL (`20261019145843`), each
with its designer and owning snapshot. MariaDB uses the MySQL assembly. All four
owning projects compiled with zero errors and zero warnings in the final sliced build;
this does not claim earlier warnings were fixed. No database application or provider
assurance ran. Inspected `Up` methods retain the separate protected-contestation purpose
and six-calendar-month constitution constraint, contain no table/column/FK drops or
raw data SQL, and relax four existing case-subject columns for unattributed cases.

Source review found SQL Server's broad portability normalization had changed the
current DOB receipt's explicit `UserPii` cascade to `NoAction`. That would block PII
deletion rather than remove its current provenance. The model policy now preserves
only the exact isolated `UserBirthDateCollectionReceipt.UserId -> UserPii.UserId`
cascade; its other SQL Server cascade safeguards remain unchanged. Five actual-provider
model cases are authored UNRUN/unbuilt, including a check that the unrelated
`UserPii -> User` SQL Server edge still uses `NoAction`. Native SQL Server correction
`20261019151338_PreservePrivateBirthDatePiiCascade` generated and compiled with zero
warnings/errors (18.67s); the original migration and all prior history remain intact.
Its `Up` restores the required receipt cascade and also reasserts `NoAction` for the
case-evidence FK, which already had that physical action in the initial migration.
Its generated `Down` instead assigns Cascade to that evidence FK. This does not match
the actual predecessor and remains an unresolved model/snapshot-generation discrepancy:
do not apply that downgrade or treat it as a verified reversal. No generated file was
hand-edited, and neither runtime cascade nor complete migration correctness is claimed.

Generated `Down`
removes the new tables/columns and restores stricter case nullability: it is not a
lossless data rollback and must not be applied as one. Runtime migration/rollback
assurance remains deferred with the programme's other checks.
The logical schema reference now
includes private DOB revision/current-receipt ownership and its PII-delete cascade;
that reference is not generated-model parity or an executable migration.

### Event-team mutation actor binding

Assignment, assignment by email, revocation, validity-window changes and ownership
transfer no longer accept an `ActorUserId` in their native commands.
`EventRoleAssignmentCommandHandlerBase.RequireActorAsync` resolves the existing
provider-qualified Human through `IAdminContext`, rejects machine, missing-identity
and foreign-tenant calls, and asks the selected authorization provider before any
target lookup or denial audit. Ordinary mutations require `ManageTeam`; transfer
requires `TransferOwnership`, including when invoked through the native decorator.
The email alias checks before looking up the recipient and the assignment handler
independently checks its forwarded intent.

The resolved Human supplies assignment/revocation provenance, structured audit actor
IDs and native ceiling checks. Existing owner-role restrictions, duplicate checks,
validity rules and replacement-before-revocation transfer ordering remain intact.
HTTP routes and DTO shapes are unchanged. Four real-store denial cases cover all
five concrete handlers, and one transfer-metadata case is authored; all five are
UNRUN/unbuilt. Application compilation passed with 182 warnings / 0 errors (37.62s)
and API compilation with 2 warnings / 0 errors (12.07s), using existing dependency
outputs. Scoped diagnostics were unavailable, not clean. This is actor binding,
not commit-held source fencing, qualification invalidation, positive assignment
conformance, initial-owner alias coverage or the canonical sanction overlay.
`native.membership` remains Partial.

### Event authority role-source planning

`EventAuthoritySnapshotService.GetCommitBoundForUserAndEventsAsync` now plans both
Actor/Event ownership and the contributing assignment identity/role set before
taking source fences. It acquires sorted Role, Permission and RolePermission
anchors before Actor, Event and assignment rows, rather than opposing a
Role-before-Actor writer. SQL Server and InnoDB use the existing detached
ReadUncommitted planning technique for both sets: dirty planning coordinates are
never authority, and do not retain early shared Event or grant locks.

Content notice custody acquires one batch for all explicitly approved representative
Humans. The batch fences the union of their Role/Permission/join sources before
any Actor/Event fence; separate per-Human acquisitions would reintroduce the same
cycle when a second representative needs a different Role. The public single-Human
contract delegates to that same owner.

After taking the held ownership fences, the reader compares the complete current
assignment identity/Human/Event/Role set with its plan, then rechecks it after assignment fences. A
first assignment, removed source or changed role invalidates the whole attempt;
it never acquires an unplanned Role behind Event. Status and time are still
evaluated by the ordinary fresh snapshot, so revocation of an unchanged source
does not restore its permission. Existing exact tenant predicates and caller-owned
transaction lifetime remain unchanged.

Three real-store source-expansion/rollback cases and two retaining-read-provider
writer/planner races (including distinct representatives/roles) are authored and
UNRUN/unbuilt. Focused source review identified the cross-representative cycle;
follow-up source review found no further concrete material finding in the batch
correction. Scoped Roslyn diagnostics are unavailable/timed out, not clean. The
initial Persistence compile passed with 62 warnings / 0 errors (5:54.52), and the
later batch correction passed separately with 62 warnings / 0 errors (7:48.85).
This removes one source-order inversion, not the remaining full
Human/Host/Epoch ordering, event-role mutation transaction, qualification
invalidation, initial-owner aliases, or sanction-overlay obligations. No consumer
is promoted to Supported from this source increment.

### Fresh native event delegation metadata

`EventRoleAuthorityCeilingService` now reads active/filtered permission metadata
through the existing native `IPermissionRepository`, not the ten-minute permission
display registry. A newly filtered permission cannot remain delegable because a
display cache still contains its old flags; removing a filter or activating a
permission is likewise evaluated from the current native rows. Existing same-event
team-management authority, target Event role scope, first-release role whitelist,
subset checks and the five explicitly nondelegable permissions are unchanged.
The display registry remains cached for its existing consumers.

Eight real-store ceiling cases replace the former internal-mock fixture, retaining
its subordinate/owner/approval/reviewer/management invariants and adding warm-display-
cache filtering/unfiltering/activation changes. All eight are UNRUN/unbuilt; no
test-project build was performed. Application compilation passed with 182 warnings /
0 errors (41.83s), and API compilation with 2 warnings / 0 errors (17.16s), using
existing dependency outputs. Diagnostics timed out, not clean. These are fresh
query semantics, not commit-held permission mutation/assignment atomicity or
cross-provider race proof; the remaining Event-team transaction and overlay gates
are unchanged.

### Commit-owned event ownership transfer

`EventRoleAssignmentMutationBoundary`, through its ownership-transfer port, encloses the replacement owner assignment,
original owner revocation, stable success audit and existing qualification-epoch
advance in one privacy-serialized primary transaction. The handler no longer starts
a nested transaction. It requires the replacement to be effective before revoking
the original owner, and emits allowed telemetry only after the boundary confirms
the result. Exceptions unwind the transaction before the handler translates a
business failure or records its separate denial audit.

Selected-provider authorization remains outside the primary transaction. The native
owner qualifies the actual Human and retained-erasure state, plans old/new owner and
actor assignment sources without treating discovery as authority, and holds ordered
Human/login/grant and Role/Permission/join fences, then Host/maintenance, planned
Actors, Tenant/epoch, Event and assignment fences. Actor precedes Tenant to cooperate
with native Actor/ActorPii source saves. Unplanned earlier sources invalidate the attempt. Installed hosts
require an existing matching epoch; this path never installs moderation implicitly.
Normal post-flush and actual co-located privacy commit callbacks recheck identity,
tenant/provider binding, retained erasure, exact owned state and the original
permission interval. Owned revocation cannot erase that original clock boundary;
the replacement must also remain effective at the final check.

Invocation-local before/after assignment, audit and epoch facts reconcile an uncertain
commit. Exact committed facts recover the original result even after self-revocation;
pristine rolled-back facts permit a whole-unit retry; successor or partial facts
conflict. This is not durable cross-request idempotency. Focused source review found
that an inner bootstrap retry could retain a rolled-back success receipt. The native
unit now exposes a single-attempt enrollment path for this receipt-owning caller,
so the outer privacy/primary owner reconciles before replay rather than retrying an
already-mutated callback in isolation.

Fifteen real-store source cases cover new/pending replacement, ineffective replacement
rollback, original/replacement deadline checks, retained erasure, separate/co-located
commit ownership, lost acknowledgements, actual rollback and a retryable pre-commit
conflict. They are all UNRUN/unbuilt. The initial Application/API compiles passed
(182/2 warnings, 0 errors; 54.92s/14.40s). The initial Persistence compile passed with
65 warnings; three new nullable-flow warnings were corrected by declaring the
always-throwing denial helper non-returning, not by suppression. Final Persistence
compilation, including the retry fix, passed with 62 warnings / 0 errors (5:51.96).
Focused retry follow-up found no further concrete bug; this is source analysis, not
runtime or MAD evidence. Diagnostics were partial because Roslyn was unavailable.
Assignment, email, revocation, initial-owner issuers, the canonical
sanction overlay and real provider/concurrency conformance remain open. This source
increment does not promote `native.membership` beyond Partial.

### Commit-owned event assignment windows

The same scoped `EventRoleAssignmentMutationBoundary` now implements the dedicated
`IEventRoleAssignmentWindowMutationBoundary` port. The window handler retains its
current same-event delegation ceiling, role restrictions and domain window validation,
but assignment version/window, stable success audit and existing qualification epoch
now commit together. Selected-provider checks remain outside the primary transaction;
native Human, privacy, source, tenant and original management-permission checks remain
inside the ordered commit owner. This does not copy the transfer transaction engine or
nest another transaction around it.

A Pending assignment or an Active assignment with a future window is legitimate:
unlike a replacement owner, the edited target need not be effective immediately.
The actor's original effective permission coverage is retained separately for management
and every active permission required by the target role. An indefinite management
grant cannot mask the expiry of another necessary grant. Extending one's own assignment
cannot renew those original intervals while a write is in flight. Normal
post-flush and actual co-located privacy commit checks re-read the exact owned state
and the original clock boundary. Allowed telemetry is emitted only after confirmation.

Invocation-local reconciliation includes the target's complete assignment set, stable
audit and exact epoch. A successful mutation advances the epoch; an unchanged denied
mutation with its owned denial audit does not. A lost acknowledgement can recover that
original failure as well as a success, without appending another denial audit or
pretending a denial changed authority. This is not durable cross-request idempotency;
denied telemetry can still be repeated on a genuine whole-attempt replay.

Twelve new native-store window cases cover Pending/Active future windows, original
management and full-ceiling permission expiry at normal/co-located final save,
successful lost acknowledgements and denied lost acknowledgements. One additional
retaining-provider case coordinates a real ActorPii source save with the window owner
to guard Actor-before-Tenant ordering. All thirteen are UNRUN/unbuilt. Focused source
review found the incomplete original ceiling and Actor/Tenant inversion; both are
corrected, with no further concrete finding on follow-up. Initial Application,
Persistence and API compilation passed (182/62/2 warnings, 0 errors;
35.25s/5:54.42/12.23s); final Persistence compilation of those review corrections
passed with 62 warnings and no errors (6:01.66). Diagnostics are partial because
Roslyn is unavailable. This remains source analysis, not runtime/MAD or
provider-concurrency proof. Assignment, email, initial-owner aliases, canonical
sanction overlay and real provider/concurrency gates remain open. Support stays Partial.

### Commit-owned event assignment revocation

`IEventRoleAssignmentRevocationMutationBoundary` joins the same scoped
`EventRoleAssignmentMutationBoundary`, rather than introducing a second retry or
transaction engine. The real revocation handler retains the same-event delegation
ceiling, domain terminal-state rules and prohibition on direct Owner removal. A
successful removal saves assignment status/version, its stable audit and the existing
qualification-epoch advance in one privacy/native primary transaction. A denied
Owner removal can reconcile its stable audit without changing ownership or the epoch.
Both allowed and denied outcome telemetry follow confirmed completion. A rolled-back
denial attempt does not publish an additional committed denial. This remains an
invocation-local guarantee, not exactly-once telemetry across separate requests.

The actor must remain a current qualified Human with no retained erasure fence. An
erasure-fenced target does not veto privilege removal: this narrow exception cannot
authorize a transfer, window change or new assignment. Self-revocation retains the
original management and complete delegation-permission clock through normal post-flush
and actual co-located commit checks; removing a grant cannot erase its expiry. Source
ordering remains Human/login/grant, Role/Permission/join, Host/maintenance, Actor,
Tenant/epoch, Event/assignment. The unchanged selected-provider checks occur outside
the primary unit. Exact assignment/audit/epoch facts reconcile original success or
failure before replay, including lost acknowledgement after self-revocation; they
are not reusable cross-request receipts.

Sixteen native-store revocation cases are authored UNRUN/unbuilt: Pending/Active
removal, self-revocation lost acknowledgements, Owner-denial lost acknowledgements,
actor-versus-target erasure fences, and original management/full-ceiling expiry at
normal or co-located final save, plus a real denial-audit rollback/retry observed at
the external log boundary, and target removal between a failed audit save and retry.
Captured denial telemetry is emitted only when its reason matches the final result;
an early not-found retry cannot publish stale rolled-back denial data.
Required native constructor consumers and DI ports
are migrated without an optional compatibility constructor or internal boundary mock.
Focused source review identified the pre-commit denial telemetry; emission now waits
for the final owner result, with captured denial state reset on each real replay.
Initial Application/Persistence/API compilation passed (182/62/2 warnings, no errors;
35.69s/5:44.87/13.06s), covering the native revocation owner but predating the final
Application-only telemetry corrections. Final Application/API compilation passed
(182/2 warnings, no errors; 35.93s/12.82s);
Roslyn diagnostics are unavailable. Other assignment/email writers, initial-owner
issuers, the canonical sanction overlay and real provider/concurrency assurance remain
open. `native.membership` remains Partial; no policy or deployment is activated.

### Commit-owned event assignment creation and email identity

`IEventRoleAssignmentCreationMutationBoundary` joins the same scoped event-role owner
as transfer, window changes and revocation. The assignment handler allocates its
assignment and audit UUIDs before retries, then performs the existing same-event
ceiling, duplicate and Owner-transfer checks inside the qualified primary unit.
Assignment creation accepts only Pending or Active, including future starts; an
undefined enum value cannot become a new persisted grant. The successful assignment,
stable success audit and existing qualification-epoch advance share one actual commit.
An owned denial audit changes neither assignments nor the epoch. Outcome telemetry
waits for the confirmed result; no external provider call occurs inside the primary unit.

The email handler keeps its initial authorization and exact database email lookup,
then invokes the concrete assignment producer with the originally selected Human and
email rather than dispatching another decorated command from inside the owner.
The native owner fences that Human's `UserPii` row after the ordered Human anchors,
before later sources, and rechecks the exact database email predicate before the
producer and at normal and actual co-located commit. Ambiguous email ownership,
removal or reassignment cannot silently grant the old or a newly resolved Human.
Email is not added to logs or audits.

Source planning includes the requested Role even when the target has no assignment.
Existing selected assignments must remain identical apart from the one proposed new
row. Original per-permission coverage is captured before the write: a self-grant
cannot renew the authority that allowed it. Exact invocation-local assignment,
audit and epoch facts reconcile an uncertain success or denial before replay. These
facts are not durable cross-request idempotency or a new moderation permission.

Twenty-two native-store source cases and one retaining-provider Actor-save case are UNRUN/unbuilt: both aliases, Pending/future
creation, duplicates and Owner denials, invalid status, lost success/denial
acknowledgements, retained actor/target erasure, original self-grant permission expiry,
and email removal/reassignment at normal or actual co-located final save. Product
compilation passed for Domain, Application, Persistence and API (146/182/62/2
unsuppressed warnings respectively, zero errors). Focused source review found no further concrete material defect;
it is not runtime or MAD evidence. The graph is stale
and Roslyn diagnostics are unavailable, not clean. Initial-owner issuers, sanction
overlay and real provider/concurrency assurance remain open; membership support stays Partial.

AI proposal confirmation is also an Event-creation caller. Its proposal read now
uses `AsNoTrackingWithIdentityResolution`: the included conversation/message graph
remains coherent, but pending confirmation or rejection does not become an ambient
EF write. The existing `UpdateProposedActionAsync` remains the explicit persistence
owner after the tool result. A nested tool save cannot prematurely persist that
decision, and a native Event retry need not discard it from the caller's tracker.
Tenant filters are unchanged. Six native source cases cover executed, failed and
rejected decisions with committed or rolled-back explicit updates, including a
tool save before the update. They remain UNRUN/unbuilt. Persistence compilation
passed with 62 warnings and no errors in 2:34.17; bounded source review found no
attributable material regression, not runtime or provider proof. Roslyn diagnostics
remain unavailable. This caller correction is not the initial Event-owner commit boundary
and does not add AI concurrency or cross-request idempotency guarantees.

Initial native Event creation is now being composed through a separate
`IEventInitialCreationMutationBoundary` on the existing event-team owner. It wraps
the complete parent/participation/initial-owner and optional publication graph,
not an ordinary assignment command. Invocation IDs and original publisher facts
are fixed before retry; each proven rolled-back attempt rebuilds a fresh root.
The actual publisher resolver runs under coordinated submission/visitor settings,
native creator privacy and ordered membership/Actor/Tenant sources, then again at
the actual primary commit boundary. Community reports retain their ownerless
shape; imported creation is unchanged. Installed hosts require their qualification
epoch, while no-host creation retains its separate supported path. The epoch
advance belongs to the same primary transaction.

Thirty-one new native boundary cases, eight native receipt cases and eight
settings cases are authored UNRUN/unbuilt. The Event-local receipt preserves
physical before-images across accepted saves, terminal normalized after-images,
known bulk storage/discovery changes and the complete supported child-key sets.
An owner row alone never establishes successful creation. Exact complete-after
evidence returns the original result; exact complete-before evidence permits a
fresh attempt. Mixed or unsupported evidence fails closed instead of replaying.
New physical-location and federation-planner writes currently remain unsupported
for uncertain-commit reconciliation; normal first commit is not a provider proof.
Saved-source capture also precedes a failed response, so a bulk-only mutation
cannot be committed as an effect-free failure.

Publisher submission and approval keys use authoritative system/tenant reads,
not a replica's stale settings cache, while their coordinated leases are held.
The community path does not attempt an empty approval-status fence. Original image
owners are included in the ordered Actor plan before Tenant; locked image ownership
is rechecked before the callback and at commit. An ownership change cannot nominate
an unheld Actor late in creation. Application compilation passed with 182 warnings /
0 errors in 39.86s; Infrastructure passed with 45 warnings / 0 errors in 30.45s.
Initial Persistence compilation reported 62 warnings / 5 missing-namespace errors
in 3:00.22; the authorization-contract import is corrected. Final Persistence
compilation passed with 62 warnings / 0 errors in 2:42.72. Bounded integrated and
image-order source reviews found no further attributable material defect after
their corrections; Roslyn diagnostics are unavailable. No runtime,
provider matrix, policy activation or completed initial-owner support is asserted.

### Current Human publication overlay — source integration

The approved publication policy is opt-in. Positively qualified absence of an
applicable Human `Publish` adoption leaves ordinary authorization, lifecycle
approval and readiness unchanged. Once adopted, both ordinary publish and
privileged approve must reject denied or unavailable moderation authority. An
exception, malformed selection or unreadable history is not proof of absence;
retiring configuration must not discard a still-effective contribution.

`EventPublicationExecutor` now delegates its native transaction to
`IEventPublicationMutationBoundary`, retaining its existing setting-lock override,
stable outbox identities, lifecycle checks and post-commit cache invalidation.
The subject is the current native Human, not the publisher Actor or Event owner;
the target is the actual Tenant, including containing Instance restrictions. This
does not invent an Event-scoped profile or a Resource identity for an Event.

The owning boundary acquires shared Human/login/role sources and original Event
Actor ancestry before Tenant authority, then the real policy jurisdiction slots
including first adoption. It reads publication facts before invoking the existing
publication work and retains a fresh restriction/time check through the actual
Event/outbox commit. The same check is passed to both native unit-of-work and
privacy commit owners, because co-located identity storage owns the final commit.
Unrestricted moderation remains a restriction result, never an ordinary permission.
The standalone moderation store's clean-context and transaction ownership rules
are not weakened to join the Event transaction.

A narrow `ReadPublicationInTransactionAsync` continuity read fixes the capability
to Publish and accepts only the current Tenant target; existing resource reads
retain their own capability and batch constraints. Thirty-one snapshot, twenty
continuity and twenty-six publication/rollback cases, plus one pending-caller-state
case, are authored UNRUN/unbuilt. The outbox rollback witness directly stages real
PDS rows; it does not claim a positive federation planner run. Application compiled
with 182 warnings and no errors in 40.60s. Bounded boundary, continuity and executor
fixture and snapshot reviews found no attributable material defect. Persistence
compilation passed with 62 warnings and no errors in 3:09.62; no product source
changed after that compilation began. Roslyn diagnostics are unavailable.
That source increment did not establish create-with-publish integration. It does
not establish provider/runtime support, session publication, machine/Actor
overlays, or completed programme support.

Create-with-publish now carries the normalized, required original Draft/Published
status into `IEventInitialCreationMutationBoundary`. The persisted parent must
match that status: a Draft callback cannot smuggle a Published Event past the
overlay. Only Published requests read the actual creator Human's Tenant Publish
facts, after native publisher/source acquisition and both real policy jurisdiction
namespace fences. Qualified nonadoption preserves the handler's existing approval,
readiness and publisher checks; denied or unavailable authority returns before
the callback, epoch advance or success-receipt initialization.

Successful creation advances its own qualification revision. The snapshot now
retains epoch identity and revision as explicit record-equality coordinates;
the remaining generation still includes epoch identity and every other original
authority fact. Initial creation may substitute only its exact acquired revision
plus one at terminal comparison. Ordinary publication still compares the complete
snapshot, including the unchanged revision. Neither path replaces the original
deadline with newly permitted authority. Initial creation checks the original
window again after awaited snapshot and write-receipt work, at both actual commit
owners, so expiry during I/O rolls back the whole transaction.

Whole-write Before/After/Uncertain reconciliation still precedes fresh authorization:
an exact committed After returns its retained result without renewing permission;
only proven Before permits a fresh attempt. Location and federation-planner rows
outside the receipt's supported set remain fail-closed uncertain, not evidence
of complete rich-create replay coverage. No blanket receipt allowlist was added.
Sixteen creation publication cases, eleven epoch snapshot cases, one original
status-smuggling case and two snapshot-I/O expiry cases are authored UNRUN/unbuilt.
Source review corrected a terminal time gap and two fixture mistakes: the native
Publish API takes UTC `DateTime`, and other-Human isolation uses a reachable
organization publisher rather than replacing a personal Actor's Human. Application
compilation passed with 182 warnings and no errors in 38.85s. Persistence compilation
passed with 62 warnings and no errors in 2:36.03. Focused follow-up review found no
additional attributable causal defect after the corrections; Roslyn is unavailable. No provider,
runtime, retained-recognition or positive federation-planner assurance is claimed.

Session publication now routes its entire mutation through the existing
`IEventPublicationMutationBoundary`, not a standalone moderation precheck. The
handler freezes the original session parent and tenant before acquisition, then
uses `GetByIdForEventForUpdateAsync` under that boundary. A moved/deleted session
cannot nominate a replacement parent after the original Actor sources and Tenant
have been acquired. Current Human/Tenant Publish facts govern both a new transition
and an already-Published response; existing session authorization, parent status,
concurrency and readiness remain independent requirements.

The actual session transition and parent schedule-summary update share the native
transaction and both existing terminal commit checks, including co-located privacy
ownership. Only the two moderation failure codes are mapped to session-specific
codes and the original session ID. No session notification/federation intent is
invented; cache invalidation remains after committed success. Repeat-publish retry
handling is desired-state recovery, not proof of exactly-once authorship. Seventeen
native cases are authored UNRUN/unbuilt: the first already-Published denial case
and sixteen additional cases covering nonadoption, current caller isolation,
Tenant/Instance sanctions and protection, unavailable/retired history, readiness,
parent movement and rollback after the parent summary update under both commit
owners. The movement checkpoint uses one SQLite transaction, not competing native
connections. Additional-case source review identified missing native session-update
authority in the fixture; it now seeds the existing active Event Owner assignment
and retains the decorated command handler and actual role-permission lookup. No
ordinary authorization is mocked or bypassed. No additional causal fixture defect
was identified in that bounded review. Application compilation passed with 182
warnings and no errors in 39.96s. Focused source review found no attributable defect;
Roslyn diagnostics remain unavailable, not a clean diagnostic snapshot. No provider
or runtime assurance is claimed, and service/publisher-Actor overlays remain open.

Current-Human ticket-catalog publication now joins that same native publication
boundary. The original Event/Tenant and ordinary `ManageTickets` authorization
remain independent of the Human-at-Tenant Publish overlay. Positively qualified
nonadoption preserves the native rules; adopted denial or unavailable authority
prevents publication. The callback retains entitlement-target validation and paid
commerce preflight, then materializes admission targets, retires the prior catalog,
flushes, publishes the draft and flushes again in the same owned transaction.
Both existing terminal commit checks cover all those writes, including co-located
privacy ownership. A failed check must roll back the prior retirement as well as
the new snapshot and materialized targets, without cancelling existing orders.

Only the two moderation failure codes are mapped to ticket-publication codes with
the original Event ID. Existing catalog IDs, validation/concurrency failures and
post-success cache invalidation remain intact. The authoritative machine principal
continues through its existing unit-of-work lane; this is not a machine/publisher-Actor
overlay cutover or a new exemption. The subsequent promotion and add-on handoffs
are described separately below.
Sixteen new native cases are authored UNRUN/unbuilt. They exercise actual draft
publication, independent entitlement/schedule checks, missing paid-directory readiness,
current-Human isolation, Tenant/Instance sanctions and protection, unavailable authority
and rollback after both catalog saves under both commit owners. Direct handler cases
do not establish decorated authorization behavior or full paid checkout readiness.
Application compilation passed with 182 warnings and no errors in 39.59s. Bounded
source review found no material attributable defect in the handoff or native
fixtures. Roslyn diagnostics remain unavailable; no provider, runtime or measured
activation assurance is claimed.

Current-Human promotion publication now joins the same native publication boundary
around the complete definition/code mutation. The original Event/Tenant is checked
before entry and again inside the callback; ordinary `ManagePaidEventCommerce`
authorization and native promotion validation remain independent. Positively
qualified nonadoption preserves those rules, while adopted denial or unavailable
authority prevents entry or aborts the actual commit. The existing authoritative
machine lane retains its serializable unit of work; this is not a machine or
publisher-Actor overlay cutover. The subsequent add-on handoff is described below.

Definition publication, active-code digest generation, code creation and persistence
must be atomic. Body `ArgumentException`/`InvalidOperationException` instances now
escape the callback and are translated only after its transaction has rolled back;
otherwise a digest failure could return validation while committing a published
definition without its code. Only those exact body failures are translated, not
dirty-caller rejection or terminal authority/concurrency failures. Successful
responses retain the derived promotion payload and definition ID. The new denial
and unavailable failures use the original Event ID and map to HTTP 403 and 503,
respectively; existing validation stays 400. Existing HTTP idempotency replay is
unchanged and is not a new authorization recheck of a completed response.

Twenty new cases are UNRUN/unbuilt: one dirty-caller native invariant, three API
status cases and sixteen native publication/rollback cases. The latter use actual
definition/code persistence, independent tenant/instance sanctions and protection,
wrong ancestry, digest-provider failure after Publish, and post-save rollback under
separate and co-located privacy ownership. Direct handlers do not establish decorated
authorization, HTTP replay or machine-principal behavior. Application
compilation passed with 182 warnings and no errors in 39.99s; API compilation passed
with 2 warnings and no errors in 38.28s. Bounded production/API and native-fixture
source reviews found no additional causal handoff defect. Two source witnesses
were corrected: validation returns derived `ValidationProblemDetails`, and ordinary
not-found responses retain their existing empty ID rather than the new moderation
failure's Event ID. No product changes or repeated builds were needed for those
test-only corrections. Roslyn diagnostics remain unavailable; no provider, runtime,
concurrency, MAD or activation assurance is claimed.

Current-Human add-on catalog publication now joins the same original-Event/Tenant
publication boundary. It repeats the actual tenant, PlatformManaged participation
and Actor/Organizer ownership checks inside each attempt. Current catalog
retirement, draft publication, persistence and successful catalog mapping share
one transaction, with the existing entry and actual-terminal moderation checks
under both primary and co-located privacy commit owners. Domain exceptions escape
the callback for rollback; this does not add unrelated exception translation.

The publish-only internal command response preserves a successful catalog's native
ID and payload while keeping the HTTP success shape `HalResource<EventAddOnCatalogDto>`.
Restriction and authority unavailability are distinguishable HTTP 403 and 503;
ordinary absent or unmanageable targets remain 404. Other add-on operations keep
their existing contracts. Qualified nonadoption preserves native ownership rules,
not an ordinary permission grant. The authoritative machine lane keeps its prior
serializable route and ownership checks. Add-ons currently have no ticket-style
paid preflight, and none is invented here. No order cancellation, notification,
cache behavior or idempotency-replay authorization policy is added.

Twenty-one new cases are UNRUN/unbuilt: one first-authored dirty-caller native
case, four controller failure/HAL cases and sixteen native catalog cases. The
native witnesses cover independent tenant/instance sanctions and protection,
unavailable authority, native ownership and missing targets, an empty-catalog
domain failure, and post-save rollback of publication, prior retirement and stamps
under separate and co-located privacy ownership. Direct handlers are not proof of
decorated authorization or HTTP mapping. Application compilation passed with 182
warnings and no errors in 35.75s; API compilation passed with 2 warnings and no
errors in 19.84s. Bounded production/API and native-fixture source reviews found
no concrete causal defect. Roslyn diagnostics are
unavailable; this is not provider, runtime, MAD or activation assurance.

Native registration-form version publication now joins the existing current-Human
publication boundary before loading the tracked version. The original Event,
form, version, tenant and publication time remain fixed across attempts. Native
`RegistrationForms.Publish` authorization, exact ownership, expected concurrency
stamp, Draft-only preflight and generated-schema invariants remain independent
requirements; unrestricted moderation facts do not grant ordinary permission.

The callback includes preflight, schema-bundle generation/pinning, user attribution
and repository persistence. The registration repository borrows the boundary's
existing transaction and retains its storage-reference fences. A failed actual
terminal check therefore rolls back the version status, artifacts, hash and stamp,
including when the co-located privacy owner performs the real commit. The shared
artifact generator and provider-import/mapping writers are not changed or claimed
covered by this native-authoring handoff.

Only the boundary's two publication failures are translated, preserving the form
version identity: `registration_form_publish_restricted` is HTTP 403 and
`registration_form_publish_authority_unavailable` is HTTP 503. Strong `If-Match`
validation and preflight remain 400; missing ownership remains 404 and concurrency
remains 409. Successful publication keeps its existing response and HAL consumers.
Seventeen source cases are UNRUN/unbuilt: the first dirty-caller witness and sixteen
native form cases. They cover real decorated ordinary authorization, independent
preflight, complete generated artifacts, original/current Human distinctions,
tenant/instance sanctions and protection, unavailable authority, retired support,
and actual post-save rollback under separate and co-located privacy ownership.
Ownership-error witnesses call the actual service rather than substituting its
authorization owner. Application compilation passed with 182 warnings and no errors
in 40.67s; API compilation passed with 2 warnings and no errors in 34.33s. Bounded
production/API source review found no concrete attributable defect. Fixture review
identified an organizer shortcut in the two no-permission cases; their publisher
and organizer now belong to another Human, so the real EventOwner permission
distinguishes denial from allowed preflight. These source corrections remain
UNRUN/unbuilt. No stale-stamp case is authored in this increment; ownership-error
cases qualify adoption rather than qualified absence.
Roslyn diagnostics are unavailable, not a clean result. No provider/runtime,
material-review, activation or complete `publication.form` conformance is claimed.

The external-provider import command also publishes a frozen schema version, so
its Human lane now joins the same original-Event/Tenant publication boundary before
connection lookup or remote schema reading. The callback retains the actual
connection, normalized snapshot, drift classification, revision, form/version,
generated artifacts, pinning and save as one native operation. The existing
machine-principal lane still calls its original import operation directly; this
does not introduce machine moderation or change binding provisioning, mapping
publication, subscriptions or the shared artifact generator.

Ordinary `ManageRegistrationChannels`, route tenant/Event ownership and repository
filters remain independent. Qualified nonadoption is not ordinary permission.
Connection/schema failures retain connection IDs; missing requested forms retain
form IDs; existing/published imports retain version IDs; drift failures retain
revision IDs. In particular, the existing blocked-drift evidence revision is still
saved despite its failure response. Only publication restriction/unavailability
map to `registration_provider_external_import_publish_restricted` (403) or
`registration_provider_external_import_publish_authority_unavailable` (503), with
the original Event ID. Other management actions and validation mapping are unchanged.

Seventeen source cases are UNRUN/unbuilt: the first dirty-caller witness and
sixteen native import cases covering frozen artifacts/provenance, current
restrictions, independent ordinary permission, duplicate imports, retained drift
evidence and post-flush rollback under separate/co-located privacy owners.
Fixture review found a missing command namespace and an organizer-authority
shortcut in the no-permission case; both are corrected. The permission fixture
now uses another Human's publisher/organizer Actor while retaining decorated
native authorization. Wrong-request-tenant coverage keeps the ambient tenant
unchanged; it is not proof of a switched-tenant authorization flow. Rollback
assertions clear tracking and query persisted state, not a separate verification
scope. Application compilation passed with 182 warnings and no errors in 36.26s;
API compilation passed with 2 warnings and no errors in 20.06s. Production/API
source review found no concrete correctness defect. No production change or
redundant build was needed for the fixture corrections. Diagnostics remain
unknown because Roslyn is unavailable; no runtime/provider, material-review or
complete form-publication support is claimed.

### Delegated provider binding publication — source integration in progress

The existing binding publication owner now handles both the Event-authorized
management command and its low-level native writer command. It retains one
mapping-hash/state writer instead of nesting publication boundaries. The low
command requires an Event anchor; selected attempts verify the original tenant,
Event, binding, form/version, connection and registered provider tuple after the
boundary reloads its owning context. The repository then fences the original
connection, form, version and binding rows before reloading the graph and checking
those coordinates. These native row fences survive both binding SaveChanges and
the actual primary or co-located privacy-owned commit. A replacement connection
or Event ownership is rejected, not followed under already-retained locks. This
also holds the parent stamps used by native form/mapping writers; serializable
snapshot reads alone are not claimed to protect the connection tuple.

This handoff applies only to current-Human delegated automation whose registered
descriptor has neither form provisioning nor subscription management. The actual
Microsoft Forms descriptor qualifies. Its high-level local readiness checks run
inside the existing Human/Tenant Publish boundary before mapping publication;
the low-level owner retains its existing drift/time contract without acquiring
new managed-readiness prerequisites. Ordinary channel-management authorization
remains independent. Qualified nonadoption preserves those ordinary rules;
adopted restriction or unavailable authority blocks entry or rolls back at the
actual native/co-located privacy commit.

Managed providers and machine callers retain their existing paths. In particular,
remote provisioning/subscription effects are not placed in a replayable local
transaction or claimed reversible. New binding publication restriction and
authority-unavailability failures retain Binding identity and map to 403/503;
native readiness, drift and missing-binding failures keep their existing 400
contract. Nineteen new cases are authored UNRUN/unbuilt: eighteen native binding
cases plus the required-Event-anchor validator case. They include genuine
persisted deterministic hashes across reversed mapping insertion order, LOW
blocking drift without HIGH readiness, and source-tuple retarget rejection before
publication. The latter are same-transaction source-order witnesses, not
PostgreSQL cross-connection blocking proof. Rollback coverage is pairwise, not
the full HIGH/LOW × failure × commit-owner matrix. The two obsolete mock-owner
publication unit methods were replaced by these native business counterparts;
unrelated mapping tests remain unchanged.

Initial Application compilation failed with 182 warnings and four API-use
errors; the real descriptor capability type and conflict-exception constructor
are now used without suppressions. Corrected Application compilation passed with
182 warnings and no errors in 39.03s. Initial Persistence compilation then found
one incorrect tenant-context property; corrected compilation against the actual
`TenantId` contract passed with 62 warnings and no errors in 2:50.00. Initial API
document generation loaded the old Persistence assembly before that dependency
finished; its ordered rebuild passed with 2 warnings and no errors in 28.85s.
Roslyn diagnostics remain unavailable; compilation does not resolve the key blocker.

Native-fixture review identified a separate, existing persistence blocker:
`PublishedMappingRevisionHashKey` is an EF alternate-key component, yet publishing
an already-persisted Draft changes it from empty to the published hash. Ordinary
tracked persistence cannot perform that key transition. Consequently the success
and post-flush rollback cases are not currently valid execution witnesses. The
hash-order fixture also attempted a duplicate tenant/connection/form-version
binding; it now uses a distinct connection with the same descriptor tuple and
different name/workspace, preserving the intended equal-hash comparison. All
nineteen cases remain UNRUN/unbuilt. The immutable publication-reference design
and its dependent attempt foreign keys require a bounded root-cause correction;
no constraint, model or migration has been weakened or changed to bypass it.
The dependent registration-attempt FK currently pins the exact tenant, binding
identity and published mapping hash; native attempt and callback/write-effect
checks also consume that identity. Dropping the hash FK, replacing binding IDs or
silently updating an EF key would weaken those invariants rather than fix the
publication model. The recommended design direction is a binding-owned immutable
publication identity born with its final hash, preserving the exact attempt FK.
The maintainer approved that bounded correction, including Domain/Persistence
changes, generated provider migrations and existing-evidence transfer; no migration
application was approved. The source model now introduces
`RegistrationProviderBindingPublication`, born with its final tenant, binding,
ordinal mapping hash and UTC publication time. Binding IDs and public hash/time
properties stay unchanged. A nullable-time composite binding FK points to that
immutable principal, allowing a saved Draft to insert publication evidence before
updating its non-key publication fields. The same dependency direction supports
publication before the binding's first insert without a reciprocal FK cycle.
Attempts retain their exact tenant/binding/hash FK, now targeting the publication
principal rather than the mutable Draft key. Native save guards reject orphan
publication insertion, evidence replacement/deletion and partial or mismatched
attempt provenance. These guards are not a claim of direct-SQL append-only enforcement.

The PostgreSQL human Event-provider source guard also checks each mapped
column's comparison semantics. Publication mapping hashes are marked
`UsePortableOrdinalAscii`, whose PostgreSQL representation is `C` collation in
both the model and the generated publication migration. The guard now requires
that exact collation for marked properties; unmarked properties retain the
existing non-collatable/default rule. It does not accept `C` for arbitrary source
columns. Type, nullability, table ownership, native privileges, routine, epoch,
RLS and trigger checks are unchanged. A mismatched collation still rejects
runtime observation and installation with `human_event_provider_source_drift`;
installation does not repair it. No model, migration, permission or configuration
setting changes. Two native regression variants exercise opposite collation
drifts with healthy observations before mutation and after restoration; they
remain locally unbuilt and unrun pending remote provider verification.

Two Domain and nineteen native persistence cases are authored UNRUN/unbuilt.
Both insertion-order cases now include an exact provider attempt in the same
save as publication, and malformed half-null attempt coordinates are rejected.
Source review found a persisted blocked-state bypass in the new ownership guard;
its regression was authored first, then the guard was narrowed to original Draft
state. The bounded follow-up found no additional concrete fixture or ownership
defect; it is not an EF-model or provider execution witness.
Domain compilation passed with 146 warnings and no errors in 6.19s. Persistence
initial compilation passed with 62 warnings and no errors in 3:16.34, before that
final guard change. Native PostgreSQL, SQLite, SQL Server and MySQL migrations have
now been scaffolded, without database access or application. All four provider SQL generators integrate
an evidence-transfer preflight: canonical ordinal hashes, complete publication
coordinates and exact tenant/binding/attempt provenance must match before any
schema change. Every Published binding, including soft-deleted history, transfers
its existing binding ID, hash and timestamp unchanged into the immutable principal.
Initial database creation does not read nonexistent legacy tables. Downgrade must
prove bidirectional binding/publication equivalence and restore the legacy exact
attempt FK before removing publication evidence. SQLite additionally defers that
removal until after native owner-table rebuilds. Invalid evidence stops migration;
it is never repaired, rehashed or silently discarded.

The transfer requires an exclusive migration window and enforced CHECK constraints.
On nontransactional DDL providers a failed preflight can leave an integer-only guard
table; operator cleanup must not bypass the underlying validation. Forty-four
generation-only transfer cases are authored UNRUN/unbuilt in addition to the two
Domain and nineteen persistence cases. They are not execution of generated SQL.
The SQLite migration product project compiled with no warnings or errors in
24.26s; the scaffolder compiled separately and ran with `--no-build --no-restore`.
The MySQL transfer permits only the native UTF-8/ASCII hash-column charset transition
after canonical-ASCII preflight, preserving all other column facets. SQLite's
integer-only guard drop does not disable foreign keys before preflight; publication
removal remains deferred until after native owner-table rebuilds. The standalone
Down refusal case now supplies a complete inverse key/FK shape, so it specifically
guards the required post-rebuild boundary.

Persistence compiled with 62 warnings and no errors in 3:25.52 before a subsequent
native SQL review found a computed-column dependency: PostgreSQL and SQL Server
must remove the stored attempt hash key before altering its source hash collation.
The generator now stages its exact native reference indexes and computed column,
preserves the source hash unchanged, then recreates the same derived expression,
target collation and indexes before restoring the exact FK. Preflight still runs
before any of these changes. Four first-authored generation cases cover both
directions on both engines and reject a replacement expression; all remain
UNRUN/unbuilt. That iteration compiled with 62 warnings and no errors in 2:52.21.
Follow-up review found SQL Server also retains its provider-pair CHECK over the
source hash. The generator now removes that exact native CHECK before the source
alteration and restores its unchanged target-model SQL after computed-key/index
recreation. Unknown dependency shapes are rejected rather than silently dropped.
The same four cases now use actual native Up/Down operations, the historical
downgrade target model and fresh, unprepared generator input; reversing collation
metadata alone is no longer described as downgrade coverage. These strengthened
cases remain UNRUN/unbuilt. That correction compiled with 62 warnings and no errors
in 3:51.20. The SQLite, SQL Server and MySQL migration product projects then compiled
with zero warnings/errors in 32.18s, 34.76s and 22.37s respectively. Native bounded
Up/Down SQL generation completed for all four families, without opening or applying
a database. SQLite emitted pending-rebuild SQL-operation warnings; generation alone
does not establish that the emitted transfer can execute.

Reviewing the actual SQLite downgrade exposed a further ordering dependency: its
temporary attempt table is copied before the binding rebuild restores the legacy
three-column parent key. The generator now creates a temporary unique index on those
unchanged binding coordinates after evidence preflight and before copying attempts.
The old binding table's later native rebuild removes that temporary index; the target
table receives its real legacy alternate key. Foreign keys need not be disabled ahead
of validation, and the publication table remains until both owner rebuilds finish.
One first-authored case uses actual native Down operations and the historical target
model to require this ordering; it is UNRUN/unbuilt. Final bridge compilation passed
with 62 warnings and no errors in 3:09.89. The SQLite migration project then compiled
with zero warnings/errors in 0.94s, and native bounded Up/Down SQL regeneration exited
successfully without database access. The regenerated Down script places the bridge
index after preflight, before temporary attempt copying, and leaves publication removal
until after the binding rebuild. SQLite's pending-rebuild generation warnings remain
recorded; neither compilation nor SQL generation proves database execution. Earlier
diagnostic SQL pairs are preserved separately rather than overwritten. There are 65
root-correction cases in total (2 Domain, 19 persistence, 44 generation), all UNRUN/unbuilt.
The final bounded review of regenerated SQLite Up/Down SQL and its source found no
additional concrete defect. It confirmed preflight/transfer, temporary parent-key
availability and post-rebuild publication removal; it did not execute either script.
The generated SQL Server migration also exposes the previously
recorded unrelated case-evidence FK downgrade discrepancy (`Cascade` versus the
predecessor's physical `NoAction`); it remains an unapplied release gate, not a
hand-edited correction or a claim that the downgrade is safe. No migration is applied,
hand-edited or represented as safe merely because
it generates. This dependency affects managed
as well as delegated publication, despite the narrower moderation handoff.
Support remains Partial, not provider, runtime, remote replay or compensation
assurance.

### Resource publication — source integration in progress

`EventResourceManagementWorkflow` now runs only publish/republish through the existing
`IEventPublicationMutationBoundary`, retaining the original parent Event/Tenant,
both native resource authority rechecks, version CAS, destination/payload checks,
state transition and retention-qualified audit in the owning transaction. Current
Human restrictions apply at the actual Tenant, not a fabricated Resource Publish
scope. Qualified nonadoption preserves ordinary authorization; adopted denial or
unavailability blocks entry and the actual primary/privacy commit. Withdraw,
archive, delete and other mutation paths retain their existing owners and rules.

For publish requests, `EventResourceModerationSnapshotReader` reads the same Tenant
publication facts once per batch, including the qualified Human subject. Its resource
generation explicitly includes the qualification epoch revision; qualified absence
also retains a generation without inventing subject authority. This connects native
authorization and publish affordances to the same restriction facts while preserving
ordinary resource permissions. Read/upload capability scopes remain independent.
The initial resource authorization snapshots still precede the publication boundary;
this change does not claim a new dirty-caller guard before that existing preflight.

Single publish authorization separates ordinary native/provider permission from the
moderation outcome. Only after both ordinary checks allow the manager does the
orchestrator report an adopted restriction as Forbidden or unavailable authority as
Unavailable. This avoids hiding an otherwise authorized manager's outage behind
NotFound without exposing resources to unauthorized callers. Default evaluation and
batch/HAL decisions remain moderation-gated, so neither condition advertises a publish
link. Both fresh authorization snapshots and mutation requalification retain these
checks; the owning publication boundary independently checks actual commit authority.

Before returning a moderation-specific publication failure, ordinary publication
authority is re-evaluated at the same fresh timestamp and its disclosure/provider
input must still match the frozen provider input. A delayed provider Allow therefore
cannot reveal restriction or outage classification after timed staff authority
expires. Cancellation and request deadlines take precedence after awaited reads.

Thirty-three cases are authored UNRUN/unbuilt: one immutable-facts case, sixteen native
workflow cases, fourteen publication-snapshot cases and two after-provider expiry
cases. The expiry cases do not independently exercise expiry during snapshot B.
They do not establish
end-to-end HAL/Cerbos, stored-file, zero-retention or provider concurrency conformance.
Application compilation passed with 182 warnings and no errors in 35.93s after the
failure-classification correction; Persistence passed with 62 warnings and no errors
in 2:40.55. Older manual fixtures now construct the real native publication boundary
with native identity, asynchronously owned privacy authority and continuity, not a new
internal mock. Bounded fixture review corrected a missing contract namespace and the
success assertion: native material mutation rotates the parent Event stamp. Follow-up
source review found the native-authority expiry disclosure window; the fresh-time
correction and its two regression witnesses received no further concrete finding.
Final Application compilation passed with 182 warnings and no errors in 44.61s;
unchanged Persistence was not rebuilt. Roslyn diagnostics are unavailable, not
clean. Support remains Partial; compilation/source review is not runtime, provider or
MAD assurance. No publisher-Actor, machine, material-review or cleanup ban is introduced.

### Verified AT Protocol participation writes

`AtprotoParticipationMutationFactory` joins Group/Organization onboarding to the
existing recognition/privacy enrollment and primary serializable transaction. The
bootstrap acquires its source fences before `MatchAndBind` and JIT provisioning, using
the verified provider account and exact current/planned external-login and Human IDs,
not an HTTP caller or an ordinary membership-management bypass. Person login does not
acquire this membership participant.

The transaction-owned installation namespace covers absent authority sources. Ordered
Human/login/role/tenant-membership fences precede the shared host, maintenance owner,
tenant/status and existing qualification epoch; actor, represented identity and
Group/Organization participation/member fences precede promotion or consolidation.
An existing configured host without its tenant epoch fails closed. No source is
automatically installed and no participation is approved by this mechanism.

After the real repository creates a new represented-actor admin membership, the
participant verifies its exact DID, Human, owner and role before advancing the existing
epoch in that same transaction. Repeated notification or an already-existing membership
does not advance it. Pending participation stays pending. Metadata-only and Person
paths do not manufacture a membership invalidation.

The normal primary pre-commit callback and the actual co-located privacy commit both
recheck retained erasure, the exact login, active Human/tenant enrollment, qualification
revision and recorded admin membership/represented actor. The participant is reset on
each primary replay. Failures after JIT are raised through the enrollment owner so its
transaction rolls back before the bootstrap maps the bounded failure result; returning
a failed onboarding value must not commit partial provisioning. No second privacy
transaction or generic commit-callback registry is introduced.

Eleven real-store source cases are authored UNRUN/unbuilt, including the actual
onboarding operation for Group and Organization creation/replay, pending participation,
duplicate notifications, retained erasure, wrong-Human membership and late login,
role or actor changes with separate and actual co-located commit ownership. These are
not full OAuth/bootstrap, provider-contention or final disclosure-flush proof. Focused
source review reported no concrete material finding; compilation and deferred runtime
assurance remain separate evidence. Product builds passed with unsuppressed warnings:
Application 182 / 0 errors, Persistence 62 / 0, API 2 / 0. No test project was built.
Membership support remains Partial.

### Target-owned allegations

Target-owned allegations retain immutable native material provenance without
inventing a responsible subject. `ModerationCase.Subject` and its scalar identity
are nullable until a separate attributed owner is established. Intake assignments
start unresolved and unqualified; absent attribution cannot create a final finding.
New facts may append after closure without reopening adjudication or changing
another subject's responsibility. These model checks do not qualify native
sources, authorize narrative access or complete provider/runtime assurance.

`ModerationOperationalBudget` now supplies portable rolling-window accounting
for reports, contestations, Event creation/publication/import and service effects.
Terms explicitly name a canonical subject, authority scope, optional mandate,
action, positive limit and finite window; a Service subject requires a mandate.
Usage counts distinct original UUIDv7 occurrences, including the exact window
boundary. Exact retries preserve their original time and remain Duplicate even
when older than the current window. Changing limits does not reset supplied
history, and other subjects/origins/scopes/mandates/actions cannot spend its quota.
Conflicting duplicate timestamps, future records and noncanonical clocks reject
an apparent capacity assessment. Available is accounting, not a native permit.
Fourteen Domain vectors are authored UNRUN/unbuilt; the Domain product compiled.

Native Human allegation intake now uses explicit operator-qualified terms.
`ModerationOperationalBudgetSource` records a tenant, Human/action, positive
maximum, finite rolling window, independently named operator and finite validity.
The current tenant/platform administrator records; the named different native
administrator adopts the exact source revision and writer generation.
Replacement selects the latest generation; expired, missing, unadopted,
withdrawn or unauthenticated source/adoption receipts make new intake unavailable.
Withdrawal still permits a current administrator to retire expired/departed
ownership. Service terms are rejected: identity binding is not a service mandate.
Typed record/adopt/withdraw CQS and authenticated 32 KiB no-store POSTs under
`/api/moderation/operational-budgets` retain manual validation and route-bound
withdrawal. Their configuration metadata grants no reporting or target access.
The three concrete command records and nested `ModerationOperationalBudgetTerms`
use `JsonUnmappedMemberHandling.Disallow`, matching the existing strict native
moderation boundary. Caller-supplied recorder/adopter identity, administrator
assertions or a fabricated mandate cannot hide in ignored JSON properties.
The generated contract must close all four objects and preserve their exact
required fields and named action values; compilation alone does not prove this.
New intake explicitly finalizes native disclosure after its complete primary save,
then rechecks target, narrative and operational-budget authority before commit.
The transaction interceptor's later idempotent flush cannot move disclosure
finalization after those checks. Loss of the source/adoption, original writer
receipt or finite validity rolls back both acceptance and its only usage record.
Opaque receipt minting also finalizes disclosure before its final native source,
relationship, caller and expiry checks. It releases no bearer if those checks
fail after finalization; receipt/source writes and disclosure advancement roll
back together without renewing the original deadline.
The digest-only receipt model itself rejects the unbounded maximum-date expiry
sentinel at issuance and persisted-state validation; a host-side check is not its
only finite-lifetime boundary.

`ModerationQualificationAuthority` discovers budget and narrative operators
together before ordered Human/identity/role fences. An ordinary reporter remains
an ordinary reporter. `ModerationOperationalBudgetStore` acquires current terms
on the owning serializable intake transaction and locks the whole canonical
Human/origin/Tenant/no-mandate/SubmitReport bucket. The concrete success journal
already exists: immutable intake acknowledgements joined to consumed native
receipts retain the caller, canonical occurrence, complete native origin and
original acceptance time. Reusing that authoritative journal avoids a second
counter store, duplicated private narrative or another retention purpose.
Complete relevant history is grouped by original occurrence and earliest
successful acceptance, never a new operation ID, receipt, policy generation or
credential. A duplicate suppresses a charge; independent native target authority
still runs. Invalid submissions and rollback create no acceptance or charge.

New acknowledgements retain the exact budget source/adoption and writer
generations. Case, narrative, receipt consumption and acknowledgement still
flush and commit together. After the complete flush, budget requalification
checks the actual operators, original writer receipts, selection/deadlines,
transaction and exact expected history before the owning commit. A later
duplicate cannot renew its first acceptance timestamp. Exact original request
replay returns only its old acknowledgement, including after budget withdrawal,
and supplies no new action or disclosure permission. Provider migrations,
generated API/Client artifacts and native/runtime assurance remain queued.
Other owning commands, service mandate approval and two distinct actually
approved first-publisher Events remain mandatory unfinished work.

`AuthorityScope.Event` is a distinct Tenant child, tagged `Event = 7`; Resource
still identifies an actual EventResource. Event and Resource remain distinct even
when their UUIDs match. Scalar reconstruction now requires eight coordinates:
Event has an exact Tenant ancestry and `EventId`, with neither Community nor
Resource coordinates. All other tags reject `EventId`. Native carriers, store
predicates, notice ownership proofs, API ancestry reconstruction and measurement
receipt JSON preserve that distinction; receipts require the eighth field rather
than accepting an old-format fallback.

Catalogue revision 2 adds Event eligibility only to CFG29 classification
correction. Other reference profiles retain their existing scope sets. A prior
catalogue declaration cannot be reinterpreted as Event authority. The closed
Content-case prerequisite family acquires administration at the actual parent
Tenant, then fences and rereads the exact Event and live Actor ancestry without
selecting payload. Calendar disclosure permission and measured conformance require
separate qualified owners; migrations, generated contracts and runtime/provider
assurance remain queued.

### Reference classification recurrence calculation

`ModerationClassificationRecurrence` is a pure, framework-free calculation, not a
native progression command. Its acceptance coordinates retain the canonical
subject, exact jurisdiction, immutable violation-rule revision, acceptance ID and
historical acceptance time. Each input snapshots an actual validated finding and
its original occurrence, attribution source/generation and separately qualified
conduct time. Conduct cannot postdate the original case opening. Matching these
coordinates does not prove actual acceptance, classification misconduct or current
authority; the native producer must establish those facts under its writer fences.

Only final findings in the exact subject/jurisdiction with matching prior policy
acceptance contribute. A revised policy neither reinterprets an old finding nor
supplies missing old acceptance. The reference window is `(now - 180 days, now]`
on original conduct, not delayed finalization or replay. Future finalizations and
reversed findings do not contribute. Findings across attribution generations for
one original occurrence count once, while all contributing finding/revision,
occurrence, acceptance and rule identities remain in ordered immutable provenance.
Conflicting snapshots of one finding, occurrence time or acceptance identity fail
closed rather than selecting a stale final finding or refreshing a conduct date.

Zero occurrences yield no reference profile; one through four select CFG30,
CFG31, CFG32 and CFG33, and further occurrences do not expand CFG33. A fresh
assessment removes reversed contributions without mutating an earlier result.
The result carries no sanction, enforcement interval, permission or activation
receipt. Separately qualified persistent evasion is not inferred from report
volume, justification text or a caller flag. Its independent producer, genuine
historical policy acceptance, complete fenced history and native progression
command remain unfinished. The 28 authored Domain cases are UNRUN/unbuilt under
the programme override; the production Domain project alone compiled with zero
warnings/errors. This is not native/provider or recurrence-activation assurance.

### Non-punitive Content/Event classification controls

`ModerationClassificationControlSelection` retains the complete CFG29 composition
from an actual independently accepted whole-Event classification review. Its
source, review and Event-generation IDs, revisions, writer generations, native
stamp, policy publication/profile, controller provenance and digest are immutable.
`ValidUntil` caps receipt freshness; it is not a fabricated sanction end or review
date. The existing decision-backed selection and sanction lifecycle remain separate.

`POST /api/moderation/classification-controls/selections` records that composition.
`POST .../adoptions` independently adopts one exact retained binding ordinal:
every effect and base/additive consequence needs its own adoption. The author is
distinct from the material owner/reviewer and their controllers; the adopter is
also distinct from the author. Both require current native administrator
authority. The target is the typed Event, while administration is acquired at
its actual parent Tenant. Tenant, Resource and another Event cannot substitute.

The native qualification transaction acquires real definition/material owners,
fences discovered humans before subordinate locks, stages new rows only after
the final clean-tracker material check, and advances the epoch with a
principal/request-bound receipt atomically. It rechecks source heads, inventory,
review, Event generation, roles, digest and deadlines after save, on replay and
receipt recovery, and at the original custody/final-disclosure commit boundary.
The separate tables are trust-domain/tenant filtered and preserve immutable birth
provenance. The source-kind constraint includes the new receipt tags 41 and 42.

`POST .../adoptions/{adoptionId}/revoke` makes the route identity authoritative.
Correction authenticates retained birth receipts and the current corrector,
without reacquiring expired or withdrawn material or a deleted native Event as
live authority. It retains grounds and revocation writer generation. All three
writes are authenticated, write-rate-limited, non-cacheable and bounded to 1 MiB;
their native receipts are excluded from generic idempotency response storage.
These are source controls, not an anonymous audience grant or calendar delivery
owner. Their authored tests remain deferred under the programme override.

### Owned anonymous calendar projection

The public `GetEventCalendarExportRequest` now returns a single-use
`PublicEventCalendarProjection`, not an export DTO. Its repository contract owns
a closed anonymous purpose and returns native Event/session entities under a
lease. Administrative provenance is not the viewer's identity. The native
anonymous verifier is implemented by `ModerationPublicEventCalendarRepository`
with a closed `ModerationSourceReadSession` provenance core. The public branch
cannot carry authenticated authority or an original-effect recipe, while
authenticated wrappers preserve their existing current/candidate checks.
It acquires the actual selected active definition, complete independently adopted
CFG29 Event bindings, native material/taxonomy/age/governance and unrestricted
whole-Event compilation under primary/privacy/human/content fences. Fingerprints
and currentness are reread at terminal use. Its deadline is the minimum real
source deadline, capped at 30 seconds from serialization entry.

Production `IModerationPolicyPublicationEvidenceOwner` is still absent, so genuine
allowed-calendar and downstream source-change test groups remain unauthored.
The three native cases authored so far guard candidate denial, original privacy
serialization and cross-tenant rejection. A fixture that manufactures successful
measurement evidence is not an activation path. The programme's genuine
measured-conformance work must supply that dependency; source integration and
compilation do not certify public exposure.

At terminal use the projection acquires that lease, checks exact tenant/Event/
session coordinates under validity cancellation, resolves location with `RequesterUserId: null` and Public
purpose, and maps the DTO only under custody. `PublicEventCalendarHttpResult`
builds ICS bytes and the filename privately with the existing Ical.Net builder.
It rechecks the original native source and half-open deadline before headers or
bytes, then holds the lease until both `Response.Body.WriteAsync` and
`Response.CompleteAsync` settle. Buffered completion can flush remaining data
after writing returns, so it stays inside the same native delivery callback.
Execution-strategy callback retries cannot repeat external delivery.
Expiry between native revalidation and entity access is rejected without mapping
an unhandled export. Guest registration HAL consumes native availability under
this lease: a nonnull pending projection or administrator claim grants no link.

The native lease's mandatory `ValidityCancellation` joins request cancellation
through preparation, writing and completion. During delivery it aborts the HTTP transport at
expiry; a started or attempted calendar response is never replaced with a
ProblemDetails body. Even an outstanding write that ignores its cancellation
token retains the original fences until it settles. Completion has no token
overload: its cancellation registration keeps aborting the transport without
detaching the unfinished task or releasing custody early. Three event-driven
completion cases are authored but unrun, using the response-body feature over
the real stream feature rather than a mocked completion call. These mechanisms do not
establish real Kestrel cutoff conformance by declaration or cancellation alone:
native/provider and actual transport assurance remain queued and unrun.

The public route is non-cacheable and still warns that external calendar copies
cannot be recalled. Missing Events return 404; unavailable current qualification
returns 503 before output. Complete unrestricted whole-Event classification is
required for an anonymous viewer with unresolved age; there is no privileged
signed-in bypass, invented age evidence, partial-summary or segregation exception.
The separate attendee calendar contract is not widened by this public projection.

Scheduled finite-custody disposal precedes maintenance authentication.
`ModerationReconciliationJob` purges expired Pre-Finding and original case
narrative custody in the selected tenant before acquiring a machine credential.
`ModerationCaseNarrativeRepository.PurgeExpiredAsync` is bounded monotone cleanup:
it needs neither a live custody policy nor a decryptor, retains birth provenance,
and removes only expired/withdrawn material. A missing credential must therefore
not retain private narrative payloads. Authenticated notice cleanup and effect/
review recovery remain after credential acquisition. One scheduled stateful
storage regression is authored but unrun; existing native disposal coverage is
also deferred with the programme.

Retained erasure recovery now has a committed replay-input reader:
`EmbeddedPrivacyErasureAuthorityRepository.ReadSqliteCommittedReplayInputStateAsync`.
Under the original active identity writer gate it derives a read-only, private,
unpooled SQLite connection, reads mapped sequence/hold metadata, and requires
every sequence above the retained floor through the high-water mark. Missing
rows, pseudonymized rows above the floor and rows beyond the head are rejected
even when the committed counter is unchanged. Before returning, it requalifies
the original store identity, revision, recovery generation, floor/head and key
references against the original gate/context/connection. Private facts are not
materialized. This is an internal replay prerequisite for file-backed Embedded/
colocated SQLite, not a completed restore, moderation qualification epoch or
cross-owner recovery proof. The measured operations source stays unregistered;
artifact custody remains undecided. Ten native cases are authored but unrun.

Current-policy consumption checks its linked original/consumer cancellation token
after terminal delivery returns, before reporting Allowed. A writer that finishes
without observing cancellation cannot turn an aborted read into success.
`ModerationCurrentPolicyReadProjection` still disposes its original native lease
on that exception and restores the native request frame; bytes already written
are not undone. Two real current-authoring cancellation cases are authored but
unrun. The current-policy and both quarantine metadata HTTP results now directly
await `HttpResponse.CompleteAsync` inside their original native disclosure callback.
The callback token registers transport abort and is checked around write/completion;
completion is not detached by a cancellation wait. Original fences therefore stay
held until outstanding buffered completion settles. Quarantine results track output
attempts: a subsequent native rejection aborts rather than appending ProblemDetails
to an already attempted payload. Two native-backed actual HTTP cases are authored
in the API suite, source-linking the original Persistence Database and current-
authoring fixture rather than substituting successful repository responses.
The installed empty authoring slot qualifies under the real native transaction;
minimal-response serialization uses the real assembler base. Response feature
entry/release signals hold completion pending, including after request abort,
and assert the original transaction/trust-domain through completion, cancellation
propagation, no replacement payload and unchanged persisted counts. Both cases
and test compilation remain deferred; no publication/activation qualification is
inferred from their source.

Controller identity requires an independent certifier. Shared assignment mutation
and persisted-state validation exclude the reviewer, proposed controller and known
Human subject. Native `QualifyModerationReviewerController` also checks the proposed
controller against the current writer before policy/evidence creation: a change
from a different old controller cannot make self-certification a malformed-input
fallback. It is a native authority denial, preserving the original assignment,
evidence, conflict assessment, receipt set and qualification head. Three shared
Domain and two native rollback cases are authored but remain unrun.

## Reference catalogue and native policy publication HTTP

`GET /api/moderation/reference-catalogue` exposes the immutable Domain reference
catalogue through `GetModerationReferenceCatalogueQueryHandler`. The query orders
every reference by its stable profile identifier and preserves complete effect,
scope, subject, response, prerequisite, recognition and consequence-plan values.
It reads no tenant selection, private case, identity or deployed support state.
The standard HAL family emits only public `self` and `refresh` GET links; it
never infers publication, preparation or activation permission. Its wrapper and
nested reference value types are explicitly included in the schema catalogue.
This is shared Simple/Advanced authoring vocabulary, not a second policy model.

Two independent authenticated write capabilities now adapt the existing native
commands: `POST /api/moderation/policies/{policyId}/publications` dispatches
`PublishModerationPolicyCommand`, and `POST .../publication-preparations`
dispatches `PrepareModerationPolicyPublicationCommand`. Immutable substitution
makes the route policy identifier authoritative and preserves all other expected
host/source/qualification/predecessor coordinates, operation/publication
identities and complete proposed state. Missing nested intent reaches existing
manual validation; the transport does not construct a grant or compilation
context. The native repository retains actual acquisition, compilation, retry,
transaction and active-evidence ownership.

Both writes allow bounded 4 MiB JSON, are rate-limited and non-cacheable, and keep
native operation receipts outside generic idempotency response storage. Their
typed responses distinguish a reserved candidate identity/revision/deadline from
an adopted publication identity/revision and publication-time `PublishedActive`.
Preparation does not publish policy, and neither response establishes current
deployment conformance. Validation, authentication, denial, conflict and
unavailability remain distinct ProblemDetails outcomes.
The native schema owner explicitly maps the flattened catalogue wrapper and
registers its complete nested authoring values. Thirty-five moderation enum
types use the existing named-wire allowlist on both documentation surfaces;
native enum collection items, including immutable scope/subject sets, point at
the same enum components as scalar fields. This matches the API's actual
string-enum serializer rather than shipping integer fields to the Client.
Twenty-seven write-boundary cases, three catalogue/projection/read-link cases
and four hosted schema cases are authored UNRUN/unbuilt. They do not supply
native publication or activation proof. The worker compilation timed out without
a result; parent product API/native generation and regenerated Client compilation
passed after artifact inspection exposed and corrected an empty wrapper and
integer enum schemas.
`GET /api/moderation/policies/current?scope=tenant|instance` now supplies latest
adopted authoring state through `GetModerationCurrentPolicyQueryHandler`.
The handler captures the actual human/request tenant, not permission. Terminal
`ModerationCurrentPolicyReadProjection` acquires an installed native
`ModerationQualificationAuthority` and owns a clean serializable transaction.
It retains identity/role/host/tenant and jurisdiction-slot fences through private
HAL preparation, revalidation and awaited `Response.Body.WriteAsync`; no deferred
MVC formatter writes after lease disposal. Cancellation and disposal release the
read transaction and restore the original request frame.

The read selects the latest adopted pointer, not an older active pointer or
unadopted candidate. An installed empty slot returns an unreserved prospective
UUIDv7 with next-write revision zero; missing installation remains unavailable
and GET installs/reserves nothing. Expected authority source/revision comes from
the current request tenant's epoch, even when an instance publication originated
in another tenant. Application mapping restores complete retained classes,
reasons, disabled profiles and every plan. Historical expected/inventory
coordinates remain separate from current next-write/installed coordinates.
Neither historic selection nor installed inventory implies current support.

Nonserialized `NativeAuthoringQualified` supplies only `draft-publication` and
`publication-preparation` HAL links inside that native lease. There is no
activation link, export-derived permission or local role inference. Anonymous
GET metadata does not grant access: native failures remain distinct 401, 403 and
503 responses, with no-store/no-referrer delivery. The flattened wrapper,
retained authoring types and bounds are explicitly registered with the existing
schema owner, including semantic scope and rule references in the wrapper,
bounds and retained selected-profile collection.
Object reference repair includes converter-backed empty inline objects: the
registered owning CLR property names the existing factory-preserving intent
component, rather than leaving an opaque Client object. This is schema repair,
not a second serializer or permission source. Registered object repair retains
CLR reference nullability: an installed empty slot's authoring value remains
null rather than acquiring a generated empty definition. Required jurisdiction
and compilation bounds retain their nonnullable semantic references.
Thirty-eight new native/mapping/HTTP cases and two additional
native/Swagger schema cases remain UNRUN/unbuilt. Actual assembled-HAL success,
provider concurrency and BFF/administration integration remain assurance work.
Final native export and generated Client product compilation passed; inspected
wire/generated declarations retain typed scope/bounds/rules, nullable authoring
and no native grant. Compilation does not prove actual terminal authority or
hosted native/Swagger parity.

### Private per-destination configuration import preview

Selected moderation imports retain their native three-way definition comparison
instead of reducing it to a section-wide status. Each mapped destination exposes
its source/target names, current policy revision, baseline presence, complete-set
eligibility, definition changes/conflicts and bounded compiler diagnostics.
First imports report added definitions as well as catalogue/mode declarations.
Reserved operation/profile identities, host/epoch bindings and native authority
entities are not part of this representation.

`ConfigurationImportPreviewDisclosure` is a nonserialized, one-use owner. The
initial session result contains no rich details. Terminal delivery reacquires
the original session, stored artifact, token digest and native destinations under
privacy serialization and the existing serializable unit. It compares the whole
preview binding, privately prepares HAL bytes, then repeats session expiry,
artifact/reservation and current native authority checks before delivery. Session
acquisition, cancellation, consumption and expiry cleanup share a provider-native
write fence; acquisition reloads committed state instead of trusting an old tracked
entity. Session and native fences remain owned during the awaited body write and
native commit. HTTP completion follows successful disclosure/commit, not the other
way around. A clock-backed expiry token remains active through that completion;
terminal clock checks also reject expiry after awaited delivery. An attempted
provider retry cannot replay output, and failures after delivery is attempted
abort the response. Already delivered bytes cannot be recalled. The co-located
privacy commit also revalidates the binding. These controls do not authorize
partial imports or activate policy.

The existing schema catalogue names the three nested preview types. The Client
consumes generated records, renders destination details with semantic headings
and encoded values, and continues to gate Apply on the server's HAL link and
whole-preview readiness—not a destination's diagnostic status. Six native expiry/
one-use cases include blocked body writing and blocked post-commit HTTP completion.
A native cancellation-before-disclosure case and a tracked-cancellation regression,
first-import additions, named-schema assertions and five generated
HTTP/rendered cases are authored UNRUN/unbuilt. Product compilation, generation
and runtime assurance have separate receipts; no browser, concurrency or actual
HTTP-to-native disclosure conformance is claimed here.

`ModerationReferenceProfilePreview` is a read-only Client component over the
generated reference record. Both display modes retain required basis, native
effect/target/execution, prerequisites, response review and every thirteen-
dimension consequence plan and its seventeen safeguard/composition values.
Automatic historical restoration and recall of disclosed copies remain explicit
negative invariants, not implied by a restoration-mode label.
Base and additive plans remain separate; Advanced
adds reference coordinates without changing the supplied object. Semantic
headings/definition lists and logical token-based scoped CSS provide the source
layout. The component has no command, role inspection, action affordance or
current-support claim. Six component cases are authored UNRUN/unbuilt; it is
composed into the guided administration workflow. Browser,
accessibility and responsive rendering assurance are deferred, not certified.

`ModerationPolicyAdministrationService` now uses the generated current/reference
clients and actual POST transport from the named current-policy HTTP pipeline.
Shared DI deliberately resolves that named pipeline rather than an unrelated
default `HttpClient`: Server forwarding and browser credentials/antiforgery
remain owned by the existing host registrations, with no unsafe automatic retry.
`ReadAsync` distinguishes missing authority, malformed adopted state and an
installed uninitialized slot. Complete generated graphs are serialized into
frozen transport snapshots; access returns detached typed copies, so generated
mutable input collections and extension data cannot mutate service-owned state.

New operation preparation freezes complete proposed meaning before awaiting a
fresh native read, then uses only that read's host/source/qualification/writer/
policy/predecessor coordinates. It preserves retained catalogue/inventory and
all class/reason/profile/strategy/selection intent, rather than silently
migrating historical definitions. Exact POST relation/path/method and same-origin
validation supplies action availability. Candidate preparation and adopted draft
publication remain different typed results. Explicit receipt retries resend the
same frozen request and identities; no refresh, implicit resend or activation
follows a failure. A UI must compare returned operation policy/revision with
the original displayed baseline before POST and report a stale-preview conflict.
Thirty-five real generated-client/HTTP-handler cases are UNRUN/unbuilt.

`ModerationCurrentPolicyPreview` renders all retained meanings and profiles,
including unselected declarations, and distinguishes current expected revisions/
installed inventory from historical inputs. A null authoring value never becomes
an empty adopted policy. It exposes no actions or support claim, and display
mode changes only coordinate visibility. Both current and reference previews
consume `ModerationConsequencePlanPreview` for identical thirteen-mode and
seventeen-safeguard output with correctly nested headings. Four new component
cases and six existing reference cases remain UNRUN/unbuilt. These are page
composition primitives; rendered certification remains deferred.

`Pages/Moderation/PolicyAdministration` is now composed at `/moderation/policies`
through the authenticated, uncached Blazouter route. `PolicyAuthoringState` keeps
the complete detached generated graph. Class/reason/profile changes append
semantic revisions without deleting disabled definitions or silently replacing
their inventory. Native-mode declarations, selected profiles and inventory
alignment are explicit edits; Simple/Advanced only changes presentation.
`PolicyAuthoringProfile` obtains complete recipes, ground/basis compatibility,
response timing, approval floors, calendar months, scope/subject coverage and
recognition choices from the returned catalogue/bounds. It stages local intent,
not a second native compiler or active readiness.

The page compares the prepared operation's policy/revision with the displayed
baseline before POST. A stale head retains work and sends nothing. The first
empty-slot read can supply an unreserved identity; subsequent current reads accept
optional UUIDv7 `prospectivePolicyId` and echo it only while the native slot remains
empty. An adopted selection always wins. Transport creation supplies this frozen
intent ID to the generated read method and follows the newly returned native HAL
link; it never substitutes a caller-generated path. This avoids the former
identity churn without reservation, mutation, inferred authority or weakening
the displayed-head guard. Five new native vectors remain UNRUN/unbuilt.

Independent HAL booleans expose draft publication and candidate preparation.
Publication is only a publication-instant observation; a candidate never becomes
an adopted/active policy in the page. Rejection, cancellation and uncertainty
retain edits and the exact frozen operation; explicit recovery resends it, while
resuming authoring creates a separate intent. A deliberate latest observation
does not replace the editing baseline until the user confirms. Twenty-nine
editor cases are authored UNRUN/unbuilt, including valid native prospective
echo versus a conflicting response. Labeled controls, linked help/errors,
focus/cancellation handling and logical scoped CSS are source, not an
 accessibility/browser/provider certification.

 Cancelling an inline class, reason or profile editor now returns focus to that
 section's Add button after rendering, through `IAccessibilityFocusService`.
 It does not redirect class/reason editing to the unrelated profile section.
 Three additional real-focus-service/rendered-component cases are authored
 UNRUN/unbuilt; no browser or assistive-technology assurance is implied.

## Native decision adjudication and activation HTTP

The existing typed native lifecycle handlers now have six authenticated POST
adapters under `/api/moderation/decisions/{decisionId}`:
`/proposal`, `/findings/{findingId}/acceptance`, `/approval`, `/rejection`, `/withdrawal` and
`/activation`. Adjudication and activation remain separate route capabilities.
Route decision/finding IDs replace body selectors; subjects and complete target
ancestry are intent, not supplied caller/grant/role facts. Scope/subject JSON
uses the factory-preserving MVC converters.

Handlers still manually validate UUIDv7 operation/effect/reference IDs and
expected decision/policy revisions, then call `ModerationOperationService` on
the existing native fenced store. Finding ownership, current independent human
approval, historical correction authority, complete current activation
prerequisites and the original interval are not weakened by HTTP. Activation
can schedule an existing future interval; it cannot invent missing recovery or
consumer evidence. Native receipt/outbox ownership, exact retries and rollback
remain unchanged.

All six actions retain 16 KiB JSON limits, write rate limiting, no-store policies,
named operation IDs and suppression of generic idempotency response storage.
Success contains only decision/contribution identities and the accepted revision,
never findings, private case material or grants. CommandFailurePolicy preserves
400/401/403/404/409/429/503 ProblemDetails. Thirty-two API vectors are authored
UNRUN/unbuilt: ten invalid route-decision substitutions, two finding-selector
substitutions and twenty actual handler/service denial/unavailability/policy
conflict/missing-decision mappings. Source diagnostics are clean; hosted auth,
generated OpenAPI/client/HAL and native/provider execution remain required.

Proposal now has its own capability-specific adapter. Its `ExpectedRevision`
is the original current open-case revision, not a decision revision; the other
five actions use the expected decision revision. The route owns the new stable
decision ID; case, original rule meaning, capability set, quorum and interval
remain explicit intent qualified by the native proposer/store.
MVC's rule converter calls `ModerationRuleReference.Create`; the proposal
converter calls its defensive constructor so capabilities are snapshotted,
protected remedies cannot become restrictions, and long-term intervals still
require the existing review/quorum rules. Unknown/duplicate terms, including
repeated nullable end/review fields, are malformed JSON. No default quorum,
calendar, new rule meaning or authority is introduced. Twenty-five actual wire
vectors plus six proposal HTTP/service vectors are authored UNRUN/unbuilt.
The proposal extension brought the decision-controller boundary class to 38 cases; the earlier
32-case source receipt does not certify this extension.

The four converter-owned intent shapes also have one shared native/Swagger schema
adapter. Its references retain recursive ancestry, subject origin and original
rule meaning; fields do not expose computed coordinates, grants or private case
facts. Documented capability items exclude protected remedies, quorum is bounded,
and nullable interval terms remain explicit. Native factories and owners still
enforce cross-field validity and permission. Twelve hosted schema/wire parity cases
are authored UNRUN/unbuilt; documentation metadata does not certify enforcement
or activation.
Forced native export and direct component/request inspection now demonstrate the
four typed shapes and proposal references. Swagger execution, generated clients
and runtime authorization remain separate unrun obligations.

Revocation and supersession now have separate bounded authenticated POST adapters:
`/api/moderation/decisions/{decisionId}/sanctions/{sanctionId}/revocation` and
`/api/moderation/decisions/{decisionId}/sanctions/{supersededSanctionId}/supersession`.
Revocation routes own the decision/contribution selectors; supersession routes
own the existing replacement decision and original contribution selectors.
The supersession body names the new sanction, expected old sanction revision and complete
old/new scope intent. Native owners still qualify the current corrector,
historical case/finding/subject ancestry and both jurisdictions; supersession
still requires the existing approved replacement and genuine current prerequisites.
Expired original private custody cannot become a new correction veto or an authority grant.
The controllers add no policy, timing, completion or attribution logic.
Both preserve 16 KiB JSON, no-store/write limiting, native receipt retry semantics
and distinct ProblemDetails. Sixteen new route/failure vectors are authored
UNRUN/unbuilt; the boundary class now has 54 cases. Forced product compilation
and native export passed; both named POSTs have required route selectors and typed
bodies. Hosted auth, native execution, HAL and generated clients remain unverified.

### Correction notices require current access

The maintainer selected current-permission-only correction access on 2026-10-22.
The original notice's recipient identities remain correction obligations; replacing
a reviewer or representative cannot replace those recipients. Original membership
is provenance, not present permission to read the case or its explanation.

Correction disclosure must independently qualify each original recipient's current
case-reading permission, identity, privacy and original finite disclosure bounds.
Loss of permission means unavailable/undelivered, not delivery to a replacement,
historical-role access or a new correction-only grant. This decision creates no
case-body authority, retention extension or deleted-subject verification route.
Canonical revocation still removes only its named contribution; it does not restore
seats or privileges, release independent quarantine, or certify notification/effect
completion. Original-recipient capture and terminal delivery accounting remain
under implementation; the access decision alone does not establish that support.

The current bounded source increment records **pending original-audience
obligations**, not a correction disclosure. The native writer resolves exactly one
sanction-creating Activate/Supersede effect, verifies its retained capture and both
source hashes, and locks and checks the complete original occurrence set. A newer
reviewer cannot enter that set. An expired historical reviewer qualification is not
reused as permission and cannot erase the original obligation. Missing, purged,
expired or inconsistent creating custody remains unavailable; it is not rebuilt
from current reviewers or policy.

`ModerationNoticeCaptureSnapshot.PendingCorrection` carries that immutable anchor
and original recipient tuples. Its deadline is exactly the original source's
still-live retention deadline. A separate Domain factory creates only unavailable
occurrences, with no native intent. There is no approved Notice body, finding
disclosure, acknowledgment reuse or outgoing notification graph. Unavailable here
means **unprepared**, not proof that a particular recipient has lost permission.
Current case-reading permission and independently approved correction text still
need a positive delivery owner. The capture is explicitly substantive, so neither
the inert-effect path nor the active CFG09 completion recipe can claim it complete.

Human revocation now joins the existing privacy-owned transaction path and retains
the same capture recheck at the primary and actual co-located commit boundaries.
The named sanction changes independently of other sanctions, quarantine, seats or
privileges. Original recipient/capture drift aborts the transaction rather than
renewing custody or substituting recipients. Original custody is first checked
through a payload-free retention/hash envelope after the source fences; expiry
cannot authorize reading its private snapshot. Receipt replay proves every pending
recipient from persisted rows, not stale tracked copies, and keeps the original
deadline through the final awaited authority check.

Expiry cleanup now discovers dependents through the canonical creating sanction
and Revoke effects, not their unvalidated JSON anchor or retention timestamp. It
locks the original source before its dependent correction sources, validates the
exact hashes, original audience and unchanged ceiling, and removes only pending
unavailable occurrences and their private source in the original approval's
transaction. Unrelated notices sharing a deadline cannot exhaust that dependency
scan. A divergent anchor/ceiling, unexpected native graph or acknowledgment aborts
cleanup; canonical effects, sanctions and history remain. Independently approved
normal notices retain their separate purpose. A previously cleaned independent
Revoke notice is excluded only with its exact host/writer cleanup proof and
absence of every governed copy. Historical original and dependent source tenant
projections may independently be null or the actual writer tenant; the captured
writer, host, scope, cause, hashes and deadline still have to match exactly.
The existing native notification
cleanup owner's soft-delete limitation remains a separate, source-identified
blocker: a retained native intent returns unavailable and rolls the entire purge
back rather than falsely reporting removal. This change does not claim that owner
can purge portal/email graphs.

145 cases are authored **UNRUN/unbuilt**: 98 contract/factory, 17 native
source/writer and 30 cleanup cases. These include exact expiry during a fence
wait or an awaited recipient read, missing-recipient receipt replay, post-save
rollback with separate and co-located privacy authorities, independent
same-ceiling saturation, divergent row/JSON anchors, nullable historical source
projections and proven earlier independent cleanup. Rollback checks preserve both
original and pending occurrence sets. The creating activation ledger in the native fixture is
independently seeded, not measured activation-writer evidence. Domain compilation
passed with 149 warnings and no errors in 5.22 seconds; Application compilation
passed with 182 warnings and no errors in 36.82 seconds. Initial Persistence
compilation found three missing-namespace errors; the namespace was corrected.
An intermediate corrected Persistence build passed with 62 warnings and no errors
in 3:12.38, before the latest deadline and cleanup corrections. Final Persistence
compilation passed with 62 warnings and no errors in 4:01.45. Bounded expiry,
replay and final cleanup source reviews found no additional concrete defect after
the recorded corrections; these are not executed lifecycle or concurrency proofs. Roslyn
diagnostics are unavailable; no tests, provider/runtime assurance or database
application has been performed. This partial increment does not close the
correction workstream or establish current-permission correction delivery.

### Adjudicated-case read grants — separate positive permission

The maintainer approved a distinct finite, revocable read-purpose grant for a
named Human, issued by a currently qualified administrator of the case's custody
tenant. `ModerationAdjudicatedCaseReadGrant` does not reuse allegation-only
`ModerationCaseReadGrant`, issue an allegation/material receipt, or create a
correction-only exception for former reviewers. Subject, reviewer, organizer and
administrator status alone does not make someone the named recipient.

The native owner authenticates the creating Activate/Supersede effect and its
original snapshot and effect-binding hashes, the adjudicated case revision, the
original custody writer and host, and the original notice's finite retention
ceiling. A payload-free envelope is checked before the private snapshot; the
clock is checked again after awaited reads. The grant cannot outlive that ceiling
or be renewed by replay. Historical audience and reviewer qualification are not
used as current permission.

Case custody and decision jurisdiction remain distinct: an attributed Tenant
case may have a Resource decision whose immediate parent is that exact Tenant.
The grant authenticates this existing canonical relationship at acquisition and
terminal recheck; arbitrary containing or unrelated scopes do not qualify.
The authenticated original retention instant is preserved at full tick precision,
including a sub-microsecond ceiling. Only newly issued grant, expiry and revocation
instants use the grant contract's microsecond precision; the original purpose is
neither rounded nor extended.

Acquisition discovers the complete Human set before user, identity and role
fences, then qualifies the host and tenant. Current named-recipient access also
requires the original issuer's exact still-current administrative role and the
recipient, issuer and Human case subject's retained privacy boundaries. A
replacement role does not inherit the original grant. Revocation instead uses
the current administrator and can operate after the private purpose expires.
Both normal primary commit and the co-located privacy owner's final commit use
the same terminal recheck; failed settlement must roll back rather than publish
a permission prefix. Native read results are permission metadata, not body output
or a transferable bearer token.

Grant settlement freezes the intended mapped row values before the first save.
`ExploreDbContext` binds that exact grant object and original transaction through
the enrollment owner's later save and final native recheck. Each guarded save
checks ownership before preparation, after native recovery preparation and after
the save interceptors return. A substituted recipient, expiry or additional
tracked grant aborts the transaction instead of becoming the accepted snapshot.
The lease remains active through the co-located privacy owner's final check.
The adjudicated owner also binds its exact, already qualified
`ModerationQualificationEpoch` as an unchanged observation, with all mapped values
frozen. Only that object is admitted: changing its revision, replacing it or
detaching it aborts settlement. This does not authorize qualification writes or
admit arbitrary unchanged tracked rows.
The prior substitution and additional-write regressions compiled remotely but
failed before their settlement probes were reached. The new qualification
mutation/replacement cases remain unbuilt and unrun; this mechanism still
requires successful remote provider verification.

Two supplementary native source cases exercise issuance by an actual custody
TenantAdmin rather than a platform administrator, then loss of that exact role
or its active membership. Reads and exact issuance replay must become unavailable
without rewriting the retained grant. Both cases are UNRUN/unbuilt; bounded
source review found no concrete fixture defect, not runtime authority proof.

Two authenticated write routes under
`api/moderation/adjudicated-case-read-grants` grant or revoke this purpose. They
return only the committed grant identifier. The ordinary allegation grant routes
remain unchanged; no public read/body endpoint is added. Expiry cleanup removes
all grant metadata bound to the original effect/host/tenant/trust before original
custody disposal, including revoked grants. The existing exact-saga erasure sweep
also removes grants naming the erased Human as recipient, issuer or revoker.

Source integration and assurance remain in progress. After the original-retention
precision correction, Domain compilation passed with 149 warnings and no errors
in 5.02 seconds. Application compilation passed with 182 warnings and no errors
in 46.77 seconds. Initial Persistence compilation exposed five symbol/member
errors, now corrected without suppressions. The canonical attributed-case scope
correction compiled with 62 warnings and no errors in 2:46.70. Follow-up source
review found a separate construction-time expiry race: checking one clock instant
and constructing with another could throw instead of returning unavailable.
Construction now validates and uses one captured issue instant, while retaining
fresh terminal deadline checks. Its additional native regression is authored but
**UNRUN/unbuilt**. That follow-up compiled with 62 warnings and no errors in
2:41.65; bounded review found no additional construction defect after correcting
the regression's trust-filtered persistence assertion. This is source review and
product compilation, not an executed regression or provider assurance. The new
Domain, validator, model/native-owner and retention/erasure regression sources
total 189 **UNRUN/unbuilt** cases. API compilation and native schema publication
passed with 2 warnings and no errors in 54.40 seconds. Generated browser-client
compilation passed with 230 warnings and no errors in 52.73 seconds; Release
schema, client and mutable-policy captures exactly matched their source artifacts.
The native generated-contract tool was explicitly prebuilt without restore and
supplied through `GeneratedContractToolAssembly`; generation was not disabled.

Native `AddAdjudicatedCaseReadGrants` migrations were generated for PostgreSQL
(`20261022194142`), SQLite (`20261022194303`), SQL Server (`20261022194317`) and
MySQL (`20261022194338`); MariaDB shares the MySQL family. None was hand-edited or
applied. Bounded static review found no attributable grant-table defect: exact
retention ticks, ordinal hashes, accountable revocation shape and the four
Restrict relationships remain represented. Generated migration assemblies compiled:
PostgreSQL with 62 warnings and no errors in 3:06.94; SQLite, SQL Server and MySQL
with no warnings or errors in 20.27, 30.88 and 22.37 seconds respectively. Native
bounded Up/Down SQL generation from `ImmutableRegistrationProviderBindingPublication`
completed for all four families without database execution. Artifact inspection
preserved the precise original retention ticks, the separately normalized new
instants, ordinal hashes and exact grant relationships. SQL files are under
`.artifacts/migrations/AddAdjudicatedCaseReadGrants/`; emitting them is not executing
their checks or proving provider behavior. MariaDB shares the MySQL migration
family but has no separate engine witness here. The existing
SQL Server CaseEvidence rollback discrepancy is corrected separately at the
native SQL-generator boundary: the named evidence FK retains its physical
predecessor's `NO ACTION`, without rewriting migration history or the approved
private-PII cascade. These artifacts still do not certify that downgrade or
provider behavior; see `BACKUP_RESTORE_UPGRADE.md`.
This permission owner does not implement independently approved
correction text, disclosure, delivery, effect completion or deleted-subject
claimant verification.

### Correction-text approval — separate from access and delivery

`ApproveModerationNoticeCorrectionDisclosureSource` is a distinct native
qualification mutation for an already accepted Revoke with a pending original
audience. It does not relax the ordinary disclosure-approval writer's prohibition
on approving an effect that already exists. The new writer authenticates the
creating effect, original custody hashes, complete unchanged audience and pending
capture, and requires the current custody administrator and exact case/decision
revisions. It also verifies the accepted canonical Revoke receipt.

New text must qualify against the original policy, publication, profile,
contribution and selection digest. Historical approval expiry is not itself a
current-permission check, but neither historical metadata nor the new approval
grants anyone case access. If that original policy cannot qualify, the operation
is unavailable; a replacement policy is not silently selected. The intent must
contain an explicitly empty representative list: original recipient obligations
come only from authenticated custody, never from the new author's recipient list.

The approval retains exactly the original custody ceiling, with a finite validity
within it. It does not restart a six-calendar-month interval. Fresh writes, exact
replay and conflict recovery retain source rechecks through the actual primary
commit and the outer co-located privacy commit. Only an approval, its qualification
receipt and the native epoch may change. Pending captures and recipient
occurrences stay unchanged; no body is returned, notification created,
acknowledgement accepted or effect marked complete. Current recipient permission
and subsequent disclosure/delivery remain separate, unfinished owners.

Original source and approval bodies are queried only after a fresh check of the
original custody ceiling, including after lock waits; post-read and actual-commit
checks remain. A separate typed-receipt proof identifies correction approvals for
purpose cleanup, not for disclosure. Cleanup joins a correction-only due page to
its original creating approval and removes the correction approval with its
pending source and occurrences at that same original ceiling. Expired validity or
revocation does not prevent lawful cleanup, and cleanup does not complete the
Revoke effect or create an original-notice completion marker for it.

One hundred and one cases are authored **UNRUN/unbuilt**: 40 validator, 27 native
approval, two private-read expiry, 31 cleanup and one attributed-case proof case. The historical creating
capture is factory-seeded and the fixture publication is selected Draft history,
not measured activation or runtime conformance. Application compilation passed
with 182 warnings and no errors in 1:00.08. Initial Persistence compilation found
two missing-namespace errors, corrected without suppression; the combined build
then passed with 62 warnings and no errors in 2:44.98. Subsequent source review
corrected Instance-scope custody projection handling and required original-typed
disclosure provenance even when cleanup discovers a full Revoke notice through
an original-only page. Three first-authored cases guard those corrections;
bounded follow-up found no additional defect. The proof now also preserves the
existing attributed Tenant-case/Resource-decision relationship only when that
Resource's parent is exactly the case's Tenant scope. Its first-authored native
case exercises the retained proof, not the approval writer or cleanup paging.
Narrow follow-up found no further production defect and identified a missing
namespace import in that new test, now corrected without executing or building
the test project. Final Persistence compilation passed with 62 warnings and no
errors in 2:40.31; no production source changed after that build launched.
Bounded expiry review found no further
attributable defect, not runtime assurance. Roslyn diagnostics remain unavailable.
This source slice has no new HTTP endpoint, entity or migration and is not
completed correction delivery.

### Self-only transient correction preparation — source integration in progress

The notice read owner now has a separate internal preparation operation. It joins
the authenticated named Human's current adjudicated-case grant with the original
recipient occurrence and independently approved correction text. A valid grant
held by a replacement reviewer cannot expand the original audience; subject,
reviewer and administrator identity alone cannot replace personal permission.
There is no on-behalf machine or administrative batch lane.

The grant acquisition can borrow the caller's actual Serializable transaction
without saving, clearing tracking or owning settlement. It discovers the complete
Human set before acquiring identity, role, host and tenant sources, then checks
the exact original issuer role, case generation, privacy fences, custody hashes
and finite grant interval. Public grant reads retain their separate owned
transaction contract. Shared correction-source and selected-policy qualification
preserve the original-before-dependent source order; the old approval writer and
cleanup proof retain their distinct responsibilities.

Preparation checks current unrevoked text approval, its exact typed receipt,
original policy meaning and current source generation. It retains only an approval
digest and the earliest grant, approval and original-custody deadline. Both the
native primary owner and the outer co-located privacy owner repeat the join before
their actual commit. Known consumer deadlines are checked before revalidation and
are carried through the subsequent preparation requalification and private-custody
reads, including after awaited envelope queries. Reading the current approval's
envelope can shorten that bound, never renew it.
No body leaves the operation, and no notice, intent, acknowledgement, receipt,
epoch or completion is created. A true result is transient preparation, not output
or delivery permission after that acquisition ends.

Sixteen preparation and eighteen borrowed-read source cases are authored
**UNRUN/unbuilt**; the latter include grant expiry and a shorter consumer deadline
crossing at the nonprivate custody envelope. Two preparation cases separately
cross the approval deadline during audience requalification, after bounded grant
revalidation, under separate and co-located privacy ownership. Two supplementary
TenantAdmin cases are recorded separately from the historical grant batch.
Initial product-only Persistence compilation failed with 62 warnings and one
missing `ModerationPolicyPublicationProjection` namespace error in 2:32.62; the
actual application-contract namespace is now imported. The corrected batch is
compiled: Persistence passed with 62 warnings and no errors in 2:27.48, with no
production changes after launch. Narrow follow-up source review found no further
attributable material defect in the deadline corrections or new cases; this is
not test execution or provider/runtime/MAD assurance. Roslyn is unavailable, not
a clean diagnostic snapshot. No new public endpoint, entity, migration, transport
or body-output owner is introduced here.

### Self-only correction explanation callback — source integration in progress

The concrete native notice reader now has a separate internal correction-output
operation. It does not turn preparation's boolean into reusable authority. Each
invocation acquires the actual named original Human's current adjudicated-case
grant again, joins the independently approved correction text to the original
recipient occurrence and authenticated source hashes, and retains the earliest
grant, approval, qualified-source and original-custody deadline.

The callback borrows a zeroed-on-exit UTF-8 buffer containing only the approved
rule, reason, facts, remaining actions, recourse, automation and retention text.
No identifiers, approval entity, original allegation or narrative are projected.
Serialization is followed by full authority/source requalification before the
callback starts. The same recheck also runs after the callback and at the actual
outer privacy-owned co-located commit. A separate primary transaction is settled
only after its own final recheck.

External output uses a correction-specific EF execution boundary with zero retries,
not the retrying provider strategy, bootstrap retry or unit-of-work wrapper. This
single attempt spans the entire privacy operation, including its co-located
commit, and suppresses nested provider retries while allowing the owned native
transaction. An already-active retrying EF strategy is rejected before acquisition;
re-entry is also rejected. Other operations keep their configured provider retries. Once the
callback has started, consumer, cancellation, authority-loss or settlement errors
propagate: bytes already consumed cannot be recalled, and failure must not be
reported as though no disclosure occurred. The owned byte buffer is zeroed on
every exit; it is not a caller-retainable authorization lease.

This is an internal self-only callback, not an HTTP endpoint, portal/email
dispatch, on-behalf administrator/machine operation, acknowledgement or original
audience completion owner. It creates no logical notice, intent, receipt, epoch
advance or effect-completion record. Twenty-one new native cases are authored
UNRUN/unbuilt: fifteen callback cases, one ambient-transaction rejection case
and two normal/co-located expiry-during-consumption cases, plus two retry-enabled
success cases and one retrying-outer-operation rejection case. They cover the exact
approved JSON projection, original identity/current authority, private-read
deadline crossings, borrowed-buffer zeroing, consumer failure/cancellation,
non-replayed settlement failure and unchanged logical ledgers. These are direct
callback cases, not HTTP/email or all-audience delivery evidence; historical
activation remains factory-seeded.

Initial Persistence compilation passed with 62 warnings and no errors in 2:31.34.
Fixture review then exposed an EF transaction/strategy incompatibility: the raw
transaction was rejected before the commit-fault cases could reach output. Production
PostgreSQL also enables retries, so the source boundary was corrected rather than
weakening that fixture. Focused read-only review found no additional material
source defect in the one-attempt correction. Corrected Persistence compilation
passed with 62 warnings and no errors in 2:37.76; production source was unchanged
after that build began. This is compilation, not executed provider evidence. The
additional callback case covers cancellation under actual co-located privacy
ownership. All cases remain unrun/unbuilt. Roslyn diagnostics remain unavailable. Deadline checks bracket the
awaited consumer; its cancellation token is not linked to the authorization
deadline and cannot forcibly interrupt a consumer that ignores cancellation.
Bytes already consumed cannot be recalled. No runtime/provider assurance is claimed.

The maintainer selected dedicated self-only JSON HTTP transport on 2026-10-23.
`ModerationNoticeCorrectionController` exposes
`GET /api/moderation/notice-corrections/{originalNoticeId}` with seven explicit
query selectors: pending effect, its two expected hashes, approval, named grant,
case and case revision. There is no caller-selected recipient or tenant. The
Application query manually validates intent and checks native authentication,
but performs no private storage read. Its closed pending result captures only
immutable coordinates, the disclosure port and cancellation; single-use consumption
links query and HTTP execution cancellation. Coordinates and preparation do not
grant permission.

`ModerationNoticeCorrectionHttpResult` invokes the same scoped native notice reader
only during actual result execution. The reader writes the seven-field
`ModerationCorrectionExplanation` projection directly; there is no deferred MVC
formatter holding private text after custody. Both response writing and awaited
`CompleteAsync` are inside native disclosure. Request cancellation aborts output;
once writing is attempted, exceptions propagate and abort rather than replacing
the response with ProblemDetails or replaying the consumer. Already-consumed bytes
cannot be recalled. The native owner still clears its borrowed byte buffer only
after its primary/privacy settlement path has finished.

PrivateNoStore endpoint metadata bypasses the ETag middleware's buffering path;
OutputCache is disabled, and the result sends private/no-store, no-referrer and
nosniff headers. Invalid intent is 400, missing authentication 401, unavailable
qualified disclosure 404, without an existence oracle for particular failed grant
or custody checks. The original-notice representation and its acknowledgement
semantics are not reused. There is no automatic HAL discovery, portal/email
fanout, administrator/machine on-behalf lane, delivery acknowledgement or
whole-original-audience/effect completion.

Eighteen pure query cases, eight native pending-result/HTTP completion cases and
one actual ETag-boundary regression are authored UNRUN/unbuilt. The native HTTP
source checks include separate/co-located custody through blocked completion,
cancellation propagation, exact output projection and unchanged logical ledgers;
they are not executed endpoint or provider conformance. Initial Application
compilation passed with 183 warnings and no errors; its new cancellation-token
parameter-order diagnostic was corrected without suppression. Corrected Application
compilation passed with 182 warnings, no errors in 37.15s, and Persistence with 62
warnings, no errors in 2:46.97. Focused product review found no additional causal
defect; the native HTTP privacy-gate witness was moved into the disclosure's own
async execution context rather than querying its AsyncLocal gate from the awaiting
test. API compilation passed with 2 warnings, no errors in 21.52s; the native
generated client compiled with 230 warnings, no errors in 57.20s. The generated
OpenAPI route contains the eight explicit selectors in addition to the standard
API-version query/header parameters, and its versioned JSON success schema has
exactly the seven approved fields. Source schema, generated client and mutable
policy match all three Release captures. The initial JSON assertion incorrectly
ignored version parameters/media types; correcting that check required no schema
or product change. Support-only fixture extraction remains in progress; no whole
persistence suites are linked into the API test lane.
Roslyn diagnostics are unavailable. No tests/test-project builds, migrations,
database application, staging or commits were performed for this transport.

## Purpose-separated recognition contribution coordinates

`ModerationRecognitionContribution` retains only an eligible final human
decision/contribution reference, source revisions, rule, original jurisdiction
and interval, a purpose-specific capability subset and a finite retention
deadline. Action continuity excludes enrollment, admission and private logistics;
enrollment-purpose coordinates retain only the original corresponding powers.
They cannot widen a resource exclusion into an instance enrollment ban.

Capture rejects `Never`, ineffective/future/expired/revoked contributions,
nonhuman subjects, mismatched decisions and empty purpose-specific capability
sets. Rechecking rejects reversal, supersession, changed original meaning and
half-open enforcement/retention expiry. A retention deadline does not extend
enforcement, and a current-match comparison does not restore unrelated rights.
This shared Domain value stores no provider identifier, contact, DOB, profile,
role, HMAC key or token. It does not prove identity matching, native privacy
adoption, durable computed capture, erasure settlement or recovery readiness;
those actual owning mechanisms remain mandatory before capture is enabled.

## Canonical recognition token computation

`ModerationRecognitionCapturedMaterial` pairs the complete original token,
material verification tag and purpose-limited canonical contribution before
input destruction. Construction rejects cross-trust/purpose or malformed tags.
Current comparison requires the original provider/key ID/generation/version,
constant-time digest and material-tag equality, and the shared contribution's
current decision/sanction revision, scope, capability and interval checks.
Reversal, exact expiry or retention loss defeats that individual contribution;
no independent restriction or original rights are restored.
This immutable value is not a durable pending-store receipt, settled erasure,
identity proof, adoption or permission. The native saga must persist all computed
items and qualify current sources before purge. The native capture ordering
below now supplies those stages; admission and continuity consumption remain open.

`ModerationRecognitionCaptureSet` snapshots the computed items for one original
UUIDv7 erasure operation, trust and finite ring generation, with one exact writing
key ID/generation/version/material tag, common original capture instant and
finite settlement deadline within every item's retention. Empty, null, duplicate,
mixed-key/material/trust or differently timed items reject as a whole. Distinct
action/enrollment purposes and provider-qualified tokens remain separate. The
snapshot cannot prove that the native eligible-contribution/provider inventory
was exhaustively covered, that a store committed, or that erasure settled.

`ModerationRecognitionKey` adds the native computation primitive without
registering or enabling a capture service. An ephemeral 32-byte key is supplied
by the selected secret authority, snapshotted and zeroed on disposal. The
computed `ModerationRecognitionToken` retains only trust-domain UUID, provider
kind, purpose, key ID/generation, serialization version and HMAC digest. Its
constructor rejects unsupported provider/purpose/version, ambiguous key IDs and
noncanonical digests. Native identity, policy, sanction and secret authority
must still be qualified by the eventual capture owner.

Version 1 uses HMAC-SHA-256 over ordered UTF-8 fields, each prefixed by a
four-byte big-endian byte length: `ISLAMU.Moderation.Recognition.Account.v1`,
`final-action` or `final-enrollment`, canonical trust UUID, key ID, invariant
decimal key generation, invariant decimal provider kind and the exact opaque
`ProviderAccountKey.Value`. The trusted principal/binding adapter remains the
only provider normalization authority; issuer path/subject case and DID
semantics are not reinterpreted. Strict UTF-8 rejects malformed surrogates
instead of collapsing them into replacement identities. The distinct
`ISLAMU.Moderation.Recognition.Key.v1` verification domain binds trust, key ID
and generation without producing an account token. Temporary encoded buffers
and digests are cleared; neither token nor key `ToString` exposes their values.
No ring provisioning, rotation or matching is supplied by this primitive, and
erased inputs cannot be rehashed.

## Durable recognition capture before erasure

`ModerationRecognitionErasure` is the scoped coordinator used by
`RetainedAuthorityPrivacyErasureWorkflow` and `PrivacyErasureApplier`.
It reuses the selected `IPrivacyIdentityFenceAuthority` instance and ordered
recognition-setting lock, without creating an administrator acquisition.
`ModerationRecognitionErasureRepository` qualifies the erasure subject, current
final canonical contributions, original policy and native privacy-source/adoption
mutation receipts under their writer fences.

The primary store first commits a `ModerationRecognitionPending` received
request with its original operation, receipt hash, independent privacy/recovery
owners and finite settlement/escalation deadlines. A separate stage computes and
commits every purpose-qualified `ModerationRecognitionCaptureItem`: original
provider-qualified digest, key generation/version/verification tag, source and
adoption generations, sanction/decision references and revisions, exact original
scope/end/review and retention. Neither original provider bytes nor profile,
contact, DOB, accusation history or key bytes enter these rows. An empty marker
cannot become a computed-material receipt.

Before authority append, a new identity acquisition independently verifies the
committed batch and complete current contribution/provider inventory using the
original readable key. Its append lease holds the primary canonical/source fences
through the retained authority operation. Purge independently rereads the exact
retained fact and computed receipt before destroying original inputs. After that
fact exists, recovery needs only persisted complete material and current
canonical contributions, not destroyed identifiers or the old key ring.
Reversal/expiry drops only the affected item's eligibility without rewriting its
original receipt, scope or interval.

Erasing a subsequently matched subject also captures its completed continuity
bindings alongside direct sanctions. `SubjectId` always identifies the original
canonical decision subject; `ErasureSubjectId` identifies this request's owner.
The recaptured item records its exact parent item, continuity binding and finding,
and retains `OriginalCapturedAt`, original scope/capabilities/revisions/end/review
and `RetainUntil`. Only newly verified native external bindings provide alias
inputs; Local credential UUIDs and unresolved action-recovery candidates do not.
Append, purge and later keyless reads revalidate the complete ancestor receipt/
fact/binding chain. Missing or changed provenance is unavailable, while dropped
or expired ancestors stop only their dependent contribution. No canonical
sanction is reassigned or reissued and no retention clock restarts.

Ordinary erasure bypasses recognition computation and requires no ring. A
received qualifying request without complete material remains unsettled; failure
records preserve the original deadlines and progress to owned escalation or
lawful-disposition-required state rather than extending retention or claiming
completion. Replay advances those deadlines even when complete computed material
already exists but the retained authority fact has not settled. A retry or earlier
clock cannot downgrade an observed obligation; successful capture after escalation
does not clear it. Deadline observation preserves the receipt and invents no failure
cause. An authority-bound or independently no-longer-required receipt cannot regress
to an unsettled failure. Delivery of those operational dispositions, native
enrollment/linking/Local runtime assurance, readable-key rotation/replica/backup obligations,
generated schema and final assurance are still programme requirements. This
capture unit alone does not establish protected admission or measured conformance.
Receipt status reads use the current finite deadline observation even before a
replay pass persists the transition. This read does not mutate the receipt,
notify an owner or certify that a lawful disposition has been executed.
Each received-request replay also runs one batch of at most 100 expired capture
items, even when no received requests remain. The native identity gate precedes
subject, pending-receipt and item row fences. Exact original sanction end or
matching-retention ceiling drops eligibility without original inputs or keys;
the immutable material receipt, original interval and authority binding survive.
A failed write rolls back and clears only this operation's candidate tracking.
This bounded pass is not record deletion, reversal reconciliation, key retirement
or proof that replicas and backups have reached a safe retention floor.

## Native recognition matching and continuity references

The scoped `ModerationRecognitionContinuity` service exposes separate matching
and read-only terminal ports. Both use the same scoped native
`ModerationRecognitionErasureRepository`; the durable
`ModerationRecognitionContinuityBinding` filter remains deny-default.
Bindings reference one original captured action contribution and a fresh subject,
not cloned/reassigned sanctions or restored profiles, roles or accusation history.
The capture reference uses restrict-delete behavior.

Matching requires an outer recognition-setting lease, the selected retained
identity gate, then the original serializable primary transaction before subject
and source row fences. The trusted native adapter supplies the exact authenticated
`ProviderAccountKey`. Actual readable key generations recompute the distinct
purpose digests. Complete settled material, canonical case/final finding/sanction,
original source/adoption mutation receipts and current ownership are independently
qualified. Only a covered enrollment-purpose contribution excludes replacement
enrollment; action continuity never becomes blanket account exclusion.
Unsettled affected subjects fail closed, including the keyless existing-UUID
guard. A new Local UUID or unrelated provider identity proves no unique person.

Terminal consumers use `IModerationRecognitionContinuityReader` inside their
existing serializable tenant transaction. It acquires no identity/settings/write
locks after resource locks and performs no identifier recomputation. Every read
rechecks the original contribution and receipt authority; minimized results carry
generation and current-until boundaries for the owning consumer's exact capability,
target scope and policy decision. A binding alone grants no permission.
`EventResourceModerationFacts` now carries a finite optional `CurrentUntil`.
The existing native resource evaluator checks it at every authority sample,
including post-preparation, final header handoff and mutation recheck. Record
equality includes the lifetime, so a changed bound invalidates preparation even
if its generation hash stays unchanged. This is an actual terminal-evaluator
guard; native runtime and provider assurance remain unrun.
`EventResourceModerationSnapshotReader` now composes that production for Human
subjects under an adopted resource-read declaration. Unique read targets are
qualified in chunks of at most 500 within the original transaction; repeated
metadata/payload checks share their result. Only matching capability and original
scope enter the target's result. Applicable uncertainty closes that target;
an unrelated capability or sibling target does not borrow the failure or lifetime.
Surviving contributions add denial to the canonical overlay, never native grants.
Generation and current-until flow into the frozen authority facts.

The same producer bounds upcoming starts/ends of directly applicable canonical
sanctions and urgent protection, using exact subject and target scope. A future
boundary forces a fresh authority sample rather than treating an earlier
unrestricted outcome as indefinitely valid. Review dates remain review duties.
Native no-capture resource fixtures use the actual continuity service/repository;
only unavailable external key/retained-authority ports are doubled, and any
capture-bearing fixture must use its genuine authority.

Canonical review dates remain review obligations, not invented sanction expiry.
Continuity preserves the original review/end metadata and follows the canonical
sanction's effective state until expiry, reversal, supersession or retention loss.
SQLite capture-expiry and matching interval predicates use the existing native
exact-instant collation pattern on the actual connection, preserving 100ns bounds
without client-side table scans or schema conversion.

Resource-read composition does not integrate enrollment or Local/linking writers.
Their outer ordering and commit-before-current-until
contracts remain mandatory. Later erasure of a replacement subject carrying bound
contributions also needs explicit capture coverage for newly linked provider
identities. Restore-floor closure, operational dispositions, retirement,
generated schema and final programme assurance remain open.

## Qualified legal erasure integration

The native erasure owner now distinguishes a separately qualified legal disposition
from complete matching material. Compute checks the exact accepted or consumed
disposition before changing the pending revision. The append lease explicitly
selects that branch, and binds the authentic retained fact through the disposition
owner rather than `Pending.BindAuthority`. Purge independently verifies the fact
and that separate consumption; it never labels unavailable matching completed.
Received-request scanning excludes only complete consumed source coverage with
the same fact sequence, while actual use still verifies the retained fact.

All original contributing sources require independent qualified owner coverage;
one earliest request owner cannot release other sources' obligations. The original
deadline and pending material remain bound to each decision. Immutable accepted-owner
receipts preserve both authenticated operations and the real retained authority
high-water observed at authorization. Recovery locates the exact committed fact
after that floor and validates the complete original authorization at its recorded
timestamp. Subsequent expiry/withdrawal therefore does not strand a committed
append or require destroyed inputs/keys. Discovery is snapshot-only before the
original ordered capture anchors; actual consumption retains its required fences.
These source mechanisms are not runtime or completed-erasure assurance.

`ModerationRecognitionCorrectionReceiptAuthenticator` checks protected legal
correction receipts under a separate purpose. It uses the exact original receipt
hash and finite protected calendar/record lifetime, so source withdrawal or a
shorter status lifetime cannot close the remedy floor. Ordinary
`RetainedAuthorityPrivacyErasureWorkflow.AuthenticateReceiptAsync` still expires
with its saga/pending status credential; correction cannot extend account or
status access. The native repository is shared across erasure, continuity
and recovery ports; required concrete recovery notification composition is
registered. Privacy replay now schedules operational delivery after pending
matching/erasure reconciliation, outside the old transactions. Notification
settings precede native fences, obligations commit before delivery, and actual
required in-app graph delivery is verified before marking delivered; an owner
acknowledgement is a separate operation. Native owner-scoped HTTP/CQS surfaces are
implemented; generated schema/client and real-provider assurance remain open.
Four parent native legal cases are authored but
unrun/unbuilt under the programme verification deferral.

### Source-scoped recovery operations

`ModerationRecognitionRecoveryController` delegates to the native operation graph.
Its GET actions preserve the repository's `[AllowAnonymous]` convention, but the
handlers require the authenticated, currently qualified named recipient. The
operator context qualifies current source/adoption and pending revision in the
native transaction; a notice's captured revision is not current mutation authority.
The context exposes only that source's coordinates/options and an aggregate source
count. Assessment and independent authorization use those expected revisions;
acknowledgement requires actual delivered notice ownership. HAL policies publish
only the recipient's current affordances; handlers independently enforce writes.

The route family `/api/moderation/recognition-recovery` provides:

| Method | Relative route | Operation |
| --- | --- | --- |
| GET | `/escalations` | Bounded recipient notice list |
| GET | `/escalations/{id}` | Exact recipient notice |
| GET | `/escalations/{id}/context` | Current source assessment coordinates |
| POST | `/requests/{intentId}/sources/{sourceId}/assessments` | Recovery-owner assessment |
| POST | `/assessments/{id}/authorize` | Independent privacy-owner authorization |
| POST | `/escalations/{id}/acknowledge` | Delivered recipient acknowledgement |

`GET /api/privacy-erasure/recognition-corrections` explicitly authenticates
`ModerationRecognitionCorrectionReceipt`, then reauthenticates the original
receipt in its CQS handler. Send it only as
`Authorization: ErasureCorrectionReceipt <original-receipt>`. Returned facts omit
stored proof digests. The scheme grants neither account nor ordinary status
authority. `ModerationRecognitionCorrectionOpenApiSecurityTransformer` documents
that mandatory scheme despite `[AllowAnonymous]`, for both native OpenAPI and
Swagger. All three response DTOs participate in the HAL schema catalog.
These source connections do not constitute executed HTTP or provider assurance.

## Native key lifecycle and finite cleanup

### Protected original notice and notification clock

For Event-owned Content, `ModerationNoticeDisclosureIntent.ContentRepresentatives`
selects at most 32 exact native Event owner/manager assignments, revisions and
human recipients. The actual disclosure writer and final flush qualify current
Event management, canonical human, active tenant membership and primary erasure.
The committing capture retains that exact selection, a separate
`ContentRepresentative` occurrence per human and the original Content occurrence.
Mutating Content lifecycle operations and disclosure approval enter selected
`ExecuteEnrollmentSerializedAsync` before primary authority acquisition. The
existing provider execution strategy owns the whole attempt. If embedded
authority adopts the primary transaction, the writer neither begins nor commits
another transaction; its retained callback revalidates after disclosure flush
at the authority's actual commit. Separate-primary deployments commit their
owned serializable transaction before returning from the retained callback.
Fresh attempt state and exact receipts recover a transient uncertain commit
without recreating sources or native notification graphs.

The final lifecycle callback compares persisted effect/source/capture and receipt
coordinates, requalifies original assignments against both primary and retained
erasure, and repeats grant/deadline/retention checks after asynchronous reads.
Approval final validation also compares its actual receipt, qualification epoch,
immutable approval and original representative cutoff. Unique-conflict approval
recovery uses the same ownership branch. Abort outcomes propagate through
enrollment before being translated, so a shared transaction cannot commit partial
work after a failed final check. Opaque lifecycle replay proves the original
atomic write and does not reacquire historical disclosure custody; approval
replay still requires its explicitly current original custody.

`ModerationNoticeContentCustody` uses the existing commit-bound Event authority
owner; an unrelated administrator, submitter, reviewer or replacement assignment
is not inferred as a representative. Empty, stale or cross-purpose selection
cannot produce a substantive Content capture.

Original Content response custody is separate from human finding evidence.
`ModerationContentResponse` retains the Content subject, original representative
occurrence and exact Event assignment revision, with at most 8,192 UTF-8 bytes
protected by the existing Data Protection authority. Its row-specific purpose
binds original host, tenant, effect, notice, case, Content, representative,
assignment/version, snapshot and finite privacy ceiling. The server never puts
plaintext in finding evidence, delivery intents, generic idempotency storage or
telemetry. The response row and submission receipt are immutable and share the
original ceiling; original cleanup or the representative's native all-tenant
erasure physically deletes this custody.

`IModerationContentResponseRepository` uses the same scoped native qualification
owner. Availability and submission acquire selected retained privacy custody
before primary authority, check original ownership/R metadata before private
snapshot selection, and requalify the exact original assignment through primary
commit and an authority-adopted final flush. Availability has a deferred single-use
HAL projection; `POST /api/moderation/notices/{noticeId}/content-response` takes an
opaque UUIDv7 operation identity, original assignment/version and snapshot hash.
Exact retries preserve the original response; a changed statement conflicts.
Submission neither acknowledges notification nor changes a decision, finding,
sanction, quarantine cause or whole-recipe completion. The actual acknowledgement
receipt supplies calendar-month metadata; an earlier response does not invent a
notification or renew R.

`GET /api/moderation/content-responses/{responseId}/statement` uses a native CQS
single-use result, not a plaintext DTO. Current independently qualified
case-reviewer assignment, native permissions, controller/conflict evidence,
current procedural source and separate adoption are required; an administrator
or the original representative cannot borrow reviewer status. Response ancestry
is checked against the original source's retained hashed metadata, without
reopening the allegation snapshot. Selected retained erasure and primary native
fences stay held through decryption, final requalification and the awaited HTTP
write. The response is private no-store/no-referrer/nosniff UTF-8 plain text.
The source owner does not retry external output, does not translate a transport
exception into success, and zeroes its plaintext buffer on success or failure.
The original R and current reviewer/policy expiry remain hard ceilings. Reviewer
UI, actual independent review disposition, generated provider/wire
artifacts and final runtime assurance remain programme obligations; possessing
a response receipt alone does not finish the original consequence recipe.

Qualified reviewers discover exact response identities through
`GET /api/moderation/cases/{caseId}/content-responses`, rather than receiving
statement bytes as a side effect of opening a case. Its cursor page contains
at most 64 `ModerationContentResponseSummary` entries: response/case/effect
identities, original submission/R and current qualification cutoff. Neither
representative identity, ciphertext nor original snapshot is selected for this
metadata path. The full page's representative set is discovered before the
first ordered human/host/tenant/epoch acquisition; each original is independently
qualified against its current reviewer, native permissions, procedural adoption,
hashed ancestry and retained erasure. HAL preparation occurs inside that custody,
and the owner repeats all proof and cutoff checks before the awaited output.
Any unavailable original or changed authority closes the whole prepared page,
not only one statement link. Empty/unavailable pages are the same concealed 404.
The `statement` link is current navigation, not historical permission; opening
it still runs the independent statement owner. Collection/detail policies, native
CQS, concrete HAL registration and schema/security catalogs are wired. Reviewer
UI, generated client/provider artifacts and deferred runtime assurance remain
open; metadata discovery neither acknowledges a notice nor completes review.

### Original CFG09 completion and committed-pointer replay

The dispatcher now releases its exact claimed lease in one shared `finally`
for both originally inert and substantive work. Failed inert completion cannot
strand a live lease merely because it used the simpler completion owner.
Release still names the original effect, lease and claimed revision; native
authority checks refuse a successor's claim and retain completed receipts.
It is not a completion marker, cancellation override or authority bypass.
Two additional native recovery-page vectors use that actual dispatcher and
lease repository, injecting a rejected inert completion before/after flush.
The first claim rolls back and releases while the next intent completes in the
same bounded page. Empty-tail cursor wrap retries only the rejected intent at
the unchanged clock; neither outbox repair nor lease-expiry delay is invented.
These vectors are authored UNRUN/unbuilt, not operational conformance evidence.
Two native SQLite vectors reject completion before/after its final flush,
assert the failed work remains incomplete with its claim released, then retry
at the unchanged clock. They are authored UNRUN/unbuilt; existing successor/
ambiguous-commit witnesses and final provider assurance remain required.

`ModerationOriginalEffectCompletionRepository` owns the substantive original
CFG09 transition behind `IModerationOriginalEffectCompletionRepository`.
The dispatcher executes exhaustive native cause accounting and then calls this
owner with only its exact effect/lease/revision. `Pending` carries a bounded
missing-proof reason; neither a receipt prefix nor a caller completion flag can
acknowledge the outbox pointer. The actual scoped owner joins the original hashed
capture, retained publication/profile/contribution and independent adoptions,
all thirteen originally selected modes, every native cause ordinal, original
recipient occurrences and actual acknowledgement/calendar/R, current protected
representative recourse, committed independent object-review disposition and
complete current terminal-consumer/prerequisite proof. Preservation modes stage
no booking, seat, credential, admission or recognition mutation. Unsafe completed
review retains its guard; a retired ordinal requires its exact separate safe
review and release receipts and cannot reactivate.

Retained case custody and the exact action scope are now checked separately.
`ModerationFinding.MatchesRetainedCase` checks immutable case/subject/occurrence/
creation ancestry, attribution presence and hierarchical scope; it does not
authorize a Resource or fresh adjudication. `ModerationOriginalFindingProofOwner`
fences and rereads the actual captured finding, requiring its exact ID/revision/
state/reason and Resource action scope. An attributed tenant-owned proceeding
may therefore retain custody while that finding names one exact Resource.
The native owner additionally checks the retained source's case/generation/
subject/occurrence/evidence and host/tenant/trust tuple. Its finite qualification
window is evaluated at the original finding finalization, not extended at retry
and not required to remain live now. The original recipe rechecks this proof on
its own held primary transaction; completion includes it in the native proof
digest. Existing privacy, human, publication, clock and genuine outcome gates
remain necessary. `ModerationResourceQuarantineRepository.QualifyAsync` now uses
the same proof owner rather than requiring custody scope to equal Resource action
scope. It still requires that exact finding, active sanction, original publication,
finite selection, independent profile/target adoptions and current native material.
Tenant custody cannot substitute another case, subject, Resource or attribution.
The native fixture seeds the accepted lifecycle boundary; authored cases assert
recipe ownership, actual cause application/replay and progression to the separate
independent-review obligation, not native activation or terminal completion.
Tests and provider assurance remain deferred.

Original recipe acquisition, its transaction-held `CurrentAsync` recheck and
completion now bind the selection's case ID/revision to the actual captured
finding, not to the case's latest revision. Closing the proceeding advances
that case but neither rebases the immutable selection/adoption nor invalidates
their original ancestry. Rewriting both retained rows to the newer case revision
is rejected; changing the selection after acquisition also fails the terminal
recheck. Three native cases are authored UNRUN/unbuilt. They reach the distinct
missing cause-accounting obligation without creating a completed effect, reopening
the case, extending private retention or supplying activation/conformance proof.

Selected retained privacy serialization precedes primary authority, and both an
owned transaction and retained-authority adopted primary transaction repeat final
proof after the relevant flush. `ModerationOriginalEffectCompletionReceipt` and
the exact effect completion commit together. The immutable receipt binds original
snapshot/effect/selection/proof digests, publication/profile, actual cause count,
exact lease interval/revision, completion time and qualification/R ceilings. Its
cascading source foreign key preserves original cleanup; it creates no late-R
accounting exception. Invocation-local ambiguous commit retry retains fixed
coordinates and verifies its actual receipt rather than executing broader work.

The original completion owner explicitly flushes disclosure after saving the
effect transition and receipt, before its final proof callback. On separate
privacy storage its directly owned primary commit must not defer this work until
after qualification; colocated enrollment still repeats the callback after the
outer owner's flush. This ordering correction does not supply the missing
positive terminal-consumer measurements or certify a completed original recipe.

An outbox pointer retried in a later invocation uses
`IModerationEffectLeaseRepository.ReadOriginalCompletionAsync`: current fenced
maintenance credential, canonical tenant and exact original completed transition
must agree. The actual original completion owner atomically records only its
receipt ID, host, writer tenant and completed revision on `ModerationEffectIntent`.
These coordinates share the existing ledger lifetime; they retain no snapshot,
proof digest, recipient or new retention deadline. Original receipt and notice
still cascade away at R. Cleanup may advance the current effect revision without
rewriting that completed revision or defeating acknowledgement. A naked
`CompletedAt`, missing marker or another host/writer cannot substitute.
No private original snapshot, statement or new consequence is read/executed;
native maintenance authority is reread before returning. This is acknowledgement
of already committed work, not late completion of unfinished consequences.
The inert completion probe remains separate. Native committed-boundary fixtures
do not certify successful full-recipe or activation assurance. Provider migrations
for these ledger columns/portable tuple constraint remain queued for programme
model integration.

Complete original completion is not yet runtime available. The generic
`ModerationConsumerConformanceReader` accepts only each consumer's independently
current concrete runtime and native signed measurement, including final
accepted-receipt requalification after catalogue reads. Explicit concrete runtime
mappings now cover all 23 Embedded ReadResource consumers; they do not supply
conformance verdicts. Complete genuine executed measurements for that set remain
required. Native audience body preparation now covers eighteen detail/page
cases across download, external access and management affordances. Current
exact-material quarantine exposes only safe status; quarantine after native
preparation, including after private JSON serialization, must prevent terminal
delivery. The serialization cases commit the actual aggregate transition on an
independent connection before the final native disclosure check and require a
fresh safe representation. Each body observes its selected provider/version and
structured public database identity for the invocation-owned journal. These
cases are UNRUN/unbuilt, not a catalogue publication, passing measurement or
proof of effect-writer activation.
Four additional native management bodies cover detail, list, audit and metadata
export, eight cases each. They use stored-file and external-link material to
exercise safe current quarantine, quarantine after preparation and during
private serialization, and withdrawn organizer authority over an already-safe
status response. Native canonical snapshots and final projection checks remain
the authority even when the ordinary provider allows access. Audit history and
export payload fields stay withheld for quarantined material; each body observes
its actual selected database for the journal. All 32 cases are UNRUN/unbuilt.
Original-bound measurement after selected-publication replacement now
has a private retained-recipe authority and explicit native preparation/acceptance
source, still UNRUN. A separately adopted late minimized completion contract
remains unsupplied.
The owner preserves these missing facts as pending rather than changing inventory,
borrowing a replacement publication or reopening expired original evidence.
Provider/schema/client generation and all deferred native positive/race/runtime
assurance remain programme requirements.

Original measurement requests may supply `OriginalEffect` with effect identity,
snapshot hash and effect-binding hash. These are untrusted custody coordinates,
not permission. `ModerationOriginalRecipeAuthority` can mint an original-purpose
borrow only on its owning serializable transaction after retained privacy and
sorted primary human fences. It rereads the exact live original notice/effect,
immutable publication/definition, original contribution and captured people,
current publisher/preparer roles, native cleanup and hard R. Historical actors
are discovered from metadata before acquiring those fences; the private snapshot
is not a discovery source before privacy qualification.

The borrow applies only to original completion and original measurement
preparation/acceptance. Current activation, lifecycle and publication-candidate
qualification retain their selection requirements. New original challenges
require the consumer's current genuine installation; acceptance independently
authenticates the exact signed receipt and current issuer/source provenance.
Changing the selected publication does not relabel a current measurement, extend
its signed validity or renew original R. Original preparation/acceptance
requalifies again at final native commit, including adopted outer transactions.
Missing original constitution or AG05 purpose evidence preserves Pending rather
than borrowing replacement adoption. Twenty-one native original-authority cases
are authored UNRUN; source integration does not establish whole-recipe conformance.

The challenge itself now retains all three original-anchor coordinates or none.
Domain and EF owner checks reject partial anchors, and after-save behavior makes
them immutable. Native acceptance compares a marked challenge with its supplied
original purpose and rechecks retained coordinates after the row fence. The
borrowed evidence session and owned launcher reconstruct original preparation
with those exact persisted coordinates, preserving its native mutation digest.
Launcher pre/postflight checks original notice metadata and published contribution
without reading private snapshots, verifies published-candidate definition
provenance and current actors, and includes original custody in its finite
fingerprint/deadline. Five additional Domain anchor cases are authored UNRUN.
Provider migration generation/application remains final programme work.

Original constitution record/adopt requests now have an explicit untrusted
`OriginalEffect` anchor. Their native writer borrows the private original recipe,
matches its immutable publication/profile/mode/definition and bounds fresh
procedural qualification by original custody. Source rows freeze all original
coordinates plus the custody ceiling; adoption retains the exact source revision
and generation. Substantive terms, independent reviewers, native availability
receipts, caseload and current administrators are requalified rather than copied
from replacement policy. Ordinary qualification excludes original-specific rows.
Both normal execution and unique-conflict receipt recovery run inside retained-
erasure serialization and serializable primary ownership. Metadata-only original
human discovery precedes sorted fences, and the original constitution purpose
is reacquired at actual primary commit, including adopted outer transactions.
No existing source/adoption deadline, signed measurement or R is renewed.
Nine Domain and 27 native persistence vectors are authored UNRUN; generated
provider schema and operational/concurrency assurance remain pending.

Measured-operations record/accept now supports the same deliberate original
purpose. Requests retain an untrusted `OriginalEffect`; native acquisition binds
the exact original definition/publication/profile/mode and activation binding.
Source rows freeze effect/snapshot/effect-binding/custody coordinates, and
acceptance matches the exact original anchor and source revision/generation.
Ordinary qualification excludes these rows. Both primary and unique-conflict
recovery paths retain erasure serialization and requalify the purpose at actual
commit; original human discovery precedes sorted fences. Mutation receipt
reconstruction includes the anchor, including terminal withdrawal provenance.
Missing recovery, key-convergence or transport observations remain missing.
Neither recording nor acceptance certifies AG05, renews signed measurements or
extends original custody. Nine Domain and 37 native persistence vectors are
authored UNRUN; provider generation and genuine operational assurance remain open.

Target-owned intake can receive an unresolved reviewer assignment before subject
attribution. `ModerationReviewerAssignment.Assign` preserves its case/tenant and
null subject; `ModerationQualificationRepository` permits only assignment and
withdrawal to read that unattributed case. Native administrator/reviewer identity,
privacy, case scope/revision and final transaction checks remain required.
Controller qualification, finding, urgency and other case-source operations
still require attribution. Triage creates no disclosure grant, qualified quorum
or responsibility finding. One Domain and three native vectors are authored
UNRUN; complete qualified queue/fallback integration remains open.

Explicit responsibility qualification now stages separate subject-owned cases
with immutable confidential occurrence/evidence links. Event submission can
qualify an actual submitting human, Event content its native owner, an
organizational Actor its actual organization, and Service identity its native
principal. No publisher-user, employee or participant-to-global-human inference
is permitted. Qualification is not guilt or permission to read the occurrence.
Parent case-human discovery now includes the qualifier and read-grant actors
before subordinate fences. Linked case-source writes acquire current attribution
and recheck it after flush and in retained-custody final callbacks. The retained
qualification epoch is a provenance floor, not permission: legitimate writer
progress requalifies the same native facts under the current fenced head without
rewriting or renewing the source. Regressed authority, changed topology,
stale/revoked grants, roles, original material and expiry still fail closed;
the current head must remain stable across each final recheck.
A native progress regression is authored UNRUN. Human finding acceptance and
approval now acquire current attribution for linked cases and recheck it at
clean terminal permission checkpoints, including stored receipt resolution.
Dirty pre-flush checkpoints requalify native facts without invoking the
post-flush-only recheck. Existing native finding/reviewer permissions still
authorize the observer; retained qualifier/grant provenance does not give that
observer disclosure permission. A distinct-observer metadata-only vector is
authored UNRUN (29 attribution vectors total).
Complete activation/reconciliation lifecycle, pre-finding protected response
and attribution renewal workflows remain open.
Scheduled finding observation now borrows `IModerationFindingProvenanceSession`
from the actual `ModerationAuthorityParticipant` acquisition. The native sealed
session binds the exact context, serializable transaction, case, operation,
principal and current source; a marker implementation or copied metadata is
not authority. An installed maintenance service can observe only `Reconcile`,
without becoming a human qualifier, reviewer, recipient or key owner.
`ModerationCaseTargetProofOwner` and `ModerationQualificationRepository` retain
the original human roles, grant, attribution, privacy-owner custody and response
deadline. Discovery includes procedural source publishers/adopters, then parent
acquisition fences all humans before downstream reads; observers borrow those
fences rather than acquiring humans late. Reconciliation holds selected identity
custody through commit. Dirty checkpoints reacquire exact facts; clean terminal
callbacks recheck every acquired obligation even if the clock crosses the
decision's end. This port exposes an evidence reference, not protected text or
recipient actions. Source compilation is not provider/runtime acceptance.
One attributed proceeding remains tenant-owned for one original subject and
occurrence. `ModerationFinding.Finalize` now requires an explicit action scope
and exact attribution source/generation instead of copying the custody scope.
Resource finalization qualifies the actual resource and its exact source material;
another resource in the same Event cannot borrow the finding. Native case lookup
allows only that narrower attributed resource target, while procedural policy
and reviewers remain case-owned. A non-null scope identifier and zero-for-absent
attribution generation keep finding uniqueness provider-portable; nullable
ancestry columns cannot accidentally admit duplicates.
Nine new Domain vectors are authored UNRUN, and existing finding factories were
migrated. Immutable head/renewal integration, generated provider schema, genuine
Content observation and final-commit witnesses remain open. Native-owner effect
profiles are not overlay sanctions; this does not establish activation.
`ModerationCaseAttributionHead` now owns the revisioned current source without
rewriting the opening case. Initial attribution persists that exact head with
its case/source; reads fence and compare current source, generation and revision
through terminal recheck. The Domain advancement contract rejects stale tuples,
foreign/closed proceedings and unchanged live lifetime renewal. Thirty-four head
vectors are authored UNRUN. Generation-bound response/notice selection is still
being integrated; schema/runtime remain open.
Source qualification now requires its explicit positive generation. Historical
rows are unique per case/generation while the head remains unique per original
subject/occurrence. Generation-two Domain fixtures use the real source factory;
this prepares immutable renewal without adding a new recurrence or changing the
opening link. Native renewal now stages a consecutive source, immutable expected-
tuple receipt and current-head advancement in the same serializable transaction.
Fresh native humans, roles, material and policy are reacquired before subordinate
fences. The transaction permits only those three owned writes; postflush and
final checks compare source/head, exact receipt, original case revision and
qualification deadlines. Exact retries return the same current qualified source,
never another generation or a rewritten case.
Responsibility grounds and reviewer/read-grant/policy facts now have separate
digests. Replacing a read grant does not create new grounds for renewing a live
attribution; its original authorization still remains independently pinned.
Forty-seven receipt Domain cases and eleven native renewal cases are authored
UNRUN. Source compilation passed; native provider/replay/rollback acceptance
and generated schema remain open.
`ModerationCaseAttributionController` now exposes three authenticated closed
commands: `POST /api/moderation/case-attributions`, `POST .../{caseId}/renew`,
and `POST .../sources/{sourceId}/withdraw`. Named routes dispatch the existing
manual-validated CQS handlers; route case/source IDs replace body target IDs.
No body conveys current tenant, host, caller or authority. The native owner still
qualifies original material/read grants, responsibility and finite current facts.
Success contains only case identity for initial attribution or source identity
for renewal/withdrawal, never confidential occurrence/private entity payload.
All three use write rate limiting, 16 KiB JSON bounds, private/no-store caching
and generic idempotency-response-storage suppression. Native operation receipts
own exact retry; unavailable qualification maps to generic `503`, not a guessed
existence/permission distinction. Four API boundary cases using the actual CQS
validators are authored UNRUN: valid body IDs cannot replace invalid route
selectors before reaching persistence. These are not hosted HTTP or native
authorization witnesses. Qualified discovery/HAL administration, client parity
and full transport/provider assurance remain open.
Custody and notices now pin the actual source ID, generation and evidence revision,
with their deadlines bounded by that source. Native early human discovery and
Resource/finding selection use the current head instead of the immutable opening
link. Private readers compare the retained custody/notice pins to that exact
current generation before decrypting; old ciphertext, response receipts and clocks
are neither retargeted nor renewed. The one-opportunity-per-case uniqueness and
erasure tombstone still prevent replacement from restarting a private clock.
Thirty generation Domain cases and one native unexpired-response substitution
witness are authored UNRUN; source compilation passed, runtime assurance pending.
Historical safe-text revocation has a separate retained-ownership path:
`RevokeModerationNoticeDisclosureSource` still qualifies the current canonical
administrator, tenant/trust/host, original approval author, case revision and
approval revision under native fences, but it does not acquire live attribution
or the old private response actors. It may close an approval after source
withdrawal/expiry and physical response purge without decrypting or renewing it.
Exact operation replay preserves the original receipt; changed retry fields
conflict. Finding acceptance, approval and activation retain their current-source
requirements. Seven native correction vectors are authored UNRUN; provider
assurance remains pending.
The same retained-case distinction now applies to profile/Event/resource adoption
revocation, procedural-policy revocation and reviewer withdrawal. Closed cases do
not reopen; procedural source/adoption pins retain their original case revision
while the request separately binds the current case revision. Source withdrawal
does not require the source's old private opportunity to remain live.
Native lifecycle Reject/Withdraw/Revoke fence historical participant coordinates
without requiring the original adjudicator, response actors or subject to remain
alive. The correcting principal still requires current native identity and its
operation-specific issuer/administrator permission; Revoke still matches the
exact sanction owner. These terminal operations do not reacquire live target
material or qualify new activation.
Resource finding reversal acquires administration at the actual parent tenant
then fences/rereads the retained case and finding, checking their exact Resource
action scope, subject, occurrence and creation ancestry. It does not require the
resource to remain undeleted or import authority from another Resource. The old
adjudicator remains immutable provenance, not a current correction prerequisite.
Every reversal also checks the retained case/finding subject, occurrence and
creation ancestry before mutation. A foreign case reference cannot borrow
correction merely because the finding's action scope matches. Non-finding
withdrawals require their original exact case scope.
Twenty-six native cases across this correction unit are authored UNRUN, including
closed procedural withdrawals and Resource substitution/departure. The lifecycle
fixture witnesses authority acquisition, not complete consequence delivery.
The protected pre-finding source is now installed separately from original
post-decision Content response. `ModerationPreFindingCustody` records explicit
finite purpose, mandate, payload limit and independent privacy-owner acceptance.
`ModerationPreFindingNotice` binds one attributed case/evidence revision and
explicit human or current Event owner/manager recipient. Independently implicated
representatives and interested Content reviewers/controllers cannot disclose it.
Separate coordinate-bound Data Protection purposes encrypt notice and response;
native receipts freeze exact retry provenance. Acknowledgement and elapsed time
never substitute for an authenticated retained response.

Model trust/tenant filters and scoped repository/response-owner registrations
are installed. Finalization uses protected response provenance for attributed
cases without falling back to plain human response evidence; qualified urgency
remains distinct. Human lifecycle consumers discover response actors before
fences, reacquire dirty checkpoint facts and recheck clean terminal provenance
under retained selected privacy custody through actual primary commit/replay.
Withdrawal and expiry physically clear ciphertext without extending its calendar.
Attributed Human `RecordResponse` acquisition now discovers the original response
actors before native fences and selects only response identifiers for the actual
case/recipient. `IModerationPreFindingResponseOwner` acquires them through the
parent's original provenance session, checks the case revision and response time,
and rechecks clean terminal custody. Dirty mutation checkpoints use the same
acquisition rather than manufacturing generic evidence. The generic
`RecordModerationCaseResponse` writer denies attributed cases before persisting
its statement. Seven real-store vectors cover protected-only progression, expiry
and missing-receipt refusal despite generic evidence, and generic-capture denial;
three include a real complete-flush control, then exact response expiry or
response-receipt loss inside the owning primary transaction. Negative cases
require the decision/effect to roll back and the deleted receipt to be restored.
They remain unrun/unbuilt. This bridge retains the existing actual-Human subject
rule and does not confer Content representation or finding/approval authority.
`ModerationStore` includes Human `RecordResponse` in its existing privacy enrollment
path, rather than using the ordinary serializable-only branch. This retains the
selected identity authority and its borrowed primary transaction through final
disclosure flush and the owner's pre-commit callback. The three complete-flush
vectors also require that real privacy owner to own the primary transaction.
This is reuse of existing case privacy custody, not a selection of the pending
whole-resource writer-fence or deployment-route lifetime contract.
Native notice issuance now stages a required in-app pointer through
`ModerationNoticeNativeMaterializer.MaterializePreFindingAsync` and the existing
recipient graph materializer on that same primary transaction. The closed
`moderation.pre-finding.portal.v1` template stores only the notice UUID, fixed
safe title and null body, with no email, external delegation or link capability.
`ModerationAvailabilityRequired` preserves private response availability even
when optional context notifications are muted. Missing native materialization,
recipient or local routing makes issuance unavailable, never silently external.
Generic graph creation reserves the template/reference/deduplication namespace
to the original issuer's newly staged live notice. Final issue qualification
fences and rereads the exact recipient/tenant, immutable graph birth and one
required in-app delivery; substituted or missing graphs reject exact retries
without repair, acknowledgement or a new retention interval. Delivered in-app
means the graph exists, not that the recipient has read or answered it.
Withdrawal, expiry and ordinary erasure dispose these exact pointer graphs
before their private source disappears. The shared cleanup owner selects only
opaque notice/recipient coordinates in batches of 100 and deletes dependants
before intents inside the existing owning Serializable transaction. It needs
neither plaintext nor a live actor/decryptor, and does not touch another
notification purpose. Fifteen new native vectors are authored UNRUN/unbuilt;
nine existing expiry/erasure vectors now assert pointer disposal or rollback.
UI navigation, generated clients and runtime/provider assurance remain open.
Twenty-four vectors are authored UNRUN (10 Domain/14 native), including the
dirty-versus-terminal checkpoint distinction. Native command/HTTP presentation,
delivery, scheduler and erasure disposal, machine reconciliation response
qualification, provider generation and runtime assurance remain open; this is
not an activation/readiness claim.
Recipient acknowledgement and response now have distinct closed CQS commands,
`AcknowledgeModerationPreFindingNoticeCommand` and
`SubmitModerationPreFindingResponseCommand`. Manual validators bound UUIDv7
identities, exact case/notice revisions and UTF-8 response bytes. Commands carry
intent only; the existing native writer independently resolves caller, selected
tenant, current recipient and custody. The response maps only the authorized
notice identity and generic denied/not-found outcomes, never private case
coordinates or response text. Ten Application vectors are authored UNRUN;
HTTP/portal runtime and UI navigation remain open. The remaining administrative operations now
also have closed typed commands: propose custody, independent owner acceptance,
explicit notice issuance and withdrawal. Manual validators enforce finite
substantive terms and coherent Human/Content recipient coordinates; native
roles, independence, current assignment and accepted purpose are still qualified
by the writer, not inferred from validation. Thirteen additional Application
vectors are authored UNRUN; no legal duration, recipient or authority is selected
by a command default.
Administrative HTTP now exposes those four existing native commands through
two capability-specific controllers. `POST /api/moderation/pre-finding/cases/{caseId}/custodies`
proposes terms; `POST .../custodies/{custodyId}/acceptance` independently accepts
them; `POST .../custodies/{custodyId}/withdrawal` withdraws the selected custody.
`POST /api/moderation/pre-finding/cases/{caseId}/notices` issues the subject-safe
notice. Route case/custody IDs overwrite body selectors. Tenant, caller, roles,
accepted mandate, independence and original recipient assignment remain native
writer facts. Proposal/acceptance/withdrawal JSON is bounded at 16 KiB; issuance
at 128 KiB, with substantive text/retention limits still enforced by native custody.
All four actions require authentication, write rate limiting, no-store policies,
named operation IDs and generic idempotency-response-storage suppression. Success
returns only the authorized custody/notice UUID; validation, authentication,
hidden denial, conflict and unavailable outcomes use ProblemDetails. No
administrative route reads confidential material or acknowledges/responds for a
recipient. Sixteen transport/real-handler validation and failure-mapping vectors
are authored UNRUN/unbuilt; hosted authorization, provider assurance and generated
OpenAPI/client reconciliation remain programme-exit obligations.
The shared factory-only `AuthorityScope` and `SubjectReference` can now bind at
the MVC JSON boundary without weakening their constructors. Scope JSON retains
only kind/ID/parent ancestry; subject JSON retains kind/ID/origin. API converters
rebuild each through native domain factories, including Resource ancestry under
Tenant or Community. Duplicate or unknown members, incomplete hierarchy,
noncanonical values and claimed authority are rejected as malformed JSON.
Thirty-three adversarial/round-trip vectors are authored UNRUN/unbuilt. No caller
grant, current qualification, snapshot rewrite or lifecycle endpoint is supplied
by this serialization prerequisite; schema/client and hosted assurance remain open.

Private HTTP notice disclosure is source-connected at
`GET /api/moderation/pre-finding/notices/{noticeId}`. A single-use query captures
the selected tenant; decrypted payload and HAL preparation remain inside the
native owner's retained authority until actual response write. Output is
`private, no-store` and `no-referrer`; GET never acknowledges. The view includes
only the authorized subject-case ID/revision needed for recipient commands,
not the confidential occurrence/attribution coordinates. Recipient views expose
current acknowledgement/response links; independently qualified reviewer views
expose no recipient mutations.
Separate authorized POSTs use `/acknowledgement` and `/response`, overwrite the
body's notice claim with the route target and keep retained native receipts
outside generic idempotency response storage. JSON may be up to 512 KiB to
accommodate escaped text; UTF-8 statement bytes remain bounded at 65,536.
The Blazor Client now has an authenticated, uncached
`/moderation/pre-finding/notices/{noticeId}` route. Its service uses the generated
per-tag client through the existing BFF transport and projects only the protected
HAL view. Acknowledgement and response controls depend on returned links, not
local roles; writes carry the displayed case/notice revisions and a fresh
operation ID. Successful writes reload the native view rather than inventing
acknowledgement state. Reviewer views expose no recipient controls.

Invalid response submission now queues focus through `IAccessibilityFocusService`
until the connected error has rendered. Whitespace-only or over-limit UTF-8 input
keeps the draft, marks the textarea invalid and returns focus to that field without
a write. Route cleanup discards queued focus alongside private state. Two rendered
cases use the real focus service and generated-client HTTP boundary; both remain
UNRUN/unbuilt. This follow-up has no new product compilation or browser/accessibility
assurance; its scoped diagnostic request timed out.

The notification mapper classifies an existing fixed in-app pointer using its
canonical notice UUID, owning recipient and exact stored delivery deduplication
key. It publishes only the `ModerationPreFindingNotice` navigation discriminator;
the bell and inbox route it to the protected portal. Titles are not classification
inputs, no private key/recipient fields are added to the list DTO, and navigation
is not an access grant. The GET still independently requalifies native custody.

Route changes, disposal and the original retention deadline clear private view
and response draft state. Long finite deadlines use bounded timer checkpoints
without moving the original ceiling. Recipient expiry, stale-route and transport
witnesses are authored but unrun. Generated Client product compilation passed
after the native Roslyn transformer repaired quoted integer defaults emitted for
number-or-string schemas; the wire schema and native page default remain intact.
HTTP/runtime, visual, provider and delivery assurance remain open.
The existing Quartz reconciliation pass now invokes physical pre-finding expiry
cleanup before acquiring its maintenance credential. Cleanup is monotone,
tenant/host-bound and limited to the configured batch capped at 100; it empties
encrypted notice/response columns under native row fences without extending the
original deadline. Recipient/reviewer or maintenance authentication is not an
erasure prerequisite. Two expiry/privacy vectors are authored UNRUN; scheduler
source compilation passed; operational/runtime assurance remains open.
Ordinary human erasure now enters this private graph through the existing
`EraseModerationCaseNarrativesAsync` exact-saga, identity-fenced serializable
settlement and complete tenant inventory. Native batches select identifiers,
never ciphertext, and delete affected notices/responses, owned custody and
linked/actor receipts. Accepted privacy custody, retained deadlines, missing
decryptors or moderation activation do not veto ordinary erasure; the existing
legal-hold-pseudonymized guard remains separate.
Deleting a notice cannot reopen its one response opportunity: a minimal
`ModerationPreFindingErasureTombstone` retains only case and original
host/trust/tenant coordinates, with no human, private text, request digest or
recognition material. The tombstone is saved in the same uncommitted settlement
before deletion and the native issue checkpoint rejects renewal. Eight owning
erasure/rollback/non-renewal cases are authored UNRUN (37 Domain/native cases);
seven owning error diagnostics are clean. Persistence source compilation passed;
generated migrations and real-provider/race assurance remain open.
`AttributeModerationCaseCommand` and `WithdrawModerationCaseAttributionCommand`
now expose the actual native attribution writer through closed CQS handlers.
Manually instantiated validators bound UUIDv7 identities, revisions, purpose and
finite UTC intent; the repository still owns current caller/tenant/relationship
qualification and actual commit. Success returns only the authorized subject
case identity or the already named withdrawn source identity, never occurrence
evidence or reporter acknowledgements. Sixteen contract vectors are authored
UNRUN; discovery compilation is not wire/runtime acceptance.

Portal, acknowledgement and optional SMTP requalify the original representative
assignment at actual use. Native primary fences and selected retained-erasure
serialization cover private preparation and the awaited output. Insert-once
acknowledgement keeps `SubjectKind`/`SubjectId` bound to the Content object and
records the distinct `AcknowledgedByHumanId`. It is neither a human finding nor
quarantine release. Optional pointer mail is not acknowledgement. Minimal status
uses only hashed capture metadata and current original custody; after original
attribution is purged, current Event management cannot reconstruct historical
status permission. Cleanup proves every original representative occurrence and
removes its governed native graph at the same original ceiling. These source
contracts still require generated provider migrations/client parity and final
runtime assurance; full CFG09 consequence completion remains separate.

`ModerationNoticeDisclosureIntent.RetainUntil` is a separately approved finite UTC
privacy ceiling. Its immutable approval and the original hashed
`ModerationNoticeSnapshot` preserve that ceiling; the snapshot takes
`ContestationCalendarMonths` from the exact selected profile, never retention
prose, recognition terms or sanction expiry. `ValidUntil` remains the original
acquisition deadline. Capture and native post-flush/final-authority checks require
enough calendar time for notification now within the original ceiling.

The protected portal exposes `GET /api/moderation/notices` and
`GET /api/moderation/notices/{id}` through native CQS and a private single-use
projection. Current canonical subject or independently current reviewer
qualification remains fenced through HAL/JSON preparation and the final HTTP
write. Historical recipient identity and an original explanation are not current
enforcement grants. Explicit safe-text revocation can close that text without
rewriting a remedy receipt.

Read/status acquisition transfers a lease only after successful revalidation and
construction of its acquisition result. Before transfer, denial or an exception
disposes the created lease itself; failure before lease creation cleans up the
transaction directly. Lease disposal clears its trust frame and disposes only a
transaction it owns. An adopted retained-authority transaction remains with that
authority. Successful transfer leaves disposal with the single-use projection.

`POST /api/moderation/notices/{id}/acknowledge` binds an operation UUID, current
canonical subject and the exact original snapshot hash. An insert-once
`ModerationNoticeNotificationReceipt` records server-observed `NotifiedAt` and
`RemedyUntil = NotifiedAt.AddMonths(originalCalendarMonths)`. The actual receipt,
not a GET, reviewer read or generic pointer, supplies notification proof. Retry,
expiry, reversal and policy replacement cannot restart its clock. The original
zero-month profile remains zero. A notification must satisfy forward calendar
arithmetic `NotifiedAt.AddMonths(M) <= RetainUntil`; subtracting months from the
ceiling is not equivalent at month ends.

The acknowledgement owner explicitly finalizes pending disclosure after saving
the receipt and before rereading the original recipient, capture, transaction and
forward calendar floor. On separate privacy storage, its direct primary commit
must not trigger new disclosure work after that terminal qualification. Colocated
enrollment still retains the same validation after the outer owner's disclosure
flush. Six native vectors are authored, not run: Human and original Content
representative controls, calendar-floor loss and recipient deletion after an
observed persisted disclosure-epoch advancement. Denials require receipt, epoch
and injected recipient-state rollback; original retention and pending effects
remain unchanged. The observer uses actual database state, not SQL text or timing.

Before receipt, full disclosure uses that same forward predicate at actual output.
After receipt, qualified reads remain bounded by `now < RetainUntil`. Late or
missing notification is unproven, not expired contestation; it cannot justify
backdating, a shorter calendar floor or automatic retention expansion. Qualified
minimal remedy status has its independent native owner below. The separate
decision/case ledger is not deleted by this notice ceiling. Receipt filters require
exact current tenant and trust domain.
Source compilation, generated schema and runtime assurance are separate gates;
all new tests remain unbuilt/unrun under programme deferral.

`ModerationNoticeCleanupRepository` independently scans original approval IDs
before authority-dependent scheduled reconciliation. Its fresh scope authenticates
the installed maintenance credential and binds the actual host, trust domain and
tenant. At most 100 approvals are scanned; live heads advance without deletion.
Only an acknowledged `Applied` result advances the tenant-bound Quartz cursor.
Due cleanup atomically removes original disclosure text/facts, source, occurrences
and acknowledgement receipts. Lost commit acknowledgement leaves the cursor
unchanged; a retry cannot reconstruct the removed explanation.

For a proved complete original write, five coordinates on the existing
`ModerationEffectIntent` record cleanup identity, host, writer tenant, original
ceiling and purge time. They carry no snapshot hash, private targets or text, add no
retention lifetime, and disappear with that independently governed ledger.
`ModerationStore` accepts this marker only for exact accepted-operation replay,
with all original payload/copy rows absent and final current authority rechecked.
Missing/corrupt captures receive no marker. Cleanup never sets `CompletedAt`,
proves notification or declares a complaint ineligible.

The same scoped `NotificationIntentRepository` owns graph cleanup and normal
materialization/repair. The original portal/in-app producer now also creates an
eligible optional SMTP sibling using `AccountModerationOriginalNotice = 16`,
never the unrelated event-availability kind. Its dedicated template contains
only the protected notice reference; no explanation, finding or display name
enters email. Cleanup discovers exact effect prefixes,
including soft-deleted orphan intents, validates recipients/pointers and removes
delivery/notification/intent rows and this exact governed SMTP sibling, including
dependent attempts/receipts, under native fences. A substituted SMTP kind or
external delegation fails closed rather than granting cleanup authority over a
different provider's work. Materialization and repair require the actual original
source and hash inside its existing transaction/fence and before its original
ceiling. No missing source is rebuilt from current findings. Native and transitional
Swagger schemas require human Bearer authentication for the dedicated notice
routes, including the required `[AllowAnonymous]` GET annotations; API-key and
purpose-only credentials are not advertised as substitutes.

`IModerationNoticeEmailAuthority` is a mandatory scoped final-use owner in the
actual SMTP factory. `SmtpEmailService` connects/authenticates first, then the
native owner qualifies the original recipient, host/tenant ancestry, disclosure
approval, reviewer role/evidence, retained erasure authority, active membership,
current preferences/governance and verified address. It acquires metadata-only
original ownership/retention before private SnapshotJson selection. Queue
eligibility likewise projects the exact source ceiling and compares it outside
SQL for provider-portable DateTimeOffset handling. Neither is a reusable grant.
Native primary and retained-erasure fences remain held through the single
transport callback; no provider execution strategy retries the send. Transport
configuration, finite disclosure deadline and original calendar guard are
rechecked immediately before SMTP. Definite authority rejection atomically
settles the existing outbox, attempt, receipt and delivery as Skipped with no
automatic retry. Acceptance keeps the existing uncertain/accepted transport
semantics; it never creates subject NotifiedAt, acknowledgement, a new R or
contestation eligibility. Cleanup does not recall a message already accepted
by an external SMTP server. Disposition 6 (`NativePortalEmailQueued`) requires
native regeneration of `ck_moderation_notice_native`; all authored SMTP,
erasure, deadline, cleanup and provider race witnesses remain deferred.

Six further native SMTP vectors await recipient deletion, email-verification
loss, preference mute, disclosure revocation, source substitution or a freshly
verified address change at the real loopback EHLO/HELO ready boundary. Admission
has already occurred; final native qualification has not. Negative cases require
zero MAIL/RCPT envelopes and DATA; the address-change case requires one native
pointer message to the current verified address and no private queued content
or notification acknowledgement. The peer awaits the database mutation before
replying, not a sleep or polling interval. These vectors are UNRUN/unbuilt;
fresh LSP requests timed out and do not establish compiler or runtime success.

`GET /api/moderation/decisions/{decisionId}/effects/{effectId}/notification-status`
is an independently qualified current-human CQS read, not possession-based
recovery. `ModerationNoticeStatusRepository` binds the exact effect to the original
decision/case subject ancestry and actual current host/trust/writer tenant. Its
single-use private projection retains native serializable fences through HAL
assembly, JSON serialization, final qualification and output. Current human
identity is re-resolved; roles, tenant membership and service ownership confer no
status authority. Full-text subject notices advertise the implemented status
route; reviewer notices do not advertise subject-only status.

The existing original source derives bounded `StatusMetadataJson` and
`StatusMetadataHash` atomically from the immutable hashed capture. Original
host/writer/case, scope/subject-origin fingerprints, selected calendar months and
effect/snapshot binding permit status queries to select only minimized metadata.
No original `SnapshotJson`, approval text, finding or case evidence enters this
read. These metadata columns inherit original-source immutability and deletion
at the same ceiling; they add no record class or retention lifetime.

`Proven` exposes actual validated receipt time, original calendar floor/months
and ceiling while that purpose remains available. `Unproven` means no qualifying
receipt, not a claim that notification never happened. `Unavailable` withholds
dates/months for damaged/missing proof or a reached ceiling before cleanup.
`PurposePurged` uses only the independently qualified existing-ledger cleanup
marker and original ceiling; it cannot recover notification facts. None of these
states decides complaint expiry, grants complaint/review operations or invents
support routes. Explicit full-text revocation and late notification windows do
not remove this independently qualified minimal status. Deleted-subject status
still requires its own approved purpose-bound proof owner; recognition-correction
credentials neither provision an ordinary user nor grant this route.

The dedicated selected secret provider shares its actual resolver with lifecycle
observation; no alternative key configuration is introduced. Native lifecycle
metadata stores commitments/generations, never secret bytes. Recognition setting
locks precede refreshed selected observations and the selected identity/native
transaction. Stage, independent byte acknowledgement, write switch and retirement
have required final selected-source re-observation at the owning commit.
Original capture and authority binding enlist exact original source/backup
obligations in that same native transaction.

Minimum file-backed SQLite runtime ownership is physical-file based: Unix retains
an exclusive cooperative sharing lock; Windows retains a separate handle-specific
non-SQLite-reserved byte-range lock. This is not inferred from a host UUID or a
pathname sidecar. Stop every process before restore; it does not prevent privileged
native writers or Unix rename. Other provider/remote topology requires its genuine
owner, not a single-replica claim. Windows runtime assurance remains unrun on this
Linux workstation.

Whole expired matching sets may be pruned only without surviving external
descendant/item/binding references. Strict `AcquirePurgeAsync` verifies the
authentic selected fact, original complete receipt/count/obligation commitments,
key aggregate floors and finite receipt lifetime; `FindPurgeAsync` is diagnostic.
Pruned append leases recheck that lifetime at their native commit checkpoint.
First qualified purge fixes the earliest original disposition/replay receipt
ceiling; retries never renew it. Privacy replay sweeps expired linked receipts and
obligations before authority-dependent work, so an authority outage cannot prolong
their retention. Unlinkable key horizon/watermark aggregates preserve the original
backup retirement obligation after those linked records are forgotten.

## Deferred action matching without account exclusion

`ModerationRecognitionActionRecovery` represents operational uncertainty, never
an asserted identity match or a sanction against the fresh subject. A row pins
the fresh UUID, exact existing/preallocated verified native login UUID and original
capture item, with the original privacy/recovery owners and a finite disposition
ceiling no later than original sanction end or matching retention. It contains
no provider identifier bytes, new probe digest, profile or accusation history.
Escalation is immediate. Scheduled native owner notification is integrated; real
delivery/disabled-routing retry cases remain unrun. The owner-scoped native
assessment/list/read/ack surface is implemented as described above; its HTTP and
provider witnesses remain unrun.

Both completed bindings and recovery candidates retain `CaptureMaterialReceipt`,
computed from the existing immutable capture-receipt serialization for that
single item. Terminal readers verify it before using scope, capability or purpose
to prove a target unrelated. A changed item cannot make its former resource
silently unrestricted or resolve matching uncertainty; its applicability is
unknown and the snapshot closes. An unchanged item with an unavailable aggregate
receipt/source still closes only its applicable targets. Eligibility drops are
excluded from this commitment, so lawful correction does not rewrite provenance.

Known action-purpose key/authority failures may stage those minimized references
in the caller's original transaction. Successfully probed nonmatches and admission
candidates are not included. `ActionRecoveryStaged` is false unless complete
settled original receipt material and an exact native login identity are supplied.
An admission failure/exclusion or an unavailable store/unknown inventory cannot
become an unrestricted result. The caller must verify the same trusted binding
before actual commit; a preallocated UUID alone proves nothing.

Protected resource reads report `ActionRecoveryRequired` only where the original
potential capability/scope could apply. Ordinary sign-in and remedies are distinct
from protected capability availability; no old roles/history are restored and no
"banned" attribution is made. Complete actual matching of that pinned live login
resolves uncertainty and attaches only qualified surviving references, or confirms
no match. A different account of the same provider cannot resolve the row.
Scheduled erasure replay now walks unresolved recovery IDs in bounded native
batches. Each retry holds recognition settings before the selected identity gate
and serializable primary owner, rereads the exact pinned live login, and matches
action purposes only. A missing/replaced login or unreadable authority leaves the
original obligation unresolved, without substituting another provider account.
Successful matching saves references and resolves only that login's uncertainty
in the same transaction; post-disclosure validation checks the binding and finite
source deadline immediately before the owning commit. Original end/retention
expiry resolves operational matching keylessly while preserving receipt material.
Original-owner escalation/disposition and native runtime assurance remain open.
Native enrollment now has a distinct primary-owner hook: Save and disclosure
flush precede validation immediately before commit; colocated SQLite performs
that validation at the retained authority's final adopted commit. Exact already
bound sign-in validates its existing native binding and privacy gate without
applying replacement-admission rules. Runtime assurance remains unrun.

Shared setting groups retain the union of visitor/SMTP and recognition leases
through nested scopes. Already-held keys are not reacquired; only additional
keys may be acquired before the primary transaction. Fully held groups may be
reused inside it, and leaving the inner scope restores the outer key set.
This preserves the original lock order rather than masking/reacquiring an
outer lease after identity or subject fences.

## External recognition key-ring source

`ModerationRecognitionKeyRingProvider` resolves the instance-only
`moderation.recognition_keyring` catalogue key through the existing selected
`ISecretResolver`, without a tenant binding, alternate authority or ring cache.
Its default is Infisical `/api/MODERATION_RECOGNITION_KEYRING` or the selected
environment/shared User Secrets key `MODERATION_RECOGNITION_KEYRING`.
User Secrets retains the existing Development/Testing restriction and is never
fallback. This optional capability is not required for platform bootstrap.
The scoped provider is registered; resolving it alone does not establish matching
or permission. The separate capture coordinator qualifies its actual use.

The strict JSON source has exactly ring fields `version`, `trustDomainId`,
`ringGeneration`, `issuedAt`, `validUntil`, `keys`; each key has exactly `kid`,
`generation`, `k`, `status`. Version is 1, trust is canonical lowercase UUIDv7,
generations are positive and below `long.MaxValue`, timestamps are UTC `O`
format and `k` is canonical unpadded base64url for 32 bytes. A 64-KiB bound and
1–16 unique key IDs/generations apply. Exactly one key is `active`; other keys
are only `readable`. Unknown/duplicate properties, material, source, scope,
foreign trust, ambiguous writers or noncurrent intervals reject the whole ring.

A snapshot expires at the earlier source deadline or five minutes after the
actual secret resolution. Future/stale observations and backward clocks reject.
Exact trust/key ID/generation selection yields independently disposable
`ModerationRecognitionKey` copies. Snapshot disposal clears its decoded buffers
and closes further borrowing; it does not revoke copies already borrowed.
The native terminal capture/match owner must reacquire current secret/native
authority and dispose every borrow. Metadata retains original ring/binding/source
revision and material `VerificationTag`; equal identity/generation with a
different tag is divergence, not a matching token or convergence receipt.

The catalogue declares overlap rollout with candidate validation and replica
acknowledgements. This generic policy and external JSON do not establish actual
replica/backup floors, safe retirement, emergency recovery or activation.
HMAC material cannot be rehashed after input erasure. Those native durable
controllers, backup horizons and qualified catastrophic dispositions remain
mandatory. Parsing owns/clears partial material, retains no JSON payload and
emits only bounded status errors; the existing resolver remains the selected
authority and may own its ordinary cache. No raw material/identifier logs.

## Substantive deployment and reviewer governance

`ModerationPolicyDefinitionSourceOwner` owns fenced immutable selection and
candidate reads and complete graph restoration. The publication writer delegates
to that reader; deployment and constitution readers depend on it directly.
Neither reader depends on the stateful publication writer or manufactures a
deployment owner to obtain a definition. This keeps future evidence composition
free of a publication-writer/evidence-owner constructor cycle. Original native
borrowing, serializable connection identity, provider tenant checks, host ancestry
and candidate predecessor/current-administrator checks remain binding. The
definition, deployment and constitution owners are concrete scoped services;
their registration supplies no activation or publication permission.

`ModerationProfileDeploymentSourceOwner` joins the actual lifecycle configuration
to that independently current deployment and adoption on the original native
evidence session. Its lifecycle callback rereads the production contribution
selection/profile-adoption proof; a publication borrow has no such callback.
The owner rereads configuration, full deployed runtime coverage and adoption
before returning the minimum existing deadline. It preserves the original
configuration's source identity, generation and contribution, never replacing
them with deployment coordinates. Changed artifacts, lost roles, revoked or
missing receipts and replaced/closed borrows make the result unavailable.
This scoped read supplies no operational prerequisite, conformance verdict or
activation permission.

`ModerationActivationSourceOwner` now composes that profile join and all five
concrete prerequisite owners on the same original native acquisition. It checks
the current context/transaction and exact compiled binding before and after
consumer or prerequisite reads. It routes only named prerequisites actually
required by the selected rule/mode. Consumer installation coordinates remain
independent of conformance. The shared `ModerationConsumerConformanceReader`
requalifies each required consumer's actual installed runtime, native signed
assertion, approved catalogue and primary/product/configuration identities.
Missing concrete owners or complete executed measurements remain unavailable.
Human discovery includes accountable actors for the latest installation of each
consumer/mode before acquiring sorted native human fences; this discovers
participants, not permission. The finite required inventory is not intersected with
available owners, so this composition does not enable incomplete profiles.

`ModerationMeasuredOperationsSource` captures an immutable actual operational
report for one exact profile/binding, including current primary identity/provider,
finite consumer measurement references, constitution/reviewer availability and
explicit missing recovery/key/transport evidence. Independent finite acceptance
acknowledges those exact contents, including gaps; it does not approve readiness.
The original-session owner requalifies the report and acceptance before returning
`MeasuredOperations`: every required consumer must be measured, canonical and
erasure stores must have completed recovery, and recognition additionally needs
current purpose-specific privacy adoption, recognition-store recovery and complete
key/replica convergence. Remote mode separately requires fresh authenticated
transport evidence. The operational proof port has no implementation registered;
missing actual owners cannot be replaced by digests, inventory support, authored
tests or an administrator's acceptance. Terminal withdrawal verifies original
receipts and current acting authority without requiring expired/departed original
participants to regain authority.

`ModerationDeploymentSource` captures the selected policy/profile/mode, exact
candidate or published definition, public primary-database identity/version and
complete required consumer installation/runtime/product generations.
`ModerationDeploymentSourceOwner` reacquires those actual owners through the
original evidence session. `ModerationDeploymentAdoption` requires a different
current administrator and exact finite source generation. Publishing a prepared
definition does not extend existing leases. Withdrawal remains possible after
expiry or artifact drift.

`ModerationConstitutionSource` retains normative jurisdiction, qualified evidence
and response, protected remedy routes, independent-controller review, concealed
evidence exposure, quorum, review intervals and accountable escalation terms.
Reviewer identity, qualification and independence grounds remain accountable
human assessments. `ModerationConstitutionAvailability` separately retains a
reviewer's self-recorded finite availability and maximum caseload. The native
owner checks its exact generation and current assignments; a roster does not
infer availability or conceal unresolved workload.

Constitution recording and adoption require different current administrators,
independent of the roster. `ModerationConstitutionSourceOwner` reacquires current
accounts/roles, source, adoption and availability receipts on the original
transaction and returns only the `ConstitutionAndReviewers` prerequisite.
It cannot establish another prerequisite, certify case-specific findings,
produce `Passed` measurements or enable activation. The qualification writer
retains source/receipt/epoch atomicity and post-save checks. The native receipt
constraint includes kinds 19–31 for deployment, constitution, recognition/privacy,
child/age, event-rights and measured-operation records;
provider migrations and runtime assurance remain programme-exit work.

Maintenance acquisition discovers deployment recorders/adopters/withdrawers and
constitution recorders/adopters/withdrawers, reviewers/controllers and
coordination owners, plus recognition/privacy recorders, privacy/recovery owners,
their canonical controllers and adopters/withdrawers for current policy publications.
Child/age recorders, independent reviewers, collection owners, their canonical/
governance controllers and adopters/withdrawers enter the same discovery before
locking actual user and role rows. Subordinate source acquisition never adds unfenced human
permission to the service principal. Production evidence composition still needs
every independently current prerequisite and measured consumer owner.

`ModerationChildAgePoliciesController` now exposes authenticated named source,
adoption and route-bound withdrawal POSTs under `/api/moderation/child-age-policies`.
The three concrete native CQS handlers remain the sole write path: manually
instantiated validation precedes exact native mutation, and current principal/
tenant/controller/qualification facts are independently acquired by the owner.
Top-level command JSON rejects unrequested members, including individual DOB
and claimed permission. Reviewed policy terms do not accept subject evidence.
The 256 KiB request ceiling covers twelve 2048-character grounds even when JSON
escapes every character; native term bounds remain tighter. Write limiting,
response/output no-store and idempotency response suppression preserve existing
privacy boundaries; native receipts own replay, not cached command bodies.
RFC 7807 maps unavailable/denied/conflicting native outcomes. Withdrawal replaces
body adoption intent with the route ID before validation. Twenty-three transport
cases are authored UNRUN/unbuilt; source compilation is not native authority or
collection readiness. HAL/editor/export/generated and hosted assurance stay open.

Measured-report recorders, nominated reviewers, their controllers and acceptance/
withdrawal authors are also discovered before the same human/role fences.
Native CQS discovers the three report/acceptance/withdrawal handlers through the
existing fixed Application operation graph, without a second registration path.

## Substantive recognition and privacy governance

`ModerationRecognitionPrivacySource` binds adopted purposes and obligations to
one host, trust domain, tenant, policy revision, publication, profile and mode.
The governing administrator records substantive identity, independent-controller,
privacy/legal and recovery mandate grounds. The nominated privacy owner must
independently adopt that exact source generation; a current administrator role
alone supplies neither those grounds nor acceptance. Native current authority
is rechecked for the recorder and privacy/recovery owners, with all controllers
retained in the original acquisition's human fences.

The four complete record classes distinguish pending computed recognition,
eligible matching material, protected correction evidence and disposition/replay
receipts. Matching is limited to the exact final active sanction purpose, scope,
revision and interval; it ends on reversal, expiry, purpose loss or the finite
retention ceiling. Matching and pending material cannot acquire legal holds.
Only independently justified correction evidence or replay receipts can carry
finite, reviewed holds, without extending enforcement. Calendar remedy floors
and backup replay obligations remain separate from the sanction interval.

Received-but-unsettled capture requires escalation before the adopted finite
lawful-request deadline and qualified recovery or legal disposition. Ordinary
erasure remains with its existing authority and does not depend on this gate.
Readable key obligations cover original matching retention plus backup horizon
and safety margin; they do not permit rehashing erased inputs or prove actual
replica/key convergence. No provider identity, matching token, subject history,
contact, DOB or secret key material is stored in this policy source.

`ModerationRecognitionPrivacySourceOwner` reacquires immutable definitions,
source/adoption receipts and current authority before and after observations on
the original `ModerationConsumerEvidenceSession`. Source, request-bound receipt
and qualification epoch commit atomically, with post-save/replay checks.
Independent adoption reads do not trust a caller-retained source snapshot;
withdrawal remains possible after expiry or source replacement. Its concrete
scoped registration supplies only the `RecognitionAndPrivacy` prerequisite when
closed routing uses it. It does not certify legal status, implement capture or
erasure settlement, declare `Passed`, or activate moderation. Those runtime
owners, complete routing and generated provider migrations remain mandatory.

## Substantive child and age governance

`ModerationChildAgePolicySource` records reviewed child-risk, privacy/legal and
classification grounds for one exact host, trust, tenant, policy revision,
publication, profile and mode. The nominated independent reviewer must hold
both privacy/legal and child-safeguarding mandates, separate from recorder and
collection owner. Canonical-controller and independence grounds are explicit;
current administrator status does not replace these accountable statements.
The reviewer independently adopts that exact finite source generation.

Typed obligations require lawful minimum collection purposes and a policy-bound
pre-collection gate, actual-subject consent/guardian prerequisites, correction
and erasure. Age assurance distinguishes qualified local manual threshold
evidence from declared DOB; a reviewed optional provider cannot become a forced
universal ID/face requirement. Unknown, contradictory or expired evidence stays
restricted, not guilty. Content classification is reviewed, not inferred from
event format, religion or behavior. The whole event remains the audience unit
unless complete, separately qualified segregation exists; purchaser adulthood
does not qualify participants. Debate's floor of 18 is adopted ISLAMU policy,
not universal legal or religious certification.

The currently accepted calendar is Gregorian UTC with March 1 adulthood for a
February 29 birthday in a non-leap year; the adoption records its jurisdiction
rationale. Threshold crossings require reconsideration. DOB and assertion
correction may change eligibility without restoring unrelated rights, erased
history or recognition-carried DOB. Ordinary erasure, safe catalogue information
and protected remedies remain independent.

`ModerationAgePolicy` supplies the shared declaration-only calendar model.
Its immutable policy ID/revision/jurisdiction and explicit `TimeZoneInfo` determine
the local date of the supplied instant, never the server or viewer calendar.
The default is UTC with March 1 for non-leap February 29 anniversaries; an explicit
alternative remains proposed semantics, not independently accepted governance.
Evaluation returns only Unknown, UnderThreshold or AtLeastThreshold. It retains
no DOB, and AtLeastThreshold is a declaration result, not assured adulthood or an
access grant. Missing/future DOB is Unknown; corrected input is recomputed without
memoized earlier eligibility. Threshold comparison avoids constructing an
out-of-range future anniversary. Native policy selection, lawful collection,
current assurance/guardian evidence, expiry and audience enforcement remain open.

`ModerationIndividualTarget` distinguishes an instance-scoped canonical Human
from one opaque registration-session/individual slot. `ModerationIndividualBinding`
retains exact policy/jurisdiction/purpose/source coordinates, a Guid observation
stamp, a separate ordered long sequence and a finite UTC window.
`ModerationAgeEvidence` stores only threshold metadata. Local manual provenance
rejects known self-attestation/controller equality and unsupported vendor claims;
native identity, controller independence and qualification must still be proved.
`ModerationCollectionReceipt` requires a native applicability assessment and its
exact account/subject-consent/guardian-consent references, even when the reviewed
required set is empty. It snapshots those sets and cannot borrow purchaser consent.
Both records match independently reacquired metadata within their original
half-open windows. Correction/withdrawal/expiry/erasure retires current use and
clears usable bindings/provenance; no historical evaluation or renewal revives it.
These shared Domain models supply no native grant, signing endpoint or body gate.
Native session ownership, consent, qualified manual issuance, source/adoption,
persistence, pre-body collection enforcement and derived-state purge remain open.

`ModerationChildAgePolicySourceOwner.AcquireCalendarAsync` now bridges independently
adopted native semantics to `ModerationAgePolicy`. It reacquires source and adoption
on the original live transaction/session, retaining current context, roles,
definition, request receipts and source-generation fences. Its minimized observation
includes exact source/adoption identities and ordered generations, accepted age
floors and the adoption's original finite deadline. Validated native terms currently
map only Gregorian UTC/March 1; a proposed alternative cannot enter via this reader.
Absent/withdrawn/stale adoption returns no calendar. This is normative calendar
provenance, not activated collection, authenticated individual assurance or a grant.

`EventContentClassificationVersion` freezes taxonomy lookup/category meaning,
reviewed floors, independent human/controller coordinates and exact normative
source/adoption generations separately from EventType. `EventContentClassification`
reviews immutable category snapshots and immediate child inventory against native
Event/session/resource GUID ancestry and finite coverage. Correction/withdrawal
is terminal, including for historical-time evaluation; replacement needs new review.
`ModerationAudiencePolicyCompiler` requires every supplied material's exact current
review, category versions and complete declared ancestry. It takes the maximum
floor across the whole event, including the adopted Debate floor. Unknown,
withdrawn, changed or incomplete metadata returns ReviewRequired with no partial
floor. There is no format inference, permissive segregation switch or access result.
Native material inventory/stamping, persistent material review/concurrency and
actual audience consumers remain open; supplied coordinates cannot prove ownership.

Native taxonomy source/adoption code now binds 1-64 categories to the exact adopted
constitution and child-age sources. The actual coordinator must belong to the
qualified roster; a nominated distinct-controller qualified reviewer separately
accepts the proposal with current self-recorded availability. Operators must
substantively qualify content meaning, child risk and floor-review competence in
that independently adopted roster; administrator status and mandate text alone
do not qualify. Logical taxonomy heads replace the entire inventory, and native
selection checks the newest head before acceptance: expiry, withdrawal or pending
replacement cannot reveal older approved meaning. Exact request-bound receipts,
source/adoption generations and original-transaction authority are rechecked at
dirty saves, clean replay and final identity flush. Current scoped correction can
withdraw after historical owner departure without changing birth provenance.
`IModerationClassificationTaxonomySourceOwner` resolves the same scoped concrete
owner. HTTP/export/generated-provider integration and physical assurance remain
open; current taxonomy metadata is not material approval or an audience grant.

Three immutable CQS commands now expose native proposal, adoption and withdrawal
through manually instantiated validators and thin handlers. Validation rejects
malformed UUID/revision/lease intent, absent or duplicate category inventories and
non-independent review coordinates before dispatch. It does not qualify supplied
mandates or decide the adopted floor; the native owner independently rechecks both.
Withdrawal intent contains no replacement lease or renewed collection prerequisite.
`ModerationClassificationTaxonomiesController` exposes three authenticated named
POSTs under `/api/moderation/classification-taxonomies` for sources, adoptions and
route-bound adoption withdrawal. All delegate to the corresponding native CQS
owner, retain write limiting/no-store and suppress idempotency response storage;
native receipts own retries. Top-level command JSON is closed. A 2 MiB request
ceiling covers the 64-category maximum, including escaped 2048-character meanings
and rationales. RFC 7807 mapping carries only qualification outcomes, not review
text. Editor/export, regenerated clients/OpenAPI and full native HTTP execution
remain open. The API-only compile uses previous dependency artifacts while the
material worker owns changing source; it does not verify new Application JSON
attributes or native material changes. Full integrated compilation remains queued.

Native material proposals now have immutable `ModerationClassificationMaterialSource`
inventories and separate finite `ModerationClassificationMaterialReview` acceptance.
The scoped `IModerationClassificationMaterialSourceOwner` resolves to the same
concrete owner as its native registration. It acquires the original context/
Serializable transaction, exact current Event generation, independently adopted
taxonomy and child-age sources, current Event owner/manager assignment and
independent reviewer/controller provenance. Caller component lists must match the
complete native inventory; they never assert completeness. Newest whole-Event
head selection precedes acceptance checks, so replacements and terminal endings
cannot reveal old approval. Post-expiry correction preserves birth receipts.

The finite carrier inventory now covers 41 native kinds. Persisted scalar
fingerprints detect raw/unstamped descendant edits; tracked native mutations also
advance the Event GUID stamp before ordinary concurrency preparation. Parent
review found that `Actor` scalars omitted separately persisted `ActorPii` display
text and profile media. The corrected inventory includes only that public actor
profile carrier and its referenced managed storage, not `UserPii`, DOB, participant
answers or identity evidence. Tracked actor-profile edits invalidate affected
Events/session speakers; raw display-name or external-picture substitution changes
the scalar fingerprint. Three additional real native cases guard those paths,
bringing this unit to 16 Domain/39 native cases, all UNRUN/unbuilt.
Current material projection supplies the existing pure audience compiler only;
it issues no age assurance, collection consent, audience grant or conduct finding.
Current-inventory reads/editor/export, generated providers and all actual terminal
audience consumers remain open. Integrated full-dependency Persistence source
compilation passed; this does not validate EF model/provider/runtime behavior.

Three immutable material CQS commands now map proposal, independent acceptance
and accountable ending to the existing native source/review writer. Manual
validation checks exact typed coordinates, finite bounds, independent mandate
structure and unique material/component/category selections; the body cannot
establish inventory completeness, actual Event ownership or reviewer permission.
Material commands require a Tenant target, matching the existing native Event
scope contract. Top-level unknown JSON members are rejected. The Application
operation graph discovers one handler per command without separate registrations.
Twenty-five Application cases remain UNRUN/unbuilt; CQS source compilation
passed. Native current-inventory transport, protected final use and all broader
classification/audience integration remain open.

`ModerationClassificationMaterialsController` now exposes authenticated named
POST sources/reviews/reviews/{reviewId}/end through those CQS handlers. No-store,
write limits, suppressed idempotency response storage and RFC 7807 mapping follow
the existing source-governance boundary; ending uses the route review ID.
The 16 MiB ceiling supports both 4096-entry bounded inventories, 64 category
references per material and escaped maximum review grounds. Native acquisition
still verifies every component against real rows. A registered API-owned JSON
converter emits and accepts only the material's six native coordinates, never
its computed `Parent` or supplied completeness/approval. Child coordinate pairs
must be coherent; duplicates, unknown fields and null material fail closed.
The native/Swagger transformer publishes the exact same closed component.
Twenty JSON/twenty-two controller/two hosted schema cases remain UNRUN/unbuilt;
API source compilation passed. Review/editor/export/
generated contracts, actual HTTP and all current audience consumers remain open.

Material current-inventory transport is now authored separately from source
mutation. Its authenticated bounded POST accepts candidate mandate/native
aggregate coordinates only. Server tenant/human capture, deferred native
acquisition, private DTO/HAL/JSON preparation and a final original-lease check
precede the actual body write. The lease must survive delivery; it supplies
finite taxonomy category metadata, six material coordinates and payload-free
component digests, not DOB, raw payloads, approval or an audience grant.
The source-record action is gated by internally mapped native owner
qualification, without claims inspection. The native owned read now borrows the
actual tenant-authoring transaction/evidence session and current nominated
humans. Original selected policy/epoch, real owner assignment, independent
reviewer/controllers, finite governance/age/taxonomy and complete material/
component set/counts remain bound through final output. The unfiltered latest
Event source generation is compared under its slot fence; revalidation never
replaces expected coordinates. Disposal closes the original read transaction
and restores its tenant/trust frame. No new writes, reservations, epoch advances
or commits are introduced; existing no-op native DML fences remain.
Thirteen Application/twelve API/thirty native cases are authored UNRUN/unbuilt.
App/API/native source compilation passed; complete native/HTTP
race execution remains pending. Editor/export/generated
contracts and actual audience consumers remain open.

The separate nominated-reviewer source read now selects only the exact current
unaccepted proposal. It captures server tenant/human and borrows the current
native authoring/evidence transaction; publisher/manager or unrelated moderator
identity cannot substitute for the nominated independent reviewer. Original
proposal receipt, actual complete material/component inventory, finite policy/
taxonomy/age/governance, assignment/controller qualification and unfiltered
latest Event head remain mandatory. Any retained acceptance closes this
candidate read. Final checks also compare original source mandate/validity and
ordered material/category selections; no stale draft or approval is revealed
after replacement. Private Application/HAL/JSON preparation precedes the final
native check and actual delivery under retained fences. Only mapped native
reviewer qualification exposes acceptance; the subsequent command reacquires
authority. This is metadata, not resource disclosure or an audience grant.
Thirty-one native/eight Application/three API cases are authored UNRUN/unbuilt;
App/API/native source compilation passed. Actual body,
privacy/provider/concurrency witnesses, editor/export/generated contracts,
retained correction views and audience consumers remain open.

Current moderation transport declarations have been natively exported and the
per-tag Client regenerated through private NSwag/record normalization and exact
capture publication. Export/Client compilation passed; no generated contract
was hand-edited. Actual native/Swagger/HTTP interoperability, source body and
provider/race assurance remain UNRUN, and guided classification/age controls and
audience enforcement are not supplied by generation alone.

Material authoring transport preserves a detached original inventory/proposal
and uses only qualified HAL source/acceptance links. A prepared operation keeps
its body, operation/result identity and original HTTP context; uncertain writes
may be explicitly replayed without a permission refresh or generation renewal.
The repaired native HAL catalogue makes inventory/material selection/component
arrays use the existing generated contracts directly. Native export and
transport compilation passed; 36 authored HTTP cases and two typed-HAL schema
cases remain UNRUN.

### Existing-Human profile collection ingress and private declaration

The new source-defined existing-Human `ProfileCompletion` path acquires native
collection authority before MVC can read the names or DOB body. The endpoint
marker runs after authentication/authorization and before idempotency body
fingerprinting. `X-Profile-Collection` contains bounded, strict assessment and
optional consent coordinates, not an authorization assertion. The admission owner
reacquires the actual self, selected publication, original observation, latest
independent assessment and complete required prerequisite set. Only the currently
implemented empty set or subject-consent set can proceed; account-eligibility and
guardian-consent requirements remain closed, never omitted.

The privacy owner retains its actual colocated primary transaction, native source
fences and original finite deadline through body consumption, disclosure flush and
commit. Direct name handlers require that private current owner. Response Start,
Complete and PipeWriter output stay in a bounded 64 KiB private response feature;
pending output must seal successfully before commit and only sealed bytes are
delivered afterward. Deadline expiry rolls back the mutation and becomes an
unavailable response; original caller cancellation remains cancellation. No body
callback is automatically replayed. Separate-primary topologies unsupported by
the individual acquisition remain unavailable.

`GET /api/User/{id}/profile-completion` returns self-only nomination coordinates,
their original expiry and declaration revision/presence, never the declared date.
HAL advertises names/DOB actions only after native requalification. The browser
refreshes this preflight before opening a personal-information form, requires an
explicit selection when several contexts exist, and preserves the exact selected
assessment rather than choosing a different context for the same profile. DOB
drafts are cleared on cancellation, expiry, lost collection links and disposal.
An unavailable selection is not silently replaced by a new singleton; selection
is disabled while preflight refreshes. Changing context without an open form does
not move keyboard focus. Ordinary user resources
advertise preflight rather than an unconditional names-edit affordance.

Private DOB recording uses `UserPii` revision CAS and one current
`UserBirthDateCollectionReceipt` in the same primary transaction. The receipt
retains minimal original policy/observation/assessment/consent coordinates, no DOB
history and no reusable permission. Its lifecycle belongs to `UserPii` by cascade.
Repeated correction within one admission replaces only that subject's saved
receipt, detaching the obsolete owned tracking entry before reusing its identity;
unrelated or unsaved tracked changes are never discarded.
Bodyless removal requires current native self authority and revision CAS but not
live collection consent; a retained erasure fence denies further self-service
access while the independent erasure workflow remains responsible for cleanup.
Declaration is not assured age and does not establish adulthood or eligibility.

The retained-authority erasure path reaches the generic `UserPii` repository;
`ForgetPiiAsync` is not that production call chain. On SQL Server, the portable
model policy normalizes the observation, assessment and consent ownership edges
to `NoAction`, so an eventual PII delete cannot be relied on to dispose them.
`UserLocationPrivacyErasureRepository` now explicitly removes subject-owned
collection consents, assessment adoptions, assessment sources and observations,
in dependency order, after its exact Serializable saga/subject-fence check and
before PII deletion. It selects only bounded identifiers and keeps every delete
subject-bound across trust/tenant, expiry and withdrawal filters. It does not
remove administrative procedure/age definitions or another subject's metadata.
Existing recognition-before-purge, retained erasure facts, recovery accounting,
primary rollback, settlement coverage and replay ownership remain unchanged.

Four new whole-workflow cases are authored UNRUN/unbuilt: current authority,
retired consent/policy, previously read metadata, and a real disposal failure
followed by fresh runtime replay. The pre-PII command checkpoint requires the
metadata to be absent before deleting PII, so SQLite cascades cannot mask a
missing owner operation. This is source-level coverage, not an executed workflow
or SQL Server engine witness. Bounded source review corrected shared-vocabulary,
withdrawal-result and post-reconciliation tracking witnesses, with no further
attributable material finding. Disposal detaches only selected, unchanged tracked
rows after successful deletion, preventing a later PII cascade from deleting them
twice; pending selected changes fail closed rather than being discarded. Initial
Persistence compilation passed with 62 warnings and no errors in 2:39.05; the
saved-tracker follow-up passed with 62 warnings and no errors in 3:45.07. Roslyn
diagnostics remain unavailable. No model/migration or public contract change is
introduced, and no test or provider assurance is claimed.

Readiness now has a source-defined, private `DeclaredAge` display for each qualified
collection option. The native admission owner computes it inside its actual
acquisition, using the already qualified AG04 parents and the fenced subject PII.
The current PII-owned receipt must match the original observation, independent
assessment/adoption, consent, policy/revision, publication and profile, as well as
the subject and PII receipt link. Missing or expired originals produce `Unknown`;
they are not silently revalidated for another context. The adopted Debate minimum
is evaluated using its UTC/March-1 calendar. The display contains only kind,
threshold outcome/minimum, declaration revision, evaluation time and finite end—no
DOB, exact age, subject/issuer identifiers or reconstructed provenance. It can only
be `Declared` or `Unknown`, never `Assured` or an audience/admission permit.

No second age row is persisted. Correction and clearing recompute against current
PII and its current receipt; required withdrawn consent withholds the qualified
option. The display ends at the earliest current admission, original receipt or
next UTC day boundary, without renewing any original. Source review identified
that later awaited admission checks could outlive that shorter display window.
`ReadOptionsAsync` now reads a fresh terminal clock after all awaited work, removes
expired options, and withholds an expired `DeclaredAge` without pretending that
the still-live collection admission renewed its shorter display window. The
browser presents dated read-only wording and keeps all actions HAL-gated.
Fifteen additional native cases, one private-contract whitelist case, one named
OpenAPI/string-state contract and two actual generated-transport/rendered cases
remain UNRUN/unbuilt. Initial Application/Persistence/API compilation passed with
182/62/2 unsuppressed warnings and zero errors (41.38s/3:23.69/32.05s). The exporter
logged the existing lifecycle scan failure with durable state retained; that is
not runtime health evidence. Final expiry-fix Persistence compilation passed with
62 warnings/0 errors (3:57.54). Both enum types now use the existing string-enum
catalogue; API export compiled with 2 warnings/0 errors (25.44s). The first browser
build found an undefined local `Selected`; using the actual `SelectedOption`
removed that error without suppression. Final browser compilation passed with
230 warnings/0 errors (43.96s). Native Debug generation and complete Release/Debug
schema/client/mutable-policy comparisons passed
`DECLARED_AGE_RELEASE_DEBUG_CAPTURE_PARITY_OK`. These are compilation and artifact
receipts, not native, browser, accessibility or provider assurance. Manual
assurance, participant/audience use and other intake aliases remain open. This
display adds no schema migration or collection purpose.

Avatar mutation has its own `/api/User/{id}/profile-image` operation and ordinary
self/provider/storage checks, independent of collection consent. The names DTO no
longer contains the image field. Browser actions use only HAL links and generated
clients; no role inspection or compatibility path restores the former mixed body.

This is implementation source, not runtime assurance. Authored admission,
declaration, pre-body, response-buffer, transport and rendered cases remain UNRUN
and unbuilt: 23 admission cases, seven declaration cases, seven ingress/response
cases, two private-contract cases, one unauthenticated image-handler case, two
generated-client transport cases and ten rendered settings cases. The generated
date-only wire contract uses a `DateTimeOffset` client property; the handwritten
adapter constructs UTC midnight and the generated date converter emits only the
calendar date. The additional declaration case calls the real
`UserRepository.ForgetPiiAsync` and asserts that deleting the subject's `UserPii`
cascades to its current receipt without deleting the user or another subject's
PII. That repository regression remains UNRUN; it is not evidence that the
erasure worker invokes this path. Current provider migrations, hard-erasure execution, positive hosted
HTTP-to-native evidence and complete audience/DOB-derived invalidation remain
open. Other enrollment, provider and participant collection aliases are not
declared complete by this existing-Human slice. The earlier receipts below record
the state before this source integration.

Product-only compilation passed for the latest Persistence (62 unsuppressed
warnings/0 errors), API (2/0) and Blazor Client (230/0) sources. The first browser
pass exposed an accessibility mismatch between the public JSON context and its
nomination model; fixing the handwritten model removed the error without touching
generated output. The actual NSwag/native generation pipeline produced the named
contracts, and authoritative schema/client/policy inputs match both Release and
Debug captures. These receipts establish compilation and artifact parity only;
they do not establish native, browser, accessibility or deployment conformance.

A profile-procedure Domain model describes the first supported existing-Human
`ProfileCompletion` vocabulary. Preliminary applicability/reference fields are
distinct from normal DOB/name/email fields; normal DOB is explicit, while
names/email have no defaults. Procedure source and independent adoption retain
exact accepted child-age parents, nominated owner/reviewer and original finite
writer coordinates. These structural models and their Application nomination
contracts do not authenticate applicability, consent, guardianship or native
collection permission. Native persistence/currentness, pre-body acceptance,
individual receipts and correction/erasure integration remain open. Sixty-one
Domain cases are authored UNRUN. The distinct `GuardianConsentReference` is now
separate from `GuardianRelationshipReference`; the bounded preliminary vocabulary
allows five explicit gate fields without silently truncating an extra field.
Relationship proof never satisfies guardian consent. Earlier Domain/Application
compilation predated this vocabulary change. Settled Persistence compilation
and current native API/Client compilation and exact captured-contract parity passed.

Native materialization is now prepared with private setters/constructors, real
original adoption coordinates and immutable scalar policy snapshots. EF maps
bind parent/source identities, unique writer/adoption slots, finite windows,
revision-CAS withdrawal and immutable after-save state; persisted-state
validation retains the factory boundaries. These later mapping edits have
clean diagnostics and passed the combined native qualification product build;
actual primary-provider/malformed-row execution remains UNRUN. They do not turn procedure acceptance
into individual consent or authorize sensitive-body collection.

Native procedure qualification now binds source recording to the accepted
child-age parent's actual collection owner, independent acceptance to its
nominated reviewer, and retained withdrawal to exact original receipts.
Current acquisition checks latest source/adoption slots, canonical participants,
policy/publication/profile/mode and request-bound writer coordinates on the
original connection/transaction. Before-save and final-commit checks retain that
same evidence session, including the outer colocated privacy commit; invalid
latest sources never fall back. Sixty-one real-primary witnesses are authored
UNRUN and integrated Persistence compilation passed. Typed CQS integration is
complete; three authenticated bounded HTTP procedure writes, fifteen Application,
25 API and two native/Swagger schema witnesses are authored UNRUN. Targeted
API compilation/native export and generated Client compilation passed after the
full dependency sweep timed out. Guided administration,
non-administrator individual applicability/consent and before-body collection
remain open; this owner provides procedure policy metadata only.

`ModerationProfileSubjectConsent` now models one private existing-Human
`ProfileCompletion` self-consent operation. It requires the same bound Human,
an explicit assessed `SubjectConsent` requirement and a permitted preliminary
reference field. It retains the complete original assessment, binding and
procedure/parent coordinates with a finite window; its own consent generation
is not compared to qualification-writer sequences. Metadata conversion supplies
only the subject-consent prerequisite, never guardian/account eligibility or a
complete collection receipt. Withdrawal is terminal subject-only revision CAS,
available after parent/consent expiry without renewing the original grant.

The concrete profile consent also rejects trust-wide or resource policy scopes
and a tenant jurisdiction different from the original procedure's tenant.
These are structural Domain boundaries, not native authentication or independent
assessment acceptance. Ninety-six cases are authored UNRUN; fresh source/test
diagnostics are clean and the corrected Domain product build passed. Persistence,
actual subject/assessment/latest-generation owners, sensitive-body gating,
correction invalidation and PII hard-delete integration remain open. No consent
endpoint, guardian proof or permission to collect data is supplied by this model.

Consent storage source now maps `moderation_profile_subject_consents` to the
actual shared-key `UserPii` owner with cascade hard deletion, while retaining the
original procedure source with restrictive deletion. Original provenance is
immutable after save; only terminal revision/withdrawal fields remain writable.
The consent revision is a concurrency token, and subject/source/consent-slot
generation is unique. A named moderation filter requires the original source's
exact trust and tenant. Specific bounded binding/assessment columns reconstruct
Domain factories from explicit coordinates; they do not use API converters or
naive CLR deserialization. Scalar UTC ticks make finite-window ordering portable.

Thirty-nine relational witnesses are authored UNRUN. Fresh diagnostics are clean,
and combined compilation passed, but materialization, provider/malformed-row behavior,
migrations and erasure-job execution remain pending. Constructed applicability
references in these mapping tests are explicit structural preconditions, not
authenticated decisions. This mapping does not expose a consent writer or
authorize collection.

Profile applicability source/adoption models now require an explicit original
Human/purpose/policy binding and complete required set. The nominated collection
owner records; the independently nominated reviewer accepts, with subject
self-review and controller conflicts rejected. Required kinds must have the
procedure's explicit preliminary reference vocabulary. Guardian requirements
need both separate relationship and consent references. An empty set becomes
accepted metadata only through the same independent adoption, not a default.

The accepted assessment uses its adoption's original ID/revision/individual-slot
generation and finite acceptance window. Changed observations, sources, parents
or requirement sets cannot reuse it; terminal original revision withdrawal
remains available after expiry without granting anything new. One hundred six
Domain cases are authored UNRUN with fresh diagnostics clean. Native lawful
determination, identity/latest-generation fences, erasure-job integration,
prerequisite ownership and pre-body enforcement remain open.
Combined compilation passed with the new consent map and guardian vocabulary;
runtime and provider/model behavior remain UNRUN.

Applicability storage now maps private proposals and acceptances beneath the
actual shared-key PII owner: `UserPii -> assessment source -> adoption` is the
single cascade deletion path. Procedure history and actual recorder, reviewer
and controller relations use restrictive foreign keys. Four immutable scalar
originals expose the procedure source, subject and two controllers for real
relational keys; Domain validation checks them against the original binding
and procedure. The subject/procedure/assessment generation slot is unique,
and each source has at most one acceptance.

The complete prerequisite set uses an explicit bounded mask from 0 through 7,
including positively empty 0. Binding reconstruction uses the unchanged
factory-based binding codec; the consent-only assessment codec is not reused.
Named `ModerationProfileAssessment` filters require exact resolved original
trust and tenant even when generic tenant filtering is bypassed. Only terminal
adoption revision/time/actor may change after save; revision is a concurrency
token and original UTC windows remain finite. Seventy-four relational cases
are authored UNRUN with fresh diagnostics clean. Combined storage compilation
passed; materialization, provider/migration and native erasure-job assurance
remain pending.

Native terminal assessment withdrawal now has its own scoped repository
registration. It discovers the retained source, resolves the actual local/provider
Human, checks original nominated correction authority and revision, and retains
real privacy-owned serializable row fences through the actual outer
`beforePrimaryCommit`. Identity/login, subject PII/privacy, tenant/trust,
host/epoch, source, terminal fields and cancellation are checked again there.
Expired or withdrawn parents and newer unaccepted proposals do not obstruct an
exact historical withdrawal; no applicability/consent/collection receipt is
created. Thirty-nine native cases are authored UNRUN with fresh diagnostics
clean; new storage/withdrawal compilation and provider behavior remain pending.

Proposal and independent acceptance still need an original authentic subject
observation distinct from the acting collection owner/reviewer. The existing
self-acquisition does not authorize another subject. Parent-policy authors'
current administrator qualification is preserved; the approved actual-subject
gate does not require weakening it or making ordinary subjects administrators.
The original subject-observation unit is now source-complete, not an accepted assessment.

`WithdrawModerationProfileCollectionAssessmentCommand` now maps the exact
retained intent into the native corrective repository. Its handler manually
instantiates validation, forwards cancellation and returns only the committed
adoption ID. Invalid scope/aggregate UUIDs, aliased source/adoption/subject IDs,
unknown mode, nonoriginal revision and unordered assessment generations never
reach the owner. Human IDs are not incorrectly restricted to aggregate UUIDv7.
Unavailable or unqualified retained targets share the standard not-found
response, rather than exposing private target existence or demanding an
administrator role for historical correction. Four Application cases are
authored UNRUN; three new files have clean fresh diagnostics and passed the
combined Persistence product compile. Native operation discovery supplies scoped
handler registration. Corrective HTTP source is integrated; API compilation/
export and guided action affordances remain pending.

`ModerationProfileCollectionSubjectObservationOwner` now persists the actual
authenticated existing Human's nomination under the original identity-owned
serializable transaction and final commit callback. The nomination contains only
profile and native mode; subject, scope, selected ACTIVE policy, accepted
procedure/parents, stamp and finite window are native values. The owner stays
alive through final principal/provider-login/PII/privacy/tenant/trust/host/epoch/
policy/parent/expiry/cancellation rechecks.

Immutable scalar observations live beneath `UserPii`, contain no provider account
string, DOB, email or age assertion, and retain an optional original login-row
identity plus original policy/selection/procedure digests. Their own
Human/profile/mode slot sequence is serialized and cannot reset at a policy
change. Later nominated-owner/reviewer reads retain the original subject, reject
supersession or changed originals without fallback and never renew the original
window. `ToBinding()` converts metadata only. The private named filter rejects
missing/foreign trust or tenant and generic bypass. Thirty-five Domain and
fifty-three native cases are authored UNRUN. Scoped owner registration is wired;
combined Persistence compilation passed (exit 0, 6m56.43s). Child diagnostics were
clean; parent requests for fresh diagnostics timed out. Strict
`ObserveModerationProfileCollectionSubjectCommand` and its manually validated
handler now map only profile/mode into this owner. The authenticated no-store,
write-limited HTTP nomination endpoint rejects subject/time/authority assertions.
Five Application and eleven API cases are authored UNRUN; new transport
diagnostics are clean. API compilation/export remain pending.

Native assessment proposal/acceptance source now uses the original observation
while authenticating the nominated owner/reviewer as a distinct acting Human.
The existing scoped repository gained inventory-backed acquisition and native
record/adopt methods without changing terminal withdrawal. The subject/procedure
slot owns serialized CAS generations and latest-source selection; invalid latest
sources do not fall back to an older accepted source. Exact required sets include
positive empty, but neither record nor acceptance supplies consent or a collection
receipt. Original windows, identity/private state and accepted parents are
reread through the actual outer before-primary-commit callback; existing graph
references attach unchanged. Ninety-eight native cases are authored UNRUN/unbuilt
with child diagnostics and whitespace clean. Parent product and complete native
witness review are complete. The initial integrated API product build passed
exit 0 in 8m00.27s; owned whitespace passed and context remains beneath 15,000
bytes. New record/adopt CQS maps strict nominal original coordinates, explicit
copied complete requirements, source revision/generation and native slot CAS.
The handlers manually instantiate validation and return only the committed ID.
The authenticated no-store assessment controller now records and accepts through
separate named POSTs. Unknown fields, sensitive inputs and claimed subject/time/
authority fail binding at the outer and original-object levels. Eight further
Application and thirty-eight API cases are authored UNRUN/unbuilt; their fresh
diagnostics are clean. Combined record/adopt compilation passed exit 0 in
7m18.68s; current native export and generated Client compilation also passed.
Four named HTTP paths and strict outer/nested schemas were inspected; exact
Release/Debug capture parity passed for those four boundaries.

Native `ModerationProfileSubjectConsentOwner` now grants only actual canonical
original self against a genuinely independently accepted native applicability
assessment demanding SubjectConsent. An explicit positive-empty or other-only
assessment cannot authorize gratuitous consent. The internal assessment reader
selects latest source identity before qualification, then its exact independent
live adoption; tied, withdrawn, expired, substituted or unaccepted latest state
has no fallback. Original observation and all parents are reread through the
actual acquisition, not reconstructed from consent snapshots or client scalars.

Consent uses its own subject/procedure slot, generation CAS and unchanged finite
original windows. New IDs cannot renew an already granted original, including
expired/terminal consent. The private PII-owned graph attaches existing parents
unchanged. Grant and withdrawal retain the sole real enrollment transaction,
identity scope and fences through the true outer before-primary-commit callback.
Final rereads reject identity/provider-login/private-PII/privacy/host/tenant/trust/
epoch, observation/assessment/parent/policy, slot, expiry or cancellation changes.
Historical withdrawal uses original canonical self and current private identity
fences without demanding live policy/observation/assessment/parent availability.
Only revision 1 to 2 and terminal time change; historical generation is distinct
from the latest slot CAS. It cannot grant, renew or restore collection authority.

Scoped DI, manual CQS validation and strict authenticated grant/withdraw HTTP
boundaries are integrated. Unknown members fail at outer, assessment and original
levels; sensitive input and supplied subject/required-set/grant time cannot bind.
The route replaces body consent ID before validation. Handlers expose only the
committed ID; responses are bounded/no-store/private and not replay-stored.
Eighty-six native, seven Application and forty-two HTTP cases are authored
UNRUN/unbuilt; complete parent review and fresh new-source/test diagnostics are
clean. Existing registration using warnings remain, unsuppressed. Integrated API
compilation, current native export and regenerated Client compilation passed in
7m06.04s/6.94s/46.11s. Actual new paths/closed schemas/Client methods were read;
Release schema bytes match and Debug capture refreshed. Current six-operation/
seven-comparison complete JSON/schema gate passed. AccountEligibility, GuardianConsent/relationship, authenticated
prerequisite reads, collection receipts/body gates, migrations and provider/
concurrency/runtime assurance remain open. Returned consent is source metadata,
not authenticated prerequisite proof.

`ModerationProfileCollectionIndividualAcquisition` now provides a private
non-administrator policy-evidence acquisition for an actual existing Human.
It borrows only the real colocated identity-fence-owned serializable transaction,
resolves canonical local/provider identity, and retains the original principal,
provider binding, connection, tenant/trust/host, epoch and selected ACTIVE policy.
The existing source owners independently qualify the accepted procedure and
child-age parents; their administrator requirements are not weakened.
Revalidation compares original source/adoption/definition/selection and finite
observation coordinates, never a replacement or renewed lease. Disposal ends
currentness without committing or starting a transaction.

Forty-one native cases are authored UNRUN, with clean fresh diagnostics and
combined Persistence compilation passed. Their trusted persisted ACTIVE-policy
starting state is explicitly seeded from a real Draft graph; real parent/
procedure writers and the real privacy owner are then exercised. This is not an
activation witness or measured publication-evidence composition. The private
acquisition is not a collection receipt or public endpoint: applicability,
native consent/guardian owners, pre-body enforcement, PII persistence/erasure
and retention through each future writer's actual outer commit remain open.

`UserPii` now retains optional declared DOB, collection receipt ID and a separate
correction revision in private CLR fields. EF maps those fields to the existing
hard-deleteable PII owner and makes the correction revision a concurrency token;
they are not public serialized properties. Structural mutation requires the exact
Human, identity enrollment/profile-completion purpose, current receipt assessment
and expected revision. Clearing needs no live collection or consent; ordinary
erasure deletes the PII row. A declaration does not create assured evidence.
Protected native readers/writers, authenticated individual consent/collection
owners, pre-body acceptance, derived-evidence invalidation and generated provider
migrations remain open. Field storage and matching metadata are not permission.

Native writes bind source/adoption, request-bound receipt and qualification
epoch atomically with post-save/replay rechecks. Source and adoption observations
reacquire the original `ModerationConsumerEvidenceSession` before and after
reads, including persisted epoch currentness and selected immutable definition.
Missing receipts, authority, current generations or finite leases reject.
Withdrawal remains terminal after expiry, supersession or departed owner.
The concrete scoped owner is registered; receipt kinds 26/27 and original
maintenance discovery include all governing/reviewer/collection participants.
This supplies only normative `ChildAndAgePolicy` evidence. Native DOB collection,
consent, assurance, classification and audience consumers, optional provider
qualification, generated migrations, complete routing, measured conformance
and activation remain separate requirements.

## Event continuity timeliness

### Native event-rights governance source

`ModerationEventRightsSource` freezes the exact host/trust/tenant, publication/
policy revision, definition, profile/mode, original terms and accountable rights
review, continuity and settlement mapping mandates. Three native CQS commands
record, independently adopt and terminally withdraw through the qualification
repository, with request-bound receipts, atomic epoch advancement and post-save/
replay authority checks. Receipt kinds 28/29 and EF constraints are registered.
The nominated reviewer/controller must remain independent of recorder and
mapping ownership; one operator may own both mapping sets.

`ModerationEventRightsSourceOwner` reads the immutable definition owner, never
the stateful publication writer. Source and adoption observations reacquire the
original concrete evidence session, current persisted qualification epoch and
provider tenant before/after reads. Missing original receipts, departed parties,
expired leases, selection/definition drift or wrong transaction fail closed.
Withdrawal preserves original provenance even after expiry, deselection,
replacement or original-owner departure. Maintenance discovers recorder,
reviewer, both mapping owners/controllers, governance controller, adopter and
withdrawer before existing sorted human fences; discovering them lends no human
permission to a service principal.

This is normative EventRightsAndPremises evidence only. It obliges actual willing
qualified stewardship and finite fallback, explicit premises/event/session scope,
innocent companion preservation, participant-rights partition, complete native
settlement causes and historical merchant/currency/minor units/idempotency.
It cannot establish any concrete event permission, consenting steward, finance/
merchant permission or refund availability. Those target-specific native
consequence owners remain open, as do activation/conformance. A missing
nonurgent mapping remains unavailable; necessary qualified urgent protection
requires a separately accountable pending settlement. No source adoption causes
event deletion, postponement, refund, provenance rewriting or rights resurrection.

### Original activation deadline

`ModerationEventRightsTerms` adds the normative event-rights contract: a willing,
currently permitted steward and accountable fallback; only dependent
registration pauses; explicit premises/event/session scope and host permission;
actual participant-rights partitions and complete settlement causes; innocent
companions; historical accepted merchant/currency/minor units and existing
native idempotency. Missing mapping makes a nonurgent effect unavailable.
Qualified urgent necessary protection may carry separately owned pending
settlement rather than fabricating a completed refund.

`ModerationEventRightsOwnership` separates independent rights review from
recorder and accountable continuity/settlement mapping owners/controllers.
One actual operator may govern both mapping sets; independent review must still
come from a different canonical controller. These are governance mandates, not
actual event/host/finance permissions, consenting fallback or legal certification.
The terms delegate fallback timing to `ModerationEventContinuityDeadline`, and
exclude provenance rewriting, automatic event cancellation/postponement and
ticket/membership/history resurrection. Native source/adoption/withdrawal,
original-session observation and actual target-specific consequence/settlement
qualification remain to be implemented. No AG03 source verdict or activation
is supplied by these Domain values.

`ModerationEventContinuityDeadline` expresses the shared fallback timeliness
rule independently of event permission: the earlier of original activation
plus 24 hours or event start minus two hours. An unknown start requires immediate
escalation; an already overdue known deadline remains overdue. Exact deadline
observations are due, later reads cannot renew the value, and non-UTC/regressing
or unrepresentable clocks reject. This value does not supply a consenting
steward, physical jurisdiction, participant settlement mapping or an event
mutation. Native continuity ownership, current schedule/permission fences,
dependent-registration pause and fallback execution remain separate work.

## Local transaction boundary

`ModerationStore` owns a serializable primary-store execution-strategy unit.
Operation and effect identities arrive already allocated. A versioned SHA-256
request binding includes purpose, qualified subject origin, complete target
ancestry, expected revisions, proposal terms and any exact superseded contribution.
A completed receipt
also binds the qualified principal. Identical retries return the original
accepted aggregate identities and revision; changed content conflicts.

Qualified urgency findings retain two purpose-distinct coordinates: the actual
case evidence and the committed native qualification receipt that produced it.
The finding writer fences and rereads that receipt, checks exact source/revision,
host/trust-domain/tenant, asserting human and completion time, and positively
requalifies the urgency assessor's current administrator permission. Reusing
the evidence identity as its own qualification identity is invalid. A missing
receipt leaves the finding and qualification epoch unchanged; an old receipt
alone cannot preserve a withdrawn assessor's authority.

The native lifecycle CQS write bridge validates stable UUIDv7 operation, effect,
decision and purpose-specific references and positive revisions through a
manually instantiated validator. It rejects Evaluate as a mutation and forwards
accepted intent/cancellation to the shared store-owned operation service.
Responses contain only accepted decision/contribution identities and revision.
Native callers can also resolve the closed typed handlers for
`ProposeModerationDecisionCommand`, `RecordModerationDecisionResponseCommand`,
`AcceptModerationFindingCommand`, `ApproveModerationDecisionCommand`,
`ActivateModerationDecisionCommand`, `RejectModerationDecisionCommand`,
`WithdrawModerationDecisionCommand`, `RevokeModerationSanctionCommand`,
`ReconcileModerationSanctionCommand` and `SupersedeModerationSanctionCommand`.
These immutable intents fix their operation kind rather than accepting it from
a body. They preserve preallocated operation/effect identities and expected
case/decision, policy and applicable contribution revisions. Shared operation
construction failures become command validation; the existing lifecycle
validator still owns common shape rules. No request supplies a current actor,
role, approval or deployment certification. Every handler uses the same
store-owned operation service; future-start activation schedules the already
approved interval, and reconciliation handles the current contribution deadline.
The normal native operation discovery registers these handlers with its
authorization/timing pipeline. Handler-owned current authority remains binding.

Qualified urgency can accept the exact current final finding directly from
Proposed without writing a fictitious subject response. The shared session
reads the actual finding entity under complete subject-origin and target
coordinates; the native store fences that matching row before returning it.
`AcceptUrgentFinding` checks its final purpose, case, subject, scope, rule,
independent adjudicator and finalization instant. Normal response-based findings
retain the AwaitingFinding path. Both paths now load and check the actual final
finding before changing the decision: exact case, subject origin, target, rule,
matching response-or-urgency purpose and current finalization time. The shared
engine does not accept a supplied finding UUID alone. Adjudicator independence
uses the canonical human identity, so changing that human's origin cannot make
the decision issuer an independent adjudicator. Urgency does not bypass independent approvals
or activation evidence, and a removed assessor or missing qualification receipt
cannot commit a finding transition or effect.
The native effect planner copies the preallocated operation/effect identities
and actual post-transition revisions into the minimized durable pointer; it
neither allocates retry identities nor sends consequences.

Effect-specific safe-disclosure approval uses the existing native qualification
writer and its fence order: canonical human/identity sources precede host,
tenant and qualification epoch; case and decision precede the selected policy
slot/publication and disclosure effect slot/row. The primary serializable
transaction owns every acquisition until commit. Approval/revocation, epoch and
exact principal/request receipt commit together; no delivery occurs inside
execution-strategy replay. Final target/contribution acquisition and notice
reading remain separate owners, never inferred from these approved text rows.

`ApproveModerationNoticeDisclosureCommand` names a preallocated effect, exact
current or immediate-next decision revision, current case and selected rich
publication/profile. Current-revision authoring supports contribution-only
Revoke/Reconcile operations; it does not authorize an unchanged decision for
a decision-changing operation. Stale or skipped revisions are rejected.
The native source writer verifies that case, decision, publication and exact
rule/reason meanings before storing `ModerationNoticeDisclosureApproval`.
Subject-safe facts, remaining actions, recourse, automation and retention text
are explicitly approved plain text, not read from finding justification.
Intent is bounded to 128 KiB and sixteen facts; scalar metadata and typed facts
remain separate from the native operation receipt. Exact retries preserve the
original acceptance; changed intent or actor cannot reuse it.

`RevokeModerationNoticeDisclosureCommand` advances that approval once without
rewriting original content or extending expiry. Its exact current case/scope
can be revoked after case closure or tenant inactivity, while actual native
administrator qualification still applies. Expiry during the final flush rolls
back approval, epoch and receipt. These writes supply only the safe-content
half of the original-source contract: final disclosure acquisition still needs
independent current target/contribution and reviewer-policy sources.
`IModerationPolicyPublicationNoticeAuthority` now resolves to the same scoped
`ModerationAuthorityParticipant` that owns the original lifecycle acquisition.
Its adapter checks the effect's exact sanction identity/revision before acquiring
the complete independently adopted Event/resource target selection, approved text
and reviewer policy on that transaction. `ModerationPolicySelectionNoticeSourceOwner`
is registered for capture; it cannot reconstruct authority in a second scope.
Missing, replaced, unsupported or expired target owners still reject the complete
selection. Storing approved text alone does not enable substantive effects, and
this composition does not complete a consequence or supply runtime conformance.

`ModerationAuthorityParticipant.AcquireNoticeQualificationAsync` supplies the
native content/reviewer-policy read on that same acquired lifecycle transaction.
Human and maintenance discovery include the original disclosure approver before
identity/role fences, never as a late identity lock. The read requires the
original grant object, newly staged exact effect/decision, current actual
approver role, unrevoked approval and its original source-kind-8 receipt. It
reconstructs the closed mutation digest from retained intent and verifies actor,
host, tenant, publication and writer generation instead of trusting row presence.
The supplied publication must also match the actually acquired jurisdiction,
publication writer generation and catalogue revision. Reusing its UUID and policy
revision with substituted source metadata cannot mint a new approved profile
stamp for disclosure.
The lifecycle acquisition retains the original decision revision independently
of the mutable tracked decision. Notice capture requires revision one for
Propose, that unchanged acquired revision for Revoke/Reconcile, and its immediate
successor for decision-changing operations. Revoke's expected sanction revision
is never interpreted as a decision revision. Matching forged disclosure and
decision rows cannot replace this original acquisition anchor.
Separate current/adopted procedural observations supply reviewer limits; the
earliest deadline bounds capture. This read deliberately returns no target or
contribution authority. The final notice owner still needs actual native target
generation acquisition and separate target approval, joined to the retained
contribution selection below.

`RecordModerationContributionSelectionCommand` now persists the full authored
contribution through the native qualification transaction. Its selection header
binds the future effect/current or immediate-next decision revision, exact case and rich publication/
profile; normalized `ModerationContributionBinding` rows retain each effect and
each base/additive target partition, including operation, scope, subject, target
revision and event window. The retained compiler validates the complete profile
set and decision interval before row, bindings, epoch and source-kind-9 receipt
commit together. Original array order survives reconstruction and therefore
preserves the selection digest. Headers/bindings are immutable after save.

These rows are drafts, not native target source observations or approvals.
Unknown object coordinates may be authored without accessing an object payload;
current target resolution/adoption must still independently reject an absent,
changed or foreign target before use. The scope-coordinate factory shared by
the domain and native projection validates complete tagged ancestry. No target
generation is invented from a native GUID concurrency stamp, and the complete
notice owner remains unregistered pending that actual generation/adoption owner.

`ModerationAuthorityParticipant.AcquireNoticeContributionDraftAsync` now joins
that retained draft to the separately approved disclosure on the exact acquired
lifecycle transaction/grant. Both human and maintenance discovery include the
draft author before user/role fences. Under the contribution slot and row fence,
the read reconstructs the ordered typed bindings, checks their digest against
both the retained header and disclosure, and verifies the original kind-9
receipt's closed mutation digest, author, host/tenant, epoch and acceptance time.
The retained profile is recomposed against the current decision interval.
Missing receipts, copied grants, foreign transactions, changed partitions,
removed author authority and exact deadline expiry return no draft.

The read holds the existing qualification authority through the owning
transaction. Lock order is native identity/role, host/tenant/epoch, case/decision,
publication, disclosure slot/row/receipt, then contribution slot/row/receipt;
named locks belong to that same transaction/acquisition connection. The store
owns commit/rollback and final authority requalification. The result's deadline
is the earliest of disclosure, procedural qualification and draft validity.
This proves the selected draft and disclosure belong together; native target
observation/adoption remains a separate, required join before any consequence.

`RecordModerationResourceGenerationCommand` supplies the resource-specific
native observation owner. The caller supplies the resource identity, expected
previous numeric generation and expiry, never a GUID version or source stamp.
The qualification writer captures the actual publisher/organizer Actor, Event,
EventResource and, for stored files, the owning StorageObject/provider binding
under their native fences. It projects only IDs, concurrency stamps, delivery
kind and immutable provider identity. Destination ciphertext, document bytes,
paths, endpoints and credential references are not read.

| Resource generation writer | Authority and acquisition order | Completion owner |
| --- | --- | --- |
| Native administrator CQS observation | Current identity/role, Host/maintenance; non-retaining resource/Event/storage ancestry plan; sorted publisher/organizer/storage-owner Actor anchors; Tenant/status/epoch; Event; EventResource; owning StorageObject and immutable provider binding; `moderation.resource-generation:{host}:{resource}` slot; generation row | Qualification repository commits immutable observation, kind-10 receipt and epoch together on the acquisition connection; post-flush authority/expiry failure rolls them back |
| Independent resource target adoption | Current identity/role including discovered draft author, observer and decision issuer; Host/maintenance; exact retained generation parents and non-retaining storage-actor plan; sorted Actor anchors; Tenant/status/epoch; case/decision/publication; native resource ancestry; resource-generation slot/row/receipt; contribution slot/row/receipt; exact selection/binding adoption slot/row | Qualification repository commits adoption, original kind-11 receipt and epoch together; revocation uses current administration and the retained case/selection/adoption, without requiring a live resource |

For this Record writer, `EventAuthorityProviderOperations.ReadResourcePlanAsync`
plans the exact resource ID, owning Event, publisher/organizer, optional storage
object and its optional Actor owner before Tenant/epoch acquisition. SQL Server
and MySQL use the existing separate, non-retaining planning connection so early
Resource/Event/storage reads do not recreate the retaining-read lock cycle.
The detached coordinates nominate locks, never permission. The reader compares
every planned ancestry coordinate before any Actor acquisition, then checks the
fenced resource/storage state again; substituted parents cannot add a late Actor
lock behind Tenant. The helper retains the existing Event planner's connection
and transaction ownership. Mixed-target/final-use paths still have their own
acquisition sequences and are not claimed fixed by this scoped correction.

Fresh recording requires a source-ID-bound plan. Exact accepted receipt retry
remains receipt-based when the resource, Event or publisher has since been
deleted: it neither re-observes missing material nor renews the original source.
Four native ancestry/storage cases were authored before the planner change; a
further retained-retry case precedes its replay correction. One retaining-provider
real Actor-save race was added to the existing provider fixture. All six cases
remain **UNRUN/unbuilt**. Focused source reviews found no concrete material
finding in the planner or retained-retry correction, not MAD/runtime proof.
Scoped Roslyn diagnostics are unavailable, not clean. Initial Persistence source
compilation passed with 62 unsuppressed warnings and zero errors in 6:48.68;
the final retained-retry follow-up passed with the same warning/error counts in
6:54.60. Neither is provider assurance.

Resource adoption now also proposes its earlier Actor fences from the exact
retained generation, never from a caller-supplied replacement target. Publisher,
organizer, Resource/Event and stored-object identities remain the original
generation's coordinates. Only the stored object's mutable Actor ownership is
discovered through the non-retaining planner. The entire sorted Actor set is
acquired before Tenant/epoch, and the fresh adoption compares the reloaded
generation ID and all original coordinates before its native reader runs. A
changed publisher, organizer, storage object or storage actor cannot introduce
an earlier Actor lock after Tenant. These coordinates are provenance, not
adoption authority: independent current administration, native versions, source
receipts, contribution binding and the original finite window remain required.

Exact accepted retry remains receipt-based after Resource/Event/publisher
deletion; retained-only revocation does not gain a live-ancestry prerequisite.
Five native cases were authored before this adoption correction, and one real
retaining-provider Actor-save race was added afterwards. All six remain
**UNRUN/unbuilt**. Focused source review found no concrete material finding in
this increment, not MAD/runtime/provider proof. Scoped Roslyn diagnostics are
unavailable. Current Persistence source compilation passed with 62 unsuppressed
warnings and zero errors in 6:17.83; other lifecycle, mixed-target and
classification/material acquisition paths remain open. Compilation is not
runtime or provider assurance.

The resource slot allocates `expected previous revision + 1`, even when a new
observation finds unchanged GUID stamps. This is a versioned observation, not a
conversion of a GUID into an integer, and a new observation cannot silently
renew an old adoption. Stored files must have an exact native resource purpose/
ownership and matching immutable provider binding; links must retain a protected
destination and no stored object. Deleted, missing or mismatched native ancestry
produces no observation. Earlier observations remain immutable and tenant/trust
filtered. Exact receipt retry returns the original accepted result. Current
native re-resolution and independent target adoption/revocation are still
required before this source can authorize consequences. Generated schema
artifacts are not evidence of native support; provider witnesses remain queued
for final programme assurance.

`AdoptModerationResourceTargetCommand` and `RevokeModerationResourceTargetCommand`
now supply the separate resource adoption lifecycle. Adoption binds one exact
retained selection ordinal to the accepted resource-generation identity and
numeric revision. The native author and observer are discovered before human/
role acquisition. A current administrator distinct from the draft author,
decision issuer and human subject verifies the current case, decision's next
revision, selected rich profile, complete composition and finite interval.
Human qualification binds the resource's current Event submitter to the Human
subject. Content qualification instead preserves the existing native identity:
the Content subject is the owning Event in its exact tenant origin, while its
Resource scope and every selected ordinal name the actual EventResource.
`ModerationQualificationRepository.ResourceTargets` and the final
`ModerationAuthorityParticipant.ResourceTarget` reader require
`generation.EventId == decision.Subject.Id` for Content, never the submitter's
Human identifier. Retained typed profile composition still decides target
compatibility. Content cannot borrow a tenant-wide resource partition or an
Event-target adoption; the registered composite native owner rejects mixed
Content/Event coverage before returning any target set.

This supplies CFG09's exact unsafe-resource qualification prerequisite, not
quarantine execution. It neither changes native publication state nor completes
an effect, and it does not grant a sanction against the resource's submitter.
Complete quarantine, safe-status/private-disclosure, review and authorized-release
owners and their conformance evidence remain required before CFG09 activation.
Original-payload expiry still cannot be extended or replaced with mutable findings
or the original-notice cleanup marker.

The native resource now has a separate `MaterialVersionId`, allocated by its
actual delivery setter and unchanged by metadata edits or concurrency-stamp
rotation. Genuine `ModerationResourceGenerationReader` observation records this
identity under the existing native ancestry fences; final native-version joins
compare it independently of `ResourceStamp`. Missing/invalid material identity
cannot be retroactively filled from current state into an old generation.

`EventResourceModerationQuarantine` is an operational resource cause, not a
maintenance authorization or global effect completion. `EventResource` retains
one immutable receipt per effect/ordinal and a persisted concurrency-checked
cause count. Its tenant-filtered mapping and composite ownership key bind the
actual Event/resource. The count blocks publication, republication, payload
eligibility and delivery replacement even with unloaded causes. Metadata edits,
time passage, ordinary withdrawal, deletion and parent redaction cannot release
it or restore discarded material. Receipts retain only opaque native cause and
material identities plus transition time, not original private notice content.
The registered `ModerationResourceQuarantineRepository` now executes one
originally selected CFG09 cause. It requires an installed persisted-key
maintenance service, the exact live effect lease and lawful original capture.
It validates original typed plans, final finding and active sanction, then
independently requalifies current selection, profile/resource adoptions,
native mutation receipts, human roles, tenant, ancestry and material.
Mutation and immutable per-cause receipt share the primary Serializable
transaction; qualification and the earliest lease, credential, retention and
purpose deadlines are checked again after flushing. Exact replay retains the
original receipt/time and permits a resource-stamp change only with a matching
cause; material and ancestry remain checked. Missing original capture or
elapsed purpose cannot acknowledge an existing cause.

The cause owner explicitly finalizes accumulated disclosure before its terminal
qualification callback, including when retained privacy and primary state use
separate stores. Commit-time automatic disclosure must not run after the last
current-authority or original-purpose check. The colocated privacy owner's
outer callback remains in place. Three native vectors are authored, UNRUN:
current control, original-purpose expiry and native maintenance-key expiry at
persisted disclosure-epoch advancement. Loss must roll back the cause, resource
guard/stamp, disclosure epoch and injected key change without completing or
releasing the original leased effect.

Original discovery and mutation now enter the selected retained identity
serialization before primary authority acquisition. Every discovered participating
human is checked against retained erasure facts before the original private
capture is read; a missing application saga is not evidence of live custody.
The mutation retries the whole retained/primary attempt. It borrows a primary
transaction adopted by the retained owner without beginning, disposing or
committing that transaction; otherwise it owns and commits a Serializable
primary transaction while holding the retained gate. Final native qualification
survives callback return and runs after the adopting authority's actual
disclosure flush, checking the exact frame and original deadlines. Failure
propagates through enrollment before becoming an unavailable result. An erased
original participant closes original execution and replay but does not release
an already applied cause or restore material; fresh safety custody remains the
separate route.

`ExecuteOriginalAsync` discovers original ordinal/resource/material coordinates
under the native leased purpose. An unsupported ordinal or missing native material
rejects the entire discovery before any valid prefix is executed. Each action
then independently reacquires execution qualification. Its typed result records
the original expected cause count and actual native receipts: `Partial` preserves
an unfinished prefix, while `Accounted` includes every applied or exactly retired
cause without reapplying retired guards. Neither outcome proves notifications,
protected response, completed review or terminal-disclosure closure. The actual
`ModerationEffectOutboxDispatcher` rejects incomplete cause accounting and preserves
its independently proven inert completion path and routes supported resource/
Content causes to this owner. It releases its exact lease when the remaining
recipe is incomplete, so a partial quarantine never completes the effect or
acknowledges the outbox wake-up. Unknown effects stay closed. Complete original
recipe verification, generated schema and
runtime conformance remain required; these source
owners do not open CFG09 activation.

`ModerationResourceQuarantineReleaseRepository` implements the separately
approved fresh safety chain: finite purpose proposal, independent adoption,
current material inspection attestation, independent approval quorum and exact
per-cause release. Each step is an immutable principal/request-digest-bound
receipt, not Urgency/Response or the vanished original allegation. The actual
human/controller/reviewer, host/tenant/epoch, procedural policy/adoption,
mutation-receipt and Actor/Event/resource/material fences are reread through
the primary Serializable transaction. Final qualification after flush checks
every cutoff and agreement between the guard count and active native causes.
Historical findings remain intact; original private purpose expiry does not
prevent a independently lawful fresh decision or supply its permission.

Tenant-owned Resource-attributed cases now enter that chain through the release
owner's closed `ResourceSafetyProvenanceSession`. The capability binds the live
repository, context, Serializable transaction, intent object, human principal
and independently qualified acquisition. The native attribution owner joins the
exact current head/source generation with payload-free occurrence/material
coordinates and current Resource/Event/storage ancestry; it does not read the
original disclosure grant or reopen allegation custody. Procedural policy and
reviewer receipts retain their actual case scope. Attribution humans join early
discovery before human/role/host fences; a changed discovered set cannot trigger
late human acquisition. Post-flush and final pre-commit checks compare attribution
metadata and repeat current policy/reviewer/purpose/native-material qualification.
The same metadata recheck gates safety-state and inspection output. Genuine
expected-head renewal may support a new independent purpose; another read grant
alone cannot prolong expired source metadata. Direct Resource-scoped native cases
retain their separate exact-scope checks. Twenty-three authored native vectors
remain UNRUN; their historical quarantine/finding factories certify neither
original activation nor complete consumer conformance.

The native CQS handlers are discovered through existing operation registration;
validators are manually instantiated. `ModerationResourceSafetyController`
exposes authenticated bounded/no-store JSON
`POST /api/moderation/resources/{resourceId}/quarantine-reviews` and
`POST /api/moderation/resources/{resourceId}/quarantine-causes/{quarantineId}/release`.
Routes bind exact resource/cause coordinates while native immutable receipts
own operation replay, not generic idempotency response storage. Responses
contain accepted identity and original recorded time only, with RFC 7807
400/401/403/404/409/503 failures. HTTP access alone supplies neither reviewer
independence nor material safety. The repository is mandatory scoped and
both new review/release tables use central named Tenant filters. A narrowly
allowlisted Content-resource case prerequisite route acquires administration
at the actual parent tenant while retaining the original Resource mutation/
receipt target. It fences and rereads exact case and Actor/Event/resource
ancestry; other governance and Urgency routes keep their former scope limits.
Resource read acquisition already used containment and is not widened.

Release retires only one cause through concurrency-checked `ReleaseId`, records
the immutable release and changes the persisted active count atomically. Original
apply retries independently qualify first, then recognize a retained exact
release as `Retired`, never reinsert it or infer complete effect acknowledgement.
Exact authorized release replay returns its original identity/time after fresh
purpose expiry without another transition. Overlapping causes survive; resource
publication and current audience are recomputed independently, and deleted or
discarded bytes cannot be restored. Administration UI, full recipe closure,
generated tenant/RLS/schema and runtime/provider
assurance remain open.

Fresh safety review and per-cause release explicitly flush accumulated disclosure
after their complete primary save, then requalify current humans, original
independent-purpose deadlines, native material/stamp and provenance before commit.
The transaction interceptor's idempotent fallback cannot postpone disclosure past
that terminal qualification. Six native vectors are authored, not run: current
review/release controls, exact purpose-deadline loss and actual human deletion
after persisted tenant disclosure-epoch advancement. Loss must roll back new
review/release receipts, cause/count/stamp, epoch and injected human state together,
while preserving overlapping causes, prior review completion and historical findings.
No original notice or maintenance credential supplies this fresh permission.

`POST /api/moderation/resources/{resourceId}/quarantine-state` is a separate
protected operational-metadata query. It names the current material, fresh
resource case/revision, caller's qualified reviewer/revision and exact native
qualification revision; an ordinary administrator cannot borrow another
reviewer's coordinates. Page numbers are bounded and each page contains at
most 64 causes plus `HasNext`, not an unbounded historical inventory.
The native owner independently requalifies every cause and exact retained
release, duty and completed-review ownership inside primary Serializable and
retained-erasure serialization. Shared opaque human discovery includes current
reviewer/policy participants and Event/resource/storage owners; their primary
and retained erasure fences remain held through actual output.

Successive independently completed fresh reviews of one unreleased cause remain
valid immutable history. Operational state selects the latest `CompletedAt`, with
completion identity as a descending deterministic tie-breaker, rather than assuming
one completion per cause. The selected receipt still requires the same native
row fence, exact cause/material ownership and terminal qualification. This does
not restart the original review duty or release material after a safe disposition;
the separate qualified release remains mandatory. Two native regression cases
are authored UNRUN/unbuilt for successive unsafe/safe reviews at distinct or equal
instants, retained history and unchanged overlapping quarantine causes. Scoped
Roslyn diagnostics are unavailable; provider/runtime assurance remains deferred.

The pending result maps entities to cause/material identifiers, due/completed
review dates and disposition, and exact release identity/date. It never maps
original effect/finding/notice identities, human attribution, private text,
provider coordinates or material bytes. Terminal HAL serialization rechecks
the same generation, reviewer, epoch, human inventory and finite deadline
immediately before body output; callbacks cannot retry and pending results
cannot be reused. Active causes expose only `record-safety-review` under that
current qualification. Retired causes expose no mutation link. State discovery
does not grant material inspection, safe-release permission, original recipe
acknowledgement or public access; independently adopted purpose/complete
approval remain mandatory on their separate routes.

`POST /api/moderation/resources/{resourceId}/quarantine-causes/{quarantineId}/review-state`
names an actual current review tip in addition to the exact cause/material,
fresh case and caller's reviewer/epoch coordinates. The native chain reader
reconstructs at most five immutable purpose/adoption/attestation/approval rows.
It verifies their request digests, predecessors, current independent controllers,
procedural adoption and native version; a tip with an actual successor is stale.
Withdrawal, expiry, erasure, retirement or changed qualification prevents
disclosure. This is not a list of original reviewers or historical allegations.

The handler exposes only receipt/purpose/predecessor identities, closed steps,
finite terms, attestation disposition and timestamps. Native action qualification
calls the same `RequireNextStep`, independent-chain and inspection-reviewer guards
as the actual writers. HAL therefore offers only current adopt/attest/appropriate
approval/withdraw actions. Inspection requires a still-qualified original purpose
participant; another qualified reviewer cannot borrow that action. Release
requires a complete safe quorum and the exact final approving human, never
unsafe completed review or another participant. The server-only capability fields
are excluded from JSON: clients gate solely on `_links`. Primary and retained
erasure fences span serialization, followed by exact chain/human/native/action
requalification immediately before output. Reading this state mints no receipt,
attestation, release, original acknowledgement or extended purpose clock.

`POST /api/moderation/resources/{resourceId}/quarantine-causes/{quarantineId}/inspection`
is the separate no-store native query, not an ordinary manager/download grant.
It names the adopted second purpose receipt and current reviewer/case/material/
epoch coordinates. Only that proposal/adoption's currently qualified human and
controller participants may inspect, even before safety attestation. The owner
holds retained-erasure serialization, setting locks and primary/native fences
through output and requalifies before private reads and each chunk. Captured
native storage providers must match current transport policy, material length/
version and SHA-256; protected destinations are validated text attachments,
never redirects or server fetches. Owned buffers are zeroed, callback delivery
cannot retry, and terminal headers prohibit caching, referrers and sniffing
with a sandbox. The query creates neither an attestation nor a release.

`ModerationResourceQuarantineDutyRepository` scans at most 100 tenant-bound
causes per scheduled pass and independently qualifies each under current
installed-key/host/service/tenant/epoch, retained erasure and native metadata
custody/ancestry. An immutable duty preserves the selected review deadline,
never later than 24 hours; discovery/retry cannot restart it. At the deadline,
current eligible native managers receive a content-free separately governed
notification graph, atomically bound to an immutable escalation receipt.
The five-minute execution purpose is credential-bounded and never reads original
SnapshotJson, chooses original notice recipients or extends their R/calendar.
Unavailable routes/recipients remain unavailable, without guard release.

The duty owner explicitly flushes accumulated disclosure after its complete save
and before the final credential, manager, native-version and five-minute-purpose
checks. Separate privacy storage therefore cannot leave automatic commit-time
disclosure after the last qualification. Three native vectors are authored,
UNRUN: current control, exact independent-purpose deadline and native-key expiry
during persisted epoch advancement. Loss must roll back duty/escalation and
notification rows, epoch and injected credential change while preserving the
original quarantine and pending effect. This is neither renewed original custody
nor acknowledgement of its unfinished recipe.

`IndependentlyInspectedUnsafeForCurrentAudience` with independent
`ApproveContinuedQuarantine` quorum records an immutable actual review completion.
Proposal/adoption/incomplete quorum cannot satisfy the duty, and unsafe completion
cannot satisfy safe release. The guard count stays unchanged.
`ModerationReconciliationJob` authenticates this bounded duty pass in a fresh
tenant/machine scope, resets its dedicated cursor on tenant change, advances
only an available result and clears both accessors in finally. Three new duty/
completion/escalation entities retain central Tenant filters. Review step 6
and provider/RLS schema must be regenerated natively; source integration is
not evidence of actual review, delivery or final runtime assurance.

`EventResourceAudienceWorkflow` now returns only current authorized identity,
`MaterialVersionId` and `availability: quarantined` while the persisted cause
count is nonzero. It omits title/kind/requirements, description, language,
accessibility/alternative, file metadata and protected origin; all audience
action flags are false. Existing native current-audience/existence admission,
resource-version and attachment generation remain in its terminal proof.
`EventResourceManagementWorkflow` likewise omits draft, timestamps, file/origin,
export semantics/download references and audit attribution, using only the
minimal current `status: quarantined` projection. Management HAL emits authorized
self/collection navigation, not ordinary mutation or review bypasses. A normal
management role is not a fresh safety-review purpose.

All prepared audience/management/export/audit representations retain their
existing A/provider/B and final serialization qualification. A quarantine or
audience revocation before terminal execution discards the prepared response;
a refresh recomputes from actual persisted state. Safe status never broadens
private resource existence or implies guilt by the submitter. Nullable DTO
semantics permit this minimized shape; owning OpenAPI property inventories are
updated, but generated OpenAPI/client contracts and sixteen authored native
HTTP/material-storage/terminal-race cases remain unrun. Final explicit-release
audience recomputation and discarded-byte non-restoration require integration
with the separate release owner.

Actual resource ancestry is acquired again and every native stamp must match
the observation; a later numbered observation invalidates adoption of the old
generation even if GUID stamps are unchanged. Both the generation's kind-10
receipt and the selection's kind-9 receipt are reconstructed from retained typed
fields and checked against the original closed mutation digest, principal,
authority epoch and acceptance timestamp. No copied source stamp is enough.
The finite adoption ceiling is bounded by both source and selection expiry.
Original acceptance coordinates are immutable after save; the only transition
is accountable revision-2 revocation, which never renews a target or requires
the resource still to exist. Exact retries retain original receipts. Final
notice/consequence use still needs its own transaction-bound current adoption/
native-version join and complete runtime conformance.

`ModerationAuthorityParticipant.AcquireResourceTargetObservationAsync` now
performs that final-use join for one resource partition on the acquired native
lifecycle transaction. It first reacquires the complete draft and subject-safe
disclosure, then rereads the exact adoption, original observation and actual
native lineage. Human and maintenance acquisition discover every retained
resource adopter/revoker and source observer before their native identity/role
fences. Removed authority, accountable revocation, changed native versions,
missing kind-10/kind-11 receipts or a newer observation return no target.
Receipt digests are reconstructed from retained closed mutation coordinates,
including the original principal, epoch and acceptance time.

The adoption retains the approved observation's writer generation explicitly;
the returned current stamp comes from the separate native acquisition and must
equal that retained approved stamp. Expected Actor/event/storage lineage is
checked before replacement dependency fences are introduced, so reparenting
fails rather than acquiring a new identity late. Each result is bounded by the
earliest draft/disclosure/procedural/adoption deadline and rechecks the exact
acquired lifecycle grant before return. It qualifies only that ordinal. The
complete notice owner must still acquire every required effect/consequence
partition, deduplicate the complete target set and join the actual rich profile;
this single-resource method does not certify whole-profile readiness.

`AcquireResourceNoticeApprovalAsync` supplies the complete retained resource-only
target set on that same lifecycle transaction. Every effect and consequence
ordinal must have its own current receipt-bound adoption, even when several
ordinals name the same target. It fences the union of retained Actors, Events,
resources, storage objects and provider bindings in dependency order with sorted
identifiers before the per-ordinal rereads. Missing, unsupported or revoked
partitions reject the entire result. Only after all ordinals qualify does it
deduplicate equal full targets; unequal owner/source stamps for an equal target
reject the result. The returned set shares the earliest deadline across all
partitions, disclosure and procedural evidence, followed by a final exact grant
check. This complete resource-only join is not registered as a generic notice
owner; other target kinds and actual profile-source readiness remain required.

`RecordModerationEventGenerationCommand` now captures an Event independently of
resource attachment observation. The immutable `ModerationEventGeneration`
stores the native Event, publisher/optional organizer Actor GUID stamps and
current submitter coordinate, without title, venue, participants or private
logistics. Its own monotonic numeric revision and qualification epoch retain
the distinct meaning of the original native stamps.

The native writer joins the same serializable qualification transaction and
requires the actual tenant administrator. After the existing Human, login, role,
Host and maintenance-service fences, it plans the Event's exact publisher and
optional organizer and fences that sorted Actor union **before** Tenant/status
and qualification epoch. This matches ordinary Actor/ActorPii source saves,
which already take Actor before Tenant. On retaining-read providers the existing
detached, non-retaining planner supplies coordinates only, not permission or a
retained Event read lock. The immutable plan travels with the acquired native
authority. The Event reader rejects changed publisher/organizer ancestry before
any new Actor acquisition, then fences Event and rereads that exact lineage.
It subsequently takes `moderation.event-generation:{host}:{event}` and the
observation row before allocating expected revision + 1. Row, kind-12 acceptance
receipt and epoch commit together; the final post-flush clock check rolls back
an expired prefix. Exact accepted retry returns the original receipt. Query
filters bind actual tenant/trust domain, and all stored fields are immutable.
Native Event/Actor/submitter coordinates have no erasure-blocking foreign keys.
The observation is not target adoption, event stewardship consent, a consequence
or deployment conformance; event-specific independent adoption and final-use
qualification still need their own owners.

Two native-store publisher/organizer substitution regressions and one actual
retaining-provider Actor-save/native-qualification race are authored **UNRUN and
unbuilt**. The race requires the ordinary Actor save to commit while qualification
is paused before its Actor anchor; source-order assertions alone are not database
concurrency proof. Scoped diagnostics are PARTIAL because Roslyn is unavailable;
the targeted Persistence compilation passed with 62 unsuppressed warnings and
0 errors (6:26.00), against existing dependency outputs. This is compilation,
not test/provider proof. This correction is specific to
recording Event-generation observations, not proof that every Event-target or
other qualification writer now shares the complete order.

A focused source review found no concrete material finding in the changed order,
detached provider planning, exact-plan rejection or source/receipt retry paths.
That bounded review is not runtime, retaining-provider or MAD evidence.

`AdoptModerationEventTargetCommand` now independently adopts one retained
selection ordinal against one original accepted Event observation. Current
native administration must differ from the selection author, decision issuer
and human subject. The Event's actual current submitter must match that subject;
the native Actor/Event GUID lineage is reread, never inferred from selected
target coordinates. The current complete rich publication/profile/interval and
both original kind-9 selection and kind-12 observation receipts are verified
before immutable adoption, kind-13 receipt and epoch commit together.

Event-target adoption now discovers the exact retained generation during its own
qualification acquisition. After the existing Human/login/role and Host/service
anchors, it fences that generation's sorted publisher/organizer Actor union
**before** Tenant/status and epoch. The plan carries the generation ID as well
as Event and Actor coordinates. Reloaded generation metadata must match every
coordinate before the native reader can request an Actor lock; changed live
ancestry cannot add a replacement Actor after Tenant is held. Neither the
request's target nor contribution intent supplies this plan or permission.
It then qualifies the original source receipt, contribution slot/row/receipt and
`moderation.event-target:{selection}:{ordinal}`. A later numbered observation
conflicts even with unchanged native stamps. Acceptance retains the approved
observation's writer generation explicitly and is bounded by both source
windows. `RevokeModerationEventTargetCommand` is an accountable one-way revision-2
transition that preserves original coordinates and exact retry identity; it uses
retained case/adoption authority without requiring a live Event. An inactive
tenant or closed case does not make revocation depend on resurrecting the Event.
The current owner supports the actual human submitter's native tenant/Event
partitions only. It is not stewardship consent or consequence execution; those
owners remain independent prerequisites.

An accepted receipt still replays without requiring a live Event or publisher.
Retained-only revocation is unchanged and does not reacquire those live sources.
Two native ancestry substitutions, an additional publisher-deletion replay case
and one real retaining-provider Actor-save/adoption race are authored **UNRUN and
unbuilt**. The race must let the ordinary Actor save commit while adoption waits
before its Actor anchor, then reject the changed original Actor stamp without an
adoption/receipt/epoch prefix. Focused source review found no concrete material
finding in this increment; that is not MAD, runtime or provider evidence. Scoped
diagnostics remain PARTIAL because Roslyn is unavailable. Targeted Persistence
compilation passed with 62 unsuppressed warnings and 0 errors (6:13.54), against
existing dependency outputs only. Other material, control and final-use qualification paths
are not claimed fixed by this Record/adoption-specific planning.

`ModerationEventGeneration.MatchesNativeSubject` centralizes only the native
identity invariant: a Human must be the observed submitter, while a Content
subject must be the exact observed Event. Service and Organization subjects
cannot borrow either identity. Domain adoption checks this before retaining the
partition; the writer and final-use reader share the same check. Their current
Human-only lifecycle qualification remains in force. This method neither accepts
an Event Content contribution nor supplies classification, material approval,
audience eligibility or a terminal calendar/discovery disclosure owner.

`AcquireEventTargetObservationAsync` now joins this original adoption to the
qualified complete draft/disclosure on the actual lifecycle transaction and
original grant object. Both human and maintenance acquisitions discover every
retained Event adopter/revoker/observer before native identity/role fences.
The final read rechecks current independent administration and original kind-12/
kind-13 acceptance digests, then independently rereads actual Actor/Event GUIDs,
submitter and latest numeric generation. Approved source coordinates come from
the retained adoption; current source comes from the native reader, not an
approval clone. Reparenting rejects the read before replacement Actor fences.

`AcquireEventNoticeApprovalAsync` qualifies every retained effect/consequence
ordinal in an Event-only selection. The shared source acquisition plans and
fences its sorted Actor union before Tenant/epoch; the notice owner then fences
all Events before per-ordinal reads. Different ordinal orders cannot invert
shared native parent locks. Only after every ordinal succeeds may identical full
targets with equal owner/source stamps deduplicate. The result carries the
earliest partition/draft/disclosure/procedural deadline, checked again after the
final asynchronous authority reread. Missing, revoked, expired or changed
partitions return no complete approval. Event-only coverage does not certify
mixed target kinds or deployment prerequisites; generic notice composition
remains unregistered until actual complete ownership exists.

`AcquireNativeNoticeApprovalAsync` now composes the implemented Event and
resource owners in a single retained selection. Every effect/consequence ordinal
must have exactly one native adoption owner; missing, unsupported or ambiguous
coverage rejects the whole set. The acquisition carries an internal
`ModerationNativeTargetPlan` for the exact host, tenant, effect, retained selection
and revision. Non-retaining discovery proposes the complete Resource/Event
publisher, organizer and stored-object Actor union. Human qualification takes
its existing Human/login/grant/role and Host/service anchors, then the sorted
Actor union **before** Tenant/status/epoch. Maintenance acquisition merges its
service Actors into that same sorted union before credential and Tenant/epoch
anchors; both lifecycle reconciliation and effect acquisition use this path.
Early classification-material interested-Human discovery also uses the existing
non-retaining provider planner instead of retaining Event/Actor reads before
the Human anchors.

The native notice owner rechecks the exact selection/adoptions/generations and
storage Actor coordinates against that acquired plan, then fences Events,
resources, storage objects and provider bindings in sorted tiers. A substituted
or newly appearing ancestry rejects qualification; it cannot append an unplanned
Actor behind Tenant. Each ordinal still runs its original owner-specific
receipt, independent administration, version and deadline checks; selecting an
owner does not grant its authority. Deduplication follows complete qualification
and all results share the earliest deadline after the final authority reread.
This closes mixed Event/resource target composition only, not other native
target kinds, deployment activation or substantive consequence execution.

The resource-only and Event-only approval methods, both public single-ordinal
observation methods and the quarantine execution consumer use the same acquired
plan. Resource reads pass its storage ancestry into the native generation
reader, and Event reads pass its original ownership coordinates. These are
fence proposals, not fresh target permission: discovery does not require live
Event/Resource/Actor existence, unexpired adoption or complete eligible coverage
merely to reach original receipt replay. Existing retained-only Revoke, Reject
and Withdraw behavior, original custody, acknowledgement and deadlines are not
extended by this plan.

Seven further native-store cases are authored and **UNRUN/unbuilt**: five human
cases cover mixed first-use composition with distinct Event/storage owners,
late Event/storage substitution and both single-ordinal readers; two machine
cases cover the sorted union including the maintenance Actor and late storage
substitution. These fixtures do not prove full machine reconciliation, leased
effect execution or provider concurrency. Persistence product compilation passed
with 62 unsuppressed warnings and zero errors in 6:47.59; scoped diagnostics
are partial because Roslyn is unavailable. This
does not promote support or close the programme's remaining runtime gates.

Connected-consumer inspection found four further acquisition gaps; the following
source corrections are not covered by that initial compilation receipt. Initial
maintenance binding discovery now uses the non-retaining planning context;
credential, binding and live-service qualification still run after the acquired
anchors. A missing live Event plan no longer prevents lookup of an already
accepted Event-observation receipt; fresh recording still requires its exact
source-bound plan.

Classification-material readers now receive generation-ID-bound Event ancestry
on their actual acquired provenance, including maintenance and the existing
public-calendar provenance path. The native generation reader checks those
coordinates before acquiring Actors. Public discovery remains a fence proposal,
not a new anonymous disclosure permission or accepted material assessment.

Original Content-effect completion also plans its current representative Event
ownership and assignment sources during initial acquisition. Representative roles
join the same sorted Role set, their permission sources are fenced before Host,
and current representative Actors join the early Actor union. The later custody
reader borrows that plan, checks its complete current ownership/assignment set,
and never adds Role, Permission or Actor anchors behind Tenant/resource fences.
Its original representative, approval, private-purpose and deadline checks remain
required. Other custody callers are not claimed migrated by this correction.

Seven additional cases are authored and **UNRUN/unbuilt**: two accepted Event
replays after native deletion, a credential expiring at Tenant acquisition, a
material-parent substitution, and three borrowed-custody snapshot cases covering
unchanged authority, changed Actor and changed role without late early anchors.
That four-gap follow-up compiled with 62 warnings and zero errors in 7:11.38.
Further source review identified two remaining material acquisition inversions:
mandate checks still acquired Event roles after Tenant, and current-authoring
inventory/review reads added material sources only during a second acquisition.

Material provenance now carries a separate native Event-authority plan alongside
the generation ancestry. Its assignment roles join the original sorted Role set;
the combined material/representative permission set is acquired before Host and
Tenant. Mandate checks filter and borrow that acquired plan rather than taking
new Role, Permission or Actor anchors. Authenticated, maintenance and the existing
positive public-calendar paths use this same source ordering. These coordinates
remain provenance, not a grant of material acceptance or anonymous disclosure.

Current-authoring reads discover the selected publication without retaining native
planning locks before their first qualification acquisition. That acquisition
includes the publication's nominated humans, material roles and Actor sources.
The fenced selection must still match the proposed publication. Material evidence
borrows the original acquisition; revalidation compares its original Event-parent
and assignment coordinates before reacquiring that same publication. It cannot
silently substitute a later selection or add material sources as a second phase.

Two further inventory/review cases assert the original owner Role and publisher
Actor precede the first Tenant anchor, with no newly introduced early anchors
afterward. Both are **UNRUN/unbuilt**; Persistence compiled with 62 warnings and
zero errors in 7:38.44. Focused source review found no concrete material defect
in that correction; this is not runtime or MAD evidence. Scoped diagnostics are
PARTIAL because Roslyn is unavailable.
None of these
sources proves full reconciliation, original substantive completion, anonymous
calendar delivery or real-provider concurrency.

### Original representative notice portal acquisition

`ModerationNoticeReadRepository` now proposes each bounded window's Content
representative Event-parent and assignment coordinates without retaining native
planning locks. The original recipient's Event-role IDs join the combined sorted
Role acquisition; their Permission/join sources and complete Actor union precede
Tenant/qualification epoch. Original snapshot coordinates nominate anchors only:
all current recipient, source, approval, representative identity/revision,
privacy and original finite custody checks remain mandatory.

Each detail/list lease retains those exact per-notice plans. Custody borrows them
on initial qualification and terminal revalidation rather than acquiring new
Actor, Role or Permission anchors behind Tenant. A changed Event parent or
assignment set withholds the notice instead of silently locking replacement
sources. Acknowledgement carries the same plan through its direct normal and
actual co-located commit rechecks; an acknowledgement receipt never establishes
complaint-body custody or a new representative, purpose or retention period.

Seven first-authored acquisition/terminal-read cases and two actual co-located
acknowledgement cases are **UNRUN/unbuilt**. Focused review also found that the
final awaited recipient checks could cross the representative's original expiry
after qualification; three additional normal/co-located clock-checkpoint cases
are authored **UNRUN/unbuilt** for that correction. The shared commit helper now
captures one fresh time after all awaited recipient checks and requires it to
precede the acquired representative deadline as well as the original notification
retention ceiling. Normal and co-located commits use this same helper. Initial
compilation reported 62 warnings and two CS7036 errors in the connected SMTP
caller. That caller now discovers the same per-notice plan before its first
acquisition anchor, acquires Event-role/Permission/Actor sources before Tenant,
and supplies the original plan to final SMTP qualification. Three additional
native SMTP cases (original delivery and parent/role substitution) are authored
**UNRUN/unbuilt**. Persistence compilation including this caller correction and
the portal's post-await clock correction passed with 62 warnings and no errors
in 7:02.40. SMTP still sends only
the existing optional pointer and never creates a notification acknowledgement.
Neither these fixtures
nor earlier compilation establish provider concurrency, hosted delivery or MAD
assurance; no database has been updated.

The independent `ModerationNoticeStatusRepository` now discovers the original
status-metadata representative's Event coordinates without retaining planning
locks. It acquires that Human's Event-role/Permission sources before Host, and
the complete ordered Actor set before Tenant/epoch. Status metadata proposes
anchors only: the same original representative and all current native custody,
privacy, assignment, source and notification proofs must still qualify.
Initial and terminal status qualification borrow the identical plan; changed
parents or assignment roles withhold status rather than acquiring replacement
early anchors behind Tenant. A private qualified-status value also retains the
representative's finite custody deadline, checked again after all awaited final
identity/privacy reads. Original notification dates and ceilings are never
renewed; Human-subject and purpose-purged status remain separate.

Six additional native-store status cases are authored **UNRUN/unbuilt**, covering
original positive status, parent/role changes at acquisition and after private
preparation, and expiry during final identity reads. Initial source compilation
passed with 62 warnings and no errors in 7:11.17; the subsequent nonretaining
Host-discovery correction is included in the successful disclosure-approval build
(62 warnings, no errors, 2:47.04).
Focused source review found no material defect (not MAD or runtime proof).
This does not establish complaint
payload permission, provider deadlock freedom, hosted delivery or full migration
of the remaining pre-finding, response, approval and capture/store consumers.

### Disclosure approval representative source acquisition

`ApproveModerationNoticeDisclosureSource` now plans only the persisted decision's
exact Content subject and explicitly nominated original representative Humans.
The nonretaining discovery proposes Event ownership and assignment coordinates;
it does not grant disclosure permission. Their Event-role IDs join the existing
ordered role acquisition, Permission/join sources precede Host, and the complete
publisher/organizer Actor union precedes Tenant and qualification epoch. The
existing independent administrator, case, policy, assignment revision, native
Owner/Manager, privacy and original finite-custody checks still decide whether
an approval may be recorded.

Initial approval, exact-receipt replay and committed-result recovery acquire
their own attempt's sources before ledger lookup. Normal and actual co-located
before-commit checks borrow that attempt's identical plan, including a null plan
for non-Content subjects. They never replan or append replacement Actor/Role
anchors behind Tenant. A changed parent or assignment set rejects the attempt;
approval, mutation receipt and qualification revision remain one native unit.
Human-subject notices with no Content representatives retain their separate path.
No representative, retention window, acknowledgement or complaint-body purpose
is created by these coordinates.

Seven native-store approval/replay/after-save substitutions and one retaining-
provider Actor-save scenario are authored **UNRUN/unbuilt**. Focused read-only
source review found no material defect, not MAD or runtime/concurrency proof.
The first build found a local-name collision (CS0136); renaming that local, without
changing behavior or suppressing warnings, produced a successful Persistence build
with 62 warnings and no errors in 2:47.04. Remaining pre-finding, protected-response
and capture/store consumers are not declared complete.

### Protected Content response representative source acquisition

Response availability and submission now nominate the exact original notice
recipient's Content Event and assignments from persisted ownership metadata,
without reading the private snapshot for planning. Nonretaining discovery is
not permission: the original notice envelope, full snapshot, approval, current
subject, exact original representative assignment, privacy and finite custody
must still qualify. Representative Role/Permission sources precede Host and the
sorted publisher/organizer Actor union precedes Tenant and qualification epoch.

Initial submission, availability and exact-request replay acquire their own
attempt's plan. Saved-response, prepared-availability and actual co-located
submission commit checks borrow that same plan rather than adding replacement
Actor or Role anchors. The final custody clock is checked after awaited native
identity/privacy reads. A response still does not acknowledge notification, renew
retention, change the finding, or create complaint-body authority. The existing
row-specific encryption and request-digest binding remain unchanged.

Fourteen additional real-store cases are authored **UNRUN/unbuilt**: ten
submission/replay/separate/co-located substitution vectors, three availability
vectors, and a co-located expiry rollback vector. The last vector additionally
guards against committing prepared native changes when terminal availability
qualification fails. Initial Persistence compilation passed with 62 warnings and
no errors in 2:47.95; focused source review found no additional material defect
(not MAD or runtime proof). The explicit abort that rolls back prepared native
changes compiled with 62 warnings and no errors in 3:16.69. No hosted,
provider-concurrency or complete protected-intake assurance is implied.

### Pre-finding original representative source acquisition

Pre-finding discovery now nominates the persisted attributed case, exact subject,
scope and attribution source, and original notice recipient/assignment tuples
without retaining subordinate planning locks. First issue adds only its explicitly
intended recipient. Human recipients retain the exact-subject, empty-assignment
path; they do not require an Event plan. Content representatives still need all
existing independent reviewer, custody, attribution, privacy, original receipt,
assignment revision and finite-window checks. Planning is not permission.

The existing qualification and consuming maintenance acquisitions merge Event
roles and permission sources before Host, and the complete ordered Actor union
before Tenant/epoch. Initial and terminal recipient checks borrow the same
case-bound plan. Unbound response-proof rechecks retain their original acquisition;
bound finding proofs retain the actual parent provenance and its context,
transaction, session and grant identity checks. Rechecks cannot replace the plan
or extend the attempt's original minimum deadline. Retained withdrawal and cleanup
do not gain renewed representative authority.

Private disclosure now aborts the privacy-owned transaction if prepared proof
fails terminal qualification, rather than committing prepared native changes
while withholding bytes. Its check remains an actual-output check, not a new
post-flush disclosure callback. Pre-finding writes retain their existing normal
and actual co-located precommit checks after disclosure flush.

Twenty-nine additional real-store cases are authored **UNRUN/unbuilt**: twenty
issue/acknowledgement/response/disclosure/evidence source and replay vectors, one
co-located prepared-disclosure rollback vector, and six actual co-located
disclosure-flush issue/response vectors. The last six check successful commit,
changed original assignment and expiry, including notice/response, receipt and
pointer-graph rollback. One further actual participant-attribution case requires
the original investigation reporter and privacy/governance controllers to be
included in the acquired Human set before Tenant. Source review found their
omission from the shared discovery used by direct pre-finding and consuming
maintenance paths; discovery now unions the existing investigation-human owner
inside its nonretaining planning callback. Initial compilation passed with 62
warnings and no errors in 2:52.85; the correction compiled with 62 warnings and
no errors in 2:46.90. Focused source-review follow-up found no additional material
defect, not MAD or runtime proof. None of these authored cases proves provider
concurrency, machine reconciliation execution or hosted behavior.
Other capture/store custody owners and the full moderation programme remain open.

Issuance now shares discovery's existing technical bound of 64 stored notices per
case. A NEW issue that would exceed that complete-discovery bound is rejected
under the existing case/source transaction before private materialization, receipt
or epoch writes. Original-operation replay is not a new issue and remains
eligible at the boundary. This prevents an otherwise successful 65th issue from
making subsequent recipient acknowledgement/response acquisition unavailable; it
does not select a reviewer-workload policy or change custody duration. The added
real native regression authors 64 genuine issues, exact replay, rejected overflow
with no writer prefix, and acknowledgement of the original notice, **UNRUN/unbuilt**.
Persistence compilation passed with 62 warnings and no errors in 2:45.52;
Roslyn diagnostics remain unavailable, not a clean snapshot. No tests, provider
assurance or database application was performed.

`ModerationAuthorityParticipant` implements both authority interfaces as the
same scoped instance. The store acquires it before receipt lookup; the shared
operation service receives that exact participant and grant. Permission is
operation-specific: an actual subject response, issuer withdrawal, qualified
independent finding or approval, current native administration, or the installed
maintenance service's reconciliation. The frame binds the identical session,
operation object and primary transaction. Role lookup rows are individually
fenced; finding adjudicators and evidence assertors are discovered before human
authority fences, including an independently identified urgency assessor.
Every resolution also requalifies that operation's permission, not just the
transaction and qualification epoch. This includes the store's post-flush
checkpoint and its stored-receipt return path: reviewer/evidence expiry or a
closed proposal case cannot retain an earlier grant. The participant returns
the same acquired grant object; requalification never widens its jurisdiction
or renews evidence lifetime.

`ModerationNativeActivationQualification` requires the finite inventory's exact
consumer set, independently current consumer sources and each prerequisite
owner's current decision under retained fences. Profile capability sets must be
known and unique; observation time cannot move backwards during qualification.
The independently adopted native deployment/profile observation must carry a
finite UTC `ValidUntil` later than the observation. Its deadline bounds the
whole result along with procedural and prerequisite deadlines; profile expiry
during asynchronous consumer/prerequisite acquisition rejects activation at the
final clock check, even if every other source remains valid.
Missing measured source composition disables activation without disabling
drafting, response or authorized correction. Duplicate source-owner composition
is rejected, not silently selected. The production deployment/profile,
conformance, generation and prerequisite owners remain required integration;
there is no approval default, and authored tests cannot create their evidence.

Production Application composition calls `AddNativeOperations`, which discovers
each concrete request/handler pair from the Application assembly once and adds
the scoped handler behind its authorization/performance decorators. Typed
moderation commands participate in that same fixed discovery; they do not need
an additional runtime dispatcher. This source wiring does not by itself prove
caller integration or positive native activation readiness.

`AdoptModerationProfileCommand` now records an independent finite configuration
adoption of one original complete contribution selection and its exact current
rich publication/profile. The adopter must have current native administration
and differ from the draft author, decision issuer and subject. The writer checks
the original kind-9 acceptance digest/coordinates and complete interval
composition; declared mode must belong to the publication, and the required
prerequisite set is derived from that mode/profile's compiled gates, never from
a caller-provided subset. Like Event/resource target adoption, profile adoption
accepts only a selection anchored to the current or immediate-next decision
revision; accepting the draft does not authorize either lifecycle transition.
`ModeCode` stores the closed native mode values
(1 Embedded, 2 RemoteStrict); `PrerequisiteMask` uses bit `n - 1` for prerequisite
code `n`, including mandatory ConstitutionAndReviewers and MeasuredOperations.

`ModerationProfileAdoption` retains selection writer generation, publication,
profile, case and declared mode/prerequisites as immutable coordinates. Its
kind-14 receipt and epoch commit atomically under
`moderation.profile-adoption:{selection}` after the original contribution
slot/row/receipt. `RevokeModerationProfileCommand` performs accountable terminal
revision-2 withdrawal without rewriting the accepted configuration or requiring
an open case/live target. Exact retries preserve original receipts; post-flush
expiry rolls back acceptance. This is native profile configuration authority,
not evidence of installed deployment topology or passed consumer conformance;
current profile reads and independent deployment/conformance/prerequisite
owners must still join before positive activation.

`AcquireProfileConfigurationAsync` now reads that adopted configuration on the
actual acquired lifecycle transaction, before its decision transition. It
requires the original grant and tracked current decision, exact current
publication metadata, complete operation-bound selection and original kind-9/
kind-14 acceptance digests with principal/host/tenant/epoch/time. Both human and
maintenance acquisition discover profile adopters/revokers before identity/role
fences; an unknown replacement identity is never acquired while holding the
contribution/profile locks. The reader rechecks current author/adopter authority,
independence, exact compiler-derived prerequisite set, mode and complete interval.
Lost administration, revoked adoption, missing receipts, expired authority,
foreign transaction, cloned grant or detached decision produce no observation.
The decision revision is the immutable acquisition anchor, not a fresh value
read from the mutable tracked decision. Reconcile, including scheduled sanction
activation, requires a current-revision selection because it changes only the
sanction; decision-changing operations require the immediate successor.
The final asynchronous authority reread precedes the shared earliest-deadline
check. This returns native configuration authority only; it is not installed
deployment, passed consumer conformance, substantive prerequisite approval or
target permission. Full activation-source composition remains pending.

The native lifecycle now consumes this reader before activation qualification.
It passes the still-private original acquisition directly rather than publishing
an unqualified `_acquisition` or creating a participant/source-owner DI cycle.
On repeat qualification the independently reread persisted decision must match
the tracked decision before that actual tracked instance enters the reader.
Deployment/profile observations must match the independently accepted
configuration's transaction, publication, profile, decision, mode, source
revision/generation, capabilities, full contribution digest and prerequisite
set. That set must also equal the compiler's gates for the exact rule/mode;
baseline gates alone, omitted privacy authority or injected extra gates cannot
change the adopted meaning. Gate ordering has no meaning. Configuration expiry
joins publication, procedural, deployment/profile and prerequisite deadlines;
the final asynchronous acquisition must finish before the earliest deadline.
Missing substantive deployment/conformance/prerequisite owners still reject
activation, even when configuration adoption succeeds.

`InstallModerationConsumerCommand` now records an Embedded `resource.payload`
artifact installation from `ModerationResourcePayloadRuntimeSource`, registered
with the native API authentication/resource authority composition. The host
checks its actual native snapshot owner and fingerprints the loaded API,
Application and Persistence module versions plus the shipped capability
inventory. Expected caller fingerprints cannot replace that observation.
This identifies installed artifacts, not successful tests, complete deployment
readiness or coverage of other inventory consumers.

`ModerationConsumerInstallation` preserves the original tenant/host/trust-domain,
artifacts, installer, writer epoch and finite validity. Kind-15 original receipts,
installation rows and qualification epochs commit atomically under
`moderation.consumer:{host}:{tenant}:{consumer}:{mode}`. Both original acquisition
and post-save checks must see the requested artifacts; uncertain-commit and exact
retry paths retain original coordinates. A live installation cannot be
overwritten. Accountable terminal withdrawal does not require the old runtime;
replacement requires expiry or withdrawal and preserves original receipts.

`AcquireConsumerInstallationAsync` rereads the latest installation, its original
kind-15 digest/principal/host/tenant/epoch/time and current native artifacts on
the actual lifecycle transaction. Human and maintenance paths discover
installers/withdrawers before identity/role fences. Lost installer authority,
withdrawal, absent original receipt, changed binary/inventory, expired validity
or a foreign transaction rejects the source; it never falls back to an older
installation. `ModerationCapabilitySourceObservation.ValidUntil` is a mandatory
finite UTC deadline. The registry rejects invalid/expired sources, and both
publication and activation qualification use the earliest consumer deadline
after all asynchronous evidence reads. Independent trustworthy conformance
measurement receipts and complete substantive activation owners remain pending;
this kernel supplies no `Passed` verdict and enables no new restriction.

`PrepareModerationConsumerMeasurementCommand` now prepares an immutable
`ModerationConsumerMeasurementChallenge` against the independently selected
publication, exact compiled required consumer contract and latest live installation.
The source writer discovers the installer before identity/role fences, acquires
publication authority before the consumer slot/row, then checks the original
kind-15 receipt and actual loaded artifacts. It freezes publication/binding/contract
digests, installation revision and accepted epoch, runtime/inventory, preparation
administrator and a 256-bit nonce. Nonce allocation precedes execution-strategy
replay; exact retries retain the persisted original nonce. Challenge, kind-16
receipt and qualification advancement commit together. Original, ambiguous-commit,
unique-conflict and post-flush checks cannot requalify a withdrawn/replaced/expired
source or a changed publication. A challenge expires no later than its installation
and contains no test verdict or activation permission.

Measured conformance now also has its own mandatory finite UTC deadline in
`ModerationCapabilityRegistration.ValidUntil`. Refreshing `VerifiedAt` cannot
extend historical measurement authority. Registry matching rejects expired,
unbounded and non-UTC deadlines; publication and activation qualification retain
the earliest measured deadline through all later asynchronous evidence reads.
`EnrollModerationMeasurementIssuerCommand` and
`RevokeModerationMeasurementIssuerCommand` now record separately approved signer
trust through the existing qualification transaction. The native owner requires
current instance administration, including on exact replay; retaining only tenant
administration cannot replay an earlier deployment approval. Enrollment validates
the actual canonical P-256 SubjectPublicKeyInfo, pins runner/catalogue/test-bundle
SHA-256 digests and retains finite validity and the original approving human.
Host/tenant/key slot and enrollment row fences protect uniqueness. Enrollment,
kind-17 mutation receipt and qualification epoch commit atomically. Revocation
discovers the original approver before identity/role fences, verifies the original
approval receipt and advances the immutable trust row from revision 1 to terminal
revision 2. A key identity cannot be reused to replace an earlier trust enrollment.
Only public key material is persisted; no signing secret is introduced.

`ModerationMeasurementReceiptVerifier` authenticates the closed version-1 ES256
wire contract against an independently supplied native enrollment snapshot.
It pins algorithm, token purpose, audience, key identity, exact binding and approved
artifact digests. Canonical UUIDv7 identities, decimal-string Int64 revisions,
lowercase SHA-256 digests and finite round-trip UTC timestamps have one accepted
encoding; unknown/duplicate fields and noncanonical signature encodings fail.
The signed receipt includes the original 256-bit challenge nonce. Its `Known`
outcome means authentic issuer assertion, not native acceptance, honest execution,
passing conformance or restriction permission. The future accepting/current-source
owner must independently check that nonce, persisted challenge and installation,
original trust receipts and current authority under retained fences.

`ModerationConsumerMeasurementReceipt` supplies the immutable retained storage
shape for that accepting owner: exact signed receipt/run/challenge/enrollment/key
coordinates, compact token bytes and matching SHA-256, accepting human, original
writer epoch and finite interval. The native tenant/trust filter and restrictive
foreign keys preserve ownership; unique host/tenant/challenge and host/tenant/run
indexes prevent a second accepted assertion for the same measurement. Scalar
integrity checks never authenticate a signature or manufacture `Passed`.

`AcceptModerationConsumerMeasurementCommand` now retains an authenticated assertion
through the owning qualification transaction. Discovery adds the original
preparer, installer and signer approver before identity/role fences. The owner
rereads selected publication, exact compiled consumer contract, latest installation
and original kind-15 receipt; retains the immutable challenge and original kind-16
receipt; then holds the issuer slot/row and original kind-17 approval. The signer
approver must still have the real platform-admin role, independently of a tenant
role. The Application verifier port resolves the actual Infrastructure ES256
implementation; missing verification authority is unavailable, never accepted.

After signature verification, signed receipt/challenge/nonce, installation,
contract/inventory/runtime, and measurement times must match those independent
sources. Measurement cannot precede challenge or issuer approval, and signed
validity cannot exceed either source. Exact signed bytes, kind-18 acceptance
receipt and epoch advancement commit together. Original execution, receipt replay,
ambiguous-commit verification, uniqueness recovery and post-flush paths repeat
source qualification; replay also requires the original retained assertion.
Missing original receipts, runtime replacement, key revocation or measurement
expiry cannot return a currently qualified acceptance. Retained assertion history
remains immutable after authority expires. Acceptance is authenticity and current
source binding only: no case catalogue, passing registration or activation verdict
is manufactured by this transaction.

`ModerationAuthorityParticipant.AcquireConsumerMeasurementReceiptAsync` now
requalifies that retained assertion on the actual lifecycle acquisition. Both
human and maintenance acquisition discover the accepting/preparing/approving
humans for the current resource-file installation before identity/role fences.
The reader retains the consumer installation, challenge, issuer and accepted row
in writer order and verifies original kind-15/16/17/18 command receipts. It uses
the real injected ES256 verifier, not token presence or a stored verdict. The
actual signer approver must retain platform administration, and all other
accountable source administrators must remain qualified. Selected publication,
acquisition and loaded runtime are rechecked before the final clock is compared
with the earliest installation/challenge/issuer/assertion/acceptance deadline.
A private overload borrows the original acquisition without exposing a
provisional grant. Its immutable observation retains original source generations
and public trust; it never returns `Passed` or substitutes for case completeness.

The approved bundles now link an invocation-owned TUnit 1.63 lifecycle journal.
Measurement mode uses a fresh parent-owned RunId directory and create-new
assembly journal; ordinary runs with no mode variable perform no journal work.
Early/late end receivers and explicit retry/prior-attempt snapshots preserve
sticky failure evidence around teardown. Synchronous serialized JSONL writes
retain ordered records and a session-completion footer; the footer is not a
passing verdict. Native node/session identities are captured when actually
present, never inferred from context IDs. The launcher must reconcile them with
its own terminal process/MTP results, exact approved catalogue and body spans.
Linked compilation, hook ordering and runtime retention remain final-assurance
checks under the implementation-first execution override.

The launcher signer is an instance-only bootstrap secret named
`moderation.measurement_signing_private_key_pem`. Selected Environment,
Infisical `/moderation`, or explicitly selected shared UserSecrets in
Development/Testing supply `MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM`;
ordinary settings/tenant bindings cannot store its value. Setup metadata marks
it Secret, optional, no default and not startup-owned. It is injected into the
launcher only, never API services or test children. The independently enrolled
public SPKI must match, and signer rotation requires a new key identity/enrollment,
followed by accountable retirement of the old trust; historical rows are retained.

`eng/moderation/MeasurementLauncher.csproj` supplies the native Linux/.NET host.
Its only selectors are tenant, challenge, issuer enrollment and a parent output
directory. Short serializable primary-database reads qualify original source
rows and reconstruct original approval request digests before and after owned
execution; no transaction survives across children. The assembly-internal trust
filter is replaced only by explicit independently read host/trust/tenant
predicates, retaining other filters and PostgreSQL tenant RLS.

The independently enrolled, fixed-path catalogue maps each stable wire case ID
to exact journal/native/MTP identities across Domain, API and Persistence bundles.
It pins runner, dependency directories, product artifacts, native runtime,
public configuration and actual provider version. Each bundle executes once in
a private RunId directory and private process group with cleared environment.
Only separately selected test-database inputs are projected. Ordered journal
lifecycle/attempt evidence, actual TUnit case/body/session spans, MTP TRX terminal
records, process exit and drained stdout/stderr must reconcile exactly. Any
failure, retry, incomplete/extra coverage, expired source or changed artifact
rejects signing. Inline ES256 signing then emits the complete native acceptance
command through atomic `acceptance.json` publication; the owning authenticated
writer must still reacquire current authority. Exit zero means assertion issued,
not accepted, qualified or activated.

The launcher-only test-database fields are documented under
`MODERATION__MEASUREMENTTESTDATABASE__*` in `.env.example`. All are optional for
ordinary startup and excluded from startup generation. Password is Secret,
username Sensitive, and neither credential nor a hash of it enters the public
configuration digest. Version 1 projects structured database inputs only.

The signed receipt also retains separately scoped `PrimaryConfigurationDigest`
and `TestConfigurationDigest`. `PrimaryDatabasePublicIdentity` canonically hashes
public provider/role/endpoint/database/schema/TLS/flavor/version coordinates.
Runtime comparison uses the primary identity; actual fixture-provider evidence
must use the isolated test identity. The combined configuration digest still
binds the whole launcher recipe, environment and selected authority; it must
never be compared with a runtime-only identity. All three exclude credential
text and credential hashes. The unpublished strict receipt shape was changed
directly; no legacy token parser or optional-field fallback was retained.

`PrimaryDatabaseProviderComposition.ConfigureApplication` captures the public
identity in the actual EF context options after structured configuration has
been validated. This extension installs no services and shares the existing
provider cache; distinct database names do not create separate EF service
providers. The original consumer session's `AcquirePrimaryDatabaseAsync` reads
these context-bound coordinates together with the open connection's real
`ServerVersion`. It requires the same owned transaction connection, matching
native EF provider and current original session before and after observation.
Uncomposed, closed, disposed or replaced contexts return no observation.
Returned database facts are never themselves a `Passed` verdict.

Actual resource-payload runtime observation also hashes the eight native product
assemblies through `ModerationMeasurementProductManifest`, the same complete
ordered byte recipe used by the launcher. It checks disk module identities
against loaded assemblies, rejects linked/missing/oversized artifacts and
returns no observation on unreadable bytes. MVID-based runtime generation and
the inventory fingerprint remain separate: matching either does not substitute
for matching product bytes. Controlled installation fixtures deliberately carry
no product-byte proof. Fifteen pure recipe invariants are authored but UNRUN.

Lifecycle journal schema 2 retains bounded terminal database witnesses. Each
API and Persistence case must record the provider, actual server version and
credential-free public configuration digest observed by its native database
owner after database use. Reconciliation requires the exact selected isolated
test-database digest and provider/version, joins the witness time to that case's
actual TUnit body span and rejects setup-only or preflight observations.
Domain cases must remain database-free. The original-session preparation case
now records its independently acquired primary facts; complete catalogue-case
instrumentation and portable fixture composition remain open, so this source
does not establish executed conformance.

External-provider inputs and real immutable `catalogue.v1.json` publication
remain programme obligations. Never publish synthetic case mappings to unlock
the runner. Actual execution and linked test compilation remain deferred.

`ModerationMeasurementCatalogue` now owns the same strict immutable grammar for
launcher and runtime: bounded bytes, required closed fields, ordered bundle
identities and bijective journal/node/MTP membership. The engineering adapter
retains only fixed execution paths and actual runtime-version checks.
`IModerationMeasurementCatalogueSource` resolves a currently qualified
enrollment's exact digest through the real Infrastructure reader at
`<application-base>/moderation/catalogues/<sha256>/catalogue.v1.json`.
Missing, linked, altered or malformed deployment bytes are unavailable; there
is no "latest", working-directory, caller-path or embedded fallback. Neither
parsing nor loading supplies current approval, execution evidence or `Passed`.

Keep the exact catalogue as a content-addressed deployment sidecar outside
measured product assemblies. It contains the expected product-artifact digest;
embedding it into an assembly included in that digest would create a circular
byte identity. Current issuer approval and all native authority deadlines remain
separately requalified, never cached as file permission. Provider/deployment
observations, complete conformance evaluation and original-acquisition joining
remain distinct programme work. This loader does not manufacture them.

The actual native measurement catalogue and complete current
conformance-source reader remain programme work. Neither preparation,
signer approval, signature authenticity nor synthetic port tests substitute for
completed measurements.

Ten immutable case-source commands cover intake, response, reviewer assignment,
controller qualification, conflict assessment, withdrawal, urgency, final
finding, finding reversal and inactive policy adoption. Concrete handlers
manually instantiate their validators and forward exact intent and cancellation
to `IModerationQualificationRepository`; the repository retains all native
authority and revision checks. Existing canonical subject/controller/reviewer
identifiers remain opaque nonempty identities, while newly allocated moderation
source and operation identities require UUIDv7. Both generic and typed entries
preserve that distinction. Policy commands copy nested draft collections so
later caller mutation cannot replace their authorable meaning. Responses expose
only accepted source identity and revisions, not custody payloads.

Typed confidential intake separates `ModerationCaseTarget` from an attributed
`SubjectReference`. Target kind, complete native ancestry and source generation
remain distinct even when identifiers match. `ModerationEvidenceReference`
retains the occurrence, authenticated source generation and material-fact
coordinates; those structural values are not permission or final guilt.

`SubmitModerationAllegationCommand` exposes the established confidential
committer through a closed native CQS handler. Its only inputs are operation,
receipt identity, opaque token and the exact
narrative bytes originally supplied as issuance Material. The token and narrative
are defensively copied on initialization and access, including serialization
and record copies; it is omitted from diagnostic string output. Manual validation
requires UUIDv7 operation/receipt identities and exactly 32 token
bytes before entering the transaction. Native operation discovery supplies the
normal authorization/timing pipeline; the committer retains the actual
non-machine account, tenant, target and relationship authority.

`ModerationAllegationResponse` maps only the original acknowledgement identity
and acceptance instant. Canonical case identity/revision stay inside persistence.
It returns no receipt
token, caller, evidence, target, responsible subject or current case status.
Changed accepted intent maps to conflict; unavailable qualification and
incomplete acceptance return failure. Exact authorized retries preserve the
original acceptance even after submission expiry without granting case reading
or turning the allegation into a finding. Historical replay includes the original
material digest in its HMAC identity and never rehydrates purged narrative.

The native writer resolves canonical occurrence ownership under its transaction-
retained occurrence lock, fences an existing case, and derives its revision from
that row rather than reporter input. Independently qualified reports can retain
new material facts on the same occurrence, including a closed case, without
revealing canonical coordinates or reopening adjudication. Acceptance is unique
per tenant/caller/operation and per receipt. Reissued existing material receives
its own immutable reporter acceptance without duplicate evidence, extended
narrative custody or resurrection after physical purge. Replay digests use
`moderation.case.intake.v3`; old development request coordinates are not accepted.
Hidden-content occurrence coordinates are stable across independently qualified
material sources; their source generations and material remain separately bound.
Current actor enforcement is retained until the approved complete cutover.
This source unit's regression cases are authored UNRUN; source diagnostics do
not establish provider/concurrency or wire-contract acceptance.

Narrative custody has its own Tenant/TargetKind/confidential-allegation policy,
independent of publication, profile, recognition and final attribution.
`ModerationCaseNarrativePolicySource` and independent
`ModerationCaseNarrativeAdoption` use the actual qualification transaction, native
human/controller roles and mandate grounds, original mutation receipts and epoch.
Kinds 32/33 are included in the real receipt constraint; three central
ModerationAuthority filters preserve exact tenant/trust isolation.
Closed `RecordModerationCaseNarrativePolicyCommand`,
`AdoptModerationCaseNarrativePolicyCommand` and
`WithdrawModerationCaseNarrativePolicyCommand` preserve the qualification
command base. Their concrete handlers manually instantiate the shared family
validator, map exact immutable mutation fields and call the existing native
writer; no second policy authority or recognition grant is introduced.
`ModerationCaseNarrativePoliciesController` exposes authenticated 32 KiB,
no-store JSON source/adoption/withdrawal POSTs at
`/api/moderation/case-narrative-policies`. Native acquisition resolves current
human/controller roles and exact tenant/host/epoch; a supplied scope or role
claim cannot authorize custody. Recording does not adopt, adoption requires
the named independent privacy owner, and withdrawal never rewrites original
capture deadlines. Responses contain accepted source/adoption identity and
revisions only, with RFC 7807 failures. Generic idempotency response storage
is suppressed; native immutable mutation receipts own exact retries.
Policy read/HAL administration and generated contracts remain open.
`ModerationCaseNarrativeRepository` is the mandatory scoped intake dependency.
It verifies exact receipt-qualified source/material digest, protects bytes using
the existing persistent Data Protection authority and distinct
`ISLAMU.Moderation.CaseNarrative.v1` purpose, then stages only ciphertext.
Row-bound protection coordinates include original case/source/material, receipt,
reporter, policy/adoption/epoch generations and immutable capture/purge instants.
Ciphertext, evidence, consumed receipt and acknowledgement share one primary
Serializable save with final proof/policy rechecks. Missing custody authority or
protection rejects new intake; neither receipt-lifetime nor recognition authority
is substituted. Separate current read proof/policy surrounds decryption and
returns a disposable zeroing buffer, never an entity or diagnostic string.
`ReadModerationCaseNarrativeQueryHandler` manually validates opaque case/read
coordinates and prepares a pending read, not a durable authorization result.
The actual `ModerationCaseNarrativeResult` invokes custody `DiscloseAsync`
at response execution. The primary Serializable transaction and native
case/material, human/erasure, grant, policy and epoch fences remain held through
the response-body write. A submission token cannot serve the independently
issued read purpose. Data Protection unprotect and exact original material
digest comparison precede final current native read/policy/time checks.
There is no later MVC payload formatter, cache entry or provider execution
retry after output starts; delivery failures propagate and borrowed plaintext
is zeroed on both success and failure. Bytes already delivered are not recalled.

Finite physical cleanup retains only original acknowledgement/provenance needed
to prevent replay resurrection. The real configured maintenance pass now calls
the bounded expired/withdrawn narrative purge before scheduled authority work.
SQLite uses parameterized SQL time eligibility composed with the same native
filters; batches order opaque IDs without unsupported DateTimeOffset sorting.
Ordinary-erasure cleanup has two explicit owners. The custody repository can
perform one bounded selected-tenant purge only after a durable native saga;
it cannot settle a global erasure. `PrivacyErasureApplier` invokes
`IUserPrivacyErasureRepository.EraseModerationCaseNarrativesAsync` inside the
existing authority-verified primary Serializable settlement, before policy
coverage, replay checkpoint and local settlement are recorded. The native
repository requires the exact saga intent, original subject, policy version
and authority sequence. It pages the complete tenant inventory, including
inactive tenants and former memberships, and deletes batches of at most 100
IDs with both tenant and original reporter predicates. Another reporter's
material survives even inside the same case. PostgreSQL switches its
transaction-local tenant for each RLS partition and restores the previous
session setting; disabling EF filters alone is never cross-tenant authority.
No decryptor, current custody adoption, recognition adoption or payload
projection is needed. Failure rolls back all local batches and completion
markers; the existing durable saga supports replay. Query/materialization
size is bounded, not total settlement duration. Original acknowledgements
and provenance prevent intake replay from recreating erased bytes.
Administration/HAL, generated schema and runtime/provider assurance remain open.
Existing native intake witnesses now create genuine independent policy/adoption
receipts before issuance, use one external ephemeral Data Protection authority
per fixture, and submit the exact issuance Material as Narrative. Their original
target/caller denial and acknowledgement-replay assertions remain; changed
accepted narrative intent is also rejected. This is authored source coverage,
not a test execution receipt.

`IssueModerationSubmissionReceiptCommand` accepts only untrusted target and
relationship lookups plus two separately bounded material buffers. Existing
native identifiers stay opaque nonempty identities; the owner independently
resolves and authorizes them. Both buffers are defensive snapshots, including
record copies and getter access. Manual validation accepts at most 64 KiB per
buffer; a read grant cannot become an intake relationship.

`ModerationCaseIntakeCommitter.IssueSubmissionReceiptAsync` owns a fresh
serializable transaction inside the provider execution strategy. It rejects
outer/ambient transactions, tenant bypass, preexisting tracked work and machine
principals. It snapshots material before awaiting, saves the staged source and
receipt, then rechecks the original native authority, same account/tenant and
finite expiry before commit. A database failure is settled inside the strategy
without reminting. Lost commit acknowledgement withholds the token even if the
receipt reached storage. Owned material and failed bearer buffers are zeroed;
caller-owned transaction/work is not cleared on entry rejection.

`ModerationCaseReadGrantController` separates administrative read-purpose intent
from receipt minting. Authenticated
`POST /api/moderation/case-read-grants/{caseId}` accepts `operationId`,
`expectedCaseRevision`, `sourceId`, `granteeUserId` and finite UTC `validUntil`
representable at microsecond precision; the handler rejects precision loss.
The route supplies the authoritative case lookup. `granteeUserId` is a legitimate
target account, never the current administrator's identity or authority; the
native owner resolves both independently. The grant command maps only the
committed entity ID, not confidential material, source facts or a bearer.
`POST /api/moderation/case-read-grants/{grantId}/revoke` supplies `operationId`
and `expectedGrantRevision` equal to the original live revision, 1. The route
controls the grant lookup. Closed native CQS handlers instantiate validators
manually and return generic `ModerationCaseReadGrantUnavailable` on native
denial. Malformed intent is 400 and unavailable native authority is 503.
Both routes are authenticated, write-rate-limited, bounded to 16 KiB and no-store,
with generic idempotency response storage suppressed. Native immutable operation
ownership, rather than an HTTP response cache, owns retry semantics. Named
routes are `GrantModerationCaseReadPurpose` and `RevokeModerationCaseReadPurpose`.
`ModerationCaseReadGrantRepository` owns the serializable primary transaction,
save, owned-write guard and final native recheck. Creation uses `operationId`
as the immutable grant identity. An exact retry retains original issuance and
expiry and requalifies the original administrator role grant, account, source,
case revision, target facts and current lifetime policy. Changed intent, expiry,
withdrawal or a replacement role grant cannot remint or extend the grant.
The owned-write boundary also admits the recovery advancement generated by this
DbContext: the same object, exact recovery coordinates, singleton installation
slot and original transaction must still match. Merely being a
`ModerationRecoveryStoreState` is not write authority. Grant and revocation
receipt values stay frozen across saving and the final native recheck; recovery
replacement, mutation, detachment, deletion or an unrelated tracked row aborts
the transaction. The original staged recovery object must remain tracked even
when it would otherwise disappear from the tracker enumeration after saving.
Revocation commits revision 1 to 2 together with
`ModerationCaseReadGrantRevocationReceipt`; the receipt retains the exact
operation, grant, expected revision, administrator/role grant and original
revocation time. A different operation cannot borrow an earlier revocation.
Both entities remain tenant/trust-filtered, with a restrictive unique
revocation-to-grant relationship. Provider retries reacquire current authority;
neither historical operation success nor a submitted allegation supplies a
present read permission.
Provider schema generation, generated clients and hosted assurance remain final
programme obligations.

`IssueModerationReadReceiptCommand` accepts only the identity of an independently
issued read grant. The same native commit boundary reacquires that grant's
current account, case revision, source material, lifetime policy and target facts;
submission acceptance never supplies this permission. Receipt expiry cannot
outlive the grant. Read receipts cannot become submission receipts. Both token
responses defensively snapshot exposed binary memory, and neither contains
the confidential case payload.

`ModerationCaseIntakeController` exposes authenticated
submission receipt issuance with an original Event ancestry plan for native
`EventVersion`, including the `LocalIncident` alias. The existing proof owner
fences the actual caller account first, then the sorted original publisher and
organizer Actors before Tenant. Native target resolution and final issuance
checks require that same plan and may only check its held Actor fences; a changed
parent cannot introduce a replacement Actor after Tenant acquisition. This aligns
issuance with the native Event writer's Actor-before-Tenant order without changing
reporter relationship qualification, target/occurrence identity, original finite
deadlines or confidential custody. Read-purpose issuance, downstream receipt
acquisition and other target kinds retain their separate paths.

Ten additional real-committer cases are authored **UNRUN/unbuilt**: EventVersion
and LocalIncident with unchanged ancestry, publisher/organizer replacement at
Tenant acquisition, and replacement after receipt save. They assert original
anchors before Tenant, no replacement Actor acquisition, and no committed source
or receipt prefix on denial. Focused source review found no attributable material
defect. Persistence compilation passed with 62 warnings and no errors in 2:27.59;
Roslyn diagnostics remain unavailable. Neither establishes provider contention
or runtime assurance.

The same original Event ancestry plan now also covers submission issuance for
event-linked native Actor targets, including the hidden-content alias. The alias
does not become an EventVersion target: actual Actor membership and version,
reporter relationship, policy and original deadline still qualify independently.
Only the original sorted publisher/organizer anchors may be used after Tenant;
ancestry substitution rejects issuance before a replacement Actor is acquired.
Standalone Actor semantics, read issuance and protected-reading coverage are not
expanded by this submission-only change.

Five additional hidden-to-Actor real-committer cases are authored **UNRUN/unbuilt**
in the existing submission-order fixture: unchanged ancestry and early/terminal
publisher or organizer replacement. They require original anchor ordering and
rollback with no persisted source or receipt prefix. Persistence compilation
passed with 62 warnings and no errors in 2:47.82; bounded source review found no
concrete material defect. Roslyn diagnostics remain unavailable. No provider
contention, runtime/MAD assurance, new confidential-body permission or custody
policy is claimed.

Event-linked native Service submission issuance now also uses that original
Event Actor plan before Tenant. Actual service ownership must still join the
Service to a nondeleted publisher/organizer Actor, with current native stamps;
credential owner, operator and issuer identities are not attribution evidence.
Native resolution and terminal rechecks compare the exact original ancestry
before following it, using only the already-held Actor anchors. Standalone
Service behavior is unchanged. Five additional real-committer cases cover a
committed control and early/terminal publisher or organizer substitution,
**UNRUN/unbuilt**. Persistence compiled with 62 warnings and no errors in 3:25.79.
Bounded source review found an inherited fixture assumption: custody setup already
advances the qualification epoch, so the shared regression now compares against
the actual post-setup revision rather than a fixed value. No production defect
was found in the two conditional extensions; Roslyn remains unavailable. This
does not repair Service read issuance/disclosure or establish provider contention,
runtime/MAD assurance, body access, a new lifetime or custody authority.

Read-receipt issuance separately retains the original EventVersion publisher/
organizer tuple within its current issuance transaction, including native
EventVersion-backed local incidents. Its pending terminal check compares exact
roles before revalidating receipt authority: swapping publisher and organizer
cannot pass merely because the Event stamp and fenced Actor-ID set are unchanged.
This tuple is not persisted as a new fingerprint or receipt claim. The original
read grant, source, deadline and save/flush/commit boundary remain authoritative;
the comparison acquires no replacement Actor fence and grants no body permission.
That terminal-only increment did not repair read issuance's separate acquisition
order. Four new real-committer control/swap cases are authored
**UNRUN/unbuilt**, asserting bearer withholding, receipt rollback and unchanged
grant/parent authority. Bounded source review found no attributable material defect.
Persistence compilation passed with 62 warnings and no errors in 2:27.83;
Roslyn diagnostics remain unavailable, not a clean snapshot. No tests or provider
contention checks were executed.

Read issuance now also discovers its original grant/source/installation metadata
after caller-account fencing and before Tenant acquisition, through the existing
provider-aware non-retaining planning read. Normal filters remain enabled; the
planning context's temporary installation-derived trust selector is restored in
`finally`. For EventVersion-backed material, including local incidents, sorted
original publisher/organizer Actor anchors precede Tenant. Actual fenced host,
grant and source coordinates must match the early plan before following native
ancestry; neither a changed source nor a replacement Actor extends the held plan.
The existing terminal role check uses this early tuple, with full original
grant/source coordinates rechecked before receipt authority. Planning metadata
does not authorize a read: current grant, case, issuer, policy, native proof and
original deadline remain mandatory at the actual save/flush/commit boundary.
Plans reset only with the actual transaction; no ambiguous-commit bearer remint
or recovery has been added. That issuance increment did not cover downstream
protected-read acquisition; the separate narrative handoff is described below.

Twelve new native real-committer cases are authored **UNRUN/unbuilt**, covering
caller/Actor/Tenant order, first-Tenant publisher/organizer and grant caller/source
drift, terminal same-set role swaps, and rollback without a receipt prefix or
changed original grant. Persistence compilation passed with 62 warnings and no
errors in 3:03.81; bounded source review found no attributable material production
or fixture defect, not runtime/MAD proof. Roslyn diagnostics are unavailable, not
a clean snapshot. No provider concurrency,
database application or hosted-read assurance is claimed.

Protected narrative disclosure now discovers the original receipt/grant/source
and Event publisher/organizer coordinates before custody qualification. The
existing provider-aware non-retaining planning read keeps normal filters and
restores its temporary trust selector. Its transaction-local source plan is not
read authority: qualification includes the original grant issuer Human and role,
then sorted original Actor anchors before Tenant, while actual token, grant,
case revision, issuer, source, custody policy and finite original deadline remain
independently mandatory. A changed original coordinate rejects the attempt rather
than following a replacement issuer, source or Actor. Same-set publisher/organizer
role swaps are also rejected. The qualified installation trust selector used by
the native qualification owner is distinct from the restored planning selector.

The same existing read-planning handoff now includes Event-linked native
Actor and Service targets. Both read-token issuance and protected disclosure
retain original publisher/organizer roles before Tenant; normal filters, native
Actor membership/stamps and actual publisher-Service ownership/stamps remain
independent requirements. A null Event ID on a standalone Actor/Service must not
cause Event discovery or imply a publisher relationship. The existing original
issuer, read grant, bearer, case revision, custody and deadline checks are not
replaced by planning metadata. Eighteen additional parameter cases (nine issuance,
nine disclosure) and two standalone read/disclosure cases are authored
**UNRUN/unbuilt**. They cover original ancestry, same-set role swaps, native stamps,
actual Service ownership, rollback and borrowed-byte zeroing. Persistence
compilation passed with 62 warnings and no errors in 2:45.51. Bounded source review
found no attributable production or fixture defect; Roslyn diagnostics remain
unavailable, not a clean snapshot. No provider/runtime support,
new read permission, lifetime or custody policy is claimed.

The intake owner now freshly authenticates the original receipt at every terminal
read recheck and compares its complete validated coordinates, including original
evidence and token digest, before current case/native proof checks. It borrows the
token memory already owned by the narrative reader, not an additional bearer copy.
Protected bytes are still authenticated and rechecked inside the same live native
transaction before external output; no output retry, custody extension or new
body permission is introduced. Existing cleanup zeroes owned token/plaintext,
clears native tracking/selectors and releases the read transaction.

Eighteen new parameter cases are authored **UNRUN/unbuilt**, each exercising
early-Tenant and late pre-output checkpoints through the real disclosure owner.
They cover issuer Human/role and Actor ordering, ancestry and same-set role drift,
receipt token/relationship and grant source/caller/issuer-role drift, no output,
rollback, borrowed-byte zeroing and a fresh valid retry. Application compilation
passed with 182 warnings and no errors in 36.49 seconds. Initial Persistence
compilation reported two invalid direct `AuthorityScope` property accesses;
the existing `Coordinates` projection corrected those checks without weakening
host qualification. Corrected compilation passed with 62 warnings and no errors
in 2:47.49. Bounded source review found no concrete causal production or fixture
defect, not runtime/MAD proof; Roslyn diagnostics remain unavailable.
Other target-kind ordering and real-provider/hosted disclosure assurance remain
open. No public route, persisted schema, grant policy or receipt lifetime changed.

The controller exposes authenticated
`POST /api/moderation/cases/submission-receipts`,
`POST /api/moderation/cases/read-receipts` and
`POST /api/moderation/cases/allegations` through closed handlers.
`POST /api/moderation/cases/{caseId}/narrative` performs the protected read;
POST keeps the bearer out of URL logs and caches. Its JSON intent contains
`caseId`, `expectedCaseRevision`, `receiptId` and base64 `presentedToken`;
the route supplies the authoritative case lookup. The response is a no-store,
no-referrer, nosniff `application/octet-stream` attachment named `allegation.bin`.
Malformed coordinates are 400; unavailable or unqualified material is uniformly
404 without disclosing whether a private case exists. Authentication remains
401/403. No metadata or responsible subject is added to the raw original bytes.
Named routes are `IssueModerationSubmissionReceipt`,
`IssueModerationReadReceipt`, `SubmitModerationAllegation` and
`ReadModerationCaseNarrative`.
Success returns 200; malformed intent is 400,
changed original accepted intent is 409 and unavailable native qualification is
503. Native authorization failures retain 401/403 through `CommandFailurePolicy`.
Write rate limiting,
192 KiB request bounds, no-store response/output caching and suppressed generic
idempotency response storage protect the opaque bearer boundary. Binary fields
retain base64 JSON. The receipt response exposes only its committed identity,
token and expiry; the acceptance never returns the token. These source changes
do not provide policy activation or provider-runtime assurance. The generated
OpenAPI/client outputs and provider schemas remain final programme obligations.

`ModerationCaseTargetReceipt` is the digest-only native storage owner for
case-purpose intake. It retains one tenant/account, host binding, qualification
epoch and relationship source generations, exact target/provenance and a finite
deadline. Consumption binds one case and operation; exact retries preserve the
original consumption time and expiry. Changed callers, target/source versions,
relationship generations, qualification epochs or token digests cannot consume
the row. The model enforces complete consumption and ancestry, and its query
filter requires both the bound trust domain and owning tenant.
The registered `ModerationCaseTargetReceiptRepository` authenticates a
32-byte opaque token by hashing it and comparing the retained digest in constant
time. It requires the native serializable transaction and a current
authenticated non-machine account, fences the actual user/erasure, tenant,
installed host, epoch and receipt rows, and rereads exact ancestry, qualification
generation and lifetime. A delegated key cannot borrow its owner's account
receipt. Foreign-account or foreign-tenant rows are rejected before receipt
fencing. The returned entity still needs separate current target/relationship
qualification; authentication is not a lasting disclosure or adjudication permit.
These are storage and domain invariants, not a completed protected-target
resolver. Native issuance must qualify the actual relationship and select
expiry from current adopted intake policy; the committing intake writer must
reread those sources and save case, receipt consumption and acknowledgement
atomically. Receipt issuance/validation integration and generated migrations
remain required; ordinary target existence, guest-order access and erasure
tokens must not be treated as confidential case-purpose authority.

`ModerationCase.OpenAllegation` now owns an exact typed target and occurrence
without creating a `SubjectReference`. Its owned `ModerationCaseEvidence`
rows retain distinct material provenance by case revision. Exact current retries
do not rewrite evidence; changed facts advance the revision without changing
the target or occurrence. Findings, reviewer assignments, qualification writers
and subject-qualified session lookup reject these unattributed cases. There is
no automatic attribution transition.

`ModerationCaseIntakeRepository` stages that ownership in the caller's
serializable transaction. It authenticates the receipt, requires a separate
`IModerationCaseTargetProofOwner` to acquire actual type-specific sources,
compares the complete observation and fences occurrence/case ownership.
Submission and `ReadAllegation` are separate purposes. After saved receipt
consumption, the committing writer must call `RecheckStagedAsync` before commit.
The registered `ModerationCaseTargetProofOwner` now supplies native issuance,
staged/read proof and final rechecks. It preserves GUID concurrency stamps
separately from long relationship revisions, adopted per-kind lifetimes and
native administrator-issued, revocable read grants. Event-owned sources require
their actual event identity/version. Standalone Actor and Service sources omit
both event coordinates and fence the actual native object; a service needs no
publisher Actor. Their reporter relationship requires an exact current native
instance-administrator grant, not tenant administration or API-key ownership.
Unversioned platform grants retain their real row identity and a digest of their
grant facts; GUID stamps and long revisions remain distinct. Source uniqueness
uses only nonnullable coordinates and digests, with the native digest binding
real event ancestry when present, so NULL event columns cannot bypass the unique
index. Participant target and reporter
relationship acquisition share the same native parent-first owner: discovery
selects the registration-order fence, then the exact event/order and participant
rows are reloaded while both fences remain held. The immutable participant/order
binding, both GUID generations, deletion state and linked caller are checked;
discovery never establishes authority. A changed parent or child invalidates
the final issuance checkpoint. A distinct `PublicEvent` relationship permits an
unrelated authenticated reporter only for an actually published, publicly
eligible event, not participant access. It fences publisher,
participation and relevant ATProtocol source rows and reuses the existing native
eligibility predicate through final recheck; it never authorizes a private
target or `ReadAllegation`. Standalone caller coverage beyond current instance
administration and occurrence provenance must still be reconciled before replacing the intake API; enumeration of
nine kinds is not complete caller coverage. Custody, wire/API cutover and
generated schema remain open.

`ModerationCaseIntakeAcknowledgement` is the immutable storage record for
original acceptance. Capture requires the actual consumed receipt, open
unattributed case and exact owned material row; it retains case/evidence
revisions and the original consumption time. Later evidence or closure must not
rewrite that acceptance. The registered `ModerationCaseIntakeCommitter` keys
the immutable access-intent digest with a private copy of the opaque token,
clears that copy afterwards, and commits case, consumption and acknowledgement
in one serializable transaction. It rechecks saved native authority, caller and
expiry immediately before commit. Exact replay verifies original
receipt/material ownership and returns the same acknowledgement after
submission expiry; it does not renew target access or reconstruct a missing
acknowledgement. Ambiguous commit verification requires that invocation's exact
captured acknowledgement. Staging/proof caches reset only for a new transaction,
including read-purpose receipt issuance after a prior commit or rollback. The
same scoped owner reacquires the account, host, qualification, native target
and read-grant fences; it never treats an earlier checkpoint or expired fences
as current authority. Protected narrative wire/read access and complete erasure
handoff still need integration. Acknowledgement is neither
current target-disclosure authority nor proof of guilt.

### External advice and preserved actor protection

`EventReportExternalLink.CaseConcurrencyStamp` retains the exact native case
generation sent in the outbound provider envelope. Case identity and generation
are paired; report-only links retain neither. The dispatcher rejects a stale
queued case generation before sending current case material, and exact completed
markers include both original coordinates. It never rebinds an existing
dispatch identity to a new generation.

New Coop and Osprey advice must match that retained generation to the actual
current native case under the serializable report/case owner. Omitting a claimed
stamp or supplying today's stamp cannot replace the original native generation.
Coop's optional camel/snake expected stamp must also match the retained source.
A mismatch returns `event_report_case_concurrency_conflict` before storing new
advice. `EventReportSignal.SourceCaseConcurrencyStamp` copies the original
generation into immutable advisory provenance; database checks enforce complete
source coordinates. Exact already-retained advice is resolved first, so a later
case change can acknowledge original history but cannot create new advice.
Changing the link's retained generation cannot transplant that historical
acceptance. This is source-generation qualification, never final guilt or
permission to execute discipline; measured deployment and consequence owners
remain separately required.

Coop callbacks now retain advice in `EventReportSignal`, never an executable
`EventReportDecision`. Processing rereads the original native inbox identity,
exact-byte SHA-256 and complete callback material, then requires a previously
dispatched native tenant/report/event/case/provider binding. Osprey retains its
authenticated HTTP intake and also requires the actual dispatched case binding;
neither provider can manufacture a wider target. Exact replay retains original
advice. Changed material, inbox identity or dispatch target scope/identifier
conflicts without replacing it; one callback may retain multiple signal
ordinals with the same correlation. MySQL/MariaDB ignore index filters, so the
native model projects non-advisory external/correlation identities into nullable
computed uniqueness slots rather than restricting advisory ordinals.
Generated provider migrations and real engine verification remain required.

`ExecuteReportDecisionCommandHandler` rejects provider-sourced decisions at
preflight and each revalidation before claim or enforcement, including old
pending decisions. Local moderator account checks remain. Existing
`ActorController` suspend/reinstate routes, `ModerateActorCommandHandler`,
`Actor.Suspend/Reinstate`, `ActorModerationRecord` and ATProtocol identity
moderation remain the current human protection mechanism. Their `IsSuspended`
consumers include public discovery/event eligibility, local login, machine
subject enrollment, registration/resource authority and ATProtocol projection.
Replacing the global flag therefore requires the complete capability-specific
consumer and remedy cutover; deleting the flag or its public-event sync intents
early would reopen enforcement or strand account recovery. Generated actor
client methods do not create HAL authority; future operator controls must use
the server's actual links.

The store atomically stages the lifecycle state, qualified receipt, durable
effect intent and a `ModerationEffectPlanningRequested` outbox wake-up in the
same primary transaction. The message ID and aggregate pointer are the exact
preallocated effect ID; its payload is null, so case evidence and subject
details are not copied into the delivery channel. An ambiguous commit retry
cannot allocate another wake-up, and a non-Known outcome rolls all four records
back. Handoff leaves the intent incomplete: it is not consequence delivery.
The native consequence dispatcher and scheduled reconciliation remain required
owners; until that route exists, the current composite dispatcher rejects the
unknown message type instead of acknowledging a no-op.

`schemas/moderation-capabilities.json` is the finite native activation inventory:
52 consumer obligations, all 12 non-chat capabilities and explicit Embedded and
RemoteStrict sets. It is embedded in the Application assembly as
`Explore.Application.Moderation.Capabilities`; composition loads only that
shipped resource. There is no working-directory substitute or fallback matrix.
The singleton registry caches immutable contract data, never current permission.

Each consumer names its subject/target resolvers, ordinary authorization owner,
entry and terminal checkpoints, writer generation, freshness class, aliases and
conformance obligation. Source support labels are observations, not readiness.
In particular, an atomic outbox producer does not fulfill `queued.moderation`,
and RemoteStrict retains every local obligation plus the actual receiver cutoff.

`ModerationCapabilityRegistry.Match` compares the complete exact required set
with independently observed native sources. Registration evidence binds the
full policy/revision/publication/jurisdiction/rule/capability/mode, inventory
fingerprint, exact boundary contract, source identity/revision/writer generation
and the same current observation instant. Missing, extra, duplicate, unknown,
stale, failed or mismatched evidence returns no accepted registrations. A match
compares evidence only: native publication must still positively qualify
prerequisites, caller and scope, and retain the real source fences through commit.
Authored tests and contract declarations supply no passing runtime conformance.

On 2026-10-20 the maintainer explicitly excluded native scheduled publication
from the current delivery. Inventory revision 2 removes only
`publication.scheduled` and its Publish/Manage references in both modes; it does
not declare that owner implemented or conformant. The complete rationale, both
scope options and future authority/job/recovery requirements are preserved at
`dev/backlog/scheduling.md` in the **main repository outside the worktree**.
`EventLifecycleScheduler` reminders, scheduled moderation reconciliation and
existing queued-delivery obligations remain in scope. Future automatic
publication requires separate feature approval and reviewed inventory re-entry;
an ambient timer never becomes publication authority. Revision/fingerprint and
exact registration matching remain mandatory, with no acceptance of stale or
extra scheduling evidence. This scope change does not promote other support
states or close remaining programme assurance.

## Typed non-chat reference definitions

`ISLAMU.Moderation.Domain.Policies.ModerationReferenceCatalogue` contains the
approved 27 non-chat profiles: CFG01, CFG05-CFG20, CFG23-CFG26 and CFG28-CFG33.
The ten base bundles define all thirteen impact dimensions explicitly, alongside
three additive strategies for publication integrity, age eligibility and
classification integrity. The protective bundle requires exactly one
entitlement-integrity or credential-recovery purpose; both can coexist as
independent contributions.

Reference effects distinguish binary overlay denial from native-owner review,
quarantine, eligibility and operation-specific controls. A compiler must not
flatten a native-owner effect into a blanket capability denial. Timing binds
the selected event end as a value; rescheduling cannot extend it. Grades do not
change physical scope, ordinary permission remains necessary, and restoration
does not recreate cancelled tickets, removed roles or erased history. These are
immutable reference definitions, not adopted profiles or measured consumer
support.

`ModerationScheduleRiskReview` now models a separate owned review obligation,
not another personal sanction. Its immutable source identity, revision, rule,
scope and original interval survive pending, accepted, withdrawn and overdue
transitions. Current coordinates bind the event/session, opaque native session
and participant GUID generations, policy ID/revision/publication and rule
reference separately. A rule definition alone cannot stand in for an adopted
publication. Exact deadlines become overdue at use without waiting for a sweep;
rebind preserves original ancestry and terminal work cannot restart.
Sixty-five Domain cases are authored UNRUN/unbuilt. Retained-state validation
now guards every lifecycle transition, deadline observation and receipt replay:
source identities/revision/interval, canonical UTC deadlines, same-trust owners,
coordinate ancestry and complete mutually exclusive terminal provenance must
remain consistent. Pending metadata cannot claim an at-deadline transition;
overdue metadata cannot predate the deadline. None of these checks supplies
native permission. Product-only compilation of the added guard is recorded in
the task ledger. The draft application admission shell was removed because its positive
constructor could not be used by an Event native provider across the module
boundary. Genuine risk/reviewer admission, atomic persistence/deduplication,
all schedule writers, final credential/disclosure fences and adopted fallback/
settlement remain to be implemented. Structural domain acceptance is not
native permission, qualification or an extension of the original sanction.

`ModerationScheduleRiskReview.Storage` now reconstructs the checked semantic
values from native scalar columns. Original and current schedule/participant
generations, policy publication/revisions, source ancestry, full accountable
human origins and terminal receipts remain separate; only the current tuple
changes on rebind. No JSON converter, owned semantic-value entity or new receipt
factory was added. `ModerationScheduleRiskReviewConfiguration` declares a stable
original-work unique key, optimistic revision concurrency, restricted sanction/
finding/reviewer foreign keys, scope/identity/deadline/state/terminal constraints
and a trust-domain/exact-tenant named authority filter in `ExploreDbContext`.
General tenant bypass cannot widen that filter. Sixteen native hydration,
stale-transition, duplicate-work, trust/tenant and malformed-row vectors are
authored UNRUN/unbuilt. Migrations, provider materialization and positive native
risk/permission remain unverified or unfinished; the storage row is not a grant.
Four additional native storage vectors retain exact accepted finding/assignment
IDs and revisions, deny missing referenced parents, and separate later reviewer
withdrawal from historical acceptance. The history stays intact but cannot
replay current reviewer authority. Twenty storage vectors are now UNRUN/unbuilt;
these structural cases supply no native event-risk admission.

The installed native review-duty owner now implements
`IModerationScheduleRiskReviewReconciliationRepository`. Its bounded pending-ID
scan keeps every authority filter and the exact installed solution/instance/
tenant. Each review reacquires the existing persisted maintenance key, machine
binding, active tenant and qualification epoch under a separate serializable
transaction and row fence. It materializes only the explicit overdue transition.
Before commit, current maintenance custody and the entire frozen review tuple
are independently reread; finite purpose loss, owner/interval/deadline replacement
or stale state rolls back. No original sanction, effect, private material,
notification, credential or settlement is changed. The existing Quartz job
uses a separate tenant-reset cursor and a fresh scope, capped at 100 reviews.
Eleven real native deadline/custody/rollback vectors and two cursor availability
vectors are authored UNRUN/unbuilt. This pass supplies durable overdue metadata,
not positive risk admission, escalation delivery or adopted fallback completion.
An observation earlier than the retained review update now returns a per-row
unavailable outcome before any transition; it cannot throw away the whole
bounded pass or move review time backwards. One additional native rebind/clock
regression case is authored UNRUN/unbuilt, bringing deadline vectors to twelve.

The missing positive join starts before this review row: current native Human
responsibility attribution supports authored EventVersion submission, not a
ParticipantIncident. A roster link identifies a participant but does not prove
incident responsibility. The existing native finding finalizer owns substantive
case, policy, reviewer, response/urgency and attribution custody; a participant
incident qualification and an exact current Event/session/participant/window
risk join must precede admission. All schedule writers must then consume that
owner, including federated imports that assign schedule fields directly.
Credential and private-output owners must independently consume its current
obligation. The stored review and deadline pass do not fill any of these gaps.

### Constitution complaint-window minimum

`ModerationConstitutionTerms` and the constitution source's EF substance
constraint require at least six calendar months for protected complaints.
This is the project's minimum protection, not a statement that every operator
has the same statutory obligations. The existing native source writer and
adoption qualification remain authoritative; this guard creates no complaint
permission and does not change a source's or adoption's finite expiry.
Calendar eligibility is not a 180-day recurrence window or a payload-retention
instruction. Six Domain cases and one native writer denial/rollback case are
authored UNRUN/unbuilt. Production Domain and Persistence compilation passed
with 146 and 62 unsuppressed warnings respectively, zero errors. Generated
migrations and runtime/provider assurance remain pending.

### Explicit protected-contestation custody prerequisite

`ProtectedContestation` is a separate narrative-policy purpose, not an alias for
`ConfidentialAllegation` or `ParticipantInvestigation`. The existing native
source/adoption endpoints can record its explicit finite byte/retention terms
and independently adopt the exact current source. Neither another purpose's
adoption nor a notification receipt grants this custody. Current Human/controller
qualification, purpose-specific latest-source selection, writer receipts and
actual commit revalidation remain required. Existing payload consumers continue
to select their original purposes; this addition opens no complaint-body route.

Legal complaint eligibility and physical payload custody are separate. In
particular, an applicable six-calendar-month eligibility floor does not select
six months of narrative retention. No default retention, erasure hold, complaint
intake authority, independent appeal assignment or outcome is introduced here.
Two Domain and six additional native cases are authored UNRUN/unbuilt, including
cross-purpose rejection, both target kinds, late expiry/owner loss and withdrawal
replay. Domain, Persistence, API and browser production compilation passed with
146, 62, 2 and 230 unsuppressed warnings respectively and zero errors. Native
Release/Debug client generation and schema/client/mutable-policy capture parity
also passed; these are source/artifact receipts, not runtime proof. Generated
migrations and runtime/provider assurance remain open. The contestation lifecycle is not
complete and no policy has been activated.

The selected allegation-stage threshold is an independent investigator's
substantive incident-specific assessment, not mandatory corroboration and not
guilt. Its protected grounds need a separate `ParticipantInvestigation` custody
purpose; existing `ConfidentialAllegation` cannot authorize those new statements.
The existing native source/adoption writer now supports that purpose only for
`ParticipantIncident`. Selection slots, current-source reads, writer verification
and pre-lock Human/owner discovery are purpose-specific. Each lane still requires
actual accountable controllers, mandate, finite retention/size limits and the
separately named privacy owner's exact adoption; neither lane adopts the other.
The original allegation-capture Domain factory explicitly rejects investigation
policies. One new Domain and four native cases are authored UNRUN/unbuilt.
Assessment capture, exact incident/session/window/participant qualification,
attribution consumption, generated schema/client and runtime assurance remain
unfinished; recording/adopting investigation custody alone creates no assessment,
Human case, finding, sanction or risk permission.
`ModerationParticipantInvestigationGrounds` now requires four distinct bounded
assertions: identification grounds, alleged conduct, evidence provenance, and
contradiction/credential-misuse assessment. It encodes the exact machine-consumed
fields within the selected custody byte limit, clears rejected encoded material
and formats only its type name. Encoding is transient plaintext awaiting native
protection, not qualification, a stored finding or permission. Five Application
contract cases are authored UNRUN/unbuilt. Finite encrypted assessment Domain
source implementation is underway separately from its native writer.
The finite encrypted `ModerationParticipantIncidentResponsibilitySource` Domain
row now retains exact original material/narrative, native Event/session/order/
participant generations, incident window, alleged Human, independent assessor/
read issuer, and separate qualification/custody/adoption provenance. It bounds
immutable physical purge and local availability by every original finite grant
and policy deadline; withdrawal never renews or rewrites origin. Parent review
added exact material-to-participant ID/version checks and corrected the structural
fixture to use participant rather than session target identity. Fifty-one Domain
cases are authored UNRUN/unbuilt. This is storage shape, not a native authenticated
assessment producer, finding or admission permission; persistence/erasure/native
writer and attribution integration remain mandatory.
The context now gives assessment rows the exact trust-domain/selected-tenant
filter used by original narrative custody; general tenant bypass cannot widen it.
The existing original allegation read path now shares
`ModerationOriginalNarrativeAuthentication`: exact purpose-1 source/adoption,
original material identity/version/digest and immutable row coordinates bind
Data Protection authentication. Rejected/decrypted bytes are cleared; actual
read-grant, Human, privacy, source and terminal disclosure fences remain with the
native owner. This primitive does not mint assessment access or responsibility.
Three additional native vectors tamper actual stored ciphertext, immutable
row-binding deadline or host protection authority. Every failure checks both
owned read handoff and that the real disclosure callback is never invoked.
These cases are UNRUN/unbuilt. Assessment mapping/storage fixtures are underway;
native producer, exact receipts/attribution and erasure/purge remain open.
Assessment mapping now uses exact UTC ticks for finite deadlines and portable
retention arithmetic, eight relational checks, immutable origin fields and
revision concurrency. Seventeen restrictive parent relationships bind native
occurrence/material/narrative/Event/session/order/participant/Humans/read/policy;
custody source/adoption also bind exact host/trust/tenant. Fifty-six native
storage vectors are authored UNRUN/unbuilt, including no-authority/foreign-scope
and general-filter bypass, unrecorded-time rejection, opaque generation and
100ns deadline roundtrips, private-byte ownership, restrictive deletion and
stale withdrawal. These are storage witnesses, not native investigator permission.
Restrictive narrative ancestry means physical purge must dispose dependent
assessment rows first. The exact serializable, durable-saga cross-tenant owner is
`UserLocationPrivacyErasureRepository.EraseModerationCaseNarrativesAsync`;
assessment disposal is now integrated in both physical owners. Selected-tenant
cleanup spends its single capped page on ended assessment custody first, using
exact tick deadlines or recorded assessment/investigation/original adoption
withdrawal. It fences original parents before assessments and excludes an
original narrative while any dependent assessment remains; another page continues
without exceeding the row budget or breaking restrictive ancestry. Neither a
live role, policy nor decryptor can veto monotone physical cleanup.
Global erasure retains exact durable user/intent/policy/sequence saga authority,
caller-owned serializable transaction and complete tenant inventory/RLS partition.
Identifier-only batches of 100 delete assessments before original narratives for
the erased alleged Human, assessor, read issuer or actual original reporter.
Unrelated purchaser/organizer/submitter/account link/policy owner does not broaden
that predicate. Existing recovery accounting commits or rolls back with disposal.
Seven selected-cleanup and 26 global-erasure cases are authored UNRUN/unbuilt;
provider/RLS/rollback execution remains final programme assurance, not source proof.
Native investigation runtime/provider assurance remains unfinished.
Withdrawal discovery on SQLite is deliberately only a coarse candidate filter:
Julian-day comparison loses sub-millisecond precision. Cleanup now reads a
bounded identifier/deadline-only projection and requires exact stored UTC expiry
or withdrawal at/before observation before deleting any assessment. A clock
regression one tick before investigation/original withdrawal cannot cause early
disposal through rounding. Two additional native vectors are UNRUN/unbuilt,
bringing disposal coverage to 35 authored cases; no runtime assurance is claimed.
The closed investigator intent now names exact case/evidence/material/read,
alleged Human and Session generation/window coordinates plus finite custody and
protected grounds. Manual validation does not encode confidential assertions.
Thirteen contract vectors are authored UNRUN/unbuilt. Early native acquisition
discovers all required incident/read/reporting Humans and both separate custody
lanes before subordinate fences. Actual source Kind34/replay/privacy-owned
writer now stages only authenticated encrypted assessments. The actual native
owner requires independent current assessor and read-issuer authority, live
alleged Human/reporting accounts, exact retained material and Event/order/
participant/Session generations and the incident window. Original purpose-1
ciphertext is authenticated on the same serializable primary transaction;
independently adopted purpose-2 custody bounds protected assessment capture.
Data Protection uses `ISLAMU.Moderation.ParticipantInvestigation.v1` with all
immutable coordinates plus actual native authorization and original ciphertext
fingerprints. Exact retry and both primary/privacy-owned precommit callbacks
requalify retained bytes without extending original clocks. Thirty-four native
capture cases are authored UNRUN/unbuilt.

`ParticipantInvestigation` attribution is a separate Human under-review basis,
with exact nullable assessment ID/revision coordinates forbidden on other bases.
Manual validation and portable storage constraints require revision 1. The native
owner authenticates both protected custody lanes, current incident generations,
independent original assessor/read issuer and the actual Kind34 capture receipt
reconstructed from authenticated grounds. Source shape or roster/account linkage
cannot supply permission. Initial attribution, exact replay, live renewal, current
acquisition and held final recheck all use this same qualification; validity
cannot outlive the assessment. Current finding/approval provenance discovers
assessment and custody Humans before subordinate fences. A missing key, expired/
withdrawn/disposed source or substituted tuple fails closed. Historical attribution
retains scalar provenance without a restrictive assessment FK, so confidential
disposal does not delete history or create current permission. Forty Domain/
Application and 30 native attribution cases are UNRUN/unbuilt. EventSubmission
still requires the actual EventVersion submitter. Native OpenAPI and Client SDK
generation/compilation are recorded below; migrations and final provider/race
assurance remain open.
Responsibility and authorization digests remain separate: assessment grounds and
incident coordinates may change responsibility facts, while a fresh source ID,
grant, custody adoption or deadline alone cannot renew a live proceeding.
An independent current native reviewer may requalify the original assessment
without impersonating its investigator or obtaining a disclosure grant.
The closed authenticated capture adapter is
`POST /api/moderation/participant-investigations/sources`. Its concrete command
rejects unknown JSON members and exposes four bounded grounds fields, not a
generic polymorphic mutation. A manually instantiated validator shares native
side-effect-free intent validation; the handler maps every coordinate to Kind34.
Existing `AddNativeOperations()` discovers the handler from the Application
assembly. The 32 KiB write retains write-rate limiting, response/output no-store
and suppressed idempotency response storage. Success returns only accepted
source identity/revisions; validation and authority failures never echo grounds.
Twenty-nine Application and 24 API contract cases are authored UNRUN/unbuilt;
hosted binding/DI and operational HTTP remain unverified. The actual exported
operation contains all 21 required coordinates/grounds and closes additional
properties. Source inspection found responsibility and custody enums initially
exported as unconstrained integers. `OpenApiStringEnumSchemaCatalog` now registers
`ModerationResponsibilityBasis`, `ModerationCaseTargetKind` and
`ModerationCaseNarrativePurpose` for the native and Swagger adapters; two additional
hosted contract cases are authored UNRUN/unbuilt. Forced native export exposes all
five named responsibility bases and both separate custody purposes. Regenerated
Client SDK compiles, exposes the named enum and uses shared string-enum serializer
configuration rather than transmitting its unrelated CLR ordinal. This is artifact
and compilation evidence, not hosted binding or provider execution.

Current participant physical risk remains a separate unimplemented native
producer. CFG15 urgent protection and CFG16 confirmed Event-window exclusion
cannot be inferred from this assessment, attribution, generic urgency/finding or
the Event submitter target source. A confirmed-risk producer must retain an actual
independently final finding plus separately assessed current Event/session/window
relevance, current native generations, finite protected custody and independent
qualified authority. Its custody purpose decision is pending: separate risk
purpose or an explicitly expanded new investigation source/adoption. Existing
purpose-2 adoption supplies neither option automatically. Emergency protector
authority, terminal admission/private logistics, schedule review creation and
unsupported/overdue rights fallback remain open; no positive physical permission
is claimed from pure storage factories or deadline maintenance.

These definitions are not adopted profiles or measured consumer
evidence. `ModerationPolicyProposedStateCompiler` now validates complete proposed
state and produces typed preview contributions and explicit native obligations;
it never returns activation permission.

Calendar delivery still needs a native Event/participant consequence owner.
`ModerationAuthorityParticipant.TargetExistsAsync` resolves a resource-scoped
target to an actual EventResource and its owning Event, not an Event ID
masquerading as a resource. Two human/Content adoption substitution cases are
authored UNRUN/unbuilt. The installed `calendar.export` runtime mapping proves
artifact ownership only. Current calendar handlers enforce ordinary public or
registration-scoped location access, then expose DTOs for file preparation;
they do not produce a terminal moderation/age/participant grant.
`EventDiscoveryDisclosureMutationScope` does not cover registration, order,
participant, admission or moderation writers, and the existing event-generation
reader observes version/actor ancestry rather than those permissions. No second
tracked query, resource grant or discovery epoch may stand in for the missing
positive owner. Implement that owner before a single-use calendar projection
can release privately prepared bytes under exact current purpose, target,
participant, source generations and finite deadline. Previously distributed
calendar copies remain outside server revocation.

`ModerationPolicyCompilationResult.Previews` binds each supported profile/mode
to the exact proposed publication, complete consumer/effect/consequence sets,
activation gates, native prerequisites and applicable diagnostics. Mode-specific
unsupported-source diagnostics retain their `Mode`; an unsupported remote mode
cannot relabel an embedded preview as unsupported. Global/profile-wide
obligations remain visible in every applicable mode. The only dispositions are
`Blocked` and `RequiresNativeQualification`, never ready or active. Source support
labels cannot satisfy absent native evidence. Invalid compilation has no partial
previews; retired definitions remain in the draft but have no activation preview.
This pure projection does not change `Compose`/`ComposeRetained`, native
publication qualification or owning transition semantics.

### Rich native publication and selection

`PrepareModerationPolicyPublicationCommandHandler` commits a finite immutable
`ModerationPolicyPublicationCandidate` before measurement. The candidate reserves
the future publication ID/revision and canonical complete active-state digest.
Its separately identified normalized definition graph has `AdoptedRevision = null`,
no evidence and no active flag; preparation advances its original qualification
epoch but moves neither latest nor active pointer. This resolves the bootstrap
cycle without weakening exact measurement binding or mutating publication history.

The final active writer requires the exact candidate body, unchanged predecessor,
current original preparing administrator and complete current qualification. It
inserts the reserved identity under a new final operation ID and atomically moves
selection pointers; the candidate and definition remain immutable. A nullable
unique `(PolicyId, AdoptedRevision)` preserves adopted-revision uniqueness while
unadopted definitions can coexist. SQL Server's native EF convention filters
nullable unique keys; real provider/migration evidence remains final assurance.
`ModerationPolicySelection` rejects unadopted definitions explicitly.

Consumer evidence now travels through an explicit
`IModerationConsumerEvidenceSession`. The lifecycle participant lends its actual
private acquisition during initial qualification, before publishing `_acquisition`;
publication lends its own separately qualified original authority. Neither creates
a provisional lifecycle grant, another participant, a replacement transaction or
an ambient permission cache. Installation and accepted-assertion reads share
`ModerationConsumerEvidenceSession`, retaining installation/challenge/issuer/
acceptance and original operation receipt fences. Closing a borrow invalidates its
transaction identity without closing the caller's transaction. Qualifiers pass
the same borrow to current-source/conformance owners and reject a closed or
different transaction even when an owner returns otherwise matching evidence.
Profile, prerequisite, publication current-source and publication approval
methods now receive that same borrow explicitly. Their callers check it after
each observation, before invoking the next owner; disposal cannot leave a
transaction-ID-only path through the remaining prerequisites.

The concrete session exposes an internal, currentness-checked native view of
its exact context, transaction, source authority and compiled policy. It does
not accept substitute coordinates or start a transaction. Its lifecycle-only
profile callback captures the original private acquisition, including exact
operation, decision, grant and contribution-selection checks. It rereads the
existing profile/adoption proof before and after session validation, without
depending on `_acquisition` already being published. Publication borrows cannot
use that callback as lifecycle configuration authority. This is transport for
original native evidence, not a deployment owner, prerequisite approval or
passing conformance verdict.
The assertion observation still means authentic current provenance, not Passed.

`IModerationMeasurementRuntimeArtifacts` independently reads the eight already
loaded product modules and their deployed bytes. Its Infrastructure singleton
checks loaded/PE module identities, rejects reparse/missing/changed sources and
reproduces the ordered `moderation-products-v1` digest used by the launcher.
This exposes product facts without paths or a verdict. Full runtime conformance
still requires independently current provider/configuration facts, the complete
approved catalogue, actual exercised-provider witnesses and freshly qualified
accepted receipts; neither loaded files nor signature/cardinality prove them.

`ModerationEmbeddedConsumerRuntimeSource` observes explicit actual service
implementations for all 23 Embedded ReadResource consumers. Resolution is deferred
until observation to avoid recursively constructing lifecycle repositories while
they acquire their own runtime-source collection. Missing/mismatched owners or
unreadable/linked/mismatched product modules return no observation.
`ModerationMeasurementProductManifest.RuntimeGeneration` binds consumer, mode,
inventory and the complete pinned product-byte digest. Those bytes include the
compiled concrete owner mappings. The launcher reproduces the same versioned
artifact recipe and separately requires catalogue/challenge consumer equality;
it does not attest service availability merely by reading files.

Native installation, challenge, validator, catalogue grammar and EF checks are
no longer payload-only. Consumer IDs are canonical lowercase 1-64-character
codes; the native writer additionally requires actual membership and fingerprint
in its independently injected deployment inventory and exact observed runtime.
Only Embedded installation/challenge modes are accepted. Domain scalar validity
and database nonempty-ID checks are not inventory membership or permission.
Provider migrations must be regenerated and verified at the final programme
gate; no generated migration or snapshot was hand-edited.

Candidate and challenge preparation retain the definition ID separately from the
future binding publication ID, so the foreign key points to real committed
definition content rather than a not-yet-existing final publication. Acceptance
checks that exact definition source; the launcher reconstructs original candidate
request provenance and refuses changed predecessor/content/role/window. Human
and maintenance acquisition discover the original preparer before canonical
human/role fences. Exact replay and ambiguous commit requalify original sources.
Publication and current activation deadlines include preparation expiry, and a
later ordinary draft never silently retires the independently active pointer.

`ModerationPolicyPublicationRepository` owns the complete immutable relational
publication graph and its identity-bound operation receipt. It qualifies the
actual human, tenant, host and epoch before replay, then retains the jurisdiction
slot fence through its serializable commit. Expected revision, previous
publication and native source coordinates must all match. Definitions, historical
meanings, profiles, consumer boundaries, prerequisite obligations and all thirteen
consequence dimensions are saved together with the epoch and selection pointers.

`PublishModerationPolicyCommandHandler` is the native CQS entrypoint for this
complete graph. Its validator is manually instantiated and bounds immutable
authoring intent before native acquisition; it cannot supply historical meaning,
current permission or activation evidence. The repository repeats the necessary
pre-serialization bounds for direct native callers. Only the accepted policy
identity, publication identity, revision and publication-time state leave the
handler. Current conformance and native entities are not response authority.

`ModerationPolicySelection.LatestPublicationId` records authoring progress;
`ActivePublicationId` changes only on complete active qualification. A later draft
does not silently retire an active publication. Restoration compares stored
obligations with compiler-derived completeness, so damaged storage cannot become
a passing subset. Instance isolation follows the jurisdiction tenant, not the
tenant that published the instance definition.

The writer and selected-profile reader are composed. The resource-read snapshot
now reads the complete latest rich declaration graph through
`ModerationPolicyPublicationGraph`. `ModerationAuthorityParticipant` now reads
the same retained rich publications under each actual jurisdiction slot fence.
Case-source finding/adoption commands still require their clean structural
cutover; the two representations are not mirrored.
No mirror or compatibility adapter is authorized. Production measured deployment,
consumer, prerequisite and semantic consequence evidence owners remain absent:
inactive publication works without them, while active publication fails closed.
Stored evidence is historical provenance, never current activation permission.

### Native lifecycle qualification

The participant retains latest declarations for status, withdrawal, revocation
and expiry. Proposal terms must match the complete selected overlay profile:
subject/scope shape, exact capability set, duration/review boundary and approval
count. Native-owner controls, event-bound intervals and zero-quorum notices
require their typed owning lifecycle; they cannot be flattened into this
decision ledger's blanket capability mask.

Eligible new activation selects the actual supported active pointer before
receipt replay, not the latest draft or a `PublishedActive` flag. The native
publication repository independently requalifies the full policy's current
deployment/consumer/prerequisite/consequence evidence. The activation source
also qualifies this exact profile, decision revision and complete typed
contribution, including targets, intervals and saved plans. Both qualifications
return their actual validity deadlines. The committing acquisition retains
the earliest deadline and cannot extend its original lease during a later
qualification. Resolve rejects expiry before a receipt return or commit.

Findings retain separate rule and reason owner/code/revision coordinates, using
the established `ModerationRuleReference` value contract for both identities.
Finalization matches the requested reason against the independently selected
retained profile; native finding acceptance and approval also compare the
retained reason. Hydration validates its scalars, and reversal preserves them.
A later publication or support change cannot rewrite the original reason.
Provider-generated schema updates remain required before deploying these fields.

Reviewer and urgent-evidence lifetimes now require separate current
`ModerationQualificationPolicyCurrentSource` and adopted
`ModerationQualificationPolicyApproval` observations for the exact host, case
and scope. Source identity/revision/writer generation and both lifetime limits
must match independently. The source is reread after approval acquisition and
again while the grant is retained. Portable profile timing and authoring bounds
cannot supply these procedural limits. The owner must retain its actual source
and approval fences on the caller's primary transaction and cannot acquire
human locks after case acquisition.

The port has no default approval or production evidence owner. Missing,
withdrawn, expired or substituted governance remains unavailable; the controlled
native test witnesses supply no runtime conformance. Current ordinary human/
maintenance authority, case/finding state, independently qualified controllers
and quorum remain mandatory. Human source discovery/fencing still precedes
subordinate source acquisition.

Finding acceptance retains the independently qualified adjudicator assignment.
It rejects an adjudicator account controlled by the decision issuer even when
the account itself differs from that issuer. Controller identity, not a second
account or delegated credential, establishes reviewer independence. The same
native source checks reject conflicted reviewers, copied occurrence identifiers
in place of owned final findings, and substituted subject origins before mutation.

### Immutable notice source

The notice source captures one original transition under its writer transaction,
not an asynchronous reconstruction of current restrictions. Its separately
approved safe text, adopted profile, exact target revisions and thirteen-dimensional
plans remain bound to the stable effect identity. Private finding justification
is never converted into a subject explanation.

`ModerationStore.Session.SaveAsync` stages capture with the decision, sanction,
operation receipt, effect intent and payload-free wake-up in the same serializable
transaction. The version-2 envelope preserves original decision/finding, installed
host, writer tenant, qualification epoch and reviewer evidence coordinates.
An inert envelope requires no finding, approvals or sanction and no substantive
notice; it does not borrow disclosure approval or use an operation-kind allowlist.
Substantive capture requires its independently current disclosure owner.
Capture failure or expiry rolls back even a shared-context flush. After its
final flush, the writer resolves the exact acquired authority again and checks
both native and capture deadlines. Receipt replay and ambiguous-commit verification
require the original matching capture; changed payload bytes or a mismatched hash
cannot verify a receipt, even when the JSON remains semantically parseable.

Capture now independently rereads the selected rich publication under its
jurisdiction fence instead of reconstructing structural rules. The snapshot
checks the complete selected semantic digest, not just matching publication
coordinates. Class and reason meanings come from that adopted graph; the
separate disclosure approval must approve those exact strings.

Substantive Content capture also borrows the original native participant acquisition.
`IModerationNoticeCapture` receives the originating `IModerationSession`; the native
participant checks the exact private Store session, receiving context, operation,
grant object, tracked effect and current primary transaction before exposing its
already-held representative plan and fenced humans. A copied grant or a previous
Store session rebound through another acquisition cannot substitute for that frame.
Neither captured source metadata nor a source-observation record is transferable
authority. Capture does not reacquire a fresh representative Role/Actor plan behind
Tenant or reconstruct fenced humans from the representative list.

Both initial custody checks and the existing-source branch use that acquired plan.
The latter returns the unchanged original source only while its exact occurrences,
current native representatives and original finite deadline still qualify. Store
checks the persisted effect/source, original JSON/hash/retention and original capture
deadline after the full primary flush and at the actual co-located privacy commit,
then invokes that same strengthened capture check. Nothing renews an original
window, rewrites source bytes or acknowledges an effect. Inert capture and Human
subject semantics stay independent of the concrete Content-only borrowing path.

Nine new real-native acquisition/inert cases are authored **UNRUN/unbuilt**. They
cover exact/copy/foreign-context/old-session/rebound-session/effect/acquisition/
transaction frames and key-independent inert capture; they are not themselves a
complete substantive Content source-owner/Store success or final-flush witness.
The additional fixture source described below remains unrun. Source-only review found
no concrete material defect, not runtime/MAD proof. Application, Persistence and API
source compilation passed with 182, 62 and 2 unsuppressed warnings respectively and
zero errors (38.45 s, 2:33.91 and 11.20 s). Scoped Roslyn diagnostics remain unknown.

#### Resource-Content prerequisites and substantive capture fixture

Resource-scoped Content cases retain their original Resource scope and Content
Event subject, but administrative acquisition uses the actual parent Tenant for
the closed case-prerequisite family. That family now also admits qualified urgency,
contribution selection, Resource-target adoption and original disclosure approval.
Each mutation names its actual native case; the post-acquisition checks still require
the exact case scope, tenant-origin Content subject, Resource-to-Event ancestry,
current publisher/organizer and original independent writer evidence. These four
additions are Resource-only: neither new Event-scope mutations nor profile adoption,
activation or a relaxed global Tenant gate are introduced.

Source review found that reaching the existing case helper from urgency/selection
could acquire Event Actors after Tenant. The family now nominates an immutable case,
typed subject/scope and native Event-parent plan through the existing non-retaining
provider discovery. Its publisher/organizer join the sorted Actor union before Tenant
and qualification epoch. The actual case and native ancestry must match that original
plan before any subsequent Actor acquisition; changed parents reject without taking
replacement Actor locks late. This also orders the already-admitted case prerequisites;
it does not make discovered coordinates transferable permission.

Sixteen additional fixture cases are authored **UNRUN/unbuilt**. Their setup uses
real generation, case, Draft CFG09 publication, procedural policy/adoption, reviewer
controller/conflict, urgency and finding writers, then actual contribution selection,
every target-binding ordinal's independent adoption, and exact disclosure approval.
Only the starting Proposed decision is an explicitly historical boundary. The actual
participant, source owner, capture, notification materializer and Store execute urgent
`AcceptFinding`, not activation. Normal and actual co-located commit expectations cover
the immutable substantive snapshot and Subject/Reviewer/ContentRepresentative
occurrences. Late assignment, parent and finite-deadline changes are injected after
complete primary persistence or during the actual outer disclosure flush; failed
expectations require the decision, receipts, effect, captured source and notification
graph to roll back. Separate cases reject foreign Resource/Event scope and late
publisher/organizer substitution. No fixture execution, provider concurrency, delivery
route, activation or runtime conformance is claimed.

The initial four-mutation gate change compiled with 62 unsuppressed warnings and
zero errors (2:31.45). Its initial Actor-order correction also compiled with 62
warnings and zero errors (2:27.28). Follow-up review found that terminal disclosure
reacquisition rediscovered the case-parent plan despite borrowing its representative
plan. It now borrows the original case-parent plan too, retaining even its absence;
two further cases require changed parents to reject with no late replacement Actor
anchor and a missing original plan never to be reconstructed. The final correction
compiled with 62 unsuppressed warnings and zero errors (2:22.84); focused source-review
follow-up found no remaining attributable finding in that bounded correction, not
runtime/MAD proof. Scoped Roslyn diagnostics remain unavailable. No migration or
public wire shape changed, and no database was updated.

Recipient qualification uses a separately approved, independently current
reviewer-policy source bound to the exact case and scope. Its maximum
qualification lifetime is not borrowed from authoring duration ceilings.
Source revision, expiry and the real identity/conflict evidence remain necessary
through the committing transaction.

`Compose` still rejects unsupported profiles for new-contribution previews.
`ComposeRetained` reconstructs the full saved contribution meaning for a
qualified recorded transition, including unsupported profiles, without granting
activation. Notice capture uses that retained path so retirement cannot remove
the explanation of a withdrawal, revocation or expiry. It retains target,
subject, timing, complete effect/plan and owning-transition validation. Historical
restoration independently checks the complete base/additive saved plans against
the exact retained catalogue revision; a missing base plan cannot escape through
the declaration digest. Composition consumes the saved plans, not a newly
reconstructed consequence plan. Current activation selection still requires
supported profiles and complete current-inventory integrity.

Notice entities, mandatory trust-domain/tenant filters and atomic writer capture
are composed, including protected original portal reads and the dedicated
original SMTP final-use owner. Complete other target/consequence owners,
generated migrations and behavioural/provider assurance remain open.
An optional native portal pointer does not complete the effect's other
consequences, and no unrelated email purpose is reused to conceal missing
moderation delivery authority.

`IModerationAuthorityParticipant` is the native transaction participant. It must
acquire the actual permission sources before returning a positive frame.
`IModerationAuthorityContext` alone is insufficient: revisions are observations,
not locks. The participant shares the acquisition context, re-reads native
sources under their fences and retains them through commit. An unavailable
participant cannot produce an unrestricted evaluation.

The store validates the ambient tenant and PostgreSQL's native
`app.current_tenant_id`, rejecting unbound, bypassed or mismatched leases.
Moderation query filters stay closed outside the store-owned qualified frame.
Tenant rebinding clears that frame. Mutable reads are tracked for optimistic
revision predicates; read-only restrictions are snapshots. Hydrated aggregates
are validated before their semantic state is returned.
Case, decision and contribution queries bind the complete subject origin in SQL,
not only its kind and identifier. A qualified row must exist before its global
row fence is acquired; a foreign identifier cannot lock an unrelated record.

Known mutations commit the aggregate, contribution, qualified receipt and
minimized effect intent together. Denied, unavailable, missing and conflicting
operations abort the unit and clear failed tracking. Commit-uncertain
verification opens a fresh serializable transaction, obtains current authority
and checks the exact qualified receipt. External delivery is never replayed.

### Native effect ownership and recovery

`IModerationEffectLeaseRepository` is scoped to the native primary context and
owns serializable claim/read/release and original-inert completion transactions. It fences the actual host,
registered maintenance service, persisted API key/binding, tenant/status and
qualification epoch before the effect. Configured credentials remain denied
without their native generation-fence authority. Canonical decision scope binds
tenancy; the original operation receipt binds the effect's cause coordinates.
An Instance-scoped decision has no scope tenant, but its original capture has
one exact writer tenant. Recovery discovery and even direct read/claim bind that
source owner; instance jurisdiction cannot authorize another tenant to discover
or lease it. Original-inert completion compares source tenancy to that actual
maintenance tenant, not the decision's nullable scope-tenant coordinate.
Absent original ownership remains unavailable; purge metadata supplies no
completion or substitute execution authority.
Later decision state cannot replace its recorded cause or retrospectively grant
activation.

`ModerationMaintenanceAuthority.AcquireForEffectAsync` is a separate closed
identity/fence acquisition for an actual effect lease. Its canonical effect,
decision, operation receipt, original writer tenant and unelapsed lease are
reread on the primary Serializable transaction, including actual PostgreSQL
tenant binding. It uses the same native human/controller/role, installed service,
persisted credential and epoch fence order as reconciliation, then rechecks
current credential possession and original-source lifetime. Wrong, stale or
elapsed lease ownership is rejected before original payload selection. The
source is first projected as tenant/deadline metadata; reaching R returns no
acquisition without selecting SnapshotJson, even before physical cleanup. A
final deadline/lease check still bounds the completed acquisition. The lifecycle
`AcquireAsync` entry remains Reconcile-only. This acquisition never grants a
Human operation, interprets a consequence plan, qualifies a native target,
extends retention or completes an effect; the resource executor must still
join those independently qualified facts and final cutoffs.

Claims allocate their UUIDv7 identity, start and two-minute expiry once, outside
execution-strategy replay. Exact coordinates and current lease validity are
checked after the final flush and during commit verification. Expiry at that
boundary rolls back the claim and revision. Expired leases can be reclaimed;
release requires the exact owner/revision, even after expiry. Ambiguous release
recovery accepts only the next unleased generation after this invocation
observed and released the requested owner; a committed successor is untouched.
Release and lease ownership never mark consequences complete.

`CompleteWithoutSubstantiveWorkAsync` requires a saved version-2 envelope proving
`HasNoSubstantiveWork`, its actual payload/binding hashes, original cause receipt,
retained writer tenant, installed host/epoch identity and full immutable scope/
subject ancestry. Current decision or epoch advancement does not rewrite the
original empty work. The exact lease must remain live after the final flush.
Lease writes now flush accumulated disclosure mutations after the primary save
and before the final credential and exact-lease checks. Automatic transaction
finalization therefore cannot advance disclosure after the last validity check.
Three native inert-completion vectors observe actual disclosure-epoch advancement:
current completion retains the exact lease, while reaching its original deadline
or revoking the native credential rolls back completion, disclosure advancement
and the injected credential change without releasing or renewing the lease.
All three remain UNRUN/unbuilt under the implementation-first override.
Commit-uncertain verification recognizes only this invocation's completion
coordinates; a repeated external completion conflicts instead of adopting a
successor. `ReadWithoutSubstantiveWorkAsync` separately verifies a previously
completed original-inert effect without changing ownership or renewing expiry.

Both original-inert operations first read only source tenant/retention metadata
under the original notice fence. The canonical inert writer supplies no
disclosure retention ceiling and retains the actual committing tenant on its
opaque source, even when decision jurisdiction is Instance. The original
snapshot retains that same writer coordinate while its decision scope remains
Instance. This does not stage notices or invent a tenant delivery scope; there
is no disclosure graph for inert work. Capture replay and committed-operation
verification require the source's writer tenancy to match the captured writer,
including after uncertain commit acknowledgement. A source with finite `RetainUntil` belongs to
substantive private custody and is rejected before `SnapshotJson` is fetched,
whether still live, beyond R or awaiting application-side erasure settlement.
The dispatcher can then route its opaque effect pointer to the qualified native
owner. The inert probe grants no access to that owner's private capture and does
not retrospectively classify substantive work as empty.

The real `CompositeOutboxMessageDispatcher` routes the pointer-only wake-up and
dead-letter reconciliation to `ModerationEffectOutboxDispatcher`. Each invocation
creates a fresh native scope, binds the explicitly configured reconciliation
tenant, authenticates the selected maintenance credential, and clears machine/
tenant state afterward. A proven original-inert effect is claimed and completed;
duplicates reverify the original capture before acknowledging. Busy, foreign,
missing, corrupt or substantive work throws so normal outbox retry/dead-letter
semantics remain intact. This is the configured single-tenant maintenance scope,
not automatic fleet discovery. Real substantive source/consequence producers
remain required; notice or delivery does not substitute for their completion.

The existing Quartz reconciliation job also runs one bounded effect-recovery
pass through `ModerationEffectRecoveryService`. Native discovery selects
incomplete intents with absent or already completed outbox wake-ups and no
live lease, under the installed maintenance identity and selected tenant.
Pending, processing, failed and dead-lettered outbox rows remain owned by their
existing retry/operator workflow. Recovery creates only an ephemeral canonical
pointer and calls the real dispatcher: it neither creates a new intent nor
marks an outbox row complete. The dispatcher reacquires original cause, capture
and current native maintenance authority. Unsupported substantive work remains
pending. An independent tenant-bound effect cursor advances past denied work;
sanction, effect, notice-cleanup, resource-review and schedule-risk cursors all restart when
the configured tenant changes. The job clears all five before assigning the new
cursor tenant or invoking any owner. A cleanup success followed by failed
discovery therefore cannot persist foreign sanction/effect progress under the
new tenant. Same-tenant failures preserve the owning cursor's last acknowledged
position. Quartz job data is progress only, never authority or effect completion.
Each bounded owner runs independently in a fresh scope: pre-finding expiry,
narrative expiry, authenticated notice cleanup, sanction reconciliation, resource
review, schedule-risk review and effect recovery. A stage exception does not
prevent subsequent owners from making independently qualified progress. The
sanction cursor is acknowledged after its own bounded pass, not after resource
review. Failed stages do not acknowledge unfinished scans; successful stages
retain their own progress even when the fire ultimately fails. The job rethrows
the first stage failure after attempting the remaining stages, without logging
private exception details or emitting its successful-completion message.
Requested cancellation stops immediately, clears the active scope and skips the
remaining stages. Physical expiry still does not require maintenance credentials;
every privileged stage authenticates afresh. Eight scheduler regression cases
are authored but unrun; these are cursor/failure-boundary contracts, not native
provider, effect-completion or activation evidence.
`ModerationReconciliationBatch` and `ModerationEffectRecoveryBatch` snapshot
incoming target collections into immutable arrays, including record `with`
assignments. Reusing a query's mutable discovery list cannot erase its accepted
targets while retaining an advanced scan cursor. Their read-only public shapes
and JSON arrays remain unchanged; discovery still conveys no authority.

`ModerationReconciliationQuery.ReadDueAsync` projects and compares both
`ScopeEventId` and `SubjectOriginEventId` between the sanction and its decision,
alongside the other typed coordinates. Its coordinate reconstruction uses
`AuthorityScope.FromCoordinates`, preserving the complete ancestry rather than
rebuilding a Resource-only leaf. A valid Event-qualified subject therefore
retains its exact origin in the discovered target and CQS command; a different
decision Event cannot satisfy the join. The existing eligible sanction-scope
filter and publication/profile checks remain the discovery boundary, followed
by independent native operation qualification. Two origin round-trip vectors
(Resource and Event) and one decision-Event substitution vector are authored
against the real SQLite discovery fixture and remain unrun/unbuilt.

Three scheduled-expiry vectors are authored against the actual native lifecycle,
receipt-backed generation/contribution/target/profile/disclosure writers,
complete notice capture and primary flush/commit callbacks. The loss vectors
invalidate the exact qualification revision or capture deadline after the complete
flush, requiring rollback of sanction, receipt, effect, notices and notification
graph. Historical starting aggregates/credentials are explicitly not activation
evidence. These cases remain unrun/unbuilt under implementation-first execution.
Seven additional native vectors now specify the distinction between already-ended
expiry and response-dependent activation. One withdraws original response custody
through its actual native writer before expiry; three remove the original custody
revision or encrypted notice/response column after the complete primary flush.
They require expiry without borrowed response evidence, changed acknowledgement,
restored private bytes or renewed retention. Three Resource-target vectors use
native Resource generation and per-ordinal target adoption, verify exact Resource/
Event/stamp lineage and exercise successful expiry or complete-flush rollback.
All seven are authored UNRUN/unbuilt with clean source diagnostics. Positive
active-policy activation, Content lifecycle coverage and downstream effect
completion remain separate assurance requirements.

`ModerationStore` explicitly flushes accumulated disclosure mutations after its
complete primary save and before rechecking capture lifetime and current native
authority. This also covers a separately owned primary transaction: commit-time
disclosure finalization cannot run after the last lifecycle permission check.
The colocated privacy owner's repeated flush remains idempotent. Three additional
native scheduled-expiry cases enlist actual disclosure work and observe its
persisted epoch advancement before retaining valid custody, expiring capture or
revoking the maintenance credential. Loss must roll back the sanction, receipt,
effect, notice/notification graph and disclosure epoch, including the injected
credential mutation. These cases are authored UNRUN/unbuilt; compilation is not
runtime or provider assurance.

## Committed recovery coordinate readers

Canonical and Embedded retained-erasure metadata readers derive separate
read-only, private-cache, unpooled SQLite connections from their original native
connections. They accept no caller-supplied database path and export no connection,
private authority fact or snapshot. The retained reader uses mapped counter
columns for store identity, revision, recovery generation, high-water, retained
floor and identity-key ID/verification tag. The complete committed tuple must
equal the originally acquired tuple, and the same identity owner/context/
transaction gate must still qualify after reading. Initialization, uncommitted
append/key binding, another owner or escaped gate lifetime cannot supply it.
Twelve native vectors are authored UNRUN/unbuilt for separate and colocated files,
including committed legal-hold compaction and release. A hold release advances
the exact revision even when high-water, floor, generation and key reference
are unchanged; an old tuple cannot stand in for the resulting store state.

These readers are plumbing, not completed backup/restore/reconciliation proof.
Whole-file artifact custody, finite disposal, authenticated isolated restoration,
real retained-erasure replay and canonical catch-up remain separate owners.
Current measured-source terminal qualification also advances the canonical head
before reacquiring operational evidence; a committed recovery reader correctly
rejects that mutation transaction. Integration must separate pre-write proof
acquisition from post-flush currentness without weakening that rejection or
claiming recovery of uncommitted writes. No operational proof adapter is registered.

The independent reviewer acceptance boundary also requires unchanged qualification
authority after the full graph flush. An authored epoch-loss vector requires
acceptance, receipt, epoch and canonical-head rollback together while retaining
the original immutable report and deadline; the existing expiry vector now checks
the same graph. These cases remain UNRUN/unbuilt, with fresh LSP unavailable.
Current recovery qualification of a historical report remains an explicit pending
contract decision: transaction-local continuation does not certify recovery of
the canonical head created by writing that report.

Measured-source record/acceptance/withdrawal and exact receipt replay now reject
pending tracked work at terminal verification. A full source or acceptance flush
cannot be followed by an unrelated staged case and still return qualified success.
Two native post-flush staging vectors are authored UNRUN/unbuilt and require
source/acceptance, receipt, epoch and canonical-head rollback together, with no
foreign case persisted. The guard does not weaken immutable proof comparison,
manufacture recovery evidence or decide the pending recovery-currentness contract.

## Lifecycle and generated schema

The shared lifecycle can propose against an exact open-case revision, reconcile
scheduled activation or expiry, and supersede one exact contribution. Proposal
issuer and issuing scope come from the grant. Supersession requires that grant
to cover both scopes and prohibits widening; replacement and superseded
contribution pointers are stored in the same effect intent and transaction.
Clock time is read again after activation qualification. Effective half-open
expiry does not depend on a sweep, and review deadlines do not become punitive
expiry.

Primary migrations are native EF-generated artifacts for PostgreSQL, SQLite,
SQL Server and MySQL/MariaDB. The [selected-authority scaffolding
workflow](OPERATIONS.md#add-an-application-migration-with-the-selected-secret-authority)
preserves secret ownership when selecting another provider model. Generation is
not proof of database migration behavior; that proof remains the programme's
real-provider final gate.

## Writer and fence ownership

### Native authority source ownership

`ModerationHostBinding` records installation-established trust-domain, solution,
instance and selected existing maintenance-principal identities. It is not the
bootstrap timestamp or a request-derived trust root. A checked singleton slot
and unique index prevent another binding from silently reparenting the primary
store; the selected principal has a restrictive foreign key.

`ModerationQualificationEpoch` has one owner per binding and actual tenant,
restrictive parent foreign keys, a positive revision check and an
application-managed concurrency token. A stale writer cannot overwrite the
committed generation. Primary-context queries also require the current tenant
filter. Neither row initializes permission or publishes an active policy:
installation, current native qualification and adopted policy remain separate
positive operations. Qualification must still join the actual source-row fences
described below rather than treating the epoch label as permission.

The native `EstablishModerationAuthoritySourcesCommand` manually validates
preallocated UUIDv7 coordinates. `ModerationAuthoritySourceRepository` owns its
serializable execution-strategy unit and rechecks actual instance-admin
assignment/role, canonical user/login bindings, selected service and tenant
status under native row fences. A transaction-owned installation lock protects
the initially absent singleton. Identical setup preserves an advanced epoch;
changed historical coordinates conflict. Ambiguous acknowledgement verifies the
exact persisted binding and epoch rather than allocating replacements.

Machine callers are rejected through `IMachinePrincipalAccessor`. A user-owned
API key can carry its owner's internal user identity, but cannot inherit that
human's instance-installation privilege. This is separate from later explicitly
qualified maintenance operations. The installer establishes source provenance,
not an adopted policy or active enforcement.

`ModerationMachineSubjectBinding` associates exact credential source, ordinal
key ID, authority tenant and owner coordinates with an explicitly selected
existing `ServicePrincipal`. It never derives identity from a key name, service
code, human owner or maintenance principal. Replacement credentials can bind to
the same canonical service, preserving sanctions across credential replacement.
The source slot is unique per installed host, credential source and key ID;
historical coordinates are immutable and revocation is terminal.

`EstablishModerationMachineSubjectCommand` manually validates its coordinates.
Its native repository rejects machine callers, acquires current human/provider,
administrator, host, tenant and qualification fences, then fences the selected
service, its actors and the exact persisted credential. New bindings advance the
qualification epoch in the same serializable commit; identical retries preserve
it. Changed service selection or ownership cannot rewrite a binding. Reads
recheck the selected credential and service, reject deleted or suspended service
actors, and do not fall back between configured and persisted sources. Configured
descriptor changes are external to relational fencing and require fresh
consumer checks. This provenance foundation does not complete owner liveness,
resource consumer conformance or profile activation.

`IConfiguredApiKeyResourceAuthorityReader.ReadAsync` remains a point-in-time
metadata read, not terminal custody. `AcquireAsync` instead returns one original
named-options/descriptor lifetime with immutable principal metadata, its actual
nullable UTC expiry and validity cancellation. The registered API implementation
subscribes to named-source reload and then revalidates the captured source,
closing replacement across subscription. It never adopts replacement metadata,
even when the values match. Revalidation also checks the original descriptor,
private credential-hash identity and all captured owner/scope/expiry coordinates;
credential material and source objects are not exposed by the lease.
Actual expiry invalidates the lease. A monotonic acquisition budget prevents
backward wall-clock movement from renewing that bound. Long deadlines use bounded
native timer scheduling, not a shorter authority TTL; a source with no expiry
uses source invalidation without inventing a deadline. Disposal cancels and
releases the original subscription/timer; no observer means no terminal lease.
Twelve cases using the real named OptionsMonitor/change-token path are authored
but unrun/unbuilt. The native terminal owner must still bind the authenticated
caller, retain primary/privacy/writer sources and consume validity cancellation
through actual completion. This source lifetime is neither authentication nor an
action grant, and does not establish native consumer conformance or activation.

`AcquireAuthenticatedAsync` has the narrower consumer purpose: it requires this
request's server-issued `ConfiguredApiKeyAuthenticationOrigin`, matching actual
configured-key caller coordinates and key ID. The real handler records the exact
options/descriptor references, hash actually matched, immutable successful-ticket
metadata, original expiry, and original clock/timestamp after successful configured
authentication. No raw credential is retained, and the receipt's diagnostic text
does not expose metadata or hash. Persisted, expired and failed authentication
issue no configured receipt.
Authenticated acquisition preserves that original source and monotonic budget,
not a new sample with equal metadata. Missing receipt, different caller/key,
credential rotation or re-created named options fails closed, without falling
back to metadata acquisition. Normalized scope-set comparison matches actual
ticket claim normalization without allowing additional scopes.
The existing two configured-success logging scenarios now exercise real
authentication, real named OptionsMonitor retention, absent-origin rejection,
wrong-key rejection and equal-metadata source recreation; failed/persisted
scenarios require no configured receipt. These cases remain unrun/unbuilt.
Native consumer integration must use authenticated acquisition, not infer original
credential provenance from the general metadata-only Read/Acquire operations.

The native `AddModerationMachineSubjectBindings` migrations add only the binding
table and its constraints/indexes on PostgreSQL, SQLite, SQL Server and shared
MySQL/MariaDB models. Existing keys are not automatically assigned a service.
Restrictive foreign keys prevent deleting the installed host, selected service
or authority tenant while historical bindings reference them. `Down` removes
the binding table, not service identities or sanctions; rolling it back discards
binding provenance and must precede any protected consumer activation.

The permanent qualification foundations are in the shared Domain assembly.
`ModerationFinding` binds a final finding to an actual open case revision,
owner-qualified rule, adjudicator and response or qualified-urgency reference;
reversal preserves original ancestry and appends its exact audit.
These Domain checks validate source structure, not the truth of evidence
references. Native source writers and lifecycle participants must establish
those facts under the primary transaction's current writer fences. The Domain
foundation alone neither registers an enforcement path nor activates a profile.

`ModerationFindingsController` exposes the existing finalization and reversal
writers through authenticated JSON POSTs:
`/api/moderation/cases/{caseId}/findings/{findingId}/finalize` and
`/api/moderation/findings/{findingId}/reverse`. The route owns both finalization
identities or the reversal finding identity; immutable command copies preserve
the original operation, scope intent and all expected revisions. The two concrete
commands reject unmapped JSON members. A body cannot supply adjudicator identity,
administrator status or a completed response opportunity. The manually validated
handlers still acquire those facts through `ModerationQualificationRepository`,
not from controller claims or a new authorization bypass. Independent reversal
retains original rule/reason provenance after policy replacement.

These named routes use 64 KiB request bounds, the write limiter, no-store and
suppressed generic idempotency-response storage. Native receipts remain the
replay authority; minimized qualification responses expose no confidential case
or response text. Eighteen new transport cases are authored UNRUN/unbuilt:
unknown/missing provenance, both route identities, authenticated/noncached
metadata and hosted anonymous rejection. Existing native finding vectors now
invoke the real typed handlers, with self-adjudication, foreign-tenant targeting,
exact finalization/reversal replay, changed-intent replay rejection and
self-reversal assertions. Those native vectors are also UNRUN/unbuilt and are
not hosted authenticated HTTP-to-native proof. Application/API source compilation
passed, with unsuppressed warnings in untouched files; Roslyn diagnostics were
 unavailable. OpenAPI and `IModerationFindingsClient` were subsequently regenerated
 through the existing export/NSwag pipeline, including strict request schemas and
 matching Release/Debug schema, client and mutable-policy captures. Client source
 compilation passed with 230 unsuppressed warnings and zero errors; this also
 compiled the earlier editor-cancellation and protected-response focus changes.
 The export host logged a lifecycle-delivery scan failure, not a healthy runtime
 receipt. Scoped focus diagnostics timed out. All test sources remain UNRUN and
 unbuilt; authenticated HTTP-to-native and provider/race assurance remain open.
No new HAL action is advertised without an independently qualified case read
projection. Finding finalization is not decision acceptance, sanction activation
or classification recurrence; historical publisher acceptance and original
conduct/history qualification remain separate unfinished owners.

`ModerationReviewerAssignment` starts unresolved. Current controller evidence
and an independently assessed conflict record are necessary, with half-open
expiry and irreversible withdrawal. Distinct accounts sharing a controller
cannot supply independent quorum; renewed controller proof invalidates the
previous conflict assessment. Current native roles and actual controller and
conflict evidence still require fenced server qualification.

`ModerationPolicy` separates inactive adoption from qualified activation.
Complete-state replacement returns a new checked revision and retains immutable
rule meanings. Enabled effects require exact publication/scope/revision/time
binding and complete prerequisite and registered-consumer evidence. The
supported mask is explicit per rule capability; complete profiles must prove
their entire required effect set rather than silently omit an unsupported
consumer. These Domain checks validate source structure, not the truth or
ongoing freshness of native evidence. Publication and lifecycle writers must
qualify that evidence under their current fences; adopted identifiers alone
neither grant permission nor activate runtime enforcement.

Native qualification persistence uses `moderation_findings`,
`moderation_reviewer_assignments`, `moderation_policies` and
`moderation_policy_rules`. Scalar finding and reviewer rows retain restrictive
case parents; policy children retain their actual policy owner and revision.
One unique `(jurisdiction_kind, jurisdiction_id)` owns the current policy head
on every provider, avoiding nullable-ancestry uniqueness differences. The leaf
identifier is checked against its tagged ancestry, while reviewer rows preserve
the actual case tenant for direct filtering. Child policy reads inherit current
parent trust-domain and tenant filters.

Native EF scaffolds own the provider schemas. Hydration restores scalar/child
state without rerunning publication, then validates the complete Domain state.
Database checks reject partial controller/conflict proof and invalid revisions;
native concurrency rejects stale qualification writes. Applying the schema
neither records evidence truth nor installs the checked source mutation writers
or an active enforcement profile.

Native qualification writes use `IModerationQualificationRepository`, the
manually validated `ApplyModerationQualificationCommandHandler` and the scoped
`ModerationQualificationRepository`. A request names authorable targets and
expected revisions, never supplied permission or activation evidence.
`ModerationQualificationAuthority` resolves the current human through
`IAdminContext`, rejects machine-principal privilege inheritance, fences actual
users, login bindings, administrative grants, memberships and role definitions,
then rereads persisted host, tenant and qualification ancestry on the owning
serializable transaction connection. Peer identities are acquired in native
key order before subordinate source rows. Subject-authored responses require
actual case ownership; they do not require administrative privilege or an active
tenant membership.

`moderation_qualification_evidence` retains immutable response, controller,
conflict and finite urgency assertions with their actual case and native
assessor. Qualification ceilings come from the current adopted policy rather
than an unrestricted caller expiry. Finding finalization checks the current
reviewer, retained controller/conflict evidence and their native administrative
assessors before accepting an adopted rule and response or unexpired urgency.
Reversal appends the existing finding's accountable audit. Policy adoption
preserves previously adopted rule meaning and resets publication to inactive;
it never manufactures consumer or prerequisite evidence.

Procedural limits now have a separate native owner,
`IModerationQualificationPolicySourceOwner`. The deployment activation port
does not supply a fallback. `PublishModerationQualificationPolicyCommand`
creates or replaces `moderation_qualification_policy_sources`;
`AdoptModerationQualificationPolicyCommand` retains a distinct frozen approval
in `moderation_qualification_policy_adoptions`; its explicit revocation command
invalidates that approval. These use the existing native source writer,
actual administrator acquisition, case/slot/row fences, epoch advancement and
exact request-bound receipt. Source replacement invalidates an old adoption
without rewriting it. Limits and finite validity are operator intent, never
defaults or derived restriction duration.

The registered `ModerationQualificationPolicySourceOwner` joins current open
case/scope/revision, current source, separate adoption and their actual immutable
native mutation receipts. Source writer generation is its resulting
qualification epoch; adoption retains that generation, not its own later write
epoch. Both reads retain the shared case-policy slot and source/adoption row
fences through the caller's transaction. Missing, stale, revoked or expired
governance fails closed. Reviewer and urgency writes can qualify independently
of deployment measurement; actual restriction activation still requires its
separate current deployment, prerequisite and consumer-conformance owners.
The new provider schema requires native generation; no migration was hand-edited.

Each accepted source mutation advances its tenant qualification epoch and
inserts `moderation_qualification_mutation_receipts` in the same commit. The
retained receipt binds the exact request digest, current principal, host,
tenant, source kind/identity, resulting source revision and accepted generation.
An identical authorized retry returns that retained outcome before rejecting
its now-stale expected generation. Changed request reuse conflicts, and
uncertain commit acknowledgement is reconciled only against the exact receipt
under newly acquired current native authority. No external effect runs inside
the execution strategy. Evidence and command diagnostics omit private text;
receipt contents contain no statement or justification.

Acquire privacy serialization first when identity changes. Within the primary
moderation transaction, acquire jurisdiction and qualification sources before
subject and policy sources, then owning Event dependencies. Sort peers by their
native keys. The table describes required participation, not completed runtime
enforcement for later programme phases.

| Writer | Authoritative source and fence | Acquisition connection and release owner | Linearization point |
| --- | --- | --- | --- |
| Tenant qualification grant/revoke | Actual `TenantUserRoleGrant`, membership, user and relevant role rows; provider-native row fence conflicts with the ordinary repository update | Store's primary connection; transaction completion releases row locks | Grant/revocation commit |
| Event reviewer role changes | Actual `EventRoleAssignment` and owning Event/role rows, not a sanction-only lock | Same primary transaction; native source participant retains ownership | Role/window update commit |
| Sanction transition | Exact qualified decision/contribution revision and subject fence | Primary transaction; native participant plus application-managed revision predicate | Accepted receipt commit |
| Policy publication | Actual owner-qualified immutable policy and active revision sources | Primary transaction; policy mutation owner joins the participant order | New active policy revision commit |
| Identity link/unlink | Existing privacy identity boundary before subordinate moderation sources | Selected privacy authority connection followed by the primary connection; each owner releases its own leases | Qualified identity authority commit |
| Erasure | Existing authority-first privacy boundary; exact durable recognition receipt where eligible | Privacy authority owner and primary moderation owner; separate-store settlement remains explicit | Erasure authority fact commit after required receipt |
| Enrollment/departure | Exact trust enrollment and monotonic authority generation | Enrollment owner; native/remote participant as explicitly selected | Enrollment/departure generation commit |
| Admission/disclosure | Current capability plus qualification/policy/identity sources retained by the final native owner | Final owner transaction or bounded final disclosure handoff | Native final commit or irreversible handoff |

`RelationalNamedLock` retains MySQL/MariaDB acquisition connection ownership.
Disposal must not recover that connection from a committed `DbTransaction`,
whose `Connection` may be null. SQLite uses the repository's documented
single-instance coordination; it does not acquire distributed guarantees.

## Event-resource final disclosure

Private file and redirect preparations remain owned by their resource-authority
lease until `CompleteHeadersAsync` requalifies native facts in a fresh
serializable transaction. The final gate compares the current resource,
attachment, governance, disclosure and provider input with the accepted snapshot.
A changed or unavailable source releases no preparation; failure disposes the
private content or destination. Cancellation and the original deadline remain
binding through the single-use handoff. The gate does not replay external
provider work inside that transaction.

Audience detail and collection queries return `EventResourceReadProjection<T>`,
not a publicly readable DTO. Its single-use ownership also applies to native CQS
callers. Representation preparation can build HAL privately, but delivery runs
only after fresh scope, row, alternative-resource and continuation qualification.
Original and terminal cancellation tokens remain binding. The HTTP
`EventResourceProjectionResult<T>` executes this protocol after result filters,
immediately before invoking the prepared serializer. Serializing a native result
cannot disclose the private payload.

`EventResourceReadProjection<T>` also checks linked cancellation after delivery:
a callback that ignores cancellation cannot return Allowed. Its HTTP result
registers transport abort on that original/consumer token, awaits the prepared
result and `Response.CompleteAsync` directly, and retains the projection's
existing preparation owner until completion settles. Completion failure aborts
and propagates instead of appending an error representation. Cancellation does
not detach outstanding completion or release preparation early.
Two Application arguments and four HTTP cases are authored but unrun/unbuilt.
These tests cover transport and preparation custody only, using controlled entry
outcomes, not native qualification or conformance. The current audience final
authorization still returns before delivery; the original source/privacy/writer
fences must separately be joined to this terminal lifetime. This transport fix
does not establish that missing native ownership or certify activation.

Management detail/list, audit and metadata export use the same private projection
protocol. Final checks retain exact resource versions and attachment generations.
Audit disclosure also binds the original retention policy and the earliest
retained entry's expiry: expiry during representation preparation or the final
provider call refuses the prepared page, even before retention cleanup runs.
Export requalifies both every export target and the decisions that produced its
download affordances; changed download authority cannot leave a stale link.

Audience detail and collection projections also capture the exact native
download, access and management outcomes in immutable batched proof arrays.
Server-owned affordance fields are excluded from HTTP JSON and have internal
setters; link policies use them only as candidates for the ordinary permission
check, never as input grants. Terminal disclosure rechecks view and action
requests in one bounded batch and compares the action outcomes with those that
prepared the representation. Any gained or lost action outcome invalidates that
representation even when metadata view remains allowed. A fresh read can retain
the metadata while omitting the denied action. This avoids one query per link
and does not change the cursor or HTTP DTO shape.

Native audience and management query consumers must replace direct `Value` access with
`Projection.DiscloseAsync(prepare, deliver, cancellationToken)`. Preparation
constructs a private representation; only `deliver` may serialize or transfer it.
The terminal outcome, not the initial query's preparation status, determines
whether delivery occurred. A consumed, refused or cancelled projection cannot
be replayed. This is a native source-contract change; HTTP DTOs and routes retain
their existing shapes.

Resource upload reservation, idempotent reservation, finalization, finalized
replay, cancellation and cancelled replay return `StorageUploadSessionResult`.
Mutation authority requires the existing `update` permission. Reservation, staging,
attachment and finalized replay additionally select the server-owned `Upload`
moderation purpose; ordinary updates and cancellation do not inherit that purpose.
A successful mutation does not itself authorize disclosure of persisted session fields.
Resource success carries only a private projection, backed by a separate
`view-management` authority lease; ordinary generic uploads retain their ordinary
command response. The terminal gate compares the original accepted read snapshot
with current resource, caller, canonical moderation and provider facts after
representation preparation. Read denial can therefore withhold the response
without undoing an already committed reservation, attachment or cancellation.

Both the resource reservation endpoint and generic finalization/cancellation
aliases execute `EventResourceProjectionResult` for resource-owned success.
Their HTTP success envelope remains `BaseCommandResponse<StorageUploadSessionDto>`.
Native callers must consume the projection and dispose the result; it has no
direct resource-session `Id` payload. The projection atomically owns its payload
and lease, releases ownership on failed preparation, denial or delivery, and
supports explicit disposal of an abandoned result. Replaying the mutation never
replays a consumed read grant.

`EventResourceUploadMutationBoundary` wraps each of those four upload transactions
in the existing privacy identity/recovery serialization. Before the workflow can
lock a session or resource, it acquires the installer's existing named fence
(`moderation.authority-source.installation`), the canonical Human row when present,
the shared host-binding write fence, the existing Tenant write anchor, and the
current qualification epoch row, in that order. The host fence also serializes
instance-scoped qualification writes originating in a different tenant; a
tenant-local epoch alone would not cover them. Tenant precedes epoch to match
participating disclosure writers. Installation
also writes that Tenant anchor: a stale PostgreSQL serializable snapshot must retry
after concurrent first adoption rather than treating an absent host as permission.
These transaction-owned fences are retained
through the actual commit, not released after reading a snapshot. Provider object
writes remain outside both the primary and privacy transactions; attachment
reacquires the boundary after the provider returns. Stable session/staging IDs
survive local transaction retries.

Successful callbacks must preserve the original purpose-specific moderation facts
and a currently permitted half-open interval after disclosure flush, immediately
before commit. Both the ordinary primary commit and co-located privacy-owned
commit invoke that check, using a fresh clock after the authority queries return.
Provider-conflict retries enclose the entire privacy/primary unit, including the
co-located path; they do not retry only its final SQL command.
Denial rolls back the successful mutation; failed
results may still retire staging and release quota. Cancellation and failed-stage
cleanup retain their existing owners rather than requiring permission to upload.
ReadResource response authorization remains separate. Participating writers use
the shared fences without a restart; direct SQL or other nonparticipants require
the approved writer protocol or an owned maintenance exclusion. The installation
fence serializes these short local upload transactions and is not held during I/O.

The native snapshot reader keeps `ReadResource` and `Upload` adoption, continuity
results and generation hashes separate, including mixed batches for the same
resource. An invalid server purpose fails closed. Recognition continuity accepts
only those two resource capabilities, retaining exact target and unresolved
recovery checks. Thirteen new persisted-reader/transaction-boundary cases are
authored **UNRUN/unbuilt**, including cleanup rollback distinctions and a scheduled
restriction starting before or after a write flush, expiry during final authority
queries, and a genuine shared-connection privacy-owned commit with rollback.
The co-located case asserts that privacy owns the transaction; it is not a
separate-authority fixture relabelled as co-located. These fixture facts are not
native activation evidence. Complete adopted-Human workflow/racing-writer,
co-located final-flush and multi-provider conformance remain open; no consumer is
promoted to Supported by this source increment.

Resource requests and fresh snapshots now carry value-equal
`EventResourceMachinePrincipal` coordinates: the exact opaque string key ID,
authority tenant, owner kind, owner ID and credential authority. Authentication
emits `explore:api-key:source`; missing, unknown or duplicate source claims remain
unqualified. A machine request has no human
`SubjectUserId`, including when its key belongs to a user. Batch qualification
cannot combine different machine coordinates. Audience proofs capture the
original caller before native preparation and compare it with the current
caller at delivery. Audience cursors use purpose/query version 2 and bind those
same coordinates; old tokens require a fresh first page rather than a legacy
identity fallback.

Every native machine snapshot rereads exclusively the captured credential
authority. Persisted keys use the fresh no-tracking authentication repository
lookup, require Active or PendingRotation status, and require expiration strictly
after the evaluation instant. Key ID, authority tenant and owner coordinates
must still match. Configured keys use the named API-key options monitor, exclude
absent, duplicate, inactive, expired or malformed descriptors, and copy metadata
without credential material. Neither source can fall back to the other after
revocation or deletion. These checks run before preparation and again before
final metadata transfer. Every tenant-bound credential also rereads its exact
authority Tenant with the current TenantStatus lookup and requires
`IsActiveState`; suspended, archived, purged or unavailable tenants cannot
authorize delivery. Tenant-owned credentials must name that same tenant as
OwnerId. This checks the credential tenant, independently of the resource's
tenant, through an exact-ID no-tracking entity read.
User-owned credentials also require a current
nondeleted user and an active, nondeleted membership in the credential's exact
authority tenant. The bounded no-tracking membership read includes its user;
owner or membership removal during preparation prevents terminal disclosure
even when the key remains Active. This does not borrow the user's roles or
human moderation subject. InstanceAdmin-owned credentials require global
ownership (no authority tenant), a current nondeleted owner and a current
platform-scoped `platform.admin` assignment. The bounded no-tracking assignment
read includes the actual User and Role, so owner deletion or assignment removal
during preparation prevents terminal disclosure without granting the machine
that human's administrator permissions. Organization and group credentials
require the actual nondeleted owner and its exact nondeleted, nonsuspended
placement in the authority tenant. The bounded no-tracking placement reads
include the Organization or Group, retain the normal tenant/soft-delete
filters and do not borrow a human issuer's management permission. Owner
deletion, placement removal or placement suspension during private preparation
prevents terminal disclosure even when the key remains Active.

These boundaries do not close the whole moderation consumer contract.
Resource-read moderation is adoption-gated: installation alone preserves the
existing native resource authorization. An applicable policy adopted for
ReadResource enforcement requires current canonical subject/service provenance
and available moderation authority; missing qualification prevents disclosure.
Inactive adopted definitions enable no restrictive effect. Per-capability
publication support and complete native consumer evidence must remain current
before activation, and unrestricted moderation never grants resource access.
Adoption nevertheless requires current canonical read provenance. Retiring
activation support does not make the policy unadopted or erase existing sanctions;
the read consumer continues evaluating the current persisted contributions.
The reader uses retained declared profile effects, including unsupported profiles,
instead of the active pointer or current inventory support labels. Latest publication
must match its actual host, jurisdiction, policy and revision, and its restored
declaration digest must match the saved complete state. Missing scope/effect rows
or mismatched selection coordinates fail closed, rather than making the target
unadopted. Historical inventory changes do not withdraw read protection.
Adoption is selected at instance or tenant jurisdiction; a resource-scoped profile
declares protection for that jurisdiction's resources, while each sanction retains
its exact resource scope. An unrelated resource therefore remains adopted but
unrestricted when it has no matching contribution. Snapshot generations bind the
actual policy/publication/revision and declared-state digest along with current
subject, epoch, binding and contribution revisions.
Canonical service provenance and the shared resource-read overlay are integrated.
Complete consumer conformance and activation qualification remain open. Those gaps
preclude a complete `ReadResource` registration or profile activation.

## Verification and activation boundary

The PostgreSQL race witnesses hold the native qualification row through the
owner transaction, probe the competing row with `NOWAIT` and then execute the
ordinary revocation repository. They also exercise revocation committed before
an otherwise reusable evaluation request. Signals precede triggers and have
bounded timeouts; no sleep or timing-based negative assertion proves locking.

This schema and transaction machinery do not activate a moderation profile.
Native permission composition, policy publication, effect draining, protected
remedies and complete consumer coverage remain mandatory programme work.
Drafts, imported definitions and schema installation confer no authority.
