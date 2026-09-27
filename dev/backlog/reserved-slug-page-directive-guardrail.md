# Reserved-Slug Route Catalog Guardrail

- **Status:** Deferred; architecture guardrail not implemented.
- **Owner:** Blazor and architecture-test maintainers.
- **Decision dependency:** [ADR-034](../../docs/internal/adr/ADR-034-root-path-tenant-resolution.md).

## Problem

Root tenant matching is safe only while every application-owned root route is in
the Domain reserved-slug catalog. Today that relationship is maintained by review.
A new Razor page directive, Blazouter `RouteConfig`, BFF endpoint, or static
asset can introduce a root segment without updating the catalog, allowing a
tenant slug to shadow an application-owned path.

## Proposed Guardrail

Expose the compiled client route table through a testable route inventory and
combine it with compiled endpoint metadata and the machine-consumed static asset
manifest. Assert that each application-owned literal first segment is in
`ReservedTenantSlugs.All`, including routes intentionally under tenant bases.
Classify parameterized and catch-all routes explicitly. Avoid scraping Razor
source text or CSS: the guardrail must test the routes and assets actually shipped.

## Acceptance Boundary

- Adding a client route, BFF endpoint, or shipped asset root without reserving
  its first segment fails a deterministic architecture test.
- The test reports the missing machine-consumed segment, not source prose.
- Parameterized/catch-all routes are classified explicitly and fail closed when
  the test cannot establish ownership.
- Existing reserved entries that are not Razor pages remain valid; brand,
  framework, API, callback, health, and static-asset reservations are not pruned.
- The check is case-insensitive and exercises the compiled catalog authority.

## Non-Goals

Do not scrape raw source files, generate production constants from test-time
inventories, pin prose, or replace runtime reserved-slug checks.
