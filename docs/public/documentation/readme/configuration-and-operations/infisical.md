---
description: Create the Infisical project, folder tree, and machine identity that ISLAMU Event reads.
---

# Infisical Setup

This guide is the exact folder-and-key layout ISLAMU Event expects inside Infisical. Create the structure once, point the deployment at it with the five `INFISICAL_*` bootstrap inputs, and every service resolves its credentials from that single authority.

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

1. Create a Machine Identity in the organization.
2. Attach a **Universal Auth** authentication method and generate a Client ID and Client Secret.
3. Grant each service identity **read** access only to its required folders. The browser-facing BFF identity must be limited to `/keycloak`, `/blazor` and `/atproto` frontend values, not `/`, `/database` or `/api`. Use separate backend/provisioning identities for database and container credentials. Write access belongs only to the backend identity that performs an enabled Setup secret-write capability.
4. Copy the project UUID from the project settings page.

These four values plus the environment slug are *secret zero*: they live in the deployment environment, never inside Infisical itself.

---

## 3. Point the Deployment at Infisical

Set these in `.env` (or your orchestrator's environment injection) and leave every other credential blank:

```bash
SECRET_PROVIDER=Infisical
INFISICAL_URL=https://app.infisical.com   # or your self-hosted base URL
INFISICAL_PROJECT_ID=<project UUID>
INFISICAL_CLIENT_ID=<universal auth client id>
INFISICAL_CLIENT_SECRET=<universal auth client secret>
INFISICAL_ENV=prod
```

When `SECRET_PROVIDER=Infisical`, only Infisical results are authoritative. The platform never silently falls back to environment variables or User Secrets for a value the authority failed to supply; startup fails closed instead.

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
3. Retain the actual `secretPath` in recursive Infisical responses. Custom proxies
   must not strip it: missing or out-of-scope provenance fails startup. Child
   folders keep their own namespace and cannot overwrite parent database values.
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

Read by the `Explore.Blazor` BFF.

| Key | Purpose |
|---|---|
| `API_ENDPOINT` | Absolute base URL the BFF uses to reach `Explore.API`. |
| `BFF_ADMIN_HOSTS` | Optional comma-separated list of dedicated admin hostnames (e.g. `admin.example.org`). |
| `GOOGLE_CLIENT_ID` | Optional Google SSO client identifier. |
| `GOOGLE_CLIENT_SECRET` | Optional Google SSO client secret. |

### `/keycloak`

Required only when `AUTHENTICATION_PROVIDER=keycloak`.

| Key | Purpose |
|---|---|
| `KEYCLOAK_ENDPOINT` | Public base URL of Keycloak. |
| `KEYCLOAK_INTERNAL_URL` | Optional internal base URL when Keycloak is reached across private container networks. |
| `KEYCLOAK_REALM` | Realm name. |
| `KEYCLOAK_CLIENT_ID` | Browser/BFF client metadata used for onboarding detection (alias: `KEYCLOAK_BLAZOR_CLIENT_ID`). |
| `KEYCLOAK_BLAZOR_CLIENT_SECRET` | Confidential client secret for the Blazor BFF. |
| `KEYCLOAK_REQUIRE_HTTPS_METADATA` | `true` (default) or `false`; enforce HTTPS metadata validation for OIDC endpoints. |
`KEYCLOAK_DB_DATABASE`, `KEYCLOAK_DB_USERNAME`, and `KEYCLOAK_DB_PASSWORD` are backend provisioning inputs, not BFF-readable `/keycloak` entries. Inject them only into their owning container/provisioning environment; database keys in frontend folders fail closed.

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

Runtime and migrator logins must be distinct. Never give runtime services the migrator role, and never expose either to the Blazor client.

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
> This folder uses the `IDENTITY_DATABASE_*` prefix, **not** the bare `DATABASE_*` names used by `/database`. The platform reads `IDENTITY_DATABASE_HOST`; a key named `DATABASE_HOST` placed here is not read as an identity setting, and because `/database` is fetched recursively it can collide with the primary database host. Name these keys exactly as listed. The same rule is why `/database/erasure` uses `ERASURE_DATABASE_*`.

| Key | Purpose |
|---|---|
| `IDENTITY_DATABASE_PROVIDER` | `PostgreSql`, `Sqlite`, `SqlServer`, `MariaDb`, or `MySql`. |
| `IDENTITY_DATABASE_CONNECTION_STRING` | Optional complete operator-managed connection string. Prefer the discrete fields below. |
| `IDENTITY_DATABASE_HOST` | Credential database host. |
| `IDENTITY_DATABASE_PORT` | Credential database port. |
| `IDENTITY_DATABASE_NAME` | Credential database name or persisted SQLite path. |
| `IDENTITY_DATABASE_SCHEMA` | Provider namespace for Local Identity objects. |
| `IDENTITY_DATABASE_RUNTIME_USERNAME` | Least-privilege runtime credential username. |
| `IDENTITY_DATABASE_RUNTIME_PASSWORD` | Runtime credential password. |
| `IDENTITY_DATABASE_MIGRATOR_USERNAME` | Schema-owner credential username. |
| `IDENTITY_DATABASE_MIGRATOR_PASSWORD` | Schema-owner credential password. |
| `IDENTITY_DATABASE_TLS_MODE` | `Prefer`, `Required`, or `Disabled`. |
| `IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE` | `false` for strict CA verification. |

Local Identity tables share the primary application database when colocated (`IDENTITY_DATABASE_TOPOLOGY=colocated`, recommended for single-instance deployments), and connect to this external store when configured with external topology (e.g. when sharing a central identity database across multiple distinct applications or SaaS solutions like the ISLAMU suite).

### `/cerbos`

Required only when `AUTHORIZATION_PROVIDER=cerbos`.

| Key | Purpose |
|---|---|
| `CERBOS_GRPC_ENDPOINT` | gRPC endpoint of the Policy Decision Point. |
| `CERBOS_HTTP_ENDPOINT` | HTTP endpoint used for the Admin API. |
| `CERBOS_USE_TLS` | `true` when the gRPC channel is TLS-protected. |
| `CERBOS_PLAINTEXT_MODE` | `true` for internal container networks (`h2c`); `false` when TLS is active. |
| `CERBOS_USE_POLICY_SCOPE` | `true` or `false`; enables scoped policy evaluation. |
| `CERBOS_ADMIN_USERNAME` | Admin API username used by server-side policy package sync. |
| `CERBOS_ADMIN_PASSWORD` | Admin API password used by server-side policy package sync. |
| `CERBOS_ADMIN_PASSWORD_HASH` | The verifier the Cerbos server itself is configured with. |
| `CERBOS_PG_URL` | PostgreSQL connection string for Cerbos storage backend. |
| `CERBOS_POSTGRES_USER` | Database username for Cerbos PostgreSQL container. |
| `CERBOS_POSTGRES_PASSWORD` | Database password for Cerbos PostgreSQL container. |
| `CERBOS_POSTGRES_DB` | Database name for Cerbos PostgreSQL container. |

The API resolves the Admin API credentials directly from the selected secret authority during policy sync; the browser receives only configured/ownership metadata. If the API restarts during unfinished onboarding, a protected onboarding page returns to `/setup` so the operator can re-enter the setup secret and resume the original page.

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
| `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | OAuth client private key set used by the BFF. |
| `ATPROTO_SESSION_ENCRYPTION_KEYRING` | Session-envelope encryption keyring. |
| `ATPROTO_SESSION_JWT_PRIVATE_JWKS` | First-party session JWT signing key set. |

### `/ai`

Optional. Configures the assistant provider.

| Key | Purpose |
|---|---|
| `AI_PROVIDER` | Provider selection: `NONE`, `FAKE`, `OPENAI_COMPATIBLE`, `ANTHROPIC_COMPATIBLE`, `OPENAI`, `AZURE_OPENAI`, or `ANTHROPIC`. |
| `AI_ENDPOINT` | Base URL for compatible/self-hosted providers. |
| `AI_MODEL_ID` | Model identifier passed to the provider. |
| `AI_API_KEY` | Provider API credential. |
| `AI_TOOL_PROPOSALS_ENABLED` | `true` or `false`; enables assistant tool proposals. |
| `AI_OPENAI_API_KEY` | Registry-bound OpenAI credential for tenant-scoped bindings. |
| `AI_ANTHROPIC_API_KEY` | Registry-bound Anthropic credential for tenant-scoped bindings. |

`OPENAI` and `ANTHROPIC` need `AI_API_KEY` plus `AI_MODEL_ID`. Every other provider needs `AI_ENDPOINT` plus `AI_MODEL_ID`.

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
| `ADMIN_SUBJECT` | Headless bootstrap subject: canonical UUIDv7 username, Keycloak `sub`, or ATProto DID. |
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
| `IS_OFFICIAL_INSTANCE` | `true` only for the canonical upstream project deployment. |
| `OFFICIAL_ORIGIN` | Canonical origin URL for official instances. |
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
| `Explore.API` | `/keycloak`, `/database`, `/database/erasure`, `/database/identity`, `/api` (including subfolders `/api/controlplane`, `/api/operator-identity`, `/api/bootstrap`, `/api/webpush`, `/api/ratelimiting`), `/cerbos`, `/mcp`, `/ai`, `/storage`, `/smtp`, `/stripe`, `/integrations/listmonk` |
| `Explore.Blazor` (BFF) | `/keycloak`, `/blazor`, `/atproto` |
| `Explore.AppHost` (Aspire) | `/keycloak`, `/database`, `/database/erasure`, `/api`, `/blazor`, `/cerbos`, `/mcp`, `/ai`, `/storage`, `/smtp`, `/stripe`, `/integrations/listmonk` |
| Migration and design-time factories | `/database`, `/database/erasure`, `/database/identity` |

Folder reads are recursive, so a request for `/database` also returns the secrets stored under `/database/erasure` and `/database/identity`, and a request for `/api` also returns its subfolders (`/api/controlplane`, `/api/operator-identity`, `/api/bootstrap`, `/api/webpush`, `/api/ratelimiting`). This is why subfolder key names must stay distinct from their parent's key names.

---

## 6. Runtime Secret Bindings

Beyond startup bootstrap, individual credentials are resolved on demand through `SecretBinding` records that name an Infisical path and key. The runtime resolver scans the project tree, so these folders do not need to appear in any startup list. Create a folder only when you enable the corresponding capability.

| Folder | Keys |
|---|---|
| `/setup` | `SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY` |
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
