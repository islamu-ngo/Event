# Moderation authority and persistence

The reusable `ISLAMU.Moderation.Domain` owns qualified jurisdictions, independent
approvals, decision transitions and half-open restriction intervals. Its entities
store scalar ancestry coordinates and reconstruct checked semantic values.
`Explore.Persistence` maps those same entities; it does not keep a second
serialized interpretation of their state.

## Canonical recognition token computation

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
No ring provisioning, rotation, matching, persistence, erasure settlement or
recovery is supplied by this primitive, and erased inputs cannot be rehashed.

## Local transaction boundary

`ModerationStore` owns a serializable primary-store execution-strategy unit.
Operation and effect identities arrive already allocated. A versioned SHA-256
request binding includes purpose, qualified subject origin, complete target
ancestry, expected revisions, proposal terms and any exact superseded contribution.
A completed receipt
also binds the qualified principal. Identical retries return the original
accepted aggregate identities and revision; changed content conflicts.

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
Mutation authority remains the existing `update` transaction check. A successful
mutation does not itself authorize disclosure of persisted session fields.
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
