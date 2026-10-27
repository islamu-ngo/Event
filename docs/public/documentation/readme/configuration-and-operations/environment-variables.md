---
description: Comprehensive reference for all baseline, advanced, and profile-specific environment variables.
---

# Environment Variables Reference


ISLAMU Event follows **Convention over Configuration**. The platform includes sensible defaults for all advanced operational dials so that everyday self-hosters can launch a production instance with minimal friction.

> [!TIP]
> **Baseline vs. Advanced Configuration:**
> - **`.env.example` (Baseline):** Contains the essential configuration keys needed to run a standard instance (URLs, database credentials, authentication secrets, legal identity, and local storage).
> - **This Document (Exhaustive Reference):** Catalogs every supported environment variable across core services, advanced performance dials, and auxiliary service profiles. If a variable is marked `Advanced`, it is omitted from `.env.example` and uses its built-in default unless you explicitly override it.

---

## Runtime actor ownership access

The existing Runtime database login can read only the Actor linkage identifiers
needed by enrollment, reporting, admission decisions and ticket purchasing:
actor ID, user/group/organization IDs and the deletion flag, plus tenant ID,
actor ID and deletion flag on tenant memberships. No new credential or
environment variable is required. This access does not include private Actor
profiles, moderation fields, writes or permission delegation.

C# workflows authorize the caller and enforce the selected tenant; membership
RLS remains a database safeguard. Direct access with a stolen Runtime credential
can bypass C# admission and enumerate the permitted Actor linkage metadata.
Treat that credential as sensitive even though private profile columns remain
unavailable. Installation refuses unexpected privileges instead of silently
normalizing them; investigate drift before restarting migration or enrollment.

## Guided Setup Adds No Environment Inputs
Offline preparation uses separately installed
[ISLAMU Configurator](https://github.com/ISLAMU/Configurator) and Event's local
definition, not new environment switches. Follow
[the external handoff](../../../self-hosting/setup-assistant.md#external-configurator-handoff).
Event owns its environment catalogue and validates runtime binding; Configurator
does not become a secret authority. Keep credentials and generated dotenv files
on a protected native host. The public browser accepts only declared public
choices. This repository/tooling extraction needs no database migration.

Legal identity seeds are optional during installation. Configure disclosure and
paid-event identity later through authenticated administration; authoritative
kind/country choices come from the server rather than another environment list.
The [getting-started groups](../administration-and-branding/admin-guide.md#getting-started-after-setup)
do not change provider responsibility, secret authority or SQLite processing bounds.

### Consumer identity and API token policy

Compose/Aspire use `INFISICAL_API_CLIENT_ID/CLIENT_SECRET`,
`INFISICAL_BFF_CLIENT_ID/CLIENT_SECRET`, `INFISICAL_COMBINED_CLIENT_ID/CLIENT_SECRET`
and `INFISICAL_MIGRATION_CLIENT_ID/CLIENT_SECRET`. Configure only the consumers
your topology launches. URL, project and environment remain shared nonsecret
coordinates; the machine identities and their ACLs are independent. The generic
`INFISICAL_CLIENT_ID/CLIENT_SECRET` fields are direct-process inputs, not an
orchestrator fallback. All credentials require process/deployment restart.

For each enabled Infisical consumer, use the same `INFISICAL_API_`,
`INFISICAL_BFF_`, `INFISICAL_COMBINED_` or `INFISICAL_MIGRATION_` prefix with
`TOKEN_LIFETIME` (`Finite` or `Periodic`), `TOKEN_PERIOD_SECONDS` (required only
for periodic identities), `RENEW_BEFORE_SECONDS` (default 60), and
`REQUEST_TIMEOUT_SECONDS` (default 15, maximum 60). A finite period is unset or
blank; a nonblank value is rejected. These policies are independent, not a
shared fallback. Direct process launches instead use
`SecretProvider__Infisical__TokenLifetime/TokenPeriodSeconds/RenewBeforeSeconds/RequestTimeoutSeconds`.
See [secret lifecycle and recovery](secrets.md#fail-closed-authority-status).

`KEYCLOAK__AUDIENCE` is public configuration identifying the API resource
(default `islamu-event-api`). `KEYCLOAK__PERMITTEDCLIENTS__0` is an optional,
separate requesting-client restriction, also public. Add contiguous indices for
more clients; Compose needs an explicit service entry for each additional index.
Omit unused entries completely: a blank member is invalid, and permitted clients
never expand accepted audiences. See [the identity cutover](infisical.md#consumer-isolation-cutover)
for ACLs, provider-resource injection and cold-restart requirements.

## Removed edition inputs

There is one supported build, with the same dependency versions for contributors,
self-hosters and hosted deployments. No Lucky Penny license key or edition choice
is required. Remove these obsolete inputs from deployment environments, Infisical
and CI configuration:

- `USE_COMMERCIAL_LUCKYPENNY`, `LUCKYPENNY_LICENSE_KEY`,
  `AUTOMAPPER_COMMERCIAL_VERSION`, `MEDIATR_COMMERCIAL_VERSION`.
- `Licensing__LuckyPenny__Enabled`, `Licensing__LuckyPenny__LicenseKey` and
  old `Licensing__AutoMapper__LicenseKey` / `Licensing__MediatR__LicenseKey` entries.
- Docker build argument `USE_COMMERCIAL_LUCKYPENNY_LIBS` and MSBuild properties
  `UseCommercialLuckyPennyLibraries`, `AutoMapperCommercialVersion`,
  `MediatRCommercialVersion`.

Do not replace them with another edition flag. Rebuild using the repository's
locked dependency graph. Other provider credentials and licensing obligations
are unchanged; no database migration is required for this removal.

## 1. Core Deployment & Networking

| Variable | Status | Default | Description |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | **Baseline** | `Production` | Runtime mode: `Production`, `Staging`, `Development`, or `Testing`. |
| `PUBLIC_BASE_URL` | Required for public exposure | Localhost hosting only when absent | Canonical public HTTPS authority for host admission and sensitive onboarding links. Never use an internal listening address. |
| `EFFECTIVEHOSTAUTHORITY__INSTANCEHOSTS__0` | Advanced | `islamu-event-api` in Compose; otherwise none | Additional exact instance/API/admin host. No wildcard. Add explicit Compose mappings for further indices. |
| `EFFECTIVEHOSTAUTHORITY__SUBDOMAINBASEDOMAINS__0` | Advanced | None | Instance-owned base for single-label tenant subdomains; must also be an approved instance host. |
| `FORWARDEDHEADERSTRUST__FORWARDLIMIT` | Advanced | `1` | Trusted forwarding hop ceiling, from 1 to 10. |
| `FORWARDEDHEADERSTRUST__TRUSTLOOPBACKPROXY` | Advanced | `false` in Compose | Explicitly trust loopback as a proxy only when that matches the deployment. |
| `FORWARDEDHEADERSTRUST__KNOWNPROXIES__0` | Advanced | None | Exact ingress/BFF proxy address. Additional indexed fields require explicit service projection. |
| `FORWARDEDHEADERSTRUST__KNOWNNETWORKS__0` | Advanced | None | Bounded trusted proxy CIDR; trust-all networks are rejected. Combined proxy/network list ceiling is 32. |
| `OPERATIONALENDPOINTS__ADDRESS` | Advanced | None | Exact local loopback/private IP of the separate operational listener. This does not bind a listener. |
| `OPERATIONALENDPOINTS__PORT` | Advanced | Disabled | Local port of that separate listener, 1–65535. Never use the public application listener. |
| `API_HTTP_PORT` | **Baseline** | `7039` | Internal HTTP port for `Explore.API`. |
| `UI_HTTP_PORT` | **Baseline** | `7002` | Internal HTTP port for `Explore.Blazor` (BFF). |
| `KEYCLOAK_HTTP_PORT` | **Baseline** | `8080` | Internal HTTP port binding for Keycloak container. |
| `MAILPIT_SMTP_PORT` | **Baseline** | `1025` | SMTP port binding for local Mailpit container. |
| `MAILPIT_UI_PORT` | **Baseline** | `8025` | Webmail UI port binding for local Mailpit container. |
| `DEPLOYMENT_MODE` | **Baseline** | `SingleTenant` | Multi-tenancy mode: `SingleTenant` or `multi_tenant`. Must be set before first-run onboarding. |
| `BFF_ADMIN_HOSTS` | Advanced | None | Comma-separated list of dedicated admin hostnames (e.g., `admin.example.org`) to render the Instance Console. |

> [!NOTE]
> **We recommend:** Keep `DEPLOYMENT_MODE=SingleTenant` for individual community centers, mosques, and local non-profits. Use `multi_tenant` only if operating a multi-chapter umbrella organization hosting independent community spaces.

### Private readiness, scraping and domain activation

Bind a separate Kestrel operational endpoint on the exact private IP/port selected
above, for example a loopback listener reached by a local monitoring agent.
Configure that binding in the deployment's Kestrel endpoint settings and keep it
out of public ingress and published public container ports. Remote monitoring
requires a private network and firewall ACL. `/health`, webhook readiness under
`/health/webhooks/`, and `/metrics` return 404 on every other local listener,
even with a logged-in application user or forged Host/forwarding headers.
Keep `/alive` for public orchestration liveness: it returns only 200/503 and no
provider, tenant, or diagnostic body. Configure the probe's Host as an approved
instance host.

The API/Combined host must receive forwarding from a bounded trusted proxy with
all three host/proto/peer fields. Do not expose the backend listener as a way
around ingress: the baseline Compose API port is published on host loopback only;
the BFF still uses the internal service network. Do not treat `AllowedHosts=*` as tenant domain approval.
Custom domains additionally require the operator proof lifecycle in
[custom domains](../administration-and-branding/custom-domains-and-seo.md#operator-ownership-attestations).
Structured .NET configuration may supply complete arrays; the baseline Compose
environment maps only the first optional proxy/network/instance-host/base slot.

### Independent database credentials

For server databases, inject independent `DATABASE_RUNTIME_USERNAME/PASSWORD`,
`DATABASE_GLOBAL_USERNAME/PASSWORD`, and `DATABASE_MIGRATOR_USERNAME/PASSWORD`
through the selected approved secret authority. Omission and an explicit empty
value both remain empty in rendered Compose configuration; neither gets a
fallback password. Startup rejects missing/whitespace passwords, the retired
`explore` default, and username-as-password. Setup readiness additionally rejects
shared role usernames or password values and preserves protected-value
provenance without printing credentials.
Each role must use its own fields: generic `Database:Username/Password` and
`DATABASE_USERNAME/PASSWORD` do not substitute for Runtime/Migrator authority.
An explicit empty structured role value also cannot fall back to an environment
alias. Inject the exact password bytes; binding does not trim a real password.

Local Identity C# lifecycle workflows select a separate
`DATABASE_LOCAL_IDENTITY_WRITER_USERNAME/PASSWORD` purpose before resolving their
stores or handlers. Do not reuse Runtime, Global or Migrator credentials. With
external identity storage, additionally inject the external database's
`IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME/PASSWORD`. These accounts may
modify their fixed identity inventory; ordinary Runtime and Global do not inherit
those rights. Credential theft can bypass C# workflow rules within those grants.

Login synchronization also needs limited read-only bootstrap metadata and the
existence of an erased identity. Its writer can read bootstrap status, mode,
provider, completing-user ID and ordering keys, plus erasure subject IDs—not
configured-administrator fingerprints or private erasure receipts, state and
fences. A stolen writer credential can enumerate that metadata. Reprovision a
fresh development permission profile when adopting this inventory; readiness
rejects an older partial profile instead of silently expanding its permissions.

Only the migration service receives migration credentials; API runtime does not
inherit them. Provision real database roles/grants separately: setup readiness
is not proof of permission separation. Runtime must not have DDL, superuser or
PostgreSQL `BYPASSRLS`; Global owns only the constrained capabilities it needs.
SQLite does not require these server credentials. Resolve generated references
through the selected authority before deployment, rotate through the owning
credential procedure, and never paste rendered secret-bearing machine
configuration into logs or support reports.

Instance-key operations also require four independent backend credential pairs:

| Environment pair (`_USERNAME` and `_PASSWORD`) | Direct .NET section | Purpose |
|---|---|---|
| `DATABASE_INSTANCE_KEY_READ` | `Database:InstanceKeyRead` | Instance-key metadata |
| `DATABASE_INSTANCE_KEY_WRITE` | `Database:InstanceKeyWrite` | Fixed policy changes and revocation |
| `DATABASE_INSTANCE_KEY_ISSUANCE` | `Database:InstanceKeyIssuance` | Self-owned creation and metadata-only replay |
| `DATABASE_PLATFORM_KEY_REPORT` | `Database:PlatformKeyReport` | Historical platform usage aggregates |

Give each server login a distinct password and only its matching PostgreSQL
executor role: `event_instance_key_read_executor`,
`event_instance_key_write_executor`, `event_instance_key_issuance_executor`, or
`event_platform_key_report_executor`. The migration service installs those
capabilities; deployment provisioning assigns login membership with inheritance
enabled and role switching disabled. Do not grant owner/migrator membership,
direct private-table rights, or combine the four executor memberships.

These credentials do not inherit Runtime or Global values. Missing credentials
or native capability admission fail startup rather than silently returning an
incomplete report or advertising keys that cannot be revoked. Keep them
server-side, restart API pools after rotation, and do not forward them to BFF
processes. SQLite uses the selected persisted file without server credentials.
Native provider verification and generated catalogue refresh are still queued
under the programme's final-only assurance schedule.

Persisted machine footer **link/group PATCH** adds no credential pair.
The migration service installs `event_machine_footer_runtime`; provision that
execute-only role to the existing ordinary Runtime login with inheritance
enabled and role switching disabled. Its owner and migrator roles must never
belong to API logins. Do not give this executor to Global, authentication,
scanner, or any of the four independent key-management/report logins.

This capability checks a stored InstanceAdmin key and the exact tenant,
group and optional link on the Runtime write transaction. It does not expose
key hashes, secrets, usage or enumeration and cannot edit settings, foreign
groups or instance-default groups. Missing or contaminated grants deny the
operation; API requests never provision privileges. Installation/provisioning
and native tests remain queued for final assurance.

Room updates use the separately installed `event_machine_room_runtime` role
on the same existing Runtime login, also with inheritance enabled and role
switching disabled. It exposes only current-key authority for an exact room
and its actual source/requested destination. Keep its owner/migrator roles
away from consumers and its executor away from Global, authentication,
scanner and key-management/report logins. Do not grant this executor footer
execution or direct table rights. An API Runtime login may hold both separate
executor memberships when needed; no new credential or environment pair is
introduced. This preserves Cerbos's existing InstanceAdmin room cell without
changing Local permissions. Provisioning and native checks remain unrun.

---

## External Data Protection

These are mandatory for the selected host partition and have no defaults.
Split API uses `API`, Split UI uses `BFF`, and Standalone uses `COMBINED`.
Do not inject another process's ring. See [the provisioning and recovery
runbook](secrets.md#external-data-protection-rings).

| Variables | Authority | Meaning |
| --- | --- | --- |
| `EVENT_DP_API_RING`, `EVENT_DP_BFF_RING`, `EVENT_DP_COMBINED_RING` | Selected Environment / Infisical / local User Secrets | Secret versioned ring JSON, at most 16 documents and 32 KiB; no DB/Redis/file fallback. |
| `EVENT_DP_API_ADMISSION`, `EVENT_DP_BFF_ADMISSION`, `EVENT_DP_COMBINED_ADMISSION` | Same selected authority, separately maintained admission record | Complete historical inventory, revocations, ring hash, active ID, generation and bounded serving lease. |
| `EVENT_DP_API_ADMISSION_SHA256`, `EVENT_DP_BFF_ADMISSION_SHA256`, `EVENT_DP_COMBINED_ADMISSION_SHA256` | Independent operator-controlled process environment only | Uppercase SHA-256 of the exact approved admission JSON; must survive candidate/database restore. |
| `EVENT_DP_API_MINIMUM_GENERATION`, `EVENT_DP_BFF_MINIMUM_GENERATION`, `EVENT_DP_COMBINED_MINIMUM_GENERATION` | Independent operator-controlled process environment only | Positive monotonic floor; never lower it during restore. |

An ordinary rollbackable `.env` containing both candidate and expectations does
not establish independent control. Provision expectations through a separately
protected deployment authority. The runtime cannot prove those operational ACLs.
Infisical roots are `/runtime/<partition>/data-protection` and
`/admission/<partition>/data-protection`, with one exact value per root,
nonrecursive reads, no references and read-only runtime permissions. User Secrets
is limited to Development/Testing; independent pins still use process environment.
Renewal and changes require coordinated restart/drain, not live mutation.

## 2. Database & Relational Persistence

Migration and credential-bearing EF tooling use the explicitly selected
`SECRET_PROVIDER` and migration-purpose acquisition. Supply database variables
to the actual command's process; launching Aspire does not inject a different
terminal's environment. User Secrets is restricted to Development/Testing and
uses the shared `event-shared-secrets` store, with `src/Explore.API` as its CLI
owner. There is no fallback from an unavailable authority to application settings
or a raw connection string.

`DesignTime:ModelOnly` is deliberately **not an environment variable**. Pass
`--DesignTime:ModelOnly=true` to EF after `--` for offline model scaffolding,
together with the explicit provider and any required schema/server-version
fields. It cannot apply migrations or prove database access.

Before upgrading, configure credentials independently for every database role
your deployment selects. Use that role's structured username/password fields or
explicit environment aliases; generic or sibling credentials cannot fill missing
values. An explicitly empty structured value blocks its environment alias.

Before restarting with this version, rotate any server database role password
that is `explore` (ignoring case and surrounding whitespace) or matches its login
name ignoring case. Update that role's password in the selected secret authority
after changing it on the database server. Startup rejects those values rather
than replacing them. Other password whitespace is significant and is preserved.
SQLite does not require server credentials.

Queue readiness now uses a separate Global database login. For server databases,
set `DATABASE_GLOBAL_USERNAME` and `DATABASE_GLOBAL_PASSWORD` (or
`Database:Global:Username` and `Database:Global:Password`) in the selected secret
authority. The Global username must differ from Runtime. SQLite uses a separate
context lease without server credentials.

On PostgreSQL, run application migrations before starting readiness checks, then
have the database administrator grant `event_queue_drain_health_global` to that
Global login with inherited execution and no role switching. Never grant the
health owner or migrator group, private-table reads or queue writes to this login.
The health function returns thirteen queue counts, not private rows or identifiers.
Review and narrow any existing broad Global grants separately; migration does not
replace that operator review. After rotating Global credentials in the database
and selected secret authority, restart API processes to replace their pool.

| Variable | Status | Default | Description |
|---|---|---|---|
| `DATABASE_PROVIDER` | **Baseline** | `PostgreSql` | Selected provider: `PostgreSql`, `Sqlite`, `SqlServer`, `MariaDb`, or `MySql`. |
| `DATABASE_HOST` | **Baseline** | `postgres` | Hostname or IP of the database server. |
| `DATABASE_PORT` | **Baseline** | `5432` | Database port (`5432` for PostgreSQL, `1433` for SQL Server, `3306` for MySQL). |
| `DATABASE_NAME` | **Baseline** | `islamu_event_db` | Target database name. |
| `DATABASE_SCHEMA` | **Baseline** | `islamu_event` | Schema namespace for PostgreSQL/SQL Server (clean table names inside it). |
| `DATABASE_RUNTIME_USERNAME` | **Baseline** | None | Least-privilege credentials used by `Explore.API` for runtime queries. |
| `DATABASE_RUNTIME_PASSWORD` | **Baseline (Secret)** | None | Password for runtime database user. |
| `DATABASE_GLOBAL_USERNAME` | **Baseline** | None | Independent login for API-owned bounded cross-tenant capabilities. |
| `DATABASE_GLOBAL_PASSWORD` | **Baseline (Secret)** | None | Password for the independent Global database login. |
| `DATABASE_MIGRATOR_USERNAME` | **Baseline** | None | DDL-capable credentials used by `Event.MigrationService` to apply migrations. |
| `DATABASE_MIGRATOR_PASSWORD` | **Baseline (Secret)** | None | Password for migration service database user. |
| `DATABASE_TLS_MODE` | **Baseline** | `Prefer` | TLS verification mode: `Disable`, `Prefer`, or `Require`. |
| `DATABASE_TRUST_SERVER_CERTIFICATE` | Advanced | `false` | Set `true` only in local development to trust self-signed TLS certificates. |

Server databases require Global's own username and password; missing values
never fall back to Runtime, Migrator, or root database credentials. The direct
.NET equivalents are `Database__Global__Username` and `Database__Global__Password`.
Global connects through an independent login and pool for explicitly granted
cross-tenant capabilities, not universal `BYPASSRLS` access. On PostgreSQL use
`NOSUPERUSER NOBYPASSRLS`; only the migration service receives DDL authority.
Keep the password server-side in the selected secret authority and restart
consuming API processes after rotation. SQLite needs no login credentials.

For PostgreSQL, run the migration service before starting API consumers.
The policy installer records the expected tenant-policy fingerprints; API pools
check those fingerprints and effective login privileges without changing grants
or policies. Missing or altered policies, table ownership, elevated role
membership, or extra applicable permissive policies stop database acquisition.
The v2 integrity stamp uses stored policy expressions, so changing a connection's
search path does not invalidate unchanged policies. Re-run the migration service
to provision v2 stamps before restarting consumers; older stamps are rejected.
Provision the documented roles and capability grants under migration authority;
supplying Global credentials alone does not grant those capabilities.

Pre-authentication routing reads use a separate short-lived scope before the
request database lease is acquired. This requires no additional credentials or
grants and does not promote Runtime authority. An unpublished tenant's bootstrap
directory binding still grants no administrative access by itself.

Aspire local-data modes use PostgreSQL. Configure the Runtime and Global logins
in the selected secret authority and provision their grants separately: API and
Standalone no longer reuse the local PostgreSQL parent's migration login.

For PDS delivery, the migration installer creates the
`event_pds_settlement_global` capability group. Under migration authority, grant
membership to the account named by `DATABASE_GLOBAL_USERNAME`, with inheritance
enabled and role switching disabled. Do not grant it to the Runtime account.
Settlement freezes the persisted claim tenant before its transaction; private
rows remain tenant-constrained, and the shared record and all affected discovery
epochs commit or roll back together.

The migration-installed materialization and settlement groups inherit the fixed
functions of `event_discovery_disclosure_runtime`; that name does not mean the
ordinary Runtime account should receive it. Its owner, migrator and execution
groups must remain non-login, non-elevated and free of unexpected parent roles
or unrelated object ownership. Installation rejects an unsafe existing setup
with PostgreSQL `42501` instead of silently altering those roles. Correct the
database authority deliberately under the selected migration account before
retrying; do not add owner/migrator membership to Global or Runtime.

PDS queue admission also requires `event_pds_admission_global` membership for
the configured Global login.

Actor and exact-DID moderation additionally require
`event_atproto_moderation_global` membership for that login, with inheritance
enabled and role switching disabled. Never grant its owner/migrator groups or
private table access to Global or Runtime. Suspension and compensation share one
transaction; planning reads session metadata, while credentials remain confined
to the tenant-bound delivery transport.

For PDS admission, use
the configured Global login, with inheritance enabled and role switching
disabled. Do not grant its owner/migrator groups or queue-table read access to
Global or Runtime. This capability atomically claims at most 100 eligible
messages, including expired processing leases, and returns identifiers only.
Message content and protected OAuth sessions are loaded through Runtime
credentials bound to each claimed tenant before delivery. Settlement continues
to use the separate capability described above.

For Listmonk IntegrationSync processing, grant
`event_integration_sync_discovery_runtime` to the configured Global login under
migration authority, with inheritance enabled and role switching disabled.
Do not grant its owner/migrator groups or add queue-table read privileges to
Global. This capability discovers only tenant/outbox IDs in batches of at most
1,000. The worker then loads and processes each row through Runtime credentials
bound to that tenant; ambiguous provider outcomes remain parked for recovery
rather than automatically resent.

For SMTP email processing, also grant `event_email_admission_global` to the
Global login with inheritance enabled and role switching disabled. Its owner
and migrator groups must remain unavailable to Runtime and Global. Admission
and stale recovery use this execute-only capability; message content is loaded
separately with Runtime credentials bound to the owning tenant. Global/per-tenant
concurrency caps and reminder backlog controls remain enforced together.
Cancellation before sending can be recovered, while an uncertain SMTP outcome
remains Unknown for reconciliation rather than automatic resend.

ATProto ingestion and recovery-policy discovery require no additional credential
pair or governance executor-role grant. The Migrator installs the fixed column
permissions for dedicated, non-secret federation settings on the configured
Runtime and Global logins. Global can read the complete tenant override audience;
ordinary tenant writes remain tenant-constrained. Only Global can write instance
settings. Private generic tenant settings are not exposed. The API still applies
instance locks, deployment mode and backfill precedence.

Do not restore or grant `event_atproto_governance_runtime` or its owner/migrator
roles. The replacement rejects leftover governance roles, the former routine or
its policy; it does not remove them automatically. Roles are PostgreSQL
cluster-wide, so a fresh database on an old cluster may still be rejected. Use an
explicitly disposable development environment or arrange a reviewed transition
for retained data. See [the development transition](backup-restore-upgrade.md#dedicated-federation-settings-development-transition).

Pre-tenant API-key and scanner authentication use the configured Global login;
no additional credential pair is required. Run the application Migrator to install
the fixed column permissions through `event_api_key_authentication_global` and
`event_scanner_authentication_global`, with inheritance enabled and role switching
disabled. The API-key profile reads only its mapped lookup/metadata fields,
including the one-way secret hash, and changes only last-use time and IP. The
scanner profile reads its mapped lookup fields, including digest/version, and
cannot write. Runtime receives no authentication-profile permissions; there is
no table-wide grant, raw-key/HMAC-key read, credential management, check-in
mutation or identity-write authority from these profiles.

Ordinary tenant-key management uses a separate fixed Runtime storage permission
set, not either authentication role. Runtime can read key metadata but cannot
read credential hashes or last-use IP addresses; it can insert new keys and
digest-only issuance receipts and update the declared policy/status fields.
Its supporting lookup access is limited to credit-period/owner-type IDs and
status ID/usability, with status-ID writes for the existing transaction fence;
lookup descriptions remain unreadable. These global lookups receive no tenant
policy, and unexpected columns or grants make readiness fail without repair.
Runtime can also read tenant IDs for parent-existence checks, including inactive
tenant IDs; it cannot read tenant descriptions or private user data through this
permission. This does not broaden the tenant-key or receipt policies.
Restrictive tenant policies keep this storage access within the selected nonnull
tenant and hide platform keys. The API retains issuance authorization, secret
disclosure and replay rules. A stolen Runtime database credential can choose a
tenant session and forge a new key or change its permitted policy/status without
those API checks; its narrow status-ID write also permits key tampering subject
to database constraints, but cannot change usability or descriptions. Protect
and rotate Runtime as sensitive application write
authority; fixed storage permissions do not independently authorize a human.
Credential theft also permits enumeration of tenant IDs, not tenant descriptions
or private user data, through the fixed parent-key read.
The Migrator rejects partial installations and unexpected grants or policies
instead of silently repairing them.

Tenant scanner management uses the same fixed Runtime storage installation,
without inheriting the Global authentication role. Runtime can read and insert
the 16 mapped scanner fields, including lookup digest/version, and update only
revocation, concurrency and the existing row-fence key. It cannot update an
issued scanner's digest or identity. Issuance also reads fixed event identity/deletion metadata and participation
mode, not private event content or event write permissions. Those permissions
support the API's own issuance checks rather than a database business routine.
The API separately resolves the authorized user's personal Actor for issuance
and revocation records; platform User and Actor identifiers are not interchangeable.
Restrictive tenant policies hide foreign
rows even when application query filters are bypassed; the raw scanner token and
HMAC key are not database read permissions. Instance-key management/report
credentials remain unable to read or write scanners. Protect Runtime accordingly:
credential theft allows direct scanner enumeration, creation or revocation in
its selected tenant sessions without the API's human authorization.

Runtime also has fixed admission-target reads and inserts, with updates limited
to operational status and its concurrency marker. The C# application coordinates
target creation, scanner issuance, check-in and stopping; waiting operations begin
their transaction only after the previous coordinated workflow finishes. These
permissions do not provide private event content or target-key updates. Direct
database edits bypass that coordination, so administrative tools must not make
concurrent target changes outside the application workflow. SQLite deployment
continues to require a single application instance for process-local coordination.
An acknowledged commit followed by cleanup failure is reported without replaying
the operation; an uncertain commit must be reconciled rather than blindly retried.

The API performs exact key matching, preserves persisted credential precedence
and rejects ambiguous scanner matches. Usage changes are transactional and
conditional on the update interval. A failed commit acknowledgement or cleanup
is reported as an error, not silently retried into a misleading unchanged result;
do not assume that such an error proves rollback. All trusted services sharing
Global can directly read the profile's fixed lookup fields across tenants.
Protect that credential as sensitive application authority and rotate or revoke
it on suspected compromise; the API's exact-lookup rules do not constrain someone
who has stolen the database credential.

Do not restore the former authentication owner/migrator roles or lookup/usage
routines. Installation rejects those retained artifacts and changed permissions
instead of removing or repairing them. Use an explicitly disposable fresh
development cluster or arrange a reviewed transition for retained data; do not
delete retained data just to pass startup.

Membership discovery continues to use `event_user_membership_global` on the
configured Global login, with inheritance enabled and role switching disabled.
Keep its owner/migrator groups and direct private-table access away from both
application logins. It supplies one user's active tenant memberships, not
credential management, check-in mutation or identity-write authority.

External provider-account binding lookup uses EF with the existing Global
credential. The Migrator installs SELECT only on the binding's ID, user ID,
provider ID/key/display name and creation/update audit columns. No Runtime
read, write permission, table-wide SELECT or grant option is added. Every service
sharing the Global credential can read these columns; the application restricts
each lookup to the exact provider and account. Keep that credential restricted
to trusted backend services. Grant these column reads directly to Global, not
through another role. Admission rejects binding privileges on any other role in
Global's transitive membership graph, including ADMIN-only memberships that
could delegate access later.

Localization's configured-status check uses the existing Runtime or Global
account; no additional credential is required. Matching migration tooling grants
SELECT only on four secret-binding metadata fields: setting key, qualifier,
scope type and scope ID. The API selects the instance or current tenant in C#
and returns only whether the localization binding is configured. It does not
read back a secret, expose a provider reference or grant binding writes.

These metadata rows are not protected by PostgreSQL row-level security. Someone
who steals either database credential can enumerate those four fields across
scopes, even though normal API calls apply tenant selection. Private binding
fields and externally stored secret values are not included in this grant.
Keep both accounts confined to trusted backend processes; do not add broader
table permissions or restore the retired localization-status routine.

If startup rejects retained localization routines or roles, use a reviewed
development database transition; installation does not remove them automatically.
Do not delete retained data merely to pass readiness.

The external provider-account binding cutover requires a fresh isolated PostgreSQL instance/cluster
without the obsolete `event_external_login_authentication_owner`,
`event_external_login_authentication_migrator` and
`event_external_login_authentication_global` roles. Provision the database and
declared accounts there. Creating only a new database in the old cluster is
insufficient because roles are cluster-wide. If the old external-login routine
or any of those roles remains, startup migration fails with
`external_login_requires_fresh_development_database`. The application does not
delete data or clean up old database objects. Preserve any data you need before
explicitly provisioning a replacement;
do not grant the removed execution role or broaden Runtime permissions to bypass
the failure. No new environment variables or secret authority are required.

Reference-data cache loading also uses ordinary EF reads, with the existing
Runtime credential. The schema installer grants read-only columns for seventeen
declared reference types, such as event formats, languages and role definitions.
These permissions do not expose role assignments, permissions or user-private
data. No write, table-wide read or grant-delegation permission is added. All
services sharing Runtime share the reference-column reads; tenant-specific event
types remain subject to tenant isolation and are excluded from the tenantless cache.

The old `global_lookup_catalogue` function and `event_global_lookup_owner`,
`event_global_lookup_migrator`, `event_global_lookup_runtime` roles are no longer
used. Their presence causes `global_lookup_requires_fresh_development_database`.
Use a fresh isolated development PostgreSQL instance/cluster without those roles;
creating another database in the same cluster does not remove them. Do not add
the removed execution roles or broaden permissions to bypass admission. There is
no automatic object cleanup or data deletion, and no new environment variable.
Preserve needed development data before deliberately provisioning a replacement.

Cache initialization now fails visibly if any reference read fails. A failed or
cancelled refresh retains the previous complete cache and reports the failure;
it does not replace good data with an empty or partial cache. Correct the reported
database or permission problem before retrying rather than granting broad access.

Administrative scope discovery additionally requires
`event_admin_authority_global` on the Global login, with inheritance enabled and
role switching disabled. Do not grant its owner/migrator groups or direct access
to private role-grant/membership tables. This capability returns only one user's
eligible administrative scope IDs and platform-administrator status, across
tenants; it does not grant mutation authority or expose member PII.

Backends performing verified ATProto actor consolidation additionally require
`event_actor_consolidation_runtime` on their selected transaction-owning login,
with inheritance enabled and role switching disabled. It moves references for
an exact authorized source/target actor pair across tenants without granting
direct private-table access. Keep its owner/migrator groups separate. This
operation remains in the onboarding transaction so source retirement cannot
commit while foreign-tenant references are left behind.

Managed tenant provisioning additionally requires `event_managed_domain_global`
on Global for exact reserved-host discovery across tenants. Notification fanout
requires `event_fanout_admission_global` on Global for bounded claim admission
and aggregate health. Grant both with inheritance enabled and role switching
disabled, never their owner/migrator groups or direct private-table access.
Fanout recipient/cursor work remains on independently acquired exact Runtime
tenant leases; cancellation leaves fenced claims for ordinary expiry recovery.

Web Push dispatch requires `event_webpush_admission_global` on Global for
owner discovery, claim/recovery and aggregate health; discovery/recovery
batches have a maximum of 1000. Subscription secrets, preferences and
settlement remain on fresh exact Runtime tenant leases. Queue readiness
requires `event_queue_drain_health_global` for its fixed 13-count snapshot.
Grant these execution groups with inheritance enabled and role switching
disabled, never their owner/migrator groups or private-table grants.

Incoming webhook message/effect drains additionally require
`event_incoming_webhook_global` on Global for bounded claims and exact renewal.
It exposes identities and fences, not message payloads or effect envelopes.
Grant it with inheritance enabled and role switching disabled; execution and
settlement stay on fresh exact Runtime tenant scopes.

Instance operations report synchronization counts additionally require
`event_report_operations_global` on Global. It exposes scalar status/cutoff
counts, not report content, and excludes soft-deleted parent reports. Use
inheritance-enabled, role-switch-disabled execution membership without
owner/migrator or private-table privileges.

Email operational health uses the existing email admission execution group;
its snapshot contains counts, oldest-due time, a bounded 1–100 tenant backlog
sample and processor flags, never recipient content. The tenant-directory and
email-admission capability installers reject contaminated managed roles with
`42501`: login/elevated attributes, unexpected parent membership, unrelated
object ownership, or effective table/column/sequence access on their execution
groups. This includes grants in unrelated non-system schemas. Provision these
groups for function execution only; remove unintended authority through your
approved database administration process before retrying installation.
Resource audit retention uses migrator-installed, fixed Global read permission
on audit tenant IDs, not an execution-group membership or stored discovery
routine. Audit contents remain private; deletion uses exact Runtime tenant
scopes. Email retention similarly reads only seven queue metadata columns through
Global; message contents, recipients and redaction remain tenant-bound. Run the
application migrator to install these profiles; do not substitute table-wide
grants or manually recreate the retired business routines. Local webhook delivery requires
`event_local_webhook_admission_global` on Global for discovery, seven-key
governance, claims and recovery; payload/credential access and settlement stay
tenant-bound. Use inheritance-enabled, role-switch-disabled memberships without
owner/migrator groups or direct private-table grants.

Registration finalization drains require `event_registration_finalization_global`
on Global for bounded effect claiming only. Financial/order work and settlement
use fresh exact Runtime tenant scopes; the claim capability never exposes
payment or fulfillment graphs. Grant inheritance-enabled, role-switch-disabled
execution membership only to Global, not Runtime or owner/migrator groups.

Payment reconciliation requires `event_payment_reconciliation_global` on
Global for claims and aggregate health only. Attempt hydration and settlement
use fresh exact Runtime scopes; captured identity and lease fences guard
economic writes and Unknown recovery remains atomic. Use inheritance-enabled,
role-switch-disabled membership without private-table or owner/migrator grants.

The storage reconciliation custody upgrade requires
`event_storage_reconciliation_global` and `event_local_storage_quarantine_global`
on the selected Global login only. These execute fixed metadata-quarantine and
durable local-orphan operations; they do not grant ordinary storage-table writes.
Do not add these groups to Runtime, reuse the accounting role, or grant their
owner/migrator groups to application logins. Use inheritance-enabled,
role-switch-disabled memberships. The security programme's generated migrations
and final authority checks remain upgrade prerequisites, not completed receipts.
Keep original local bindings and private quarantine destinations available for
recovery; see [Storage](../integrations-and-ai/storage.md#reconciliation-quarantine-recovery).

> [!NOTE]
> **We recommend:** Use PostgreSQL for multi-container production deployments. For lightweight, single-server setups with zero external infrastructure, use `DATABASE_PROVIDER=Sqlite` with the Standalone Docker image.

Subject erasure requires `event_subject_erasure_global` on Global only, with
inheritance enabled and role switching disabled. Keep private-table and
owner/migrator grants absent. Provider work and local disposal settle together;
an application failure rolls them back without removing committed retained
authority.

Instance storage reporting and repair require `event_instance_storage_admin_global`
on Global only, with inheritance enabled and role switching disabled. Repair is
all-or-nothing across tenants and providers, including newly created counters.
The capability cannot modify custody objects, sessions or tombstones.

Location privacy review health requires `event_location_review_global` on the
Global login, with inheritance enabled and role switching disabled. This
execute-only capability reports the total active review queue across tenants;
it exposes no location or tenant details. Do not grant location-table access
or owner/migrator membership to Global, or grant this capability to Runtime.

Location governance setting changes require `event_location_governance_runtime`
on Runtime and `event_location_governance_global` on Global, with inheritance
enabled and role switching disabled. Runtime can operate only on its leased
tenant; instance-wide corrections use Global. These capabilities expose policy
facts, not addresses, and mark affected associations for privacy review without
changing their selected disclosure fields. Setting, correction, audit and queued
effect commit together or roll back together. Do not grant either group private
table privileges or owner/migrator membership.

The email-policy groups additionally allow bounded, fenced tenant revision
reconciliation in the setting transaction. Pause state and SMTP token buckets
are not changed by that reconciliation.

Instance setting mutations now acquire the selected Global pool before their
write dependencies and transaction are created. Provision the Global login
with the required instance-setting object privileges alongside the listed
purpose-function memberships; these are not private tenant-table grants.
Explicit target-tenant administration instead uses a fresh Runtime lease for
the authorized target. Request authorization still runs before either mutation.
The selected target is isolated to that operation; it does not change the
administrator's current request tenant or another concurrent operation.

Bootstrap post-commit effect recovery requires `event_manifest_replay_runtime`
on Runtime and `event_manifest_replay_global` on Global, with inheritance
enabled and role switching disabled. The groups expose only the bounded
safe tenant-result reader for one successful bootstrap operation. They do not
grant configuration-table writes or general cross-tenant reads; keep table,
owner and migrator privileges absent from these execution groups.

Instance import snapshot reads require `event_configuration_import_snapshot_global`
on the selected Global login, with inheritance enabled and role switching
disabled. Do not grant this group to Runtime. It exposes only manifest-catalog
settings, stored branding, and active paid-policy reads for the selected target;
it grants no private-table access or import writes. Keep owner and migrator
memberships absent. Import snapshots use their dedicated owner connection so
fidelity checks can read changes within the same transaction before commit.

The same Global import login also needs `event_configuration_import_state_global`
for fixed instance session/protected-artifact operations and
`event_configuration_import_legal_source_global` for current editable legal
source. Coordinated publication-policy settings require
`event_configuration_import_publication_global`; importing legal drafts or
revisions additionally requires `event_configuration_import_legal_mutation_global`.
Ordinary catalog scalar settings require `event_configuration_import_scalar_global`;
this group cannot change specialized policy, resource, privacy, or operator settings.
Atomic receipt and deferred-effect creation requires
`event_configuration_import_receipt_global`; it does not authorize arbitrary
outbox processing or receipt edits.
Paid-policy document imports require `event_configuration_import_paid_policy_global`
on the same selected Global login. This capability validates every active tenant
policy, including tenants absent from the import, and writes policy revisions
atomically with the import. It preserves prior versions rather than replacing
history. Do not grant the Runtime ceiling group to the Global import login.
Tenant display-name imports additionally require
`event_configuration_import_tenant_identity_global`. This capability keeps name
updates and discovery-cache invalidation in the same atomic import, so readers
cannot retain a previously disclosed tenant identity after the update commits.
Tenant branding document imports require `event_configuration_import_branding_global`.
This capability creates or updates only catalog branding, retains existing
creation identity/audit, and rejects a changed document instead of overwriting
it. Give the selected Global login inherited execution membership with role
switching disabled; do not grant document-table privileges.
Resource-setting imports, including Portable manifests containing catalog
defaults, require `event_configuration_import_resource_global` on the selected
Global login. This capability changes resource values only, retains system
locks and ceiling checks, and cannot remove settings or change locks. Enroll it
with inheritance enabled and role switching disabled, without table privileges.
Ordinary tenant-setting imports require `event_configuration_import_tenant_setting_global`.
It handles only the ordinary manifest catalog, respects instance and tenant
locks, and preserves creation audit when updating an override. Enroll the same
Global login with inheritance enabled and role switching disabled; coordinated
and resource settings keep their separate capabilities.
Instance upload, cancellation, receipt/history and forward-rollback requests
use the same selected Global authority. Instance direct-transfer promotion
also uses that authority for staging; Runtime requires no instance staging-table
write grant for these operations. A rollback creates a fresh import session from the retained
snapshot. Cancelling a session atomically removes its protected upload bytes and
keeps the cancelled session evidence; it cannot remove an active session's bytes
or a retained rollback snapshot. A rollback uses that retained
snapshot rather than reversing committed database history. Fresh rollback
sessions and their protected artifacts are staged atomically through fixed
operations; do not grant the Global login direct session-table write access.
Send the complete returned access token unchanged for preview, cancellation,
and apply; apply also supplies the
returned source operation ID, which must match the token's operation prefix.
These operations cannot publish legal documents. Use inherited memberships
with role switching disabled; grant none of these groups to Runtime and add
no private-table privileges. Legal source includes
unpublished drafts but does not transfer publication or acceptance history.
These groups do not authorize the remaining configuration-section writes.

Visitor-policy changes at instance scope require `event_visitor_policy_global`
on the selected Global login, with inheritance enabled and role switching
disabled. This grants only execution of fixed visitor validation functions;
do not grant it to Runtime or grant private-table/owner/migrator access.
Validation and setting writes share the same transaction, preserving live
tenant participation checks and uncommitted instance locks.

SMTP policy reconciliation needs `event_email_policy_runtime` on Runtime and
`event_email_policy_global` on Global, with inheritance enabled and role
switching disabled. These grant only the fixed email-policy override reader,
not general cross-tenant access. Reads and revisions share the setting
mutation's transaction; keep private-table and owner/migrator grants absent.

Paid-policy ceiling validation requires `event_paid_policy_ceiling_runtime`
on the policy-mutation Runtime login, with inheritance enabled and role
switching disabled. This permits only bounded active-policy validation reads
within the current transaction. Missing or changed policy authority blocks
the mutation; no policy-table writes or owner/migrator grants are added.

---

## 3. Authentication Providers and Local Identity

Select exactly one primary authentication provider. Local Identity is the
runtime default when no provider is explicitly selected. AT Protocol can be
independent while Local Identity or Keycloak is primary, or it can be the sole
passwordless authority.

| Variable | Status | Default | Description |
|---|---|---|---|
| `AUTHENTICATION_PROVIDER` | **Baseline** | `local` | Primary provider: `local`, `keycloak`, or `atproto`. |
| `IDENTITYCORRELATION__TRUSTEDISSUERS__0` (and subsequent numeric indices) | Advanced | Empty allowlist | Exact OIDC issuers permitted to correlate verified email with an eligible existing account. Separate from login admission; process restart required. |
| `AUTHENTICATION_LOCAL_JWT_KEY` | **Baseline (Secret)** | None | Base64-encoded Local JWT signing key of at least 256 bits; required when Local Identity is primary. |
| `AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD` | Advanced | `5` | Consecutive failed Local Identity attempts before lockout. |
| `AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES` | Advanced | `15` | Local Identity lockout duration. |
| `ATPROTO_LOGIN_ENABLED` | Baseline | `false` | Enables AT Protocol login. It must be `true` when `AUTHENTICATION_PROVIDER=atproto`. |
| `IDENTITY_DATABASE_TOPOLOGY` | **Baseline** | `colocated` | Local credential storage: `colocated` with the application database or `external`. |
| `IDENTITY_DATABASE_PROVIDER` | External topology | None | External credential provider: PostgreSQL, SQLite, SQL Server, MariaDB, or MySQL. |
| `IDENTITY_DATABASE_HOST` | External topology | None | External credential database host. |
| `IDENTITY_DATABASE_PORT` | External topology | Provider default | External credential database port. |
| `IDENTITY_DATABASE_NAME` | External topology | None | External credential database name or persisted SQLite path. |
| `IDENTITY_DATABASE_SCHEMA` | External topology | Provider default | Provider namespace/schema for Local Identity objects. |
| `IDENTITY_DATABASE_TLS_MODE` | External topology | Provider default | TLS verification mode: `Prefer`, `Required`, or `Disabled`. |
| `IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE` | External topology | `false` | Set `true` only in local development to trust self-signed TLS certificates. |
| `IDENTITY_DATABASE_RUNTIME_USERNAME` | External topology | None | Least-privilege runtime credential username. |
| `IDENTITY_DATABASE_RUNTIME_PASSWORD` | External topology (Secret) | None | Least-privilege runtime credential password. |
| `IDENTITY_DATABASE_MIGRATOR_USERNAME` | External topology | None | Schema-owner/migrator credential username. |
| `IDENTITY_DATABASE_MIGRATOR_PASSWORD` | External topology (Secret) | None | Schema-owner/migrator credential password. |
| `IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME` | External topology | None | Dedicated C# Local Identity lifecycle writer login; no Runtime/Migrator fallback. |
| `IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD` | External topology (Secret) | None | Dedicated lifecycle writer password. |
| `IDENTITY_DATABASE_SERVER_VERSION` | MariaDB/MySQL | Provider default | Explicit server version for provider composition. |

External Identity binding requires the discrete endpoint and selected purpose's
credentials. A shared `IDENTITY_DATABASE_CONNECTION_STRING` is not supported;
unsupported purposes cannot borrow Migrator credentials. An explicitly empty
structured credential masks its environment alias and fails validation.

Local token issuance and validation use the same deployment signing key; the
identity writer does not need access to private database secret bindings. Supply
Base64 key material of at least 256 bits through the selected secret authority.
The structured `Authentication:Local:JwtKey` declaration takes precedence over
`AUTHENTICATION_LOCAL_JWT_KEY`; explicitly blank structured input refuses signing
instead of falling back to the alias. Change the deployment authority and restart
the API to replace this key; changing a database secret binding does not rotate it.

### Keycloak resource and client context

These nonsecret keys use the standard .NET double-underscore configuration
mapping, directly on the API or Standalone process:

| Variable | Status | Default | Description |
|---|---|---|---|
| `KEYCLOAK__AUDIENCE` | Advanced | `islamu-event-api` | One intended API resource audience. Replaces the default, rather than adding another audience. |
| `KEYCLOAK__PERMITTEDCLIENTS__0` (and subsequent numeric indices) | Advanced | No client restriction | Optional exact, case-sensitive `azp` allowlist, independent of resource audience. Include every intended BFF/service client when enabled. |

Use one nonblank client ID per numeric index; duplicate or blank entries
prevent authentication registration. An enabled list rejects missing `azp`;
an absent/empty list permits it but still rejects a malformed present value.
The `aud` resource claim is always required. Neither the BFF client ID nor
an allowed `azp` substitutes for the configured resource audience.
Configure Keycloak's access-only audience mapper accordingly. The old
`Keycloak:ValidAudiences` and `Authentication:ValidAudiences` arrays no longer
control admission.

Restart the consuming process after changing these settings. Docker Compose
explicitly passes through index zero when set; further indices require a service
override. Unused indices must be absent, not empty. These process-owned values do not originate from tenant settings,
provider-client discovery or runtime client-secret selection. See
[Authentication](../security-and-identity/authentication.md#keycloak-api-audience-and-client-admission)
for the supported access/ID token purpose contract.

### Exact issuer trust for account matching

Set one nonempty issuer per numeric index, for example
`IDENTITYCORRELATION__TRUSTEDISSUERS__0=https://identity.example.org/realms/community`.
Supply these variables to the API or Standalone process. With Docker Compose,
explicitly forward the selected indexed variables in a service override; adding
a key to Compose's interpolation `.env` alone does not inject it into a container.
The equivalent application configuration is `IdentityCorrelation:TrustedIssuers`.
Omit all members to disable automatic email matching; do not use an empty member
as an empty-list marker.

Only deployment operators own this allowlist. Tenant administrators cannot widen
it. Values must be exact issuer authorities, with no wildcard, user information,
query or fragment. Host/scheme case, default ports and trailing slashes normalize
like provider account keys; realm/path case remains significant. Duplicate
normalized authorities and malformed entries stop startup. Restart the process
after changing trust. Existing exact account bindings and permitted safe signup
do not depend on inclusion, and the allowlist does not change the provider's
`email_verified` assertion.

> [!WARNING]
> **Do not blindly mirror `IDENTITY_DATABASE_TOPOLOGY` and `ERASURE_DATABASE_TOPOLOGY`.**
> Although both settings configure database topologies, they serve fundamentally different architectural purposes with opposing default recommendations:
> - **`IDENTITY_DATABASE_TOPOLOGY` (Recommended: `colocated`):** In standard single-instance setups, keep this `colocated` so user credentials share the primary application database with zero extra operational overhead. Only switch to `external` if your organization requires a central, shared identity database across multiple distinct applications or SaaS solutions (such as sharing one user database across all ISLAMU solutions).
> - **`ERASURE_DATABASE_TOPOLOGY` (Recommended: `EmbeddedSqlite`):** The privacy erasure authority must remain *outside* the primary application database to ensure GDPR anti-resurrection guarantees survive primary database backup restoration. See [Section 7: Privacy Erasure Authority](#7-privacy-erasure-authority-gdpr--anti-resurrection).

Keycloak variables are required only when `AUTHENTICATION_PROVIDER=keycloak`:

The supported primary/AT Protocol combinations are `local/false`,
`local/true`, `keycloak/false`, `keycloak/true`, and `atproto/true`.
`atproto/false` is rejected, and Google SSO is disabled while AT Protocol is
primary. See [Authentication Providers](authentication-providers.md) for
onboarding and switching behavior.

| Variable | Status | Default | Description |
|---|---|---|---|
| `KEYCLOAK_ENDPOINT` | **Baseline** | `http://localhost:8080` | Public or reverse-proxied base URL of Keycloak (e.g., `https://auth.example.org`). |
| `KEYCLOAK_REALM` | **Baseline** | `islamu` | Keycloak realm name. |
| `KEYCLOAK_BLAZOR_CLIENT_ID` | **Baseline** | `event-blazor` | OIDC Confidential Client ID configured for the Blazor BFF. |
| `KEYCLOAK_BLAZOR_CLIENT_SECRET` | **Baseline (Secret)** | None | 32-byte hex client secret generated for the BFF client. |
| `KEYCLOAK_DB_DATABASE` | **Baseline** | `keycloak` | Database name used by the Keycloak database container. |
| `KEYCLOAK_DB_USERNAME` | **Baseline** | `keycloak` | Database username for Keycloak. |
| `KEYCLOAK_DB_PASSWORD` | **Baseline (Secret)** | None | Password for Keycloak database user. |
| `KEYCLOAK_ADMIN` | **Baseline** | `admin` | Initial Keycloak administrative user. |
| `KEYCLOAK_ADMIN_PASSWORD` | **Baseline (Secret)** | None | Password for initial Keycloak administrative user. |
| `KEYCLOAK_REQUIRE_HTTPS_METADATA` | Advanced | `true` | Enforce HTTPS metadata validation for OIDC endpoints. Set `false` only in local dev without TLS. |

---

## 4. Secret Authority Management

| Variable | Status | Default | Description |
|---|---|---|---|
| `SECRET_PROVIDER` | **Baseline** | `Environment` | Provider authority: `Environment` (direct `.env` injection), `Infisical`, or `UserSecrets` (dev only). |
| `INFISICAL_URL` | Advanced | `https://app.infisical.com` | Infisical server URL if using Infisical. |
| `INFISICAL_PROJECT_ID` | Advanced | None | Target Infisical project UUID. |
| `INFISICAL_CLIENT_ID` | Advanced | None | Direct-process Universal Auth identity; never a Compose/Aspire shared identity. |
| `INFISICAL_CLIENT_SECRET` | Advanced (Secret) | None | Direct-process credential for that consumer only. |
| `INFISICAL_ENV` | Advanced | `prod` | Infisical environment slug (`prod`, `staging`, `dev`). |

> [!NOTE]
> **We recommend:** Stick with `SECRET_PROVIDER=Environment` for straightforward self-hosting. Use `Infisical` only if you manage organizational secrets centrally across multiple servers.

---

## 5. Storage Providers (Local & Cloud S3)

| Variable | Status | Default | Description |
|---|---|---|---|
| `LOCAL_STORAGE_ROOT_PATH` | **Baseline** | `/app/storage-data/local` | Filesystem directory used when the application storage setting selects local storage. |
| `STORAGE_S3_ENDPOINT` | Advanced | None | S3 API endpoint (e.g., `https://s3.amazonaws.com` or MinIO URL). |
| `STORAGE_S3_BUCKET_NAME` | Advanced | None | Dedicated bucket name for platform uploads. |
| `STORAGE_S3_ACCESS_KEY_ID` | Advanced (Secret) | None | S3 Access Key ID. |
| `STORAGE_S3_SECRET_ACCESS_KEY` | Advanced (Secret) | None | S3 Secret Access Key. |
| `STORAGE_S3_REGION` | Advanced | `us-east-1` | S3 Region identifier. |
| `STORAGE_S3_FORCE_PATH_STYLE` | Advanced | `true` | Set `true` for MinIO / self-hosted S3; `false` for AWS S3. |

> [!NOTE]
> **We recommend:** Use `local` storage with a mounted volume for single-node deployments. Use `s3` with Cloudflare R2 or MinIO for multi-replica or high-traffic event media hosting.

Storage-provider selection is an application governance setting, not a
`STORAGE_PROVIDER` environment variable.

---

## 6. Email (SMTP & Outbox)

Outbound email defaults off. Set the governance setting `email.delivery_enabled=true`
only when you intend to send mail; supplying SMTP environment variables does not enable
delivery. This is an application setting, not an environment-variable alias. A disabled
transport keeps its configuration and does not connect to SMTP.

Instance SMTP is shared only while instance delivery is enabled. If tenant SMTP
delegation is unlocked, a tenant may enable its own host and sender with tenant-scoped
credentials even when instance delivery is off. Tenant hosts never receive instance
credentials. Both credentials may be omitted for a deliberately unauthenticated relay.
Missing or invalid configuration is reported separately from intentional disablement.

| Variable | Status | Default | Description |
|---|---|---|---|
| `MAIL_SMTP_HOST` | Optional | None | Deployment SMTP hostname; Development seed initializes `email.smtp_host` if absent. |
| `MAIL_SMTP_PORT` | Optional | None | Deployment SMTP port; Development seed initializes `email.smtp_port` if absent. |
| `MAIL_SMTP_FROM_ADDRESS` | Optional | None | Deployment sender; Development seed initializes `email.from_address` if absent. |
| `MAIL_SMTP_FROM_NAME` | Optional | None | Deployment sender label; Development seed initializes `email.from_name` if absent. |
| `MAIL_SMTP_USERNAME` | Optional secret | None | Instance SMTP authentication username in the selected authority. |
| `MAIL_SMTP_PASSWORD` | Optional secret | None | Instance SMTP authentication password in the selected authority. |
| `MAIL_SMTP_ENCRYPTION` | Optional | `StartTls` | Deployment SMTP connection security; supported modes are `None`, `StartTls`, `SslOnConnect`, and `Auto`. |

Set connection security through `email.smtp_security` (`StartTls` by default;
`None`, `SslOnConnect`, and `Auto` are also supported). Keycloak and other account
providers retain their own verification and recovery delivery configuration.

---

## 7. Privacy Erasure Authority (GDPR & Anti-Resurrection)

| Variable | Status | Default | Description |
|---|---|---|---|
| `ERASURE_DATABASE_TOPOLOGY` | **Baseline** | `EmbeddedSqlite` | Storage topology: `EmbeddedSqlite` (dedicated local file), `CoLocated`, or `ExternalDatabase`. |
| `ERASURE_EMBEDDED_PATH` | **Baseline** | `/app/data/privacy_erasure_authority.db` | File path when `ERASURE_DATABASE_TOPOLOGY=EmbeddedSqlite`. |
| `PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID` | **Required** | None | Nonsecret persistent key ID (1-64 ASCII letters, digits, `_` or `-`); identical on every replica. |
| `PRIVACY_ERASURE_IDENTITY_FENCE_KEY` | **Required secret** | None | Base64-encoded 32 random bytes from the selected secret authority; Infisical path `/privacy`. Retain with all supported authority backups; no live rotation or fallback. |
| `ERASURE_WRITER_REPLICA_COUNT` | Advanced | `1` | Maximum write concurrency for the embedded authority database. |
| `ERASURE_BUSY_TIMEOUT_SECONDS` | Advanced | `30` | SQLite busy timeout before serializable retry. |
| `ERASURE_DATABASE_HOST` | Advanced | None | Hostname if using `ExternalDatabase` topology. |
| `ERASURE_DATABASE_PORT` | Advanced | `5432` | Port for external erasure authority database. |
| `ERASURE_DATABASE_NAME` | Advanced | None | Database name for external erasure authority. |
| `ERASURE_DATABASE_RUNTIME_USERNAME` | Advanced | None | Least-privilege runtime user for external erasure DB. |
| `ERASURE_DATABASE_RUNTIME_PASSWORD` | Advanced (Secret) | None | Password for runtime user on external erasure DB. |
| `ERASURE_DATABASE_MIGRATOR_USERNAME` | Advanced | None | Schema-owner user for external erasure DB; must differ from the runtime user. |
| `ERASURE_DATABASE_MIGRATOR_PASSWORD` | Advanced (Secret) | None | Password for migrator user on external erasure DB. |
| `ERASURE_DATABASE_TLS_MODE` | Advanced | `Prefer` | TLS mode for external erasure DB: `Prefer`, `Required`, or `Disabled`. |
| `ERASURE_DATABASE_TRUST_SERVER_CERTIFICATE` | Advanced | `false` | Set `true` only in local development to trust self-signed TLS certificates. |

The same `ERASURE_DATABASE_*` names are used inside the Infisical `/database/erasure` folder, so a flat `.env` and an Infisical project never disagree on the key name. See [Infisical Setup](infisical.md#databaseerasure).

> [!WARNING]
> **Why `EmbeddedSqlite` is recommended (and distinct from Identity topology):**
> Do not blindly set `ERASURE_DATABASE_TOPOLOGY` to match `IDENTITY_DATABASE_TOPOLOGY`. They solve completely different architectural problems:
> - **Erasure Authority (`EmbeddedSqlite` recommended):** The erasure ledger is lightweight and intentionally decoupled from the primary application database. If your primary database ever needs to be restored from an earlier backup, an independent embedded or external erasure authority retains the history of user deletion requests that occurred *after* that backup, immediately re-enforcing those deletions and preventing illegal GDPR data resurrection.
> - **Identity Database (`colocated` recommended):** By contrast, `IDENTITY_DATABASE_TOPOLOGY` defaults to `colocated` within the primary database for standard deployments, and is only separated into an `external` database when an enterprise or SaaS operator needs a single, unified credential database shared across multiple distinct applications (such as across all ISLAMU products).

---

## 8. First-Run Setup & Administrator Bootstrap

| Variable | Status | Default | Description |
|---|---|---|---|
| `SETUP_SECRET` | **Baseline** | None | Pre-shared secret to unlock `/setup`. If left blank, generated automatically in volume. |
| `INSTANCE_BOOTSTRAP_MODE` | **Baseline** | `Interactive` | Mode: `Interactive` (web wizard at `/setup`) or `ConfiguredAdministrator` (headless). |
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | Advanced | None | Required if headless: `local`, `keycloak` or `atproto`. |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | Advanced | None | Exact provider subject: a lowercase, hyphenated UUIDv7 also used as the Local username, Keycloak `sub`, or ATProto DID. |
| `INSTANCE_BOOTSTRAP_BINDING_GENERATION` | Advanced | None | Required if headless: positive integer generation counter. |
| `INSTANCE_BOOTSTRAP_ADMIN_EMAIL` | Advanced | None | Optional administrator account email; this does not replace required directory legal/public contact details. |
| `INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME` / `INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME` | Advanced | None | Optional profile names; supply both or neither. |
| `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` | Advanced (Secret) | None | Required only for configured Local bootstrap, through the selected secret authority. The initial password must be privately replaced before ordinary sign-in. |

For Local bootstrap, use the configured UUIDv7 subject as the username. Bootstrap
creates the account and administrator linkage without requiring email delivery.
After setup completes, changing or leaving the bootstrap password configured
does not reset the account. Use the credential-administration flow instead.

---

## 9. Operator Legal Identity (Production Gate)

Mandatory legal identity fields required before production traffic can be served:

| Variable | Status | Default | Description |
|---|---|---|---|
| `INSTANCE__OPERATORIDENTITY__OPERATORID` | **Baseline** | None | UUIDv7 unique identifier for the operating legal entity. |
| `INSTANCE__OPERATORIDENTITY__PUBLICNAME` | **Baseline** | None | Public brand name of the deploying organization. |
| `INSTANCE__OPERATORIDENTITY__LEGALNAME` | **Baseline** | None | Full legal registered entity name. |
| `INSTANCE__OPERATORIDENTITY__ISOFFICIALINSTANCE` | **Baseline** | `false` | True only for the authoritative upstream project deployment. |
| `INSTANCE__OPERATORIDENTITY__OPERATORKINDCODE` | **Baseline** | `community` | Operator-kind code validated by the instance identity policy. |
| `INSTANCE__OPERATORIDENTITY__JURISDICTIONCOUNTRYCODE` | **Baseline** | `US` | Two-letter jurisdiction country code, such as `US`, `GB`, or `FR`. |
| `INSTANCE__OPERATORIDENTITY__PUBLICCONTACTEMAIL` | **Baseline** | None | Public contact email for legal and privacy inquiries. |
| `INSTANCE__OPERATORIDENTITY__WEBSITEURL` | **Baseline** | None | Public website URL of the operating organization. |
| `INSTANCE__OPERATORIDENTITY__LEGALNOTICEURL` | **Baseline** | None | Public URL for legal notice / imprint. |
| `INSTANCE__OPERATORIDENTITY__TERMSURL` | **Baseline** | None | Public URL for Terms of Service. |
| `INSTANCE__OPERATORIDENTITY__PRIVACYURL` | **Baseline** | None | Public URL for Privacy Policy. |

---

## 10. Advanced Authorization (Cerbos PDP)

These variables are optional and apply when using external Cerbos authorization instead of built-in Local RBAC:

| Variable | Status | Default | Description |
|---|---|---|---|
| `AUTHORIZATION_PROVIDER` | Advanced | `local` | Authorization mode: `local` (built-in RBAC) or `cerbos`. |
| `CERBOS_GRPC_ENDPOINT` | Advanced | `http://cerbos:3593` | gRPC endpoint of the external Cerbos Policy Decision Point. |
| `CERBOS_USE_TLS` | Advanced | `false` | Enable TLS when communicating with external Cerbos over gRPC. |
| `CERBOS_PLAINTEXT_MODE` | Advanced | `true` | Set `true` for internal container networks (`h2c`); `false` when TLS is active. |
| `CERBOS_PUBLICATION_RECEIPT_BASE64` | Required for deployment-selected Cerbos | None | Exact operator-signed JSON publication statement, base64 encoded. |
| `CERBOS_PUBLICATION_SIGNATURE_BASE64` | Required for deployment-selected Cerbos | None | Detached RSA-PSS/SHA-256 signature, base64 encoded. |
| `CERBOS_PUBLICATION_PUBLIC_KEY_PEM` | Required for deployment-selected Cerbos | None | Independently managed operator public key, at least 3072-bit RSA. Never inject the private key. |
| `CERBOS_PUBLICATION_REVISION` | Required for deployment-selected Cerbos | None | Exact candidate revision selected by the deployment, not copied from untrusted receipt input. |
| `CERBOS_PUBLICATION_ENVIRONMENT` | Required for deployment-selected Cerbos | None | Independently selected target environment, for example `production`. |

Compose and Aspire map these public verification inputs to
`Cerbos:PolicyPackage:Publication`. For other hosts set
`Cerbos__PolicyPackage__Publication__ReceiptBase64`, `SignatureBase64`,
`PublicKeyPem`, `Revision`, and `Environment` under the same prefix.

Publish the exact candidate ZIP or repository policy package with a fresh
administrator pair, upload schemas before policies, reload all affected PDPs and
verify installation. Retain the ZIP's `manifest.json` and `INSTRUCTIONS.md`
outside the extracted policy directory before recursive upload or runtime mounting;
they describe the package and are not policy artifacts.
The application never saves that pair and clears the form
after each attempt, on navigation and when the component is disposed. For an explicit read-only live check, the authorized policy
sync POST accepts `inspectOnly: true` together with a fresh pair.

The signed UTF-8 JSON statement contains `SchemaVersion` equal to
`cerbos-publication.v1`, the candidate `Revision`, manifest `ContentHash`,
`Environment`, exact `PdpEndpoint`, UTC `PublishedAt` and `ExpiresAt`, and
`Succeeded`. Sign success only after verified publication. Expiry must be after
publication and no more than 24 hours later. This statement records the trusted
operator's observation; it does not continuously monitor the store.

Use the operator's approved external signing authority. With an RSA key exposed
temporarily to the operator's signing tool, the interoperable signature command is:

```bash
openssl dgst -sha256 -sign "$CERBOS_PUBLICATION_SIGNING_KEY_FILE" \
  -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:digest \
  -out receipt.sig receipt.json
```

For hosted Coolify deployment, restrict the `production` environment to reviewed
protected branches/tags, require independent environment approval and configure
its independent `CERBOS_PUBLICATION_PUBLIC_KEY_PEM` and
`CERBOS_PUBLICATION_PDP_ENDPOINT` variables. Dispatch the deploy workflow with
the exact signed JSON and base64 signature. Missing, failed, expired, tampered or
wrong-candidate statements block deployment, including ordinary pushes without
receipts. CI cannot publish using the PDP's standing administrator account.

Manual Compose recovery uses a separate, explicitly selected publication profile:
first close affected resource deployments and drain old policy writers through
the existing resource control-plane procedure. A signed publication receipt cannot
reopen them; verify all PDP aliases before separately activating resources.
Export fresh `CERBOS_USERNAME` and `CERBOS_PASSWORD` for that operator shell, then
run `docker compose --profile authz --profile cerbos-publication run --rm cerbos-policy-sync`
and unset both variables afterward. Neither value belongs in Event's persistent
runtime environment. Direct `cerbosctl` recovery likewise uploads schemas, policies
and reloads before the operator signs a statement.

Startup checks gRPC health and the signed declaration without administering the
provider. A restart after receipt expiry needs a renewed declaration following
operator verification. Failed or uncertain uploads require inspection before a
fresh attempt; Event does not promise rollback. Keep resource-deployment activation
separate, drain old application replicas, then revoke their old provider credentials.
Changing the trusted public key revokes old signatures and requires replacement
receipts. No cleanup of historical backups or provider credentials is automatic.
If the receipt expires after the API rollout but before the UI rollout, the UI
trigger is blocked without rolling back the API. Reconcile deployed versions,
verify publication again and dispatch with a fresh statement for the same candidate.

> [!NOTE]
> **We recommend:** Use `AUTHORIZATION_PROVIDER=local` for simplicity and lowest resource overhead. Use `cerbos` only for large multi-tenant deployments requiring dynamic runtime policy overrides.

---

## 11. Advanced Outgoing Webhooks (Svix Infrastructure)

Optional dials when operating the `webhooks` Compose profile with self-hosted Svix:

| Variable | Status | Default | Description |
|---|---|---|---|
| `WEBHOOKS_PROVIDER` | Advanced | `Disabled` | Webhook delivery mode: `Disabled`, `Local`, `Svix`, `Composite`, or `DryRun`. |
| `SVIX_SERVER_URL` | Advanced | `http://svix:8071` | URL of the Svix instance when `WEBHOOKS_PROVIDER=Svix`. |
| `SVIX_AUTH_TOKEN` | Advanced (Secret) | None | Administrative auth token for Svix API. |
| `SVIX_QUEUE_TYPE` | Advanced | `redis` | Svix internal queue: `redis` or `memory`. |
| `SVIX_CACHE_TYPE` | Advanced | `redis` | Svix internal cache: `redis` or `memory`. |
| `SVIX_REDIS_DSN` | Advanced | `redis://redis:6379` | Redis connection URL for Svix workers. |
| `SVIX_JWT_SECRET` | Advanced (Secret) | None | JWT signing secret for Svix tokens. |

---

### Local webhook destination policy

Local delivery and registration-provider webhook submissions validate every
resolved address again when opening the connection. One forbidden address
rejects the complete answer set before sending a body or signature. Connections
use the approved IP while retaining the destination hostname for TLS and
certificate checks. Redirects, cookies and system proxies are disabled.

For deliberate internal delivery, configure operator-owned
`Webhooks:Local:AllowedPrivateCidrs` with the smallest required networks.
`Webhooks:Local:BlockPrivateNetworks=false` permits private destinations but
does not permit metadata services, unspecified/multicast addresses or scoped
IPv6 link-local destinations. IPv4 link-local metadata, AWS IPv6 metadata,
Alibaba metadata and metadata hostnames remain denied even with broad CIDRs.
These webhook exceptions do not authorize tenant AI or other outbound clients.

Each request opens a newly vetted connection, so DNS and policy changes apply
before the next handoff. This increases connection setup costs. A policy denial
is permanent rather than a transient network retry; update the destination or
its approved operator policy before retrying delivery.

## AI Provider Network Authority

Tenant-owned AI endpoints are public-only, even when the operator enables
local endpoints for the instance or the caller is a platform administrator.
Model discovery has an explicit `Tenant` or `Instance` scope; omitted scope
is tenant scope. Instance discovery requires platform administration and an
already configured, enabled OpenAI-compatible origin. Save the approved
instance origin before discovering models on a private endpoint.

Discovery and chat validate every DNS answer when connecting. Connections
retain hostname/TLS validation and disable redirects, proxies and cookies.
One forbidden answer rejects the destination; metadata services remain denied
even for an approved private instance provider.

Tenant Azure integrations supply their own API key. Instance default Azure
credentials cannot be borrowed by tenant requests, and managed identity is
excluded because it requires forbidden metadata access. Provider failure
returns a bounded error, not synthesized model availability or another provider.

## 12. Auxiliary Service Profiles (Optional Extensions)

These variables configure optional third-party integrations enabled via Docker Compose profiles:

### Formbricks (Feedback & Survey Mirror Stack)
*Enabled via `docker compose --profile formbricks up -d`*:

| Variable | Status | Default | Description |
|---|---|---|---|
| `FORMBRICKS_HTTP_PORT` | Advanced | `3005` | Formbricks web application port. |
| `FORMBRICKS_WEBAPP_URL` | Advanced | `http://localhost:3005` | Public URL for Formbricks app. |
| `FORMBRICKS_DATABASE_NAME` | Advanced | `formbricks` | Database name for Formbricks state. |
| `FORMBRICKS_DATABASE_PASSWORD` | Advanced (Secret) | None | Database password for Formbricks. |
| `FORMBRICKS_NEXTAUTH_SECRET` | Advanced (Secret) | None | NextAuth encryption secret (`openssl rand -hex 32`). |
| `FORMBRICKS_ENCRYPTION_KEY` | Advanced (Secret) | None | Formbricks data encryption key (`openssl rand -hex 32`). |
| `FORMBRICKS_CRON_SECRET` | Advanced (Secret) | None | Internal cron secret for periodic survey triggers. |

### Weblate (Crowdsourced Translation Server)
*Enabled via `docker compose --profile localization up -d`*:

| Variable | Status | Default | Description |
|---|---|---|---|
| `WEBLATE_HTTP_PORT` | Advanced | `8083` | Weblate web interface port. |
| `WEBLATE_SITE_DOMAIN` | Advanced | `localhost:8083` | Domain name for Weblate. |
| `WEBLATE_ADMIN_NAME` | Advanced | `Admin` | Initial Weblate administrator username. |
| `WEBLATE_ADMIN_EMAIL` | Advanced | `admin@example.org` | Initial Weblate administrator email. |
| `WEBLATE_ADMIN_PASSWORD` | Advanced (Secret) | None | Initial Weblate administrator password. |
| `WEBLATE_POSTGRES_PASSWORD` | Advanced (Secret) | None | Database password for Weblate database container. |

### External Moderation (Coop & Osprey)
*Enabled via `docker compose --profile moderation up -d`*:

| Variable | Status | Default | Description |
|---|---|---|---|
| `REPORTING_MODE` | Advanced | `LocalOnly` | Mode: `LocalOnly` (built-in in-database moderation), `Coop`, `Osprey`, or `Composite`. |
| `REPORTING_COOP_ENDPOINT_URL` | Advanced | `http://coop:8080` | Endpoint URL of Coop server. |
| `REPORTING_COOP_API_KEY` | Advanced (Secret) | None | API key for Coop authentication. |
| `REPORTING_OSPREY_ENDPOINT_URL` | Advanced | None | Endpoint URL of Osprey coordinator. |
| `REPORTING_OSPREY_API_KEY` | Advanced (Secret) | None | API key for Osprey coordinator. |

### Listmonk (Newsletter & Subscriber Sync)

| Variable | Status | Default | Description |
|---|---|---|---|
| `LISTMONK_ENABLED` | Advanced | `false` | Enable automatic subscriber synchronization on registration. |
| `LISTMONK_INSTANCE_URL` | Advanced | None | URL of the external Listmonk instance. |
| `LISTMONK_DEFAULT_LIST_ID` | Advanced | `0` | Default mailing list ID for event attendees. |
| `LISTMONK_API_USERNAME` | Advanced | None | Listmonk API username. |
| `LISTMONK_API_KEY` | Advanced (Secret) | None | Listmonk API token. |

---

<!-- BEGIN GENERATED ENVIRONMENT CATALOGUE -->
## Complete Environment Variable Catalogue

This generated reference lists every supported catalogue variable, including advanced and profile-specific settings intentionally omitted from the curated `.env.example`. Add only the overrides your deployment needs.

Defaults below are declared metadata, never values read from a deployment or secret store. Secret values must be supplied through the selected secret authority.

| Variable | Category | Sensitivity | Default | Requirement | Restart |
|---|---|---|---|---|---|
| `PUBLIC_BASE_URL` | platform | public | None | optional | process |
| `EFFECTIVEHOSTAUTHORITY__INSTANCEHOSTS__0` | deployment | public | None | optional | process |
| `EFFECTIVEHOSTAUTHORITY__SUBDOMAINBASEDOMAINS__0` | deployment | public | None | optional | process |
| `FORWARDEDHEADERSTRUST__FORWARDLIMIT` | deployment | public | 1 | defaulted | process |
| `FORWARDEDHEADERSTRUST__TRUSTLOOPBACKPROXY` | deployment | public | false | defaulted | process |
| `FORWARDEDHEADERSTRUST__KNOWNPROXIES__0` | deployment | public | None | optional | process |
| `FORWARDEDHEADERSTRUST__KNOWNNETWORKS__0` | deployment | public | None | optional | process |
| `OPERATIONALENDPOINTS__ADDRESS` | deployment | public | None | optional | process |
| `OPERATIONALENDPOINTS__PORT` | deployment | public | None | optional | process |
| `API_HTTP_PORT` | deployment | public | None | optional | deployment |
| `UI_HTTP_PORT` | deployment | public | None | optional | deployment |
| `KEYCLOAK_HTTP_PORT` | identity | public | None | optional | process |
| `MAILPIT_UI_PORT` | integration | public | 8025 | defaulted | deployment |
| `DEPLOYMENT_MODE` | deployment | public | None | optional | deployment |
| `SECRET_PROVIDER` | security | sensitive | None | required | process |
| `INFISICAL_COMBINED_CLIENT_ID` | security | public | None | optional | deployment |
| `INFISICAL_COMBINED_CLIENT_SECRET` | security | sensitive | None | optional | deployment |
| `KEYCLOAK__AUDIENCE` | identity | public | islamu-event-api | defaulted | process |
| `KEYCLOAK__PERMITTEDCLIENTS__0` | identity | public | None | optional | process |
| `EVENT_DP_API_RING` | platform | secret | None (secret) | optional | process |
| `EVENT_DP_API_ADMISSION` | platform | secret | None (secret) | optional | process |
| `EVENT_DP_API_ADMISSION_SHA256` | platform | sensitive | None | optional | process |
| `EVENT_DP_API_MINIMUM_GENERATION` | platform | public | None | optional | process |
| `EVENT_DP_BFF_RING` | platform | secret | None (secret) | optional | process |
| `EVENT_DP_BFF_ADMISSION` | platform | secret | None (secret) | optional | process |
| `EVENT_DP_BFF_ADMISSION_SHA256` | platform | sensitive | None | optional | process |
| `EVENT_DP_BFF_MINIMUM_GENERATION` | platform | public | None | optional | process |
| `EVENT_DP_COMBINED_RING` | platform | secret | None (secret) | optional | process |
| `EVENT_DP_COMBINED_ADMISSION` | platform | secret | None (secret) | optional | process |
| `EVENT_DP_COMBINED_ADMISSION_SHA256` | platform | sensitive | None | optional | process |
| `EVENT_DP_COMBINED_MINIMUM_GENERATION` | platform | public | None | optional | process |
| `DATABASE_PROVIDER` | database | public | PostgreSql | defaulted | process |
| `DATABASE_HOST` | database | public | None | required | process |
| `DATABASE_PORT` | database | public | None | optional | process |
| `DATABASE_NAME` | database | public | None | required | process |
| `DATABASE_SCHEMA` | database | public | islamu_event | defaulted | process |
| `DATABASE_RUNTIME_USERNAME` | database | public | None | required | process |
| `DATABASE_RUNTIME_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_GLOBAL_USERNAME` | database | public | None | required | process |
| `DATABASE_GLOBAL_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_INSTANCE_KEY_READ_USERNAME` | database | public | None | required | process |
| `DATABASE_INSTANCE_KEY_READ_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_INSTANCE_KEY_WRITE_USERNAME` | database | public | None | required | process |
| `DATABASE_INSTANCE_KEY_WRITE_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_INSTANCE_KEY_ISSUANCE_USERNAME` | database | public | None | required | process |
| `DATABASE_INSTANCE_KEY_ISSUANCE_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_PLATFORM_KEY_REPORT_USERNAME` | database | public | None | required | process |
| `DATABASE_PLATFORM_KEY_REPORT_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_USERNAME` | database | public | None | required | process |
| `DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_EMAIL_POLICY_TENANT_MUTATION_USERNAME` | database | public | None | required | process |
| `DATABASE_EMAIL_POLICY_TENANT_MUTATION_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_MIGRATOR_USERNAME` | database | public | None | required | process |
| `DATABASE_MIGRATOR_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_TLS_MODE` | database | public | Prefer | defaulted | process |
| `AUTHENTICATION_PROVIDER` | platform | public | None | optional | process |
| `AUTHENTICATION_CREDENTIAL_PROFILE_REVISION` | security | public | None | optional | process |
| `GOOGLE_CLIENT_ID` | identity | public | None | optional | process |
| `GOOGLE_CLIENT_SECRET` | identity | secret | None (secret) | optional | process |
| `IDENTITYCORRELATION__TRUSTEDISSUERS__0` | identity | public | None | optional | process |
| `ATPROTO_LOGIN_ENABLED` | platform | public | None | optional | process |
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | identity | public | None | required | process |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | identity | sensitive | None | required | process |
| `INSTANCE_BOOTSTRAP_BINDING_GENERATION` | identity | public | None | required | process |
| `INSTANCE_BOOTSTRAP_ADMIN_EMAIL` | identity | sensitive | None | optional | process |
| `INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME` | identity | sensitive | None | optional | process |
| `INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME` | identity | sensitive | None | optional | process |
| `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` | identity | secret | None (secret) | required | process |
| `AGENT_BROWSER_SEED_ENABLED` | identity | public | false | defaulted | process |
| `AGENT_BROWSER_PERSONA_PASSWORD` | identity | secret | None (secret) | optional | process |
| `AGENT_BROWSER_REDIS_PASSWORD` | security | secret | None (secret) | required | process |
| `AUTHENTICATION_LOCAL_JWT_KEY` | security | secret | None (secret) | optional | process |
| `AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD` | security | public | 5 | defaulted | process |
| `AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES` | security | public | 15 | defaulted | process |
| `AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_PATH` | security | public | None | optional | process |
| `AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_SHA256` | security | public | None | optional | process |
| `AUTHENTICATION_LOCAL_PASSWORD_HASH_ITERATIONS` | security | public | 210000 | defaulted | process |
| `IDENTITY_DATABASE_TOPOLOGY` | integration | public | colocated | defaulted | process |
| `KEYCLOAK_ENDPOINT` | identity | secret | None (secret) | required | process |
| `KEYCLOAK_REALM` | identity | secret | None (secret) | required | process |
| `KEYCLOAK_BLAZOR_CLIENT_ID` | identity | public | None | required | process |
| `KEYCLOAK_BLAZOR_CLIENT_SECRET` | identity | secret | None (secret) | required | process |
| `KEYCLOAK_LIFECYCLE_CLIENT_SECRET` | identity | secret | None (secret) | optional | process |
| `KEYCLOAKLIFECYCLEEMAIL__ENABLED` | identity | public | false | defaulted | process |
| `KEYCLOAKLIFECYCLEEMAIL__BASEURL` | identity | public | None | optional | process |
| `KEYCLOAKLIFECYCLEEMAIL__REALM` | identity | public | None | optional | process |
| `KEYCLOAKLIFECYCLEEMAIL__CLIENTID` | identity | public | None | optional | process |
| `KEYCLOAKLIFECYCLEEMAIL__DEFAULTCLIENTID` | identity | public | None | optional | process |
| `KEYCLOAKLIFECYCLEEMAIL__PROVIDERVERSION` | identity | public | None | optional | process |
| `KEYCLOAKLIFECYCLEEMAIL__ALLOWLOCALURLS` | identity | public | false | defaulted | process |
| `KEYCLOAK_DB_DATABASE` | integration | public | None | optional | deployment |
| `KEYCLOAK_DB_USERNAME` | integration | public | None | optional | deployment |
| `KEYCLOAK_DB_PASSWORD` | integration | sensitive | None | optional | deployment |
| `KEYCLOAK_ADMIN` | integration | public | None | optional | deployment |
| `KEYCLOAK_ADMIN_PASSWORD` | integration | sensitive | None | optional | deployment |
| `LOCAL_STORAGE_ROOT_PATH` | storage | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_ENABLED` | messaging | public | false | defaulted | capability |
| `ERASURE_DATABASE_TOPOLOGY` | platform | public | None | optional | process |
| `ERASURE_EMBEDDED_PATH` | platform | public | None | optional | process |
| `PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID` | platform | public | None | optional | process |
| `PRIVACY_ERASURE_IDENTITY_FENCE_KEY` | platform | secret | None (secret) | optional | process |
| `SETUP_SECRET` | platform | secret | None (secret) | required | process |
| `INSTANCE_BOOTSTRAP_MODE` | identity | public | None | required | process |
| `INSTANCE__OPERATORIDENTITY__OPERATORID` | identity | public | None | required | process |
| `INSTANCE__OPERATORIDENTITY__PUBLICNAME` | identity | public | None | required | process |
| `INSTANCE__OPERATORIDENTITY__LEGALNAME` | identity | public | None | required | process |
| `INSTANCE__OPERATORIDENTITY__ISOFFICIALINSTANCE` | identity | public | None | optional | process |
| `INSTANCE__OPERATORIDENTITY__OFFICIALORIGIN` | identity | public | None | optional | process |
| `INSTANCE__OPERATORIDENTITY__OPERATORKINDCODE` | identity | public | None | required | process |
| `INSTANCE__OPERATORIDENTITY__JURISDICTIONCOUNTRYCODE` | identity | public | None | required | process |
| `INSTANCE__OPERATORIDENTITY__PUBLICCONTACTEMAIL` | identity | sensitive | None | optional | process |
| `INSTANCE__OPERATORIDENTITY__WEBSITEURL` | identity | public | None | optional | process |
| `INSTANCE__OPERATORIDENTITY__LEGALNOTICEURL` | identity | public | None | optional | process |
| `INSTANCE__OPERATORIDENTITY__TERMSURL` | identity | public | None | optional | process |
| `INSTANCE__OPERATORIDENTITY__PRIVACYURL` | identity | public | None | optional | process |
| `CONFIGURATION_MANIFEST_MODE` | deployment | public | Off | defaulted | deployment |
| `CONFIGURATION_MANIFEST_PATH` | deployment | public | None | optional | deployment |
| `CONFIGURATION_MANIFEST_HOST_DIRECTORY` | deployment | public | None | optional | deployment |
| `ISLAMU_SETUP_SIGNING` | security | secret | None (secret) | optional | process |
| `ISLAMU_SETUP_ENCRYPTION` | security | secret | None (secret) | optional | process |
| `ASPNETCORE_ENVIRONMENT` | deployment | public | None | optional | process |
| `DOTNET_ENVIRONMENT` | deployment | public | None | optional | process |
| `MAIL_SMTP_HOST` | messaging | secret | None (secret) | optional | capability |
| `MAIL_SMTP_PORT` | messaging | secret | None (secret) | optional | capability |
| `MAIL_SMTP_FROM_ADDRESS` | messaging | secret | None (secret) | optional | capability |
| `MAIL_SMTP_FROM_NAME` | messaging | secret | None (secret) | optional | capability |
| `MAIL_SMTP_USERNAME` | messaging | secret | None (secret) | optional | capability |
| `MAIL_SMTP_PASSWORD` | messaging | secret | None (secret) | optional | capability |
| `MAIL_SMTP_ENCRYPTION` | messaging | public | None | optional | capability |
| `MINIO_API_PORT` | integration | public | None | optional | deployment |
| `MINIO_CONSOLE_PORT` | integration | public | None | optional | deployment |
| `CERBOS_HTTP_PORT` | deployment | public | None | optional | deployment |
| `CERBOS_GRPC_PORT` | deployment | public | None | optional | deployment |
| `SVIX_HTTP_PORT` | integration | public | None | optional | deployment |
| `WEBLATE_HTTP_PORT` | integration | public | None | optional | deployment |
| `COOP_HTTP_PORT` | integration | public | None | optional | deployment |
| `COOP_CLIENT_HTTP_PORT` | integration | public | None | optional | deployment |
| `OSPREY_BIDI_STREAM_PORT` | integration | public | None | optional | deployment |
| `OSPREY_SYNC_ACTION_PORT` | integration | public | None | optional | deployment |
| `FORMBRICKS_HTTP_PORT` | integration | public | None | optional | deployment |
| `FORMBRICKS_WEBAPP_URL` | integration | public | None | optional | deployment |
| `FORMBRICKS_DATABASE_NAME` | integration | public | None | optional | deployment |
| `FORMBRICKS_DATABASE_USER` | integration | public | None | optional | deployment |
| `FORMBRICKS_DATABASE_PASSWORD` | integration | sensitive | None | optional | deployment |
| `FORMBRICKS_NEXTAUTH_SECRET` | integration | sensitive | None | required | deployment |
| `FORMBRICKS_ENCRYPTION_KEY` | integration | sensitive | None | required | deployment |
| `FORMBRICKS_CRON_SECRET` | integration | sensitive | None | required | deployment |
| `FORMBRICKS_HUB_API_KEY` | integration | sensitive | None | required | deployment |
| `FORMBRICKS_CUBEJS_API_SECRET` | integration | sensitive | None | required | deployment |
| `INFISICAL_URL` | security | public | None | optional | process |
| `INFISICAL_PROJECT_ID` | security | public | None | optional | process |
| `INFISICAL_CLIENT_ID` | security | public | None | optional | process |
| `INFISICAL_CLIENT_SECRET` | security | sensitive | None | optional | process |
| `INFISICAL_ENV` | security | public | None | optional | process |
| `INFISICAL_API_CLIENT_ID` | security | public | None | optional | deployment |
| `INFISICAL_API_CLIENT_SECRET` | security | sensitive | None | optional | deployment |
| `INFISICAL_BFF_CLIENT_ID` | security | public | None | optional | deployment |
| `INFISICAL_BFF_CLIENT_SECRET` | security | sensitive | None | optional | deployment |
| `INFISICAL_MIGRATION_CLIENT_ID` | security | public | None | optional | deployment |
| `INFISICAL_MIGRATION_CLIENT_SECRET` | security | sensitive | None | optional | deployment |
| `DATABASE_TRUST_SERVER_CERTIFICATE` | database | public | false | defaulted | process |
| `KEYCLOAK_REQUIRE_HTTPS_METADATA` | identity | public | None | optional | process |
| `IDENTITY_DATABASE_PROVIDER` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_HOST` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_PORT` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_NAME` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_SCHEMA` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_TLS_MODE` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_RUNTIME_USERNAME` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_RUNTIME_PASSWORD` | integration | secret | None (secret) | optional | process |
| `IDENTITY_DATABASE_MIGRATOR_USERNAME` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_MIGRATOR_PASSWORD` | integration | secret | None (secret) | optional | process |
| `IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME` | identity | public | None | external topology | process |
| `IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD` | identity | secret | None (secret) | external topology | process |
| `IDENTITY_DATABASE_SERVER_VERSION` | integration | public | Provider default | optional | process |
| `SETUP_SECRET_REQUIRED` | platform | sensitive | None | optional | process |
| `HOSTING_REPLICA_COUNT` | deployment | public | 1 | defaulted | deployment |
| `PROMOTIONS_CODE_LOOKUP_ACTIVE_KEY_VERSION` | platform | public | None | optional | process |
| `PROMOTIONS_CODE_LOOKUP_HMAC_KEY` | security | secret | None (secret) | optional | process |
| `CONTROL_PLANE_MANAGED_MODE` | deployment | public | None | optional | deployment |
| `CONTROL_PLANE_URL` | deployment | public | None | optional | deployment |
| `CONTROL_PLANE_INSTANCE_ID` | deployment | public | None | optional | deployment |
| `CONTROL_PLANE_REGISTRATION_TOKEN` | deployment | sensitive | None | optional | deployment |
| `CONTROL_PLANE_MAXIMUM_TENANT_COUNT` | deployment | public | None | optional | deployment |
| `CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL` | deployment | public | None | optional | deployment |
| `GEOCODING_PROVIDER` | integration | public | None | optional | capability |
| `GEOCODING_ENDPOINT` | integration | public | None | optional | capability |
| `GEOCODING_LANGUAGE` | integration | public | None | optional | capability |
| `GEOCODING_COUNTRY_CODES` | integration | public | None | optional | capability |
| `GEOCODING_MAXIMUM_RESULTS` | integration | public | None | optional | capability |
| `GEOCODING_MAXIMUM_RESPONSE_BYTES` | integration | public | None | optional | capability |
| `GEOCODING_DATASET_VERSION` | integration | public | None | optional | capability |
| `GEOCODING_TOTAL_TIMEOUT_MILLISECONDS` | integration | public | None | optional | capability |
| `GEOCODING_MAXIMUM_RETRY_COUNT` | integration | public | None | optional | capability |
| `GEOCODING_RETRY_DELAYS_MILLISECONDS` | integration | public | None | optional | capability |
| `GEOCODING_READINESS_TIMEOUT_MILLISECONDS` | integration | public | None | optional | capability |
| `GEOCODING_SELECTION_LIFETIME_SECONDS` | integration | public | None | optional | capability |
| `PROVISIONING_TRUSTED` | platform | public | None | optional | process |
| `PROVISIONING_MODE` | platform | public | None | optional | process |
| `MANAGED_CLIENT_EXTERNAL_PROVIDER` | platform | public | None | optional | process |
| `PHYSICAL_TENANCY_MODE` | platform | public | None | optional | process |
| `API_ENDPOINT` | platform | public | None | optional | process |
| `INSTANCE_ADMIN_PUBLIC_ORIGIN` | deployment | public | None | optional | deployment |
| `INSTANCE__OPERATORIDENTITY__REGISTRATIONIDENTIFIER` | identity | public | None | optional | process |
| `PAYMENTS_STRIPE_MODE` | platform | public | None | optional | process |
| `PAYMENTS_ORGANIZER_DIRECT_PROVIDER_CODE` | platform | public | None | optional | process |
| `PAYMENTS_ORGANIZER_DIRECT_CONNECT_PLATFORM_ID` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__COMPLAINTOWNER` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__REFUNDOWNER` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__DISPUTEOWNER` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__RECONCILIATIONOWNER` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__ACTIVATIONSTATUS` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__REFUNDPOLICYLANGUAGETAG` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__STATEMENTDESCRIPTOR` | platform | public | None | optional | process |
| `PAYMENTS__CHECKOUTGOVERNANCE__CHARGETYPE` | platform | public | None | optional | process |
| `STRIPE_PLATFORM_SECRET_KEY` | platform | secret | None (secret) | optional | process |
| `STRIPE_WEBHOOK_SECRET` | platform | secret | None (secret) | optional | process |
| `ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY` | security | secret | None (secret) | optional | process |
| `ADMISSIONS__CREDENTIALLOOKUP__ACTIVEKEYVERSION` | security | public | None | optional | process |
| `ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY` | security | secret | None (secret) | optional | process |
| `ADMISSIONS_RECOVERY_CAPABILITY_HMAC_KEY` | security | secret | None (secret) | optional | process |
| `ADMISSIONS__RECOVERY__ACTIVEKEYVERSION` | security | public | None | optional | process |
| `ADMISSIONS__RECOVERY__CAPABILITYLIFETIMEMINUTES` | security | public | None | optional | process |
| `ADMISSIONS__RECOVERY__RATELIMITBUCKETCOUNT` | security | public | None | optional | process |
| `ADMISSIONS__RECOVERY__RATELIMITPERMITCOUNT` | security | public | None | optional | process |
| `ADMISSIONS__RECOVERY__RATELIMITWINDOWSECONDS` | security | public | None | optional | process |
| `RATELIMITING__ANONYMOUSREGISTRATION__IPPERMITLIMIT` | security | public | 10 | defaulted | process |
| `RATELIMITING__ANONYMOUSREGISTRATION__SUBNETPERMITLIMIT` | security | public | 40 | defaulted | process |
| `RATELIMITING__ANONYMOUSREGISTRATION__WINDOWSECONDS` | security | public | 60 | defaulted | process |
| `RATELIMITING__ANONYMOUSREGISTRATION__CONCURRENCYLIMIT` | security | public | 8 | defaulted | process |
| `RATELIMITING__ANONYMOUSREGISTRATION__QUEUELIMIT` | security | public | 0 | defaulted | process |
| `TICKETING__RECOVERY__ENABLED` | security | public | None | optional | process |
| `TICKETING__RECOVERY__EXPECTEDRELEASEREVISION` | security | public | None | optional | process |
| `TICKETING__RECOVERY__EXPECTEDSCHEMAREVISION` | security | public | None | optional | process |
| `TICKETING__RECOVERY__MINIMUMRETAINEDKEYVERSION` | security | public | None | optional | process |
| `TICKETING__RECOVERY__MINIMUMAUTHORITYFLOOR` | security | public | None | optional | process |
| `TICKETING__RECOVERY__MINIMUMPROVIDERCURSOR` | security | public | None | optional | process |
| `TICKETING__RECOVERY__MINIMUMIDEMPOTENCYFLOOR` | security | public | None | optional | process |
| `TICKETING__RECOVERY__MINIMUMWORKERFENCE` | security | public | None | optional | process |
| `TICKETING__RECOVERY__WARNINGOLDESTDUESECONDS` | security | public | None | optional | process |
| `TICKETING__RECOVERY__UNHEALTHYOLDESTDUESECONDS` | security | public | None | optional | process |
| `TICKETING__RECOVERY__BACKLOGTHRESHOLD` | security | public | None | optional | process |
| `TICKETING__RECOVERY__DECLAREDRPOMINUTES` | security | public | None | optional | process |
| `TICKETING__RECOVERY__DECLAREDRTOMINUTES` | security | public | None | optional | process |
| `TICKETING__RECOVERY__MANIFESTSIGNINGKEYREFERENCE` | security | public | None | optional | process |
| `TICKETING__RECOVERY__RETAINEDKEYVERSIONS__0` | security | public | None | optional | process |
| `TICKETING_RECOVERY_MANIFEST_HMAC_KEY` | security | secret | None (secret) | required | process |
| `AUTHORIZATION_PROVIDER` | platform | public | None | optional | process |
| `CERBOS_GRPC_ENDPOINT` | integration | secret | None (secret) | optional | capability |
| `CERBOS_HTTP_ENDPOINT` | integration | public | None | optional | capability |
| `CERBOS_USE_TLS` | integration | public | None | optional | capability |
| `CERBOS_PLAINTEXT_MODE` | integration | public | None | optional | capability |
| `CERBOS_ADMIN_USERNAME` | integration | public | None | optional | deployment |
| `CERBOS_ADMIN_PASSWORD_HASH` | integration | sensitive | None | optional | deployment |
| `CERBOS_PUBLICATION_RECEIPT_BASE64` | integration | public | None | optional | process |
| `CERBOS_PUBLICATION_SIGNATURE_BASE64` | integration | public | None | optional | process |
| `CERBOS_PUBLICATION_PUBLIC_KEY_PEM` | integration | public | None | optional | process |
| `CERBOS_PUBLICATION_REVISION` | integration | public | None | optional | process |
| `CERBOS_PUBLICATION_ENVIRONMENT` | integration | public | None | optional | process |
| `CERBOS_PG_URL` | integration | public | None | optional | capability |
| `CERBOS_POSTGRES_USER` | integration | public | None | optional | deployment |
| `CERBOS_POSTGRES_PASSWORD` | integration | sensitive | None | optional | deployment |
| `CERBOS_POSTGRES_DB` | integration | public | None | optional | deployment |
| `STORAGE_S3_ENDPOINT` | storage | secret | None (secret) | optional | capability |
| `STORAGE_S3_PUBLIC_ENDPOINT` | storage | secret | None (secret) | optional | capability |
| `STORAGE_S3_REGION` | storage | secret | None (secret) | optional | capability |
| `STORAGE_S3_BUCKET_NAME` | storage | secret | None (secret) | optional | capability |
| `STORAGE_S3_ACCESS_KEY_ID` | storage | secret | None (secret) | optional | capability |
| `STORAGE_S3_SECRET_ACCESS_KEY` | storage | secret | None (secret) | optional | capability |
| `LOCAL_STORAGE_CREATE_ROOT_IF_MISSING` | storage | public | None | optional | capability |
| `STORAGE_RECONCILIATION_ENABLED` | storage | public | false | defaulted | capability |
| `STORAGE_RECONCILIATION_DRY_RUN` | storage | public | false | defaulted | capability |
| `STORAGE_RECONCILIATION_QUARANTINE_MISSING_OBJECTS` | storage | public | None | optional | capability |
| `STORAGE_RECONCILIATION_QUARANTINE_ORPHAN_LOCAL_FILES` | storage | public | None | optional | capability |
| `STORAGE_RECONCILIATION_DELETE_QUARANTINED_OBJECTS` | storage | public | None | optional | capability |
| `AI_PROVIDER` | integration | public | None | optional | capability |
| `AI_ENDPOINT` | integration | public | None | optional | capability |
| `AI_MODEL_ID` | integration | public | None | optional | capability |
| `AI_CREDENTIAL_SCOPE` | integration | public | None | optional | capability |
| `AI_TOOL_PROPOSALS_ENABLED` | integration | public | false | defaulted | capability |
| `MCP_ENABLED` | platform | public | false | defaulted | process |
| `MCP_ENDPOINT_PATH` | platform | public | None | optional | process |
| `MCP_STATELESS` | platform | public | None | optional | process |
| `MCP_ENABLE_LEGACY_SSE` | platform | public | None | optional | process |
| `WEB_PUSH_ENABLED` | messaging | public | false | defaulted | capability |
| `VAPID_SUBJECT` | messaging | public | None | optional | capability |
| `VAPID_PUBLIC_KEY` | messaging | public | None | optional | capability |
| `VAPID_PRIVATE_KEY` | messaging | sensitive | None | optional | capability |
| `MESSAGING_URI` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_CONNECTION_STRING_NAME` | messaging | sensitive | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_CONNECTION_STRING` | messaging | sensitive | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_EXCHANGE_NAME` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DISPATCH_QUEUE_NAME` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DISPATCH_ROUTING_KEY` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_EXCHANGE_NAME` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_QUEUE_NAME` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_ROUTING_KEY` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_PARKING_QUEUE_NAME` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_PARKING_ROUTING_KEY` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_CLIENT_PROVIDED_NAME` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_CONSUMER_ID` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_PREFETCH_COUNT` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_ENABLED` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_CONSUMER_ID` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_PREFETCH_COUNT` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISH_TIMEOUT_SECONDS` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_POLLING_INTERVAL_SECONDS` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_BATCH_SIZE` | messaging | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_RETRY_DELAY_SECONDS` | messaging | public | None | optional | capability |
| `WEBHOOKS_ENABLED` | integration | public | false | defaulted | capability |
| `WEBHOOKS_PROVIDER` | integration | public | None | optional | capability |
| `WEBHOOKS_SVIX_BASE_URL` | integration | public | None | optional | capability |
| `WEBHOOKS_SVIX_ENVIRONMENT` | integration | public | None | optional | capability |
| `WEBHOOKS_SVIX_PROVIDER_VERSION` | integration | public | None | optional | capability |
| `WEBHOOKS_SVIX_CAPABILITY_POLICY_VERSION` | integration | public | None | optional | capability |
| `WEBHOOKS_SVIX_AUTH_TOKEN_SECRET_REF` | integration | sensitive | None | optional | capability |
| `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET_REF` | integration | sensitive | None | optional | capability |
| `WEBHOOKS_SVIX_AUTH_TOKEN` | integration | secret | None (secret) | optional | capability |
| `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET` | integration | secret | None (secret) | optional | capability |
| `SVIX_TAG` | integration | public | None | optional | deployment |
| `SVIX_DB_DATABASE` | integration | public | None | optional | deployment |
| `SVIX_DB_USERNAME` | integration | public | None | optional | deployment |
| `SVIX_DB_PASSWORD` | integration | sensitive | None | optional | deployment |
| `SVIX_DB_DSN` | integration | public | None | optional | deployment |
| `SVIX_REDIS_DSN` | integration | public | None | optional | deployment |
| `SVIX_QUEUE_TYPE` | integration | public | None | optional | deployment |
| `SVIX_CACHE_TYPE` | integration | public | None | optional | deployment |
| `SVIX_JWT_SECRET` | integration | sensitive | None | optional | deployment |
| `WEBLATE_IMAGE` | integration | public | None | optional | deployment |
| `WEBLATE_SITE_DOMAIN` | integration | public | None | optional | deployment |
| `WEBLATE_ADMIN_NAME` | integration | public | None | optional | deployment |
| `WEBLATE_ADMIN_EMAIL` | integration | sensitive | None | optional | deployment |
| `WEBLATE_ADMIN_PASSWORD` | integration | sensitive | None | optional | deployment |
| `WEBLATE_POSTGRES_USER` | integration | public | None | optional | deployment |
| `WEBLATE_POSTGRES_PASSWORD` | integration | sensitive | None | optional | deployment |
| `WEBLATE_POSTGRES_DB` | integration | public | None | optional | deployment |
| `REPORTING_ENABLED` | observability | public | false | defaulted | none |
| `REPORTING_MODE` | observability | public | None | optional | none |
| `REPORTING_SYNC_REPORTS` | observability | public | None | optional | none |
| `REPORTING_EVALUATE_SIGNALS` | observability | public | None | optional | none |
| `REPORTING_MIRROR_REVIEW_QUEUE` | observability | public | None | optional | none |
| `REPORTING_EXECUTE_DECISIONS` | observability | public | None | optional | none |
| `REPORTING_HEALTH_STUCK_PROVIDER_SYNC_MINUTES` | observability | public | None | optional | none |
| `REPORTING_HEALTH_FAILED_PROVIDER_SYNC_WARNING_THRESHOLD` | observability | public | None | optional | none |
| `REPORTING_OSPREY_ENABLED` | observability | public | None | optional | none |
| `REPORTING_OSPREY_ENDPOINT_URL` | observability | public | None | optional | none |
| `REPORTING_OSPREY_API_KEY` | observability | sensitive | None | optional | none |
| `REPORTING_OSPREY_ALLOW_LOCAL_PROVIDER_ENDPOINTS` | observability | public | None | optional | none |
| `REPORTING_COOP_ENABLED` | observability | public | None | optional | none |
| `REPORTING_COOP_ENDPOINT_URL` | observability | public | None | optional | none |
| `REPORTING_COOP_API_KEY` | observability | sensitive | None | optional | none |
| `REPORTING_COOP_ALLOW_LOCAL_PROVIDER_ENDPOINTS` | observability | public | None | optional | none |
| `REPORTING_COOP_WEBHOOK_SECRET` | observability | sensitive | None | optional | none |
| `COOP_IMAGE` | integration | public | None | optional | deployment |
| `COOP_MIGRATIONS_IMAGE` | integration | public | None | optional | deployment |
| `COOP_CLIENT_IMAGE` | integration | public | None | optional | deployment |
| `COOP_NODE_ENV` | integration | public | None | optional | deployment |
| `COOP_OTEL_SERVICE_NAME` | integration | public | None | optional | deployment |
| `COOP_UI_URL` | integration | public | None | optional | deployment |
| `COOP_SESSION_SECRET` | integration | sensitive | None | optional | deployment |
| `COOP_DATABASE_USER` | integration | public | None | optional | deployment |
| `COOP_DATABASE_PASSWORD` | integration | sensitive | None | optional | deployment |
| `COOP_DATABASE_NAME` | integration | public | None | optional | deployment |
| `COOP_WAREHOUSE_ADAPTER` | integration | public | None | optional | deployment |
| `COOP_ANALYTICS_ADAPTER` | integration | public | None | optional | deployment |
| `COOP_SCYLLA_HOSTS` | integration | public | None | optional | deployment |
| `COOP_SCYLLA_USERNAME` | integration | public | None | optional | deployment |
| `COOP_SCYLLA_PASSWORD` | integration | sensitive | None | optional | deployment |
| `COOP_SCYLLA_LOCAL_DATACENTER` | integration | public | None | optional | deployment |
| `COOP_SCYLLA_SSL` | integration | public | None | optional | deployment |
| `OSPREY_IMAGE` | integration | public | None | optional | deployment |
| `OSPREY_RUST_LOG` | integration | public | None | optional | deployment |
| `OSPREY_POD_IP` | integration | public | None | optional | deployment |
| `LISTMONK_ENABLED` | integration | public | false | defaulted | capability |
| `LISTMONK_INSTANCE_URL` | integration | public | None | optional | capability |
| `LISTMONK_DEFAULT_LIST_ID` | integration | public | None | optional | capability |
| `LISTMONK_PRECONFIRM_SUBSCRIPTIONS` | integration | public | None | optional | capability |
| `LISTMONK_SYNC_ON_REGISTRATION` | integration | public | None | optional | capability |
| `LISTMONK_API_USERNAME` | integration | secret | None (secret) | optional | capability |
| `LISTMONK_API_KEY` | integration | secret | None (secret) | optional | capability |
| `ERASURE_WRITER_REPLICA_COUNT` | platform | public | None | optional | process |
| `ERASURE_BUSY_TIMEOUT_SECONDS` | platform | public | None | optional | process |
| `ERASURE_DATABASE_HOST` | database | public | None | optional | process |
| `ERASURE_DATABASE_PORT` | database | public | None | optional | process |
| `ERASURE_DATABASE_NAME` | database | public | None | optional | process |
| `ERASURE_DATABASE_RUNTIME_USERNAME` | database | public | None | optional | process |
| `ERASURE_DATABASE_RUNTIME_PASSWORD` | database | sensitive | None | optional | process |
| `ERASURE_DATABASE_MIGRATOR_USERNAME` | database | public | None | optional | process |
| `ERASURE_DATABASE_MIGRATOR_PASSWORD` | database | sensitive | None | optional | process |
| `ERASURE_DATABASE_TLS_MODE` | database | public | None | optional | process |
| `ERASURE_DATABASE_TRUST_SERVER_CERTIFICATE` | database | public | None | optional | process |
| `DATABASE_SERVER_VERSION` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_EMBEDDED_PATH` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_WRITER_REPLICA_COUNT` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_BUSY_TIMEOUT_SECONDS` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_HOST` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_PORT` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_DATABASE` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_TLS_MODE` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_TRUST_SERVER_CERTIFICATE` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_RUNTIME_USERNAME` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_RUNTIME_PASSWORD` | integration | sensitive | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_MIGRATOR_USERNAME` | integration | public | None | optional | deployment |
| `PRIVACY_ERASURE_AUTHORITY_MIGRATOR_PASSWORD` | integration | sensitive | None | optional | deployment |
| `SETUP_SECRET_FILE` | integration | sensitive | None | optional | deployment |
| `AI_OPENAI_API_KEY` | integration | secret | None (secret) | optional | capability |
| `AI_ANTHROPIC_API_KEY` | integration | secret | None (secret) | optional | capability |
| `AI_OPENAI_COMPATIBLE_API_KEY` | integration | secret | None (secret) | optional | capability |
| `AI_ANTHROPIC_COMPATIBLE_API_KEY` | integration | secret | None (secret) | optional | capability |
| `AI_AZURE_API_KEY` | integration | secret | None (secret) | optional | capability |
| `KEYCLOAK_INTERNAL_URL` | integration | public | None | optional | deployment |
| `REDIS_CONNECTION_STRING` | integration | sensitive | None | optional | deployment |
| `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | platform | secret | None (secret) | optional | process |
| `SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY` | security | secret | None (secret) | optional | process |
| `KEYCLOAK_CLIENT_ID` | identity | secret | None (secret) | optional | process |
| `ATPROTO_SESSION_ENCRYPTION_KEYRING` | platform | secret | None (secret) | optional | process |
| `ATPROTO_SESSION_JWT_PRIVATE_JWKS` | platform | secret | None (secret) | optional | process |
| `REGISTRATION_PROVIDER_API_TOKEN` | platform | secret | None (secret) | optional | process |
| `REGISTRATION_PROVIDER_WEBHOOK_SECRET` | platform | secret | None (secret) | optional | process |
| `POSTGRESQL_HOST` | database | secret | None (secret) | required | process |
| `POSTGRESQL_PORT` | database | secret | None (secret) | required | process |
| `POSTGRESQL_DATABASE` | database | secret | None (secret) | required | process |
| `POSTGRESQL_USERNAME` | database | secret | None (secret) | required | process |
| `POSTGRESQL_PASSWORD` | database | secret | None (secret) | required | process |
| `ANALYTICS_POSTHOG_PUBLIC_KEY` | observability | secret | None (secret) | optional | none |
| `ANALYTICS_POSTHOG_HOST` | observability | secret | None (secret) | optional | none |
| `ANALYTICS_PERSONAL_API_KEY` | observability | secret | None (secret) | optional | none |
| `LOCALIZATION_TMS_API_KEY` | platform | secret | None (secret) | optional | process |
| `CONTROL_PLANE_REGISTRATION_CREDENTIALS` | deployment | secret | None (secret) | optional | deployment |
<!-- END GENERATED ENVIRONMENT CATALOGUE -->

## Related Guides & Next Steps

* **[Secrets Management](secrets.md)** — Securely bind passwords, API keys, and certificates via Environment or Infisical.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Apply your `.env` configuration in a production split-container stack.
* **[Backup, Restore & Upgrade](backup-restore-upgrade.md)** — Production backup scripts and version migration procedures.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Practical solutions for configuration mismatches and startup errors.
