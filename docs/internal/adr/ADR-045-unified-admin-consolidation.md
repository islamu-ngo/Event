# ADR-045: Unified Administration and External Fleet Separation

- Status: Accepted
- Date: 2026-10-02

## Context

The embedded Event Control Plane shell duplicated routing, diagnostics, and
administrative navigation already served by the settings console. Its naming
also conflicted with the separate Event Control Plane fleet application.
`InstanceAdminSettingsLayout` was misleading because SingleTenant deployments
use that component for both instance infrastructure and directory settings.

Consolidation must preserve capabilities: tenant lifecycle and tenant-plan
administration remain reachable, and overview, domain, and operational
information and actions move into the unified console before duplicate pages
are removed.

## Decision

`AdminSettingsPage` and `UnifiedAdminSettingsLayout` own the shared administration
surface. Every host uses the existing router. Dedicated administration hosts
select the instance-settings landing route instead of another application shell.
SingleTenant mode presents Administration with instance and tenant sections;
MultiTenant instance settings present Instance Administration, while selected
tenant settings present Tenant Administration.
Tenant-only delegated authority continues to select the tenant layout in either
mode; a route selection never expands the user's instance authority.

Internal administration controllers and Application contracts use instance
terminology and `/api/admin/instance/*`. Existing native command/query handlers,
validation, tenant boundaries, concurrency checks, and HAL authorization remain
the behavior owners. Browser contracts come from regenerated OpenAPI, not
handwritten copies of Application DTOs. Client affordances follow current HAL
links; navigation and host classification never confer authorization.

`INSTANCE_ADMIN_PUBLIC_ORIGIN` is the administration origin configuration key,
forwarded through AppHost/Compose and represented in the setup environment
catalogue. There is no fallback to the retired variable.

`/api/management/*`, managed registration, and fleet credentials continue to
serve external Event Control Plane software. Their names and protocol are not
part of the internal administration rename.

## Consequences

- One renderer and navigation surface removes duplicate administration shells.
- Diagnostics and remediation remain available alongside settings.
- Tenant lifecycle and plans remain distinct pages linked from the console.
- Internal API paths, operation names, and origin configuration are breaking
  development changes; callers must use regenerated contracts and current keys.
- No database migration or tenant-authority expansion is required.
- Recovery is a forward fix or deployment of the preceding coherent application
  revision, not a compatibility adapter or database rollback.

## Alternatives Rejected

- Keeping two shells would preserve duplicate navigation and host-specific drift.
- Deleting duplicate pages without comparing capabilities would lose operator
  information and actions.
- Renaming external fleet integration would conflate two independent scopes.
- Retaining obsolete route/key aliases would leave the naming ambiguity intact.
