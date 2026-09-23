---
title: Governed event resources
status: implemented-private-file-intake
last_updated: 2026-09-23
---

# Governed Event Resources

`EventResource` is an independent tenant/event-owned aggregate for governed event materials. Management authors semantic drafts; audience reads expose only currently authorized metadata. Metadata never substitutes for the separate delivery decision.

## P5.A draft-management API

This is an organizer-management contract, not a public material catalogue. The
authenticated, private/no-store routes are:

| Purpose | Route |
| --- | --- |
| Read one management representation | `GET /api/eventresource/{id}/management` |
| List an event's management representations | `GET /api/event/{eventId}/resources/management?page=&pageSize=` |
| Read the bounded management audit | `GET /api/eventresource/{id}/audit?limit=` |
| Create a draft | `POST /api/event/{eventId}/resources` |
| Update draft semantics | `PUT /api/eventresource/{id}` |
| Archive, delete, or the reserved state routes | `POST /api/eventresource/{id}/archive`, `DELETE /api/eventresource/{id}`, `POST /api/eventresource/{id}/publish`, `POST /api/eventresource/{id}/unpublish`, `POST /api/eventresource/{id}/moderate` |

Every management representation is private/no-store, including its HAL form and
audit result. Organizer clients follow only the server-authored HAL links; a
link is an affordance for the current decision, not a durable permission. The
management detail and collection expose `self`, collection, audit, create,
edit, archive, delete, unpublish, and moderation relations only when applicable
and authorized. Stored-file management additionally offers `upload-file` and,
when current policy and inspected ownership permit it, `publish`.

Management pages contain only the requested page number/size, authorized HAL
items and collection actions. They expose no total count, total pages or
count-derived navigation: parent management authority does not authorize counts
of provider-denied resources on other pages. Each read loads at most 100 rows,
including archived history, without materializing the entire event inventory.

Create bodies contain a client-retained UUIDv7 `resourceId`; it is the replay
identity and is not server-derived tenant or subject authority. Update and state
requests supply the representation's `version` as `expectedVersion`; stale
versions conflict, and unauthorized or missing targets are not exposed. Every mutation requires an
`Idempotency-Key`. `RequireIdempotencyKey` and `RevalidateIdempotencyReplay` are
both active: a replay does not reuse an old authorization result, and there is
no suppression bypass for this controller.

P5.A drafts carry only semantic metadata, audience rules, availability intent,
and a governed delivery *type* placeholder. They accept no storage reference,
external destination, encryption material, file input, or other delivery
payload. Delivery is configured through its separate operation. `publish`
rejects incomplete or disallowed files and unconfigured external-link placeholders;
it must not be treated as a way to publish a placeholder.
Archiving is terminal and never publishes content. It is available from draft
or withdrawn state; deletion remains permitted for an archived resource when
the current HAL action is granted.

### Protected external destinations

`PUT /api/eventresource/{id}/destination` accepts a raw link only as
write-only, idempotency-revalidated input from a current resource manager. It
validates absolute HTTPS without userinfo, literal IP or recognized local
hosts, deceptive IDN authority, control/format characters or hostname suffix
matching. The canonical origin must be in the **current intersected** instance
and tenant allow-list; no origin is enabled by default. Only the normalized
origin and a Data Protection envelope are stored. Draft, audit, export,
federation and ordinary audience DTOs never contain raw links or ciphertext.
The manager and an eligible reader can see the safe origin to warn before
navigation; teasers omit it completely. Arbitrarily seeded envelopes must
decrypt and revalidate against that same origin or publication fails.

`EventResourceDestinationProtector` uses the API's existing database-backed
Data Protection keyring and stable `islamu-event` application identity. Its
purpose chain binds the external-destination operation, version, tenant and
resource; another tenant, resource or version cannot decrypt a copied
envelope. Combined hosting reasserts this API database keyring after optional
BFF Redis registration when the API key context is registered; no-database
test/OpenAPI hosts keep their existing BFF key authority. The BFF must not
replace production destination-key authority.
Current authority is checked before unprotecting, and a fresh
single-use header gate precedes the controlled 302. The response carries the
destination only in `Location`, with `Cache-Control: no-store` and
`Referrer-Policy: no-referrer`; the API and BFF never fetch or follow it.
Unavailable keys, tampering, withdrawal and policy changes emit no Location.
Only the **initial** origin is constrained: neither DNS resolution nor later
browser/provider redirects are confined by server-side validation.

Keep retired key material available until dependent resources are withdrawn
or replaced. A backup containing both encrypted rows and unwrapped
Data Protection key XML is not confidential against full backup compromise;
use the deployment's existing key-wrapping authority where configured.

### Browser resource affordances

`EventResources` and `StudioEventResources` use the scoped
`IEventResourceService` over generated per-tag clients, never component-side
role checks or destination readback. The attendee surface loads audience
collection and fresh item details; only a root-relative, exact `download`,
`access` or `accessible-alternative` HAL relation produces a delivery anchor.
The safe origin is plain text, not a preview/fetch target. The Studio
navigation obtains `manage-resources` from the fresh audience collection,
including for a private event whose public parent `resources` link is absent.
Collection `create-resource`/`export` and each item's management relations
gate distinct controls. A mutation rereads the item/version and relation,
uses a generated client with a new idempotency key, and refreshes after a
denial without reflecting server error details. The external-destination
form clears its URL after submission and shows only safe-origin metadata.
Blazor components rely on the service and server authority independently:
HAL hides an affordance; it does not grant a write.

The audit page contains only a closed action/outcome/reason, timestamp, and
retained responsible-manager identity. A successful mutation writes that
minimal audit entry in the same serializable transaction as the resource
change. With retention zero no new entry is written and the hourly cleanup
purges existing entries; otherwise it removes expired complete rows. Subject
erasure clears manager attribution without deleting the shared resource. The
current retention configuration and operational limits remain in the existing
[event-resource governance guide](../public/documentation/readme/administration-and-branding/admin-guide.md#event-resource-governance), rather than being duplicated here.

### Native implementation boundary

The public contract above intentionally omits implementation mechanics. Native
CQS uses closed `ICommand`/`IQuery` requests and handlers rather than generic
CRUD: create, update, publish, unpublish, archive, delete, moderate, detail,
management list, and audit each retain their explicit result/failure boundary.
The authority orchestrator takes serializable A and B snapshots around provider
I/O; B compares the frozen route and complete provider inputs, including the
resource version. Mutations recheck current authority inside their own
serializable transaction, including execution-strategy replay, before the state
and audit change commit.

HAL assembly is asynchronous. Management metadata receives a final
version-bound authorization check after that assembly; this is a decision
boundary, not a guarantee that authority remains fresh through response
completion.

## Audience discovery boundary

`GET /api/event/{eventId}/resources` and `GET /api/eventresource/{id}` use
handler-owned native queries, explicit safe projections and private/no-store HAL.
Audience DTOs never carry management notes, audience-rule objects, resource
versions, backing identities or destination material. Teasers use the public
title with closed availability/requirement values; protected description,
language, accessibility, file metadata and alternative references are omitted.

Discovery evaluates the complete governed active set, capped at 500 resources,
using category-bounded authority reads and provider batches. It returns at most
100 authorized items (20 by default), without total counts or total pages.
Continuation is emitted only after another authorized item is observed. Its
position is the last returned `(SortOrder, Id)`, not a hidden candidate.

The narrow Application cursor port uses native Data Protection in Infrastructure,
with a distinct purpose, query version, tenant/event/subject-or-anonymous/machine
binding and 15-minute expiry. Inputs longer than 2,048 characters are rejected
before unprotection. Invalid, expired or wrong-scope state produces bounded 400
without echoing the token. Every continuation reevaluates current authority;
reordering may require a fresh first page.

The native result retains a non-wire disclosure proof for post-HAL validation.
It binds resource versions, private-versus-teaser disclosure, independently
authorized alternative references and the additional continuation witness.
Final fresh A/provider/B checks cannot reuse an earlier private projection after
an entitlement downgrade, even when no resource stamp changed. Proof state is
neither serialized nor a reusable grant. As with delivery's later boundary,
this does not promise freshness through response completion.

## Authorized metadata portability

`GET /api/event/{eventId}/resources/export?page=&pageSize=` returns bounded
semantic JSON under the distinct native `export` action. The parent event and
every included resource require current export authority; management visibility
alone is not a provider export decision. The management collection advertises
`export` only through its scoped capability policy.

The explicit immutable projection preserves kind, publication state, authorized
public/private metadata (including management notes), audience qualifiers,
relative availability intent and same-event semantic references. It excludes
concurrency stamps, audit history, manager attribution, storage identity,
provider keys, protected envelopes, origins and destinations. A privately owned
file may contribute its display name, MIME type, byte size and safety state.
A separate exact `download` decision controls the optional application-route
reference; export or moderation permission never implies download authority.

Pages default to 20 and are capped at 100. There are no totals or inferred final
page links. Rows are projected in a bounded serializable read, then exact
resource versions and attachment generations are bound to a fresh parent-plus-row A/provider/B export
decision immediately before returning JSON. A stale projection or any denied
row discards the entire prepared page. Cancellation remains caller-owned;
infrastructure failure carries no metadata. This is not bulk ZIP export or
resource import, and the document is not an authorization grant.

## Private file intake and delivery

`POST /api/eventresource/{id}/upload-sessions` accepts the expected resource
version, declared size, closed MIME type, safe display name, extension and
replay identity. The native command delegates to
`EventResourceFileUploadWorkflow`. Existing storage-session finalize/cancel
routes resolve resource ownership server-side and call that same workflow:
they neither require generic-storage create permission nor bypass resource
update authority. Their HTTP replay filters reauthorize committed results.

Reservation retains the resource version. Finalization spools a bounded,
delete-on-close inspection snapshot, persists an inaccessible delete-requested
staging identity, and writes the selected provider outside a transaction.
Fresh authority, version, policy and quota checks then atomically attach the
new object, record the success audit, finalize the session and retire the old
attachment. A failed commit leaves durable cleanup state rather than relying
on in-memory compensation. A replacement does not remove the old attachment
before the new one commits.

`EventResourceDocumentInspection` and `EventResourceDocumentPolicy` accept only
PDF, non-macro DOCX and non-macro PPTX declarations. PDF validation is signature
checking, not active-content or malware scanning. OOXML checks package
structure, CRCs, content types and relationships; it rejects unsupported active,
embedded, encrypted or externally related content and enforces entry, expansion
and XML bounds. This is deliberately a conservative subset, not a scanner.
The immutable object identity and SHA-256 bind an `unscanned` verdict; lifecycle
success never means `clean`. Default governance denies unscanned publication
and access. Only the instance authority can opt into this supported subset.

Generic storage access excludes resource purpose, resource owner discriminator
or any retained resource attachment independently, including malformed tuples.
The exclusion precedes list counts and applies to public-image, presign and
internal readers as well as direct content and mutation endpoints. The uploader
receives no generic access exception. Resource objects remain `PrivateOwner`
even when their audience is public.

`GET /api/eventresource/{id}/content` uses
`EventResourceContentService` for A/provider/private preparation/B and
`EventResourceFileResult` for the final clock/freshness gate immediately before
headers. The response owns the pending lease, so cancelled MVC result execution
also disposes prepared streams. File metadata disclosure binds the full
ownership, inspection and content generation after HAL assembly, not merely a
resource concurrency stamp. Audience `download` links require exact native
download authority; teasers never carry file descriptors.

Responses are full binary attachments with private/no-store, nosniff and
restrictive content policy. There are no resource presigns, conditional 304
responses, ETags, Last-Modified validators or range responses. Binary OpenAPI
media schemas generate `FileResponse`, not JSON MVC-result types. BFF uploads
bind opaque sessions to the current subject and resource; successful browser
completion returns only the resource ID, never a generic storage locator.
Split forwarding and Combined in-process transport use server-held identity;
the Combined integration fixture exercises production BFF login, SQLite and
the real local-file provider.

Combined hosting registers top-level authentication/authorization explicitly
after the cookie bridge so framework auto-insertion cannot reject the request
before trusted enrichment. The bridge validates antiforgery against the actual
cookie principal, then clears that principal for API-token authentication.
HTTP request events use normalized route templates; raw framework
request-start/finish messages are kept below the native logging threshold
because they contain URLs before routing. Warnings and errors remain enabled.
`EventResourceTraceProcessor` in shared ServiceDefaults removes resource and
upload-session identities from matching server and outbound HTTP span URLs
before exporters run. This also covers the separate Split BFF process; native
API middleware alone cannot sanitize a different host's client spans.

The forward `BindEventResourceFiles` migrations add explicit inspection binding
and upload version facts, private-owner constraints and the tenant-qualified
session/object relationship. Their PostgreSQL, SQLite, SQL Server and MySQL
histories are generated artifacts.

### Retirement and producer settlement

`EventResourceStorageLifecycleRepository` serializes activation, producer
acknowledgement and retirement through a conditional source-row fence. Uploads
commit an immutable `StorageProviderBinding` before provider writes. Successful
receipts commit independently of attachment and required-success audit; a late
receipt may settle a matching tombstone after source removal, never recreate a
resource. Content reads use the captured binding and exact object version, which
also participate in delivery generation checks.
Provider resolution/write failures return a closed storage-upload failure without
logging or serializing untrusted provider exception text. Without a confirmed
write receipt, the staged object's producer remains unsettled and its deletion
authority stays non-executable until an exact later acknowledgement.

Retirement transfers deletion authority and settles logical quota once in the
native caller transaction. Heavy moderation retires before overwriting the
storage lifecycle, then saves redaction/detachment and removes transferred
sources. This ordering does not depend on finalized sessions remaining after
subject erasure. Erasure preserves shared live materials; only the erased
subject's exact detached staging objects enter retirement. Evidence-backed
objects and their upload sessions are excluded from both ordinary retirement
and source handoff; parent moderation withdraws their resource affordance
without deleting the independently retained evidence. Expiry conditionally
fences the observed Uploading session before retiring its staged object, so a
concurrent finalization invalidates the entire transaction.

`StorageObjectDeletionTombstone` retains identifiers, the machine key, binding
and version, state, claim stamp and scheduling times, without tenant/resource/
user foreign keys or content/attribution. `AwaitingProducer` is not executable:
expiry, cancellation and elapsed time cannot prove producer settlement.
`Ready` work needs a conditional lease before external deletion. Absence and
retry updates require the matching unexpired fence; a stale worker cannot
complete newer work. Audit-row expiry remains independent.

The existing storage reconciliation job invokes
`EventResourceStorageCleanupService`. Its bounded transactional passes retire
expired resource reservations and remove transferred metadata, including
unknown-producer sources. Claims and terminal purge require source rows to be
absent. Provider I/O stays outside transactions; deletion acknowledgements alone
are insufficient without confirmed absence. Generic reconciliation, image
deletion and inventory cannot take over resource-owned or tombstoned keys.
Dry-run performs no mutation. No second scheduler or outbox is introduced.

Bindings retain original non-secret target coordinates and external secret
references, not credential values or a fallback to current provider settings.
S3 operations address exact versions; delete markers, unavailable buckets and
missing/inaccessible local roots do not establish absence. Unknown receipts or
versions remain pending for operator reconciliation; see the
[operator recovery guidance](../public/documentation/readme/integrations-and-ai/storage.md#resource-deletion-and-provider-recovery).
Only a bound local write may initialize its captured root for the first upload;
read, inventory validation and cleanup cannot recreate a missing old root.

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
