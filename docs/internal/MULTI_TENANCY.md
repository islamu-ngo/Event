ABOUTME: Explains tenant resolution, isolation, and override behavior implemented in code.
ABOUTME: Prioritizes runtime rules from TenantContext, query filters, and governance services.

# Multi-Tenancy

## Tenant Directory-Operator Identity

Every tenant has a canonical typed directory-operator identity document,
`tenant.directory-operator-identity`. Tenant creation writes the tenant,
branding document, and directory identity document in the caller-owned
transaction. Active creation requires explicit complete identity input before
any write; draft creation may persist an incomplete draft but cannot activate.

Readiness is capability-specific:

- `Activation` gates initial activation and every later reactivation;
- `PublicDisclosure` gates anonymous settings and shell composition;
- `PaidCommerce` gates paid publication and Checkout activation.

Evaluation is exact-tenant and fail closed. Missing, malformed, cross-tenant, or
incomplete identity returns stable payload-free reason codes; no branding,
instance identity, or scalar setting is used as a substitute. Tenant onboarding
marks its Identity step complete only from the readiness evaluator.

Authenticated administrators read and patch the document through its dedicated
CQRS/API resource. Optimistic concurrency uses the document revision, and the
Blazor Save action is rendered only when the exact HAL `_links.edit` relation
with `PATCH` is present.

## Deployment Modes

First-run onboarding mode:

- API configuration key `Deployment:Mode`, normally mapped from Infisical `/api` secret `DEPLOYMENT_MODE`.
- `DEPLOYMENT_MODE=multi_tenant` shows the multi-tenant onboarding flow.
- absent `DEPLOYMENT_MODE` shows the single-tenant onboarding flow only.

After onboarding, runtime mode is operator-controlled. Normal instance-admin UI cannot switch deployment mode; launch multi-tenant mode by setting `DEPLOYMENT_MODE=multi_tenant` before first-run onboarding or by following an explicit operator migration runbook.

Modes:

- `SingleTenant`
- `MultiTenant`

In single-tenant mode, tenant context always resolves to default tenant ID.

## Tenant Resolution Order

Standard runtime authority lives in `Explore.API.Middleware.ApiTenantResolutionMiddleware`.

In multi-tenant mode, the API resolves tenant in this strict order:

1. trusted `X-Tenant-Slug` header forwarded by the BFF,
2. configured admin-host exclusion before tenant host matching,
3. custom-domain lookup (`domains.tenant_custom_domain`) if custom domains are allowed,
4. subdomain lookup (`domains.tenant_subdomain`),
5. unresolved request fails closed with `404`.

In single-tenant mode, tenant context always resolves to the configured default tenant ID.

Request host source:

- uses normalized `Request.Host.Host` after trusted forwarded-header processing.

Admin-host classification is deployment-static BFF configuration, not tenant routing state. `Bff:AdminHosts` accepts exact non-wildcard admin host/origin values; those hosts are skipped before custom-domain and subdomain tenant resolution so an admin host such as `admin.example.org` is not treated as tenant slug `admin` or as a tenant custom domain. `Bff:AdminHostAllowedIpRanges` can additionally restrict configured admin hosts by exact IP/CIDR and fails closed with `403` when the remote IP is missing or outside the allowlist. Onboarding may persist the operator-entered dedicated host in `domains.admin_host` for review and DNS guidance, but runtime host classification still comes from BFF config.

For standard authenticated-browser requests, the middleware binds the resolved tenant into the request-scoped tenant context accessor immediately.

For API-key requests, `ApiTenantResolutionMiddleware` can store a requested tenant hint in `HttpContext.Items["__requested_tenant_id"]` and defer final tenant binding to `ApiTenantPostAuthenticationMiddleware` after authentication succeeds. Tenant-bound API keys bind their persisted tenant when no mismatch exists. `InstanceAdmin` API keys are nullable credentials, but tenant-scoped API and MCP requests still require an execution tenant: an explicit tenant hint binds that tenant, single-tenant mode binds the configured default tenant, and unresolved tenant-scoped API/MCP requests fail closed with `404` and `code=tenant_required`. Only explicit host-administration API routes may continue without tenant context.

## Default Tenant Fallback

Fallback default tenant ID in single-tenant runtime is:

- `018e4e5c-7f00-7000-8000-000000000001`

Used when no configured default exists.

## Domain and Subdomain Rules

Subdomain extraction behavior:

- normalizes to lowercase alphanumeric/hyphen,
- ignores common prefixes: `www`, `api`, `app`, `admin`, `localhost`,
- requires active tenant status for resolved tenant IDs.

Custom/subdomain values are stored as JSON-serialized strings in `TenantSetting.Value`.

The multi-tenant Instance Console (`/admin/instance`, `/admin/instance/tenants`, `/admin/instance/domains`) and `Explore.API` control-plane endpoints are multi-tenant-only. In single-tenant mode, the Blazor route guard redirects the control-plane console back to tenant/instance settings surfaces, while API endpoints marked `[RequireMultiTenant]` return `403` with a multi-tenant-required problem response.

## Data Isolation Enforcement

Isolation is enforced in `ExploreDbContext` with named global filters:

- `Tenant` filter for tenant-scoped entities,
- `SoftDelete` filter for soft-deletable entities.

Tenant filters fail closed when no ambient `TenantContext` is bound. Missing tenant context no longer means "query all tenant rows"; request-scoped reads must resolve a tenant before touching tenant-scoped data. System, admin, seeding, cache warmup, authentication, and worker paths that intentionally cross tenants must opt in explicitly through `ExploreDbContext.EnableTenantFilterBypass(reason)` or `IgnoreTenantFilter(reason)` and still apply a bounded predicate such as tenant id, owner id, key id, status, or outbox id. Soft-deleted row access should continue to use `IgnoreQueryFilters([QueryFilterNames.SoftDelete])` so tenant isolation remains active.

Custom `ExploreDbContext` registrations, especially integration-test hosts and
`IDbContextFactory<ExploreDbContext>` callers, must mirror production scoped
property injection. After creating the context, bind `TenantContext` and
`CurrentUserService` from the request scope and clear any stale tenant-filter
bypass state. API integration tests should use the shared test registration
helpers instead of raw `AddDbContext(...UseInMemoryDatabase...)`; otherwise the
fail-closed tenant filters correctly hide tenant rows and make fixtures drift
from runtime behavior.

PostgreSQL RLS is not enabled on production tenant tables yet. A bounded prototype now exists in persistence: `PostgresTenantSessionInterceptor` can bind `app.current_tenant_id` on EF Core connection open when `Persistence:EnableRlsTenantSession=true`, and integration tests prove forced RLS behavior through a non-superuser role. Keep EF named filters and tenant-safe foreign keys as the current enforcement model until a dedicated RLS rollout adds app/migration role separation, table policies, and admin/system-path tests.

Notable cases:

- `User` is soft-delete filtered but not tenant-scoped.
- `TenantUser` and `TenantUserProfile` hold tenant-local participation, status, profile, consent, and moderation state for a global `User`; tenant-admin actions such as suspend, ban, remove, or profile moderation must target these tenant-local rows rather than mutating global account identity.
- `TenantUserRoleGrant` holds tenant role authority as an auditable child of `TenantUser`. Effective tenant membership/admin checks require an active, non-deleted tenant user and an unrevoked tenant-scoped role grant.
- `Actor`, `AtprotoIdentity`, `Organization`, and `Group` are global. `TenantUser`, `OrganizationTenant`, and `GroupTenant` carry tenant participation. Tenant administrators can suspend or hide participation in their tenant, but cannot suspend a global Actor or exact ATProto credential.
- `OrganizationTenantEvidence` and its private Document are tenant-local retained review records. Composite tenant foreign keys bind evidence to the exact participation and storage object, and evidence review never grants global or cross-tenant authority.
- some entities combine tenant and soft-delete filters.

Public Event visibility composes global and tenant-local state without granting cross-scope authority. Local User Events require an active tenant user. Local Organization and Group Events require approved, visible, unsuspended participation, but public reads do not recheck organizer eligibility. Inbound federated Events do not create participation; they require current visible tenant presentation plus a non-tombstoned record and exact active DID identity owned by the global Actor. Tenant context therefore selects presentation and participation, not global moderation authority.

## Governed Local Address Reuse Isolation

Local address reuse is tenant-contained. Persistence applies the ambient tenant plus one eligible
visibility predicate before exact PII projection: tenant-approved, current creator, or a current
approved and unsuspended organization participation. A broad city/country enumeration or an
in-memory authorization filter is not an acceptable substitute.

Promotion receives no caller-controlled tenant or organization. The handler loads through the
fail-closed tenant-filtered repository, verifies the persisted `Location.TenantId` against
`ITenantContext`, and authorizes the trusted actor for `approve_tenant_address`. Missing, foreign,
erased, Private Home, and missing-active-PII targets return the same generic unsuccessful outcome
without mutation. Promotion cannot move a row between tenants, replace its creator, or reassign its
organization; an organization reference remains provenance even after tenant-wide approval.

The current local suggestion query is uncached and observes committed promotion immediately. Any
future cache must include tenant, actor, and organization scope and use exact post-commit tenant-local
invalidation. Shared cache keys, cross-tenant warmup, exact-address exports, and address or identifier
logging are forbidden.

## Support Access Tenant Scoping

Support access is tenant-context support, not user impersonation:

- A support session is persisted with the real actor id, target tenant id, mode, expiry, reason, and ticket/reference metadata.
- The BFF forwards support context only through a server-owned `X-Support-Access-Session-Id` header after stripping browser-supplied support headers.
- The API validates the forwarded session against the resolved tenant context before authorization can consider support access. Actor mismatch, tenant mismatch, disabled support access, stopped/revoked/expired sessions, and disallowed write mode fail closed.
- Support access never creates `TenantUserRoleGrant` rows and never changes `ICurrentUserService.UserId`; audit records must preserve the real operator identity.
- Tenant admins can review their tenant's support-access session history and audit evidence from tenant settings. That view uses the current tenant id from tenant onboarding status and still depends on the API/HAL `audit-events` affordance before showing audit drill-in.

## Managed Provider Tenant Provisioning

Trusted managed-provider automation creates tenant boundaries explicitly rather than treating `Organization` as tenancy:

1. Provider/operator authority calls `POST /api/managed-provider-provisioning/clients:ensure` with instance-admin authorization.
2. The command creates or resolves a `Tenant` for the provider customer and records provider-neutral `ExternalBinding` rows for durable idempotency.
3. The external administrator is linked to the minimal global `User` account through stable IdP identifiers, then gets tenant-local `TenantUser`, `TenantUserProfile`, user `Actor`, and `TenantUserRoleGrant` tenant-admin authority inside the new tenant.
4. Optional organizer semantics create an approved `Organization` or `Group` plus actor inside the tenant. These are tenant-scoped organizer entities, not tenancy boundaries.
5. ERP customer/admin identities receive tenant-admin authority for their tenant, not instance-admin authority. Customer-as-instance-admin is reserved for separate managed-hosting/dedicated-instance product models.

## Hierarchical Settings Model

Customization follows a 5-tier resolution cascade implemented in `HierarchicalSettingsResolver`:

1. **Instance** (`SystemSetting`) — Platform-wide defaults.
2. **Tenant** (`TenantSetting`) — Per-tenant overrides.
3. **Organization** (`OrganizationSetting`) — Specific organization overrides.
4. **Group** (`GroupSetting`) — Specific group overrides.
5. **User** (`UserPreference`) — Individual user preferences.

Resolution flow:
- Reads all tiers for the requested key(s) in a single batch operation.
- Merges values from top (User) to bottom (Instance).
- **Locking**: If a higher tier locks a setting (e.g., `SystemSetting.IsLocked=true`), lower-tier overrides are ignored.
- **Single-Tenant Bypass**: In single-tenant mode, system locks are bypassed to allow the default tenant full control.

Cache behavior:
- Each tier has its own cache prefix (e.g., `HierSettings:Tenant:{id}`).
- Default TTL: 5 minutes.
- Updates to settings invalidate the corresponding scope cache.

### Reporting Provider Delegation

Moderation reporting uses the same hierarchy but keeps the instance baseline authoritative. Local canonical report and case creation always happens first. Static `Reporting:*`, `Reporting:Osprey:*`, and `Reporting:Coop:*` configuration defines the instance provider baseline; tenant settings may only add tenant-owned Osprey or Coop targets.

Instance administrators control delegation with three instance-scope locks that default closed: `governance.lock_tenant_reporting_providers`, `governance.lock_tenant_osprey_provider`, and `governance.lock_tenant_coop_provider`. When unlocked, a tenant may configure `reporting.enable_tenant_osprey_provider` or `reporting.enable_tenant_coop_provider` plus the matching endpoint and secret settings. `reporting.tenant_external_sync_enabled=false` disables only tenant-added external targets; it does not disable local reporting or any enabled instance baseline provider.

Routing-state, update, provider-test, and tenant dashboard APIs are tenant-scoped and derive tenant identity from the normal tenant context. Tenant dashboard counts use exact tenant predicates and return aggregate queue/provider-sync health only. Instance control-plane operations intentionally aggregate moderation reporting provider-sync and lock-impact counts across tenants through an explicit tenant-filter bypass reason; those aggregates never return tenant identifiers, report identifiers, provider URLs, provider IDs, correlation IDs, payloads, raw errors, or secret material.

## Tenant Lifecycle States

`TenantStatusEnum` values:

- `Provisioning`
- `Active`
- `Suspended`
- `Archived`
- `Purged`

## Endpoint Visibility by Mode

Filters in `Explore.API.Filters.BlockInSingleTenantAttribute`:

- `BlockInSingleTenant` -> returns `404` in single-tenant mode (hide endpoint),
- `RequireMultiTenant` -> returns `403` with explicit multi-tenant required message.

## Render Policy Delegation

Render policies can be delegated to tenants via the governance cascade.

Master gate: `routing.render_policy.allow_tenant_override` must be `true` in `SystemSetting`.

Per-route-group locks (in `SystemSetting`):

- `routing.render_policy.lock_tenant_public_seo`
- `routing.render_policy.lock_tenant_operational`
- `routing.render_policy.lock_tenant_admin`

Onboarding render policy is always instance-controlled.

Tenants store overrides in `TenantSetting` with the same `routing.render_policy.*` keys. Override cascade:

1. Instance settings are read first.
2. Tenant preset/global overrides are applied.
3. Normalization runs (preset defaults, non-advanced collapse).
4. Per-route-group tenant overrides are applied only for unlocked groups.

See [RENDER_POLICIES.md](RENDER_POLICIES.md) for full delegation details.

## Branding Governance, Single-to-Multi-Tenant Transition, And Legal Identity

### 1. Why Instance Branding Exists
Even in a tenant-centric platform, Instance Branding (`GovernanceSettingKeys.Branding.*`) is architecturally mandatory for surfaces that operate outside or above an individual tenant boundary:

1. **Pre-Tenant & Cross-Tenant Entry Points**: Global authentication and IdP redirect pages before a tenant subdomain or slug is resolved.
2. **Global Error & Maintenance Pages**: Unresolved subdomain 404s, platform-wide 500 errors, and system maintenance screens.
3. **Instance Control Plane & Administration**: Backoffice management surfaces (`/admin/instance`, `/admin/instance/tenants`).
4. **Platform System Communications**: Master billing receipts, security alerts, and infrastructure-level emails.
5. **Governance Floor & White-Labeling Control**: When `Tenants.WhiteLabelingEnabled = false`, instance branding acts as the enforced visual identity across all tenancies.

### 2. Single-to-Multi-Tenant Transition Lifecycle
* **Single-Tenant Mode**: The instance and the default tenant share the same visual identity. Instance branding settings define the tenant experience.
* **Transitioning to Multi-Tenant Mode**:
  * The primary/default tenant retains its existing branding.
  * When provisioning additional tenancies (2nd, 3rd, etc.), the tenant creation flow initializes `BrandingSettings.DisplayName` from the provided tenant name (e.g., `"Dallas Muslim Center"`).
  * If white-labeling is permitted (`Tenants.WhiteLabelingEnabled = true`), tenant administrators can independently manage their display name, logo, favicon, and custom stylesheet.

### Concurrent Initial Branding Provisioning

The authenticated branding GET invokes `EnsureTenantBrandingSettingsDocumentCommand`.
`TenantBrandingSettingsDocumentProvisioningService` retains its explicit-name
update behavior when its initial read finds an existing document. If that read
misses, `ITenantSettingsDocumentRepository.CreateIfMissingAsync` inserts the
candidate or returns the exact tenant/document-key winner unchanged, including
its revision. It never converts a raced insert into an update.

Persistence recognizes only the model-derived unique `(TenantId, DocumentKey)`
constraint through the existing relational conflict classifier. Without a
caller transaction, EF owns the insert transaction. Inside a caller transaction,
an explicit savepoint fences the attempt: recovery rolls back that savepoint,
detaches only the candidate, and retains the owner's prior writes and other
tracked changes. It neither commits nor rolls back the owner's transaction.
Cancellation and unrelated failures escape after cleanup. A repeatable-read or
serializable snapshot that cannot see the winning row must be rolled back and
retried by its transaction owner; recovery never silently switches snapshots.

The service invalidates the typed tenant-document cache after a successful
insert or winner read; recovery publishes no candidate to the cache. Missing
tenants still fail the foreign-key constraint. Tenant creation, onboarding,
managed provisioning, and governance updates retain their existing transaction
owners. Canonical SQLite HTTP tests cover two successful concurrent initial
reads; PostgreSQL tests cover distinct proposed names, cache convergence,
savepoint recovery, owner commit/rollback, snapshot retry, cancellation, and
unrelated constraints.

### 3. Cosmetic Branding Fallback
In multi-tenant mode, resolving `BrandDisplayName` may fall back to the instance
name (and ultimately `"ISLAMU"`) if a tenant's cosmetic display name is
unconfigured or locked. This fallback applies only to visual/copy surfaces. It
never satisfies activation, public disclosure, paid commerce, or buyer
acceptance identity requirements.

### 4. Structured Directory, Platform, And Merchant Roles
Multi-tenant public and paid disclosures are strictly partitioned:

* **Tenant directory operator** comes from the exact tenant typed identity
  document and carries the public/legal names, jurisdiction, registration,
  contact, and links.
* **Instance platform operator** comes from `Instance:OperatorIdentity`; payment
  operations remain separately configured under `Payments:CheckoutGovernance`.
* **Organizer merchant** comes from the event organizer actor and connected
  provider recipient lineage.

See [PAYMENTS.md](PAYMENTS.md#3-structured-legal-identity-and-paid-acceptance)
for the readiness and immutable acceptance flow.

## Related

- [CONFIGURATION.md](CONFIGURATION.md)
- [OPERATIONS.md](OPERATIONS.md)
- [ADMIN_HIERARCHY.md](ADMIN_HIERARCHY.md)
- [RENDER_POLICIES.md](RENDER_POLICIES.md)
- [PAYMENTS.md](PAYMENTS.md)

