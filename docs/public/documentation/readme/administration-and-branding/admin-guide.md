---
description: Walkthrough of administrative consoles and management workflows for instance and tenant admins.
---
<!-- ABOUTME: Operator walkthrough of instance, tenant and organization administration. -->
<!-- ABOUTME: Explains console capabilities and safe Local account credential handover and recovery. -->

# Administration Guide

This guide walks administrators through the web consoles in the Blazor management interface, covering instance-level controls, tenant provisioning, monetization, branding, and organization governance.

### Event creation scope

Select the intended tenant before creating an event. Event creation derives its
authorization scope from that server-selected tenant, not a tenant identifier
in the event body. The requested organization and group identify the owning
context, and their membership checks still apply. A missing tenant selection
is denied rather than treated as instance-wide creation; an existing event ID
cannot substitute for the new-event creation identity.

### User tenant navigation and database authority

The API resolves navigation for the authenticated user only. It uses a saved
tenant preference only when that tenant is still in the user's active membership
list; otherwise it selects the latest active membership or no destination. A
navigation result is a convenience, not permission to enter a tenant, and does
not change the saved preference.

The configured Global database login can read exactly the user's identifier,
deleted flag and preferred tenant identifier through EF Core. Global is an API
database purpose, not an administrator's human role. Runtime gains no User-table
access, and profile fields, audit fields and preference writes remain separately
restricted. Protect Global credentials: someone holding that database login can
enumerate those user/preference associations even though the API selects only
the authenticated user. Database row-level security does not authenticate a human
using stolen application credentials.

This pre-release cutover removes the old navigation routine and its capability
roles. Startup refuses leftover `user_tenant_navigation_read` routines or
`event_user_navigation_owner`, `event_user_navigation_global` and
`event_user_navigation_migrator` roles rather than automatically deleting them or
repairing permissions. For disposable development, use a fresh isolated PostgreSQL
cluster; a new database in the same cluster does not remove shared roles. If data
or shared roles must survive, arrange a reviewed dependency-checked transition
before deployment. No new environment key or entity migration is needed; do not
delete retained user data or broaden grants to bypass readiness.

### Setup credentials and bearer tokens

On setup-protected routes, an invalid bearer token returns `401` even when the
setup secret is valid. Send an access token with the correct purpose, or omit
the Authorization header for an explicitly supported setup-only operation
while setup is active. A valid bearer token does not override an invalid setup
secret supplied in the same request. Rejected credentials never authorize a
credential-provider call through the other channel.

---

## 1. Administrative Consoles & Routes

| Administration Scope | Typical Role | UI Entry Points | Capabilities |
|---|---|---|---|
| **Instance Administration** | Instance Administrator | `/settings/instance` or `/admin/instance` | [Multi-Tenant Governance](../security-and-identity/multi-tenancy.md), tenant lifecycle and plans, domain guidance, global quotas, storage, SMTP and [platform monetization](../events-and-ticketing/paid-events-and-payouts.md). |
| **Tenant Administration** | Tenant Administrator | `/settings/admin` | [Tenant Branding](white-labeling.md), lookups, navigation, custom footers, event templates, and [custom registration properties](../events-and-ticketing/custom-properties.md). |
| **Organization Management**| Organization Admin | `/settings/organization/{id}` | Organization profile, membership approvals, verified organizer status, and API keys. |
| **Group Management** | Group Admin | `/settings/group/{id}` | Group profile, public event listings, group branding, and members. |

---

## 2. Instance Administration

### Offline migration scaffolding

Development/Testing application migrations can be scaffolded without acquiring
database credentials by explicitly passing `--DesignTime:ModelOnly=true` after
the EF command's `--` separator, together with `--Database:Provider=<provider>`.
MySQL/MariaDB also require the intended server version. This mode configures the
provider model and catalog but supplies no connection string. Applying migrations
or inspecting an existing database still uses the ordinary, explicitly selected
secret-authority workflow; model scaffolding is not database admission.

### Tenant deletion settlement

Tenant deletion reserves discovery before retaining the administrator's source
authority on one transaction. Tenant removal and discovery cleanup commit
together; late grant, binding or credential changes roll both back and retain
other tenants. Slug-cache refresh occurs after acknowledged deletion.
These authored controls await executable security-programme assurance.

### Registration provider connections

Creating or updating a provider connection, replacing approved origins and
deleting an unused connection retain the original administrator's authority
through the same transaction. Late credential expiration or grant revocation
rejects the whole change, including origin rows. Existing input validation and
the refusal to delete a connection still used by bindings remain.
These authored controls await executable security-programme assurance.

### Governance update settlement

Module, organization, branding, domain, tenant-delegation, admin-portal, MCP,
AI and render-policy patches retain their original administrator authority
through settlement. Supplied values and locks commit together with required
default-tenant projections or SMTP revision changes; a rejected specialized
write aborts the whole patch. Lock-only changes preserve existing stored values.
Branding cache invalidation and notifications occur after acknowledged commit.
These authored controls await executable security-programme assurance.

### Operator identity editing and import

Configuration import session creation, preview, cancellation and rollback
snapshot preparation retain the original administrator's credentials, grants
and support boundary through settlement. A rejected storage operation cannot
leave a partial session or artifact merely because its error was caught.
Results are returned only after the original owner acknowledges commit.
Uploaded bytes and preview choices are captured before asynchronous processing;
unfinished setup and background expiration retain separate ownership.
These source controls await the security programme's executable assurance.

Saving the operator identity or importing its portable manifest now retains the
original administrator's current credentials and grants through database
settlement. Expired credentials/support sessions, changed authority, stale
revision or altered effects reject the whole operation. Import audit and
identity changes commit together; successful notifications follow settlement.
Manifest import does not transfer official registry authority.

Unfinished setup uses its real setup ticket and secret, with the same pending
state throughout. It does not turn a signed human into a setup principal or
grant administrator access. Completion's initial operator seed remains separate
from later editing.

PostgreSQL editor source now has a dedicated enrolled data capability using the
existing database identities. It verifies installed native ownership and
privileges and rejects drift without automatic repair. Storage representation
is fixed before the operation begins; strict final comparisons and full-precision
credential deadlines remain. The native installer hooks and regression source
are authored, but executable provider, concurrency and setup-HTTP assurance is
still pending under the security programme.

Use `/settings/instance` or `/admin/instance` to open the unified console.
SingleTenant deployments show **Administration**, combining instance infrastructure
and directory settings. MultiTenant deployments show **Instance Administration**;
tenant-only controls remain under **Tenant Administration** at `/settings/admin`.
Tenant management and tenant plans remain available from the console navigation.
Health, provider status, domain guidance and operational warnings are retained.
Delegated tenant-only administrators can manage tenant settings in SingleTenant
mode; instance infrastructure remains restricted to instance administrators.

For a dedicated administration host, configure `INSTANCE_ADMIN_PUBLIC_ORIGIN`
with its exact browser-facing origin. Compose defaults to
`http://admin.localhost:7002`; the isolated local-agent profile uses
`http://admin.localhost:5200`. Visiting that host's root opens instance settings.
The retired `CONTROL_PLANE_PUBLIC_ORIGIN` variable has no compatibility fallback.
External Event Control Plane fleet management remains a separate integration,
not a second embedded administration console.

The authentication provider card names Local when Local Identity is selected,
alongside any enabled external sign-in providers. An SMTP warning means the
instance's saved host or sender settings are missing; running a development
Mailpit service alone does not configure or enable platform email delivery.

### Tenant resolver configuration

Updating tenant resolver settings through
`PATCH /api/instance/settings/resolver-config` requires a signed-in instance
administrator. A setup secret or configured startup routing reader does not
authorize this editor. Header forwarding remains enabled; supplied path/domain
settings retain their existing validation and normalization rules.

The server validates each patch against the current complete resolver
configuration, then commits its supplied settings together. It rechecks the
original account, credential, administrator and support-session authority, as
well as the exact stored values and metadata, before committing. A rejected or
expired request rolls back the entire patch. The local configuration cache is
invalidated only after acknowledged commit.

The source implementation covers SQLite and a dedicated PostgreSQL resolver
contract. PostgreSQL installation belongs to the existing migration identity,
not the runtime administrator: fixed functions and a private source lock
protect the complete configuration, including settings that do not yet exist.
Missing or altered native authority is rejected rather than repaired at runtime.
PostgreSQL operator-identity editing does not grant resolver editing.

SQL Server, MySQL/MariaDB and other unsupported providers still reject this
editor. Programme SQL installation, concurrency, authentication and provider
checks remain pending; this describes authored source rather than a validated
deployment guarantee.

### Authentication and authorization provider configuration

Provider configuration retains the first validated settings and binding
snapshot through writing. Administrator requests, accepted initial setup and
configured startup retain distinct authority; one does not grant another.
Changes to those sources, participation requirements or original account/
credential/support authority reject the complete update. Endpoint verification
runs outside database locks, and caches activate only after acknowledged commit.

SQLite and PostgreSQL paths are authored. PostgreSQL requires its dedicated
migration-installed provider contract and rejects missing or altered authority
instead of repairing it at runtime. Other provider custody paths remain
unsupported. SQL installation, authentication, concurrency and provider checks
are still pending under the programme's verification freeze.

### Combined ordinary and specialized settings batches

A batch containing Email and other settings, or ordinary and specialized
non-Email settings, retains the original account,
credential, support session and stored configuration through one database commit.
Each accepted setting must be completed by its own secured writer. Changed
authority, expired credentials, replaced configuration or an omitted write rolls
back the entire accepted group. BestEffort may skip invalid inputs before that
group is compiled; it cannot partially commit after those failures.

Category and scope restrictions still apply. An instance TenantDelegation batch
can include `TenantDelegation.LockSmtp` and `TenantDelegation.LockAnalytics`;
this does not grant tenant administrators access to instance-only settings.
Caches and notifications activate only after acknowledged commit. PostgreSQL
requires its independent migration-installed mixed contract, without new database
credentials or borrowed provider/storage privileges. SQLite/PostgreSQL paths and
72 regression cases are authored, but installation and runtime checks remain
pending under the programme's verification freeze.

For example, an Events batch changing the ordinary maximum session count and
publication approval uses this combined owner rather than borrowing two
independent write approvals. If either accepted effect loses its original
source or is changed outside its actual writer, both effects roll back and
no change notification is emitted. Two additional instance-level controls
cover the unchanged source and a changed effect; they remain unrun.

Standalone ordinary Settings writes have a separate SQLite implementation:
existing row identity, creation audit and definition metadata are preserved;
new metadata comes from the setting registry rather than posted entity fields.
A rejected writer remains rejected even if its caller catches the exception.
The separate PostgreSQL ordinary contract, adapter and installation hooks are
now authored, including native omission and post-completion expiry controls.
Callbacks verify their current effects; only settlement completes the native
attempt, before the original human deadlines are checked. Installation and
runtime acceptance remain unrun. SQL Server/MySQL ordinary mutations still
fail closed; no provider status here authorizes a fallback account.

### Original authority for administrative changes

Creating, updating or deleting tenant templates and custom-property definitions,
and creating a tenant-plan draft,
retain the original authenticated account, policy question and support-session
horizon through commit. A credential, binding, policy route or supporting role
change before commit rejects the whole transaction, including earlier effects.
Extending a support session during the operation cannot extend its original
deadline. The existing instance/tenant administrator rules still apply.

These seven action owners and their production registration are source-integrated;
62 Local-authentication controls are authored but unrun. Template and definition
changes keep their existing validation, quotas, concurrency checks and cache
invalidation after successful commit. This status does not
certify all administrative actions or provider acceptance.
The retained administrative regression fixture now uses real Local login for
27 tenant cases; all 94 existing cases remain active and unrun. A rolled-back
human attempt is not automatically replayed with its original authority.

Tenant API-key policy updates now retain the original human and persisted key
owner through commit, using a single transaction attempt. Seven new real-body
controls are authored and unrun. A wider source audit identified additional
definition, session-template, projection, room, plan and direct governance owners
still requiring implementation or genuine regression integration; earlier repairs
do not certify those neighboring actions.

Tenant definition purge and Event definition creation are now source-reviewed.
Purge retains dependency checks and atomic audit; creation retains parent ownership
and quotas. Revoked original authority rejects their entire transaction. Twenty
genuine controls are authored but unrun. The full sixteen-owner definition,
value and session-template packet is now source-reviewed, preserving validation,
quotas, concurrency and cache invalidation after acknowledgment. All 281 new/
retained invocations remain unrun, so this is not provider acceptance.

Human managed-provider provisioning is being separated from worker provisioning.
Its command now selects the original instance control-plane database owner before
constructing services and rejects worker-operation payloads. This does not grant
generic cross-tenant writes: the fixed provisioning contracts and compilers are
source-reviewed, while physical settlement integration remains under review. Seventeen source/routing
controls are authored but unrun; this is not a completed provisioning feature or
a provider-readiness claim.
Its enrollment settlement foundation queues follow-up work until the original
database owner acknowledges commit and releases its transaction. An uncertain
acknowledgement is not an ordinary rejection or permission to retry. Ten controls
for that foundation are authored and unrun. Human provisioning integration is now
source-reviewed; its actual-handler and retained linkage controls remain unrun.
Malformed Local linkage refuses provisioning without creating tenant state.
Customer replay preserves the existing result shape and leaves its stored
organizer membership intact. This does not certify provider readiness.
The PostgreSQL/SQLite provisioning registration now explicitly requires co-located
identity sources. Installation hooks are written but have not been executed.
Direct governance PATCH actions now select their joined Settings owner before
constructing services; their exact producers are authored and under review.
The 87 governance and 34 provisioning DATA cases remain unrun, so this is source
progress rather than operational or provider acceptance.
The provisioning compiler owns the complete tenant, mandatory documents,
administrator linkage and optional organizer graph. Local linkage does not
change credentials. Rejected attempts roll back the whole graph; an exact
customer replay does not create another tenant or consume another capacity slot.
Both PostgreSQL and SQLite require their original physical owner and terminal
completion seal. These DATA sources are reviewed, but none of their 34 controls
has been executed.

The PostgreSQL provisioning writer now checks the exact shared source writers
installed by other administrative protocols. A disabled, replaced or partially
installed writer rejects admission rather than silently weakening protection.
Only the declared migrator may install provisioning while later prerequisites
are wholly absent; application traffic requires the complete selected contract.
Independent Identity deployments do not install this co-located provisioning
protocol, and neighboring administrative guards permit that whole absence.
Any partial provisioning footprint is still rejected. These checks and their
regression controls are source-complete, not yet tested on PostgreSQL.

Tenant-plan cloning, version creation/update/publication/archive and assignment
switch/rollback now retain the original instance administrator through their
committing transaction. Revoked credentials, grants or support authority reject
all of that attempt's effects. Existing published-content protection, pinned
assignments, explicit settings application and exact rollback eligibility remain.
These seven owners are source-reviewed; their 36 new Local controls and retained
HTTP cases have not been executed.

Tenant role-grant and organization/group member changes now retain the original
account and support-session horizon through one committing transaction. Protected
membership results are released only after final admission. Intentional
self-demotion or revocation remains allowed when the existing rules allow it,
including preserving the last administrator; unrelated role/binding changes
reject the operation. Group permissions and public Group views are unchanged.
Read-only support cannot perform a Group membership write. Thirty-seven genuine
login controls are authored but unrun; this is not blanket account-administration
or provider acceptance.

Event team assignment, email assignment and revocation now retain the original
account, Event decision and support deadline through assignment and audit commit.
Team and assignable-role results are released only after final admission. Email
resolution cannot substitute another target or replay its inner assignment.
An authorized self-revocation remains possible; unrelated new authority rejects
the transaction. Existing role delegation limits and the separate ownership
transfer requirement still apply.

Thirty-eight genuine-login controls are authored but unrun. Production service
composition and PostgreSQL contract source are integrated; installation and
provider acceptance remain unverified. The changed contract requires explicit
migrator installation before traffic and rejects stale readiness. This does not
certify other account or ownership-transfer operations.

Local identity create, reset, reconciliation and list/status disclosure now
retain original administrator and support authority through each actual Identity
transaction. An authorized self-reset can complete its exact credential change
without acquiring new authority. If later binding fails, an already committed
pending identity remains recoverable by a newly authorized request. An unknown
commit/rollback result does not disclose credentials or authorize automatic replay.

The delivered human administration path currently supports co-located SQLite
only. PostgreSQL integration is in progress; external Identity and other database
providers remain refused. Twenty-six genuine-login controls are authored but
unrun. This is not complete topology or lifecycle acceptance.

The PostgreSQL Local administration capability and co-located migration hooks
are authored, but not installed or accepted. The dedicated Runtime executor
has no direct lifecycle-table grants. Its fixed operations enforce the exact
creation, binding, activation and reset group; activation preserves the password
hash and reset preserves original verification provenance. The managed Identity
bridge now retains configured validators/hashing and completes every physical
stage on the selected Runtime connection before checking original deadlines.
It withholds temporary credential issuance after failed or unknown settlement
or a changed expected credential image. All 60 native controls remain unrun;
these source paths do not establish provider acceptance. External Identity,
SQL Server and MySQL remain unsupported by this administrative adapter.

With External Identity selected, human Local reconciliation rejects before
changing credentials or creating application bindings. Independent external
authentication/Core capabilities are not evidence of that administrative owner.
Use a supported co-located topology for this adapter; no automatic cross-database
fallback or retry is introduced.

Support-session start, own-stop, force-stop and protected list/audit results now
retain original authenticated authority through session and lifecycle-audit
settlement. Ending your own forwarded session permits its intended terminal
change without renewing the original support deadline. Existing administrator,
ownership and separate force-stop rules still apply.

This source path now includes SQLite and co-located PostgreSQL. The PostgreSQL
capability writes only the intended session/audit group on the original Runtime
connection, and forwarding/current-session discovery uses bounded execute-only
reads. Migration and DI hooks are authored, not executed. All 71 native and
57 prior SQLite lifecycle/precision controls remain unrun; native-provider
acceptance and external Identity remain open. Post-response request auditing is a separate
boundary, not part of the retained lifecycle transaction.

Instance event-policy patches, onboarding policies, reporting intake and applying
an existing tenant-plan assignment have closed original-operation source paths.
Plan application retains the selected assignment/version and applies Email,
visitor, publication and ordinary settings plus assignment audit together.
A later failure rolls back earlier groups and Email revisions; notifications
follow commit. A target substituted after selecting another tenant is rejected.
Quotas validate the instance ceiling rather than creating quota rows. The plan's
22 genuine-login and 22 native controls, installation and provider acceptance
remain unrun; deployments must use the current migration-installed contract,
not silently repair stale database authority.

### Updating Administrative API Clients

Deploy regenerated clients with the matching API version. Provider credentials
belong in the selected deployment secret authority, not administrative JSON:
AI model creation and instance AI patches no longer accept `apiKey`, and
authentication-provider patches no longer accept `googleClientSecret`.
Tenant onboarding settings and completion no longer carry `aiAssistantApiKey`.
Settings and onboarding reads no longer disclose the removed plaintext
authentication secrets or the former AI/Cerbos credential-status properties;
update automation that consumed those fields.

The internal authentication-provider onboarding GET
`/api/instanceonboarding/auth-provider-configuration/internal` is removed.
Use the supported settings and onboarding operations exposed by the current
client contract. Do not recover credentials by restoring an internal route.

AI proposed-action confirmation now requires its request body. Registration
order creation requires `Idempotency-Key`; retain the same key when retrying
the same intent after an uncertain response. A rollback must preserve secret
isolation and operation-key continuity, not restore plaintext fields or create
a second order by generating a new key.

### Static Localization Bundles

Instance administrators can import a static translation bundle with
`POST /api/admin/localization/bundle`, supplying `languageCode` and a flat
`translations` object. Use a supported language and unique keys starting with
`ui.`, `lookup.` or `payment.`. Duplicate fields/keys, unknown envelope fields,
null values, arrays and nested objects are rejected; no last-value-wins import
is performed. Text such as `{{name}}` or HTML remains literal translation data,
not a server-executed template.

Keep the JSON artifact and persisted output within 4 MiB (the POST request allows
an additional 1 KiB of envelope overhead). A bundle allows 16,384 entries, keys
of up to 512 characters, values of up to 16,384 characters, and at most 524,288
combined key/value UTF-16 characters. Empty text values are allowed.

The published OpenAPI import schema requires both `languageCode` and
`translations` and describes translations as a bounded dictionary of strings.
Generated clients can therefore construct the same envelope. Server validation
also enforces duplicate-key rejection, supported languages and the combined
text budget; satisfying the schema alone does not authorize or validate an import.

An invalid import preserves the existing file and caches. Correct or split the
bundle before retrying. Invalid manually mounted writable files are ignored in
favor of embedded defaults, not partially merged. File validation completes
before the writer opens its temporary output. Multi-instance deployments still
need a shared persistent bundle volume.

### Getting Started After Setup

Open `/settings/instance?section=getting-started` after fresh sign-in. The checklist
separates **Required before public disclosure**, **Required before paid events**,
and **Recommended**. Each server-reported check retains its current state,
requirement category, reason and remediation authority. An optional capability
shown under Recommended does not become a setup prerequisite.

Use the offered operator-identity and provider actions; missing actions are not
permissions you can recover by changing a browser role or URL. The identity editor
uses the server's operator-kind and country choices, with labeled controls and
visible guidance. If choices are unavailable, refresh rather than inventing a
country or legal form. Registration identifiers are optional; complete only facts
that apply to your operator. Saving uses the document's current revision and edit
permission. Instance and directory identities remain independent.

### Prepare A Private Default Directory

A directory in **Provisioning** is private even when its operator identity is
complete. The completed setup administrator can manage its branding and operator
identity with the existing default-tenant permissions. Saving those documents
never publishes the directory.

Local administrators can replace the temporary password and sign in afresh while
the directory remains private. Replacement requires the protected, short-lived
challenge from temporary-password sign-in, not the completed setup secret. The
private administrator session does not grant public directory access.

In SingleTenant mode, authenticated instance administrators can read the default
directory through the instance-administration tenant-detail API and follow its
`activate` link when offered. Only the fixed default directory is accepted; this
does not enable the multi-tenant fleet console. Document editing still requires
that directory's tenant-admin grant, not merely a platform role.

Activation is explicit and rechecks current identity and configured capacity. If
another administrator changes the identity first, activation uses that latest
revision. Reload a conflicting edit instead of overwriting it. Repeating a
successful activation does not create duplicate lifecycle history. Public pages
remain unavailable until activation succeeds. No migration or new setting is
needed; setup-completion and getting-started screens are unchanged by this API
capability.

### Local Accounts

Open `/settings/instance?section=local-accounts`. The Local accounts entry appears
only when the server advertises the capability for your current instance access.
Tenant administration does not grant permission to manage these shared credentials.

Use **Create local account** to enter the person's email and name. A successful
creation reveals a generated temporary password once. Record the operation ID,
hand over the password privately, then select **Dismiss credential**. The recipient
must replace that temporary password before ordinary sign-in; no email delivery is
required for this supervised handover. Do not save credential-bearing responses in
logs, scripts or support tickets.

For an eligible existing account, **Issue temporary credential** requires a reason.
Reset invalidates its previous credential and sessions, preserves verification,
and requires another private password replacement. Missing or invalid credential
metadata is shown as unknown; the screen does not invent a reset action for it.

If an issuance response is lost, keep its operation ID and use **Check operation**.
Status reads never repeat issuance or reveal the password. Use **Reconcile** only
when offered to complete interrupted account linking. If handover is no longer
possible, inspect the resulting account and perform a separately authorized reset
with a new operation ID. Refreshing or reopening the section cannot recover a
previously dismissed password. See [Authentication](../security-and-identity/authentication.md)
for the sign-in and recovery boundaries.

An access-denied or conflict response can arrive after the credential change was
saved. Keep the original operation reference until its status is known; cancelling
its form or checking another operation does not resolve it. These references stay
only in the open component, so record them before leaving the section.

Successful issuance and recovery refresh the account details on your current
page without dismissing the password handover. Accounts are ordered oldest-first:
a newly created account may be on a later page rather than page one.
If refreshing account details fails, further resets remain unavailable until a
refresh succeeds; the current password handover stays visible.

### Locked publication overrides

Publication workflows that already select locked-override cleanup remove the
tenant's override and reveal the locked instance value. The server recompiles
the complete reporting-intake, approval and submission policy before saving;
an unsafe combination is rejected. Ordinary locked-setting rejection remains.
Unlocked settings are saved as values, not deleted. Cleanup retains the original
administrator's target: a foreign tenant, an instance-shaped tenant update or a
tenant-shaped instance update is rejected before deriving effects. Supplied
payloads cannot replace the original prepared changes. Cleanup also retains the
administrator, credential and support-session authority through commit, and
expiry rolls it back. Source and regression changes are authored; runtime checks
remain pending under the security programme's verification freeze.

### Event resource governance

Resource policy uses the existing native settings and configuration-manifest
workflow. Instance administrators set the ceilings; tenant administrators may
restrict them, not expand them. Instance locks remain effective in SingleTenant
mode. A batch containing an invalid or widening choice is rejected as a whole.
The **Event resources** section in instance governance shows the current
instance policy, its source and locks. The corresponding section under tenant
policies shows effective tenant values, including inherited instance limits.
The displayed configured source identifies where the stored override came
from; after an instance administrator tightens a ceiling, the effective value
can be lower than a tenant's earlier override.
An unavailable edit action or locked setting is read-only; refresh after an
access denial or policy change before editing again. Tenant controls offer
only values at or below the current effective numeric ceilings and subsets of
currently enabled delivery types, audiences, file types and HTTPS origins.
Only the instance section can opt in to unscanned documents.
After a save or denial, the policy controls reload and keyboard focus returns
to the original control when available, or to the section heading if that
control was replaced; the result is announced separately.

API clients can read `GET /api/settings/instance/event-resources` as an
instance administrator, or `GET /api/settings/tenant/EventResources` in the
current tenant. Both return private effective settings with `source`,
`isLocked`, `canEdit` and `reason`. Follow the returned `edit` HAL link when
present to submit a strict `UpdateSettingBatchDto` by `PUT`; it is not a
permanent permission. Instance reads and writes require current instance
administrator authority. A signed-in non-administrator receives 403, and an
anonymous caller receives 401. Tenant writes still enforce instance ceilings
and fresh locks on the server even if the browser has an older edit link.
Before serving resource reads on an existing development database, apply the
latest generated application migrations using the selected provider's
migration service. This includes `AlignAtprotoRecordDidCollation`, which keeps
federated event eligibility and resource access consistent; MariaDB uses the
MySQL migration catalog. Until the migration is applied, SQL Server may deny
affected resource reads as unavailable rather than expose data using a
mismatched identifier comparison.

| Setting | Default and operator effect |
| --- | --- |
| `event_resources.enabled_delivery_types` | Stored files and external links; disabling a type removes attendee access to affected resources. |
| `event_resources.enabled_audiences` | All supported audiences; disabling one hides resources whose policy uses it. |
| `event_resources.permitted_file_types` | PDF, OOXML Word (`.docx`) and OOXML PowerPoint (`.pptx`) only. |
| `event_resources.max_upload_bytes` | 10 MiB, also bounded by the current storage limits. |
| `event_resources.allow_unscanned_documents` | `false`; only instance administrators can opt in. Tenant overrides are rejected. |
| `event_resources.external_origins` | Empty; explicitly allow each HTTPS origin before external delivery. |
| `event_resources.audit_retention_days` | 30 days; allowed range 0–90. Zero disables collection; tenants may shorten retention. |
| `event_resources.max_active_resources` | 500; allowed range 0–500. Zero prevents new resource creation. |

Origin entries are authoritative HTTPS authorities, such as
`https://materials.example.org`. Do not include a trailing slash, path, query,
fragment, credentials or wildcard. Alternate spellings, including uppercase
hosts and explicit default ports, are rejected rather than silently normalized.
Tenant origin lists must be subsets of the instance list.

Tightening an instance ceiling takes effect in fresh resource checks even if an
older tenant override remains stored. Existing resources can still be repaired,
withdrawn or deleted by their authorized managers; a disabled policy does not
trap them in an unmanageable state. Enabling a delivery type does not bypass
audience eligibility, provider authorization or file safety checks.

Resource-management audit cleanup runs hourly through the platform scheduler,
including when resource delivery is disabled. It removes complete expired rows;
retention zero removes existing rows on the next sweep. Subject erasure clears
manager attribution without deleting shared event resources. Keep the scheduler
enabled while retained audit data remains.

On PostgreSQL, install the current audit-owner discovery capability before
enabling cleanup. Discovery returns tenant identities only; every deletion batch
uses that tenant's ordinary database authority and current retention policy.
Suspended tenants remain eligible when they own audit rows. A missing capability
or invalid policy fails cleanup visibly; restore the declared authority or
policy rather than granting broad Global access to private audit data.

### Resource-policy deployment activation (operator API)

Remote event-resource policies use a deployment-level activation fence. This
operator API is separate from resource authoring or publication; activating a
provider does not enable a resource or bypass instance governance.

Use `/api/event-resource-provider-activation` with current instance administrator
authority. Responses are private and non-cacheable.
The binding read also requires an authenticated account: anonymous callers
receive 401, while signed-in accounts without current instance-administrator
authority receive 403.
`Idempotency-Key` does not replay an earlier activation receipt: each retry
rechecks current administrator authority and deployment state.

1. Read `GET /bindings` for the current binding revision. Register or update a
   deployment through `PUT /bindings`, supplying its UUIDv7 deployment ID,
   normalized gRPC endpoint aliases, explicit scope, policy version and the
   expected binding revision. Empty scope selects Cerbos root policy. Keep every
   alias for the same physical policy deployment under the same ID; aliases
   cannot later be removed or transferred to another deployment.
2. Before an external policy writer changes a bound deployment, call
   `POST /begin` with its deployment ID. Resource authority closes immediately.
   Known application-managed publishers fence their own writes, but every
   external writer must participate too.
3. Stop previous writers and verify the declared scope/version across all
   reachable replicas. Call `POST /activate` with the operation ID and epoch from
   the current operation receipt, the scope/version, confirmation that previous
   writers have stopped, the positive reachable-replica count, and the number
   running the declared policy. Both counts must agree. Set
   `frozenParentPolicyContractConfirmed` to `true` only after verifying the
   parent event moderation policies and their derived-role dependencies against
   the frozen principal contract. Custom rules that use the legacy raw `nowUtc`
   attribute must be migrated to derived-time predicates before activation.
   Omitting or declining this confirmation keeps the operation closed.
4. If publication is uncertain, cancelled, incomplete or failed, keep access
   closed. Start a new operation, repair the deployment, verify convergence and
   activate that new operation. An older receipt cannot activate a newer
   operation. After a binding conflict, reload the binding revision before
   making another change.

The application cannot discover undeclared aliases or unannounced remote edits.
Replica counts and convergence are operator attestations, not measurements
inferred from an upload response. Do not treat the local epoch as a remote policy
revision. Manage these bindings only through this protocol, not generic settings
editing or configuration-manifest import.

Every control command now retains its original authenticated administrator and
database selection until the binding document and activation row have committed
together. A changed binding, activation stamp, relevant configuration row or
missing-row observation rejects that attempt; retry by reloading current state,
not by reusing an earlier receipt. Unrelated pending settings are not saved with
the operation.

In-app policy synchronization selects and validates its original target before
closing the deployment. No schema, policy or reload request is sent until that
closure is acknowledged as committed and its local lock is released. An unknown
commit outcome is a failure, even when the database might have committed: inspect
current state and begin a new operation. A setup secret can publish only to a
validated unbound target; a bound resource deployment requires current human
instance-administrator authority. Inspect-only performs no activation change,
and each foreground upload still requires a fresh one-time Admin credential pair.

### Background Scheduler

When enabled, **Background Scheduler** in Instance Settings shows the current
scheduler and job state. If a read is rate-limited or temporarily unavailable,
the section shows an error instead of stale job controls. Wait for the service
to recover, then select **Refresh**; refreshing does not run, pause or resume jobs.

### Tenant Lifecycle Management
- **Create Tenant**: Provision a new community tenant with a unique slug and primary administrator.
- **Tenant States**:
  - *Active*: Fully operational; events can be published and registered.
  - *Suspended*: Public routes return inactive notices; administrative reads remain accessible.
  - *Archived*: Read-only state prior to scheduled purge.
- **Destructive Purge**: Scheduling a tenant purge requires explicit reason confirmation and typing the tenant slug.

### Platform Monetization
Navigate to `/settings/instance` $\to$ **Monetization** (see [Paid Events & Payouts](../events-and-ticketing/paid-events-and-payouts.md)):
- **Platform Fee Policy**: Set platform fees across paid ticket sales (configured in basis points and optional fixed charges per currency).
- **Platform Contribution**: Enable optional voluntary contributions during checkout with customizable heading and body text.
- *Note*: Changes use optimistic concurrency revisions; concurrent edits fail safely to prevent accidental overwrites.

---

## 3. Tenant Administration & White-Labeling

Tenant administrators manage their community experience via `/settings/admin`:

### Branding & Appearance
- **Themes & Colors**: Configure brand primary and secondary colors (see [White-Labeling](white-labeling.md)).
- **Logos & Favicons**: Upload high-resolution community assets.
- **Navigation & Links**: Customize top navigation links and header menus.

### Custom Footers
- Choose footer layouts, social links, and copyright notices.
- Built-in governance locks prevent tenants from removing legally required disclosures (such as Terms of Service and Privacy Policy).

### Custom Registration Properties
- Define custom questions and fields for events and attendee registrations (see [Custom Properties Governance](../events-and-ticketing/custom-properties.md)).
- Set exposure levels: `Public` (visible on listing), `Private` (visible to organizers only), or `System`.

### Moving Rooms Between Locations

A room can move to another location in the same tenant only when no session,
session group, or agenda item still references it. This includes any retained
reference on a deleted record: moving a room never rewrites historical schedule
links. Non-location edits remain available for scheduled rooms. Create a replacement
room at the destination if the original room must remain linked to a schedule.

A successful move preserves the room ID and applies the final name and other
submitted edits together. Room names must be unique within the destination
location. If the room or its schedule references change concurrently, reload before
retrying; a rejected move leaves the original placement intact.

---

## 4. Organization & Group Governance

Organizers create Organizations and Groups to co-host events:
- **Verification Badges**: Instance and tenant admins can mark verified organizations to give attendees trust.
- **Membership Management**: Assign Owner, Admin, and Member roles within an organization (see [Admin Hierarchy](admin-hierarchy.md)).
- **Payment Connections**: Organizers onboard their own [Stripe Connect](../events-and-ticketing/paid-events-and-payouts.md) accounts directly through organization settings to receive ticket payouts.

---

## Related Guides & Next Steps

* **[Admin Hierarchy & Scopes](admin-hierarchy.md)** — Review permission sets across Instance, Tenant, and Event roles.
* **[White-Labeling & Branding](white-labeling.md)** — Detailed token customization and design tokens.
* **[Custom Domains & SEO](custom-domains-and-seo.md)** — Route custom domains to tenant storefronts.
* **[Configuration Manifests](../configuration-and-operations/configuration-manifests.md)** — Export and import declarative tenant settings.
