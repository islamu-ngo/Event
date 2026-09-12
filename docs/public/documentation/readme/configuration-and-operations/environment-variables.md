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

## 1. Core Deployment & Networking

| Variable | Status | Default | Description |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | **Baseline** | `Production` | Runtime mode: `Production`, `Staging`, `Development`, or `Testing`. |
| `PUBLIC_BASE_URL` | **Baseline** | `http://localhost:7002` | Fully-qualified public HTTPS URL of your application (e.g., `https://events.example.org`). |
| `API_HTTP_PORT` | **Baseline** | `7039` | Internal HTTP port for `Explore.API`. |
| `UI_HTTP_PORT` | **Baseline** | `7002` | Internal HTTP port for `Explore.Blazor` (BFF). |
| `KEYCLOAK_HTTP_PORT` | **Baseline** | `8080` | Internal HTTP port binding for Keycloak container. |
| `MAILPIT_SMTP_PORT` | **Baseline** | `1025` | SMTP port binding for local Mailpit container. |
| `MAILPIT_UI_PORT` | **Baseline** | `8025` | Webmail UI port binding for local Mailpit container. |
| `DEPLOYMENT_MODE` | **Baseline** | `SingleTenant` | Multi-tenancy mode: `SingleTenant` or `multi_tenant`. Must be set before first-run onboarding. |
| `BFF_ADMIN_HOSTS` | Advanced | None | Comma-separated list of dedicated admin hostnames (e.g., `admin.example.org`) to render the Instance Console. |

> [!NOTE]
> **We recommend:** Keep `DEPLOYMENT_MODE=SingleTenant` for individual community centers, mosques, and local non-profits. Use `multi_tenant` only if operating a multi-chapter umbrella organization hosting independent community spaces.

---

## 2. Database & Relational Persistence

| Variable | Status | Default | Description |
|---|---|---|---|
| `DATABASE_PROVIDER` | **Baseline** | `PostgreSql` | Selected provider: `PostgreSql`, `Sqlite`, `SqlServer`, `MariaDb`, or `MySql`. |
| `DATABASE_HOST` | **Baseline** | `postgres` | Hostname or IP of the database server. |
| `DATABASE_PORT` | **Baseline** | `5432` | Database port (`5432` for PostgreSQL, `1433` for SQL Server, `3306` for MySQL). |
| `DATABASE_NAME` | **Baseline** | `islamu_event_db` | Target database name. |
| `DATABASE_SCHEMA` | **Baseline** | `islamu_event` | Schema namespace for PostgreSQL/SQL Server (clean table names inside it). |
| `DATABASE_RUNTIME_USERNAME` | **Baseline** | None | Least-privilege credentials used by `Explore.API` for runtime queries. |
| `DATABASE_RUNTIME_PASSWORD` | **Baseline (Secret)** | None | Password for runtime database user. |
| `DATABASE_MIGRATOR_USERNAME` | **Baseline** | None | DDL-capable credentials used by `Event.MigrationService` to apply migrations. |
| `DATABASE_MIGRATOR_PASSWORD` | **Baseline (Secret)** | None | Password for migration service database user. |
| `DATABASE_TLS_MODE` | **Baseline** | `Prefer` | TLS verification mode: `Disable`, `Prefer`, or `Require`. |
| `DATABASE_TRUST_SERVER_CERTIFICATE` | Advanced | `false` | Set `true` only in local development to trust self-signed TLS certificates. |

> [!NOTE]
> **We recommend:** Use PostgreSQL for multi-container production deployments. For lightweight, single-server setups with zero external infrastructure, use `DATABASE_PROVIDER=Sqlite` with the Standalone Docker image.

---

## 3. Authentication Providers and Local Identity

Select exactly one primary authentication provider. Local Identity is the
runtime default when no provider is explicitly selected. AT Protocol can be
independent while Local Identity or Keycloak is primary, or it can be the sole
passwordless authority.

| Variable | Status | Default | Description |
|---|---|---|---|
| `AUTHENTICATION_PROVIDER` | **Baseline** | `local` | Primary provider: `local`, `keycloak`, or `atproto`. |
| `AUTHENTICATION_LOCAL_JWT_KEY` | **Baseline (Secret)** | None | Base64-encoded Local JWT signing key of at least 256 bits; required when Local Identity is primary. |
| `AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD` | Advanced | `5` | Consecutive failed Local Identity attempts before lockout. |
| `AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES` | Advanced | `15` | Local Identity lockout duration. |
| `ATPROTO_LOGIN_ENABLED` | Baseline | `false` | Enables AT Protocol login. It must be `true` when `AUTHENTICATION_PROVIDER=atproto`. |
| `IDENTITY_DATABASE_TOPOLOGY` | **Baseline** | `colocated` | Local credential storage: `colocated` with the application database or `external`. |
| `IDENTITY_DATABASE_PROVIDER` | External topology | None | External credential provider: PostgreSQL, SQLite, SQL Server, MariaDB, or MySQL. |
| `IDENTITY_DATABASE_CONNECTION_STRING` | External topology (Secret) | None | Complete operator-managed connection string. Prefer the discrete runtime/migrator settings below. |
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
| `KEYCLOAK_BLAZOR_REDIRECT_URIS` | Advanced | None | Comma-separated list of allowed redirect URIs if overriding default discovery. |
| `KEYCLOAK_BLAZOR_WEB_ORIGINS` | Advanced | None | Allowed CORS web origins for Keycloak client. |

---

## 4. Secret Authority Management

| Variable | Status | Default | Description |
|---|---|---|---|
| `SECRET_PROVIDER` | **Baseline** | `Environment` | Provider authority: `Environment` (direct `.env` injection), `Infisical`, or `UserSecrets` (dev only). |
| `INFISICAL_URL` | Advanced | `https://app.infisical.com` | Infisical server URL if using Infisical. |
| `INFISICAL_PROJECT_ID` | Advanced | None | Target Infisical project UUID. |
| `INFISICAL_CLIENT_ID` | Advanced | None | Universal Auth Machine Client ID. |
| `INFISICAL_CLIENT_SECRET` | Advanced (Secret) | None | Universal Auth Client Secret. |
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

> [!NOTE]
> **We recommend:** Keep `EmbeddedSqlite`. It runs with zero operational overhead and guarantees strict GDPR anti-resurrection isolation without requiring a second database server.

---

## 8. First-Run Setup & Administrator Bootstrap

| Variable | Status | Default | Description |
|---|---|---|---|
| `SETUP_SECRET` | **Baseline** | None | Pre-shared secret to unlock `/setup`. If left blank, generated automatically in volume. |
| `INSTANCE_BOOTSTRAP_MODE` | **Baseline** | `Interactive` | Mode: `Interactive` (web wizard at `/setup`) or `ConfiguredAdministrator` (headless). |
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | Advanced | None | Required if headless: `local`, `keycloak` or `atproto`. |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | Advanced | None | Exact provider subject: a canonical UUIDv7 Local username, Keycloak `sub`, or ATProto DID. |
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
| `INSTANCE__OPERATORIDENTITY__ISOFFICIALINSTANCE` | **Baseline** | `false` | True only for the canonical upstream project deployment. |
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
| `PUBLIC_BASE_URL` | platform | public | None | required | process |
| `API_HTTP_PORT` | deployment | public | None | optional | deployment |
| `UI_HTTP_PORT` | deployment | public | None | optional | deployment |
| `KEYCLOAK_HTTP_PORT` | identity | public | None | optional | process |
| `MAILPIT_UI_PORT` | integration | public | 8025 | defaulted | deployment |
| `DEPLOYMENT_MODE` | deployment | public | None | optional | deployment |
| `SECRET_PROVIDER` | security | sensitive | None | required | process |
| `DATABASE_PROVIDER` | database | public | PostgreSql | defaulted | process |
| `DATABASE_HOST` | database | public | None | required | process |
| `DATABASE_PORT` | database | public | None | optional | process |
| `DATABASE_NAME` | database | public | None | required | process |
| `DATABASE_SCHEMA` | database | public | islamu_event | defaulted | process |
| `DATABASE_RUNTIME_USERNAME` | database | public | None | required | process |
| `DATABASE_RUNTIME_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_MIGRATOR_USERNAME` | database | public | None | required | process |
| `DATABASE_MIGRATOR_PASSWORD` | database | sensitive | None | required | process |
| `DATABASE_TLS_MODE` | database | public | Prefer | defaulted | process |
| `AUTHENTICATION_PROVIDER` | platform | public | None | optional | process |
| `ATPROTO_LOGIN_ENABLED` | platform | public | None | optional | process |
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | identity | public | None | required | process |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | identity | sensitive | None | required | process |
| `INSTANCE_BOOTSTRAP_BINDING_GENERATION` | identity | public | None | required | process |
| `INSTANCE_BOOTSTRAP_ADMIN_EMAIL` | identity | sensitive | None | optional | process |
| `INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME` | identity | sensitive | None | optional | process |
| `INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME` | identity | sensitive | None | optional | process |
| `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` | identity | secret | None (secret) | required | process |
| `AUTHENTICATION_LOCAL_JWT_KEY` | security | secret | None (secret) | optional | process |
| `AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD` | security | public | 5 | defaulted | process |
| `AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES` | security | public | 15 | defaulted | process |
| `IDENTITY_DATABASE_TOPOLOGY` | integration | public | colocated | defaulted | process |
| `KEYCLOAK_ENDPOINT` | identity | secret | None (secret) | required | process |
| `KEYCLOAK_REALM` | identity | secret | None (secret) | required | process |
| `KEYCLOAK_BLAZOR_CLIENT_ID` | identity | public | None | required | process |
| `KEYCLOAK_BLAZOR_CLIENT_SECRET` | identity | secret | None (secret) | required | process |
| `KEYCLOAK_DB_DATABASE` | integration | public | None | optional | deployment |
| `KEYCLOAK_DB_USERNAME` | integration | public | None | optional | deployment |
| `KEYCLOAK_DB_PASSWORD` | integration | secret | None (secret) | optional | deployment |
| `KEYCLOAK_ADMIN` | integration | public | None | optional | deployment |
| `KEYCLOAK_ADMIN_PASSWORD` | integration | secret | None (secret) | optional | deployment |
| `LOCAL_STORAGE_ROOT_PATH` | storage | public | None | optional | capability |
| `EMAIL_DISPATCH_RABBITMQ_ENABLED` | messaging | public | false | defaulted | capability |
| `ERASURE_DATABASE_TOPOLOGY` | platform | public | None | optional | process |
| `ERASURE_EMBEDDED_PATH` | platform | public | None | optional | process |
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
| `DATABASE_TRUST_SERVER_CERTIFICATE` | database | public | false | defaulted | process |
| `KEYCLOAK_API_CLIENT_SECRET` | identity | secret | None (secret) | required | process |
| `KEYCLOAK_BLAZOR_REDIRECT_URIS` | identity | public | None | optional | process |
| `KEYCLOAK_BLAZOR_WEB_ORIGINS` | identity | public | None | optional | process |
| `KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_HOST` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_PORT` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_FROM` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_FROM_DISPLAY_NAME` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_AUTH` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_SSL` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_STARTTLS` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_REPLY_TO` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_ENVELOPE_FROM` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_USER` | identity | public | None | optional | process |
| `KEYCLOAK_SMTP_PASSWORD` | identity | sensitive | None | optional | process |
| `KEYCLOAK_REQUIRE_HTTPS_METADATA` | identity | public | None | optional | process |
| `IDENTITY_DATABASE_PROVIDER` | integration | public | None | optional | process |
| `IDENTITY_DATABASE_CONNECTION_STRING` | integration | secret | None (secret) | optional | process |
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
| `USE_COMMERCIAL_LUCKYPENNY` | platform | public | None | optional | process |
| `LUCKYPENNY_LICENSE_KEY` | platform | sensitive | None | optional | process |
| `AUTOMAPPER_COMMERCIAL_VERSION` | platform | public | None | optional | process |
| `MEDIATR_COMMERCIAL_VERSION` | platform | public | None | optional | process |
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
| `CONTROL_PLANE_PUBLIC_ORIGIN` | deployment | public | None | optional | deployment |
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
| `CERBOS_ADMIN_USERNAME` | integration | secret | None (secret) | optional | capability |
| `CERBOS_ADMIN_PASSWORD_HASH` | integration | sensitive | None | optional | capability |
| `CERBOS_ADMIN_PASSWORD` | integration | secret | None (secret) | optional | capability |
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
| `AI_API_KEY` | integration | sensitive | None | optional | capability |
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
| `KEYCLOAK_INTERNAL_URL` | integration | public | None | optional | deployment |
| `REDIS_CONNECTION_STRING` | integration | sensitive | None | optional | deployment |
| `SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY` | security | secret | None (secret) | optional | process |
| `KEYCLOAK_CLIENT_ID` | identity | secret | None (secret) | optional | process |
| `KEYCLOAK_ADMIN_USERNAME` | identity | secret | None (secret) | optional | process |
| `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | platform | secret | None (secret) | optional | process |
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
| `AI_OPENAI_API_KEY` | integration | secret | None (secret) | optional | capability |
| `AI_ANTHROPIC_API_KEY` | integration | secret | None (secret) | optional | capability |
<!-- END GENERATED ENVIRONMENT CATALOGUE -->

## Related Guides & Next Steps

* **[Secrets Management](secrets.md)** — Securely bind passwords, API keys, and certificates via Environment or Infisical.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Apply your `.env` configuration in a production split-container stack.
* **[Backup, Restore & Upgrade](backup-restore-upgrade.md)** — Production backup scripts and version migration procedures.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Practical solutions for configuration mismatches and startup errors.
