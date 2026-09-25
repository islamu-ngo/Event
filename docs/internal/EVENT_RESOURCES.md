---
title: Event resource persistence
status: implemented-foundation
last_updated: 2026-09-23
---

# Event Resource Persistence

`EventResource` is an independent tenant/event-owned aggregate for governed event materials. This persistence phase does not expose management or delivery APIs; later authority and transport phases consume the entity-first repository.

## Relational ownership

The database enforces tenant-qualified ownership instead of relying on globally unique identifiers:

- resources reference `(TenantId, EventId)` events and optional `(TenantId, EventId, EventSessionId)` sessions;
- `SessionScopeId` is the session ID for session-owned resources and the event ID for event-level resources, giving audience rows a non-null composite owner key;
- accessible alternatives reference a different resource in the same tenant and event;
- stored objects use `(TenantId, StorageObjectId)` and a unique nullable attachment index, allowing many unattached drafts but only one resource per attached object;
- admission rules reference the actual `AdmissionTarget.Id` together with tenant, event, target type and scope. Session targets must match the rule's exact session; a target from a sibling session in the same event is rejected;
- ticket qualifiers carry both catalog-version and ticket-type IDs. Composite FKs prove `ticket type -> catalog -> event`; same-tenant ticket identity alone is not accepted.

Audience discriminator and qualifier checks reject empty/contradictory shapes. Publication/payload checks permit incomplete drafts but require one delivery payload for published or withdrawn rows. Availability remains scalar columns on `event_resources`; the computed Domain `Availability` property is not mapped and no owned availability table exists.

Nullable boundary and envelope checks explicitly test required leaves for null:
SQL `UNKNOWN` must not admit a partial anchor/offset or an envelope missing its
protection version. Teaser/public titles must contain non-whitespace text, and
deleted rows retain no delivery payload.

## Repository behavior

`IEventResourceRepository` returns Domain entities only. Detail, candidate, parent, subject, target, ticket, and audit reads are bounded and no-tracking. Only `GetByIdForUpdateAsync` tracks the resource graph. Candidate ordering is deterministic by `(SortOrder, Id)`, represented by `EventResourceCursor(int SortOrder, Guid Id)`.

Candidate reads reject descending or alternate sorts before querying; their
cursor cannot describe those orderings. Equal SortOrder values continue through
the same database-ordered Id tie-breaker used by the first page.

The port has no generic unbounded CRUD/query surface. Oversized identity requests
are rejected, and subject fact reads detect budget overflow rather than quietly
return an incomplete authorization snapshot.

Mutation handlers call `Update` after the accepted Domain operation and before
the unit of work saves. It explicitly marks the aggregate modified so the existing
DbContext concurrency-stamp mechanism also runs for child-only policy edits when
the supplied audit timestamp has not changed. The Domain does not manufacture
its own persistence stamp.

The aggregate exposes `AudienceRules` as a read-only snapshot backed by a private EF collection. EF can rehydrate relationship rows without exposing a caller-mutable collection.

## Native resource governance

`EventResourceSettingDefinitions` registers the `event_resources.*` family in the
native setting registry and configuration-manifest catalog. Every member requires
coordinated mutation. `allow_unscanned_documents` is instance-only; delivery
types, audiences, MIME types, upload bytes, external origins, retention and active
resource capacity permit non-widening tenant overrides. These are native settings,
not environment variables or a separate configuration store.

`EventResourceSettingsWriter` validates the complete proposed instance state
before attaching any changed entities. Tenant value writes are compared against
the proposed instance ceiling before storage clamping; clamping must not disguise
an invalid widening request. Instance tightening does not require rewriting old
tenant choices. Untouched older overrides remain stored but become ineffective
where they exceed the current ceiling. The instance-only unscanned opt-in cannot
be set at tenant scope even when the instance has enabled it.

The relational setting lock expands this family into one ordered group acquired
before opening a transaction. Standalone writes use a serializable unit of work;
manifest application joins its caller-owned transaction and complete outer lock
group. Generic repository/resolver paths cannot bypass the writer. A rejected
batch saves nothing and emits no notifications. Accepted writes return deferred
notifications; callers invalidate caches and dispatch only after commit. Import
notifications travel through the existing durable effect outbox.

`EventResourceGovernancePolicyReader` reads current setting entities rather than
the cached hierarchical resolver. Native instance locks apply in both deployment
modes. It strictly parses the values, intersects tenant choices with instance
ceilings, and applies current storage upload limits and delegation. Malformed
effective policy fails closed. The immutable policy is captured in each authority
snapshot; final authority comparison includes the whole value, including limits
that do not change the provider's immediate boolean decision.

`EventResourceAccessRules` suppresses attendee disclosure when the resource uses
a disabled delivery type or audience. External delivery also requires an allowed
canonical HTTPS origin. Existing resource repair, withdrawal and deletion remain
subject to their normal management authority rather than being disabled by a
tightened delivery policy. Creation requires enabled delivery/audience choices
and positive capacity. Publication additionally requires a governed resource
policy and safe payload; governance alone never establishes file safety.

The Domain policy fixes the supported MIME set to PDF, OOXML Word and OOXML
PowerPoint. Defaults are 10 MiB, unscanned denied, an empty external-origin
allowlist, 30-day audit retention and 500 active resources. Retention is bounded
to 0–90 days and capacity to 0–500. Tenant retention/capacity can only decrease.
See the [operator guide](../public/documentation/readme/administration-and-branding/admin-guide.md#event-resource-governance)
for configuration effects.

## Lookups and audit

`EventResourceKind` contains 13 stable semantic material kinds. `EventResourceDeliveryType` contains only `STORED_FILE` and `EXTERNAL_LINK`. `LookupTableSeeder` repairs missing IDs idempotently; model `HasData` is not used.

`EventResourceAuditEntry` stores only closed action/outcome/reason values, responsible manager identity when retained, and a timestamp. It does not store raw metadata, destinations, or value snapshots. Tenant/resource and tenant/time indexes support bounded history and retention.

## Provider histories

`AddEventResourceProviderActivation` adds a global deployment fence with a UUIDv7
deployment identity, operation ownership, monotonic local epoch and optimistic
concurrency stamp. Its table is intentionally not tenant-owned: aliases used by
different tenants can identify the same remote policy deployment. The generated
histories live in the same four application migration assemblies; MariaDB uses
the MySQL assembly. Apply this migration before configuring remote resource
authority. It introduces no storage bytes or tenant resource governance defaults.

The native binding document and activation state commit together under the
existing setting lock/unit of work. Snapshot reads are no-tracking and operator
recovery starts a new operation; a failed or stale operation cannot reopen the
fence. See [Authorization](AUTHORIZATION.md#resource-policy-deployment-activation)
for the complete publication and convergence protocol. Reversing the additive
activation migration discards its coordination state, so withdraw resource
authority first and prefer forward repair.

`AddEventResources` is generated independently for PostgreSQL, SQLite, SQL Server, and the shared MySQL/MariaDB migration assembly. Generated migrations and snapshots are never hand-edited. SQL Server's generated unique nullable attachment index includes its provider-specific non-null filter; the other engines use their native multiple-NULL uniqueness semantics.

The catalog lookup index remains explicit while the new ticket-type lineage key
is installed; MySQL cannot drop an index still supporting the live catalog FK.
Encrypted destinations use the repository's portable text mapping rather than
contributing a large fixed-width declaration to MySQL's row-size limit.

Rollback is feature withdrawal and forward repair. Reversing a migration after resource content exists is not an accepted data-preservation procedure.
