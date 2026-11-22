# ADR-021: Instance-Scoped HAL Authorization Without Tenant Resolution

- Status: Accepted
- Date: 2026-11-20

## Context

Instance-management actions under `/api/admin/instance/*`, `/api/instance/settings/*`, and the
instance onboarding journey belong to the installation rather than to a tenant. They are marked
with the method-level `[InstanceManagement]` attribute, and the tenant-resolution, tenant-lifecycle,
and visitor-read middleware deliberately exempt those exact actions. Requiring a tenant slug or a
synthetic default tenant for them would be both incorrect and a route to authority confusion.

HAL resources expose the same actions as permission-bound links. The HAL evaluator batches those
checks and the local authorization provider historically evaluated batches of three or more through
a tenant authority profile. That profile read `ITenantContext.TenantId`. On a MultiTenant
instance-management request no tenant is resolved by design, so the read failed. The evaluator's
fail-closed policy then removed every permission-bound link, including unrelated authentication and
authorization links. The endpoint itself was authorized correctly; only the affordances disappeared.

The failure mode was misleading because two-link batches used the per-check path and passed. Adding
the third MultiTenant `manage-tenants` link changed the execution path, not the caller's authority.

## Decision

Instance-scoped authorization never requires a tenant.

1. A batch containing only `ResourceKinds.InstanceSetting` checks or tenant creation checks is
   evaluated per check through the instance-authority path. It does not build a tenant profile;
   tenant creation is permitted before a tenant row exists.
   The runtime provider's selection contract is unchanged: tenant-creation requests retain their
   existing tenant-specific Cerbos routing, and Local never substitutes for a configured PDP.
2. A batch presented to the local fallback provider that combines instance settings or tenant
   creation with other checks requires a nonempty resolved tenant. If resolution returns an empty
   identifier, that provider returns denied decisions for the batch regardless of its size or
   special resource types. The runtime provider may split dedicated-resource and discovery checks
   before delegation; those checks retain their own independent authority policies.
3. The HAL evaluator keeps its fail-closed behavior for genuine provider failures. The fix restores
   the authorization decision; it does not suppress failed checks or widen a link's permission.
4. `[InstanceManagement]` remains method-scoped. Controller-wide exemptions and invented tenant
   fallbacks remain forbidden.

## Consequences

- An instance administrator sees control-plane links without sending `X-Tenant-Slug`, including on
  MultiTenant installations where `manage-tenants` makes the batch larger than two checks.
- Tenant-scoped links still require a resolved tenant; an unresolved tenant never grants tenant
  authority.
- The authorization provider's batch behavior is now explicit and testable. The regression test
  `InstanceScopedBatchAuthorizationTests.InstanceScopedBatchDoesNotRequireTenantResolution` exercises
  a three-link instance batch with a tenant context that throws if read.
- The integration fixture must advertise the same effective host it sends requests to; otherwise
  effective-host admission rejects the request before authentication or HAL evaluation.
