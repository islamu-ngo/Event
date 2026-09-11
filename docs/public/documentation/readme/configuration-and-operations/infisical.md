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
3. Add the identity to the `ISLAMU Event` project with **read** access to every folder in the tree below. Write access is required only if you use the in-app Setup secret-write capability.
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

## 4. Folder Layout

Create the following folders at the root of the selected environment. Folder names are lowercase and exact; key names are `SCREAMING_SNAKE_CASE` and exact.

```
/
├── ai
├── api
├── atproto
├── blazor
├── cerbos
├── database
│   ├── erasure
│   └── identity
├── integrations
│   └── listmonk
├── mcp
├── smtp
├── storage
└── stripe
```

Additional runtime-only folders (`/setup`, `/webhook`, `/promotions`, `/admissions`, `/ticketing/recovery`, `/analytics`, `/localization`, `/reporting`, `/registration-providers`) are described in [Runtime Secret Bindings](#6-runtime-secret-bindings) and are created only when you enable the matching feature.

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

### `/api`

Instance-level platform credentials read by `Explore.API`.

| Key | Purpose |
|---|---|
| `SETUP_SECRET` | Pre-shared secret that unlocks `/setup`. Leave unset to generate a single-use secret in the volume on first boot. |
| `SETUP_SECRET_REQUIRED` | `true` (default) or `false`; whether the setup surface demands the secret. |
| `AUTHENTICATION_LOCAL_JWT_KEY` | HMAC signing key for embedded Local Identity access tokens. |
| `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` | Initial password for headless Local administrator bootstrap. |
| `CONTROL_PLANE_REGISTRATION_CREDENTIALS` | Managed control-plane registration credential. |
| `AUTHORIZATION_PROVIDER` | `local` or `cerbos`. Blank keeps interactive Local-first onboarding. |
| `DEPLOYMENT_MODE` | `SingleTenant` or `MultiTenant`. |
| `VAPID_SUBJECT` | Web Push contact subject (`mailto:` or origin URL). |
| `VAPID_PUBLIC_KEY` | Web Push public key. Intentionally public; served to browsers. |
| `VAPID_PRIVATE_KEY` | Web Push private key. Server-only; never leaves the API process. |
| `WEB_PUSH_ENABLED` | `true` or `false`. Defaults to enabled when all three VAPID values are present. |
| `USE_COMMERCIAL_LUCKYPENNY` | `true` or `false`; selects the commercial Lucky Penny licensing path. |
| `LUCKYPENNY_LICENSE_KEY` | Lucky Penny commercial license key. |

> [!WARNING]
> `AUTOMAPPER_COMMERCIAL_VERSION` and `MEDIATR_COMMERCIAL_VERSION` are **build-time MSBuild properties**, not runtime configuration. Storing them in Infisical has no effect on a running instance; supply them to the build environment instead.

### `/blazor`

Read by the `Explore.Blazor` BFF.

| Key | Purpose |
|---|---|
| `API_ENDPOINT` | Absolute base URL the BFF uses to reach `Explore.API`. |
| `GOOGLE_CLIENT_ID` | Optional Google SSO client identifier. |
| `GOOGLE_CLIENT_SECRET` | Optional Google SSO client secret. |

### `/keycloak`

Required only when `AUTHENTICATION_PROVIDER=keycloak`.

| Key | Purpose |
|---|---|
| `KEYCLOAK_ENDPOINT` | Public base URL of Keycloak. |
| `KEYCLOAK_REALM` | Realm name. |
| `KEYCLOAK_CLIENT_ID` | Browser/BFF client metadata used for onboarding detection. |
| `KEYCLOAK_BLAZOR_CLIENT_SECRET` | Confidential client secret for the Blazor BFF. |
| `KEYCLOAK_API_CLIENT_SECRET` | Optional; only for deployments that make the API resource-server client confidential. |
| `KEYCLOAK_ADMIN_USERNAME` | Keycloak administrator username used by bootstrap sync. |
| `KEYCLOAK_ADMIN_PASSWORD` | Keycloak administrator password used by bootstrap sync. |
| `KEYCLOAK_DB_PASSWORD` | Password for the Keycloak database container. |
| `KEYCLOAK_SMTP_*` | Optional realm SMTP bootstrap for Keycloak's own verification mail. Leave `KEYCLOAK_SMTP_HOST` blank to preserve existing Keycloak settings. |

Keycloak's own account emails are configured here and are separate from ISLAMU Event's `/smtp` delivery.

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
| `ERASURE_TOPOLOGY` | Privacy erasure topology: `EmbeddedSqlite`, `CoLocated`, or `ExternalDatabase`. |

Runtime and migrator logins must be distinct. Never give runtime services the migrator role, and never expose either to the Blazor client.

### `/database/erasure`

Endpoint and credentials for the privacy-erasure authority, used only when `ERASURE_TOPOLOGY=ExternalDatabase`. The provider is fixed to PostgreSQL.

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

`IDENTITY_DATABASE_TOPOLOGY` itself is non-secret deployment intent and belongs in the deployment environment, not this folder.

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

The browser never receives these values; it sees only configured/ownership metadata.

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

Required only when the storage governance setting selects S3.

| Key | Purpose |
|---|---|
| `STORAGE_S3_ENDPOINT` | S3 API endpoint (AWS, MinIO, Cloudflare R2). |
| `STORAGE_S3_PUBLIC_ENDPOINT` | Optional public-facing endpoint for presented URLs. |
| `STORAGE_S3_BUCKET_NAME` | Bucket dedicated to platform uploads. |
| `STORAGE_S3_ACCESS_KEY_ID` | Access key identifier. |
| `STORAGE_S3_SECRET_ACCESS_KEY` | Secret access key. |
| `STORAGE_S3_REGION` | Region identifier. |

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
| `STRIPE_PLATFORM_SECRET_KEY` | Platform secret key. `Test` mode requires an `sk_test_` prefix; `Live` requires `sk_live_`. |
| `STRIPE_WEBHOOK_SECRET` | Endpoint signing secret. The Connect endpoint uses only this binding. |

### `/atproto`

Required only when AT Protocol login is enabled.

| Key | Purpose |
|---|---|
| `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | OAuth client private key set used by the BFF. |
| `ATPROTO_SESSION_ENCRYPTION_KEYRING` | Session-envelope encryption keyring. |
| `ATPROTO_SESSION_JWT_PRIVATE_JWKS` | First-party session JWT signing key set. |

---

## 5. Which Service Reads Which Folder

Each host reads a bounded folder list at startup. A key placed outside the folders its consumer reads will not be found.

| Host | Folders read at startup |
|---|---|
| `Explore.API` | `/keycloak`, `/database`, `/database/erasure`, `/database/identity`, `/api`, `/blazor`, `/cerbos`, `/mcp`, `/ai`, `/storage`, `/smtp`, `/integrations/listmonk` |
| `Explore.Blazor` (BFF) | `/keycloak`, `/blazor`, `/atproto` |
| `Explore.AppHost` (Aspire) | `/keycloak`, `/database`, `/database/erasure`, `/api`, `/blazor`, `/cerbos`, `/mcp`, `/ai`, `/storage`, `/smtp`, `/stripe`, `/integrations/listmonk` |
| Migration and design-time factories | `/database`, `/database/erasure`, `/database/identity` |

Folder reads are recursive, so a request for `/database` also returns the secrets stored under `/database/erasure` and `/database/identity`. This is why subfolder key names must stay distinct from their parent's key names.

---

## 6. Runtime Secret Bindings

Beyond startup bootstrap, individual credentials are resolved on demand through `SecretBinding` records that name an Infisical path and key. The runtime resolver scans the project tree, so these folders do not need to appear in any startup list. Create a folder only when you enable the corresponding capability.

| Folder | Keys |
|---|---|
| `/setup` | `SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY` |
| `/webhook` | `WEBHOOKS_SVIX_AUTH_TOKEN`, `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET` |
| `/promotions` | `PROMOTIONS_CODE_LOOKUP_HMAC_KEY` (each binding uses a `v{version}` qualifier) |
| `/admissions` | `ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY`, `ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY`, `ADMISSIONS_RECOVERY_CAPABILITY_HMAC_KEY` |
| `/ticketing/recovery` | `TICKETING_RECOVERY_MANIFEST_HMAC_KEY` |
| `/analytics` | `ANALYTICS_POSTHOG_PUBLIC_KEY`, `ANALYTICS_POSTHOG_HOST`, `ANALYTICS_PERSONAL_API_KEY` |
| `/localization` | `LOCALIZATION_TMS_API_KEY` |
| `/registration-providers` | `REGISTRATION_PROVIDER_API_TOKEN`, `REGISTRATION_PROVIDER_WEBHOOK_SECRET` |

Registration-provider credentials are tenant-scoped. When several tenant connections need distinct tokens for the same key, distinguish them with the binding's `Qualifier` field rather than by inventing new key names.

External moderation credentials (`REPORTING_COOP_API_KEY`, `REPORTING_OSPREY_API_KEY`, `REPORTING_COOP_WEBHOOK_SECRET`) have no Infisical binding folder. Supply them through the deployment environment alongside the rest of the `REPORTING_*` dials.

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
