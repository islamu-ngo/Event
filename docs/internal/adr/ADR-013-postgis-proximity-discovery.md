<!-- ABOUTME: Decision record for a separately approved PostGIS proximity-discovery capability. -->
<!-- ABOUTME: Defines governed public points, occurrence eligibility, privacy, indexing, readiness, and fallback. -->

# ADR-013: PostGIS Proximity Discovery

| | |
|---|---|
| **Status** | Proposed — planned, not implemented |
| **Date** | 2026-07-16 |
| **Deciders** | ISLAMU Event Platform — Architecture, Privacy, Product, Operations |
| **Supersedes** | None |
| **Superseded by** | — |

## Context

The current home-discovery release is area-based. A tenant-configured area has a stable ID, display metadata, a coarse public centroid, and tenant-local location mappings. An explicit browser action may compare a transient browser origin with those coarse centroids, but the origin is not sent to the server and the product does not claim exact proximity or distance.

Exact venue coordinates currently belong to `LocationPii`. They must not be added to the generic `LocationListDto`, downloaded to the browser, or silently repurposed as public discovery data. A useful “near you” result also cannot be calculated once per event: an event may have several future sessions at different locations, while draft, private, past, unscheduled, online-only, deleted, or moderated occurrences are ineligible.

The current self-hosted database image is plain PostgreSQL. No PostGIS extension, spatial EF mapping, spatial index, proximity endpoint, or readiness check exists. Exact proximity therefore requires a separately approved operational and privacy capability rather than an approximation inside the area-only release.

## Decision

When separately approved, implement exact proximity with PostgreSQL PostGIS as the sole spatial engine. The capability has three explicit modes:

- `disabled`: no area or proximity discovery;
- `area_only`: current stable-area discovery, with no distance claims;
- `postgis`: area discovery plus server-side exact proximity after readiness succeeds.

There is no generic spatial-provider abstraction, browser-side venue scan, downloaded-point Haversine fallback, or in-memory distance fallback. If PostGIS mode is configured but unavailable, readiness is degraded/unhealthy as defined by the implementation package and the product falls back to honest area-only wording; it never fabricates exact results.

### Governed Public Points

Introduce a tenant-scoped `LocationDiscoveryPoint` only in the separately approved implementation phase. It is a public-discovery projection associated with a tenant-owned `Location`, not a replacement for `LocationPii` and not automatically public because PII coordinates exist.

The point uses `geography(Point,4326)` and is populated only through an explicit approval/backfill workflow. The model records the location/tenant relationship, active public-discovery state, and approval audit evidence. Revoking approval removes the point from discovery without exposing or mutating the PII record.

Generic location DTOs continue to omit exact coordinates. Only an authorized administration surface may manage approval; public responses expose distance and nearest-occurrence metadata, never the stored point or raw venue coordinates.

### Eligible Occurrences And Ranking

A proximity candidate is an eligible future event-session occurrence, not an event row. The query must enforce the normal tenant and public-visibility rules and include only non-deleted, scheduled, published future sessions under public published events whose location has an active governed discovery point. Online-only sessions and locations without an approved point are excluded.

The database first applies `ST_DWithin` to the governed geography point and the transient origin using a bounded radius in metres. It then uses `ST_Distance` to select the minimum eligible occurrence distance per event. The response may include rounded distance, nearest session/location identifiers and safe display name, and nearest occurrence start time; it never includes either point.

Ordering is stable by distance, nearest occurrence start time, then event ID. Pagination/cursor design must preserve that ordering. Tests must cover multi-location events, equal-distance ties, just-inside/on/outside radius, past-session exclusion, online-only exclusion, and tenant isolation.

### API And Origin Privacy

Exact proximity uses a first-party `POST`, because origin data must not enter a URL, shared cache key, referrer, browser history, or access log. The request contains a rounded and validated origin, a bounded radius, and supported discovery filters. It is accepted only after an explicit user action.

The origin is transient request data. It is never persisted in settings, database rows, logs, traces, metrics, analytics, errors, screenshots, or durable application state. Responses are private and `no-store`; shared output caching and ETag replay are disabled for this route. Stored user preference remains an area ID/mode and, if later approved, a bounded radius—never an origin.

### Spatial Index And Query Shape

The implementation migration must enable PostGIS explicitly, add the governed geography column, and create a GiST index that supports `ST_DWithin`. Tenant/location relational indexes remain separate and are applied before or alongside the spatial predicate. The approved query shape must be verified with real PostgreSQL/PostGIS integration tests and `EXPLAIN (ANALYZE, BUFFERS)` evidence at representative volume.

No application loop may load all venue points and calculate distances after materialization. Spatial filtering and minimum-occurrence selection stay in PostgreSQL.

### Readiness And Operations

PostGIS mode is ready only when all of the following are true:

1. the deployed PostgreSQL image/service supports PostGIS;
2. the extension and canonical migration are applied;
3. the governed point table and required GiST/tenant indexes exist;
4. a bounded spatial smoke query succeeds;
5. the configured mode is consistent across API instances.

The future health check exposes only bounded status/failure categories. It must not expose origins, coordinates, addresses, location IDs, tenant IDs, query text, connection strings, or database exception details.

Self-hosting documentation and deployment manifests must name the PostGIS-capable image/version, upgrade/preflight steps, backup/restore behavior, extension verification, index verification, and area-only rollback. Operators take a database backup before enabling the migration or approved-point backfill.

## Consequences

1. The current release remains area-only and cannot use “near you,” distance, or nearest-occurrence wording.
2. `LocationPii` and generic location contracts remain private and unchanged.
3. Exact proximity becomes tenant-safe, occurrence-aware, geodesic, and index-backed when implemented.
4. PostGIS becomes an operational dependency only for deployments that explicitly select `postgis` mode.
5. Nearby responses cannot use shared caching; area-level home responses retain their existing tenant/area/mode cache posture.
6. Phase 6 requires a canonical migration, real PostGIS tests, privacy tests, readiness, and self-hosting updates before product copy changes.

## Alternatives Considered

1. **Expose `LocationPii` coordinates through `LocationListDto`** — rejected because it widens a generic public contract and enables bulk venue-point collection.
2. **Calculate Haversine distance in Blazor or Application memory** — rejected because it requires downloading/materializing points, bypasses spatial indexes, and creates a second semantics path.
3. **Store one coordinate/distance on `Event`** — rejected because proximity belongs to future session occurrences and multi-location events.
4. **Introduce a generic spatial provider with an in-memory fallback** — rejected as speculative abstraction with weaker correctness and failure semantics.
5. **PostGIS with governed public points and area-only fallback** — accepted as the planned capability.

## Activation Gate

This ADR does not authorize implementation. Phase 6 begins only after explicit user/product approval of the geospatial schema, public-point approval workflow, privacy contract, deployment impact, and verification budget. Until then, `area_only` is the only supported discovery mode.

## Related

- Historical planning workstream: `home-discovery-experience` (gitignored local working memory).
- [ARCHITECTURE.md](../ARCHITECTURE.md)
- [DOMAIN.md](../DOMAIN.md)
- [SELF_HOSTING.md](../SELF_HOSTING.md)
- [SECURITY_OVERVIEW.md](../SECURITY_OVERVIEW.md)
