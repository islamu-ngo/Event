# ADR-034: Root-Path Tenant Resolution

- Status: Accepted
- Date: 2026-09-26

## Context

Multi-tenant browser navigation needs stable, bookmarkable community URLs that
work for a basic self-hosted deployment. Requiring one subdomain per tenant would
also require wildcard DNS and TLS. Resolver configuration therefore needs one
unambiguous representation for an unprefixed tenant path: an empty path prefix.

Root paths share a namespace with Blazor pages, framework resources,
authentication callbacks, API infrastructure, static assets, and names that imply
platform authority. Treating every first segment as a tenant would let a tenant
slug shadow those routes. Browser routing must also preserve the API trust
boundary: public clients cannot be allowed to select tenant context by forwarding
an untrusted privileged header through the BFF.

## Decision

Browser tenant routing defaults to `/{slug}`. In resolver configuration an empty
path prefix means root matching. A configured nonempty prefix remains a supported
choice, for example `/communities/{slug}`. Single-tenant mode continues to use the
configured default tenant.

The BFF resolves the first eligible path segment, establishes the tenant base
path, rewrites the remaining browser path, and translates the resolved route
context into `X-Tenant-Slug` for API calls. It strips a browser-supplied tenant
header before creating that internal header. The API does not parse browser paths:
it resolves a trusted header first, then a mapped custom domain, then a tenant
subdomain, and otherwise fails closed with `404`.
Interactive circuits retain the request's deployment prefix separately from the
tenant-inclusive document base, so later navigation preserves the same tenant.

A case-insensitive Domain-owned reserved-slug catalog protects application roots,
framework and API infrastructure, authentication endpoints, health/metrics and
static-asset paths, and governed names. Tenant create and slug-update validation
reject reserved values. The resolver configuration API carries the catalog to the
BFF so the browser host does not reference backend assemblies; the BFF keeps an
outage-time safety catalog for configuration-read failure.
Root matching also requires the same three-to-five-hundred-character lowercase
alphanumeric and single-hyphen grammar as tenant creation. Configuration files
and fingerprinted static filenames therefore pass through even when their exact
filenames are not enumerated; shipped configuration filenames are reserved in
both catalogs as well.

## Consequences

- A default self-hosted multi-tenant deployment can use one hostname and ordinary
  TLS certificate; wildcard DNS and TLS are not prerequisites.
- Custom domains and tenant subdomains remain optional host-based strategies. A
  trusted route-derived header takes precedence over host matching.
- Adding a Razor page, client route, BFF endpoint, or shipped static asset root
  requires reserving its first segment. Automated route-inventory coverage is deferred to
  [`reserved-slug-page-directive-guardrail.md`](../../../dev/backlog/reserved-slug-page-directive-guardrail.md).
- A future `/{language}/{slug}` shape requires an explicit localization/routing
  design; it must not overload the current matcher implicitly.
- Social anti-squatting and allocation governance remain distinct from the
  technical collision catalog.
- Existing tenant isolation remains downstream of resolution through EF query
  filters and PostgreSQL RLS. This decision changes tenant selection, not data
  authorization.

## Alternatives Rejected

- **Require subdomains:** adds wildcard DNS/TLS operational cost to the default.
- **Require a fixed nonempty path prefix:** makes clean root vanity URLs
  unavailable and gives an empty configuration value misleading semantics.
- **Let the API resolve browser paths:** duplicates presentation routing and
  weakens the BFF-to-API translation boundary.
- **Put backend constants directly in Blazor:** violates the generated-client
  boundary between the browser host and backend implementation assemblies.
