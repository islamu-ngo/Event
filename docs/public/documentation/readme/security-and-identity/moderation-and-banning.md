# Moderation and banning

Moderation restrictions apply to a qualified subject, jurisdiction and action.
A publishing restriction is not an instance-wide login or admission ban.
Independent contributions retain their original scopes and expiry; reversing
one contribution does not remove another applicable restriction.

The shared definitions distinguish an Event from an individual resource, even
when their identifiers match. Stored Event ancestry retains its exact tenant;
classification correction is the only reference profile extended to Event scope.
This model support does not enable a policy, authorize disclosure or certify
provider migrations and native enforcement.

The generated API contract describes the existing subject-safe pre-finding,
quarantine-review and recognition-recovery HAL responses and their collection
items. This corrects empty or untyped generated wrappers; it does not add an
endpoint, expand disclosure, change grant permissions or make a HAL link an
authorization substitute. Existing case-read grant/revoke operation names and
URLs are unchanged.

Generated clients describe safety-review and recognition-recovery enum values
as the named strings already returned by the API, not integers. Optional
attestations and recovery grounds may be absent when unavailable; that absence
does not imply a default decision or grant an action. Regenerate clients from
the corrected contract; endpoint permissions and runtime disclosure are unchanged.

**SQLite upgrade caution:** the combined develop/moderation histories include
resource-table rebuilds. The migration generator rejects an unexpected physical
column layout instead of silently discarding material or quarantine evidence.
The generated `RestoreMergedResourceAvailabilityOrder` correction restores a
missing availability check, but cannot recover evidence already lost by an older
rebuild. Operators must review applied migration order and use an exclusive,
separately verified upgrade procedure; never bypass the preflight or mark a
migration applied manually. Merge compilation and SQL generation did not execute
any migration or certify provider upgrade/rollback behavior.

The operation-receipt and pending-effect models now preserve that Event tag too,
rather than rejecting it after a qualified change reaches the store. Unknown
scope tags remain invalid. Eleven authored cases remain unrun; this correction
does not enable Event moderation or claim completed enforcement.

Portable configuration now retains the exact scope name `Event` in instance and
tenant manifests and tenant packages, with matching generated schemas. It does
not substitute Resource or Tenant, and ordinal or case aliases remain invalid.
Five new wire cases remain unrun; accepting a document does not authorize its
publication or enable Event enforcement at the destination.

Group and organization membership changes now use the server's authenticated caller, including when
invoked through native commands rather than HTTP. A command cannot name another
person as its administrator requester. Missing, unauthenticated or empty identities
are rejected before membership data is read. Existing membership permissions,
tenant participation, duplicate prevention and last-administrator protections remain;
HTTP request shapes are unchanged. The authored caller/state regression cases remain
unrun, and this correction does not certify the complete moderation overlay or
concurrent membership enforcement.

Organization membership keeps its resource-authorization checks as well as its
organization, tenant-participation and last-administrator protections. Its three
additional invalid-identity cases are authored but unrun; they are not proof of
concurrent enforcement or complete moderation support.

Tenant-role grants and revocations now acquire current native tenant-administrator
authority as well as the configured authorization provider's permission. Direct
handler calls cannot bypass that boundary or supply their own audit identity.
Successful role changes invalidate the existing moderation qualification revision
in the same database transaction; duplicate grants and already-revoked no-ops do
not change it. Failed final identity checks roll back both changes.

An authorized administrator may revoke their own role; that completed revocation
cannot authorize a later change. Retained erasure also blocks an actor or a newly
granted target before primary identity cleanup finishes. If a commit acknowledgement
is lost, the same invocation checks its exact stored role change and qualification
revision instead of reporting its own completed grant as a duplicate or denying its
own completed self-revocation, even when the database provider does not retry.
If the original transaction rolled back, a non-retrying failure remains a failure.
A conflicting later change is not replayed as if it
were the original request. This does not provide cross-request idempotency.
The boundary does not install moderation authority or activate a policy. Twenty
real-store regression cases are authored but unrun, and complete membership
moderation and concurrent provider enforcement remain unverified.

Organization membership changes now invalidate an existing moderation qualification
revision in the same transaction as the membership change. An unchanged role does
not advance it. This protects independent material review: becoming an administrator
of the publishing or organizing organization makes that person an interested party,
including when they control the nominated reviewer. A previously independent review
cannot rely on an earlier membership snapshot after that change commits.

Administrative membership writes still need current organization permission and
the configured authorization provider's approval. Invitation decline remains bound
to the actual recipient, not a supplied user identifier, and cannot remove the only
administrator. An authorized self-demotion or removal does not authorize a later
change. If its commit acknowledgement is lost, the same invocation reconciles the
exact membership and qualification revision; this is not cross-request idempotency.
The boundary neither installs moderation authority nor activates a policy.

Membership changes now hold the exact Organization and its Actor sources before
Tenant and moderation-revision locks, matching ordinary organization/profile-source
saves. This avoids the previous reverse acquisition order without changing who may
administer or decline an invitation. Initial organization creation keeps its separate
creation transaction. Persistence source compilation passed with 62 warnings and
no errors; source review is not concurrent database or runtime assurance.

Invitation validation and decline use the current signed-in recipient, not a user
identifier supplied by a native caller. Validation remains read-only. An outsider's
decline returns the same unsuccessful "Invitation not found" result as an absent
invitation and does not change membership or its qualification revision. The native
invitation and missing-identity regression cases have been updated but remain unrun.
The invitation list also uses the current signed-in, provider-linked user rather
than a supplied or claimed email address. Sharing or claiming the recipient's
email does not reveal that person's invitations. An email claim is no longer
required to list invitations for an identified user. The matching native HTTP
and query regressions are authored but unrun; this is not runtime privacy assurance.

Fifteen membership regression cases and two connected reviewer/controller promotion
cases are authored but unrun. They do not certify concurrent database enforcement,
the complete membership moderation overlay or deployment readiness.

### Creating an Organization and its first administrator

The native creator now comes from the actual authenticated, provider-linked Human,
not a creator ID supplied to the command. Organization, private contact record,
Organization Actor, tenant participation and first administrator commit together.
The existing approval distinction is preserved: ordinary creation remains Pending,
invisible and not organizer-eligible; a current Tenant administrator may create
Approved, visible and organizer-eligible participation. Its actual approval authority
is checked through the final commit, not only when the request begins.

Erasure restrictions, changed identity or loss of that approval authority roll back
the creation and its initial administrator. Existing moderation qualification advances
in the same commit when installed; creation does not install or activate moderation.
Ambiguous commit acknowledgement is reconciled against the exact original graph, not
treated as permission for a second creation. Public route and input DTO shapes are
unchanged, and the pre-create authorization ceiling remains in force.

Sixteen native cases are authored but unrun and unbuilt. Application, Persistence and
API product compilation passed with no errors; focused source review found no concrete
material defect in this scope. This is not provider-concurrency or deployment certification.
Organization contact-data collection/age admission, managed provisioning and the
canonical sanction overlay remain separate responsibilities.

### Creating a Group and its first administrator

The first administrator now comes from the actual authenticated native creator,
not an identity supplied to the creation command. Group creation, its Group Actor,
Pending tenant participation and first administrator commit together. A retained
erasure restriction or creator identity change before commit rolls back that entire
graph. This does not approve participation or grant organizer eligibility.

When moderation authority is already installed, its qualification revision advances
in the same commit. Ordinary creation without an installed host does not install or
activate moderation. Hierarchy and image validation remain in place. Ambiguous commit
acknowledgement is checked against the exact graph from that invocation; it does not
provide a new reusable authority or cross-request idempotency receipt.

Creation now takes the tenant hierarchy lock before its native identity and Tenant
source locks, matching the existing hierarchy-update protocol rather than waiting
for an update while holding the Tenant source. Both paths retain their original
transaction owner; this is not a requirement to restart other replicas.

Fifteen native cases are authored but unrun and unbuilt. Application compilation
passed; an incorrect provider-Tenant method call found by compilation/source review
was corrected, and Persistence/API then compiled with no errors. A later hierarchy-order
correction also compiled successfully; focused source-review follow-up found no
additional attributable defect. This is not
provider-concurrency certification or complete
membership moderation. Managed provisioning and the sanction
overlay remain open.

### Group membership changes

Ordinary group invitation, role-change and removal now share a native transaction
boundary with moderation qualification. A changed membership and its existing
qualification revision commit together; an unchanged role does not advance it.
Promoting a reviewer or their controller to administration of a publishing group
must not leave their earlier independence snapshot usable for material acceptance.
Existing duplicate, position and sole-administrator protections remain.

Membership writes now acquire the Group and its Actor sources before Tenant and
qualification revision, matching ordinary Group/Actor changes. This removes the
opposite source order without requiring a blanket restart or changing who may manage
the group. Initial creation retains its separate hierarchy-first transaction ownership.

Membership management requires current authority for the exact tenant, group and
action, not administration of an unrelated organization or authentication alone.
Local checks and Cerbos principal projections now carry the same action-specific
tenant/group scope. Machine principals retain their separate scope ceiling and
cannot acquire another tenant's group authority from this projection.

Authorized self-demotion or removal does not authorize a later change. Lost commit
acknowledgements are reconciled against the same invocation's exact stored membership
and qualification revision, not a new cross-request idempotency receipt. The boundary
does not install moderation authority or activate a policy.

Fifteen native membership cases, two connected material-review cases, twelve
authorization/projection cases and three Cerbos policy cases are authored but unrun.
Role-permission assignment, replacement and removal now participate in the role fence
used by membership changes. Replacing a permission set preserves the audit information
of grants that remain, rejects unknown permission identifiers before replacing any
grants, and leaves caller-owned serializable transaction rollback intact. Weaker caller
transactions and repository-owned retry with unsaved caller changes are rejected rather
than silently risking stale grants or lost work. Role usage checks also
include live group memberships within the current tenant. Eight corresponding real-store
cases are authored but unrun; global deletion has its own separate operation below.
One of those unrun group cases preserves administrator authority when an optional
permission-code lookup is absent; it does not grant ordinary members that authority.
Two others check actual normal/co-located source order. The correction compiled with
62 warnings and no errors; source review is not database contention or runtime proof.

The complete sanction overlay and runtime assurance remain open.
These changes are not certification of concurrent
database enforcement, live Cerbos parity or complete membership moderation support.

### Deleting an unused custom role safely

Deleting a custom role now removes its definition and permission joins together, rather
than first committing a permission wipe. A blocked or failed deletion leaves both intact.
The operation checks references across tenants, including platform assignments,
organization/group memberships, tenant grants, Event assignments and invitations.
Deleted memberships, revoked grants and expired assignments or invitations also prevent
deletion: retained history cannot lose the Role it refers to. Another tenant's rows are
not returned, and the request's tenant context is not changed.

System roles remain protected. A caller-owned Serializable transaction retains rollback
control; pending unsaved work or weaker transaction isolation is rejected. Permission
cache invalidation and policy synchronization follow successful deletion, not a partial
grant removal. These checks do not establish a new administrative permission.

Fifteen real-store cases are authored but unrun/unbuilt. A focused source review found
no further concrete material defect. Application, Persistence and API compilation
passed with zero errors; existing warnings were not suppressed or claimed fixed. Real database
contention, native caller authorization and the complete moderation overlay remain
unverified or unfinished. This is not activation or complete membership-moderation support.

Deployment migration preparation for the accumulated moderation/profile model is ongoing.
The first native migration-generation attempt was blocked by a known dependency audit
failure before any migration was generated. The approved repair now uses Pkcs/Xml
10.0.13, the required Blazor/Data Protection 10.0.13 packages, MessagePack 3.1.9 and
SignalR Client 8.0.32 in its existing dashboard family. Fresh affected restores and
locked-mode checks passed without weakening security auditing. This does not update
or certify an operator's installed .NET SDK/runtime, prove vulnerable paths were
reachable, or establish a compromise. Keep required third-party notices when rebuilding
distributions. The eight affected product builds passed with zero errors; existing
warnings remain. Migration source was generated and compiled for all four owning
assemblies; MariaDB shares MySQL's assembly. Review caught SQL Server removing the
private birth-date receipt's required PII-delete cascade. Only that isolated model
relationship is corrected; the native additive SQL Server migration generated and
compiled without rewriting the original history. Its generated downgrade also changes
an evidence relationship inconsistently with the preceding migration. Do not apply
that downgrade: this discrepancy still needs a source/generation correction.
Five model regression cases remain unrun/unbuilt.
No database was updated or migration behavior tested.
The generated downgrade removes the newly introduced records and declaration fields;
it is not a data-preserving rollback or an approved deployment procedure.
Compilation and the logical schema guide alone do
not make the moderation programme ready to deploy.

### Event-team changes use the signed-in actor

Adding event-team members, changing or revoking their roles, adjusting assignment
dates and transferring ownership now use the actual provider-linked signed-in
Human, not an actor ID supplied to a native command. The checks run before target
access or audit writes. Ordinary changes require team-management authority;
ownership transfer requires its separate transfer authority. Existing ownership,
duplicate-assignment and validity safeguards remain in place.

Routes and HTTP request shapes are unchanged. Product compilation passed, but five
authored cases remain unrun. This does not establish concurrent commit enforcement,
qualification invalidation, coverage of initial-owner creation, or complete
membership moderation support.

Commit-bound event-authority reads now take role and permission source locks
before event ownership locks. On SQL Server and InnoDB, preliminary source
discovery does not retain the shared locks that could prevent a role-source
writer from finishing. The reader validates the complete planned source set
under its real locks; a new or changed role requires a new attempt, not permission
from a late-discovered source. Content notice custody acquires all original
representatives' role sources together, rather than requesting a second role after
retaining the first representative's event locks. Current revocation and assignment
validity still apply. Five regression cases are authored but unrun. The initial
and corrected source compiles passed; focused follow-up review found no further
material issue within that correction. This is not complete concurrent event-team moderation or
deployment qualification.

Event-role delegation now reads current native active/filtered permission flags,
not the permission-list display cache. Filtering a permission therefore removes it
from the delegation ceiling without waiting for that display cache to expire;
unfiltering or activating it is also evaluated from current rows. The existing
same-event management, role-subset and nondelegable-permission rules still apply.
Eight real-store cases are authored but unrun/unbuilt; product compilation passed.
This does not establish assignment/permission changes as one protected commit or
complete membership moderation support.

### Atomic event ownership transfer

The ownership-transfer path now keeps the effective replacement, old-owner revocation,
success audit and moderation qualification revision in one native transaction. A
replacement that is not effective cannot leave a saved partial transfer. Current
identity, retained-erasure state and original permission expiry are checked again
before commit, including when the privacy authority owns the actual shared commit.
Revoking one's own old ownership does not renew its original permission deadline.

An uncertain commit is checked against that invocation's complete assignment, audit
and revision facts. Exact committed facts recover the original success; a confirmed
rollback can retry the whole operation. This is not a reusable receipt or cross-request
idempotency guarantee. Source review identified an inner-retry problem, now corrected
so outcome reconciliation precedes replay.

Fifteen source regression cases are authored but unrun/unbuilt. Application, API and
the final corrected Persistence source compile passed; focused retry follow-up found
no further concrete bug, without executing the scenarios. Other event-team writers, initial-owner creation,
the sanction overlay and provider/concurrency qualification are still unfinished;
membership support remains Partial and nothing is activated by this increment.

### Atomic event assignment-window changes

Changing an event-team assignment's start/end window now keeps the saved version,
success audit and moderation qualification revision in one native commit. Existing
role-delegation limits still apply. Future windows and Pending assignments remain
valid; they are not required to grant immediate access.

Extending one's own window cannot extend the original permission coverage that authorized
the in-flight change. Long-lived team management does not override an expiring permission
needed by the assigned role. Identity, retained erasure and those original deadlines are checked
at the actual commit, including shared privacy storage. Exact invocation-local facts
can reconcile a lost success or denied-audit acknowledgement without repeating the
saved outcome. This is not cross-request idempotency or exactly-once telemetry.

Thirteen window/provider source cases are authored but unrun/unbuilt. Focused review
corrected original permission coverage and Actor-before-Tenant source ordering; the
follow-up found no further concrete defect. Initial product compilation and final
Persistence compilation of those corrections passed (62 warnings, no errors). This is not provider or
concurrency certification. Other event-team writers, initial-owner creation,
sanction enforcement and provider/concurrency qualification remain unfinished; support
stays Partial and no deployment is activated.

### Atomic event-team privilege removal

Revoking an assignment now keeps its saved status/version, stable audit and existing
moderation qualification revision in one native commit. Direct Owner removal remains
forbidden: use the separate ownership-transfer operation. A denied removal can retain
one owned denial audit without advancing the qualification revision.

Retained erasure prevents the actor from authorizing changes, but does not let a
target veto removal of its privileges. That cleanup exception grants no new access.
Removing one's own management grant does not discard the original permission deadline;
the original management and delegation coverage remain binding through actual commit.
Lost acknowledgements reconcile the exact original saved outcome before replay. This
is not cross-request idempotency. Both allowed and denied revocation outcomes are
reported only after confirmation, so a rolled-back denial attempt does not count as
another committed denial; separate requests are not exactly-once telemetry.

Sixteen real-store source cases remain unrun/unbuilt. Focused source review identified
and corrected pre-commit denial reporting and stale denial data after an early not-found
retry. Initial and final Application/API product compilation passed; the final
compilation reported 182/2 warnings and no errors. This is not runtime evidence.
Other assignment aliases, initial-owner
issuance, sanction enforcement and provider/concurrency qualification remain unfinished.
Membership support stays Partial and this change activates no deployment.

### Atomic assignment creation and email invitations

Creating an event-team assignment now saves the grant, its stable audit and the
existing moderation qualification revision in one native transaction. Owner creation
still requires the separate ownership-transfer operation. Duplicate assignments and
ordinary delegation limits remain enforced. Pending and future Active assignments
are valid; undefined or terminal creation statuses are not.

An email invitation remains bound to the Human selected by the original exact email
lookup. The native owner protects and rechecks that identity through actual commit.
If the email is removed, reassigned or ambiguous, it grants neither the former Human
nor a newly resolved recipient. The email alias does not bypass current actor,
privacy or delegation checks, and email is not added to audit/log output.

Lost acknowledgements can reconcile the exact saved grant or denial without another
grant, audit or qualification advance. This is invocation-local recovery, not a
cross-request idempotency promise. A self-grant cannot extend its original permission
deadline while the operation is in flight.

Twenty-two native-store cases and one retaining-provider Actor-save case are authored
but unrun/unbuilt. Domain/Application/Persistence/API compilation passed with existing
warnings and zero errors; focused source review found no further
concrete material defect, not runtime proof. Initial-owner creation aliases, sanction enforcement and
real provider/concurrency qualification are still unfinished. Membership remains Partial;
this source change activates no deployment or policy.

AI proposal decisions now stay separate from nested Event-tool saves until the
existing explicit proposal update. An unrelated tool save cannot prematurely
confirm or reject the proposal; tenant visibility remains unchanged. Six native
cases are authored but UNRUN/unbuilt, covering successful, failed and rejected
decisions and rollback. Product compilation passed with 62 warnings and no errors;
bounded source review found no material regression, not executed workflow assurance. Initial Event-owner
creation remains separate unfinished work; this is not an exactly-once AI promise.

Initial Event creation is being connected to the original creator's current
publisher authority through the whole transaction, including the parent Event and
its initial owner. Community reports remain ownerless and imports keep their
separate path. The new source uses stable invocation identities and requires
whole-creation evidence when reconciling a lost commit acknowledgement; seeing an
owner row is not enough. Thirty-one native boundary cases, eight receipt cases
and eight settings cases are authored but UNRUN/unbuilt. Reconciliation only
returns success or retries when the complete supported write set proves the
outcome. Inconclusive writes, including new physical locations or federation
planning, fail closed rather than replaying creation blindly. Publisher settings
are read authoritatively under their coordinated leases, not from a stale replica
cache. The original owners of referenced images are fenced before the tenant, and
image ownership is checked again before commit to avoid acquiring an earlier source
late. Application and Infrastructure compilation passed with 182 and 45 warnings
respectively and no errors. Initial Persistence compilation failed on five missing
authorization-namespace names; that import is corrected. Final Persistence compilation
passed with 62 warnings and no errors. Bounded source review found no further material
defect after the corrections. No deployed workflow, provider matrix or policy activation
is claimed.

### Publication moderation — implementation in progress

Publication moderation is opt-in. When the instance can positively establish that
no applicable Human publication policy is adopted, the existing authorization,
approval and event-readiness rules continue unchanged. Once adopted, a restriction
or unavailable moderation authority blocks both publishing and administrator
approval. Administrator approval bypasses only the existing approval requirement,
not moderation. Missing or malformed authority is not treated as non-adoption,
and retiring a policy does not silently remove a still-effective restriction.

The source integration evaluates the person making the request against the current
tenant and containing instance. It does not substitute the Event owner, publisher
Actor or a fictional Event resource. Checks are retained until the Event and its
outbox work commit together, including deployments with co-located privacy storage.
Moderation clearance does not grant permission or waive readiness requirements.

Application compilation passed with 182 warnings and no errors. Seventy-eight
regression cases are authored UNRUN/unbuilt. Persistence compilation also passed
with 62 warnings and no errors. Bounded reviews of the transaction owner, continuity,
snapshot and native executor fixtures found no attributable material defect. The PDS rollback witness
stages real outbox rows directly, not a positive federation planner run.
This is not a deployed-workflow or provider assurance claim. That increment did
not cover creation; session publication and machine/Actor overlays remain separate.

Creating an Event directly as Published now uses the same approved opt-in rule
inside the existing creation transaction. Draft requests do not run the publication
overlay, and the saved status must match the requested status. A restriction or
unavailable authority blocks creation before its writes; clearance does not waive
publisher permissions, approval or readiness. The person creating the Event is
evaluated, not the organization Actor or an unrelated administrator.

Creation's own qualification revision increment is accounted for exactly, without
accepting unrelated authority changes or renewing the original deadline. The
original window is checked after awaited terminal reads as well as before them;
expiry rolls back the parent, participation, owner and transaction-owned intents.
Uncertain commit recovery still requires exact original whole-write evidence;
unsupported location/federation receipt coverage stays unavailable rather than
blindly replaying creation. Thirty additional cases are authored UNRUN/unbuilt.
Application compilation passed with 182 warnings and no errors; Persistence
compilation passed with 62 warnings and no errors. Follow-up source review found no
additional attributable causal defect after the corrections. These are implementation
receipts, not evidence of an executed provider or deployed-workflow guarantee.

Session publication now uses the same current-person opt-in moderation rule. A
qualified absence of adoption preserves the existing publication rules; an adopted
restriction or unavailable authority blocks the request, including repeat publication
of an already-Published session. Clearance does not grant session permissions or
waive parent status, readiness or concurrency checks.

The original parent Event stays fixed throughout the transaction. Session state and
the parent's schedule summary commit together, with the existing terminal authority
check; a failed check rolls both back. This adds no session notification or federation
behavior. Seventeen native regression cases are authored UNRUN/unbuilt, covering
restrictions, unknown authority, independent readiness, fixed ancestry and rollback;
source review corrected missing ordinary Event-role authority in the fixture rather
than bypassing session permissions. Application
compilation passed with 182 warnings and no errors; focused source review found no
attributable defect. Neither establishes deployed-workflow or provider assurance.

Human ticket-catalog publication now applies the same opt-in current-person rule.
A qualified absence of adoption preserves ticket-management and paid-commerce
requirements; an adopted restriction or unavailable authority blocks publication.
Admission-target creation, retirement of the old catalog and publication of the
new snapshot share one transaction and its terminal authority check. Failure rolls
those changes back; it does not retrospectively cancel existing orders.

The existing machine publication path is unchanged and is not claimed covered by
this Human overlay. Promotion and add-on handoffs are described below. Sixteen new
native regression cases are authored UNRUN/unbuilt. Application compilation passed
with 182 warnings and no errors; focused source review found no material attributable
defect. Neither establishes deployed-workflow or provider assurance.

Human promotion publication now applies the same opt-in current-person rule.
Qualified nonadoption preserves ordinary promotion-management permissions and
validation. Adopted denial or unavailable authority blocks publication, including
the final transaction check. The promotion definition and its usable code share
one transaction: failed code generation must not leave a published definition
behind. Machine publication remains separate; the add-on handoff is described below.

An explicit restriction returns HTTP 403; unavailable publication authority returns
HTTP 503 rather than an ordinary validation error. Existing idempotency handling
is unchanged, including replay of completed responses. Twenty new API/native
regressions remain UNRUN/unbuilt, including code-generation failure and final-check
rollback under both commit owners. They are not proof of machine or HTTP replay behavior.
Application compiled with 182 warnings and no errors; API compiled with 2 warnings
and no errors. Bounded source reviews found no additional publication defect after
correcting two test expectations for existing validation/not-found behavior. This
is not deployed-workflow assurance; all twenty cases remain unrun/unbuilt.

Human add-on publication now uses the same opt-in current-person publication
check. Qualified nonadoption leaves the existing organizer ownership rules intact;
adopted restriction or unavailable authority prevents publication, including the
final transaction check. Retiring the previous catalog and publishing its
replacement are atomic: a failed final check must not leave either change behind.
The existing machine route and ownership requirements are not newly exempted or
claimed covered by a machine moderation overlay.

Successful publication still returns the existing add-on catalog HAL payload.
Explicit restriction returns HTTP 403, unavailable authority returns HTTP 503, and
missing or unmanageable configuration remains 404. Other add-on operations,
existing idempotency behavior and prior order entitlements are unchanged. This
does not introduce ticket-specific paid preflight for add-ons. Twenty-one new
source cases are UNRUN/unbuilt, including final-check rollback of both catalog
publication and prior retirement. Application compiled with 182 warnings and no
errors; API compiled with 2 warnings and no errors. Focused production/API and
native-fixture source reviews found no concrete causal defect.
These checks do not establish deployed-workflow or provider assurance.

Native registration-form version publication now checks the current person's
publication restrictions before authoring writes and again at the actual commit.
Qualified nonadoption preserves existing registration-manager permissions,
ownership, concurrency and form preflight; it does not grant those permissions.
Schema generation, pinned artifacts and the Published status belong to the same
transaction, so a failed final check must not leave a partially published version.

Explicit restriction is HTTP 403 and unavailable publication authority HTTP 503.
Malformed `If-Match` and native preflight failures remain 400, missing ownership
404 and concurrency 409. Success keeps the existing response. Provider imports,
mapping publication, ordinary authorization and collection-purpose rules are not
replaced by this native-form check. Seventeen source regressions remain UNRUN/unbuilt,
including real registration-manager permission/preflight and post-save rollback
of publication, generated artifacts and concurrency values under both commit owners.
Application compiled with 182 warnings and no errors; API compiled with 2 warnings
and no errors. Bounded production/API source review found no concrete defect;
fixture review corrected an organizer shortcut in the no-permission scenario by
using another person's publisher/organizer. The real EventOwner permission remains
required there; the corrected cases are still unrun, not deployed-workflow assurance.

External-provider schema import creates a published frozen form version, not merely
a provider preview. Its Human lane now checks the current person's publication
restrictions before connection/schema reading and again at the actual commit.
The remote schema, drift evidence, generated artifacts and form publication stay
inside the same native operation. Existing registration-channel management
permission and tenant/Event ownership are not replaced by this check.

Explicit restriction returns 403 and unavailable authority 503 with the original
Event identity. Other import results retain their existing connection, form,
revision or version identities. A blocked-drift evidence revision is still saved
as before; binding provisioning, mapping publication and the existing machine lane
are unchanged. Seventeen source cases are UNRUN/unbuilt, including native
artifact publication, denial, duplicate/drift handling and rollback under both
privacy-storage arrangements. Source review corrected a missing command import
and a permission fixture that accidentally retained organizer authority; native
authorization is still used. Application compiled with 182 warnings and no errors;
API compiled with 2 warnings and no errors. Production/API source review found no
concrete defect. These source checks are not deployed-provider or cross-tenant
runtime assurance.

### Delegated binding publication — implementation in progress

The native mapping publisher is being connected to the same current-Human
publication checks for delegated automation that does not provision remote forms
or manage subscriptions, including the existing Microsoft Forms branch. Its
ordinary channel permission and local readiness checks remain independent;
metadata about a binding does not grant publication permission. The lower-level
native writer keeps its existing readiness contract rather than gaining new
managed-provider requirements.

Restriction and unavailable-authority results retain the binding identity and
map to 403/503. Existing readiness, drift and missing-binding results remain 400.
Managed-provider remote operations and machine callers are unchanged: a local
rollback is not evidence of remote compensation. The original connection,
form/version and binding are held through native commit, so publication cannot
silently switch to a different provider tuple after its checks.

Nineteen new regressions are source-only UNRUN/unbuilt. They cover native hashes,
readiness/drift boundaries, source retargeting and pairwise rollback—not a full
provider/concurrency matrix. Corrected Application compilation passed with 182
warnings and no errors; corrected Persistence compilation passed with 62 warnings
and no errors. The ordered API rebuild passed with 2 warnings and no errors.
Source review found an existing storage-model blocker: publishing
a saved Draft changes a mapping hash that the persistence model treats as an
immutable key. Success and post-save rollback cases therefore cannot yet establish
working publication. A duplicate-binding fixture was corrected without relaxing
its uniqueness constraint. No database or migration was changed to bypass the key
invariant. Registration attempts and callbacks must keep their exact binding and
published-hash references. The maintainer approved a separate immutable
binding-owned publication identity. Its source model now records the final hash
and publication time separately, without replacing binding IDs or weakening
attempt references. Native writes reject replacement, orphan evidence and
incomplete provenance. Both saving a Draft before publication and publishing
before the first insert, including a provider attempt in that same save, are
explicit source-regression cases. Two Domain and nineteen native persistence
cases remain UNRUN/unbuilt. Existing evidence
must transfer unchanged before the new foreign keys are installed. The first native
PostgreSQL, SQLite, SQL Server and MySQL migrations are generated, not applied.
Transfer preflight rejects incomplete
or mismatched hashes, publication coordinates or attempt references; it retains
soft-deleted history without inventing replacement evidence. Downgrade must prove
equivalent retained evidence before removing the separate publication table.
Operators need an exclusive migration window and working CHECK enforcement; failed
nontransactional DDL may leave an integer-only guard table requiring cleanup, never
a bypass of rejected evidence. Forty-four additional SQL-generation cases are
authored UNRUN/unbuilt, not database execution. SQLite validates evidence before
changing foreign-key state and removes the publication table only after restoring
native owner tables. MySQL's hash charset change is limited to canonical ASCII
evidence. PostgreSQL and SQL Server also need their derived attempt hash key
temporarily removed and recreated around the source-column collation change;
the generator preserves the original source hash and restores the exact reference
index and expression. SQL Server's source-dependent provider-pair CHECK is also
temporarily removed and restored unchanged; preflight still precedes these changes.
The four dependency cases now target the actual upgrade and downgrade operations
and their respective models, not merely reversed metadata. They remain unrun.
The CHECK follow-up compiled with 62 warnings and no errors, and bounded upgrade and
downgrade SQL was generated for all four provider families without application.
SQLite generation reported pending-rebuild warnings. Review of its downgrade found
that attempts need their legacy binding key before copying; a temporary unique index
now bridges that native rebuild ordering, after validation and without changing the
retained evidence. Final Persistence compilation passed with 62 warnings and no errors;
the SQLite migration project compiled without warnings/errors and its bounded SQL
regenerated successfully. The emitted downgrade now places that temporary parent key
before copying attempts and removes publication evidence only after rebuilding the
owners. Generation warnings remain recorded. All 65 root-correction cases remain
UNRUN/unbuilt. A final bounded SQLite source/artifact review found no additional
concrete defect; these are not executed transfer or provider-assurance results.
The separately recorded SQL Server downgrade-FK discrepancy now has a targeted
generator correction that retains non-cascading moderation evidence. Regenerate
older rollback scripts; generated files are not approved deployment scripts or
executed provider assurance.
Domain compilation passed with 146 warnings
and no errors; initial Persistence compilation passed with 62 warnings and no
errors before the final ownership-guard correction. No migration has been applied.
The underlying correction also affects managed-provider publication. Support
remains Partial, not an assurance that deployed providers have passed.

### Resource publication — implementation in progress

Resource publication and republication now use the current person's Tenant publication
restriction facts, both before the write and at the actual owning commit. Ordinary
resource permissions, parent eligibility, version checks and safe destination/payload
requirements remain necessary. A positively qualified absence of adoption preserves
those rules; adopted denial or unavailable authority blocks publication. Withdrawal,
archive and deletion are not newly blocked by this publication restriction.

Publish affordances consume the same Tenant facts through the resource authorization
snapshot. The state change and its retention-qualified audit stay together on rollback.
For an otherwise authorized manager, a restriction is distinguished from unavailable
authority instead of both appearing as a missing resource. Unauthorized callers do
not gain visibility from this distinction; publish links remain withheld in either
case. Timed staff authority is checked again after a delayed provider response before
revealing either failure, so an expired permission cannot expose that distinction.
Thirty-three new cases remain UNRUN/unbuilt. Earlier Application compilation passed with
182 warnings and no errors; Persistence passed with 62 warnings and no errors. Native
fixture source corrections are recorded; follow-up source review found no further
defect after the expiry correction. Final Application compilation passed with 182
warnings and no errors. This is not
deployed-provider, end-to-end HAL/Cerbos or runtime assurance.
Support remains Partial.

### Scheduled publication is excluded for now

The maintainer explicitly deferred native “publish this event or session later”
on 2026-10-20. This is a scope exclusion, not an implemented scheduling feature
or a passing moderation check. Existing event dates, session timetables,
reminders, scheduled moderation reconciliation and queued delivery are unchanged.

The complete plain-English and technical explanation, both options, future
authority and recovery decisions, and acceptance criteria are preserved in the
main repository's `dev/backlog/scheduling.md`, outside the moderation worktree.
Automatic publication would require a separately approved feature plan and a
reviewed return to the activation inventory. A timer alone must not bypass
ordinary permissions, publication readiness or current moderation authority.
Inventory revision 2 removes only the dedicated scheduled-publication consumer
and its Publish/Manage obligations; all remaining consumers and exact evidence
matching remain required. No other support state is promoted by this exclusion.

### AT Protocol organization and group onboarding

Verified organization/group onboarding now coordinates new administrator membership
with the existing moderation qualification revision in the same native transaction.
It uses the verified DID's exact linked Human, not an ambient browser identity or a
general membership-management exemption. An existing membership replay does not
renew qualification, and pending tenant participation is not silently approved.
Ordinary Person login is unchanged by this membership participant.

Retained erasure, linked identity, active tenant enrollment and the represented admin
membership are checked at the actual commit, including co-located privacy storage.
A late onboarding failure rolls back the owned provisioning transaction rather than
committing a partial membership. This does not install moderation sources, activate a
policy or complete the sanction overlay.

Eleven native-store cases, including the real organization/group onboarding consumer,
are authored but unrun. They are not certification of the complete OAuth/bootstrap
flow or concurrent database enforcement; membership support remains Partial.

### Target-owned allegations

An allegation can belong to reported material without naming a responsible
person. An intake reviewer assignment is not a qualified misconduct finding.
Additional material does not reopen a closed adjudication or transfer another
person's responsibility. These source models do not enable confidential access
or certify the native intake workflow.

Operational budget accounting now has a source foundation that separates the
actual subject, action and service mandate. Exact retries count once and preserve
their original time; changing a limit does not erase supplied usage history.
It uses no network or device fingerprint and does not turn quota usage into a
disciplinary finding.

The native Human allegation-intake implementation now requires an explicitly
adopted local report budget; it has no default quota or unlimited-capacity
fallback. A current tenant or platform administrator records Human/SubmitReport
terms, a positive maximum and rolling window, a different accepting operator
and a validity deadline. That named administrator independently adopts the
exact recorded generation. Use authenticated POSTs at
`/api/moderation/operational-budgets/sources`, `/adoptions` and
`/adoptions/{adoptionId}/withdraw`; writes are limited to 32 KiB, rate-limited
and non-cacheable. Missing, expired, replaced, withdrawn or unverifiable terms
make new submissions unavailable. Ordinary reporters need no administrator role.
Requests and their nested terms reject unknown fields. Recorder/adopter identity
and administrative authority come from the server, not extra JSON properties;
a service mandate cannot be supplied as an undeclared budget field.
New report intake is rechecked after the final disclosure update. If its budget
authority expires or is lost there, the submission and its usage roll back together.
The server also withholds a newly minted receipt if its authority expires or its
qualifying relationship disappears during final disclosure processing.

Only successful, independently authorized submissions consume report capacity.
The native transaction uses the original accepted occurrence history: another
receipt, revised material or a changed quota cannot restart its clock or charge
the same occurrence twice. Failed or rolled-back submissions leave no charge.
An exact retry can return its original acknowledgement after budget withdrawal,
without recreating a report or granting target access. There is no second
private counter database or new network/device identifier.

Source/adoption administration and transaction-coupled Human report accounting
are implemented in source; migrations, generated clients and runtime/provider
assurance are still pending. Budgets for other native actions, actual service
mandate approval and first-publisher review remain unfinished. A service
credential or an administrator's budget configuration is not a service mandate.

An Event jurisdiction now has its own type under its exact tenant. It is not a
resource jurisdiction with an Event identifier: Resource continues to mean an
actual EventResource. Matching identifiers cannot transfer authority between the
two. The version 2 reference catalogue permits Event scope for classification
correction only; existing disciplinary profiles keep their previous jurisdictions.
Earlier catalogue declarations must not be treated as newly approved Event policy.

This source change preserves Event identity in administrative intent, stored
state and measurement receipts. It does not make an Event publicly disclosable.
Qualified content approval, actual audience eligibility and a current check
through calendar delivery are still required. Provider migrations, generated
contracts and end-to-end assurance remain pending.

The source now includes a reference recurrence calculation, not an automatic
disciplinary action. It counts distinct original occurrences only when final
findings match the same canonical subject, exact jurisdiction and policy rule
actually accepted before the conduct. Duplicate findings or attribution
generations cannot inflate a strike; reversal removes only the corrected finding's
contribution. Changing today's policy cannot invent yesterday's acceptance.

The proposed rolling window excludes conduct exactly 180 days old and includes
conduct through the assessment time; delayed finalization cannot refresh it.
The calculation selects warning/correction, seven-day publication review,
seven-day publication pause or reviewed thirty-day publication pause reference
profiles. It does not start or renew these intervals or change physical admission,
administration or permanent rights. Persistent evasion still needs its own
independently qualified finding, not report volume or an operator checkbox.

The actual historical acceptance writer, complete current-history qualification
and native progression command remain unfinished. The 28 new calculation cases
are authored but UNRUN/unbuilt; a successful production-only compilation is not
runtime assurance or permission to activate classification discipline.

Classification correction now has separate, non-punitive administrative source
controls. `POST /api/moderation/classification-controls/selections` retains the
complete CFG29 control from an independently accepted whole-Event material
review. `POST .../adoptions` requires another independent administrator to adopt
each exact effect and base/additive consequence binding. The request names the
typed Event and exact material, review and generation coordinates; a tenant,
resource or different Event cannot stand in for it.

The selection author cannot be the material owner, reviewer or either controller.
The adopter must also be distinct from that author. The server rereads current
material, inventory, roles, receipt provenance and deadlines through the
transaction's final commit, including retries. The validity deadline limits
receipt freshness; it does not invent a disciplinary sanction or ban duration.
These controls alone do not make private or age-restricted content public.

`POST .../adoptions/{adoptionId}/revoke` ends the route-bound adoption with
accountable grounds. Correction remains possible after review withdrawal,
expiry or native Event deletion, using retained original provenance rather than
renewing old approval. These authenticated writes are non-cacheable,
write-rate-limited and bounded to 1 MiB. Migration, generated-client and
end-to-end assurance are still queued with the rest of the programme.

The public `GET /api/Event/{id}/calendar` source now prepares the ICS file under
a single-use native disclosure lease instead of returning an unowned export DTO.
Public location disclosure remains anonymous even when the requester is signed
in. The intended public acquisition requires complete, current, unrestricted
whole-Event classification; administrator provenance does not give the visitor
an age exception or permission to disclose restricted material.

The server privately prepares bytes, rechecks the original source and deadline
before output, and retains custody until writing and response completion settle.
Buffered response completion remains under the same lease. Native deadline
cancellation aborts the transport; it does not release custody while writing or completion is
still outstanding or send a second payload after a calendar response starts.
The route is non-cacheable. Missing Events return 404, and unavailable current
qualification returns 503 before output. This boundary does not recall bytes
already sent or prove remote calendar deletion.

The separate anonymous native verifier is implemented, with a deadline bounded
by current sources and at most 30 seconds from acquisition serialization.
Production publication-evidence ownership is still missing, so this is not yet
a genuine qualified public activation path. Positive native acquisition coverage,
provider/runtime and actual transport-cutoff assurance remain open; existing
fixture declarations are not production evidence. Guest registration offers the
calendar link only after native availability, not because an export is pending
or the requester has an administrator claim. This source update does not certify
deployment readiness. The retention warning still applies:
calendar providers can keep previously imported details after access changes.

Expired confidential case narrative and Pre-Finding material disposal does not
wait for the maintenance credential. The scheduled tenant-scoped pass removes
only expired or withdrawn custody, retaining original provenance and active
material. A credential outage still prevents authenticated effect/review work,
but must not prolong private payload retention. Runtime assurance for this
ordering change remains queued with the programme.

An unchanged retained-store counter does not prove that erasure replay input is
intact. The internal SQLite recovery reader now checks the actual committed range
above its retained floor, under the original owner gate, without reading private
facts. That check is not evidence that a backup was restored or reconciliation
completed, and does not enable moderation activation. Artifact custody,
canonical/recognition recovery integration and runtime assurance remain open.

A current-policy read cannot report success if its original request or consuming
operation is canceled as delivery finishes. Native read fences are released and
the request frame is restored on cancellation. This cannot retract bytes already
sent. Current-policy and quarantine metadata responses now keep native read
protection through buffered response completion. Cancellation aborts transport
without releasing protection while completion is still outstanding; a rejected
quarantine response cannot append a second error payload after output begins.
Two regressions now use the original native authoring database and actual HTTP
result to check buffered completion and cancellation; they do not activate a
policy. Their compilation and execution remain deferred with the programme.

The shared moderation engine records decisions and minimized change intents in
the local primary transaction. Retries preserve the accepted result instead of
creating another restriction. Permission must remain current through that
transaction, and an unavailable authority is not an unrestricted result.
Private evidence belongs to separate custody, not change intents or telemetry.

The public `GET /api/moderation/reference-catalogue` supplies the same complete
reference vocabulary for Simple and Advanced administration: allowed responses,
scopes, subjects, prerequisites, recognition terms and consequence recipes.
Reference definitions are not your adopted policy or a report of current
installed support. Its links permit only reading and refreshing the catalogue.
The catalogue contains no private case or account data.

Policy authors can submit complete intent to authenticated
`POST /api/moderation/policies/{policyId}/publications`, or reserve an exact
future active intent through `POST .../publication-preparations`. Both require
the original operation identities and exact expected native revisions; the
route policy ID overrides a body selector. The server still independently
checks administrative authority and complete native prerequisites.
Preparation supplies a candidate identity and finite deadline, not activation.
A publication response describes publication-time state; it is not permission
to bypass current enforcement or proof of deployment readiness. Requests are
non-cacheable, write-rate-limited and bounded at 4 MiB. These source routes and
their thirty-four authored cases still require final hosted, provider and runtime
assurance. Product API and generated Client compilation passed, including typed
reference fields, all thirteen recipe dimensions and named wire values;
guided policy administration remains unfinished.

A read-only reference preview component now preserves the same prerequisite,
response, safeguards and complete base/additive recipe information in Simple and Advanced
presentation. Neither mode hides the safeguards against automatic historical
restoration or the limit that disclosed copies cannot be recalled.
Advanced adds technical coordinates; changing display mode does
not change policy meaning or supply an action. The guided page now composes
these previews; generated contract integration and visual assurance remain
programme requirements.

`GET /api/moderation/policies/current?scope=tenant|instance` provides current
authoring coordinates for your actual request tenant or installed instance.
The server independently requires native authoring authority at delivery, even
though the GET endpoint accepts anonymous requests. Its latest adopted state
includes disabled definitions and all base/additive plans; an older active
policy or prepared candidate does not replace that authoring head.

Use the returned current expected revisions for the next write, not the retained
historical revision or inventory coordinates. An installed empty slot has an
unreserved proposed policy identity and revision zero; reading it creates
neither policy nor installation. Missing installation is unavailable. Only
server-qualified HAL links offer draft publication or candidate preparation.
These links do not activate policy or prove operational support, and write
handlers independently recheck authority and revisions. The read is not cached
and suppresses referrers; identity, authority or selection changes before final
output discard the privately prepared response. Forty authored boundary and
schema cases remain unrun pending programme assurance. Native API export and
generated Client compilation passed with typed coordinates and nullable empty-slot
authoring; guided administration and actual hosted/provider behavior still need
that assurance.

For an empty slot, repeat its UUIDv7 creation intent as optional
`prospectivePolicyId` when reacquiring current authoring coordinates. The server
echoes it only while that slot is still empty; an adopted policy always supplies
its own current identity instead. This is not a reservation, installation,
tenant selector or permission. Follow the freshly returned HAL write link, and
do not silently rebase local edits if the current policy or revision changed.

The Client transport now preserves complete typed authoring intent and uses
only server-provided draft/preparation links. It distinguishes sign-in, forbidden,
stale/conflicting, unavailable and uncertain outcomes. An uncertain write can
be deliberately retried with its exact original operation and payload; it does
not silently become a new publication or active policy. A candidate remains
preparation only.

The retained-policy preview includes unselected profiles and complete separate
base/additive plans alongside their safeguards. Historical inventory/revisions
and current next-write coordinates are visibly distinct; switching presentation
does not change meaning or infer support.

The guided page at `/moderation/policies` now stages complete class, reason and
profile revisions while keeping every original definition, including unselected
ones. Inventory alignment, supported-profile declarations and native modes are
deliberate choices; Advanced is only presentation. Reference recipes guide
response grade, timing, approval count, calendar-month contestation, scope/subject
coverage and recognition without claiming deployment support.

Draft publication and candidate preparation are separate server-linked actions.
If the displayed head became stale, nothing is posted and local work remains.
Explicit receipt recovery reuses the exact original operation; a separate latest
observation replaces the editing baseline only after confirmation. Candidate
success never means adoption or activation. The authored controls, browser
behavior, accessibility and 29 editor cases remain unverified until final
programme assurance.

 Cancelling a local class, reason or profile edit returns keyboard focus to the
 matching section's Add button without publishing or removing retained policy
 definitions. These three additional authored component checks remain unrun;
 keyboard and assistive-technology behavior still need final assurance.

### Inspecting moderation configuration imports

The import preview shows each mapped moderation destination separately: current
revision, whether a baseline exists, definition additions/changes/removals,
conflicts and compiler diagnostics. An initial import lists its added
definitions rather than presenting an empty comparison. Reserved native IDs and
authority bindings are not exported in these details.

These are private, current-authority results. Before sending the prepared
response, the server rechecks your original session, artifact, access token,
expiry, selected mappings and native authority while retaining the participating
writer fences through delivery. An expired or changed preview is not silently
rebased or partially applied; refresh it explicitly. A response interrupted
during delivery must not be treated as a completed preview.

Cancellation and expiry cleanup use the same native session fence as disclosure.
The session deadline cancels a blocked response write and remains active until
response completion; completion follows the native commit. An expiry or failure
after writing begins aborts the response rather than returning a replacement
success or retrying the body. Bytes already received cannot be recalled.

The read-only destination display does not grant permission. Apply remains
available only through the server-provided HAL action for the complete ready
preview. Import still creates draft definitions only, never deployment
activation. Authored native expiry/one-use checks include blocked body writing
and blocked post-commit completion; another check cancels the actual session before
disclosure and requires zero preparation or delivery. These, schema and generated-client/rendered
checks remain unrun; browser, accessibility and provider/concurrency assurance
are deferred.

### Reviewer independence

Reviewers, their proposed controllers and the known person under review cannot
certify that controller's identity. A native administrator proposing themself as
controller is denied as an authority conflict, including when replacing a
previously independent qualification. The prior qualification and conflict
assessment remain unchanged; administrator status does not supply independence.
The authored regression cases still require programme runtime assurance.

Decision workflows now have authenticated JSON POST routes under
`/api/moderation/decisions/{decisionId}` for
`/proposal`, `/findings/{findingId}/acceptance`, `/approval`, `/rejection`, `/withdrawal`
and `/activation`. Supply the original UUIDv7 operation/effect identities,
expected decision/policy revisions, canonical subject and complete target
jurisdiction. Route IDs own the selected decision and finding. The server still
checks current native ownership, independent reviewer authority and exact
original meaning; JSON cannot supply an approval or a permission grant.
Activation remains unavailable without all genuine current prerequisites and
uses the existing interval rather than extending it. These routes do not
claim that production activation or consequences are ready.
Responses contain only authorized decision/contribution IDs and revisions,
never private evidence. Each request is bounded at 16 KiB, non-cacheable and
write-rate-limited; native operation receipts govern retries. The 32 new API
cases are unrun, and generated clients, hosted authorization and provider
assurance remain required before deployment.

For `/proposal`, the expected revision identifies the current open case.
Supply the case ID, original rule owner/code/revision, restrictive capabilities,
explicit start/end/review times and required approval count. The server retains
these terms through its domain factories and then independently qualifies the
actual proposer and adopted policy. Missing or contradictory intervals, invalid
quorum, protected-remedy restrictions and duplicate JSON terms are rejected;
no default term supplies consent or authority. Later actions use the decision
revision. Proposal creates neither an accepted finding nor an active restriction.
Twenty-five new wire cases and six proposal boundary cases remain unrun.

Moderation intent documentation now describes the same fields on both native
OpenAPI and the current Swagger baseline, including complete parent ancestry,
subject origin, original rule and proposal terms. It does not expose computed
coordinates as writable facts or grant authority. Client generation and twelve
hosted documentation-contract cases remain pending programme assurance.
The native exported contract has been generated and inspected for these four
shapes and proposal-request references. This does not certify hosted Swagger
parity or native authority enforcement.

Corrections now have authenticated POST routes
`/api/moderation/decisions/{decisionId}/sanctions/{sanctionId}/revocation` and
`/api/moderation/decisions/{decisionId}/sanctions/{supersededSanctionId}/supersession`.
For supersession, `decisionId` selects the existing approved replacement; the
body names its new sanction identity, the expected original sanction revision
and both complete scopes. Route selectors override conflicting body IDs.
The server independently checks current correction authority and exact historical
ownership; the route grants neither permission nor wider reach.
Successful correction neither reopens another case nor recreates consumed rights.

Correction notices must retain the original recipients, but each person must still
have current permission to read the case. A former reviewer who has lost that
permission is recorded as unavailable/undelivered; a new reviewer cannot take their
place or inherit their delivery obligation. Receiving the earlier notice does not
restore access, extend retention or grant a new correction-only permission. This
is the maintainer's selected access rule; original-recipient notification and
completion accounting remain under implementation, not certified as operational.

The current implementation increment preserves the original recipients as pending
correction obligations. It does not send a correction, make its text readable,
reuse an old acknowledgment or report delivery complete. Every pending occurrence
is unprepared/unavailable until current permission and independent disclosure
approval are positively established. A replacement reviewer cannot take its place.
These records retain the original notice's deadline, not a new retention period.
Cleanup now follows the original sanction relationship and removes pending records
with the original disclosure approval. Changed deadlines or recipient anchors
cannot hide a dependency, and unrelated notices sharing that deadline do not count
against its discovery limit. Missing or expired original records cannot be rebuilt
from today's membership; expired private custody is not read to recover them.
Replay must prove every pending recipient, rather than accept a partial write.
Historical instance-scoped records do not lose their original writer relationship
merely because their source tenant projection is null. A previously expired,
independently approved correction notice must have exact cleanup proof and no
remaining copies before it can be omitted from later original-notice cleanup.

The existing native notification cleanup has a separately identified limitation:
a retained native intent makes cleanup unavailable and rolls back the purge. This
increment does not fix or certify removal of email/portal notification graphs.
145 contract, source/writer and cleanup cases remain **UNRUN/unbuilt**. Domain and
Application compilation passed; final Persistence compilation passed with 62
warnings and no errors. Bounded source follow-ups found no further concrete defect,
not operational or provider assurance. Correction
delivery still needs positive current permission and independent text approval.
Native export shows both typed operations; sixteen new transport cases and hosted/
provider authorization remain unrun, with HAL/client integration still required.

#### Explicit adjudicated-case read permission

A current administrator of the case's custody tenant may issue a separate,
finite, revocable read-purpose grant to a named person. Being the subject, a
reviewer or an organizer is not an automatic exception. The new purpose does not
reuse an allegation receipt or change existing original-notice/status rights.

Access remains tied to the exact case revision, current recipient and original
issuer authority, retained privacy boundaries and the authenticated original
notice's retention ceiling. Replacing an administrator's role does not transfer
an old grant to the replacement role. Expiry or revocation cannot be bypassed by
replaying an earlier request. Original-purpose cleanup and Human erasure remove
the associated permission metadata.

The case's custody tenant can remain responsible when the adjudicated decision
concerns a Resource directly within that tenant. This does not authorize a
different tenant or resource. The original notice's precise retention deadline
is preserved, not rounded into a longer period.

Two additional unrun source cases cover an ordinary custody-tenant administrator
and subsequent loss of that administrator's role or membership. The historical
grant remains recorded, but it must no longer authorize current access or replay.

The grant/revoke routes return identifiers only, not case bodies or delivery
tokens. Independently approved correction text and delivery still need their
own qualification. The settlement repair keeps the originally qualified recipient
and deadline fixed before saving and rejects additional tracked grants, including
changes introduced between the enrollment owner's saves. The original
qualification record must also remain unchanged; the grant operation cannot
advance or replace it. Earlier regression cases compiled remotely but failed
before the settlement probes were reached. The new qualification mutation and
replacement cases remain **UNRUN/unbuilt**; this is not provider/runtime
certification.
Source integration is in progress: Domain and Application
compiled, and the custody-scope correction compiled in Persistence with 62 warnings
and no errors. The subsequent construction-time expiry correction also compiled
with 62 warnings and no errors; bounded source review found no additional
construction defect. API and generated browser-client compilation also passed,
with 2 and 230 warnings respectively and no errors. Generated Release schema,
client and policy captures matched. All four provider migration families have
been generated, not applied; static review found no grant-table defect, not a
working-provider certificate. All four generated migration assemblies compiled,
and bounded upgrade/downgrade SQL was emitted and inspected without connecting to
or updating a database. The separate SQL Server evidence-FK rollback discrepancy
is corrected at the generator, without rewriting historical migrations or the
approved private-PII cascade. These artifacts are not a safe-downgrade certificate. The 189 regression cases
are **UNRUN/unbuilt**; no database has been updated or operational permission flow
certified.

#### Approval of correction text is not permission to deliver it

A separate source implementation now allows a current custody administrator to
approve new text for a pending revocation correction. It checks the authentic
original notice, unchanged original audience and original policy meaning. The
author cannot substitute new recipients or extend the original retention period.
Missing original custody or an unqualified original policy makes approval
unavailable rather than inventing replacement authority.

Approval alone does not reveal a case, send a message or mark a correction
delivered. Each original recipient still needs current case-reading permission,
and actual disclosure and delivery need their own checks. The pending notice
records remain unavailable/unprepared. The original deadline is checked before
private-text reads, including after lock waits. Approval text is disposed of with
the original notice purpose even if its own validity expired or it was revoked;
that purge does not mark a correction delivered or its effect complete.

One hundred and one cases are authored but **UNRUN/unbuilt**, covering validation,
native approval, private-read expiry, cleanup and attributed-case proof. Application compilation passed with
182 warnings and no errors; the initial combined Persistence build passed with
62 warnings and no errors. Cleanup review then corrected Instance-custody handling
and rejected rewritten correction approvals on original-only cleanup pages.
The proof also retains a Tenant-custody case's existing attributed Resource
decision only when the Resource belongs directly to that Tenant. The additional
case checks this proof, not completed delivery or cleanup paging. Narrow source
review corrected a test import and found no further production defect. Final
Persistence compilation passed with 62 warnings and no errors in 2:40.31. These
are source checks, not operational assurance.

#### Transient preparation is self-only, not delivery

The native read owner now joins an original recipient's own current case grant
with the independently approved correction text. A replacement reviewer cannot
take an original recipient's place, even with a valid grant. Administrators and
machines gain no on-behalf delivery permission.

This internal operation returns no text and creates no notice, outgoing message,
acknowledgement or completion. It repeats permission, source, approval and deadline
checks through the actual primary/privacy commit boundary; its transient success
must not be reused as a later disclosure token. Original retention is unchanged.
The earliest known deadline also bounds repeated source qualification, so an
expired approval cannot authorize another private custody read while a longer
case grant remains live. Sixteen preparation and eighteen borrowed-read source
cases remain UNRUN/unbuilt. Initial compilation found a missing namespace import,
now corrected. Persistence compilation passed with 62 warnings and no errors;
bounded follow-up source review found no further material defect. Neither result
establishes runtime, provider or delivery assurance.

#### Internal correction output is not notification delivery

The native reader now has source logic for a single-use, self-only correction
explanation callback. It checks the original named recipient's current case grant
and independently approved text again; an earlier successful preparation does
not authorize a later read. Only the approved explanation fields are passed to
the callback, while the original finite custody and permission limits remain in
force. The borrowed byte buffer is cleared when the operation exits.

Output is never automatically retried. If a consumer or final authority check
fails after output starts, that failure propagates rather than pretending the
already-consumed bytes were recalled. This internal step adds no HTTP route,
email/portal dispatch, administrator-on-behalf access or delivered/completed
status. Twenty-one new native cases are authored but UNRUN/unbuilt. A single-attempt
EF boundary preserves explicit transaction support when provider retries are
enabled, without replaying output; retrying outer EF operations are rejected.
Initial compilation passed with 62 warnings and no errors. Fixture review exposed
the transaction/strategy incompatibility; focused review of its source correction
found no additional material defect. Corrected product compilation passed with
62 warnings and no errors, not deployed-provider assurance. Cancellation
under co-located privacy ownership has an additional authored, unrun case.
Checks bracket the callback rather than forcibly interrupting it at expiry.
These are not HTTP/email, all-recipient delivery or deployed-provider assurances.
That internal increment did not add a public transport. The maintainer subsequently
approved a separate self-only JSON route:
`GET /api/moderation/notice-corrections/{originalNoticeId}`. Its explicit pending
effect/hash, approval, named-grant, case and case-revision coordinates are not
permission. The caller must still be the original recipient with current case
permission and independently approved correction text at actual response writing.
No current reviewer may substitute for an original recipient.

The response contains only Rule, Reason, Facts, RemainingActions, Recourse,
Automation and Retention. Writing and response completion stay inside the native
custody operation. Caching and ETag buffering are disabled; cancellation or a
post-start failure aborts the response without retrying or inventing a replacement
successful result. Bytes already consumed cannot be recalled. Invalid requests
are 400, missing authentication 401, and unavailable disclosure 404. No automatic
discovery link, portal/email notification, administrator-on-behalf access,
acknowledgement or delivery/effect-completion status is added. The original
notice response and acknowledgement remain separate.

Twenty-seven query, native HTTP and caching-boundary cases are authored but
UNRUN/unbuilt. Application and Persistence compilation passed with 182 and 62
warnings respectively and no errors. Focused product review found no further
causal defect; a test's privacy-gate observation was corrected to run inside actual
disclosure. API and generated-client compilation also passed with 2 and 230
warnings respectively and no errors. The generated success schema contains exactly
the seven approved explanation fields, and all three Release contract captures
match their sources. Fixture integration remains in progress; these checks are
not deployed endpoint or provider assurance.

A proposal must refer to the current open case. A correction replaces only the
named contribution, requires authority over its old and new scopes, and cannot
widen the restriction's reach. Scheduled state is reconciled from the current
time, but an expired restriction stops applying at its exact end even before a
maintenance sweep. A review deadline alone does not expire a restriction.

Scheduled discovery preserves the subject's complete original jurisdiction,
including its distinct Event or Resource identity, when preparing a recovery
request. A decision naming a different Event cannot qualify that contribution.
Discovery supplies only a candidate; the native operation must still establish
current maintenance authority and all activation or expiry prerequisites.

The configured maintenance sweep can also recover unfinished effect intents
whose wake-up is missing or already completed. It rechecks their original
cause and current maintenance permission; it does not recreate restrictions
or bypass missing consequence owners. Existing outbox retries and operator
dead letters keep their own workflow. A failed leading effect cannot prevent
later effects from being examined.

If originally inert work fails while completing, the dispatcher now attempts
to release its exact claim so the normal retry need not wait for lease expiry.
This never acknowledges failed work or releases a successor's claim; current
maintenance permission and exact native lease/revision checks still apply.
Cancelled or unavailable authority remains subject to normal recovery. Two
native failure/retry cases are authored but remain unrun.
Two further native cases cover a failure after the recovery pass has actually
claimed work: later work in the same page can complete, and cursor wrap can retry
the released intent without waiting for expiry. They remain unrun.

Completing originally inert work now finishes pending disclosure updates before
rechecking its exact lease deadline and current maintenance credential. If either
is lost during that finalization, completion and disclosure changes must roll
back while retaining the original lease identity and deadline. Three native
cases specify valid completion and both late losses; they remain unrun.

An instance-wide decision still has one original writer tenant. Its recovery,
read and lease operations stay with that tenant; a maintenance credential in
another tenant does not acquire the work merely because its decision covers
the instance. Missing original ownership cannot be replaced by a cleanup marker.

The separate effect-maintenance acquisition now requires its exact live lease,
original cause and writer tenant on the primary transaction. It does not make
the maintenance service a human adjudicator or approve a resource consequence.
Actual consequence execution and release still require their native owners.

An expired original explanation is not opened while checking that permission.
Lease ownership and the original tenant/deadline metadata are checked before
reading its payload, including when cleanup has not yet run.

## Implementation and activation

The current programme is building the authoritative ledger and native
integration. Installing its schema does not enable a policy, replace existing
actor protection or certify a deployment as protected.

The shared recognition contribution model now separates action continuity from
admission/enrollment purposes and preserves the original sanction's jurisdiction,
powers and deadline. It rejects expired, reversed, future or unrelated
contributions; keeping a minimal record longer cannot prolong enforcement.
This foundation retains no provider identifier, contact, DOB, profile, old role
or key material. The native capture ordering below now persists complete computed
material before erasure; identity matching, admission/continuity consumption,
deployment adoption and final assurance remain required.

The native token computation primitive now uses a versioned, length-prefixed
HMAC-SHA-256 format with separate action/enrollment purposes, trust domain and
key ID/generation. It preserves the trusted provider-account identity exactly
and rejects malformed Unicode rather than inventing a replacement identity.
Only minimized digest coordinates are returned; ephemeral key material is
cleared on disposal. Keys still require the selected secret authority. The
primitive alone neither configures the ring nor grants matching permission.

Computed capture material now pairs an original purpose-bound token with its
material identity and exact final contribution. Wrong trust/purpose, changed
material, reversed restrictions and expired intervals cannot preserve a match.
The value alone is not the durable receipt or settled erasure; the native
coordinator independently verifies the persisted complete batch before purge.

The computed batch also fixes the original erasure operation, writing ring and
finite settlement deadline. Empty, duplicate, mixed-key/material/trust or
differently timed items reject instead of being partially accepted. A batch in
memory is not the committed receipt: the native owner must establish complete
inventory coverage and independently reread its durable capture.

Qualifying erasure now records a received request and then commits the complete
purpose-limited digest material before appending the retained erasure fact or
destroying provider identifiers. Current final restrictions, original privacy
adoption and key identity are rechecked under the shared identity/subject fences.
Recovery after destruction uses that persisted material and current restrictions;
it does not restore old profiles or require the destroyed identifiers.

When a matched replacement account is later erased, completed continuity
references can be captured for its newly verified external bindings. The original
decision subject, scope, capabilities, end, review and retention ceiling remain
unchanged. Exact parent receipt/binding provenance is retained; missing provenance
closes that contribution and reversal or expiry releases only its dependent chain.
An unresolved matching candidate or new Local credential is not such a reference.
This does not restore a profile, move a sanction to another person or renew it.

Recognition key rotation uses the existing selected secret authority. Native
metadata holds commitments and original backup obligations, not secret bytes.
Stage/byte acknowledgement/write switch/retirement re-observe that selected
source at the native commit; erased identifiers cannot be rehashed to rotate.
Minimum SQLite runtime ownership uses the actual database file on Unix/Windows,
not a host ID or sidecar path. Stop all processes before restore. Remote and other
provider topologies need their genuine owners; no replica readiness is inferred.

Cleanup removes only whole expired matching sets without surviving dependent
references. A finite proof requires the authentic erasure fact and original
obligations; a summary marker is insufficient. Its original receipt ceiling is
fixed once. Expired linked receipts/obligations are swept before authority-dependent
recovery, while unlinkable key backup horizons remain. These mechanisms still
require generated-schema/provider/runtime assurance.

Ordinary erasure needs no recognition key. A qualifying request whose key or
store is unavailable stays received but unsettled with its original finite
deadline and owned escalation/lawful-disposition status. No missing capture is
reported as completed erasure, and no failure extends the original sanction.
Complete computed material without authority settlement still reaches the same
original escalation and lawful-disposition deadlines. Retries do not restart that
clock or clear an outstanding obligation; a settled receipt cannot revert to an
unsettled request. Receipt status shows an elapsed obligation without waiting for
a recovery run; that status is not a claim that an owner has been notified or the
required disposition has been completed.

Qualified legal erasure has a separate native decision and receipt path. It does
not claim that missing matching material was computed or settled. The original
request revision, deadline and every contributing source's independent owner
coverage remain bound to the decision; a single owner cannot release another
source's obligations. Append and purge verify the authentic retained erasure fact
through this separate path. Accepted-owner receipts and the real authority
high-water bind recovery to the exact committed fact and the authorization valid
at its recorded time. Later withdrawal/expiry does not require restoring identifiers
or keys to consume that fact. Runtime recovery tests remain unrun.

The original request receipt can authenticate only a separately bound protected
legal correction route after the shorter status receipt expires. Ordinary account
and erasure-status credentials are not extended. Correction has its own finite
calendar protection and does not disappear because the original source is later
withdrawn. These source changes do not certify completed erasure, operational
notice delivery or provider/runtime readiness.
Scheduled erasure replay also expires matching eligibility in bounded batches,
including when no received requests are waiting. It uses the original restriction
end or matching-retention ceiling and needs neither provider identifiers nor keys.
Receipt evidence remains intact; this does not yet delete retained records or
certify backup, replica or key-retirement safety.
Operational delivery of those dispositions, enrollment/linking/Local provisioning
gates, readable-key rotation and replica/backup obligations remain open. Capture
alone does not certify protected admission, completed continuity or deployment
readiness; schema and final programme assurance are still required.

Native matching now has purpose-qualified digest and original-authority checks,
plus minimal references that attach surviving action restrictions to a fresh
subject without moving the original sanctions. Current reads need no destroyed
identifiers or keys and independently check those references again. A review date
does not silently end a restriction that the canonical decision still keeps active.
Resource terminal evaluation now also rejects an expired continuity lifetime at
the actual header or mutation checkpoint; a changed lifetime invalidates private
preparation even when its generation remains the same.
The resource-read consumer now applies those references only to their original
capability and target, carrying their lifetime into the existing terminal checks.
An applicable authority failure closes its target; an unrelated restriction
does not block or shorten another resource's access. Existing native audience
and permission checks remain necessary.
Enrollment and Local/linking order, later erasure of a replacement subject, restore
closure and the remaining assurance gates must be completed before protection
can be claimed.

Action-only matching outages must not become account-wide bans or silently
unrestricted access. Minimized recovery references now distinguish an unresolved
protected capability from a known match: they pin the current native login and
original scope/end/retention, without copying identifiers or private history.
Affected resource reads remain unavailable until actual matching or a lawful
disposition resolves that uncertainty; unrelated resources are not closed by it.
Committed references also retain an immutable material commitment. Changed stored
scope/purpose cannot silently make an originally protected resource unrelated or
settle uncertainty; unknown applicability closes the snapshot. Ordinary source/
receipt outages with intact material remain scoped to applicable resources.
Scheduled erasure replay now retries only the original pinned live login and
action purposes; it does not impose replacement-admission rules on an existing
account. Missing bindings or unreadable authority keep the original obligation,
and the original sanction end/retention ceiling cannot be renewed by retrying.
Scheduled owner delivery and qualified disposition source are integrated. Delivery
marks require the actual required in-app notification graph, and disabled routing
leaves the committed obligation pending for retry. Source-scoped operator
assessment/list/read/acknowledgement endpoints are implemented; runtime
verification and generated client/schema assurance remain pending.

The named recipient starts with
`GET /api/moderation/recognition-recovery/escalations`, reads an exact notice, then
reads `/escalations/{id}/context` for the current source and request revisions.
Follow only the returned HAL actions. The recovery owner submits an assessment
under `/requests/{intentId}/sources/{sourceId}/assessments`; the independently
qualified privacy owner authorizes it under `/assessments/{id}/authorize`.
Acknowledging `/escalations/{id}/acknowledge` confirms an actual delivered notice,
not completed erasure or permission to settle another source. Context does not
expose other owners' private material, and an old notice is not current authority.

The original receipt has a separate correction-only route:
`GET /api/privacy-erasure/recognition-corrections` with
`Authorization: ErasureCorrectionReceipt <original-receipt>`. Send the credential
only in that header, not a URL or request body. Its sanitized response omits stored
proof digests; the credential cannot access an account, ordinary erasure status,
or owner writes. Its finite correction lifetime remains independent of status
expiry. These source mechanisms still await programme runtime assurance.

Original notice capture now joins approved safe text with independently current
Event/resource target and reviewer-policy authority in the committing lifecycle
transaction. A text approval alone grants no disclosure permission. Missing,
replaced, unsupported or expired target coverage rejects the whole selection;
capture does not certify consequence execution or completed notice delivery.
The source wiring still requires programme runtime assurance.

The protected notice portal uses `GET /api/moderation/notices` and
`GET /api/moderation/notices/{id}`. It shows only the current subject's original
approved explanation or a currently qualified reviewer's notice. Reads and mail
scanning do not acknowledge delivery. An explicit
`POST /api/moderation/notices/{id}/acknowledge` acknowledges receipt, not agreement
with the decision, using the exact notice hash and an operation identity.

For an unsafe resource whose Content subject is its owning Event, the original
safe disclosure explicitly selects qualified Event owners or managers as human
representatives. Their exact assignments and versions are captured with the
original notice. An administrator or reviewer is not automatically a
representative, and a replacement manager does not inherit confidential history.
The representative must still qualify when opening, acknowledging or receiving
optional pointer mail. Revocation, expiry, changed assignment or erasure closes
that access. Acknowledgement records the actual representative separately from
the Event subject. Original approval and capture also serialize with the
selected retained erasure authority through their actual primary commit, so
an erased representative cannot qualify merely because the primary erasure
record has not appeared yet. Deployment composition determines which authority
owns that transaction; the writer must not create a nested transaction.
Final checks and rollback preserve the original calendar and discard partial
approval or capture when custody fails.

Acknowledgement does not accuse the human, release quarantine, prove every
recipe step or renew retention. Original notification status also requires this
custody; it cannot be reconstructed after that attribution is purged.
Provider migrations, generated client parity and programme runtime assurance
remain pending before these source contracts are ready for deployment.

The first qualified acknowledgement fixes the notification timestamp and the
original profile's calendar remedy floor; retries never restart it. Its privacy
retention ceiling is independently approved and remains fixed. The full original
notice is available before acknowledgement only when enough calendar time still
fits inside that ceiling. After acknowledgement, qualified access lasts only until
the ceiling. Sanction expiry/reversal does not prematurely remove an otherwise
valid explanation. Late or missing acknowledgement remains unproven notification,
not expired complaint eligibility. A pointer, read or delivery preference cannot
prove notification or extend retention. A separate protected status route uses
the original decision/effect coordinates:
`GET /api/moderation/decisions/{decisionId}/effects/{effectId}/notification-status`.
Coordinates alone grant no access; the current canonical subject must still
qualify independently. A full-text subject notice advertises this route in HAL.

Acknowledgement now finishes pending disclosure invalidation before its final
recipient and calendar checks, including when privacy authority uses a separate
store. If the recipient is deleted or the original calendar floor no longer fits
while that finalization runs, the acknowledgement and disclosure changes must
roll back together. This cannot start a new retention period or complete pending
moderation consequences. Six Human/Content representative cases specify valid
completion and these losses; they remain unrun under the programme-wide deferral.

Status distinguishes actual proven acknowledgement from unproven, unavailable or
purpose-purged proof. It shows actual receipt time and the original calendar floor
only when that proof is available. Purged proof cannot be reconstructed, and null
facts do not mean “never notified” or “complaint expired.” The route exposes no
private original text and remains independent of full-text revocation or a late
notification window. It does not invent a complaint/support action. Deleted
subjects still need a separately approved purpose-bound status owner; existing
recognition-correction credentials do not grant ordinary account access.

Native cleanup source now uses the configured maintenance identity to remove the
original explanation and its governed pointer/acknowledgement copies at the fixed
privacy ceiling. The separate case/decision ledger remains. A minimal marker can
prove that an original write was lawfully cleaned up; it cannot prove notification
or that pending consequences completed. Bounded scheduled scans do not extend
deadlines, and an uncertain commit cannot advance their cursor. The actual native
pointer graph now has a dedicated optional account-moderation SMTP sibling.
It contains only an original protected notice reference, not the explanation,
finding or recipient display name. After connecting, its native final-use owner
checks original purpose, current recipient/reviewer qualification, retained
erasure, membership, preferences, governance and verified email address before
sending. Expired or withdrawn authority settles as Skipped without automatic
retry; queued work cannot borrow a new recipient or revive an erased explanation.
SMTP acceptance never acknowledges the subject, starts a notification calendar
or extends the original privacy ceiling. Cleanup removes this governed local
email graph and rejects substituted external work; it cannot recall a message
already accepted by an external mail server. Source compilation and final
generated-schema/provider assurance remain separate gates.

Six additional authored SMTP checks change delivery authority after connection
but before any sender/recipient envelope. They require no message after loss of
recipient, verified email, preference, disclosure or source authority. A newly
verified address must receive the freshly qualified pointer, not the old queued
address or explanation. These checks have not run; delivery assurance remains open.

Native caller source now distinguishes exact-bound sign-in from replacement
provisioning and checks the exact live binding at the owning commit boundary.
Local credential, lifecycle and delivery owners join the same ordering before
their transactions. Nested visitor/SMTP and recognition setting leases preserve
outer ownership rather than reacquiring a held lock. These source changes do not
replace the still-deferred native runtime and provider assurance.

Event-rights governance now has native record, independent-adoption and terminal
withdrawal operations. A source fixes the exact publication/profile/mode and
reviewed continuity, premises, companion-rights and settlement obligations.
The independent rights reviewer adopts a finite lease; withdrawal preserves
original provenance even after expiry, replacement or an original owner's
departure. Current source, human/controller authority and original receipts are
rechecked rather than trusting an administrative label.

These are accountable mapping mandates, not proof that a particular event has a
willing permitted steward or that a merchant can settle a particular participant's
rights. Actual host/premises/finance permissions, consenting handoff and native
settlement remain to be integrated. Nonurgent unavailable mappings cannot be
approved; necessary urgent protection requires separately owned pending
settlement. Adoption does not activate a policy, delete/postpone an event, issue
a refund or restore expired rights.

The optional server recognition ring now has an instance-only setting:
`moderation.recognition_keyring`. Supply `MODERATION_RECOGNITION_KEYRING` through
the explicitly selected environment/shared User Secrets authority, or Infisical
`/api/MODERATION_RECOGNITION_KEYRING`. To use the shared local store, explicitly
select `SECRET_PROVIDER=UserSecrets` in Development/Testing; it is never fallback.
Tenant-specific recognition-ring bindings are rejected. Leave this capability
unavailable when the secret is absent; platform bootstrap and ordinary erasure
do not require it. Never commit key material.

The value is strict version-1 JSON: ring fields `version`, `trustDomainId`,
`ringGeneration`, `issuedAt`, `validUntil`, `keys`; key fields `kid`, `generation`,
`k`, `status`. Trust must be the exact canonical lowercase UUIDv7, timestamps
UTC round-trip `O`, generations positive, and material 32 bytes encoded as
canonical unpadded base64url. Keep 1–16 unique key IDs/generations, exactly one
`active` writing key and any other keys `readable`. Unknown or duplicate fields,
foreign trust, malformed material, ambiguous writers or expired values reject
the whole ring. Acquisitions last at most five minutes and cannot outlive the
source's deadline.

Retain original readable generations for matching their original tokens.
Different material under the same identity/generation is divergence; never
silently replace a missing key or promise to rehash erased identifiers.
Resolution and the overlap-rollout policy do not prove replica convergence,
backup recovery or safe retirement. Native durable acknowledgement/backup
floors and catastrophic recovery remain to be completed, alongside capture and
matching. Supplying a valid ring does not activate moderation or certify those
mechanisms.

Installation identity is recorded separately from tenant qualification. Changing
request coordinates or repeating setup cannot silently replace the recorded
trust ancestry. The selected maintenance principal must already exist; creating
the binding or a qualification revision does not grant it permission.

The native installation operation requires a current human instance
administrator and checks the selected service and tenant in the transaction.
User-owned API keys cannot borrow their owner's administrator privilege.
An identical repeat preserves the established identities and current revision;
conflicting coordinates do not overwrite historical ancestry.

A final finding is tied to the actual case and rule revision, with a response
or qualified urgent-protection reference. Reversal preserves the original
record and adds its accountable correction. These checked foundations do not
by themselves enable the new moderation runtime.

Authenticated finding writes are now source-defined at
`POST /api/moderation/cases/{caseId}/findings/{findingId}/finalize` and
`POST /api/moderation/findings/{findingId}/reverse`. The route selects the case
and finding even if the body names another identity. Retain the original
operation ID and exact expected qualification, case, policy or finding revisions
when retrying. Native receipts distinguish an exact retry from changed intent;
the generic response cache is deliberately not the authority for these writes.
Finalization still requires a currently qualified independent reviewer and an
actual protected response opportunity or qualified urgent exception. Reversal
requires independent current native authority and preserves the original reason,
including after a policy change. A caller cannot assert these permissions in JSON.

Both operations accept at most 64 KiB JSON, require authentication and write
limiting, return private noncached minimized results, and use ProblemDetails for
failures. They do not publish investigation text or automatically restrict the
subject. There is no new case action link until a qualified case view can safely
offer it; clients must not invent an action from account roles. Eighteen new
transport cases and extended native writer/replay assertions are authored but
unrun/unbuilt. The published schema and typed `IModerationFindingsClient` now
include both operations, generated through the existing tooling rather than
hand-edited. Client source compilation succeeded; matching generated captures
are not authenticated end-to-end, provider or concurrency assurance. The export
host also logged a lifecycle-delivery scan failure, so generation does not prove
service health. These routes do not establish
historical publisher policy acceptance or enable classification progression.

Native administrators can now approve explicit subject-safe text for one
planned case transition. Approval binds the exact case, current or next decision version
and selected policy meanings; it does not publish private investigation notes.
An exact retry preserves the original approval. Withdrawal retains the
original record, does not renew its deadline, and remains possible after the
case closes or the tenant becomes inactive. These text approvals do not grant
case-reading rights, establish guilt or enable consequences; the complete
native disclosure and consequence workflows are still being integrated.
Revoking or reconciling a contribution changes its sanction without advancing
the original decision. Those notices therefore bind the current decision version;
decision-changing transitions must bind the next version. The runtime checks
that distinction against the version acquired before mutation, rather than
accepting matching substituted rows. Preparing current-version text alone
cannot authorize a decision-changing transition.

Activation qualification now also respects the independently adopted deployment
profile's finite deadline. If that authority expires while other required
owners are being checked, activation remains unavailable; longer-lived consumer
or reviewer evidence cannot renew it. Real deployment and measured prerequisite
owners are still required before production activation can succeed.

Policy compilation now explains each selected profile and deployment mode
separately, retaining its full effects, consequences, consumer requirements,
activation gates and native prerequisites. Unsupported remote support is not
reported as an embedded-mode failure. Each explanation is either blocked or
still requires native qualification; source support labels cannot certify that
the deployment passed its checks. Retired definitions keep their historical
meaning without appearing as a new activation option. These explanations do
not activate policy or grant any action.

An independent current administrator can now adopt the exact complete profile
selection, declared mode and required prerequisite set for a planned transition.
The author, issuer and affected subject cannot approve their own selection.
Adoption is finite, keeps the original publication/profile meaning and can be
accountably withdrawn. This accepted configuration does not prove that a
deployment is installed, consumers passed measurement or prerequisites are met;
it does not activate restrictions by itself.
Independent profile and Event/resource target adoption can bind either the
current decision version for contribution-only work or its immediate next
version for a decision transition. Stale or skipped versions cannot borrow
those approvals. Scheduled reconciliation retains the original decision version
even when the sanction begins; its configuration must match that version.

The current profile reader now checks the original acceptance and current
independent administrators on the actual lifecycle transaction. Revoked or
expired adoption, lost administration and missing receipts reject the result.
The declared mode and required prerequisite set must still match the original
selected profile. This configuration check does not replace physical deployment
or measured consumer/prerequisite evidence.

The native profile/deployment join now checks that actual configuration and
independently adopted deployment together, then repeats both observations
before returning. Its deadline is the shortest existing configuration,
deployment or adoption lease; none is renewed. Artifact drift, withdrawn
authority or a replaced transaction makes the join unavailable. This check
does not establish operational readiness or passing measurements and cannot
activate a restriction by itself.

The native composition connects that join to the five prerequisite owners and
independently current consumer installations. The shared measurement reader
checks each consumer's own signed assertion against its actual installed runtime,
approved complete catalogue and current product/provider/configuration identity.
It checks retained measurement authority again before returning a result.
Missing concrete runtime owners or genuine executed consumer measurements still
prevent activation; an inventory declaration is not a passing measurement.

The API now has explicit installed-runtime observations for the full 23-consumer
Embedded resource-protection set, including queued, storage and derived-output
owners. Each observation requires the expected concrete services and pinned
product bytes. A missing or replaced implementation stays unavailable.
Installation and measurement preparation accept each consumer's own bounded ID,
but only when it belongs to the exact current native inventory. The launcher
binds its catalogue to that same challenge consumer and reproduces the deployment
artifact identity. These observations do not mean the tests ran or the deployment
is ready; complete approved measurements and final provider/runtime assurance
remain required.

Eighteen native audience cases have been prepared to exercise quarantined
resource status and download, external-access and management actions in detail
and collection responses. They include a quarantine committed after a response
has been assembled privately, before delivery, and record the actual selected
database identity. They have not been run or published as a measurement
catalogue; this source preparation does not certify deployment readiness.
Another 32 prepared cases cover management detail, lists, audit history and
metadata exports for both stored files and external links. They also check that
withdrawn organizer authority cannot release a previously prepared safe status
response. These cases remain unrun; the installed consumer declarations are not
passing measurements.

Original resource recipes also have a separate retained measurement path.
An administrator may identify the exact original effect and its stored snapshot/
effect-binding hashes when preparing or accepting a measurement. Those values
do not grant access: the native owner checks original custody, current qualified
people, issuer/signature provenance, tenant and privacy boundaries, cleanup and
the original retention deadline. A replacement publication does not become the
original recipe, and an expired measurement must be genuinely remeasured rather
than relabeled or extended. This path cannot activate an old publication or
reopen evidence after retention expires. Missing original purpose approvals
remain pending; execution and complete conformance are still unverified.

The original coordinates are retained on the immutable measurement challenge.
The launcher verifies their original preparation provenance before and after
execution, and native acceptance rechecks the same custody. A partial or changed
anchor stays unavailable; no old publication can be chosen merely because it
exists. This implementation is source-compiled, with actual original measurement,
provider migrations and complete runtime assurance still unverified.

Governance can now be deliberately qualified for a retained original recipe.
The operator must supply its exact original custody coordinates; native writes
independently verify the original publication/profile, substantive governance and
currently qualified independent reviewers. Replacement policy adoption is not
substituted. New evidence has its own finite deadline within original custody,
and it cannot renew older evidence or original retention. Ordinary activation
cannot consume original-purpose governance rows. These source changes and 36
new regression vectors still require deferred provider/runtime assurance.

Operational reports and independent acceptance can now be deliberately bound
to that retained original recipe too. Native writes verify its exact original
scope and finite custody, keep it separate from ordinary activation, and recheck
the purpose at final commit. This does not fill missing recovery drills, key
assurance or authenticated transport evidence, certify operational readiness,
or renew original retention. Its 46 new regression vectors remain unrun.

An authorized administrator can assign an open intake report for unresolved
triage before deciding who is responsible. Assignment can be withdrawn and does
not attribute the report to anyone, grant access to its private material or make
the reviewer eligible to approve a finding. Stale or closed intake cannot receive
a new assignment. Qualified review and the complete queue/fallback experience
still require their separate workflow and runtime assurance.

Responsibility under review now has a separate, explicitly qualified subject
case linked internally to the confidential occurrence. Native relationships,
not report counts or publisher-account assumptions, establish that link.
Different subjects remain independent; qualification itself creates no finding
or sanction and grants no access to other reports. Complete finding and
pre-decision response integration is still pending.
Typed native attribution and withdrawal commands now call that qualified writer.
Authorized review receives only its case identity; reporter acceptance remains
separate. Withdrawing qualification does not rewrite another subject's finding.
Command contract cases are authored but not yet executed, and public navigation
and wire-contract assurance remain pending.
Human finding acceptance and approval now check retained responsibility
qualification again at final permission checks. That check is not permission
to disclose the confidential report, and does not automatically reverse a
historical finding when a qualification is withdrawn. Complete activation and
reconciliation assurance remains pending.
Protected pre-finding notice and response now have a separate source workflow.
An independent privacy owner accepts explicit finite custody before capture.
Human subjects and explicitly selected current Content representatives receive
a subject-safe account, not the confidential occurrence or report. Implicated
representatives cannot receive that disclosure. Notice and response are
separately encrypted; acknowledgement alone or silence is never response evidence.
Withdrawal or expiry removes retained ciphertext without renewing the deadline.
For an attributed Human case, advancing a decision's response stage now uses
that protected original response and its current receipt/custody, not a separate
generic statement. The generic case-response capture rejects attributed cases.
An expired opportunity or missing response receipt cannot be replaced with older
generic evidence. Advancing this stage records neither a finding nor an approval.
The transition must also roll back if original response custody is lost after
its complete database flush; the authored late-loss cases await final assurance.
Human response advancement now uses the existing privacy-erasure serialization
path through final commit, not only a standalone primary database transaction.
Issuing this protected opportunity now also creates one required native in-app
pointer for its exact qualified recipient. It contains only a safe title and
notice reference, not an allegation, response, email or bearer link. Muting
optional context notifications does not hide this required availability notice.
Unavailable native routing or materialization rejects issuance rather than
sending through an external provider. Availability does not acknowledge receipt
or prove that a response was submitted.
Exact retries retain the same pointer and original deadline. Missing or
substituted delivery cannot be silently repaired to restart the opportunity.
Withdrawal, expiry and ordinary erasure remove its pointer graph within the
same native transaction while preserving unrelated notifications. The authored
delivery/cleanup cases remain unrun; UI navigation, generated clients and
runtime/provider assurance are still pending.
Native finalization and human finding permission checkpoints now consume the
retained authenticated response. Presentation, delivery, cleanup scheduling,
erasure integration and runtime/provider assurance are still pending; the
authored cases have not been executed.
Typed acknowledgement and response commands now call the native recipient
workflow. Sending a response remains separate from acknowledging the notice;
the server rechecks the actual recipient and unchanged finite custody.
HTTP/portal runtime and navigation are still pending, and the new command
contract cases remain unrun.
Custody proposal, independent acceptance, notice issuance and withdrawal now
also have typed native commands. Operators must supply the explicit finite
purpose/mandate and recipient intent; the native writer independently checks
current authority and representation. These commands do not choose default
legal retention or make a report a finding.
Authenticated administrative routes now call those commands:
`POST /api/moderation/pre-finding/cases/{caseId}/custodies` proposes explicit
terms, followed by `POST .../custodies/{custodyId}/acceptance` for independent
acceptance or `POST .../custodies/{custodyId}/withdrawal` for withdrawal.
`POST /api/moderation/pre-finding/cases/{caseId}/notices` issues independently
authored subject-safe text under accepted custody. Supply an original UUIDv7
operation ID and expected case/custody revisions; route IDs own the selected
targets. The server requalifies the actual caller, privacy owner, purpose,
reviewer independence and recipient assignment. These routes return only a
custody or notice ID, never confidential evidence or response text, and cannot
acknowledge or submit a response for the recipient. Custody requests allow
16 KiB JSON and issuance 128 KiB; accepted custody still bounds actual text
and retention. Native receipts govern retries without extending deadlines.
The new boundary cases are unrun; generated clients and hosted/provider
authorization assurance are still required before deployment.
Qualified scope/subject JSON now has a strict intent representation: scope
objects contain `kind`, `id` and an optional `parent`; subject objects contain
`kind`, `id` and `origin`. The server rebuilds valid jurisdiction ancestry rather
than accepting a caller's claimed trust, roles or permissions. Duplicate/unknown
members and invalid ancestry are malformed requests. These values identify the
requested subject and jurisdiction only; current native ownership still decides
whether any action is lawful. The wire cases remain unrun and this prerequisite
does not itself publish a lifecycle endpoint or certify deployment readiness.

The native notice API now has source routes:
`GET /api/moderation/pre-finding/notices/{noticeId}`, plus authorized
`POST .../acknowledgement` and `POST .../response`. Reading a notice never
acknowledges it. The private, non-cacheable HAL view supplies recipient actions
only after current native qualification; a reviewer view cannot act as the
recipient. Its case ID identifies the authorized subject case, not the
confidential occurrence. Current tenant and recipient authority are rechecked
before output.

The Client now includes a protected notice page reached from the notification
bell or inbox. Its pointer contains no allegation or response text and grants no
access by itself: opening it still requires current tenant and recipient custody.
Reading remains separate from acknowledgement. The page offers acknowledgement
or response only when the private API supplies those actions, and refreshes the
native view after a successful write. Reviewer views cannot respond for the
recipient.

If a response is blank or exceeds its UTF-8 byte limit, the page keeps the draft
and returns keyboard focus to the response field after displaying its connected
error. No submission is made. The two rendered validation cases are authored but
unrun; browser and assistive-technology checks remain deferred.

Private text and unsent response drafts are cleared when the page changes,
is disposed or reaches its original retention ceiling. Long finite retention
uses timer checkpoints, not deadline renewal. Client source compilation and
generation passed; tests, browser/accessibility checks and live delivery
assurance remain deferred until the complete programme implementation is ready.
When the configured moderation reconciliation scheduler runs, expired private
pre-finding payloads are now erased in bounded native batches before maintenance
credential authentication. A missing credential cannot extend that notice's
retention deadline; unexpired recipient custody is preserved. This is source
integration, not completed scheduler/runtime assurance.
Ordinary account erasure now includes affected private pre-finding notices,
responses, custody and submission receipts across tenant partitions. An accepted
privacy owner does not gain an erasure veto, and erasure does not wait for notice
expiry or decryption. Only a case-scoped marker without human identity or private
content remains to prevent restarting the erased notice opportunity. It does
not match a replacement account, create a restriction or restore private history.
Provider migrations and runtime erasure assurance remain unfinished.

Scheduled finding checks now use the installed maintenance service's own
case-bound authority, not a human identity inherited from its credential owner.
The service observes only whether the original response remains qualified; it
cannot read the response, acknowledge a notice or respond for its recipient.
Original human roles, attribution, privacy custody and retention must still be
current through the final transaction commit, even when a decision expires
during processing. Missing or revoked service authority prevents observation.
Source integration does not establish completed runtime or activation assurance.
An attributed case keeps its original tenant-owned proceeding and occurrence.
A resource-specific finding instead names its exact qualified action scope and
attribution generation; the case's custody does not grant tenant-wide discipline.
Another resource cannot inherit that finding merely by belonging to the same
Event. Native quarantine now uses that exact retained finding without requiring
the proceeding's tenant custody to become Resource-scoped. Original selection,
independent adoption, live authority and exact material checks still apply.
Applying or replaying one quarantine cause does not complete the independent
review, recipient notification or remaining original consequences.
Older responses and findings cannot be renewed by selecting new source
metadata. The native renewal command now preserves that same proceeding and
records the expected source/generation/revision with the new qualification.
An exact retry cannot produce another generation, and a fresh read grant alone
cannot prolong unchanged live grounds. Private notices and responses retain their
original source generation and deadline; renewal cannot reuse them as a response
to newly qualified material or restart their privacy clock. Generated schema
and runtime assurance remain open.

Qualified native review can now invoke these authenticated JSON commands:

| Command | Route | Successful identity |
| --- | --- | --- |
| Qualify responsibility under review | `POST /api/moderation/case-attributions` | Subject-case ID |
| Renew the same proceeding | `POST /api/moderation/case-attributions/{caseId}/renew` | New source ID |
| Withdraw qualification | `POST /api/moderation/case-attributions/sources/{sourceId}/withdraw` | Withdrawn source ID |

Renewal supplies the exact expected source, generation and head revision plus
fresh qualification; its nested qualification operation ID identifies the retry.
The native writer independently checks current human roles, original material,
read grants and finite lifetime. Authentication alone grants none of these.
The route identifies the renewal case or withdrawal source, and a body cannot
change the current tenant or caller. Responses contain only the authorized
identity, without allegations, reporter identities or private statements.
Requests are limited to 16 KiB and write rate limiting; responses are no-store
and cannot be recovered through a generic HTTP idempotency response cache.
Invalid requests return validation ProblemDetails. Unavailable native
qualification returns generic `503` rather than disclosing case existence.
Withdrawal preserves original history and findings. Qualified administrative
navigation, regenerated clients and real HTTP/provider assurance remain pending.

A currently authorized administrator can revoke retained subject-safe explanation
text after its original attribution expires or is withdrawn and the private
response has been purged. Revocation still requires the exact case and approval
revisions and current authority in the owning tenant. It cannot restore private
bytes, prolong retention or change the finding or independent restrictions.
An exact retry returns the same correction receipt; changed fields conflict.
This source path has authored but unrun native regression cases.
Withdrawal of reviewer qualification and procedural/profile/target adoptions
also follows retained ownership instead of requiring old private response access.
A closed proceeding stays closed. Original adjudicator or subject departure does
not itself block currently authorized terminal correction; issuer-only withdrawal
and administrator-controlled rejection/revocation remain distinct.
Resource finding correction retains the original exact Resource scope even after
material removal. It cannot reverse another Resource's finding or authorize a new
restriction. Complete consequence delivery and provider assurance remain open.
Original resource work also distinguishes the proceeding's retained tenant
custody from the finding's exact Resource action. Retry checks the original
finding and its retained qualification generation; it cannot use another case
or Resource, rewrite the original source or restart its lifetime. Qualification
must have been valid when the finding was finalized. These source checks do not
acknowledge completed resource work: actual cause accounting, privacy, review,
release and outcome proof remain separate prerequisites.

Administrators can record a finite operational report and have its exact contents
independently accepted or withdrawn. Reports preserve missing measurements rather
than silently treating acceptance as readiness. The operational prerequisite
requires complete measured consumer coverage, current reviewer availability and
completed recovery of both canonical and erasure stores. Recognition additionally
needs purpose-specific privacy adoption, recognition-store recovery and verified
key convergence across required replicas; remote mode needs fresh transport proof.
The native recovery/key/transport proof authorities are still being implemented.
Until their actual observations exist, the prerequisite remains unavailable even
when a report has been accepted.

Profile, prerequisite and publication observations now share the original
native transaction's evidence session. If that session closes or its transaction
is replaced, qualification stops before consulting the next owner. The profile
reader retains the exact originally acquired operation while independently
rechecking its adoption; publication preparation cannot borrow that lifecycle
authority. This wiring supplies neither deployment approval nor a passing
measurement, and does not activate restrictions.

The programme now retains a typed deployment observation with its exact policy,
profile, mode, primary-database identity/version and required installed consumer
generations. A different administrator adopts that finite observation.
Changing an artifact or losing current authority invalidates qualification;
publication cannot renew the original deployment or adoption deadline.
Withdrawal remains available after expiry or deployment drift.

Reviewer governance is separately recorded and independently adopted. It names
the jurisdiction ceiling, evidence and response requirements, protected support
and independent-redress routes, review quorum, intervals and accountable
escalation owners. Reviewer identity, competence and independence are accountable
human assessments, not automatic certification. Each reviewer separately records
finite availability and a maximum caseload; unresolved assignments count toward
that workload. Expired, withdrawn, superseded or overloaded availability cannot
support this prerequisite. These retained governance terms do not establish
guilt, grant confidential case reading or prove the other activation requirements.

Production composition, provider migrations and the programme's runtime
assurance remain incomplete. Recording or adopting these sources does not enable
moderation or certify the installation as protected.

The shared event-continuity deadline now computes the earlier of 24 hours after
original activation or two hours before event start. Missing or already overdue
schedules require immediate escalation, and later observations cannot renew the
deadline. This timing rule grants no stewardship, physical or financial power;
native consenting fallback and dependent-registration handling remain to be
implemented. It cannot automatically cancel, postpone or refund an event.

The event-rights terms also require willing, currently permitted stewardship,
separately qualified physical scope, companion-safe rights partition and existing
settlement-owner idempotency against the historical merchant and currency.
Missing mapping makes nonurgent action unavailable; qualified urgent necessary
protection may retain accountable pending settlement. Independent governance
review is separate from continuity/settlement mapping owners. These obligations
grant no event, finance or premises power, and their native source/adoption and
target-specific consequence implementation remain open.

Recognition/privacy governance now separately records purpose, record-class
retention, correction, finite legal-hold rules, backup margin and accountable
recovery ownership for an exact policy/profile/mode. The nominated independent
privacy owner adopts that finite generation. The governing writer records
identity, controller independence and privacy/legal/recovery mandate grounds;
administrator status alone is insufficient, and adoption is not professional
or legal certification.

Pending capture and eligible matching material cannot receive legal holds.
Matching ends with the original sanction's expiry or reversal, purpose loss or
retention ceiling; a hold on separately justified correction evidence or replay
receipts cannot extend the sanction. Received-but-unsettled capture must escalate
before its finite lawful-request deadline for qualified recovery or legal
disposition. Ordinary erasure remains independent. The source stores neither
provider identities, matching tokens, DOB, contact/history nor secret keys.

Readable-key and backup obligations preserve original computed material; they
do not establish operational key convergence or permit recreating erased inputs.
Missing receipts, authority, adoption or current generations reject this
prerequisite. Withdrawal remains available after expiry or replacement.
Actual recognition capture, erasure settlement, recovery and complete consumer
readiness are still separate requirements; adopting the policy does not enable
capture, restore erased profiles/roles or certify the deployment.

Child/age governance now records reviewed minimum collection purpose,
child-risk, consent/guardian, assurance and classification obligations for an
exact policy/profile/mode. Its nominated independent reviewer must hold both
privacy/legal and safeguarding mandates, separate from recorder and collection
owner; administrator status alone is insufficient. Independent finite adoption
does not renew the source, and withdrawal remains available after expiry,
replacement or owner departure.

Authenticated POST contracts now cover policy `sources`, `adoptions` and
`adoptions/{adoptionId}/withdraw` under `/api/moderation/child-age-policies`.
They submit policy reasoning, not individual DOB, consent or age evidence.
The route identifies the adoption to withdraw; a body cannot replace it.
Requests are write-limited, uncached and bounded to 256 KiB JSON. Actual native
qualified reviewers and controllers remain mandatory. These contracts do not
enable individual collection or claim an assured adult; generated clients,
editor/export, complete native HTTP execution and readiness remain unfinished.

Declared DOB is not qualified age assurance. Local manual threshold assurance
remains a supported policy route; a reviewed optional provider is not mandatory
universal ID or face collection. Unknown, contradictory or expired evidence
stays restricted rather than becoming a conduct finding. The actual viewer or
participant must qualify; purchaser adulthood cannot qualify companions.
Classification is reviewed content, not event format or inferred religion.
Partial segregation cannot make the rest of an event safe.

The accepted source currently uses Gregorian UTC and March 1 adulthood for
February 29 birthdays in non-leap years, with adopted jurisdiction grounds.
Debate's minimum of 18 is ISLAMU's adopted policy, not a universal legal or
religious ruling. Corrections and adulthood crossings require reconsideration
without restoring unrelated rights or erased DOB/history. Ordinary erasure,
safe catalogue information and protected remedies remain independent.

The shared calendar model now calculates a declaration's threshold using an
explicit policy timezone rather than the viewer's or server's date. Its default
is UTC and March 1 for a February 29 birthday in a non-leap year. Missing or future
birth dates produce Unknown, and correcting a declaration recomputes its result.
This model stores no DOB and does not establish assured adulthood, approve
collection or enable an age gate. Native assurance and complete audience
enforcement still require their own qualified owners.

Finite metadata now distinguishes a canonical account holder from one actual
guest's registration-session slot, so purchaser or companion identity cannot
substitute for that individual. Local manual assurance and collection consent
references bind the exact subject, purpose and policy/source generations.
Changed references, correction, withdrawal or expiry invalidate reuse; retirement
cannot restore historical eligibility or extend the original deadline. These
models retain no DOB and do not authorize collection or certify assurance:
native identity/consent owners and server-side body gates remain to be connected.

The native reader now obtains calendar semantics only from a current independently
adopted policy source. Missing, withdrawn or stale adoption supplies no calendar;
the observation preserves source/adoption generations and the original expiry.
This connects reviewed calendar settings to the model without enabling collection
or claiming that an individual's age has been assured.

The classification model now versions reviewed content categories separately
from event format. Its compiler takes the strictest reviewed floor across the
whole event and every supplied session/resource, including the adopted Debate
floor. Missing or changed reviews require review rather than exposing a partial
“safe” summary. Correction and withdrawal cannot revive earlier qualification.
This is metadata compilation, not live eligibility enforcement: complete native
material inventory and audience consumers remain open.

The native taxonomy groundwork now separates owner proposal from independent
reviewer acceptance under adopted governance and child-age policy. Reviewers need
substantively qualified content/floor competence and current availability, not
just administrator status. A replacement covers the complete category inventory;
an expired, withdrawn or unaccepted newest proposal cannot revive an older one.
Editor/export and physical verification remain unfinished, and taxonomy
acceptance itself does not approve an event's actual material or assure viewer age.
The command path validates intent before native dispatch; validation is not
permission. Authenticated POST contracts now cover `sources`, `adoptions` and
`adoptions/{adoptionId}/withdraw` under
`/api/moderation/classification-taxonomies`. Withdrawal uses the route adoption
ID; requests are write-limited and uncached. Whole inventories permit up to 64
categories within a 2 MiB JSON ceiling. Generated clients, editor/export and
complete native HTTP verification remain unfinished.

Native material review now binds one complete Event inventory to the current
owner/manager assignment, independently reviewed taxonomy and nominated
independent reviewer. A changed or incomplete session/resource/form/provider/
purpose/option/destination inventory cannot reuse acceptance; the latest
unaccepted replacement cannot expose an older approved snapshot. Public actor
display text and profile pictures also belong to that reviewed inventory.
Current native versions are reread, including edits that did not advance a root
stamp. Ending a review after expiry keeps its original provenance without
renewing approval. This groundwork does not yet enable live audience access:
transport/editor/export, every final-use consumer and full runtime assurance
remain unfinished.

The material command path now validates proposal, independent acceptance and
accountable ending intent before native dispatch. Supplied inventory/version
coordinates are checked against native rows; they are not permission or proof of
completeness. Ending preserves the original acceptance rather than extending it.
Authenticated POST contracts now cover `sources`, `reviews` and
`reviews/{reviewId}/end` under `/api/moderation/classification-materials`. These
bounded, write-limited and uncached commands retain native current ownership,
complete inventory and independent acceptance. The route identifies the review
to end. Material JSON accepts paired native identities/GUID stamps, not computed
parentage or claimed approval. The 16 MiB ceiling supports the complete bounded
4096-entry material/component inventory; it accepts no raw resource contents.
Editor/export/generated controls and full HTTP
execution remain unfinished, as do final audience consumers.

The new inventory read transport accepts candidate mandate intent in a bounded
authenticated POST, keeping review grounds out of query URLs. It supplies only
current category meanings/versions, material identities/stamps and component
digests. Server identity and complete native inventory must remain qualified
through the actual private-before-release response write; no user role label
grants the HAL source-record action. The native owner retains actual current
ownership, independently qualified review, finite prerequisites and the complete
inventory through final delivery, then closes its read transaction. Changed
material/components, current source generation, roles, tenant, policy or expiry
invalidate the original response rather than silently renewing it. Product source
compilation passed; runtime assurance is still pending. This transport does not assert accepted
classification, individual age eligibility or permission to collect data.

The nominated independent reviewer can now request the exact unaccepted source
metadata through `GET /api/moderation/classification-materials/sources/{sourceId}/review`.
The source ID is not a permission grant: actual current reviewer/controller,
ownership, policy and complete inventory remain qualified through delivery.
A publisher or generic moderator role cannot substitute for that reviewer.
Changed proposal/categories/content, a newer source, expiry or any retained
acceptance closes the read. The returned HAL acceptance link is native-qualified;
acceptance itself performs a separate current check. Raw resource contents and
individual age evidence are not returned. Product source compilation passed;
runtime assurance and guided controls remain unfinished.

The current moderation transport has now been exported into OpenAPI and the
typed Client regenerated and product-compiled. Generated contracts do not grant
moderation authority or enable unfinished guided controls: live interoperability,
privacy/provider/race assurance and final audience enforcement remain pending.

The material authoring transport keeps the originally displayed proposal and
uses only server-provided owner or nominated-reviewer actions. A lost response
can be retried with the same frozen operation; it does not silently refresh
permission or replace the selected material. Integration compilation passed;
authored HTTP checks remain unrun, and this transport is not yet a guided page
or an audience-access grant.

The first profile-collection procedure model separates preliminary gate
declarations/reference coordinates from normal birth-date/name/email inputs.
It requires explicit reviewed choices and independent acceptance beneath the
exact child-age policy; names/email are not silently added. The vocabulary has
separate guardian relationship and guardian consent
reference fields; a relationship alone never supplies consent. These references
remain non-authoritative until their respective native owners qualify them.
This remains structural policy groundwork, not an enabled form or permission to collect
personal data. Native subject consent, any required guardian evidence and
before-body server enforcement remain unfinished.

The native procedure writer now checks the accepted collection owner and
independent reviewer, exact policy parents and original finite proposal through
the final database commit. Withdrawal preserves the original record rather than
renewing it; a newer invalid proposal cannot reopen an older acceptance.
Integration compilation passed; the authored primary-provider checks remain UNRUN.
Authenticated procedure source/adoption/withdrawal HTTP writes are now authored
with bounded policy bodies, original parent generations and route-bound withdrawal.
Their Application command source is integrated; targeted API compilation,
native export and generated Client compilation passed. A broader dependency
build timed out before that successful targeted recovery. The fifteen Application,
25 API and two hosted schema witnesses are not yet executed. The new guardian
reference vocabulary passed native/API/Client compilation and exact contract parity.
This still does not enable individual collection, establish consent or provide
a guided administrator form.

Private subject-consent lifecycle source now distinguishes a person's own
profile-purpose operation from guardian or purchaser permission. It retains
the original reviewed procedure and assessment with finite validity; withdrawal
remains possible after expiry and cannot renew the grant. It does not satisfy
other required prerequisites or accept a different tenant's policy scope.
Ninety-six Domain cases are authored UNRUN.
Native identity/assessment ownership, before-body enforcement and
ordinary PII deletion integration remain unfinished, so no consent form or
sensitive-data collection is enabled by this source slice.

Private consent storage is now source-mapped beneath the ordinary PII owner:
hard deletion of that PII row is configured to remove consent metadata too.
Original scope/procedure/assessment coordinates stay immutable, with exact
tenant/trust filtering and terminal withdrawal revision checks. Thirty-nine
relational cases are authored UNRUN; compilation passed, but provider/migration and actual
erasure-job assurance remain pending. This does not yet provide a usable native
consent or sensitive-data collection endpoint.

Profile applicability lifecycle source now requires a complete explicit
prerequisite decision and separate acceptance by the nominated independent
reviewer. Even “none required” needs that acceptance. A guardian relationship
and guardian consent have distinct references; neither is inferred from the
other. The original subject, purpose, policy and finite window cannot be
substituted or renewed, and withdrawal remains terminal. One hundred six Domain
cases are authored UNRUN. Native lawful determination, erasure-job integration and
before-body enforcement are still unfinished; compilation passed.

Private applicability proposals and acceptances now have storage mappings
beneath ordinary PII, so hard deletion is configured to remove both without
deleting unrelated procedure history. Original subject, procedure, controllers
and finite windows stay immutable; acceptance withdrawal uses revision checks.
The complete requirement set explicitly represents “none required” rather than
defaulting to it, and rows require their original resolved tenant and trust.
Seventy-four relational cases are authored UNRUN. Storage compilation,
provider/migration and actual erasure-job checks are pending; no collection
endpoint or lawful applicability permission is enabled by these mappings.

Native assessment withdrawal now has registered source that lets the original
nominated correction actor withdraw an exact retained acceptance even after
expiry. Actual identity, private subject state and original authority are checked
again at the final transaction commit; it does not renew or issue collection
permission. Thirty-nine native cases are authored UNRUN; compilation and
provider assurance remain pending. The original observation owner is now
source-complete and registered; proposal and independent acceptance still need
qualified actor integration and do not follow from an observation alone.
Policy authors keep their existing qualification; ordinary profile subjects
are not required to become administrators.

The corrective assessment command now validates retained coordinates and
forwards cancellation to the native owner. Its result contains only the
committed acceptance ID; unavailable or unqualified targets do not reveal
private target existence. Four Application cases are authored UNRUN, with
compilation and HTTP/guided action integration still pending.

Actual-self profile nomination now has native source that resolves the person
and original policy/procedure on the server. It stores only finite immutable
observation metadata beneath private PII, not DOB, email, provider account strings
or an age assertion. Later independent reviewers see the original subject and
window rather than becoming that subject or renewing the observation. Identity,
private state and policy changes close it before final commit. Thirty-five Domain
and fifty-three native cases are authored UNRUN; combined Persistence compilation
passed. Actual-self nomination and retained correction HTTP source is integrated
with strict JSON, write limits and no-store. Nine Application and thirty-two API
cases are authored UNRUN; new transport diagnostics are clean, but compilation/
export/client integration for these endpoints remains pending.

Native proposal and independent acceptance source is also now present. The
qualified nominated owner and reviewer remain separate from the actual original
subject; exact required sets include positive empty, and acceptance cannot renew
the original window under another source ID. Ninety-eight native cases are
authored UNRUN/unbuilt. Initial integrated API product compilation passed.
Record/adopt HTTP source is now integrated with strict original coordinates,
explicit complete requirements (including empty) and exact source/CAS acceptance.
The acting owner/reviewer and original subject are resolved natively, never
chosen through claimed client identity. Seventeen Application and seventy API
cases are authored UNRUN/unbuilt. Current record/adopt compilation passed;
current native export and generated Client compilation also passed. Exact
Release/Debug capture parity passed for those four boundaries.

Actual-self consent now has native source and authenticated grant/withdraw
operations under `/api/moderation/profile-subject-consents`. The person must be
the real original local/provider subject. A genuinely independently accepted
applicability assessment must demand this consent; empty or other-only
requirements cannot authorize an unnecessary consent operation. Administrators,
policy owners and reviewers cannot consent on someone else's behalf.

Each grant retains the original observation, accepted assessment, purpose and
finite window. Its consent generation is separate from policy/assessment
generations, and another ID cannot renew the same original grant. Withdrawal
authenticates original self and can terminally correct a historical grant after
the policy, assessment or parents expire or disappear. It does not restore
permission or extend retention.

Both HTTP operations accept expected coordinates only, reject claimed identity,
sensitive data and unknown fields, and retain bounded no-store responses with
no replay-response storage. The native owner keeps identity/private-state checks
through the real final transaction commit. Eighty-six native, seven Application
and forty-two HTTP cases are authored UNRUN/unbuilt. Current product compilation,
native export and generated Client compilation passed; actual new routes and
typed methods were inspected. Exact current capture and complete schema/JSON
checks passed. Account/guardian
authorities, collection receipts and sensitive-body collection are unfinished.
This does not yet enable a lawful DOB/profile collection form.

Ordinary existing-Human profile policy acquisition now has native source that
resolves the person's real local/provider identity without making them an
administrator. It keeps the original transaction, selected active policy,
reviewed procedure and finite validity; policy owners and independent reviewers
still require their own qualification. Forty-one native cases are authored
UNRUN and integration compilation passed. These cases assume an already
active policy and do not establish publication activation. No individual
collection, consent form or sensitive-body acceptance is enabled by this read.

### Completing a personal profile without premature collection

The source-defined profile-completion flow first checks the signed-in person's
current, reviewed collection context. The settings page refreshes a private
preflight before opening names or birth-date inputs. Where several contexts are
available, the person chooses explicitly. The preflight contains policy
coordinates, expiry and declaration presence/revision—not the birth date and not
a reusable permission. The API checks those coordinates again before reading the
sensitive request body. Missing or unsupported eligibility/guardian requirements
keep collection closed.

The API holds the owning privacy transaction and current source checks until the
mutation commits. Its bounded response stays private until then. If the original
permission expires, the mutation rolls back; client cancellation is not treated
as success. A declared date is private and is not proof of adulthood. Correction
replaces its current receipt rather than retaining a DOB history. Removal uses
the current revision and does not require renewing collection consent. Once an
erasure fence exists, self-service cannot reopen retained identity data; normal
erasure cleanup remains independent.

An additional unrun native test covers deleting the private identity row and its
current collection receipt while preserving the account and other people's PII.
It does not establish that the full erasure worker has executed this cleanup.

The erasure worker now has explicit source logic to dispose of that person's
profile observations, independent applicability assessments and consents before
deleting private identity data. This avoids depending on database cascades that
SQL Server deliberately restricts. Expired policy, withdrawn consent, a different
current tenant or loss of membership must not prevent this subject-owned cleanup.
Other people's collection metadata and the administrative policy definitions are
preserved. The existing durable erasure fence and recognition-before-purge order
are unchanged; a failed primary settlement rolls back and remains replayable.
Four new whole-workflow cases are authored but UNRUN/unbuilt, including failure
and replay. Persistence compilation passed with 62 warnings and no errors, and
bounded source review found no further material defect after fixture corrections.
Previously read metadata is disposed without discarding pending changes or
attempting a second deletion. This is not evidence that a deployed erasure worker
or provider matrix has passed.

Qualified private readiness also has a declared-only calendar display. It compares
the declaration only with the adopted threshold, never exposes the stored date or
exact age, and explicitly says that the result is not verified age evidence or
access permission. A missing, expired or different original collection receipt
yields Unknown; consent withdrawal cannot supply a qualified result. Correction
and removal do not leave a separate stored age copy. The display expires with its
original receipt, current collection context or next UTC calendar day, whichever
comes first. It cannot turn a declaration into assurance, adulthood certification
or participant eligibility, and it does not enable an action missing from HAL.

Fifteen additional native cases, a private-response whitelist case, a named
string-state schema contract and two rendered transport cases are authored but
UNRUN/unbuilt. The native owner now withholds expired display metadata after all
later checks instead of treating a live collection context as a renewed display.
Application/Persistence/API and corrected browser product compilation passed with
zero errors; existing warnings remain unsuppressed. The final expiry correction
also compiled, and generated string-state contracts and both Release/Debug
captures match their authoritative inputs. None of these checks executes the new
tests or certifies browser accessibility, provider behavior or deployment. This
display does not activate an age policy or authorize an audience decision.

Changing a profile image is a separate action with ordinary ownership and storage
checks, not a way to submit names or DOB without collection admission. The UI
follows server HAL links independently for image changes, collection and removal.
Private DOB drafts disappear when cancelled, expired or the component is disposed.
They also disappear when a refreshed context loses its collection action. If the
chosen context disappears, the page requires another explicit choice rather than
silently selecting a replacement. Selection stays disabled during refresh, and
ordinary context selection does not unexpectedly move keyboard focus.

These changes are still implementation source. The new native, API, transport and
rendered tests have not run or been built, and provider migrations and hosted
end-to-end verification remain pending. This existing-profile flow does not
certify initial registration, external-provider or participant intake, or complete
age/audience enforcement. Earlier storage receipts follow for historical context.

Persistence, API and browser product compilation now passes, and generated
contract captures match their authoritative inputs. This does not change the
unrun-test, migration or end-to-end limitations above, and does not activate an
age policy or certify the deployment.

Private identity storage now supports an optional declared birth date with a
collection reference and correction revision, outside ordinary serialized user
properties. Declaration is not assured adulthood. Removing it does not require
renewing consent or collection permission. This storage groundwork does not yet
enable enrollment/profile capture: native individual gates, protected correction,
provider migrations and evidence invalidation remain unfinished.

These sources store policy obligations, not subject DOB, consent/guardian
evidence or age assertions. Native collection, assurance, classification,
complete audience handling, provider qualification and runtime verification
remain to be completed; recording or adopting these obligations does not enable
those mechanisms or certify the installation.

Lifecycle activation now compares that accepted configuration with the
deployment owner's observation. A different source generation, selected
publication or contribution rejects the transition. Required approvals must
match the profile's exact compiled prerequisite set, not just its baseline
requirements, and configuration expiry bounds the whole check. Missing actual
deployment, consumer measurements or substantive approvals still prevents
activation.

The native control plane can now record and withdraw a finite installation of
the Embedded resource-file consumer. It compares the expected artifacts with
the actual loaded native owner and inventory; binary or inventory changes make
the earlier source unavailable. A live source cannot be overwritten, and
withdrawal preserves its original audit coordinates. Current reads also reject
lost installer administration, missing receipts and expiry. Installation expiry
bounds both publication and activation checks. This is artifact identity, not
proof that consumer tests passed or that the full moderation deployment is ready.

Before measuring a new active revision, an administrator prepares its exact
immutable publication candidate. This reserves the future publication identity,
revision and full content without selecting or activating it. After measurements,
the final publication must retain those same coordinates, the unchanged prior
revision and current original administrator authority. Changed content, expiry
or another adopted revision prevents reuse. Preparation and final publication
are separate native operations; the final operation cannot replay preparation
as if activation already happened.

The runtime also compares actual deployed product bytes with the measured
product identity. Missing modules, different module identities, linked files
or detected file changes cannot supply current evidence. This check does not
claim that the host is cryptographically attested or that tests passed.
Current source reads remain tied to the operation's original database
transaction; retaining or closing an old observation cannot authorize a later
operation.

Measurement receipts keep separate public identities for the actual primary
database and the isolated test database. This prevents a test-database result
from being mistaken for the application's runtime configuration. Credentials
and hashes of credentials are excluded; rotating them does not falsely change
the public database identity.

An administrator can prepare a finite measurement challenge for the current
resource-file consumer and an exact proposed publication. The server checks the
actual installation and compiled consumer contract, then preserves a fresh nonce
and original artifact coordinates. Retrying the same operation does not create a
new challenge or extend its window. Changed publication, artifact replacement,
withdrawal or expiry prevents reuse. This prepares testing; it does not declare
that tests passed. Measured test receipts also require their own expiry, which
can shorten publication and activation validity independently of installation.

An instance administrator can separately approve a measurement signer's public
P-256 key, exact runner and test-bundle identities, and a finite trust window.
The server retains the original approval and prevents replacement using the same
key identity. Revocation is terminal and preserves that history; replaying an
approval still requires current instance administration, not just a remaining
tenant administrator role. Signing secrets are not stored in these trust records.

The receipt verifier checks a fixed ES256 format against this independently
selected public trust, including the original challenge nonce and exact artifact
coordinates. A valid signature proves who asserted the result, not that the host
honestly ran the tests or that a restriction may activate.

The native accepting command now independently checks the original challenge,
current installation and publication, actual signer approval and their original
operation receipts before retaining an assertion. Its signed nonce, artifacts and
measurement window must match those sources. A retry cannot replace the assertion,
recreate missing history, or remain qualified after key retirement, installation
replacement or expiry. Retention preserves the original assertion; it does not
create a passing test result. The approved native launcher, exact measurement
catalogue and current conformance reader are still being implemented, so these
records do not yet establish deployment readiness.

Current native reads also recheck the retained assertion's original operation
history and the real installation, publication and signer roles under the
owning transaction. Withdrawal, replacement, retirement, lost administration
or expiry makes the observation unavailable without rewriting its history.
This is still an authenticated assertion, not proof of complete passing tests.

Provision the optional `MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM` only
for the native launcher through the selected secret authority: Infisical
`/moderation`, explicit environment injection, or shared User Secrets in
Development/Testing only. Its P-256 public key must match the separately
approved enrollment. Never inject it into API services or test children, store
it in tenant settings, or include it in reports. Ordinary startup does not
require this key. Rotation uses a newly approved key identity; retire the old
enrollment rather than replacing its historical public trust.

The native launcher runs fixed Domain, API and Persistence bundles once each
and reconciles its own process results, TUnit body spans, terminal test results
and append-only attempt journal before signing. Reports, filters, executable
paths and passing flags cannot be supplied through its CLI. Current approval
and artifacts are checked before and after the run; native authenticated
acceptance remains a separate required transaction.

Configure a separately provisioned test database through the selected authority
using `MODERATION__MEASUREMENTTESTDATABASE__*` fields documented in `.env.example`.
Never point it at the primary authority database. Only those test inputs reach
children; signer and vault credentials stay in the parent. These settings are
optional for normal startup. The version 1 input contract does not supply
external-service credentials.

Provider qualification must also show that each API and Persistence case used
the selected test database during its actual test body. The launcher checks a
native database witness against the selected public configuration, provider
and server version; merely opening the database in setup does not qualify the
case. Missing or different-database witnesses prevent assertion issuance.
Domain cases do not access a database. Complete case instrumentation and
executed provider assurance are still programme obligations.

The actual approved case catalogue is not yet published. Its absence rejects
execution, rather than implying incomplete tests passed. Do not run measurements
until the programme permits final assurance. Launcher exit zero means
`acceptance.json` was issued; it does not mean deployment readiness or activation.
Deploy its exact approved bytes separately at
`moderation/catalogues/<approved-sha256>/catalogue.v1.json` beneath the running
application's base directory. The digest must come from current native enrollment,
not a filename supplied by the caller. Missing or changed bytes remain unavailable.
Keep this public catalogue outside the measured application assemblies: putting
their expected byte digest inside those same assemblies creates a circular identity.
Installing the file alone supplies neither approval nor passing conformance.
See the native [launcher operational contract](../../../../../eng/moderation/README.md)
for exact identities, configuration and retained evidence.

The implemented native safe-text reader checks that text has its original accepted
operation receipt and that its approver still has the actual administrative
role. Copied caller claims or a stored text row alone are insufficient. Current
reviewer-policy limits and the original approval deadline remain separate
checks. This reader will join the capture workflow once exact consequence
targets have their own qualification and the complete owner is registered.

Administrators can also record a complete draft selection for one planned
transition. It retains every selected effect and consequence target, the exact
policy/case versions and the original operation partitions rather than only a
digest. Recording or retrying this draft does not approve the targets, change a
decision or enable a restriction. Actual target ownership and current versions
must still be checked independently before consequences can run.

Before using a draft in a decision explanation, the native reader now checks
that its original acceptance receipt and current author's authority still match
the separately approved disclosure. Changed target partitions, missing receipts
or expired approval cannot be substituted into an explanation. This matching
does not approve the targets or enable restrictions.

Administrators can now capture a numbered observation of an actual resource
version. The native writer checks the resource's event, publishers and stored
object/provider ownership without opening a document or revealing its protected
destination. Each observation retains the original native versions. Recording
another observation does not renew a previous approval; independent adoption
and a current native version check are still required before consequences run.

The observation writer now plans publisher, organizer and optional stored-file
Actor ownership before taking Tenant/qualification locks. Retaining-read
providers use a separate non-retaining planning connection; proposed coordinates
are checked again under native locks and never grant permission. A changed
resource/Event/storage parent cannot introduce a late Actor lock. Previously
accepted exact retry still returns its original receipt after native resource,
Event or publisher deletion, without recreating material or extending validity.
This correction is limited to observation: adoption and final-use paths remain
separate. Five native-store cases and one real retaining-provider Actor-save
race are authored **UNRUN/unbuilt**. Focused source review found no concrete
material finding, not provider/MAD proof. Initial Persistence compilation passed
with 62 warnings and zero errors; the final retained-retry follow-up also passed
with 62 warnings and zero errors in 6:54.60.
No database was updated and this does not activate a moderation profile.

An independent administrator can now adopt a particular retained resource
selection against its exact accepted observation. Self-approval, changed native
versions, missing acceptance receipts and expired sources are rejected. The
adoption expires within the original draft and observation windows. An
administrator can revoke it without requiring the resource to remain available;
revocation is terminal and preserves who originally adopted the target.
Consequences still require a current check at their final point of use.

Resource adoption now plans the original publisher and organizer together with
any stored-file Actor owner before taking Tenant/qualification locks. The
original observation fixes the Resource, Event and storage-object identities;
the current storage actor is discovered without retaining early native-object
read locks. A different parent or late storage-owner substitution is rejected,
not locked as if it had been the original target. This does not replace current
independent administration, acceptance receipts, native version checks or the
original expiry. Previously accepted exact retry survives native deletion, and
terminal revocation remains available without a live resource. Five native
cases and one retaining-provider Actor-save race are authored **UNRUN/unbuilt**.
Focused source review found no concrete material finding, not runtime/MAD or
provider proof. Persistence source compilation passed with 62 warnings and zero
errors in 6:17.83. Other final-use acquisition paths remain separate, and no
database was updated or profile activated.

Unsafe-resource preparation now supports a Content subject without treating the
object as a ban against its submitter. Content identifies the actual owning Event;
the selected scope identifies its exact resource version in the same tenant.
A different Event, reparented resource or broader partition cannot reuse that
approval. Every selected action and consequence still needs independent adoption
and a current check. This prepares the unsafe-object profile's target binding;
it does not quarantine or release an object, change publication state or enable
that profile before its complete native owners are ready.

Native resources now keep a separate material identity when a file or protected
destination is installed. Title edits and concurrency changes do not create a
different material version, and genuine version observations retain that
identity. An old observation cannot be supplemented with a newly assigned ID.

The resource-owned quarantine state keeps independent causes and blocks
publication, republication, payload access and replacement while any cause
remains. Ordinary edits, withdrawal, elapsed personal restrictions and deletion
do not release it or restore discarded material. The native maintenance owner
now applies an originally selected unsafe-resource quarantine only under its
exact live effect lease, original lawful purpose, current independent adoptions
and unchanged material/ancestry. It records one immutable cause atomically with
the guard; retries preserve the cause identity and timestamp.
Before committing that cause, the owner now finishes disclosure invalidation
and then rechecks current maintenance authority and the original finite purpose.
Expiry during invalidation must roll back the cause and guard together, leaving
the original effect pending. Three native regression cases are authored but
remain unrun under programme-final verification; this source change does not
certify provider conformance or make CFG09 ready for activation.
The real maintenance dispatcher can perform this step, but it releases its
lease and leaves the full recipe pending until notice and every other required
step are proven. A partial quarantine is not an acknowledgement of the entire
effect. Missing/expired original purpose or changed authority cannot authorize
replay. Recovery first resolves the complete original cause set; an unsupported
target or missing material cannot silently drop an obligation or execute only the
valid prefix. Cause accounting retains exact retired receipts without restoring
their guards, and distinguishes partial work from an accounted cause set. Neither
is proof that notices, protected response or review have been completed.

The native dispatcher now asks a separate original-recipe completion owner after
cause accounting. Completion requires the actual original notice acknowledgement,
protected response availability, committed independent object-review disposition
and every originally selected consequence and current terminal protection. A
pending review, SMTP pointer, unsafe-material guard or administrative assertion
cannot finish that recipe. Choosing not to submit a response is not itself a
completion failure or a human finding.

Closing the proceeding does not rewrite the case revision attached to its
original finding and operational recipe. The retained selection and independent
adoption keep that original revision; a newer case revision cannot be substituted
at retry or after preparation. This preserves accounting for original work
without reopening the case or extending private access. The remaining notice,
review, authority, retention and genuine completion requirements still apply.

When all proof is available, one transaction records an immutable original-bound
completion receipt and finishes the exact leased effect. A later pointer retry
checks the original owner's exact host/writer-bound completed transition on the
existing effect ledger under current maintenance authority. That acknowledgement
can survive notice cleanup and its later ledger revision without preserving
private text, recipients or a new retention period. It does not repeat
consequences, reopen private text or complete unfinished work after expiry.
The receipt follows original-source cleanup and cannot grant late access after
the privacy deadline. Current production measurement still lacks the complete
23-consumer resource protection set, so full completion remains explicitly pending,
not approved for deployment. Historical-publication measurement, generated
deployment/client artifacts and final runtime assurance remain to be completed.

The original completion owner now finishes pending disclosure updates before its
final proof checks even with separate privacy storage. This corrects commit
ordering; it does not provide the missing consumer proof or make complete
consequence execution available.

Ordinary audience, management, export and audit responses now show only
the exact authorized material's quarantine status, without embedded title,
description, file/name, origin, delivery link, private semantics or attribution.
Management offers status navigation only; it does not bypass a fresh qualified
safety review. Private resources remain concealed from unqualified callers.
If quarantine or audience loss occurs before output, the prepared representation
is discarded rather than delivered with stale links/details.
Original quarantine execution also checks retained erasure facts for its named
representatives, reviewers and other participating authorities before reading
the private original capture. These facts take effect even before the application
erasure saga appears. The check remains serialized through the actual resource
commit, including a final disclosure flush when the retained authority owns the
shared transaction. An existing quarantine receipt cannot override lost original
custody. Erasure does not remove that quarantine or restore discarded material;
a fresh, independently qualified safety review is still required for release.
The native release owner now accepts a separate fresh, independently adopted
safety-review purpose, current material inspection attestation and independent
approval quorum. This may release one exact cause after original private evidence
expires without rewriting the historical finding. It cannot borrow expired
permission, use urgency as safety proof or restore discarded bytes. Other causes
remain and an old apply retry cannot reactivate a retired cause. Exact authorized
release retries preserve the original receipt and time.
Fresh review and release now finish disclosure invalidation before their final
human, material and purpose checks. If the qualified human is deleted or the
purpose reaches its deadline during finalization, the new receipt and any cause,
count or disclosure change must roll back together. Six native control/loss
cases are authored but unrun; historical findings and other causes stay intact,
and this remains source integration rather than runtime assurance.
Overdue object-review maintenance uses a separate short-lived purpose to notify
current eligible managers without reopening private original evidence. Before
committing an escalation, the owner finishes disclosure invalidation and rechecks
that purpose, native authority and recipients. Expiry rolls back the notification
and escalation together; the unsafe-material guard remains. Three new native
regression cases are authored but unrun, so this is not runtime/provider
assurance or whole-recipe completion.
An attributed proceeding may remain tenant-owned while fresh safety review
qualifies only its exact Resource, material and Event. The server checks the
current attribution generation and independent review purpose without reopening
the original disclosure grant. Changes to material, reviewers, policy, purpose
or source deadline reject the final write. Renewing source qualification requires
the native expected-head operation; issuing another read grant alone does not
extend expired attribution. Source integration does not certify runtime readiness.
Authenticated 32 KiB, no-store JSON writes are
`POST /api/moderation/resources/{resourceId}/quarantine-reviews` for the closed
purpose/adoption/attestation/approval steps and
`POST /api/moderation/resources/{resourceId}/quarantine-causes/{quarantineId}/release`
for one exact approved cause. Identifiers and claimed evidence in a request
never grant authority; the server resolves current independent qualification.
Accepted results contain identity and original recorded time only. Invalid
requests are 400, missing authentication/authority 401/403, unavailable identity
404, mismatched intent/generation 409 and unavailable native authority 503.
`POST /api/moderation/resources/{resourceId}/quarantine-causes/{quarantineId}/inspection`
permits controlled inspection only under the current independently adopted
purpose and its named, qualified participants. It rechecks authority and erasure
before private reads and output chunks. Verified material bytes or a validated
protected destination are attachment output, never a public download bypass,
redirect or server fetch. Expiry/withdrawal, material change or lost authority
prevents further output; it cannot recall bytes already handed over.

An originally designated Event owner or manager can submit a protected response
through `POST /api/moderation/notices/{noticeId}/content-response` while their exact
original assignment remains current. The corresponding GET qualifies current
availability and supplies the HAL submission action; a replacement manager does
not inherit historical explanation access. Requests carry the original snapshot
and assignment/version, a UUIDv7 operation identity and at most 8,192 UTF-8 bytes
of response text. Repeating the exact operation returns its original response
identity; changing the text under that identity conflicts. The human is responding
for the Content object, not being recorded as its human finding subject.

Responding does not acknowledge the notice, change the decision, release unsafe
material or finish the consequence recipe. An adopted complaint period uses
calendar months from actual acknowledgement; a response can be submitted earlier.
The response cannot extend the original finite retention deadline. It is encrypted
with the existing deployment Data Protection authority, and original-source
cleanup or representative account erasure removes its retained text and receipt.

`GET /api/moderation/content-responses/{responseId}/statement` serves the original
response text only to a currently independently qualified reviewer of that exact
case. Being an administrator or the original representative is not enough.
Current account, native permission, controller/conflict qualification and current
procedural adoption are checked again at delivery, together with retained erasure
and the original deadline. Output is UTF-8 plain text with no-store, no-referrer
and nosniff headers. Failed output is not automatically retried, and the
server-owned plaintext buffer is cleared afterwards. Already delivered bytes
cannot be recalled. Qualified reviewers can discover response identities through
`GET /api/moderation/cases/{caseId}/content-responses`, with an optional `afterId`
cursor and a `limit` from 1 to 64 (default 32). This private HAL page contains
only response/case/effect identities and original submission, retention and
current qualification times; it does not open response text, ciphertext,
allegations or representative identity. Each `statement` link still requires
current qualification when followed. Lost authority, erasure or expiry before
delivery discards the whole prepared page. Empty or unavailable pages are the
same concealed 404; a response identity alone is not a permission.
The reviewer UI, actual review disposition, generated deployment/client artifacts
and runtime assurance are still being completed; submitting or reading a response
proves none of those steps.

Scheduled reconciliation now tracks the exact object's review deadline, bounded
to 24 hours. Overdue review creates separate content-free notifications for
currently eligible native managers, not automatic release or a new lifetime for
the original allegation. Current routing/recipients may be unavailable; that
remains explicit. A genuinely reviewed unsafe object can receive independently
approved continued quarantine without pretending it is safe. Only complete
review quorum satisfies the duty, and other causes/unsafe material stay held.
Qualified reviewers can use
`POST /api/moderation/resources/{resourceId}/quarantine-state` with the exact
current material, fresh resource case, their own reviewer assignment/revision
and current qualification revision. An administrator role alone cannot borrow
another person's assignment. Each bounded page identifies up to 64 operational
causes, review deadlines/completions and exact releases. It contains no original
allegation, human attribution, private material or download destination.

If independent fresh reviews complete more than once for an unreleased cause,
state shows the latest completed disposition while retaining earlier review
history. Equal completion times use a deterministic receipt-identity order. A new
review does not reset the original review deadline or automatically release safe
material; other quarantine causes remain in force. Two native regression cases
for successive reviews are authored but have not been built or run. This is not
provider or runtime assurance.

Active causes include the currently qualified safety-review action; retired
causes do not. Reading state is not permission to inspect or release material.
The server holds native and erasure fences through response serialization and
rechecks qualification immediately before output. Changed authority, expiry or
erasure can prevent the response. It is protected and uncached, not public
resource discovery.

Use the protected
`POST /api/moderation/resources/{resourceId}/quarantine-causes/{quarantineId}/review-state`
to read a named current review receipt and its finite purpose chain. Supply your
own current reviewer/case/qualification coordinates. A receipt identity alone
is not permission, and an earlier tip with a recorded successor is stale.
The response contains finite review terms and receipt state, not people's
identities, original allegations or inspected material.

Its HAL links offer only the next actions the native owner presently allows.
Inspection is limited to the independently adopted purpose's qualified
participants. Safe release additionally requires the full safe quorum and the
exact final approving person; completed unsafe review offers no release.
Expiry, withdrawal, erasure or changed authority blocks reuse. Reading review
state does not perform an action or extend any purpose.

Administration UI, full recipe proof, generated
contracts and provider assurance remain open, so the unsafe-object profile stays
closed.

The native notice reader now performs that current check for an adopted resource
partition. Revocation, removed administrator authority, a changed native version,
a newer observation or a missing acceptance receipt prevents reuse. Its result
expires at the earliest approval deadline. Every required partition still needs
this check before a complete explanation or consequence can be accepted.

For a resource-only contribution, the native reader now requires approval for
every selected effect and consequence before returning a complete target set.
Approving one action does not approve a different consequence naming the same
resource. A missing or revoked partition rejects the whole set, and its expiry
is the earliest deadline across all required approvals. This does not establish
readiness for profiles that require other native target types.

Lifecycle acquisition now plans all retained Resource/Event publisher,
organizer and stored-file owner Actors together before Tenant locks. Machine
maintenance includes its service Actor in the same ordered set, rather than
adding native target Actors later. The actual notice or execution consumer must
still recheck the exact selected adoptions and current versions. A changed
Event or storage owner rejects the attempt instead of introducing a replacement
Actor lock late. Resource-only, Event-only, mixed and single-partition reads
share that plan; the plan itself grants no action or complaint-body permission.

Original accepted retries do not become fresh target approvals, and retained
revocation/correction paths do not acquire a new lifetime merely because this
planning exists. Seven additional native human/machine cases are authored but
**UNRUN/unbuilt**; machine reconciliation, effect execution and real-provider
concurrency are not certified by those sources. Persistence compiled with 62
warnings and zero errors; no database is updated by this correction.

Further source inspection corrected initial maintenance credential discovery and
preserved accepted Event-observation retries after native deletion. Current
credential validity is still checked under the actual acquired locks; an old
receipt does not create a fresh observation or extend its lifetime.

Material-generation reads and original Content representative checks now carry
their ancestry and assignment plans from initial acquisition. A later custody
read must match those exact sources rather than locking a replacement Actor or
role after Tenant locks. Existing private-purpose, representative, approval and
expiry requirements remain unchanged; public calendar planning grants no new
anonymous disclosure permission.

Seven additional regression cases are **UNRUN/unbuilt**. Their source follow-up
compiled with 62 warnings and no errors. Subsequent source review also corrected
material ownership checks that still acquired Event-role locks after Tenant,
and inventory/review reads that discovered material locks only in a second
acquisition. These reads now plan the selected publication's material sources
before their first acquisition, and later mandate checks must match and borrow
that original Event-role plan. Public calendar planning still grants no new
disclosure or material-acceptance permission.

Two further inventory/review regressions are **UNRUN/unbuilt**; Persistence
compiled with 62 warnings and no errors. Focused source review found no concrete
material defect in that correction, but is not runtime or MAD proof. Full reconciliation,
original completion and provider concurrency remain unverified. No database has
been updated.

Original Content-representative portal reads and acknowledgements now plan the
original recipient's Event roles and ownership before taking Tenant locks.
Detail/list delivery and acknowledgement rechecks must match those same sources;
they cannot substitute a later manager, Event owner or role while preparing the
notice. Existing private-purpose, original assignment revision, privacy and
expiry checks still apply. These coordinates confer no complaint-body permission
and do not extend the original retention window.

Seven acquisition/read cases, two actual co-located acknowledgement cases and
three final-recipient expiry cases are authored but **UNRUN/unbuilt**. The commit
check now reads the clock again after awaiting identity/privacy checks; an expired
representative cannot gain an acknowledgement merely because the original notice
is still retained. The optional pointer-email owner now also acquires and reuses
the same original representative sources before Tenant locks. Three additional
SMTP cases are **UNRUN/unbuilt**; email still does not acknowledge the subject or
renew the notice window. The first compilation found two connected SMTP caller
errors; their correction compiled with 62 warnings and no errors. Neither
these sources nor earlier builds prove live delivery or provider concurrency.
No database has been updated.

The separate original-notification status endpoint now also locks the original
representative's current Event-role and ownership sources before Tenant locks.
It uses the same acquired coordinates when rechecking prepared status, without
substituting a later manager or Event owner. It checks the representative's
original finite authority again after final identity/privacy reads; retention
of the notice alone cannot authorize delivery after that authority expires.
Original notification dates, Human-subject status and purpose-purged status do
not gain any new permission or retention. Six native status cases are authored
but **UNRUN/unbuilt**. Initial source compilation passed with 62 warnings and
no errors; the subsequent Host-planning correction is included in the successful
approval build (62 warnings, no errors). Focused source review found no material defect, not runtime
assurance. Other
protected-response and capture consumers remain under implementation, and no
database or deployment was changed.

Recording an original Content disclosure approval now also acquires its nominated
representatives' Event roles and publisher/organizer ownership before Tenant
locks. Those coordinates come from the persisted decision, not a caller's claim
to represent the Content. All existing independent approval, original assignment,
privacy and expiry checks still apply. Replay and final commit checks reuse the
same attempt's sources; changed ownership or roles cannot silently introduce a
replacement representative or renew the notice's retention window.

Seven native approval/replay cases and one retaining-provider concurrent Actor
save are authored but **UNRUN/unbuilt**. Source review found no material defect;
compilation passed with 62 warnings and no errors after correcting a local-name
collision. Neither review nor authored cases prove live provider
behavior. Human-subject notices remain separate, and no new complaint or payload
permission is implied. No database was updated.

The original representative's protected response and availability paths now also
acquire Event roles and publisher/organizer ownership before Tenant locks. They
reuse those exact sources through private preparation and submission checks;
changed ownership cannot silently introduce a different representative. Every
request still needs the original notice, assignment revision, privacy and finite
custody qualification. A response does not acknowledge notification or renew its
retention, and the private statement remains purpose-bound encrypted data.
Fourteen additional native cases are authored but **UNRUN/unbuilt**. Initial source
compilation passed with 62 warnings and no errors; focused source review found no
additional material defect, not runtime proof. A failed terminal availability
check now aborts prepared native changes rather than committing them while
withholding the response; that correction compiled with 62 warnings and no errors.
Other
protected-intake and capture flows remain under implementation. No database was updated.

Pre-finding Content notices now also retain the original case and representative's
assignment sources before Tenant locks. Issue, acknowledgement, response, private
disclosure and finding-response evidence reuse those exact sources; changing the
Event owner or assignment cannot nominate a substitute late in the transaction.
Human-subject recipients retain their separate path. Existing custody, independent
reviewer, privacy and original deadlines still qualify every operation, and no
complaint permission or retention is created by the plan.

Failed terminal disclosure aborts prepared native changes. Existing write checks
also run at the actual co-located commit boundary after disclosure flush; response
rechecks cannot renew the original window. Twenty-nine additional native cases
are authored but **UNRUN/unbuilt**, including six co-located flush/commit vectors
for notice/response, receipt and pointer rollback. Source review also identified
omitted participant-investigation reporters/controllers; their existing native
discovery is now included before Human locks, with an additional real-store case
authored but unrun. Initial compilation passed with 62 warnings and no errors;
final correction compilation also passed with 62 warnings and no errors.
Source-review follow-up found no additional material defect, not provider,
machine reconciliation or hosted assurance. Other capture/store paths
and the complete programme remain under implementation; no database was updated.

The native writer now rejects a new notice above the existing complete-discovery
limit of 64 stored notices for a case. Otherwise a 65th notice could prevent later
acknowledgements or responses from qualifying. Exact replay of an original issue
does not consume another slot. Existing custody and deadlines are unchanged; this
is not an adopted reviewer-workload quota. The additional boundary/replay/recipient
regression is authored but **UNRUN/unbuilt**. Product compilation passed with 62
warnings and no errors, not provider or hosted assurance.

Disclosure also requires the exact acquired policy jurisdiction, writer
generation and catalogue revision. Keeping a policy UUID while substituting its
source metadata does not qualify a different profile.

An administrator can now record a finite native Event-generation observation
separately from resource observations. It retains the actual Event and publisher/
organizer version coordinates without private event logistics. Re-observation
advances its own sequence; retrying the same accepted operation does not.
Observation alone does not approve an event consequence, stewardship handoff or
target adoption.

Recording that observation now acquires publisher/organizer Actor locks before
Tenant locks, matching ordinary Actor profile changes and avoiding the opposing
lock sequence. A changed Event ownership plan rejects the whole observation;
it cannot add a new Actor lock late or leave a source/receipt/qualification prefix.
Two native-store substitutions and one real-provider concurrent-save scenario
are authored but **UNRUN/unbuilt**. Persistence compiled with 62 warnings and
0 errors; neither that compilation nor these
sources nor the migration artifacts prove provider concurrency or readiness of
other Event-target operations. No database has been updated by this correction.

An Event's content identity and the human who submitted it remain different
subjects. A human contribution must name the observed submitter; a content
contribution must name the observed Event. A service or organization cannot
borrow either identity. This identity safeguard does not
approve the Event's classification or material, and does not establish calendar
or discovery disclosure protection.

Event-target adoption now requires a separate current administrator, the exact
retained selection and accepted observation, and a fresh check of native Event
versions and submitter identity. A changed or re-observed Event rejects new
adoption against the stale observation; exact retries return the original
acceptance, not fresh authority. Adoption expires no later than either source.
Adoption now locks the retained observation's publisher/organizer Actors before
Tenant locks, matching ordinary Actor profile saves. A different retained
generation or changed Event ancestry cannot introduce a replacement Actor lock
late. Accepted retries still return their original receipt after Event or
publisher deletion, rather than trying to create fresh approval.

Accountable revocation retains the original acceptance and still works after Event deletion;
it does not restore the Event or execute a stewardship handoff or consequence.

Four further native-store/provider regression cases are authored but **UNRUN and
unbuilt**. A focused source review found no concrete material finding; this is
not provider-concurrency or runtime proof. Targeted Persistence compilation passed
with 62 warnings and 0 errors (6:13.54); other qualification paths remain separate. No database has been
updated by this correction.

Final-use Event qualification now rereads that acceptance, current administrator
authority and actual native Event versions. Changed submitter/ancestry, revoked
adoption, lost administration, newer observation or expiry rejects the result.
An Event-only notice approval requires every selected effect and consequence
partition, not just the first successful target, and expires at the earliest
partition deadline. Other target kinds, stewardship consent and deployment
readiness still require their own qualified owners.

Selections containing both Events and resources now qualify their native owners
together. Every selected partition must have one independent accepted owner and
a current native version. A missing or revoked Event partition cannot be hidden
behind an accepted resource, or vice versa. All targets use the earliest
deadline; this does not enable unsupported target kinds or consequences.

Review requires current case-specific controller and conflict qualification.
Two accounts controlled by the same human do not become two independent
approvals. Qualification expires at its exact boundary, and withdrawn assignment
cannot be restored by renewing old evidence.

Adopting rule definitions is separate from activating restrictions. An active
publication needs current prerequisite and complete consumer evidence for its
effects; an identifier or administrator role alone is insufficient. Published
meaning is retained across replacement revisions, and incomplete profiles do
not become active by omitting unsupported effects.

Qualification schemas preserve case ownership, reviewer tenant boundaries and
one current policy head per jurisdiction. Database constraints and concurrent
revision checks reject malformed or stale qualification updates. Applying these
migrations does not adopt live evidence, register enforcement consumers or
activate a moderation profile.

Native source changes retain accountable case-specific evidence and an exact
operation receipt. They check the current human's actual administrative
assignment rather than a claimed role, and user-owned API keys cannot borrow
that privilege. A subject records their own response without an administrator
fabricating it; administrative privilege and active membership are not response
prerequisites. Controller and conflict qualification must be current and
independent, with validity bounded by the adopted policy.

The source update, qualification revision and receipt commit together. An
identical retry by a currently authorized human returns the original result;
reusing the operation identity for different content does not create another
update. Policy replacement retains adopted rule meaning and remains inactive
until complete activation qualification succeeds. These source operations do
not register an enforcement consumer or enable a moderation profile.

The native lifecycle now separates current subject response, issuer withdrawal,
independent review and administrative correction. Reconciliation belongs to
the installed maintenance service: an administrator or credential owner's role
cannot impersonate it. A retry rechecks current authority before returning its
old accepted result. Missing measured deployment, consumer or prerequisite
owners keep activation unavailable while supported draft and remedy operations
remain distinct.
Permission is checked again after the final local write and before returning a
stored result. Closing a case cannot leave a new proposal committed, and a
reviewer's qualification deadline cannot be extended by an earlier successful
check.
An independently qualified urgent finding can proceed without falsely recording
a subject response. Its urgency evidence, separate qualification receipt and
current assessor authority must still exist. Ordinary response-based findings
also require the actual current final finding for this exact case and rule,
not merely a supplied identifier. An issuer using another origin remains the
same human and cannot act as an independent adjudicator. Urgency does not supply approval
quorum or enable an unsupported restriction.

Confidential intake distinguishes the object being reported from a responsible
person. Its new receipt storage binds one account, exact target and evidence
generations, current relationship and a finite deadline without storing the
receipt token itself. A receipt cannot be transferred to another person or
reused for another case. Native receipt authentication checks the actual
nondeleted account and current tenant/host qualification inside the intake
transaction; a delegated API key cannot use its owner's receipt.
The native receipt owner now commits its protected-target proof before returning
a submission token. It checks the original account, relationship and finite
deadline again after saving. A failed or uncertain commit returns no token;
retrying issuance cannot recover a token from an uncertain earlier commit.
This does not replace the existing report API or itself enable confidential case reading.

For Event-version receipts, including local incidents, issuance now holds the
original publisher and organizer before the tenant lock, matching native Event
writers. If either relationship changes before commit, issuance denies rather
than acquiring a replacement publisher late or returning a partially qualified
token. Account and reporter checks, original deadlines and confidential-custody
requirements are unchanged. Ten new native cases are authored **UNRUN/unbuilt**;
bounded source review found no material defect. Persistence compiled with 62
warnings and no errors. This does not establish provider concurrency support or
enable case reading.

Submission receipts for hidden content targeting an Event-linked Actor now use
the same original publisher/organizer-before-Tenant ordering. A changed ancestry
denies the receipt rather than substituting a publisher partway through. Actual
Actor membership, reporter authority and original deadlines are still required;
this does not extend confidential-read access or custody. Five additional native
cases are authored **UNRUN/unbuilt**. Persistence compiled with 62 warnings and
no errors, and bounded source review found no material defect. Standalone Actor
and other read paths are not newly assured; provider/runtime proof remains open.

Event-linked Service submission receipts now hold the same original publisher/
organizer anchors before Tenant. The service must still belong to a real native
publisher Actor; credential owners, operators or issuers cannot stand in for
that relationship. Changed ancestry denies issuance rather than replacing it
late. Standalone Service behavior, reporter checks and original deadlines are
unchanged. Five new native cases are **UNRUN/unbuilt**; Persistence compiled with
62 warnings and no errors. Source review corrected a test setup assumption about
the existing qualification revision, not the service authority rules. Service
read/disclosure and provider assurance remain separate, with no new
confidential-body permission or custody policy.

Read-token issuance also retains the original publisher and organizer roles until
commit. Swapping those roles cannot qualify merely because both Actor identities
and the Event version stamp remain the same. This is an internal terminal check,
not a new read permission, lifetime or receipt field. It still requires the
independently issued read grant. Four new control/swap cases are **UNRUN/unbuilt**;
bounded source review found no material defect and Persistence compiled with 62
warnings and no errors. That terminal-only increment did not establish read-path
acquisition order or provider-concurrency support.

Read-token issuance now has source logic to acquire the original publisher and
organizer before Tenant for Event-version and local-incident material. It first
discovers only the caller's original grant/source coordinates with normal filters,
then requalifies those exact coordinates under the native fences. A different
source, caller or publisher cannot replace the original plan partway through.
This discovery is not read permission: the independent current read grant, case,
issuer, policy and original deadline are still checked before commit. No token
remint after an uncertain commit, new lifetime or complaint-body authority is
introduced. Twelve native cases are authored **UNRUN/unbuilt**; Persistence compiled
with 62 warnings and no errors, and bounded source review found no material defect.
That issuance increment did not cover downstream disclosure. Other target paths
and provider-concurrency assurance remain open; no deployed read assurance is claimed.

Protected narrative disclosure now also has explicit source ordering: the original
read-grant issuer and publisher/organizer sources are acquired before Tenant.
Discovery only nominates original lock coordinates. The actual read token, current
grant and case revision, original issuer, source, independent custody policy and
original deadline must still qualify; metadata never grants complaint access.
Changed token/receipt, grant or publisher coordinates deny output rather than
substituting new authority. Terminal checks freshly authenticate the original
receipt, including after protected-byte authentication and before output.

The reader retains its existing same-transaction output and byte-zeroing cleanup;
there is no automatic output retry, new custody period or extended token lifetime.
Eighteen parameter cases are authored **UNRUN/unbuilt** with both early and late
drift checkpoints. Application compiled with 182 warnings and no errors;
Persistence also compiled with 62 warnings and no errors after correcting the
scope-coordinate access. Bounded source review found no material defect. This is
not executed provider or deployed disclosure assurance.

Read ordering now also retains Event ancestry for Event-linked Actor and Service material.
The original publishers must qualify throughout issuance and disclosure; a real
Service-to-publisher relationship is still required, not credential ownership.
Standalone Actors and Services remain independent of Events. Existing read-token,
grant, case, issuer, custody and deadline checks remain mandatory. Eighteen new
reader parameter cases and two standalone read/disclosure cases are authored
**UNRUN/unbuilt**. Persistence compiled with 62 warnings and no errors; bounded
source review found no material defect. This is not deployed-read or provider
assurance. No new complaint-body permission is created.

The authenticated intake routes are
`POST /api/moderation/cases/submission-receipts` and
`POST /api/moderation/cases/allegations`. Receipt issuance accepts native target
and relationship lookups and two base64 material fields, each limited to 64 KiB.
Account and tenant authority come from the server, never the request body.
Allegation submission also requires `narrative`: the same base64 bytes supplied
as issuance `material`, not substituted text. New intake requires a separately
adopted finite confidential-custody policy for the tenant and target kind.
This policy does not depend on a publication, attributed person or finding.
Its authenticated write routes are
`POST /api/moderation/case-narrative-policies/sources`,
`POST /api/moderation/case-narrative-policies/adoptions` and
`POST /api/moderation/case-narrative-policies/adoptions/{adoptionId}/withdraw`.
Recording finite terms does not adopt them: the separately named privacy owner
must adopt the exact current source revision and writer generation. The server
checks actual human/controller identities, current administrative authority,
tenant, mandate grounds and the expected qualification generation. Body
coordinates are intent, not proof. Withdrawal removes custody permission
without extending original deadlines or rewriting acknowledgement history.
An adopted moderation constitution cannot offer less than the project's six
calendar months of protected complaint eligibility. Operators must still assess
their applicable legal obligations. This minimum neither authorizes complaint
submission by itself nor keeps a private payload for six months: custody and
its original finite deadlines remain separate. Six Domain cases and one native
writer rejection case are authored unrun/unbuilt; migration and runtime
verification remain pending.

Protected complaints have a distinct `ProtectedContestation` custody purpose.
The same governed source/adoption routes can record its explicitly chosen finite
limits and obtain the separately named privacy owner's exact adoption. An
allegation or investigation adoption cannot be reused for this purpose. Recording
these terms does not open a complaint endpoint, assign an appeal reviewer or
change a restriction.

A legal period for making a complaint is not a requirement to keep its private
payload for that entire period. This source increment chooses no retention
default or legal eligibility policy. Two Domain and six additional native cases
remain authored unrun/unbuilt. Production compilation and generated browser
contract/capture alignment passed; generated migrations and runtime assurance
remain pending for this addition. The complete complaint and
remedy flow is still under implementation, not available from custody metadata
alone.

Investigator assessment is the selected threshold for opening participant
responsibility under review, not for deciding guilt. Investigation grounds have
a separate `ParticipantInvestigation` purpose supported only for participant
incidents. Record and independently adopt its finite custody terms through these
same governed routes; an allegation policy cannot supply that permission.
Selection and named owner qualification are separate for each purpose. Neither
recording nor adopting terms creates an assessment, attributed case or sanction.
Investigation capture/attribution, generated contracts and runtime/provider
assurance remain unfinished. One Domain and four native cases are authored unrun.
The assessment input now distinguishes identification grounds, alleged conduct,
provenance and contradictions/credential misuse. Each is required and bounded;
the adopted byte limit caps the complete encoding. These are investigator
assertions awaiting authenticated protected custody, not proof of guilt.
Five additional contract cases remain unrun; no assessment endpoint is available
from this input contract alone.
The finite encrypted assessment model now preserves original incident material,
participant/session generations and separate accountable investigation custody.
It rejects same-event participant or version substitution and cannot extend the
original physical deadline on withdrawal. Its 51 Domain cases remain unrun.
Assessment storage shape alone supplies no finding, restriction or entry grant.
Assessment storage is now scoped to the selected tenant and exact trust domain.
Original allegation reads authenticate the actual retained material under its
own custody purpose and immutable encryption binding. Tampered bytes, a changed
bound deadline or another host's keys cannot release text; three additional
native cases also check that no disclosure callback runs, all unrun.
Native protected capture, attribution and disposal now have source integration;
runtime/provider verification remains unfinished.
The assessment mapping now fixes original provenance against later updates and
preserves exact finite deadlines. Fifty-six native storage cases are authored
unrun. Required parent records cannot be deleted while an assessment references
them, so the physical purge and erasure owners must remove dependent assessments
first. Both physical owners now implement that order: scheduled cleanup obeys one
shared row budget and original deadlines, while lawful user erasure disposes
affected assessments under its exact durable saga across all tenant partitions.
An unrelated purchaser, organizer, submitter, account link or policy owner does
not trigger assessment deletion. A missing decryptor or departed custody owner
cannot retain material after its lawful disposal condition. Thirty-three new
disposal cases remain unrun; final provider assurance is still pending.
Cleanup also compares exact recorded withdrawal instants after database candidate
discovery, so clock regression cannot make rounded date comparisons erase an
assessment before its recorded cutoff. Two additional cases remain unrun; there
are now 35 authored disposal cases awaiting programme assurance.
The typed assessment intent now requires exact incident material, authorized read
reference, alleged Human, Session generation/window, bounded custody and protected
grounds. Thirteen input contract cases remain unrun. Capture independently checks
the actual investigator and read issuer, affected accounts, incident material,
Event/order/participant/Session versions and window, and the two separately adopted
custody purposes. The protected original allegation and encrypted assessment are
authenticated on the same native transaction. Retry keeps original capture and
disposal deadlines; losing current authority before commit rejects the write.
Thirty-four native capture cases remain unrun.

The separate `ParticipantInvestigation` responsibility basis names an exact
assessment ID and revision 1. It may open only the identified Human's responsibility
under review: it creates no finding, guilt, strike, sanction or admission.
Attribution uses authenticated grounds and the original native capture receipt,
not a roster, account link or plausible storage row. It checks the original
investigator/read issuer, both current custody purposes and the incident generations
again on initial use, retry, renewal and final commit. Its deadline cannot exceed
the assessment's. Confidential assessment disposal preserves attribution history,
but missing or expired assessment material cannot authorize current use.
EventSubmission remains limited to the actual EventVersion submitter. Forty
Domain/Application and 30 native attribution cases remain unrun; migrations
and provider/race assurance remain open.
A new grant, custody approval, source ID or later deadline without new incident
grounds cannot extend a live under-review proceeding. Another current native
reviewer may check the original assessment without becoming its investigator;
that internal check does not grant access to the confidential text.
Capture the independent assessment through
`POST /api/moderation/participant-investigations/sources`. Supply UUIDv7 operation/
source/case/read/material/Human/Session identities, exact case/evidence/qualification
revisions, the material version and Session generation, incident window and
finite UTC validity/disposal deadlines with at most six fractional-second digits.
The request also carries `identificationGrounds`, `allegedConduct`,
`evidenceProvenance` and `contradictionsAndCredentialMisuse`; each must be
substantive and at most 2,048 characters. Unknown fields are rejected. These are
confidential assertions for native qualification, not established conduct or a
caller-supplied authorization decision.
The actual request context and current native investigator/read/custody authority
control acceptance. Success returns only source identity and revisions. Repeat
the exact original intent to recover an acknowledgement; never extend its clocks.
Use the resulting source ID and revision 1 with the separate case-attribution
command's `ParticipantInvestigation` basis to request Human responsibility under
review. Capture alone opens no case. The capture route has a 32 KiB body bound,
write-rate limiting and private/no-store responses; do not log its grounds.
Twenty-nine Application and 24 API cases remain unrun. Native OpenAPI and the
Client SDK have been regenerated and compiled; responsibility basis, incident
target kind and custody purpose use actual named wire values. The SDK's internal
enum ordinal is not the API contract: use its configured serializer and the named
value, not a cast to an integer. Two native/Swagger hosted enum cases remain unrun.
Hosted/provider verification is still required before operational readiness.

This feature does not yet supply current physical-risk qualification or admission/
credential/private-logistics enforcement. A historical incident or final finding
alone does not establish current risk for a different Event/session/window.
Confirmed risk needs its own current independent assessment, protected finite
processing authority and actual native coordinates. Choosing a separate risk
custody purpose or explicitly expanding a new investigation adoption remains a
provider decision; existing investigation adoption must not be treated as either.
Emergency protection and rescheduling require their distinct native powers and
owned fallback/settlement paths; no automatic exclusion or renewed punishment is
available from these source artifacts.
The 32 KiB, no-store JSON routes return only accepted identity/revisions;
malformed requests are 400, denied authority 401/403, missing source 404,
generation/intent conflicts 409 and unavailable native authority 503.
Policy-reading/HAL administration and generated client/runtime coverage are
still being completed.
The original material is protected at rest, separately from receipt history,
and ciphertext and acknowledgement commit together. Missing policy or protection
rejects intake. Retention starts at capture, never extends on retry and is capped
by the adopted deadline. Additional genuine material stays distinct.
The first response contains only the committed receipt identity, opaque token
and expiry; the token is needed by the second request. Both responses prohibit
caching. Do not retain the token in logs or shared client storage.
Authentication and authorization failures retain 401/403.
Malformed requests return 400, changed original allegation intent returns 409,
and unavailable current native authority returns 503. These routes require a
qualified native intake policy; they do not activate moderation policy or prove
that the deployment has passed its required provider checks.

Grant an independently qualified material read purpose using
`POST /api/moderation/case-read-grants/{caseId}`. Supply a UUIDv7 `operationId`,
the exact `expectedCaseRevision`, native `sourceId`, target `granteeUserId` and
finite UTC `validUntil` with at most six fractional-second digits. The server
rejects sub-microsecond values instead of rounding the original intent.
The case in the URL controls the lookup. Naming a
grantee does not claim the administrator's identity or give that account access:
the server must qualify the current human administrator, target account, exact
case/material and deadline before committing. The response contains only the
committed grant identity, not material or a read token. Reuse the same operation
and original intent when retrying; do not change the deadline to renew it.
An expired or revoked grant, changed material/case, or lost original
administrator role grant cannot be repaired by replaying its creation.
The grant and its native recovery advancement commit together; only the original
transaction's exact recovery row is accepted. Changed grant coordinates or
unrelated tracked writes make the operation unavailable rather than returning a
grant identity. This does not change the request, response or retry contract.
Use `POST /api/moderation/case-read-grants/{grantId}/revoke` with its own UUIDv7
`operationId` and `expectedGrantRevision` of 1 to withdraw that original live
grant. The grant in the URL controls the lookup. Both routes require current
native human authority, reject invalid coordinates with 400 and unavailable
native qualification with 503, and use no-store responses, write-rate limits
and 16 KiB request bounds. Authentication failures retain 401/403. They do not
activate policy or certify provider/runtime readiness.

`POST /api/moderation/cases/read-receipts` separately accepts a read-grant
identity. That grant must already have been issued independently for the
current account and exact case/material; submitting an allegation does not
create it. The server rechecks the grant, case revision, protected target and
deadline before committing a finite read token. A revoked grant, changed case,
lost account authority or failed/uncertain commit returns no token. A read token
cannot submit another allegation. Its response contains only receipt identity,
opaque token and expiry and follows the same authentication, no-store caching
and write-rate limits. This route does not return evidence or identify a
responsible person.

Use `POST /api/moderation/cases/{caseId}/narrative` to read the original
confidential material with that separate read receipt. Its JSON body contains
`caseId`, `expectedCaseRevision`, `receiptId` and base64 `presentedToken`;
the case in the URL controls the lookup. Keep the token out of URLs, logs and
shared client storage. Submission receipts cannot read allegations.
The server checks the current account, exact case/material and read grant,
independent custody policy and original deadlines again at actual delivery.
It returns the original bytes as a no-store, no-referrer, nosniff
`application/octet-stream` attachment named `allegation.bin`, not an HTML page
or an attribution of guilt. Invalid coordinates return 400; missing, expired
or unqualified material returns the same 404 without revealing a private case.
Authentication and authorization failures remain 401/403.
Native authority fences remain held during the response write; a failed write
is not retried automatically and server-owned plaintext is cleared afterwards.
Already delivered bytes cannot be recalled. Generated client/schema outputs
and runtime assurance are still pending.

Ordinary account erasure now removes that reporter's confidential material
from every original tenant, including inactive tenants and former memberships,
before local erasure completion is recorded. It does not require current
custody or recognition adoption or access to decryption keys. Other reporters'
material remains, including in shared cases. Batches contain at most 100
identities, but the whole local settlement remains one transaction; a failed
batch rolls back local deletion and completion so the native erasure saga can
replay it. Original acknowledgement/provenance history remains and cannot
recreate erased narrative bytes. The deployment's provider/runtime checks
are still required.

Typed allegations can now retain an incident and its material evidence without
assigning responsibility to a person. They cannot supply a final finding or
reviewer qualification until genuine attribution and independent review exist.
Native proof now binds existing event-related objects, real reporter
relationships, explicit finite lifetime policies and separately issued read
grants. The committing service saves case ownership, receipt consumption and
original acknowledgement together. Exact retries return the original
acceptance, even after submission expiry, without reopening target access.
The native submission command now exposes that committed acceptance with only
its acknowledgement identity and acceptance time.
The opaque receipt token is copied defensively and is never returned with the
acknowledgement. Acceptance does not disclose confidential case contents,
attribute guilt or grant later case-reading rights. The browser/API receipt
and reading workflows are still being completed.
Reporters no longer supply or receive the canonical case identity or revision.
Independently qualified additional facts for the same occurrence join its
internal case without exposing other reports or reopening a closed finding.
Each reporter receives their own immutable acceptance. Reissuing copied material
does not duplicate evidence, extend its custody or recreate purged plaintext.
This is a breaking development contract; generated clients, wire checks and
runtime assurance remain pending.
Unrelated authenticated reporters can now qualify publicly eligible published
events without claiming publisher ownership; losing visibility, publisher
eligibility or participation invalidates that permission. It cannot open a
private target or read a confidential case.
Participant-related permissions also retain their exact registration-order
and participant versions. Removing either record or changing either version
before the final checkpoint invalidates issuance rather than preserving a stale
permission.
Case-read permissions are also requalified after a transaction retry or a
later transaction in the same request scope. Earlier checkpoints cannot replace
current account, target and read-grant checks.
Current instance administrators can also obtain case-purpose receipts for actual
Actor and Service records without inventing an event. A service does not need a
publisher Actor. Removing the target or grant, or changing their retained facts,
invalidates the permission. Tenant administration and credential ownership do
not supply this instance-level relationship, and submission still grants no
case-reading permission.
Broader standalone reporter coverage, policy administration
and the remaining API integration
are still being completed; the existing report API remains in place.

Coop and Osprey callbacks now supply advice for human review, not executable
discipline. The server checks the actual dispatched case and provider source;
changed scope, target or evidence cannot replace original advice under the same
provider identity. Pending provider decisions cannot be executed through the
local decision route. Existing human actor suspension/reinstatement remains in
place until all capability-specific enforcement and recovery paths are ready.
Coop and Osprey advice is bound to the native case version originally sent to
the provider. If that case has changed, new advice is rejected even when the
callback is authentic and supplies no expected version. A stale queued request
cannot send current case details under an older version. An exact retry of
previously retained advice remains an acknowledgement of its original history;
changing its provider binding cannot turn it into a current finding or
disciplinary action.
Database migrations and full runtime verification are still pending.

Native lifecycle commands now separately name proposal, response, finding,
approval, activation, rejection, withdrawal, revocation, supersession and
deadline reconciliation. They retain the exact expected revisions and operation
identity across retries; changing the request cannot reuse an accepted receipt.
They still require current server-owned authority. Activation does not bypass
the separate deployment and consumer-readiness gates, and a scheduled start does
not restrict anyone early.

Native review and urgency use separately adopted, current case-specific
qualification policy. Its two evidence-lifetime limits are independent of the
restriction's duration: choosing a longer restriction does not extend reviewer
or urgent-protection evidence. Missing, expired or changed policy approval
prevents qualification instead of falling back to a profile or default.
An authorized administrator explicitly publishes the two limits and then
adopts that exact case-policy revision. Publication alone is insufficient.
Replacement or revocation invalidates the retained approval; expiry cannot be
renewed by retrying an old command. Procedural adoption enables qualified review
and urgency evidence, not restriction activation or deployment certification.

A later draft preserves the independently active publication. New activation
must qualify that actual active selection, its complete current evidence and
the exact decision targets before an accepted operation can be replayed.
Evidence expiry remains binding through the committing transaction; a passing
check earlier in the operation is not a permanent permission.

The reference catalogue defines all 27 approved non-chat profiles, ten complete
consequence bundles and three additional strategies. Digital participation
restrictions preserve independently valid physical bookings unless another
qualified ground applies. Entitlement protection and credential recovery are
explicitly different purposes. Event protection binds its selected end instant;
rescheduling does not silently renew it, and expiry cannot recreate cancelled
tickets or removed roles. These definitions are not enabled defaults: complete
native compiler, enforcement, consequence and current-evidence integration is
still required before adoption and activation.

Separate schedule-risk review source now preserves the original personal
restriction and its end while tracking the changed event/session, actual
participant generation and adopted policy publication. Pending review has an
explicit deadline and escalation owner; accepted, withdrawn or overdue work
cannot restart or itself authorize location disclosure, credentials or another
exclusion. Sixty-five domain cases are authored but unrun. State validation
rejects corrupted source ancestry, deadlines and incomplete terminal receipts
before any transition or deadline observation. Native admission,
persistence, all reschedule paths, final output checks and fallback/settlement
execution remain incomplete; this is not enabled event protection.

Native review storage now retains the original restriction interval, original
and revised source generations, separate policy publication identity, human
owners and terminal review receipts. Duplicate original work and stale writes
have dedicated model protections, and reads remain exact-tenant/trust scoped.
Sixteen storage/isolation vectors are authored but unrun. Generated migrations,
native qualification and end-to-end enforcement remain required before use.
Four further storage cases cover accepted-review history and missing finding/
reviewer references. Withdrawing a reviewer keeps the prior receipt as history;
it does not provide current permission. All twenty storage cases remain unrun.

The existing moderation maintenance schedule now has a bounded review-deadline
pass. It uses the configured tenant and its current installed maintenance
credential, then records overdue work without extending the personal restriction.
Unavailable custody or changed work after saving rolls the transition back.
The review cursor resets with tenant configuration and advances only after an
available pass. This records overdue status only: risk qualification, accountable
escalation delivery, event fallback and rights settlement remain separate work.
The eleven native and two scheduler regression vectors are authored but unrun.
One further native case covers clock regression: maintenance rejects the
affected review without changing it or aborting the bounded pass. Twelve native
deadline cases and two scheduler cases are now authored but unrun.
Participant-incident responsibility and the current Event/session/participant
risk qualification are still required. A linked account or an old restriction
is not incident proof. Do not treat stored review status as operational
credential, admission or private-location enforcement.

Calendar exports currently apply ordinary public or registration-scoped
location access. Installed calendar-consumer metadata does not establish
current moderation, actual-participant safety or age eligibility. The Event/
participant consequence owner and final private-file release check remain
required; an Event ID cannot borrow an EventResource approval. Two native
substitution cases are authored but unrun. Do not adopt these incomplete paths
as measured calendar protection or promise recall of already imported copies.

Private resource downloads and redirects recheck current native authority
immediately before releasing their prepared content or destination. If authority
or the attachment changes, the preparation is discarded rather than disclosed
using an earlier permission check. Audience detail and collection responses also
retain their metadata privately until a fresh check at response execution,
including revocation after links were prepared. A stale private representation
is refused rather than serialized. These checks do not enable a moderation
profile: current machine-source and canonical moderation enforcement still
require complete qualification.

Management pages, audit and metadata exports also keep their representations
private until final authority checks. Audit pages are refused if retained entries
expire or retention policy changes during preparation. Exports recheck the
download decisions behind their links; retry a refused projection to obtain
current metadata rather than reusing its prepared result.

Machine requests retain their exact API-key ID, owning authority tenant and
owner coordinates and credential authority, without inheriting the owner's human identity. Audience
continuations and prepared responses cannot transfer between those identities.
Existing audience cursors must be refreshed from the first page after this
change; they are paging positions, never transferable permission grants.

Prepared machine responses also recheck the current key source before delivery.
Revoked, suspended, expired or deleted persisted keys, changed ownership or
changed authority tenancy refuse the response. Configured clients must remain
uniquely configured, active, unexpired and bound to the same owner. A response
cannot switch between persisted and configured credentials with matching key
names. These source checks do not complete canonical owner qualification or
enable a moderation profile.

User-owned credentials must also retain their actual user and active membership
in the credential's tenant. Deleting the user or removing that membership
prevents a prepared resource response from being delivered even if its key is
still Active. The machine does not acquire the user's roles or human identity.
Instance-administrator-owned credentials must remain globally owned by a
current user with a current platform-administrator assignment. Removing that
assignment or deleting its user also prevents delivery of an already prepared
response, even when the key remains Active. This ownership check does not grant
administrator privileges to the machine.

Every tenant-bound credential must retain an active owning tenant, including
when it reads a public resource in another tenant. Suspending, archiving or
purging the credential's tenant prevents a prepared response from being
delivered while leaving the other tenant's resource available to qualified
callers. Tenant-owned keys must name that exact owning tenant.

Organization- and group-owned credentials also require the actual owner and
its current, nonsuspended placement in the credential's tenant. Deleting the
owner, removing its placement or suspending that placement prevents a prepared
response from being delivered even while the key remains Active. This checks
ownership, without borrowing an issuer's human management permission.

Installing moderation alone does not change resource access; existing
resource authorization remains in force until the applicable ReadResource
policy is adopted for enforcement. After adoption, missing canonical identity
or unavailable moderation authority prevents disclosure. Inactive definitions
do not activate restrictions, and unrestricted moderation does not grant access.
Adoption still requires current canonical identity. Withdrawing support for new
activations does not remove existing sanctions or restore restricted resource access.
Resource-read adoption is declared by the selected instance or tenant policy.
A resource profile applies within that jurisdiction, but a sanction still names
its exact resource: restricting one resource does not restrict another merely
because both use the same profile. A later draft or a change to activation support
does not silently remove retained read protection. If the selected declaration
is incomplete or inconsistent, disclosure fails closed until the policy source
is repaired; it is not treated as an installation without adoption.
Retiring support also keeps the recorded rule and consequence meaning available
for a separately qualified transition notice. It does not authorize a new
restriction, and delivering that notice does not complete its other consequences.

Canonical machine moderation uses an explicit binding to an existing service
identity, not the key owner's human identity. A replacement key can select the
same service so changing credentials does not create an unrelated moderation
subject. The native installation command requires current instance-administrator
authority and verifies the selected credential and service; keys cannot install
their own bindings. Credential ownership and source are pinned, and a revoked
binding cannot be reinstated by repeating installation.

Configured keys also need explicit service selection; matching names do not
establish identity. Missing, revoked or unavailable bindings cannot establish
canonical machine provenance. These installation foundations do not activate
a protected machine consumer or a moderation profile: canonical service
complete consumer conformance and activation qualification are still required.

Configured-key source custody now retains the actual expiry and original source
instead of returning only current metadata. Reload, expiry or disposal invalidates
that custody; equal replacement metadata cannot renew it. Backward clock movement
cannot extend its original expiry budget, and no expiry is invented for keys that
have none. This is a source prerequisite: the native disclosure owner still needs
to connect its invalidation to buffered output while retaining the other authority
fences. Twelve new cases are authored; compilation/execution of tests remains
deferred, and this does not activate a protected consumer.

Configured-key authentication also retains an opaque receipt of the source
actually verified for that request. Authenticated source acquisition rejects a
missing receipt, different caller/key, rotation or source recreation even when
replacement metadata is equal. It keeps the original expiry budget rather than
starting it again at disclosure. This receipt contains no raw key and grants no
action permission. Native output-owner integration and runtime assurance remain
open; the authored authentication scenarios have not been executed.

Resource upload-session responses also require current read authority. This
includes reservations, finalization, cancellation and their idempotent replays,
even when finalization or cancellation uses the generic storage routes. Permission
to change a resource does not automatically permit reading its stored upload
metadata. A committed upload operation can succeed while its response is withheld
after read authority changes; keep the same operation identity when recovering
and requalify access instead of treating a lost response as a fresh upload.

Native upload commands return `StorageUploadSessionResult`. Ordinary uploads
retain `Response`; resource-owned success retains only `Projection`. Dispose the
result, prepare the representation privately, and inspect the terminal outcome
before transferring fields. The HTTP success envelope remains unchanged.
This response boundary does not certify complete moderation activation.

Resource reservation, staging, attachment and finalized replay also check the
canonical **Upload** restriction, in addition to ordinary update permission.
Upload is not a general ban on editing or reading: cancellation and failed-stage
cleanup remain available under their existing checks, and session responses still
require their separate ReadResource permission. Read and Upload restrictions do
not substitute for one another.

Each local upload transaction now participates in the existing privacy/recovery
and moderation writer fences. It retains them through commit and rechecks the
original moderation facts after flushing writes. The provider transfer happens
outside these transactions; attachment checks again after the transfer. If that
check fails, existing staging cleanup retains responsibility for the object and
quota rather than attaching it with stale permission. Participating administrative
changes need no restart. Direct database changes must use the approved fence
protocol or an owned maintenance exclusion. The short local transactions share
the installation fence, so they serialize there; provider transfers do not.

The shared host fence also covers instance-wide restrictions changed from another
tenant. The tenant write fence precedes the moderation generation fence, matching
other participating writers. Concurrent first adoption forces a fresh transaction rather
than reusing an older “not adopted” view. Retryable database conflicts restart the
whole privacy-enclosed local write. The final clock check happens after authority
queries, so time spent reading cannot extend a permission interval.

Thirteen new persisted-reader/commit-boundary cases are authored but **UNRUN and
unbuilt**, including expiry during authority queries and rollback when privacy owns
the same SQLite commit. Full adopted-Human upload/racing-writer, co-located final-flush and
multi-provider evidence remains outstanding. This source work does not authorize
moderation activation or certify complete upload-consumer conformance.

Embedded native integrations must consume audience and management query `Projection` through
`DiscloseAsync` instead of reading `Value`. Build the representation privately
and transfer it only in the delivery callback; check the terminal outcome.
Existing HTTP routes and response shapes remain the same.

Resource projection responses now retain their existing private preparation
through buffered HTTP completion. Original-request or consumer cancellation
aborts transport and cannot be reported as success when delivery returns.
Completion failure propagates without appending a replacement payload.
Outstanding completion is awaited before preparation is released. This source
change does not yet prove native restriction fences remain held through delivery;
that owner integration and runtime assurance remain open.

Resource audience responses bind their download, access and management links
to the same current authority used at final disclosure. If an action decision
changes while a response is being prepared, the prepared response is withheld
rather than delivering a stale action link. Refresh the resource: its metadata
may remain available with that action omitted. Link presence remains the UI's
authority signal; a visible link still does not bypass authorization of its use.

An urgent finding requires both the retained case evidence and its distinct
completed native qualification. The current authority of the assessor is
checked again when the finding is made; a historical receipt cannot replace
that permission. Missing qualification withholds the finding rather than
silently treating the allegation as proved.

The deployment ships one capability inventory with the Application assembly.
Its consumer list includes alternate native routes and final queued delivery,
not just HTTP endpoints. Partial or implemented-but-unverified source labels
do not enable an effect. Current conformance evidence must cover its complete
required set, including the remote receiver when RemoteStrict is selected.
An older registration cannot be reused after its source, policy, publication
or inventory changes. Missing packaged inventory is a deployment defect, not
permission to load a permissive fallback.

Activation requires the adopted profile's complete registered consumers,
current reviewer qualification, usable protected remedies, consequence owners
and required operator prerequisites. Imported or edited drafts remain inert
until that complete activation check succeeds. A reference profile does not
supply deployment-specific legal, staffing or safeguarding adoption.

Finalized findings retain the original declared reason separately from the
rule. Later policy changes and reversal preserve that original reason rather
than replacing it with today's explanation. New acceptance or approval still
requires the reason to match the independently selected policy profile.

Reviewer independence follows the qualified person controlling the account.
A second account controlled by the decision issuer does not provide independent
adjudication, and a delegated administrator key does not become its owner's
human approval.

The original moderation explanation is captured in the same transaction as
the change and its recovery work. A missing required disclosure owner, expired
approval or lost authority rolls the change back, including intermediate
writes. Drafts with no finding, approval or sanction record that they have no
substantive work without inventing an approved explanation. Retries verify the
original capture rather than rebuilding it from later policy or decision state.

For Content explanations, first capture and the final commit check use the same
original native acquisition and exact representative assignments. Copying grant
fields, replacing the session, or presenting previously captured metadata does not
grant current disclosure permission. Changing ownership or an assignment must
reject the original plan rather than introduce replacement authority after Tenant
locks. Rechecking an existing source preserves its original bytes, hash, retention
and finite end; it does not renew custody or complete the effect. Inert changes and
Human notices keep their separate rules.

Nine additional native acquisition/inert cases are authored but **unrun/unbuilt**;
they are separate from the fuller Content fixture source below.
Application/Persistence/API source compilation passed with 182/62/2
warnings and no errors, and source-only review found no concrete material defect.
This is not runtime, provider or deployment certification. No database was updated.

Resource-Content case prerequisites now use the actual parent Tenant for administrative
acquisition while preserving the original Resource, Content Event and independently
qualified writers. Qualified urgency, contribution selection, Resource adoption and
disclosure approval are admitted only for that closed Resource-Content path. New
Event-scope powers, profile adoption and activation are not granted. Original Event
parents are planned and locked before Tenant; a changed publisher or organizer rejects
the operation rather than borrowing replacement authority late.

Sixteen further native fixture cases are authored **unrun/unbuilt**, including a real
urgent finding-acceptance chain with actual notice capture/materialization and normal
or co-located commit observation. Their late-loss vectors require original assignments,
parents and finite custody to remain valid through complete primary persistence and
the final privacy-owner flush. They also require failed operations to leave no new
decision revision, effect, notice source or notification graph. This is test source,
not evidence that those paths have executed successfully or that a delivery route or
activation is approved. The initial prerequisite-gate change compiled (62 warnings,
no errors), as did its initial Actor-order correction. Source review then found a
terminal check that rediscovered case parents instead of retaining the original plan.
The corrected check retains that plan, including its absence; two further unrun cases
require rejection without acquiring replacement Actor authority late. The final
correction compiled with 62 warnings and no errors (2:22.84); bounded source-review
follow-up found no remaining attributable defect. All sixteen cases remain unrun and
unbuilt; this is not runtime, provider or activation proof. No database was updated.

## Scheduled reconciliation recovery

Recovery is opt-in through `Moderation:Reconciliation`. Select the actual
qualified execution tenant and supply a live persisted API credential through
the selected approved secret authority. It must be bound to the installed
maintenance service; its human owner's permissions do not grant maintenance
authority. Never track the credential or include it in job payloads or logs.

Set `MODERATION__RECONCILIATION__APIKEY` through the selected secret authority
(Infisical `/api`, environment injection, or shared User Secrets only in
Development/Testing). Ordinary appsettings cannot supply it, and a failed
selected provider never falls back to another. Rotation is observed in each
new execution scope.

Each target gets fresh credential and native authority checks. Batches remain
bounded, and a persistent cursor lets later targets progress when an earlier
one remains denied. This covers one explicitly configured tenant, not automatic
fleet enumeration. Scheduled activation still requires current findings,
approvals and supported effects. Expiry applies before maintenance runs;
review deadlines do not automatically remove restrictions.
Expiry cleanup, notice cleanup, sanction reconciliation, resource review,
schedule-risk review and orphan-effect recovery have independent bounded passes.
One failing owner no longer prevents the others from progressing. The scheduled
fire still reports failure, so a partial pass must not be treated as healthy or
as completed moderation work. Successful owners retain their own scan progress;
unfinished scans resume from their previously acknowledged position. Changing
the configured tenant resets all five cursors before any work. Cancellation
stops the remaining passes rather than continuing during shutdown. Each privileged
pass still requires a freshly authenticated maintenance credential; independent
progress grants no new authority. Scheduler regression tests are authored but
remain unrun under the implementation-first assurance deferral.
Discovery retains a fixed target batch with its scan position; reusing an
internal discovery list cannot discard those targets while advancing progress.
Expiring an already-ended contribution does not qualify a new activation.
Its notice still needs separately current approved content and native targets;
lost qualification or capture expiry before commit must roll back the entire
transition. Native regression cases are authored, but runtime assurance remains
deferred until the full implementation is complete.
Additional native cases specify that already-ended expiry does not reopen a
withdrawn response opportunity, restore private notice/response bytes or renew
their calendar when original response custody is lost. Resource-target cases
also require exact original generation and adoption rather than borrowing an
Event target. These seven cases are authored but not executed; they certify
neither activation nor completed downstream consequences.

Lifecycle writes now finish their pending disclosure updates before the final
capture-deadline and native-authority checks, including when privacy and primary
data use separate stores. Capture expiry or maintenance credential revocation
during that finalization must reject the transition and roll back its notices
and disclosure changes together. Three native cases specify valid completion and
both late-loss outcomes; they remain unrun until final programme assurance.

Recovery preparation now has a native retained-store metadata reader in addition
to the canonical reader. It checks the committed store revision, recovery
generation, retained range and identity-key verification reference while keeping
the original authority alive. It neither copies private data nor proves that
backup, restoration or reconciliation succeeded. Artifact custody and finite
disposal, isolated restoration and genuine replay remain unfinished; the ten
new native cases remain unrun. Two further cases cover legal-hold compaction
and release: releasing a hold changes the store revision even when its retained
range and key reference stay the same.

Measured-report acceptance must also lose authority safely if the qualification
epoch changes during its final database flush. The added regression requires
no acceptance or partial receipt to commit and preserves the original report.
It is authored, not yet executed. How later acceptance and activation validate
current recovery separately from the report's original snapshot is still pending;
no new operational readiness is claimed.

Measured-report writes also reject unrelated pending database changes at their
terminal check. A report or its acceptance cannot succeed while silently carrying
an extra staged case into the transaction. Two authored rollback checks remain
unexecuted; this guard does not make missing recovery proof available.

Changing the configured execution tenant restarts all four maintenance scans:
sanctions, incomplete effects, notice cleanup and quarantined-object review.
The switch clears the previous tenant's progress before any work begins, even
if the new pass fails partway through. A failure with the same tenant keeps its
last acknowledged progress. Cursor recovery never grants access or marks a
substantive consequence complete.

Durable effect work has separate, finite ownership. A live maintenance
credential can claim a two-minute lease or recover an expired one; retries do
not renew its expiry. Release leaves the work incomplete, and an older owner
cannot release its successor. Ownership and notice delivery do not prove that
all consequences succeeded. The registered dispatch route can now complete
original draft work proved to have no substantive consequences; duplicate
delivery verifies that original capture without renewing ownership. It uses
the same explicitly configured maintenance tenant and selected credential.
Instance-scoped empty work retains the actual tenant that committed it so the
maintenance worker can prove ownership. Its jurisdiction remains the instance;
it creates no tenant notice or delivery projection. Missing or changed original
writer ownership cannot be recovered by replaying the operation.
The empty-work probe reads tenant/retention metadata first. An original with
a finite disclosure-retention ceiling is handed to its qualified substantive
owner without reading its private explanation, including after retention expiry
or a retained erasure fact. The probe cannot treat that explanation as permission
or as proof that work is complete.
Missing, corrupt, foreign-tenant or substantive work remains incomplete and
uses ordinary outbox retry/dead-letter recovery. Substantive consequence owners
and producers remain required before complete effect-draining readiness can
be claimed.
