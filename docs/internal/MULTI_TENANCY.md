ABOUTME: Explains tenant resolution, isolation, and override behavior implemented in code.
ABOUTME: Prioritizes runtime rules from TenantContext, query filters, and governance services.

# Multi-Tenancy

Tenant isolation supports application-owned authorization; it does not transfer
business authority to the database. EF query filters and PostgreSQL RLS constrain
the rows an ordinary selected tenant can access. Authorized instance-wide work
uses an explicitly selected purpose with fixed storage rights and C# access checks,
not Runtime promotion. Existing native capability descriptions below are current
mechanisms to reassess during the EF-first cutover, not a mandate to keep business
logic in stored routines. See [the accepted decision](adr/ADR-application-owned-persistence.md).

A PostgreSQL database context seals tenant identity for its connection lease.
Each reader, scalar and nonquery command validates that identity before execution;
replacing the tenant context or mutating its tenant ID fails closed. Disposal
clears the sealed identity and request state before context-pool reuse.

## Global reference catalogue and visitor metadata reads

`LookupDataCache` reads its 17 declared reference entity types through
EF Core and `GlobalLookupCatalogueProviderOperations`. On PostgreSQL,
`PostgresLookupCatalogueReadPermissions` installs/attests fixed Runtime column
reads; the obsolete catalogue business routine is retired. The caller remains
the original tenantless Runtime connection in a Serializable transaction, without
owner-role promotion or private `RolePermission`/`UserPii` access.
`EventType` is the one tenant-capable reference: only its global (`TenantId IS
NULL`) rows enter this catalogue. Tenant-selected sessions cannot use it. EF
materializes the mapped entities; cache refresh publishes the complete image only
after all reads succeed, retaining the previous complete image on failure.

Visitor HAL metadata uses `IInstanceTenantSettingsReader` on the selected original
context instead of raw system/tenant repository queries. One complete
`AuthoritySettingKeys` snapshot supplies both mode and provider projection;
`IVisitorAccessProviderReader` projects the instance entities without another
database read. The capability resolver
retains lifecycle gating, instance locks, tenant overrides and its existing mode
evaluation. Provider metadata reads only the instance portion of the fixed
authority-key snapshot, so tenant settings cannot replace instance authentication
provider configuration. Neither path grants private event management authority.

## Tenant Directory-Operator Identity

 Every tenant has an authoritative typed directory-operator identity document,
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

The multi-tenant Instance Console (`/admin/instance`, `/admin/instance/tenants`, `/admin/instance/domains`) and fleet operations remain multi-tenant-only. In single-tenant mode, the Blazor route guard redirects the console back to tenant/instance settings surfaces; API endpoints marked `[RequireMultiTenant]` return `403`.

The existing control-plane tenant detail and activation routes are the narrow exception: instance administrators may address only `PlatformDefaults.DefaultTenantId` in SingleTenant mode. Other target IDs return `404`; no public slug lookup is needed. Detail HAL retains only the authorized `self` and state-valid `activate` relations in this mode. MultiTenant exact-target lifecycle semantics are unchanged.

Provisioning identity and branding documents remain private, authorized tenant-management resources. Completing identity does not activate a directory. Explicit activation rechecks the persisted identity under the shared mutation lock; identity document resolution bypasses the node-local cache so a prior ready revision cannot publish a now-incomplete directory. See [ADR-032](adr/ADR-032-progressive-instance-onboarding.md).

The public tenant slug cache deliberately excludes Provisioning rows. For
configured-administrator bootstrap only, `ApiTenantResolutionMiddleware` may
resolve an explicitly supplied `X-Tenant-Slug` against the persisted slug of
`PlatformDefaults.DefaultTenantId` when its bootstrap is configured Pending
or Completed. This fallback applies solely to `UserController.SyncUser` and
private current-user/admin-authority reads, never to API-key requests or other
tenants. `TenantLifecycleAccessMiddleware` admits authenticated SyncUser
attempts for that same Provisioning tenant; `SyncUserCommandHandler` separately
checks private external synchronization eligibility before any account write,
and the configured claim validates the exact provider account inside the
onboarding transaction. SyncUser bypasses cached idempotency responses, returns
the platform user ID rather than the onboarding receipt ID, and keeps private
responses no-store. Activation remains a distinct operation.

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

PostgreSQL runtime composition requires tenant-session binding and read-only
policy admission; it is not an optional `Persistence:EnableRlsTenantSession`
prototype. Migration authority installs model-derived policies with
`ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY`. Runtime admission
rejects privileged or elevatable logins, table ownership, missing or divergent
policies, and extra applicable permissive policies before application SQL.
EF named filters and tenant-qualified foreign keys remain separate enforcement
layers; authorization still belongs to the owning handler.

Tracked state is not tenant authority. `GenericRepository.GetById` performs a
no-tracking filtered key query rather than `FindAsync`, so a previously attached
foreign or deleted object cannot satisfy the read. Key values are captured as
query parameters; mapped metadata supplies property identifiers. Generic reads
also constrain mapped direct tenant columns even when a legacy model owner
lacks a named tenant filter. Generic tenant mutations require an exact nonempty
tenant lease, not an absent context or bypass reason.

For a tenant-bound context, `ExploreDbContext.TrackedAuthority.cs` checks pending
writes inside the existing native save transaction, before disclosure capture.
It evaluates mapped tenant filters for existing keys and changed parent
references, including parent-derived ownership, and compares direct tenant
values with persisted values rather than trusting attachment-supplied originals.
Owned policy/value slots are checked through their mapped root ownership keys,
not queried as independent entity sets.
A context-wide bypass reason cannot authorize a tenant-bound tracked write.
Failure poisons the existing transaction so a caller cannot catch the rejection
and commit earlier work through the unit of work. The check does not acquire
Global credentials, authorize an action, or replace purpose-specific authority
for unbound maintenance contexts.

`ExploreDbContext` freezes the tenant identity for a lease. Replacing the
context or mutating its tenant identity after binding is rejected before SQL,
and pool return clears request identity and bypass state.
`ITenantExploreDbContextFactory` acquires a separate ordinary Runtime lease for
an exact persisted tenant. `IGlobalExploreDbContextFactory` owns an independent
pool using explicitly selected Global credentials and normally binds an absent
tenant. Neither factory promotes an already-acquired request connection.
An EF tenant-filter bypass alone cannot bypass PostgreSQL RLS.

Cross-tenant work uses purpose-specific authority: authentication and
administrative discovery call exact-key or exact-user functions; queue
discovery returns bounded owner IDs before ordinary tenant work; disclosure
fanout preserves its source transaction through restricted functions; PDS
settlement binds the persisted claim tenant before acquisition; ATProto
materialization uses its fenced private-graph capability. Function execution
does not grant arbitrary private-table access. Disclosure's execution group
is named `event_discovery_disclosure_runtime`, but selected Global materialization
and settlement groups inherit its fixed functions; the name does not authorize
ordinary tenant Runtime callers. The installer rejects unsafe attributes on its
three managed non-login roles, unexpected membership edges, and ownership other
than its exact function catalogue in the selected database/namespace. It never
silently downgrades an unsafe existing role. The owner is reachable only through
the dedicated migrator group and selected migration login, using SET rather than
inherited table authority. Shared writes and bounded disclosure epochs retain
one transaction, including rollback on fanout above 1,024 tenants. See
[the security model](SECURITY-MODEL.md#row-level-security-rls--tenant-leases)
and [database authority provisioning](SECRETS.md) for role and grant ownership.
The public tenant-directory and email-admission installers likewise reject
unsafe existing role attributes, unexpected parent/migrator membership, and
ownership outside their exact function signatures. Their Global execution
groups must have no effective table, column or sequence privileges in any
non-system schema, including unrelated tables. Installation fails with `42501`
rather than silently repairing contaminated authority. Migration membership
uses explicit `INHERIT FALSE, SET TRUE`; normal callers receive only function
execution through the selected capability.

Managed-provider provisioning also attests both Ordinary Settings writers on
`Tenant`: the row-level INSERT/UPDATE/DELETE writer and the statement-level
TRUNCATE writer (PostgreSQL trigger flags 29 and 32). Their owning partial supplies
the original bodies, role stamp and privilege contract to
`ProvisioningSharedWriterAssertions`; the provisioning catalogue admits those
exact identities rather than a trigger-name exception. Complete migration-only
absence remains distinct from a partially installed or counterfeit protocol.

Typed native Settings images materialize every `SystemSetting` scalar explicitly.
`AllowedValues` is a CLR JSON-text property: PostgreSQL supplies a JSON array in
the native image, while SQLite supplies stored JSON text. The shared materializer
retains that constraint as JSON text without dropping metadata or changing the
original image used for admission, comparison and compilation.

Mixed email effects remain on the original Global connection and Serializable
transaction. Their native writer installs a private row witness only after the
producer's value, lock, target, metadata and audit checks. The email capture trigger
consumes that witness against PostgreSQL's complete physical OLD/NEW images;
Global has no execution grant on the consumer. Only the email owner can call it.
The mixed attempt retains its own receipts, rather than forging a dedicated email
purpose receipt. Dedicated email mutation logins and their checks remain separate.
Composed admission validates the union of both complete role graphs, routine
execution grants and relation privileges. Installers still create only their own
roles and grants. The closed-role privilege validator has no peer-role exemption.

Native compilation reserves the email claim lock, snapshots projection baselines
and membership, and returns one database timestamp to the original writer.
Projection admission binds the actor, target, original revision and suppression
history, rejects operator-state changes and repeated projections, and requires a
consumed setting change. Completion clears witness state in the original attempt
tombstone; deferred settlement rejects an unfinished attempt. Rollback removes
settings, projections and witnesses together.
An instance setting write cannot substitute a tenant override relation. A consumed
email setting change also requires its primary SMTP processor or selected tenant
control projection before group completion; completing the key alone is not enough.
Mixed acquisition and every shared-source change acquire the claim lock before
the source revision row. Dispatch already takes the claim lock before processor
updates; a consistent order avoids an inverse wait between dispatch and settings.

Ordinary public discovery has two distinct execution-only capabilities.
`PostgresDiscoveryTraversalContract` provides exact-tenant revision,
reservation, snapshot and identity operations through
`event_discovery_traversal_runtime`. `PostgresPublicEventDiscoveryContract`
provides fixed local/remote seek, member reprojection and visible-record reads
through `event_public_discovery_runtime`. The selected Runtime login inherits
execution, not owner roles or private-source table privileges. Both installers
run through the existing migration composition after disclosure installation.

The ordinary page-number public search uses that same contract's `page_local`
operation. It compiles the prepared specification through the canonical typed
criteria and selects the same earliest matching occurrence, source eligibility
and stable ordering as seek discovery. The native result includes the current
total and ordered page together, retaining the total even for an empty later
page. The repository retains or opens Serializable on its selected connection;
REST and anonymous MCP do not issue raw Event or EventSession queries. The MCP
adapter's existing maximum page size remains 25.

`PostgresPublicEventDetailContract` also materializes the public program graph:
published, scheduled sessions with both endpoints and a published live day when
assigned; published live groups and their same-event assignments; and live
agenda items on published days. This is intentionally not discovery's explicit
open-ended occurrence eligibility. Parent publication, visibility, tenant and
source-custody predicates still govern the entire result, including sessionless
detail. Program summary reads this public parent and its groups/agenda rather
than the managed parent or raw child repositories. Sessions-by-event uses the
same native detail binding. Physical locations and rooms are not hydrated:
their placement identifiers still go through `IEventLocationDisclosureService`
in the handlers, with no local disclosure substitute. The existing migration
installer derives the required source columns and policies from this projection;
Runtime receives execution only. Managed program queries retain their original
repository branches.

The public candidate reader evaluates protected source eligibility internally
and returns only bounded public entity projections. Its typed conjunction
preserves the prepared specification, persisted keysets and exact event/session
pairs. Explicit nonmapped presentation state carries public outcomes without
transporting storage keys, registration artifacts or unrelated private graphs.
Snapshot storage remains reference-only; current reprojection and the terminal
disclosure fence still run in the caller's original Serializable transaction.
`PublicEventDiscoveryReadBindingFactory` captures the production-selected
context, connection and tenant lazily. Anonymous requests need no invented human
mutation lease; an existing explicit lease must retain the same ordinary owner.
Native entry guards independently validate actual enrollment and posture.
`PostgresPublicEventResponseClockContract` separately enrolls ordinary Runtime
for timestamp-only response freshness components. The repository uses the same
selected public-read binding and retains an existing Serializable transaction,
or opens one on that original connection when none exists. Its model-derived
source predicates preserve published sessions and days, eligible local sources,
governed remote projections including owned local echoes, and eligible location
reveal components. Federation remains tenant-governed; privacy governance is
resolved only when eligible locations exist and unresolved governance fails closed.
Creation-plus-offset arithmetic remains in .NET to preserve 100ns offsets;
creation and explicit reveal times remain independent conservative boundaries.
The clock owner receives only required source columns (only the location key
from `LocationPii`), while Runtime receives execution only. Admission and
reinstallation attest the exact body, role graph, source schema and RLS policies;
neither repairs drift or grants Runtime raw source access.

Private event management uses `HumanPrivateEventManagementReader` to hold the
original authenticated human, selected tenant, credential lifetime, authorization
route, assignments and support sources through the Serializable read finalizer.
The repository verifies the exact active closed read request before and after
execution; another prepared operation cannot borrow it. Native enrollment grants
`event_private_management_runtime` execution only. My-events retains the union of
personal, organization-membership and group-membership actors, including private
and draft Events; application handlers retain DTO mapping and normal HAL policy
evaluation.

`EventDetailsProjectionService` and `GetEventPublishReadinessRequestHandler`
require this private reader; neither has a raw-repository fallback. Their queries
delegate authorization to that original-human read owner rather than acquiring
a second generic authorization source set before it. The native Details operation
requires ViewManagement; Readiness independently requires Update. HTTP endpoints
still require authentication, the native owner evaluates Local/Cerbos policy
and finalizes source custody, and HAL actions still receive their own policy
decisions.

The request annotations remain intact for declared action/resource contracts
and outer machine-caller evaluation. `RequestAuthorization` explicitly delegates
these original-human reads to their mandatory native owners, following the
same closed-owner pattern as other source-fenced operations.

`GetManagedEventProgramRequest` maps sessions, groups, days and agenda from one
native Program graph, rather than acquiring the same complete source set four
times in an MCP request. It retains ViewManagement authorization, existing
ordering and managed location fields. MCP still checks the Edit HAL affordance
and applies its location disclosure ceiling after mapping. No admission is cached.
`GetManagedEventCustomPropertiesRequest` likewise maps the definition page and
ordered values from one CustomProperties graph. Definition paging, total count,
value ordering and MCP truncation/redaction retain their existing semantics.
`GetEventRegistrationOrdersQueryHandler` also requires the native reader and
delegates its annotated ManageRegistrations action to that original-human owner;
neither an Edit HAL link nor generic event facts replace that independent action.
Machine callers retain outer annotation evaluation and cannot borrow a human read.
Team-list reads use the separate closed Team entrypoint, under the existing
`HumanEventTeamActionPreparation` ManageTeam decision and original Serializable
source owner. The native graph carries complete EventRoleAssignment, User/Pii
and Role entities; application handlers retain DTO mapping. The exact original
team request must remain acquired through repository access and final disclosure.
Generic private reads cannot select Team, and Runtime gains no direct team or
user-table access. Assignable presets retain their own prepared team authority.
The combined `GetManagedEventTeamRequest` maps members, current-user authority
and assignable presets from this same acquired team read. Both authority views
derive from its held assignment/definition sources before finalization, not a
second permission query or reusable admission cache. MCP retains its Team HAL
gate and bounded, redacted descriptors.
Team preparation constructs the source-set fence before the Serializable attempt;
acquisition and final validation still execute inside that original transaction.
Session template sync context uses the closed `SessionSyncContext` graph to
verify the live session belongs to the selected event and read its history in
one acquisition. The native graph returns no session/history for a mismatched
parent, while the existing standalone history operation retains its fail-closed
selector error. The CQS handler maps the existing history DTOs and pagination;
MCP retains its Edit HAL gate and diff behavior.
Protected guest-allocation replay uses a separate closed `GuestRecovery` read:
the repository supplies the already authenticated challenge's canonical digest,
exact order/event/tenant and original Serializable owner to the execute-only
registration capability. It may recover an expired order without refreshing any
deadline; the handler still checks the challenge's recovery window before and
after reading. Generic account/plaintext-bearer reads cannot select this kind.
The operation returns entities and rechecks its physical owner before releasing the result.

Checkout settlement retains the exact new `PaymentReconciliationEffect` only after
the original active claim's compare-and-set succeeds under its row lock. This
proves that object's Tenant foreign key inside the same physical transaction.
It also binds the exact mutated `PaymentAttempt` and its closed domain status;
preflight accepts only that retained status key, while the database foreign key
still requires the lookup row to exist. Other parent and tenant checks still run.
PostgreSQL lease comparisons reject
sub-microsecond deadline changes before parameter truncation could hide them.
The precision rule lives in the provider primitive, not the repository.

Tracked foreign-key preflight retains the existing storage-reference conflict
contract for an invisible storage parent. Missing, retired and foreign storage
references still fail before persistence and poison the transaction; they surface
as the same concurrency conflict rather than bypassing the storage protocol with
a generic tenant-authority exception.
Lifecycle approval policy resolves only `events.require_approval` through the
authoritative tenant-only settings hierarchy; instance locks still govern tenant
overrides. It no longer loads the full administrative policy DTO through raw
SystemSetting, TenantSetting and Tenant repositories.

Registration order add-ons, fair-return waitlist, participant readiness, purchase
access and ticket-transfer reads use `IRegistrationPrivateReader`. Account access
requires the original authenticated human and held principal, route, assignment
and support sources through final admission. Anonymous access instead binds the
canonical SHA-256 bearer digest, exact tenant and resource tuple to the original
Serializable Runtime owner. `registration_private_read` selects ownership or
capability authority before projecting the closed entity graph; handlers retain
DTO mapping. Caller/transaction custody and applicable bearer deadlines are
checked again before disclosure. Existing add-on/waitlist expiry semantics remain
distinct from purchase/readiness/transfer deadlines.

The migration-owned installer gives the dedicated NOLOGIN owner only declared
source-column rights; Runtime receives routine execution, not table grants or
role elevation. Live schema, role, policy and exact body attestation fail closed.
Waitlist reads supply admitted tenant/event sale-control entities to the same
paid-checkout evaluator used by other callers, preserving identity/operator
readiness and stopped-sale rules without reopening raw control-table reads.

Creation publisher options use `HumanEventCreationPublisherReader` and
`event_creation_publisher_runtime` separately. They retain all own live
memberships, including unavailable options without eligible actors, and calculate
publish capability from persisted active permission mappings. Neither read turns
the caller into an administrator or grants Runtime membership-table access.
Matching migration enrollment and successful genuine Runtime HTTP/MCP probes
are both required; a successful build or installer alone is not acceptance.
The read-only hierarchy resolver uses `IInstanceTenantSettingsReader` for
detached current system and selected-tenant settings. PostgreSQL uses the
existing `ordinary_settings_read` function and its enrolled service identity,
on the original context and transaction, without a human mutation lease or
raw source-table grants. Native readiness validates exact routines and
privileges before materialization. Discovery traversal, local-source
configuration and federation governance request the read-only interface explicitly, not the mutable
resolver's inherited read method. The mutable resolver retains its original
admitted repository source; organization, group and user resolution remains
with their existing repositories.
Other relational providers retain EF isolation and their native constraints;
these PostgreSQL RLS claims do not apply to them.

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

Moderation reporting uses the same hierarchy but keeps the instance baseline authoritative. Local authoritative report and case creation always happens first. Static `Reporting:*`, `Reporting:Osprey:*`, and `Reporting:Coop:*` configuration defines the instance provider baseline; tenant settings may only add tenant-owned Osprey or Coop targets.

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
A provider can nevertheless abort the entire transaction and remove its savepoints.
If savepoint rollback fails, the candidate is still detached, winner recovery is
forbidden, and the original failure escapes. A release failure cannot mask an
active failure or make a recovered insert appear successful. Secondary cleanup
exceptions are retained on that original exception in `Exception.Data` under
`TenantSettingsDocument.SavepointRollbackFailure` and
`TenantSettingsDocument.SavepointReleaseFailure`; callers can inspect both
without losing the original exception type, inner provider code, stack, or
cancellation token. A release failure after a successful insert remains an error
in its own right. The owner's existing whole-transaction retry policy is unchanged;
cleanup does not imply that a provider-aborted transaction is usable.

Cancellation and unrelated failures escape after cleanup. A repeatable-read or
serializable snapshot that cannot see the winning row must be rolled back and
retried by its transaction owner; recovery never silently switches snapshots.

The service invalidates the typed tenant-document cache after a successful
insert or winner read; recovery publishes no candidate to the cache. Missing
tenants still fail the foreign-key constraint. Tenant creation, onboarding,
managed provisioning, and governance updates retain their existing transaction
owners. Authoritative SQLite HTTP tests cover two successful concurrent initial
reads; PostgreSQL tests cover distinct proposed names, cache convergence,
savepoint recovery, owner commit/rollback, snapshot retry, cancellation, and
unrelated constraints. Deterministic SQLite transaction interceptors cover
rollback/release failures, candidate detachment, blocked winner recovery, and
preservation of retry-recognizable provider errors and cancellation. These
injected boundary failures do not constitute a reproduced live MySQL deadlock.

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

