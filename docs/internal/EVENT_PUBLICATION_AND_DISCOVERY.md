# Event Publication And Discovery

Scope: public occurrence selection, governed regional matching, home allocation and card presentation.
Implementation checkpoint: occurrence discovery, unique home allocation, identity correction and bounded traversal. Runtime verification remains recorded separately from implementation.

## Bounded public traversal

`GetEventDiscoveryTraversalQuery` replaces the public offset contract. Its handler
captures ordered canonical membership, not historical cards: source kind/ID,
canonical kind/ID and the exact matching session. Every batch reprojects those
references against current tenant, source, criteria and disclosure authority.
Views can change without changing captured order; a source or authority change
invalidates continuation rather than releasing old fields or counts.

The default bounds are 1000 identities, 15 minutes, 200 live snapshots per tenant,
400000 physical member rows, 10000 examined source rows and 32 combined source
seeks. Expired rows still consume physical capacity. Empty snapshots consume
independently bounded headers. Source exhaustion is distinct from stopping at a
budget; `truncated` is not an exhaustive total.

`EventDiscoveryTraversal` reads initial epochs before its Serializable boundary,
then takes the dedicated native reservation fence before source reads. This
reservation has no foreign key to a public source row. It compares current
identity/disclosure epochs at the terminal fence before inserting snapshot-owned
rows. Mutation writers accumulate affected tenants across saves and advance their
sorted epochs only after source, audit and outbox writes, at transaction commit.
Snapshot and pure view-count writes do not advance disclosure epochs.

The API assembles the complete HAL resource before
`EventDiscoveryResponseAuthority` performs its fresh native release check.
Continuations use purpose-isolated ASP.NET Core Data Protection and bind tenant,
snapshot, canonical criteria digest, ordinal, expiry and both epochs. The existing
configured key ring is shared by replicas; there is no plaintext or stale-cache
fallback. Logical expiry is independent of the purge job.

Ordinary public detail reads also cross this final authority boundary. The client
preserves `409`, `410` and `503` instead of converting them to a missing event.
`EventDetail` conceals the rejected payload, renders localized recovery and only
reloads on an explicit action. A concealed or missing `404` remains not-found.
Recovery uses the page's captured public slug, not subsequently changed ambient
router parameters. Valid public route changes reload the detail and replace its
source action IDs; unrelated routes are ignored and the subscription is disposed.
Each load owns a generation, invalidated by a replacement route or disposal.
After every asynchronous detail, location, session, aspect and agenda read, only
the current generation may publish data, errors, loading flags or persisted state.
Returning to the same slug does not revive an earlier load of that slug.
Post-edit refreshes, agenda callbacks and dialog results retain the same ownership.
Razor captures that ownership when binding callbacks, not when a delayed child
callback enters; a pending replacement read may still hold the prior event ID.
Lifecycle confirmations capture the original event ID and concurrency stamp;
navigation invalidates the confirmation rather than retargeting its command.
Completed obsolete mutations cannot reload or navigate the replacement page.
Event Team status is a native string enum; the explicit HAL schema registration
keeps generated transport aligned with `"Active"` and the assignment affordance.

Snapshot timestamps are rounded down to microseconds before persistence and
cursor protection, including shortened source-bound expiry. PostgreSQL and
MySQL-family precision therefore cannot extend authority or change an authentic
cursor's stored deadline. `event_discovery.*` settings bypass process-local
policy caches so a different replica's downward governance change takes effect.

`EventDiscoveryRank` defines the shared source/merger order. Title keys encode
invariant-uppercase UTF-16 units as fixed-width ASCII hex; source keys encode
original source GUIDs as `N`. Both source models persist these non-wire keys under
portable ordinal ASCII collation. Native ordering, seek predicates and the merger
use the same primary rank and ascending source-kind/source-ID ties, including
descending requests. Local-owned federation cards tie on original `Event.Id`.
Null date ordering is explicit rather than provider-dependent. `EventSort` uses
stable immutable sentinels, so title/views requests cannot fall through to date.

Entity setters maintain keys atomically with identity/title changes. Migration
bootstrap backfills 256-row keysets under migration authority, including suppressed
rows, before readiness; the API does not run a compatibility reader during that
transition. Criteria hashes include the rank-contract version, invalidating older
ordering contracts rather than mixing membership. These keys add no external
dependency, environment secret or public field.

Retention enumerates durable ownership reservations, even after the source
tenant/revision disappears. It does not bootstrap source authority. PostgreSQL
uses a bounded ownership-only maintenance function with a restricted runtime
execution role; it does not remove snapshot tenant filters or FORCE RLS.

See [the authority ADR](adr/ADR-event-discovery-authority.md) for ordering and
[operations](OPERATIONS.md#discovery-snapshot-retention) for bounded cleanup.

## Occurrence authority

An event identifies a program; its sessions identify scheduled occurrences. Public discovery requires a currently eligible public parent and a published session contributing to its public schedule. A published parent alone does not establish a date or regional match.

`EventOccurrenceEligibility` in Domain owns the expression predicates for published sessions, local dates and temporal views. Application attaches an immutable `EventOccurrenceDiscoveryFilter` through `EventQuerySpecification.WithOccurrence`. The filter carries one sampled `TimeProvider` instant and server-resolved location references. Dates, view and regional membership apply to the same session.

Date filters are inclusive local calendar dates, using the session's cached local projection in the event timezone. Finite intervals are half-open: an occurrence ending exactly at midnight does not occupy the following date. Explicit open-ended intervals remain ongoing until closed. A missing end on another end-time kind is not an infinite interval.

With explicit dates and no view, discovery searches all temporal states satisfying those dates. Without dates or a view, it searches current and upcoming occurrences. A requested view further constrains the matching session.

## Persistence and projection

`EventRepository` preserves the named tenant and soft-delete filters and the existing public-parent predicate. The occurrence path performs its count, page selection, matching-session selection and eligible-match counts in a serializable transaction. It reuses a caller transaction; callers must supply a consistent read boundary. Cancellation reaches the database operations.

The repository returns entities. It retains only one selected matching session per event in the read graph and supplies the count of other eligible matches through the transient, nonmapped `Event.DiscoveryAdditionalSessionCount`. `SetDiscoveryOccurrence` rejects incoherent counts and another event's or tenant's session. Application's Mapperly wrapper creates `EventMatchingSessionDto`; the strict generated maps explicitly exclude this transient metadata.

The matching payload contains local schedule fields, UTC instants, the end-time kind's open-ended indicator and, for authorized regional results, public city and country. `AdditionalSessionCount` is an eligible-match count, not the aggregate program's total session count. Missing counts remain unknown. Clients do not substitute aggregate counts.

SQLite's temporal expression adaptation belongs in `EventDirectoryTemporalQuery`, alongside its existing per-connection instant collation. The query remains server-side; provider selection does not enter Application or controllers.

## Governed regional matching

The anonymous API accepts `areaId`, not arbitrary private location references. Application resolves the active area from the tenant's `PublicExperience.DiscoveryAreas`, validates its schema and tenant-owned location references, and supplies those references to the occurrence filter. Unknown, inactive or empty areas produce no matches rather than an unfiltered search. Invalid configuration fails unavailable.

Area policy reads bypass the process settings cache. SQL applies structural public-location constraints; these do not grant disclosure. Application also resolves current location-privacy governance and calls `IEventLocationDisclosureService` for the selected regional occurrences. Both city and country must be present in the evaluator's disclosed-field set before any page or count is returned. An unresolved or suppressed authority fails the response closed; it is not converted into a partial page with an unchanged total.

Country is the existing public location value, not an invented ISO-code conversion. No street address, postcode, coordinates or private owner contact is added to the card payload.

## HTTP and client boundaries

### Home allocation

`GetHomeDiscoveryQueryHandler` owns one response-local `HomeDiscoveryAllocator`. Sections claim canonical keys sequentially in the approved priority order. Local and ATProto namespaces remain distinct, while known local ATProto bindings share one key. A section excludes earlier assignments before its final take and reads ordered next-page batches to refill duplicates.

Refill examines at most 1000 candidates in 10 batches per section. A source-confirmed end permits an empty or short result. Exhausted budget or an empty page contradicting remaining-count metadata is `Failed`, not a claim that no eligible events exist. Partial returned candidates remain reserved, and completed curated sections survive cancellation of a later section.

The 1-second section and 3-second composite limits use `TimeProvider` cancellation timers. One sampled operation instant travels through the allocator's immutable candidate requests into the occurrence filter, including refill pages. HTTP does not accept that trusted `OperationNow` field.

Public home uses `PrivateNoStore`; current `public_experience.*` settings bypass the process cache. Its existing client consumes the assigned sections directly rather than inventing a browser allocation policy. Empty and failed sections retain distinct states.

`GetEventListRequestHandler` reads the current entity graph rather than a cached page of DTOs. `GetEventDetailsRequestHandler` builds a current projection, so a still-public program cannot replay fields removed by a committed redaction. Its fresh tenant and public-parent checks remain in place. Anonymous discovery and public details use `PrivateNoStore`, bypassing shared output-cache and conditional ETag shortcuts. Unavailable discovery returns `503` with `discovery_unavailable` and no-store headers.

The OpenAPI schema is generated from API source, then the existing client generator produces immutable client records. Discovery cards, hero slides and timeline grouping consume the matching occurrence rather than treating the aggregate first date as the regional match. Known positive additional matches may be shown; unknown or zero counts do not become fabricated totals. Source and contributor attribution remains distinct from organizer ownership, with actions gated by HAL links.

## Identity correction surfaces

`EventDiscoveryIdentityController` exposes a no-store HAL status resource at
`GET api/event/{eventId}/discovery-identity`. An optional `candidateEventId` selects
a public review target; it is never an authority claim. The native
`GetEventDiscoveryIdentityQueryHandler` independently checks public eligibility,
current membership, management rights, explicit review/reversal grants and
conflict-of-interest across the affected identity groups. Read-side affordances
do not replace the command's commit-bound reauthorization.

The status resource exposes `canonical` only for an independently public local
primary. An unavailable primary supplies no ID, reason or relationship link,
including on a source owner's request. `original-event` always names the source
event. Revision and bounded decision reason are management-only; the revision
is the tenant-wide identity epoch, not a per-record concurrency stamp.

Candidates retain the flattened `eventId`, `expectedRevision`, `candidates` and
`isBounded` fields inside HAL. Separate detail and collection policies are
registered explicitly. Selecting a candidate reloads the status resource so
`review` and `reverse` reflect that target, rather than inferring authority from
a similarity match or local claims.

`EventDiscoveryIdentityPanel` is shared by full event details and sidebar
previews. Its service delegates to generated clients through the existing BFF
boundary. Review, different-offering and reversal keep the original event route,
selected target and tenant epoch. A `409` preserves input, disables submission,
and requires an explicit refresh and fresh confirmation. The existing correction
report dialog remains available through the original event's `suggest-correction`
link. A saved decision is not represented as a delivered publisher notification;
durable notification delivery is not implemented by this surface.

The canonical page link resolves the existing public event resource through
`GetPublicEventByIdAsync`, then uses `EventUrlHelper.BuildPublicPath` with its slug
and public code. This read never falls back to management detail. Missing public
data or code suppresses navigation rather than inventing a GUID page route.

### Durable publisher correction delivery

`ReviewEventDiscoveryAliasCommandHandler` commits the relationship, tenant epoch,
immutable audit entry and `EventDiscoveryIdentityCorrectionRequested` outbox
message in one caller-owned serializable transaction. A different-offering
decision records audit and delivery intent without changing the identity epoch;
its initial revision may therefore be zero.

`CompositeOutboxMessageDispatcher` routes the discriminator through
`EventDiscoveryIdentityCorrectionDispatcher`, which binds and restores the
payload tenant. The Application notification service resolves current effective
owners and active membership before using the existing recipient materializer.
Each recipient's in-app notification references only their own managed event.
The existing notification graph and outbox retries/dead-letter handling own
delivery; committing a decision does not imply that delivery completed.

Deduplication uses the immutable outbox occurrence, tenant, recipient-owned event
and recipient. Retrying one occurrence preserves its key; separate decisions at
the same nonmutating identity revision retain separate notifications. The safe
payload reference and notification contain no counterpart identity or title.

## Verification checkpoint

Domain tests exercise interval, local-date, day-publication and lifecycle invariants. SQLite integration tests exercise same-occurrence filtering, current parent eligibility, bounded graph projection and exact matching counts. Real SQLite-backed HTTP tests exercise committed restriction followed by conditional discovery, unknown areas and regional disclosure reductions. Component tests exercise matching-date presentation and truthful counts.

The selected discovery HTTP cohorts passed with real SQLite storage and response
execution. Native authority and writer checks passed on PostgreSQL, SQLite, SQL
Server, MariaDB and MySQL; subsequent adversarial repairs also passed the affected
native source-writer and retaining-read authority-planning cases. Ten architecture
failures originally reproduced on the phase3 baseline were incorrectly
quarantined: that baseline already contained this workstream's identity
migration. Their tests assumed a single migration rather than selecting the
unique `Init` migration. The receipt guard assertions are preserved while that
catalog-selection regression is repaired and reverified: target10 and full
architecture654 passed with zero skips, preserving every safety assertion.
Desktop/mobile browser and task-based attendee acceptance remain distinct gates;
the task-owned execution ledger records exact commands, counts and outstanding
observations.

## Primary framework evidence

Research accessed on 2026-10-02; functional constraints only, with no external implementation source or dependencies added.

- [EF Core pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination): fully unique ordering is required; continuation ordering alone does not freeze membership.
- [SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations): native temporal comparison support differs from other providers.
- [EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency): an explicit transaction is a complete retry unit.
- [ASP.NET Core time-limited protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads?view=aspnetcore-10.0): token lifetime does not establish current disclosure authority.

Context7 was initially unavailable. On 2026-10-03 its official EF Core collection
(`/websites/learn_microsoft_en-us_ef_core`) successfully confirmed whole-transaction
execution-strategy replay and unique ordering for seek pagination. Unique ordering
does not by itself freeze membership under mutable rank fields; bounded captured
membership and fresh disclosure checks remain separate project invariants.

## Operator guide

See [publication and sessions](../public/documentation/readme/events-and-ticketing/publication-and-sessions.md) for adopter-facing behavior.
