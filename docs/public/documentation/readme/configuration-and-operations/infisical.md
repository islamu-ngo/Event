---
description: Create the Infisical project, folder tree, and machine identity that ISLAMU Event reads.
---

# Infisical Setup

This guide describes the folder-and-key layout ISLAMU Event expects inside Infisical. Each process uses its own machine identity and exact runtime folders. Sharing a project and environment does not mean sharing read permissions.

Infisical is one of three approved authorities. See [Secrets Management](secrets.md) for how authority selection fails closed, and [Environment Variables](environment-variables.md) for the meaning and defaults of every key named below.

---

## 1. Create the Project

1. Sign in to your Infisical organization (cloud or self-hosted).
2. Create a project named **`ISLAMU Event`**.
3. Keep the default environment slugs, or create the ones your deployments use. `INFISICAL_ENV` selects the slug at runtime; typical values are `prod`, `staging`, and `dev`.

> [!IMPORTANT]
> One project per deployment lineage, one environment slug per running instance. Every replica of the same instance must read the identical project and environment, otherwise credential convergence cannot be proven during rotation.

---

## 2. Create the Machine Identity

ISLAMU Event authenticates with **Universal Auth** only.

1. Create independent Machine Identities for API, BFF and migration processes, or Combined and migration for Aspire Standalone.
2. Attach a **Universal Auth** authentication method and generate a Client ID and Client Secret.
3. Grant each service identity **read** access only to its required folders. The browser-facing BFF identity reads the exact `/runtime/bff/oidc/keycloak` and `/runtime/bff/oidc/google` roots, plus the separate `/runtime/bff/atproto/oauth` signing-key root. Do not grant it `/`, legacy `/keycloak`, `/blazor`, `/atproto`, `/database` or `/api` access. Use separate backend/provisioning identities for database and container credentials. Write access belongs only to the backend identity that performs an enabled Setup secret-write capability.
4. Copy the project UUID from the project settings page.

The bootstrap pairs are *secret zero*: inject each pair only into its consumer.
The API identity may have narrowly scoped write-only permission for the two
authentication credentials managed by setup, but must not have BFF read access.
Runtime identities must be denied `/provisioning/*`, `/infrastructure/*`, broad
`/keycloak` and `/cerbos`. Event checks requested roots locally; operators must
configure and verify the provider ACLs independently.

---

## 3. Point the Deployment at Infisical

For Compose or Aspire, set these through private operator injection:

```bash
SECRET_PROVIDER=Infisical
INFISICAL_URL=https://app.infisical.com   # or your self-hosted base URL
INFISICAL_PROJECT_ID=<project UUID>
INFISICAL_API_CLIENT_ID=<API identity>
INFISICAL_API_CLIENT_SECRET=<API credential>
INFISICAL_BFF_CLIENT_ID=<BFF identity>
INFISICAL_BFF_CLIENT_SECRET=<BFF credential>
INFISICAL_MIGRATION_CLIENT_ID=<migration identity>
INFISICAL_MIGRATION_CLIENT_SECRET=<migration credential>
INFISICAL_ENV=prod
```

When `SECRET_PROVIDER=Infisical`, only Infisical results are authoritative. The platform never silently falls back to environment variables or User Secrets for a value the authority failed to supply; startup fails closed instead.

Aspire Standalone uses `INFISICAL_COMBINED_CLIENT_ID/CLIENT_SECRET` instead of
the API and BFF pairs. A directly launched process receives only its own pair in
`SecretProvider__Infisical__ClientId/ClientSecret` (or the documented direct-process
`INFISICAL_CLIENT_ID/CLIENT_SECRET` fields). Those unqualified names are not a
Compose/Aspire fallback. All local profiles preserve the selected source.
Provider containers still need separately injected bootstrap, verifier and
database inputs; they do not use a runtime vault identity.

## Consumer isolation cutover

1. Provision the exact roots below, including empty startup folders where a
   capability is unused. Configure independent read ACLs and no imports or
   reference expressions. Root strings are exact and case-sensitive.
2. Copy provider metadata to the API runtime roots and confidential frontend
   values to the BFF roots. Keep the dedicated lifecycle credential at
   `/runtime/api/keycloak/lifecycle`. Provider database and verifier values
   belong only to resource-specific operator injection, not Event runtime.
3. Rebind stale database references to the registered runtime folder and selected
   environment. A valid logical key does not legitimize an old admin coordinate.
   The retired `keycloak.db_password` binding is not runtime authority.
4. Prepare the independently signed Cerbos deployment receipt through the
   operator-owned publication procedure. Do not restore a runtime publisher,
   human-admin credential, or retired Keycloak initialization script.
5. Drain old processes, start the new consumers, and test cold restart plus
   denied access to sibling/provisioning/infrastructure roots using each actual
   deployed identity. Revoke old grants and rotate old credentials only after
   convergence. A successful login is not evidence that an identity lacks
   broader permissions.

AppHost is trusted to observe injected launch inputs and manage its children.
Its projection does not protect against a compromised orchestrator or OS account.
Development User Secrets shares an account-owned file and is not a production
isolation mechanism. For Infisical-backed local databases, separately inject
bootstrap role credentials and stable `DATABASE_PORT` / `ERASURE_DATABASE_PORT`
values, then provision matching runtime and migrator coordinates in the vault.
AppHost will not substitute generated local credentials for a failed vault read.
This also applies to Global database credentials in both Split and Standalone
local-data profiles: keep them in the selected consumer's vault authority, not
in AppHost's `Database:Global` configuration. AppHost must not need those local
values to construct its resource graph, and supplying them does not authorize
forwarding them to an Infisical-backed child.
Recovery is forward correction and restart; never restore broad folder fallback.

> [!TIP]
> Self-hosted Infisical behind an AAAA record that is not routable will stall .NET connection attempts. The platform forces IPv4 for Infisical calls, but the host must still resolve and answer on IPv4.

---

## Configuration boundary upgrade

Before upgrading:

1. Replace an obsolete `PrivacyErasure:Durability:Mode` selector (including its
   double-underscore form) with an explicit supported `ERASURE_DATABASE_TOPOLOGY`
   value. Preserve the intended authority;
   do not simply remove the old input and accept a default. Startup rejects the
   obsolete input instead of silently choosing a replacement. Consult the
   [privacy-erasure deployment guidance](../security-and-identity/privacy-erasure.md)
   before intentionally changing topology or resetting existing state.
2. Remove database credentials from BFF-readable folders. This includes
   `KEYCLOAK_DB_PASSWORD`, even though its name begins with `KEYCLOAK`.
   Supply such values through the provisioning/container environment only to the
   backend service that owns them. BFF loading now rejects backend/root paths and
   database configuration placed inside frontend folders.
3. Provision BFF values in the dedicated runtime roots below before replacing the
   BFF processes. Reads are nonrecursive and do not expand secret references;
   custom proxies must retain the exact `secretPath`. Missing or mismatched
   provenance, unresolved references and unrelated keys fail startup. Child
   folders are not accepted. Keep the prior deployment available until the
   replacement is verified; changing these paths does not delete legacy values.
4. Keep the same five bootstrap inputs and explicit provider selection. Startup
   and runtime provider binding now use the same validated authority and path set;
   there is no implicit runtime root-folder read.

This repair does not migrate data or switch authority automatically. Back up and
follow the existing reset/restore procedure before any deliberate topology change.
Keep API, BFF and database provisioning credentials separated; never broaden the
BFF identity to work around an upgrade failure.

## 4. Folder Layout

Create the following folders at the root of the selected environment. Folder names are lowercase and exact; key names are `SCREAMING_SNAKE_CASE` and exact.

```
/
├── api
│   ├── bootstrap           (optional: headless administrator bootstrap)
│   ├── operator-identity   (optional: headless operator legal identity)
│   ├── controlplane        (optional: managed SaaS control plane)
│   ├── webpush             (optional: browser Web Push / VAPID)
│   └── ratelimiting        (optional: rate limiting & anti-abuse thresholds)
├── blazor
├── database
│   ├── erasure             (optional: external privacy erasure authority)
│   └── identity            (optional: external local identity store)
├── ai                      (optional: assistant provider)
├── atproto                 (optional: Bluesky / AT Protocol OAuth)
├── cerbos                  (optional: authorization PDP)
├── integrations
│   └── listmonk            (optional: subscriber sync)
├── keycloak                (optional: OIDC identity)
│   └── smtp                (optional: Keycloak account-email delivery)
├── mcp                     (optional: Model Context Protocol)
├── smtp                    (optional: transactional mail)
├── storage                 (optional: S3 / local uploads)
└── stripe                  (optional: paid event ticketing)
```

Additional runtime-only folders (`/setup`, `/webhook`, `/promotions`, `/admissions`, `/ticketing/recovery`, `/analytics`, `/localization`, `/reporting`, `/registration-providers`) are described in [Runtime Secret Bindings](#6-runtime-secret-bindings) and are created only when you enable the matching feature.

### `/api`

Core instance-level platform credentials read by `Explore.API`.

> [!NOTE]
> Modular subfolders under `/api` keep the core folder lean and focused:
> - `/api/bootstrap`: Headless administrator bootstrap (see [Headless & GitOps Provisioning](#headless--gitops-provisioning))
> - `/api/operator-identity`: Legal operator disclosure (see [Headless & GitOps Provisioning](#headless--gitops-provisioning))
> - `/api/controlplane`: Managed SaaS control plane connection (see [/api/controlplane](#apicontrolplane))
> - `/api/webpush`: Browser Web Push notification credentials (see [/api/webpush](#apiwebpush))
> - `/api/ratelimiting`: Anti-abuse and permit limit overrides (see [/api/ratelimiting](#apiratelimiting))

| Key | Purpose |
|---|---|
| `AUTHENTICATION_PROVIDER` | Primary authentication authority: `local`, `keycloak`, or `atproto`. Defaults to `local`. |
| `ATPROTO_LOGIN_ENABLED` | `true` or `false`; enables AT Protocol sign-in. Required `true` when `AUTHENTICATION_PROVIDER=atproto`. |
| `AUTHENTICATION_LOCAL_JWT_KEY` | HMAC signing key for embedded Local Identity access tokens. |
| `AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD` | Consecutive failed Local Identity attempts before lockout (default: `5`). |
| `AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES` | Local Identity lockout duration in minutes (default: `15`). |
| `AUTHORIZATION_PROVIDER` | `local` or `cerbos`. Blank keeps interactive Local-first onboarding. |
| `DEPLOYMENT_MODE` | `SingleTenant` or `MultiTenant`. |
| `VAPID_SUBJECT` | Web Push contact subject (`mailto:` or origin URL). |
| `VAPID_PUBLIC_KEY` | Web Push public key. Intentionally public; served to browsers. |
| `VAPID_PRIVATE_KEY` | Web Push private key. Server-only; never leaves the API process. |
| `WEB_PUSH_ENABLED` | `true` or `false`. Defaults to enabled when all three VAPID values are present. |
| `SETUP_SECRET` | Pre-shared secret that unlocks `/setup`. Leave unset to generate a single-use secret in the volume on first boot. |
| `SETUP_SECRET_REQUIRED` | `true` (default) or `false`; whether the setup surface demands the secret. |

### `/api/controlplane`

Optional configuration when connecting this instance to a central managed SaaS control plane. Both clean keys and legacy `CONTROL_PLANE_*` prefixes are accepted (aliases `/api/control-plane` or `/api/controlplane`).

| Key | Alternative / Legacy | Purpose |
|---|---|---|
| `MANAGED_MODE` | `CONTROL_PLANE_MANAGED_MODE`, `ENABLED` | `true` or `false`; enables managed control-plane mode. |
| `URL` | `CONTROL_PLANE_URL` | Public base URL of the managed control plane. |
| `INSTANCE_ID` | `CONTROL_PLANE_INSTANCE_ID` | Registered instance UUID in the control plane. |
| `REGISTRATION_TOKEN` | `CONTROL_PLANE_REGISTRATION_TOKEN` | Token used for control-plane registration. |
| `REGISTRATION_CREDENTIALS` | `CONTROL_PLANE_REGISTRATION_CREDENTIALS` | Managed control-plane registration credential secret. |
| `MAXIMUM_TENANT_COUNT` | `CONTROL_PLANE_MAXIMUM_TENANT_COUNT` | Maximum allowed tenants under managed tier. |
| `TENANT_ADMINISTRATOR_SIGN_IN_URL` | `CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL` | Hosted sign-in URL for tenant administrators. |

### `/api/webpush`

Optional browser Web Push notification credentials (VAPID). When all three VAPID values are present, Web Push is automatically enabled (aliases `/api/web-push` or `/api/webpush`). Use only the following key names.

| Key | Purpose |
|---|---|
| `WEB_PUSH_ENABLED` | `true` or `false`; explicitly enables or disables Web Push. |
| `VAPID_SUBJECT` | Web Push contact subject (`mailto:` or origin URL). |
| `VAPID_PUBLIC_KEY` | Web Push public key (served to browsers). |
| `VAPID_PRIVATE_KEY` | Web Push private key (server-only; sensitive secret). |

### `/api/ratelimiting`

Optional rate-limiting overrides for DDoS and anti-abuse protection (aliases `/api/rate-limiting` or `/api/ratelimiting`).

| Key | Purpose |
|---|---|
| `ANONYMOUSREGISTRATION_IPPERMITLIMIT` | Per-IP permit limit for anonymous registration (default: `10`). |
| `ANONYMOUSREGISTRATION_SUBNETPERMITLIMIT` | Per-subnet permit limit (default: `40`). |
| `ANONYMOUSREGISTRATION_WINDOWSECONDS` | Rate limit window in seconds (default: `60`). |
| `ANONYMOUSREGISTRATION_CONCURRENCYLIMIT` | Concurrency limit (default: `8`). |
| `ANONYMOUSREGISTRATION_QUEUELIMIT` | Queue limit (default: `0`). |

The build no longer consumes Lucky Penny licensing or edition-selection secrets.
Remove obsolete entries from the selected authority using the
[removed-input checklist](environment-variables.md#removed-edition-inputs).
Do not remove unrelated provider credentials or change the selected secret authority.

### `/blazor`

This legacy folder is not acquired by the BFF. Supply nonsecret `API_ENDPOINT`
and `BFF_ADMIN_HOSTS` through deployment configuration. Provision Google
credentials at `/runtime/bff/oidc/google` instead.

| Key | Purpose |
|---|---|
| `API_ENDPOINT` | Absolute base URL the BFF uses to reach `Explore.API`. |
| `BFF_ADMIN_HOSTS` | Optional comma-separated list of dedicated admin hostnames (e.g. `admin.example.org`). |

### BFF runtime roots

| Exact root | Accepted keys |
|---|---|
| `/runtime/bff/oidc/keycloak` | `KEYCLOAK_ENDPOINT`, `KEYCLOAK_REALM`, `KEYCLOAK_CLIENT_ID`, `KEYCLOAK_BLAZOR_CLIENT_SECRET` |
| `/runtime/bff/oidc/google` | `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` |
| `/runtime/bff/atproto/oauth` | `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` only |

The ATProto root is a separate signing-key consumer, not a third OIDC provider.
Do not place session-encryption or session-JWT signing material in any BFF root.

For Google/Keycloak credential replacement, use a coordinated maintenance
restart. Provision the selected authority, start replacement BFF processes, and
independently verify every expected replica before retiring any old reader.
Set `AUTHENTICATION_CREDENTIAL_PROFILE_REVISION` to a new nonempty UUID identifying
the approved provisioning profile, and inject it into every owning API/BFF process.
Change it whenever that profile changes. It is public deployment metadata, not a
credential and not a vault-returned key. Forward it explicitly through Compose
service overrides. Missing or invalid metadata blocks credential maintenance.
When Environment is selected instead of Infisical, inject the optional
`GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` inputs into the owning BFF process.
Keep the credential out of application settings and provide explicit Compose
service overrides rather than assuming `.env` entries are automatically forwarded.
An authentication metadata refresh reuses the process's acquired credential;
it does not refresh Infisical credential contents.

Each process has a fresh boot identity and each acquired consumer has a random
generation identity. These identify the process and acquisition, not the
credential value. Freeze exact replacement membership only after the processes
have started and been independently associated with the intended deployment.
One healthy replica, successful provider discovery, or an opaque generation
identity cannot prove fleet convergence.

Before any separately authorized legacy cleanup or rotation, verify termination
of every old process and fencing of its writers and sessions. Retain interrupted
cutover history; restore approved external provisioning and restart replacements
when recovery is needed, rather than returning credentials to application
settings or deleting evidence.

The administrator workflow uses
`/api/admin/instance/authentication-credential-cutovers`. Read GET `current`
before writing; an empty current operation returns 204. POST the collection to
approve the exact replacement deployment/replica/boot/consumer/generation
inventory, then POST `activations` for independently checked acquisitions and
POST `fence` for the three verified old-process/writer/session fences. Keep the
returned operation ID and current head/operation stamps. Preparation assigns
the current instance, actor, selected profile and 15-minute deadline server-side;
these are not caller-selected privileges.

Inspect each replacement BFF directly through authenticated GET
`/bff/auth/credential-acquisition-receipts`. Inspection checks fresh API-issued
administrator capability and matching deployment profile. An installed flag
means the process installed that acquisition into its named OIDC scheme, not
that all replicas or provider credentials have been verified. Never treat an
opaque generation as proof of credential contents.

After a lost reply or conflict, reauthorize and read GET `current` or GET
`{operationId}` to recover the retained receipt before deciding the next action.
Do not blindly resubmit stale stamps. `ReadyForCleanup` is evidence readiness,
not completed retirement: counts remain uncommitted until the separately
authorized exact cleanup succeeds. Superseded and expired receipts remain
history, not permission to mutate the replacement deployment.

### `/runtime/api/authentication/keycloak`

Required only when `AUTHENTICATION_PROVIDER=keycloak`.

| Key | Purpose |
|---|---|
| `KEYCLOAK_ENDPOINT` | Public base URL of Keycloak. |
| `KEYCLOAK_INTERNAL_URL` | Optional internal base URL when Keycloak is reached across private container networks. |
| `KEYCLOAK_REALM` | Realm name. |
| `KEYCLOAK_CLIENT_ID` | Public client metadata used for onboarding detection; not the API resource audience. |
| `KEYCLOAK__AUDIENCE` | Scalar API resource audience; defaults to `islamu-event-api`. |
| `KEYCLOAK__PERMITTEDCLIENTS__0` | Optional independent permitted client; additional values use contiguous indices. Omit unused entries. |
| `KEYCLOAK_REQUIRE_HTTPS_METADATA` | `true` (default) or `false`; enforce HTTPS metadata validation for OIDC endpoints. |
`KEYCLOAK_BLAZOR_CLIENT_SECRET` belongs only to `/runtime/bff/oidc/keycloak`.
`KEYCLOAK_DB_DATABASE`, `KEYCLOAK_DB_USERNAME` and `KEYCLOAK_DB_PASSWORD` belong
only to provider resources. The API never receives the BFF confidential secret
through Compose's common Keycloak metadata anchor.

### `/database`

Primary application database. Every key maps into the structured `Database:*` configuration section; the platform never accepts a raw connection string for the primary database.

| Key | Purpose |
|---|---|
| `DATABASE_PROVIDER` | `PostgreSql`, `Sqlite`, `SqlServer`, `MariaDb`, or `MySql`. |
| `DATABASE_HOST` | Database host. |
| `DATABASE_PORT` | Database port. |
| `DATABASE_NAME` | Database name, or the persisted file path for SQLite. |
| `DATABASE_SCHEMA` | Schema namespace (`islamu_event` by default; PostgreSQL and SQL Server). |
| `DATABASE_RUNTIME_USERNAME` | Least-privilege runtime login used by `Explore.API`. |
| `DATABASE_RUNTIME_PASSWORD` | Runtime login password. |
| `DATABASE_GLOBAL_USERNAME` | Independent Global login for API-owned bounded cross-tenant capabilities. |
| `DATABASE_GLOBAL_PASSWORD` | Startup-owned Global login password. |
| `DATABASE_LOCAL_IDENTITY_WRITER_USERNAME` | Dedicated application-side Local Identity workflow login; distinct from Runtime, Global and Migrator. |
| `DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD` | Dedicated workflow password, delivered only to the owning API or combined host; role enrollment needs the username, not this password. |
| `DATABASE_INSTANCE_KEY_READ_USERNAME`, `DATABASE_INSTANCE_KEY_READ_PASSWORD` | Dedicated instance-key metadata login/password. |
| `DATABASE_INSTANCE_KEY_WRITE_USERNAME`, `DATABASE_INSTANCE_KEY_WRITE_PASSWORD` | Dedicated fixed policy/revocation login/password. |
| `DATABASE_INSTANCE_KEY_ISSUANCE_USERNAME`, `DATABASE_INSTANCE_KEY_ISSUANCE_PASSWORD` | Dedicated issuance and metadata-only recovery login/password. |
| `DATABASE_PLATFORM_KEY_REPORT_USERNAME`, `DATABASE_PLATFORM_KEY_REPORT_PASSWORD` | Dedicated historical aggregate-report login/password. |
| `DATABASE_MIGRATOR_USERNAME` | DDL-capable login used only by `Event.MigrationService`. |
| `DATABASE_MIGRATOR_PASSWORD` | Migrator login password. |
| `DATABASE_TLS_MODE` | `Prefer`, `Required`, or `Disabled`. |
| `DATABASE_TRUST_SERVER_CERTIFICATE` | `false` for strict CA verification; `true` only for local self-signed certificates. |
| `DATABASE_SERVER_VERSION` | Optional MariaDB/MySQL version override. |
| `ERASURE_DATABASE_TOPOLOGY` | Privacy erasure topology: `EmbeddedSqlite`, `CoLocated`, or `ExternalDatabase`. |
| `ERASURE_EMBEDDED_PATH` | Persisted SQLite file path when `ERASURE_DATABASE_TOPOLOGY=EmbeddedSqlite` (default: `/app/data/privacy_erasure_authority.db`). |
| `ERASURE_WRITER_REPLICA_COUNT` | Concurrency write limit for embedded SQLite (default: `1`). |
| `ERASURE_BUSY_TIMEOUT_SECONDS` | SQLite busy timeout seconds before retry (default: `30`). |
| `IDENTITY_DATABASE_TOPOLOGY` | Identity database topology: `colocated` or `external`. |

Runtime, Global, and Migrator use distinct logins and connection pools. Global
requires its own username/password; missing values never inherit Runtime,
Migrator, or root database credentials. These `/database` keys map to
`Database:Global:Username` and `Database:Global:Password`; the username is public
configuration and the password is secret. SQLite requires no login credentials.

The four dedicated API-key pairs map to `Database:InstanceKeyRead`,
`Database:InstanceKeyWrite`, `Database:InstanceKeyIssuance` and
`Database:PlatformKeyReport` (`Username`/`Password` in each). They never inherit
another role's credentials. Restrict them to the backend identity, keep the
logins/passwords distinct, and provision only the matching execute-only roles
described in [Environment variables](environment-variables.md#independent-database-credentials).
Do not add them to the BFF's runtime credential roots. Missing native capability
admission blocks the advertised profile; API startup does not install grants.

Migration enrollment also needs the existing read, write and report login
usernames to identify those accounts. In Aspire's Environment authority mode,
the three username declarations reach the migration process as well as API and
Combined hosts. Their structured `Database:<role>:Username` declarations take
precedence over the corresponding flat aliases, including an explicitly empty
structured value: supplying a flat value does not repair that empty declaration.
An explicit Aspire target value is retained when no structured operator value
is declared. This enrollment projection adds no purpose-password forwarding or
BFF access; Infisical consumers keep using their own selected identity.

Global is limited to explicitly granted cross-tenant capabilities, not universal
`BYPASSRLS` access. On PostgreSQL it must remain `NOSUPERUSER NOBYPASSRLS`.
Persisted machine footer link/group PATCH uses the already selected Runtime
credentials and adds no Infisical key or credential pool. Provision only
`event_machine_footer_runtime` to that Runtime login as described in
[database provisioning](environment-variables.md#independent-database-credentials);
do not add it to Global, authentication, scanner or key-management/report
identities. It is a fixed target-fencing operation, not general key access or
footer settings administration.
Room updates similarly add only the distinct `event_machine_room_runtime`
membership to the existing Runtime login when provisioned. They use a
separate room-only function, not the footer executor or a new Infisical
credential. Preserve the same frozen-role separation and never grant either
executor the other purpose's function.
DDL and capability installation belong only to the migration service. Never
give API processes Migrator credentials or expose any database credential to
the Blazor client. Restart consuming API processes after rotating startup-owned
Global credentials so their independent pools use the new login.

> [!WARNING]
> **Do not blindly mirror `ERASURE_DATABASE_TOPOLOGY` and `IDENTITY_DATABASE_TOPOLOGY`.**
> Although both topology settings live side-by-side in `/database`, they address completely different architectural concerns and carry contrasting recommended defaults:
>
> - **`ERASURE_DATABASE_TOPOLOGY` (Recommended default: `EmbeddedSqlite`):** The privacy erasure authority acts as an immutable anti-resurrection ledger for GDPR compliance. It is lightweight and strongly recommended to remain *outside* the primary application database in a dedicated local SQLite file. This ensures that if the primary application database is ever restored from a backup (e.g. taken 24 hours prior), erasures executed *after* that backup was taken are preserved in the independent erasure ledger and immediately replayed, preventing deleted user data from being accidentally resurrected.
> - **`IDENTITY_DATABASE_TOPOLOGY` (Recommended default: `colocated`):** Local Identity credential storage defaults to `colocated` within the primary application database for operational simplicity and transactional consistency in standard single-solution deployments. By contrast, an `external` topology is intended for multi-tenant SaaS providers, ERP suites, or multi-solution organizations (such as the ISLAMU platform ecosystem) that operate a single, shared identity database across all organizational applications.

### `/database/erasure`

Endpoint and credentials for the privacy-erasure authority, used only when `ERASURE_DATABASE_TOPOLOGY=ExternalDatabase`. The provider is fixed to PostgreSQL.

Every key carries the `ERASURE_DATABASE_` prefix. The names are identical whether you store them here or in a flat `.env` file, so the Infisical folder and the Environment authority never diverge.

| Key | Purpose |
|---|---|
| `ERASURE_DATABASE_PROVIDER` | Fixed to `PostgreSql`. |
| `ERASURE_DATABASE_HOST` | Authority host. |
| `ERASURE_DATABASE_PORT` | Authority port (`5432` by default). |
| `ERASURE_DATABASE_NAME` | Authority database name. |
| `ERASURE_DATABASE_RUNTIME_USERNAME` | Function-execution role; no table or sequence access. |
| `ERASURE_DATABASE_RUNTIME_PASSWORD` | Runtime role password. |
| `ERASURE_DATABASE_MIGRATOR_USERNAME` | Schema/lifecycle owner role. |
| `ERASURE_DATABASE_MIGRATOR_PASSWORD` | Migrator role password. |
| `ERASURE_DATABASE_TLS_MODE` | `Prefer`, `Required`, or `Disabled`. |
| `ERASURE_DATABASE_TRUST_SERVER_CERTIFICATE` | `false` for strict CA verification. |

The two usernames must differ. Configuration binding and PostgreSQL provisioning both fail closed when one login is shared across these trust boundaries. `EmbeddedSqlite` and `CoLocated` topologies ignore this folder entirely.

### `/database/identity`

External Local Identity credential store, used only when `IDENTITY_DATABASE_TOPOLOGY=external`.

> [!CAUTION]
> This folder uses the `IDENTITY_DATABASE_*` prefix, **not** the bare `DATABASE_*` names used by `/database`. Each folder is fetched separately without recursion. Name keys exactly as listed so identity and erasure fields cannot become primary database aliases.

| Key | Purpose |
|---|---|
| `IDENTITY_DATABASE_PROVIDER` | `PostgreSql`, `Sqlite`, `SqlServer`, `MariaDb`, or `MySql`. |
| `IDENTITY_DATABASE_HOST` | Credential database host. |
| `IDENTITY_DATABASE_PORT` | Credential database port. |
| `IDENTITY_DATABASE_NAME` | Credential database name or persisted SQLite path. |
| `IDENTITY_DATABASE_SCHEMA` | Provider namespace for Local Identity objects. |
| `IDENTITY_DATABASE_RUNTIME_USERNAME` | Least-privilege runtime credential username. |
| `IDENTITY_DATABASE_RUNTIME_PASSWORD` | Runtime credential password. |
| `IDENTITY_DATABASE_MIGRATOR_USERNAME` | Schema-owner credential username. |
| `IDENTITY_DATABASE_MIGRATOR_PASSWORD` | Schema-owner credential password. |
| `IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME` | Dedicated C# lifecycle writer login; distinct from Runtime and Migrator. |
| `IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD` | Dedicated lifecycle writer password. |
| `IDENTITY_DATABASE_SERVER_VERSION` | Explicit MariaDB/MySQL server version when overriding provider defaults. |
| `IDENTITY_DATABASE_TLS_MODE` | `Prefer`, `Required`, or `Disabled`. |
| `IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE` | `false` for strict CA verification. |

Local Identity tables share the primary application database when colocated (`IDENTITY_DATABASE_TOPOLOGY=colocated`, recommended for single-instance deployments), and connect to this external store when configured with external topology (e.g. when sharing a central identity database across multiple distinct applications or SaaS solutions like the ISLAMU suite).

### `/runtime/api/authorization/cerbos`

Required only when `AUTHORIZATION_PROVIDER=cerbos`.

| Key | Purpose |
|---|---|
| `CERBOS_GRPC_ENDPOINT` | gRPC endpoint of the Policy Decision Point. |
| `CERBOS_HTTP_ENDPOINT` | HTTP endpoint used for the Admin API. |
| `CERBOS_USE_TLS` | `true` when the gRPC channel is TLS-protected. |
| `CERBOS_PLAINTEXT_MODE` | `true` for internal container networks (`h2c`); `false` when TLS is active. |
| `CERBOS_USE_POLICY_SCOPE` | `true` or `false`; enables scoped policy evaluation. |
Cerbos verifier and database inputs are injected only into Cerbos and its
database. Runtime decisions have no Admin API credential. Foreground one-time
administration requires fresh operator input; deployment publication uses the
external signed-receipt procedure. Neither process restores unattended runtime
publication or a broad `/cerbos` credential folder.

### `/mcp`

| Key | Purpose |
|---|---|
| `MCP_ENABLED` | `true` or `false`; exposes the Model Context Protocol endpoint. |
| `MCP_ENDPOINT_PATH` | Endpoint path, `/mcp` by default. |
| `MCP_STATELESS` | `true` or `false`; stateless session handling. |
| `MCP_ENABLE_LEGACY_SSE` | `true` or `false`; legacy Server-Sent Events transport. |

### `/smtp`

Instance mail transport. Supplying these values does **not** enable delivery; the governance setting `email.delivery_enabled` does.

| Key | Purpose |
|---|---|
| `MAIL_SMTP_HOST` | SMTP hostname. |
| `MAIL_SMTP_PORT` | SMTP port. |
| `MAIL_SMTP_USERNAME` | Authentication username. Omit both credentials for a deliberately anonymous relay. |
| `MAIL_SMTP_PASSWORD` | Authentication password. |
| `MAIL_SMTP_FROM_ADDRESS` | Sender address. |
| `MAIL_SMTP_FROM_NAME` | Sender display name. |
| `MAIL_SMTP_ENCRYPTION` | `None`, `StartTls` (default), `SslOnConnect`, or `Auto`. |

### `/storage`

Instance file storage configurations for local disk or cloud S3 providers.

| Key | Purpose |
|---|---|
| `LOCAL_STORAGE_ROOT_PATH` | Filesystem directory used when local storage is selected (default: `/app/storage-data/local`). |
| `LOCAL_STORAGE_CREATE_ROOT_IF_MISSING` | `true` or `false`; automatically create local directory if absent. |
| `STORAGE_S3_ENDPOINT` | S3 API endpoint (AWS, MinIO, Cloudflare R2). |
| `STORAGE_S3_PUBLIC_ENDPOINT` | Optional public-facing endpoint for presented URLs. |
| `STORAGE_S3_BUCKET_NAME` | Bucket dedicated to platform uploads. |
| `STORAGE_S3_ACCESS_KEY_ID` | Access key identifier. |
| `STORAGE_S3_SECRET_ACCESS_KEY` | Secret access key. |
| `STORAGE_S3_REGION` | Region identifier. |
| `STORAGE_S3_FORCE_PATH_STYLE` | `true` for MinIO / self-hosted S3; `false` for AWS S3. |
| `STORAGE_RECONCILIATION_ENABLED` | `true` or `false`; enables background object storage reconciliation. |
| `STORAGE_RECONCILIATION_DRY_RUN` | `true` or `false`; audit only without modifying objects. |
| `STORAGE_RECONCILIATION_QUARANTINE_MISSING_OBJECTS` | `true` or `false`. |
| `STORAGE_RECONCILIATION_QUARANTINE_ORPHAN_LOCAL_FILES` | `true` or `false`. |
| `STORAGE_RECONCILIATION_DELETE_QUARANTINED_OBJECTS` | `true` or `false`. |

### `/integrations/listmonk`

| Key | Purpose |
|---|---|
| `LISTMONK_ENABLED` | `true` or `false`; enables subscriber synchronization. |
| `LISTMONK_INSTANCE_URL` | Base URL of the external Listmonk instance. |
| `LISTMONK_DEFAULT_LIST_ID` | Default mailing list identifier. |
| `LISTMONK_API_USERNAME` | Listmonk API username. |
| `LISTMONK_API_KEY` | Listmonk API token. |
| `LISTMONK_PRECONFIRM_SUBSCRIPTIONS` | `true` or `false`; marks synced subscribers confirmed. |
| `LISTMONK_SYNC_ON_REGISTRATION` | `true` or `false`; synchronizes on event registration. |

### `/stripe`

Instance-scoped, server-only, and optional while paid events are disabled.

| Key | Purpose |
|---|---|
| `PAYMENTS_STRIPE_MODE` | Mode selection: `Test` or `Live`. |
| `STRIPE_PLATFORM_SECRET_KEY` | Platform secret key. `Test` mode requires an `sk_test_` prefix; `Live` requires `sk_live_`. |
| `STRIPE_WEBHOOK_SECRET` | Endpoint signing secret. The Connect endpoint uses only this binding. |
| `PAYMENTS_ORGANIZER_DIRECT_PROVIDER_CODE` | Provider code for organizer-direct ticketing checkouts. |
| `PAYMENTS_ORGANIZER_DIRECT_CONNECT_PLATFORM_ID` | Connect platform client ID. |
| `PAYMENTS__CHECKOUTGOVERNANCE__COMPLAINTOWNER` | Governance owner for checkout complaints (`Platform` or `Organizer`). |
| `PAYMENTS__CHECKOUTGOVERNANCE__REFUNDOWNER` | Governance owner for checkout refunds (`Platform` or `Organizer`). |
| `PAYMENTS__CHECKOUTGOVERNANCE__DISPUTEOWNER` | Governance owner for checkout disputes (`Platform` or `Organizer`). |
| `PAYMENTS__CHECKOUTGOVERNANCE__RECONCILIATIONOWNER` | Governance owner for checkout reconciliations (`Platform` or `Organizer`). |
| `PAYMENTS__CHECKOUTGOVERNANCE__ACTIVATIONSTATUS` | Payment subsystem activation status (`approved` or `suspended`; defaults to `suspended`). |
| `PAYMENTS__CHECKOUTGOVERNANCE__REFUNDPOLICYLANGUAGETAG` | Default language tag for refund policy notices. |
| `PAYMENTS__CHECKOUTGOVERNANCE__STATEMENTDESCRIPTOR` | Card statement descriptor prefix (≤22 chars). |
| `PAYMENTS__CHECKOUTGOVERNANCE__CHARGETYPE` | Checkout charge type: fixed to `direct-charge` (`OrganizerDirect` zero-custody; destination charges are rejected). |

### `/atproto`

Required only when AT Protocol login is enabled.

| Key | Purpose |
|---|---|
| `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | BFF value belongs at `/runtime/bff/atproto/oauth`, not this backend folder. |
| `ATPROTO_SESSION_ENCRYPTION_KEYRING` | Session-envelope encryption keyring. |
| `ATPROTO_SESSION_JWT_PRIVATE_JWKS` | First-party session JWT signing key set. |

### `/ai`

Optional. Configures the assistant provider.

| Key | Purpose |
|---|---|
| `AI_PROVIDER` | Provider selection: `NONE`, `FAKE`, `OPENAI_COMPATIBLE`, `ANTHROPIC_COMPATIBLE`, `OPENAI`, `AZURE_OPENAI`, or `ANTHROPIC`. |
| `AI_ENDPOINT` | Base URL for compatible/self-hosted providers. |
| `AI_MODEL_ID` | Model identifier passed to the provider. |
| `AI_CREDENTIAL_SCOPE` | Deployment reference selection: `none` or `instance`; no credential value. |
| `AI_TOOL_PROPOSALS_ENABLED` | `true` or `false`; enables assistant tool proposals. |
| `AI_OPENAI_API_KEY` | Registry-bound official OpenAI credential under `/ai`. |
| `AI_ANTHROPIC_API_KEY` | Registry-bound official Anthropic credential under `/ai`. |
| `AI_AZURE_API_KEY` | Separate instance Azure OpenAI API-key reference under `/ai`. |
| `AI_OPENAI_COMPATIBLE_API_KEY` | Separate authenticated OpenAI-compatible provider reference under `/ai`. |
| `AI_ANTHROPIC_COMPATIBLE_API_KEY` | Separate authenticated Anthropic-compatible provider reference under `/ai`. |

`OPENAI` and `ANTHROPIC` need a model and a successfully resolved instance
reference. When credential scope is omitted, official API-key providers select
`instance`; compatible providers select `none`. Compatible endpoints need an
endpoint and model. Authenticated compatible calls select their separate
provider reference, never an official credential.
Only explicit `none` permits unauthenticated compatible calls. Azure API-key
mode uses its separate reference; managed identity uses `none` and the configured
Azure identity.

Admin and tenant policy contain reference selection, never credential values.
Tenant credentials use exact tenant bindings and cannot inherit an instance
credential. A tenant-selected endpoint cannot acquire an instance key. Setup
Live handles admitted unqualified instance bindings, including the separate
compatible-provider references. Tenant binding provisioning remains separate;
do not submit a secret to ordinary settings PATCH.

`AI_API_KEY` and `ai_assistant.api_key` are retired without copying their values
to another authority. Coordinate old-writer retirement before exact live-row
cleanup. Review historical audits, notifications and backups separately; source
retirement does not prove that retained credential copies were removed.

---

## Headless & GitOps Provisioning

> [!TIP]
> **Interactive Web Wizard vs. Headless Infisical Configuration**
> If you are setting up ISLAMU Event through the browser (the standard self-hosting path), you **do not** need to create `/api/bootstrap` or `/api/operator-identity`. When you visit the web interface on first launch, you will configure your administrator account and organization/legal identity directly in the interactive setup wizard.
>
> The folders below are strictly for **automated, zero-touch headless deployments** (`INSTANCE_BOOTSTRAP_MODE=ConfiguredAdministrator`) or GitOps pipelines where no human interacts with the browser wizard.

### `/api/bootstrap`

Configures the headless administrator account when `INSTANCE_BOOTSTRAP_MODE=ConfiguredAdministrator`.

| Key | Purpose |
|---|---|
| `MODE` | Headless bootstrap mode: `ConfiguredAdministrator`. |
| `ADMIN_PROVIDER` | Headless bootstrap provider: `local`, `keycloak`, or `atproto`. |
| `ADMIN_SUBJECT` | Headless bootstrap subject: lowercase, hyphenated UUIDv7 also used as the Local username, Keycloak `sub`, or ATProto DID. |
| `BINDING_GENERATION` | Headless bootstrap generation counter (positive integer). |
| `ADMIN_EMAIL` | Optional administrator account email. |
| `ADMIN_FIRST_NAME` | Optional administrator first name. |
| `ADMIN_LAST_NAME` | Optional administrator last name. |
| `LOCAL_PASSWORD` | Initial password for headless Local administrator bootstrap. |

### `/api/operator-identity`

Configures legal and branding metadata for the deploying entity in headless deployments.

| Key | Purpose |
|---|---|
| `OPERATOR_ID` | UUIDv7 unique identifier for the operating legal entity. |
| `PUBLIC_NAME` | Public brand name of the deploying organization. |
| `LEGAL_NAME` | Full legal registered entity name. |
| `IS_OFFICIAL_INSTANCE` | `true` only for the authoritative upstream project deployment. |
| `OFFICIAL_ORIGIN` | Authoritative origin URL for official instances. |
| `OPERATOR_KIND_CODE` | Operator-kind code (e.g., `community`). |
| `JURISDICTION_COUNTRY_CODE` | Two-letter jurisdiction country code (`US`, `GB`, `FR`, etc.). |
| `PUBLIC_CONTACT_EMAIL` | Public contact email for legal and privacy inquiries. |
| `WEBSITE_URL` | Public website URL of the operating organization. |
| `LEGAL_NOTICE_URL` | Public URL for legal notice / imprint. |
| `TERMS_URL` | Public URL for Terms of Service. |
| `PRIVACY_URL` | Public URL for Privacy Policy. |
| `REGISTRATION_IDENTIFIER` | Optional company or charity registration number. |

> [!NOTE]
> For backwards compatibility, legacy flat keys (`INSTANCE_BOOTSTRAP_*` and `INSTANCE__OPERATORIDENTITY__*`) placed in `/api` remain supported and map to the same configuration models.

---

## 5. Which Service Reads Which Folder

Each host reads a bounded folder list at startup. A key placed outside the folders its consumer reads will not be found.

| Host | Folders read at startup |
|---|---|
| `Explore.API` | `/runtime/api/authentication/keycloak`, `/runtime/api/keycloak/lifecycle`, `/runtime/api/authorization/cerbos`; explicit database/API/service roots described below |
| `Explore.Blazor` (BFF) | Exact `/runtime/bff/oidc/keycloak`, `/runtime/bff/oidc/google`, `/runtime/bff/atproto/oauth`; no recursion or reference expansion |
| `Event.Standalone` | Exact API+BFF union; no provisioning or infrastructure roots |
| `Explore.AppHost` (Aspire) | None. It projects independently supplied launch identities and resource-specific operator inputs. |
| Migration and design-time factories | `/database`, `/database/erasure`, `/database/identity` |

All reads are exact, nonrecursive and reference-unexpanded. Imports are disabled.
The API also admits `/database`, `/database/erasure`, `/database/identity`,
`/postgresql`, `/api`, `/api/operator-identity`, `/api/operatoridentity`,
`/api/bootstrap`, `/api/instance-bootstrap`, `/api/instancebootstrap`,
`/api/controlplane`, `/api/control-plane`, `/api/webpush`, `/api/web-push`,
`/api/ratelimiting`, `/api/rate-limiting`, `/mcp`, `/ai`, `/storage`, `/smtp`,
`/stripe`, `/integrations/listmonk`, `/setup`, `/promotions`, `/admissions`,
`/ticketing/recovery`, `/atproto`, `/webhook`, `/messaging`, `/reporting`,
`/registration-providers`, `/analytics` and `/localization`. Existing alternate
API subfolder spellings are independent exact roots, not recursive grants;
do not put conflicting copies of a configuration key into them.
Data Protection uses its separate partition loader and process pin/floor, not
this bulk folder list.

---

## 6. Runtime Secret Bindings

Beyond startup bootstrap, individual credentials are resolved on demand through
`SecretBinding` records. Their folder must equal the registered runtime folder
and their environment must match the selected authority before any provider
request or cache use. There is no project-tree scan, recursive discovery or
old-folder fallback. Tenant-qualified keys remain supported inside their
registered folder.

For split deployments using Environment authority, supply the instance and
tenant email-policy database credential pairs already listed in `.env.example`:
`DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_USERNAME` / `_PASSWORD` and
`DATABASE_EMAIL_POLICY_TENANT_MUTATION_USERNAME` / `_PASSWORD`. Compose passes
these pairs only to the API. Provision the corresponding limited database roles;
ordinary Runtime or Migrator credentials are not substitutes for these profiles.

Aspire forwards these existing pairs to API and Combined hosts when Environment
or permitted User Secrets is selected. With Infisical, the API acquires them
through its own selected `/database` authority. Structured
`Database:EmailPolicyInstanceMutation:*` and
`Database:EmailPolicyTenantMutation:*` values take precedence over the flat
aliases, including an explicitly empty value. Missing selected credentials do
not inherit lower-priority values; whitespace-only declarations also remain
unavailable. These startup declarations are captured as a snapshot. After
changing Environment or permitted User Secrets credentials projected by Aspire,
restart the AppHost and its managed applications. For direct Infisical
acquisition, restart the owning API or Combined host. Keep both purpose passwords
out of the migration worker and BFF configuration.

The enrolled setup writer has two dedicated instance binding purposes:
`setup.signing` and `setup.encryption`, with default names `SETUP_SIGNING` and
`SETUP_ENCRYPTION` in `/setup`. These optional targets accept Infisical bindings
only; Environment and Development User Secrets do not provide a write backend.
Keep them separate from `SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY`, which protects
write commitments and is never a setup write target. The BFF cannot acquire
`/setup`; no additional consumer identity or folder grant is needed.

| Folder | Keys |
|---|---|
| `/setup` | `SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY`, `SETUP_SIGNING`, `SETUP_ENCRYPTION` |
| `/webhook` | `WEBHOOKS_ENABLED`, `WEBHOOKS_PROVIDER`, `WEBHOOKS_SVIX_BASE_URL`, `WEBHOOKS_SVIX_AUTH_TOKEN`, `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET`, `WEBHOOKS_SVIX_ENVIRONMENT`, `WEBHOOKS_SVIX_PROVIDER_VERSION`, `WEBHOOKS_SVIX_CAPABILITY_POLICY_VERSION`, `SVIX_SERVER_URL`, `SVIX_AUTH_TOKEN`, `SVIX_QUEUE_TYPE`, `SVIX_CACHE_TYPE`, `SVIX_REDIS_DSN`, `SVIX_JWT_SECRET` |
| `/promotions` | `PROMOTIONS_CODE_LOOKUP_HMAC_KEY` (each binding uses a `v{version}` qualifier), `PROMOTIONS_CODE_LOOKUP_ACTIVE_KEY_VERSION` |
| `/admissions` | `ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY`, `ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY`, `ADMISSIONS_RECOVERY_CAPABILITY_HMAC_KEY`, `ADMISSIONS__CREDENTIALLOOKUP__ACTIVEKEYVERSION`, `ADMISSIONS__RECOVERY__ACTIVEKEYVERSION`, `ADMISSIONS__RECOVERY__CAPABILITYLIFETIMEMINUTES`, `ADMISSIONS__RECOVERY__RATELIMITBUCKETCOUNT`, `ADMISSIONS__RECOVERY__RATELIMITPERMITCOUNT`, `ADMISSIONS__RECOVERY__RATELIMITWINDOWSECONDS` |
| `/ticketing/recovery` | `TICKETING_RECOVERY_MANIFEST_HMAC_KEY`, `TICKETING__RECOVERY__ENABLED`, `TICKETING__RECOVERY__EXPECTEDRELEASEREVISION`, `TICKETING__RECOVERY__EXPECTEDSCHEMAREVISION`, `TICKETING__RECOVERY__MINIMUMRETAINEDKEYVERSION`, `TICKETING__RECOVERY__MINIMUMAUTHORITYFLOOR`, `TICKETING__RECOVERY__MINIMUMPROVIDERCURSOR`, `TICKETING__RECOVERY__MINIMUMIDEMPOTENCYFLOOR`, `TICKETING__RECOVERY__MINIMUMWORKERFENCE`, `TICKETING__RECOVERY__WARNINGOLDESTDUESECONDS`, `TICKETING__RECOVERY__UNHEALTHYOLDESTDUESECONDS`, `TICKETING__RECOVERY__BACKLOGTHRESHOLD`, `TICKETING__RECOVERY__DECLAREDRPOMINUTES`, `TICKETING__RECOVERY__DECLAREDRTOMINUTES`, `TICKETING__RECOVERY__MANIFESTSIGNINGKEYREFERENCE` |
| `/messaging` | `MESSAGING_URI`, `EMAIL_DISPATCH_RABBITMQ_ENABLED`, `EMAIL_DISPATCH_RABBITMQ_CONNECTION_STRING`, `EMAIL_DISPATCH_RABBITMQ_EXCHANGE_NAME`, `EMAIL_DISPATCH_RABBITMQ_DISPATCH_QUEUE_NAME`, `EMAIL_DISPATCH_RABBITMQ_DISPATCH_ROUTING_KEY`, `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_EXCHANGE_NAME`, `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_QUEUE_NAME`, `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_ROUTING_KEY`, `EMAIL_DISPATCH_RABBITMQ_PARKING_QUEUE_NAME`, `EMAIL_DISPATCH_RABBITMQ_PARKING_ROUTING_KEY`, `EMAIL_DISPATCH_RABBITMQ_CLIENT_PROVIDED_NAME`, `EMAIL_DISPATCH_RABBITMQ_CONSUMER_ID`, `EMAIL_DISPATCH_RABBITMQ_PREFETCH_COUNT`, `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_ENABLED`, `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_CONSUMER_ID`, `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_PREFETCH_COUNT`, `EMAIL_DISPATCH_RABBITMQ_PUBLISH_TIMEOUT_SECONDS`, `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_POLLING_INTERVAL_SECONDS`, `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_BATCH_SIZE`, `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_RETRY_DELAY_SECONDS` |
| `/analytics` | `ANALYTICS_POSTHOG_PUBLIC_KEY`, `ANALYTICS_POSTHOG_HOST`, `ANALYTICS_PERSONAL_API_KEY` |
| `/localization` | `LOCALIZATION_TMS_API_KEY` |
| `/registration-providers` | `REGISTRATION_PROVIDER_API_TOKEN`, `REGISTRATION_PROVIDER_WEBHOOK_SECRET` |

Registration-provider credentials are tenant-scoped. When several tenant connections need distinct tokens for the same key, distinguish them with the binding's `Qualifier` field rather than by inventing new key names.

### Auxiliary Compose Profiles & Integration Extensions

Deployments running optional Compose profiles (`--profile formbricks`, `--profile localization`, `--profile moderation`) can store their container and operational secrets in Infisical or via direct environment injection:

#### Formbricks (`--profile formbricks`)

| Key | Purpose |
|---|---|
| `FORMBRICKS_DATABASE_NAME` | Database name for Formbricks state (default: `formbricks`). |
| `FORMBRICKS_DATABASE_USER` | Database username for Formbricks container. |
| `FORMBRICKS_DATABASE_PASSWORD` | Database password for Formbricks container. |
| `FORMBRICKS_NEXTAUTH_SECRET` | NextAuth encryption secret (`openssl rand -hex 32`). |
| `FORMBRICKS_ENCRYPTION_KEY` | Formbricks data encryption key (`openssl rand -hex 32`). |
| `FORMBRICKS_CRON_SECRET` | Internal cron secret for periodic survey triggers. |
| `FORMBRICKS_HUB_API_KEY` | Formbricks hub API key. |
| `FORMBRICKS_CUBEJS_API_SECRET` | CubeJS analytics API secret. |
| `FORMBRICKS_WEBAPP_URL` | Public web application URL for Formbricks app. |

For Aspire's `local-full` profile, supply all six secret values before starting:
`FORMBRICKS_NEXTAUTH_SECRET`, `FORMBRICKS_ENCRYPTION_KEY`,
`FORMBRICKS_CRON_SECRET`, `FORMBRICKS_HUB_API_KEY`,
`FORMBRICKS_CUBEJS_API_SECRET`, and `FORMBRICKS_DATABASE_PASSWORD`.
Aspire reads them from the explicitly selected secret authority and fails
configuration when a required value is absent. It does not generate substitute
values. Keep the selected authority and its values stable across restarts.

The Formbricks database, Cube and application database URLs share the selected
database password. Generated URLs escape reserved characters; the database
receives the original value. Store the password as its original value in the
selected authority rather than pre-encoding it.

#### Weblate (`--profile localization`)

| Key | Purpose |
|---|---|
| `WEBLATE_SITE_DOMAIN` | Domain name for Weblate interface. |
| `WEBLATE_ADMIN_NAME` | Initial Weblate administrator username. |
| `WEBLATE_ADMIN_EMAIL` | Initial Weblate administrator email. |
| `WEBLATE_ADMIN_PASSWORD` | Initial Weblate administrator password. |
| `WEBLATE_POSTGRES_USER` | Database user for Weblate container. |
| `WEBLATE_POSTGRES_PASSWORD` | Database password for Weblate container. |
| `WEBLATE_POSTGRES_DB` | Database name for Weblate container. |

#### External Moderation (`--profile moderation`: Coop & Osprey)

| Key | Purpose |
|---|---|
| `REPORTING_MODE` | Moderation mode: `LocalOnly` (built-in), `Coop`, `Osprey`, or `Composite`. |
| `REPORTING_COOP_ENDPOINT_URL` | Endpoint URL of Coop moderation server. |
| `REPORTING_COOP_API_KEY` | API key for Coop authentication. |
| `REPORTING_COOP_WEBHOOK_SECRET` | Secret for signing Coop incoming webhooks. |
| `REPORTING_OSPREY_ENDPOINT_URL` | Endpoint URL of Osprey coordinator. |
| `REPORTING_OSPREY_API_KEY` | API key for Osprey coordinator. |

> [!NOTE]
> `SVIX_CONFORMANCE_MANAGED_*` variables belong to the managed-webhook conformance test harness, not to a running instance. Do not store them in Infisical; supply them to the test environment when running that suite.

---

## 7. Verify the Configuration

1. Start `event-api` and confirm it reaches a healthy state rather than failing closed at startup.
2. Check `/health` and confirm the secret provider reports healthy. Provider states are `Unconfigured`, `Unavailable`, `Unauthorized`, and `Invalid`; each names a distinct fault.
   - `Unauthorized` — the machine identity lacks read access to a requested folder, or the Universal Auth credentials are wrong.
   - `Unavailable` — `INFISICAL_URL` is unreachable, frequently an IPv6-only DNS answer or a firewall rule.
   - `Invalid` — authentication succeeded but returned no usable access token.
3. Confirm the values you expect actually arrived by exercising the capability: sign in for Keycloak, send a test message for SMTP, upload an image for S3.

Secrets refresh periodically after startup (five-minute polling with exponential backoff and jitter by default). A failed refresh keeps the last-known-good configuration and reports the failure rather than dropping to an empty value.

---

## 8. Rotation

Treat rotations as rolling restarts. Most credentials rebind only on process reload.

1. Write the replacement value into the same Infisical path and key.
2. For database credentials, grant the new password concurrently before rotating.
3. Restart or redeploy `event-api` and `event-ui`.
4. Verify `/health` reports every service healthy.
5. Revoke the retired credential in Infisical.

`SETUP_SECRET` and `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` do not support live rotation. HMAC key material under `/promotions`, `/admissions`, `/ticketing/recovery`, and `/atproto` uses overlap rollout: publish the new version alongside the old, confirm every replica acknowledges it, and only then revoke.

---

## Related Guides & Next Steps

* **[Secrets Management](secrets.md)** — Authority selection, prohibited locations, and fail-closed semantics.
* **[Environment Variables](environment-variables.md)** — Full catalogue of every variable, its default, and its restart behavior.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Inject the `INFISICAL_*` bootstrap inputs into a split-container stack.
* **[Authentication](../security-and-identity/authentication.md)** — Bind Keycloak and AT Protocol credentials.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Diagnose secret provider startup failures.
