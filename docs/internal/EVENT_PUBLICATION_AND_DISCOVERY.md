# Event Publication And Discovery

Scope: public occurrence selection, governed regional matching, home allocation and card presentation.
Implementation checkpoint: occurrence discovery, unique home allocation and identity correction surfaces; bounded traversal is a subsequent slice.

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

The API assembly cleanup hook currently fails without Docker even on untouched `develop`; retain differential attribution rather than modifying an unrelated fixture. Successful test-body evidence does not turn that project exit into a pass. Desktop/mobile browser QA and the final provider matrix remain distinct gates.

## Primary framework evidence

Research accessed on 2026-10-02; functional constraints only, with no external implementation source or dependencies added.

- [EF Core pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination): fully unique ordering is required; continuation ordering alone does not freeze membership.
- [SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations): native temporal comparison support differs from other providers.
- [EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency): an explicit transaction is a complete retry unit.
- [ASP.NET Core time-limited protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads?view=aspnetcore-10.0): token lifetime does not establish current disclosure authority.

Context7 schemas were discoverable but tool execution returned registered-but-inactive. Official primary documentation was retrieved directly; no Context7 execution is claimed.

## Operator guide

See [publication and sessions](../public/documentation/readme/events-and-ticketing/publication-and-sessions.md) for adopter-facing behavior.
