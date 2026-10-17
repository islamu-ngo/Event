ABOUTME: Footer customization system with admin UI, templates, governance, and CQRS endpoints.
ABOUTME: Covers link groups, social links, templates, instance governance locking, and settings resolution.

# Footer Management

Tenants customize their site footer through an admin interface. Instance admins can lock specific footer aspects to enforce branding consistency across tenants.

## Structured Operator Disclosures

Footer templates remain cosmetic tenant configuration. After template
composition, the public footer renders two server-authored accountability
resources from `PublicExperienceShellDto`:

- **Directory operator**: tenant public/legal name, jurisdiction, optional
  registration identifier, contact, legal notice, optional terms, and privacy;
- **Platform operator**: instance public/legal name, official/independent
  status, jurisdiction, contact, website, legal notice, terms, and privacy.

These roles are intentionally not merged or deduplicated even when facts happen
to match. The public shell is unavailable when the tenant directory identity is
not ready for `PublicDisclosure`; the API returns non-cacheable RFC 7807 `503`
with stable code `tenant_identity_unavailable`. No tenant brand name or prose
directory disclaimer is rendered as a fallback.

## Data Model

### TenantFooterLinkGroup

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `TenantId` | `Guid?` | Owning tenant (null for instance-level) |
| `Title` | `string` | Group heading displayed in footer |
| `Order` | `int` | Display order |
| `IsActive` | `bool` | Visibility toggle |
| `Links` | `ICollection` | Child links (navigation property) |
| Auditing | | `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` |

### TenantFooterLink

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `Guid` | Primary key |
| `FooterLinkGroupId` | `Guid` | Parent group FK |
| `Label` | `string` | Display text |
| `Url` | `string` | Link target |
| `OpenInNewTab` | `bool` | Target behavior |
| `Order` | `int` | Display order within group |
| `IsActive` | `bool` | Visibility toggle |
| Auditing | | `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` |

## Settings

Footer behavior is controlled through the settings system (section: `FooterSettingDefinitions`):

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `footer.enabled` | `bool` | `true` | Show/hide footer |
| `footer.template` | `string` | `standard-3-col` | Active template |
| `footer.show_description` | `bool` | `true` | Show brand description |
| `footer.description_text` | `string` | — | Brand description content |
| `footer.show_social_links` | `bool` | `true` | Show social media icons |
| `footer.social_links` | `JSON` | `[]` | Social platform entries |
| `footer.copyright_text` | `string` | — | Copyright line |
| `footer.show_cookie_settings_link` | `bool` | `true` | Cookie consent link |

### Tenant Settings Contract

Tenant administrators load the scalar settings resource through authenticated `GET /api/footer/settings`, operation ID `GetTenantFooterSettings`, generated method `GetTenantFooterSettingsAsync`. The HAL response includes the resolved scalar values, typed social links, governance lock flags, and the authorized `edit` and `manage-link-groups` capabilities. It intentionally excludes link-group and link entities.

Scalar updates use `PATCH /api/footer/settings`, operation ID `PatchTenantFooterSettings`, generated method `PatchTenantFooterSettingsAsync`. The former `PUT` operation and `UpdateTenantFooterSettingsAsync` client method are removed. This grouped resource does not change exact-key setting operations elsewhere that still use `PUT`.

The patch body contains optional groups with presence-aware leaves:

| Group | Leaves |
|---|---|
| `general` | `enabled`, `showCookieSettingsLink` |
| `template` | `value` |
| `description` | `show`, `text` |
| `socialLinks` | `show`, `items` |
| `copyright` | `text` |

Omitted groups and leaves preserve their stored values. `socialLinks.items` is a typed list of `{ platform, url, label }`, not an unstructured JSON string in the API contract. The server validates the supplied patch before persistence, writes all accepted leaves in one transaction, and invalidates the tenant settings cache once after success.

## Templates

| Template | Layout | Use Case |
|----------|--------|----------|
| `standard-3-col` | Brand (1.5fr) + dynamic link groups (1fr each) | Default, full-featured |
| `standard-2-col` | Brand + single link column | Simpler layouts |
| `minimal` | Single row, horizontal links | Compact footers |
| `community` | Social cards + link groups | Community-focused sites |

All templates receive identical parameters from `Footer.razor` via `PublicExperienceService`. Template selection is a setting, not a code change.

## Social Platforms

Supported platforms with icon mapping via `FooterIconHelper`:

`facebook`, `twitter`, `instagram`, `linkedin`, `youtube`, `tiktok`, `bluesky`, `whatsapp`, `telegram`, `github`

Each maps to the corresponding MudBlazor icon constant.

## Governance (Instance Locking)

Instance admins can lock footer aspects to prevent tenant overrides:

| Lock Setting | What It Prevents |
|-------------|------------------|
| `footer.lock_tenant_template` | Tenants cannot change template |
| `footer.lock_tenant_link_groups` | Tenants cannot modify link groups |
| `footer.lock_tenant_social_links` | Tenants cannot change social links |
| `footer.lock_tenant_description` | Tenants cannot modify brand description |
| `footer.lock_tenant_copyright` | Tenants cannot change copyright text |

Governance is managed via `InstanceFooterGovernanceSection.razor` with toggle controls. In single-tenant mode, an info alert explains that governance has no effect.

Human Footer administration now selects its original ordinary Global or exact
RuntimeTenant owner before handler construction. Annotated Tenant/update policy
is prepared outside BEGIN; the real owner acquires S/P/route/H before business
data and revalidates source-set custody, policy inputs, credential lifetime and
the original support horizon before commit. Lower link/group and scalar writers
require the private active Footer producer, not an actor ID or bare transaction.
The existing setup-authorized governance GET reauthenticates the actual setup
secret before result release and never grants setup writes. Public rendering and
HAL affordance gating retain their existing roles. These source changes and
regression controls are not final executable/provider acceptance.

The scalar PATCH resolves current lock state on every request. Requested `template`, `description`, `socialLinks`, or `copyright` leaves that are locked by instance governance are preserved rather than overwritten; unlocked requested leaves can still be written in the same patch. The `general` leaves aren't covered by those scalar locks. HAL remains the UI authority, but direct API calls do not bypass server lock handling.

Link management has a separate boundary. The tenant settings resource includes `manage-link-groups` only when effective link-group governance is unlocked and the caller has tenant update authority. Every create, update, delete, and reorder command also calls `FooterLinkMutationGuard`, so constructing a link mutation URL directly cannot bypass the effective lock. That guard returns an authorization failure in multi-tenant mode and bypasses the instance lock in single-tenant mode, matching the platform governance rule.

## API Endpoints

### FooterController (12 endpoints)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/footer/config` | Anonymous | Public footer configuration |
| GET | `/api/footer/link-groups` | Authorize | List all link groups |
| GET | `/api/footer/link-groups/{id}` | Authorize | Get link group details |
| POST | `/api/footer/link-groups` | Authorize | Create link group |
| PATCH | `/api/footer/link-groups/{id}` | Authorize | Update supplied link-group fields |
| DELETE | `/api/footer/link-groups/{id}` | Authorize | Delete link group |
| POST | `/api/footer/link-groups/reorder` | Authorize | Reorder link groups |
| POST | `/api/footer/link-groups/{groupId}/links` | Authorize | Create link in group |
| PATCH | `/api/footer/links/{id}` | Authorize | Update supplied link fields |
| DELETE | `/api/footer/links/{id}` | Authorize | Delete link |
| GET | `/api/footer/settings` | Authorize | Read scalar settings, lock state, and HAL capabilities |
| PATCH | `/api/footer/settings` | Authorize | Patch supplied scalar setting groups and leaves |

`GET /api/footer/config` remains the anonymous public rendering contract. The authenticated settings GET and PATCH are admin contracts. Link group and link create/delete operations, grouped link/link-group PATCH operations, and `POST /api/footer/link-groups/reorder` remain explicit; they are not folded into the scalar settings patch.

## CQRS Structure

| Type | Name | Description |
|------|------|-------------|
| Query | `GetFooterConfig` | Public config for rendering |
| Query | `GetLinkGroupList` | Admin link group listing |
| Query | `GetLinkGroupDetails` | Single group with links |
| Query | `GetGovernanceSettings` | Instance lock states |
| Query | `GetTenantFooterSettings` | Authenticated scalar settings, locks, and HAL source data |
| Command | `CreateLinkGroup` | New link group |
| Command | `UpdateLinkGroup` | Modify link group |
| Command | `DeleteLinkGroup` | Remove link group |
| Command | `ReorderLinkGroups` | Batch reorder |
| Command | `CreateLink` | New link in group |
| Command | `UpdateLink` | Modify link |
| Command | `DeleteLink` | Remove link |
| Command | `PatchTenantFooterSettings` | Presence-aware tenant footer scalar settings patch |
| Command | `UpdateGovernanceSettings` | Instance governance locks |

## Admin UI

The tenant footer section at `/admin/tenant/settings?section=footer` provides:

- **General Settings**: enabled and cookie-settings toggles, plus template selection
- **Description and Copyright**: scalar text and visibility controls
- **Social Links**: typed platform, URL, and accessible-label entries
- **Link Groups and Group Links**: explicit create, edit, delete, and reorder actions when `manage-link-groups` is present

Dialogs: `FooterLinkDialog`, `FooterLinkGroupDialog`.

Autosave sends only the affected scalar group. Discrete toggles and template selection save immediately. Description, copyright, and social-link text wait 400 ms after typing and flush on blur. Saving, success, and failure messages are exposed through polite `role="status"` feedback, while load failures use an assertive alert. Link CRUD and reorder keep their explicit command boundaries. This records implemented behavior and does not claim browser visual QA.

## Related

- [API.md](API.md) — endpoint conventions and middleware
- [BLAZOR.md](BLAZOR.md) — component architecture
- [ADR-005](adr/ADR-005-footer-customization.md) — architectural decision record
- [DOMAIN.md](DOMAIN.md) — entity definitions
