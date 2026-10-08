# Moderation authority and persistence

The reusable `ISLAMU.Moderation.Domain` owns qualified jurisdictions, independent
approvals, decision transitions and half-open restriction intervals. Its entities
store scalar ancestry coordinates and reconstruct checked semantic values.
`Explore.Persistence` maps those same entities; it does not keep a second
serialized interpretation of their state.

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
