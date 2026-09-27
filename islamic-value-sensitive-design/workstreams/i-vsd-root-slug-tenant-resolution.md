# I-VSD Report — Root Slug Tenant Resolution

Last Updated: 2026-09-26 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: Root path-based tenant slug resolution (`/{slug}`)
- Workstream: root-slug-tenant-resolution
- Report kind: feature-compliance
- Report status: draft
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-26
- Reviewed input: root-slug-tenant-resolution workstream (planning)
- Supersedes: none

## Scope

This report evaluates the provider-controlled responsibilities when changing the default tenant URL scheme from a prefixed path (`/t/{slug}`) to a root vanity path (`/{slug}`), adding a reserved slug blacklist, and making path-based resolution the default for self-hosted deployments.

## Claim Boundary

This is a provider-responsibility design review, not a fatwa or Sharia certification. It addresses the platform operator's duty of care in URL namespace design, path collision prevention, and equitable tenant slug availability. It does not rule on the permissibility of specific tenant names or content.

## Findings

### IVSD-F001: Slug Namespace Collision Could Cause Service Denial
- **Lifecycle**: open
- **Severity**: medium
- **Principle**: Amanah (trustworthiness) — provider must ensure a tenant cannot claim a slug that hijacks system functionality
- **Domain**: Architecture, UX
- **Stakeholder**: Tenant administrators, end users, instance operators
- **Provider decision**: Define and enforce a reserved slug catalog at registration time
- **Evidence**: Current `CreateTenantDtoValidator` and `UpdateTenantSlugDtoValidator` lack reserved slug validation (verified: `src/Explore.Application/DTOs/Tenant/Validators/`)
- **Linked mitigation**: IVSD-M001

### IVSD-F002: Equitable Slug Availability and Squatting Prevention
- **Lifecycle**: open
- **Severity**: low
- **Principle**: Adl (justice) — slug namespace should not permit first-mover squatting of valuable community names
- **Domain**: Governance, UX
- **Stakeholder**: Late-registering communities, instance operators
- **Provider decision**: Reserved slug list includes brand-safety terms; broader anti-squatting policy is a governance concern deferred to instance operators
- **Evidence**: No anti-squatting mechanism exists today
- **Linked mitigation**: IVSD-M002

### IVSD-F003: Self-Hoster Path Accessibility
- **Lifecycle**: open
- **Severity**: medium
- **Principle**: Ihsan (excellence in service) — self-hosters on localhost/IP/free DNS should have clean, functional tenant URLs without requiring wildcard DNS or TLS
- **Domain**: Self-hosting, Deployment
- **Stakeholder**: Self-hosters, small communities
- **Provider decision**: Default to path-based resolution so self-hosters work out-of-the-box
- **Evidence**: Current `RoutingSettingDefinitions.ResolverPathEnabled` defaults to `false`; `BffResolverConfigurationProvider.CreateFallback()` defaults to `/t` prefix
- **Linked mitigation**: IVSD-M003

## Recommendations

1. **Implement reserved slug catalog** (IVSD-M001) as a Domain-layer constant list, enforced in both Create and Update validators. This is the minimum viable safeguard.
2. **Default path-based resolution to enabled** (IVSD-M003) with empty path prefix for root matching, giving self-hosters a zero-configuration multi-tenant experience.
3. **Defer anti-squatting governance** (IVSD-M002) to instance operators via admin settings; platform provides the reserved catalog as baseline protection only.

## Mitigations

### IVSD-M001: Reserved Slug Catalog
Maintain a canonical `ReservedTenantSlugs` set in `Explore.Domain.Constants`. Enforce in `CreateTenantDtoValidator` and `UpdateTenantSlugDtoValidator`. Include all Blazor route root segments, framework paths, API prefixes, auth paths, and brand-safety terms. Minimum slug length of 3 characters prevents single/double-letter collisions.

### IVSD-M002: Anti-Squatting Governance (Deferred)
This is an instance-operator governance concern. The platform provides the reserved catalog as baseline protection. Broader anti-squatting policies (e.g., proof of community affiliation) are deferred to the instance operator's admin settings and are not in scope for this workstream.

### IVSD-M003: Self-Hoster Default Path Resolution
Change `RoutingSettingDefinitions.ResolverPathEnabled` default to `true` and `PathPrefix` default to empty string. Update `BffResolverConfigurationProvider.CreateFallback()` to match.

## Stakeholders

- **Tenant administrators**: URL shape determines shareability, bookmarkability, and brand identity
- **End users**: Must reliably reach tenant pages via shared links and bookmarks
- **Instance operators**: Must understand and control the routing strategy
- **Self-hosters**: Must have working multi-tenant URLs without DNS/TLS complexity

## I-VSD Principles And Domains

| Principle | Applicable | Rationale |
|---|---|---|
| Amanah (trustworthiness) | Yes | Slug collision prevention is a trust obligation |
| Adl (justice) | Yes | Equitable namespace access |
| Ihsan (excellence) | Yes | Self-hoster accessibility |
| Hifz al-mal (wealth protection) | Not applicable | No monetary impact |
| Privacy / data minimization | Not applicable | URL slugs are public tenant identifiers |

## Validation Gaps

- No existing test validates that reserved slugs are rejected at tenant creation time.
- No test validates root-level path matching without a prefix.

## Escalation Needed

None. No scholarly or legal determination required — this is routing infrastructure.

## Evidence Reviewed

- `src/Explore.Application/DTOs/Tenant/Validators/CreateTenantDtoValidator.cs`
- `src/Explore.Application/DTOs/Tenant/Validators/UpdateTenantDtoValidator.cs`
- `src/Explore.Blazor/Services/TenantRoutePathMatcher.cs`
- `src/Explore.Blazor/Middleware/PathTenantResolverMiddleware.cs`
- `src/Explore.Blazor/Services/BffResolverConfigurationProvider.cs`
- `src/Explore.Domain/Settings/Definitions/RoutingSettingDefinitions.cs`
- `src/Explore.Application/DTOs/Onboarding/ResolverConfigurationDto.cs`
- All Blazor `@page` route directives (22 unique root segments)

## Missing Evidence

- Production slug collision data (not applicable: zero users, greenfield)

## Context Inventory

| Item | Status |
|---|---|
| Reserved slug catalog | Not implemented — IVSD-M001 |
| Root path matching | Partially implemented (`TenantRoutePathMatcher` exists but defaults to `/t` prefix) |
| Self-hoster defaults | Incorrect defaults — IVSD-M003 |

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-26 | — | draft | Workstream created | Initial planning evidence |

## Planning Handoff
- Workstream: root-slug-tenant-resolution
- Status: draft
- Reviewed input: root-slug-tenant-resolution workstream (planning)
- Findings and mitigations: IVSD-F001 -> IVSD-M001, IVSD-F002 -> IVSD-M002 (deferred), IVSD-F003 -> IVSD-M003
- Required plan mappings: IVSD-M001 -> Phase 1 (reserved slug catalog + validator enforcement), IVSD-M002 -> Deferred (explicit non-applicability), IVSD-M003 -> Phase 2 (default configuration changes)
- Escalations required before: none
- Refresh triggers: change to slug validation rules, routing topology, or self-hosting deployment model
