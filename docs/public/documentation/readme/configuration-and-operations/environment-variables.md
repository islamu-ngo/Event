---
description: Comprehensive reference for all baseline, advanced, and profile-specific environment variables.
---
<!-- ABOUTME: Public operator reference for supported environment-backed configuration. -->
<!-- ABOUTME: Distinguishes baseline settings, secret values, and optional deployment profiles. -->

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
| `PUBLIC_URL` | **Baseline** | `http://localhost:7002` | Fully-qualified public HTTPS URL of your application (e.g., `https://events.example.org`). |
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
| `KEYCLOAK_URL` | **Baseline** | `http://localhost:8080` | Public or reverse-proxied base URL of Keycloak (e.g., `https://auth.example.org`). |
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
| `STORAGE_PROVIDER` | **Baseline** | `local` | Storage provider: `local` or `s3`. |
| `STORAGE_LOCAL_ROOTPATH` | **Baseline** | `/app/storage-data/local` | Filesystem directory used when `STORAGE_PROVIDER=local`. |
| `STORAGE_S3_ENDPOINT` | Advanced | None | S3 API endpoint (e.g., `https://s3.amazonaws.com` or MinIO URL). |
| `STORAGE_S3_BUCKET_NAME` | Advanced | None | Dedicated bucket name for platform uploads. |
| `STORAGE_S3_ACCESS_KEY_ID` | Advanced (Secret) | None | S3 Access Key ID. |
| `STORAGE_S3_SECRET_ACCESS_KEY` | Advanced (Secret) | None | S3 Secret Access Key. |
| `STORAGE_S3_REGION` | Advanced | `us-east-1` | S3 Region identifier. |
| `STORAGE_S3_FORCE_PATH_STYLE` | Advanced | `true` | Set `true` for MinIO / self-hosted S3; `false` for AWS S3. |

> [!NOTE]
> **We recommend:** Use `local` storage with a mounted volume for single-node deployments. Use `s3` with Cloudflare R2 or MinIO for multi-replica or high-traffic event media hosting.

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

Set connection security through `email.smtp_security` (`StartTls` by default;
`None`, `SslOnConnect`, and `Auto` are also supported). Keycloak and other account
providers retain their own verification and recovery delivery configuration.

---

## 7. Privacy Erasure Authority (GDPR & Anti-Resurrection)

| Variable | Status | Default | Description |
|---|---|---|---|
| `ERASURE_TOPOLOGY` | **Baseline** | `EmbeddedSqlite` | Storage topology: `EmbeddedSqlite` (dedicated local file), `CoLocated`, or `ExternalDatabase`. |
| `ERASURE_EMBEDDED_PATH` | **Baseline** | `/app/data/privacy_erasure_authority.db` | File path when `ERASURE_TOPOLOGY=EmbeddedSqlite`. |
| `ERASURE_WRITER_REPLICA_COUNT` | Advanced | `1` | Maximum write concurrency for the embedded authority database. |
| `ERASURE_BUSY_TIMEOUT_SECONDS` | Advanced | `30` | SQLite busy timeout before serializable retry. |
| `DATABASE_ERASURE_HOST` | Advanced | None | Hostname if using `ExternalDatabase` topology. |
| `DATABASE_ERASURE_PORT` | Advanced | `5432` | Port for external erasure authority database. |
| `DATABASE_ERASURE_NAME` | Advanced | None | Database name for external erasure authority. |
| `DATABASE_ERASURE_RUNTIME_USERNAME` | Advanced | None | Least-privilege runtime user for external erasure DB. |
| `DATABASE_ERASURE_RUNTIME_PASSWORD` | Advanced (Secret) | None | Password for runtime user on external erasure DB. |

> [!NOTE]
> **We recommend:** Keep `EmbeddedSqlite`. It runs with zero operational overhead and guarantees strict GDPR anti-resurrection isolation without requiring a second database server.

---

## 8. First-Run Setup & Administrator Bootstrap

| Variable | Status | Default | Description |
|---|---|---|---|
| `SETUP_SECRET` | **Baseline** | None | Pre-shared secret to unlock `/setup`. If left blank, generated automatically in volume. |
| `INSTANCE_BOOTSTRAP_MODE` | **Baseline** | `Interactive` | Mode: `Interactive` (web wizard at `/setup`) or `ConfiguredAdministrator` (headless). |
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | Advanced | None | Required if headless: `keycloak` or `atproto`. |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | Advanced | None | Required if headless: exact user ID subject claim issued by Keycloak (`sub` claim). |
| `INSTANCE_BOOTSTRAP_BINDING_GENERATION` | Advanced | None | Required if headless: positive integer generation counter. |
| `INSTANCE_BOOTSTRAP_ADMIN_EMAIL` | Advanced | None | Administrator email address for headless provisioning. |

---

## 9. Operator Legal Identity (Production Gate)

Mandatory legal identity fields required before production traffic can be served:

| Variable | Status | Default | Description |
|---|---|---|---|
| `INSTANCE__OPERATORIDENTITY__OPERATORID` | **Baseline** | None | UUIDv7 unique identifier for the operating legal entity. |
| `INSTANCE__OPERATORIDENTITY__PUBLICNAME` | **Baseline** | None | Public brand name of the deploying organization. |
| `INSTANCE__OPERATORIDENTITY__LEGALNAME` | **Baseline** | None | Full legal registered entity name. |
| `INSTANCE__OPERATORIDENTITY__OFFICIALINSTANCE` | **Baseline** | `false` | True only for the canonical upstream project deployment. |
| `INSTANCE__OPERATORIDENTITY__OPERATORKIND` | **Baseline** | `community` | Operator kind: `community`, `regional`, or `commercial`. |
| `INSTANCE__OPERATORIDENTITY__JURISDICTION` | **Baseline** | `US-CA` | Legal jurisdiction code (e.g. `US-CA`, `GB`, `FR`). |
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

## Related Guides & Next Steps

* **[Secrets Management](secrets.md)** — Securely bind passwords, API keys, and certificates via Environment or Infisical.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Apply your `.env` configuration in a production split-container stack.
* **[Backup, Restore & Upgrade](backup-restore-upgrade.md)** — Production backup scripts and version migration procedures.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Practical solutions for configuration mismatches and startup errors.
