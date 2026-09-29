# Tenant Routing Knowledge Ledger

> **Scope:** Browser path tenant resolution, BFF/API translation, host fallback,
> and tenant-slug namespace safety.

## Durable Decisions

- Browser multi-tenant navigation defaults to `/{slug}`. An empty configured path
  prefix means root matching. Operators may configure a nonempty prefix when their
  deployment requires one.
- The BFF owns browser path interpretation. It establishes the tenant route base,
  rewrites the remaining path, and sends the result to the API as trusted
  `X-Tenant-Slug`.
- The rendered document base includes the tenant segment. A Blazor circuit must
  remove only the deployment prefix captured from the original request before
  matching a navigation URL; stripping the entire document base loses tenant
  context during interactive activity.
- The API resolution order is trusted tenant header, custom domain, subdomain,
  then fail-closed `404`. It does not repeat browser path parsing.
- Root matching requires a case-insensitive reserved-slug catalog. Tenant writes
  reject protected application, framework, authentication, infrastructure,
  static-asset, and governed-name segments.
- Root matching also enforces tenant slug grammar before selecting a tenant.
  Dotted configuration and fingerprinted asset filenames cannot be interpreted
  as tenant slugs even when a new filename is absent from the reserved catalog.

## 2026-09-26 - Empty prefixes are routing policy

**Context:** Root vanity routing uses an empty `PathPrefix` to represent the
absence of a path prefix across settings, transport, BFF fallback, middleware,
and circuit navigation.

**Finding:** Normalization must not replace an explicit empty value with an
implementation-selected prefix. Doing so would introduce a second source of
routing policy and make the configured URL shape inaccurate.

**Resolution:** Empty means no prefix and therefore root matching. Normalization
may add a leading slash and remove a trailing slash only for a nonempty configured
prefix. Preserve that meaning in DTO defaults, persistence/configuration
normalization, BFF fallback configuration, middleware, and circuit navigation.

**Why this matters:** Configuration semantics must survive end to end. A fallback
must not change the public URL contract. Safety during resolver-config failure
comes from the reserved slug set and authoritative slug grammar, not from
substituting another URL shape.

**References:**

- [`TenantRoutePathMatcher`](../../../src/Explore.Blazor/Services/TenantRoutePathMatcher.cs)
- [`ResolverConfigService`](../../../src/Explore.Infrastructure/Services/ResolverConfigService.cs)
- [`ADR-034`](../../../docs/internal/adr/ADR-034-root-path-tenant-resolution.md)

**Promoted ->** `docs/internal/QUICK_REFERENCE.md` Multi-Tenancy Reminder and
`docs/internal/adr/ADR-034-root-path-tenant-resolution.md` (2026-09-26).
