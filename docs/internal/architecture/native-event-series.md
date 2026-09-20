# EventSeries native operations and disclosure

> **Scope:** EventSeries only; not the whole mapping/CQS migration.
> **Source anchors:** `EventSeriesController`, `UpdateEventSeriesAuthorizationContextEnricher`, `EventSeriesRepository`, `EventSeriesDisclosureHttpTests`.

## Exact boundary

The controller injects six closed native ports and its HAL assembler: create and update return `BaseCommandResponse<Guid>`, delete returns `BaseCommandResponse<bool>`, detail and top return nullable `EventSeriesDto`, and list returns `PaginatedResult<EventSeriesListDto>`. Requests use `ICommand<T>`/`IQuery<T>` with no MediatR marker or adapter. Application discovery composes authorization outside performance timing outside each handler. Route names, verbs, HAL contracts, and public DTO shapes are unchanged.

Create and delete retain handler-owned authorization through `IAdminContext.ResolveUserIdAsync` and persisted active current-tenant admin membership. Update retains the selected provider's `Actor:update` capability; Local policy currently requires tenant administration. These are deliberately different authority mechanisms, not a new uniform grant. Being the Series actor's user alone does not grant Local writes.

The feature-owned update enricher loads the Series in the current tenant and supplies its persisted Actor ID and tenant as typed facts. Its single additive registration is in `ApplicationServicesRegistration`. Missing, deleted, or foreign Series fail before the provider call. The immutable command carries the target Series ID, expected revision, and patch only; obsolete ActorId/TenantId authority hints are removed. The controller no longer reads anonymous detail to construct a write, so unpublished/private Series remain editable without making their reads public.

The handler reloads through `GetForUpdateAsync`, independent of EF's identity map and public publication filters. The current revision must match `If-Match`; a committed ownership change also changes that revision, so a policy decision about an older owner cannot authorize a subsequent mutation. EF's concurrency predicate decides simultaneous writes. The repository translates only `DbUpdateConcurrencyException` to the existing concurrency exception/HTTP 409, preserving the cause; unrelated failures propagate.

## Public disclosure and top ranking

Public Series reads select published, public-visibility, non-deleted rows in the current tenant. All nested-event includes and list counts use IDs from the existing `WherePubliclyEligible` query, retaining its publication, visibility, actor, active tenant participation, and federation-presentation rules. No reduced predicate is duplicated in a mapper or controller. Queries are no-tracking with isolated identity resolution, so a management graph already tracked in the scope cannot add hidden children to a public result.

Top selects only Series with eligible upcoming/ongoing or undated events. The existing `EventDirectoryTemporalQuery` handles precise instant comparison, including SQLite's offset-aware SQL collation; the Series repository contains no provider branch. Eligible undated IDs are combined with that query. SQL performs candidate selection, event counting, ranking, and limiting; ties use views then ID. End exactly equal to the clock is past; one tick later remains eligible, irrespective of stored offset. The handler samples injected `TimeProvider` once. Detail still includes eligible past events, unlike top.

## Writes and caches

Manual validators and `ImageReferenceEligibility` retain active current-tenant public safe-raster requirements. Grouped immutable PATCH semantics distinguish omitted, explicit clear, and replacement fields. Invalid patches/images and stale revisions do not mutate persisted state.

Successful update preserves invalidation of each related `event:detail:{id}` key and the current tenant's event-list tag. Rejected update does not evict these entries, and another tenant's list remains cached. Series read operations themselves are not cached. No speculative create/delete cache redesign or shared event-read change accompanies this slice. Legacy generic create/update/delete and image repository APIs remain tokenless; Series-specific read APIs now accept cancellation.

## Evidence and limits

The original five-case Red HTTP probe demonstrated anonymous draft/private detail disclosure, hidden nested detail/count disclosure, and SQLite top translation failure. Expanded real SQLite HTTP tests additionally demonstrated the concurrent PATCH loser incorrectly surfacing as an unhandled persistence error. The fixes are exercised through the final registered native ports and actual TestServer routes, not direct handler mocks or test-only registrations.

The focused corpus covers six-port/controller closure, public and private reads, foreign/deleted graphs, stale identity-map children, live admin membership, draft/private editing, published transition, image eligibility, immutable patches, tenant-scoped cache behavior, provider typed facts/denial/outage, and deterministic two-writer and ownership-during-policy barriers. Exact temporal boundaries use an injected clock. Provider transport failure tests substitute only the external authorization boundary; Local authority uses persisted membership and the real Local provider. Pure mapping/patch invariants run in Application tests, and the existing repository pricing graph now seeds eligible active participation instead of bypassing tenant filters.

Verification is single canonical SQLite, not a full database or authorization-provider matrix. No schema, migration, policy grant, generated reader, or unrelated mapper change is required.

Operator guide: [Event Series management](../../public/features/event-series-management.md).
