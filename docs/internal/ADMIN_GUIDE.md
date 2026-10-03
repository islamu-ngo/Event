ABOUTME: Task-focused administration guide for instance, tenant, organization, and group operators.
ABOUTME: Maps implemented admin UI surfaces to roles, entry points, dangerous operations, and recovery notes.

# Admin Guide

> **Audience:** Admins | Operators | Contributors
> **Status:** Mixed
> **Owner:** Product/Admin
> **Last Verified:** 2026-07-29
> **Source Anchors:** `Explore.Blazor.Client/Pages/Admin/AdminSettingsPage.razor`, `Explore.Blazor.Client/Pages/Admin/Components/UnifiedAdminSettingsLayout.razor`, `Explore.API/Controllers/InstanceAdminController.cs`, `Explore.API/Controllers/InstanceTenantPlanController.cs`, `Explore.API/Controllers/InstanceTenantConfigurationController.cs`, `Explore.API/Controllers/InstanceTenantLifecycleController.cs`, `Explore.API/Controllers/PlatformMonetizationSettingsController.cs`, `ADMIN_HIERARCHY.md`, `AUTHORIZATION.md`, `AUTHORIZATION_PATTERNS.md`

## Scope

This guide documents implemented administration workflows in the Blazor admin UI. It complements [ADMIN_HIERARCHY.md](ADMIN_HIERARCHY.md), which defines authority boundaries, and [AUTHORIZATION_PATTERNS.md](AUTHORIZATION_PATTERNS.md), which defines enforcement patterns.

Each workflow below states the required role, the UI entry point, and the recovery note for risky changes. If a section is labeled planned or backend-pending, do not treat it as an operator contract.

## Admin Authority Model

| Scope | Typical role | UI entry point | Boundary |
|---|---|---|---|
| Instance | Instance administrator | `/settings/instance` or `/admin/instance`; tenant lifecycle and plans are linked from the unified settings console | Platform-wide settings, tenant lifecycle, domain/admin-host guidance, platform API keys, global provider settings. |
| Tenant | Tenant administrator | `/settings/admin` and tenant admin sub-pages | Tenant policies, public experience, lookup tables, tenant API keys, navigation, footer, templates, custom properties. |
| Organization | Organization administrator | `/settings/organization/{OrganizationId}` | Organization profile, members, verification state, organization API keys. |
| Group | Group administrator | `/settings/group/{GroupId}` | Group profile, branding, members, group API keys. |

Admin pages require authentication. Editability is still checked at runtime by role helpers and admin-state services, so a visible page does not imply write permission.

## Instance Administration

**Required role:** Instance administrator.

**UI entry points:** `/settings/instance` and `/admin/instance` open `AdminSettingsPage`, composed by `UnifiedAdminSettingsLayout`.

The shared router owns every host. A dedicated administration host lands on
`/settings/instance` rather than selecting a second embedded shell. In
SingleTenant mode, the **Administration** console combines instance infrastructure
and the default directory's tenant settings. In MultiTenant mode, **Instance
Administration** contains instance sections, while `/settings/admin` supplies
**Tenant Administration** for the selected directory. Scope comes from current
server authority and deployment state; navigation never grants mutation authority.
Delegated tenant-only administrators retain tenant settings in SingleTenant
mode without receiving instance controls.

Tenant lifecycle and tenant-plan pages remain separate capabilities accessible
from unified administration navigation. Consolidation retains the former
overview's health/provider information, domain guidance, and operational warnings
and actions in the shared console rather than deleting those capabilities.

Instance settings are the platform-control surface for static/default policy. Use them for:

- Governance and render policy defaults.
- Authentication provider configuration.
- Module, domain, branding, and localization settings.
- Provider-neutral storage policy, quotas, delegation, optional S3-compatible settings, and SMTP settings.
- Analytics and privacy settings.
- Versioned platform fee and optional contribution settings. Both default disabled or zero and are available only to instance administrators.
- Footer governance and platform API keys.
- Tenant management and tenant-plan administration are available from the unified
  console in multi-tenant deployments. Their multi-tenant-only operations retain
  `[RequireMultiTenant]`; default-directory preparation in SingleTenant mode
  retains its narrowly authorized tenant-detail and activation capability.

`INSTANCE_ADMIN_PUBLIC_ORIGIN` configures the browser-facing administration
origin in Compose and AppHost; AppHost also forwards it to `Bff:AdminHosts`.
Host classification selects the landing route, not a second renderer.
Instance-admin authorization and HAL checks still decide access and action
availability. Internal administration uses `/api/admin/instance/*`. The separate
`/api/management/*` connector and managed registration protocol remain reserved
for external Event Control Plane fleet software.

### Platform Monetization

Open `/settings/instance` and select **Monetization**. The section manages two independent versioned records:

- the platform fee policy, with an enable switch, integer basis points, and optional fixed charges entered per currency in minor units;
- the optional platform contribution, with an enable switch, DB-stored heading/body text, and ordered basis-point choices whose default must be zero.

The page is read-only unless the API returns the `edit` HAL relation. Saving sends the displayed fee and contribution version numbers together, so a concurrent operator update returns a conflict instead of overwriting a newer revision. Tenant administrators, organizers, and curators cannot use this management surface. Contributions remain separate from organizer earnings and do not add payment capture behavior.

Tenant lifecycle buttons must be driven by HAL `_links`, not by local role guesses. Destructive purge scheduling requires an operator reason and exact tenant-slug confirmation before an archived tenant can move to `Purged`; no tenant data is physically deleted in the request path. Overview and operations warnings include remediation text so operators can see the next corrective action beside the warning.

### Dangerous Operations

| Operation | Risk | Recovery note |
|---|---|---|
| Schedule tenant purge | Moves an archived tenant to the `Purged` lifecycle state after exact slug confirmation and an operator reason. | Confirm backups and export tenant-critical data first. The request records lifecycle audit evidence and does not physically delete tenant data inline; use [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) for restore/rollback planning. |
| Revoke platform API key | Direct callers using the key fail immediately. | Create and distribute a replacement key before revocation when possible. |
| Change auth provider settings | Administrators can lock themselves out if authority/client settings are wrong. | Keep a known-good configuration snapshot from [SECRETS.md](SECRETS.md) and [CONFIGURATION.md](CONFIGURATION.md). |
| Switch Cerbos/local authorization provider | Cerbos mode is fail-closed when the external provider is unavailable. | Switch back to local authorization if Cerbos availability breaks production authorization. |

### Planned Or Backend-Pending Controls

- Webhook administration is planned, not implemented as an operator contract.
- Tenant-members management under the instance surface has UI scaffolding, but backend completion is pending in the explored admin source.

## Tenant Administration

**Required role:** Tenant administrator.

**UI entry points:** `/admin/tenant/settings`, `/admin/tenant/footer`, `/admin/tenant/navigation`, `/admin/tenant/event-templates`, `/admin/tenant/custom-properties`, and `/admin/tenant/custom-property-definitions`.

Tenant administration controls tenant-level policy and public experience:

- Tenant policies and render-policy choices.
- Announcement bar and public-experience settings.
- Organization approval settings.
- Lookup tables for categories, tags, and locations.
- Navigation links and tenant footer.
- Tenant API keys.
- Event templates, template details, and custom property definitions.

### SEO And Public Discovery

There is no standalone SEO administration page. Public discovery is configured through tenant render-policy settings and public-experience settings, including discovery-centric or organization-centric behavior, home-block JSON, call-to-action JSON, and event-section presets.

### Dangerous Operations

| Operation | Risk | Recovery note |
|---|---|---|
| Delete lookup values | Category, tag, or location deletes can remove operator-facing classification data. | Export or document the value before deleting; prefer disabling/hiding if the UI offers a safer path. |
| Change public render policy | Public routes may change render mode or visibility. | Verify public pages and crawler-facing routes after the change. |
| Revoke tenant API key | Integrations for that tenant stop authenticating. | Rotate by creating the replacement key first, then revoke the old key. |
| Edit templates or custom properties | Event creation and downstream projections can change. | Review template sync conflicts and custom-property promotion reports before applying broad changes. |

### Tenant Storage Overrides

Tenant storage settings are implemented under tenant administration when the instance policy allows delegation. Tenant administrators can select allowed providers and upload-size settings only within the instance ceilings; S3-compatible secrets are redacted on reads and preserved unless explicitly replaced. When `governance.lock_tenant_storage` is enabled, the tenant surface is read-only and clients must gate save affordances from HAL `_links` plus the server-provided read-only state.

Tenant-specific SMTP and analytics override controls remain future work in the explored UI. Platform-level settings remain the current operational source for those providers.

## Organization Administration

**Required role:** Organization administrator, or a higher administrator with organization-management permission.

**UI entry point:** `/admin/organization/{OrganizationId}/settings`.

Organization administration covers:

- Organization profile settings.
- Organization members.
- Verification status review.
- Organization-scoped API keys.

Member changes are gated by runtime role helpers. Verification is exposed as status-oriented administration in the explored UI; do not assume arbitrary verification mutation unless the source path explicitly exposes it.

## Group Administration

**Required role:** Group administrator, or a higher administrator with group-management permission.

**UI entry point:** `/admin/group/{GroupId}/settings`.

Group administration covers:

- Group profile and branding.
- Group members.
- Group-scoped API keys.

Member changes are gated by `RoleHelper.CanManageGroup(...)` in the admin UI source.

## Template Administration

**Required role:** Tenant administrator.

**UI entry points:** `/admin/tenant/event-templates`, event-template detail pages, `/admin/events/{eventId}/template-sync`, and `/admin/event-sessions/{sessionId}/template-sync`.

Use template administration to manage event templates and apply template changes to existing events or sessions. Template sync supports diff, apply, and history flows. If the sync base is stale or another update wins the race, reload the diff before applying a new plan.

Dangerous operation: applying a sync plan can update event or session content. Treat the visible diff as the approval record and keep a rollback path through backup/restore procedures for bulk changes.

## Custom Property Administration

**Required role:** Tenant administrator.

**UI entry points:** `/admin/tenant/custom-properties` and `/admin/tenant/custom-property-definitions`.

Custom-property administration includes exposure, search/filter/export governance, moderation/analytics flags, bulk edit, promotion reports, projection rebuild, and definition create/edit/delete flows.

Dangerous operation: deleting or broadly changing a property definition can affect projections and operator-facing forms. Normal delete retires and soft-deletes definitions, options, and values. Permanent purge is a separate admin-only operation for dependency-free definitions; it writes an audit summary and is blocked when historical values, projection rows, audit references, or template-sync provenance exist. Review projection status and drain/rebuild controls before high-volume changes.

## Storage, SMTP, Localization, And Analytics Administration

| Area | Current admin scope | Recovery note |
|---|---|---|
| Storage | Provider-neutral instance storage settings manage local default, optional S3-compatible mode, quotas, max upload, provider health, usage recalculation, and tenant delegation. Tenant storage overrides are available only when delegation is unlocked. | Verify `Storage:Local:*`, non-secret `s3.*` governance, and external-authority bindings against [CONFIGURATION.md](CONFIGURATION.md) and [SECRETS.md](SECRETS.md); confirm database and object-storage backups in [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md). |
| SMTP | Platform SMTP settings are managed from instance administration. | Keep provider credentials in the configured secret provider; test mail after changes. |
| Localization | Instance localization supports offline/emergency controls and bundle export. | Use forced offline mode or reset-to-offline behavior if remote localization becomes unsafe. |
| Analytics/privacy | Instance analytics and privacy settings are platform-level controls. | Verify data sharing and retention expectations before enabling integrations. |

## Related Documentation

- [ADMIN_HIERARCHY.md](ADMIN_HIERARCHY.md) — admin scope and authority boundaries.
- [AUTHORIZATION.md](AUTHORIZATION.md) — authorization model.
- [AUTHORIZATION_PATTERNS.md](AUTHORIZATION_PATTERNS.md) — implementation enforcement patterns.
- [API_COOKBOOK.md](API_COOKBOOK.md) — direct caller and API-key integration guidance.
- [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) — recovery planning for risky operational changes.
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md) — custom-property lifecycle, projection, exposure, and purge boundaries.
