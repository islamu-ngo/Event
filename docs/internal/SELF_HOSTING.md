<!-- ABOUTME: Architectural specification for self-hosting boundaries, headless onboarding, and runtime identity gates. -->
<!-- ABOUTME: Directs operational deployment runbooks to canonical public documentation. -->

# Self-Hosting Architecture & Invariants

> **Audience:** Contributors | Operators
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-09-21
> **Source Anchors:** `src/Event.Standalone/Program.cs`, `src/Explore.API/Hosting/`, `src/Explore.Infrastructure/Mail/EmailDeliveryCapabilityResolver.cs`, `docker-compose.yml`

---

> 📖 **Authoritative Operator Runbooks (Single Source of Truth):**  
> Operational deployment guides, Docker Compose topologies, volume persistence, reverse proxies (Traefik/Caddy/Nginx), and step-by-step upgrade procedures have been migrated to the **canonical public documentation**:
> 
> * 🚀 **[Deployment Tiers & Sizing Guide](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/deployment-tiers)**
> * 🐳 **[Docker Standalone Runbook (SQLite Monolith)](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/docker-standalone)**
> * 📦 **[Docker Compose Production Runbook (Split Topology)](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/docker-compose)**
> * ⚡ **[Coolify with Cerbos & Traefik Runbook](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/coolify-cerbos-traefik)**
> * 💾 **[Backup, Restore & Upgrade Runbook](https://islamu.gitbook.io/islamu-event/documentation/readme/configuration-and-operations/backup-restore-upgrade)**
> 
> For C# composition roots (`Explore.API`, `Explore.Blazor`, `Event.Standalone`, `Event.MigrationService`) and startup lifecycle ordering, see **[HOSTING_ARCHITECTURE.md](HOSTING_ARCHITECTURE.md)**.

---

## 1. Headless Instance Onboarding Invariant

Interactive setup establishes the effective BFF request address internally during
authorized save/completion, without a URL field or confirmation. `PUBLIC_BASE_URL`
overrides automatic resolution for deployment-specific needs. An explicit base
domain is required when enabling subdomain routing, not for path-based tenancy.
Listening addresses are not public origins. See [the configuration boundary](CONFIGURATION.md#guided-setup-configuration-boundary).

You can bring an instance up without touching the interactive setup screens.
Set `INSTANCE_BOOTSTRAP_MODE=ConfiguredAdministrator` plus the provider key
(`local`, `keycloak`, or `atproto`), the exact subject and a positive
`INSTANCE_BOOTSTRAP_BINDING_GENERATION`. External providers require the
administrator email; Local credential email is optional. Supply both profile
names together or neither. Local additionally requires
`INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` from the selected secret authority and a
canonical UUIDv7 subject, used as its username. Under `Interactive`, leave the
configured administrator inputs unset. See [CONFIGURATION.md](CONFIGURATION.md)
for the closed options matrix.

For `keycloak`, the subject is paired with your existing `Keycloak:Authority`
issuer. For `atproto`, use the canonical DID. Deployment mode stays on the
existing `Deployment:Mode` setting.

Startup order is fixed in both Split and Standalone:
1. Migrations and lookup table seeding.
2. Configuration manifest bootstrap.
3. Serializable state preparation.
4. HTTP readiness probe goes active (`/health`).

A failure at any stage halts initialization immediately—read the earliest reason code, not the trailing symptom.

External-provider bootstrap remains pending until sign-in presents the exact
configured provider identity. Local instead provisions through
`LocalAdministratorBootstrapOperation`; it does not wait for an external
provider or send a verification message. Its temporary credential requires
private replacement before ordinary sign-in. Completed onboarding cannot be
replayed or transferred by changing the configured selector.

Interactive Local setup uses the authenticated setup-secret principal and the
`complete-local` HAL action, not public account registration.
`CompleteLocalInstanceOnboardingCommandHandler` rechecks active setup authority,
the current Local provider and launch preflight before provisioning. The wizard
collects a site name, username, temporary password and optional credential email.
Both browser completion paths submit the current journey generation; conflicts
require a refresh rather than blind credential replay. Completed Local status
retains its provider so a refreshed browser can offer sign-in without resubmitting
credentials. Password replacement remains owned by the existing Local login flow.

Journey reads bracket readiness work with two durable generation reads. Each hashes
all system settings so even a non-profile change invalidates stale admission. The
first read also projects the persisted profile; it does not issue four additional
profile lookups. The second read must remain independent to reject concurrent
changes. Completion uses the same generation algorithm without projecting the
profile, resolving secrets, or performing external readiness I/O under its lock.
This work is confined to setup/administration refreshes, not ordinary public reads.

Subsequent Local account creation/reset belongs to a current instance
administrator, not a tenant administrator or setup-secret holder. Issued
credentials are handed over privately and replaced before normal session
issuance; operation-status recovery does not reveal a password again.

Keycloak and AT Protocol retain ownership of their authentication and
verification policy. Disabling Event email does not disable provider checks or
authorize an unverified external identity. Legal/public contact email is a
separate disclosure requirement, not a Local credential or SMTP prerequisite.

The onboarding profile's `SupportEmail` is another distinct concern: public site
support identity. `InstanceOnboardingProfileSettingHelpers` persists its normalized
nullable value as instance-scoped `branding.support_email`, not
`email.from_address`. Existing `BrandingSettingGroup` and governance readback
return it as `BrandingSettingsDto.SupportEmail`. There is no separate profile
store, new endpoint or implicit transport mutation; SMTP sender and delivery
intent still require their own guarded administration.

---

## 2. Operator Identity Governance & Readiness Gate

Runtime operator identity is stored under `instance.operator_identity` and managed
at `/settings/instance?section=operator-identity` through
`GET/PUT /api/instance-operator-identity`. It is not an installation prerequisite.
Completion creates missing canonical identity drafts without inventing legal facts.
A new SingleTenant default directory remains Provisioning; existing directories
and their documents are preserved. MultiTenant creates no default directory.
Administrators reach `/settings/instance?section=getting-started` after sign-in;
publication remains a later explicit, authorized transition.

The getting-started section projects journey identity readiness into **Required
before public disclosure** and **Required before paid events**. Preflight
`RequiredToPublish` checks join the disclosure group; other advisory checks appear
under **Recommended**, retaining their server requirement category (including
`Optional`), state, bounded reason and remediation authority. This is presentation,
not a new requirement registry or workflow engine. Resource actions require HAL
relations; readiness does not grant permissions.

The instance editor retrieves value-free kind/country choices through the identity
document's `form-options` link. The metadata's label/help identifiers are translation
keys, not identity values or edit authority. Only the document's `update` relation
authorizes saves; its revision remains the concurrency token. Unavailable choices
are explicit and do not erase existing values. Registration stays optional.

- **Decoupled Startup:** API and Standalone hosts boot cleanly without operator identity environment variables, allowing the web onboarding wizard and health probes to respond.
- **Optional Headless Bootstrap Seed:** For headless `ConfiguredAdministrator` deployments, `INSTANCE__OPERATORIDENTITY__*` in `.env` can optionally provide first-run identity which is validated and persisted to the database during bootstrap completion.
- **Fail-Closed Consumer Gating:** When operator identity is incomplete, public legal notices return HTTP 503 Service Unavailable, and new paid ticket sales/checkout fail closed (`instance_operator_identity_unavailable`) before payment reservation or provider handoff. First-run setup and administrative repair remain accessible.

Required fields include:
* `OperatorId`: Canonical UUIDv7 identifier (server-managed).
* `PublicName`: User-facing organization name.
* `LegalName`: Legally registered entity name.
* `PublicContactEmail`: Public operator contact.
* `JurisdictionCountryCode`: Jurisdiction country code.
* `OfficialOrigin`: Required HTTPS origin, including for unofficial instances.
* `OperatorKindCode`: Closed `TenantDirectoryOperatorKinds` vocabulary (`registered_organization`, `sole_trader`, `individual`, `public_body`, `unincorporated_association`).
* `WebsiteUrl`, `LegalNoticeUrl`, `TermsUrl`, `PrivacyUrl`: Required HTTPS URLs.

---

## 3. Standalone Core and Optional Service Boundary

The minimum operational deployment is the single `Event.Standalone` container:
* The ISLAMU Event API and Blazor BFF run in one .NET process with SQLite persistence.
* Application, Data Protection, and embedded privacy-erasure migrations execute within that process before traffic is accepted.
* PostgreSQL, Redis, Keycloak, Cerbos, MinIO/S3, Mailpit/SMTP, Svix, and Weblate are optional external integrations, not requirements of the standalone core.

For external container dependencies and third-party license boundaries, consult [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md#standalone-and-optional-service-license-boundary).

### Bounded SQLite Operational Profile

The public [Standalone recipe](../public/documentation/readme/self-hosting/docker-standalone.md#bounded-sqlite-processing-profile)
explicitly selects the optional-processing bounds exercised by
`NativeEmailOptionalStandaloneFixture`: Webhooks, outbox and notification fanout
processing, Quartz execution, email dispatch/RabbitMQ and MCP are disabled through
their existing options. This is an explicit configuration, not new defaults or
another authority over persisted email intent.

The bound matters: `WebhookLocalTargetRepository.CountDueAsync` and
`CountStaleDeliveringAsync` retain SQLite-unsupported `DateTimeOffset` comparisons.
With default Local webhooks enabled, `LocalWebhookDeliveryHealthCheck` reports
the query failure as Unhealthy. The directory's connection-local instant
collation does not cover those queries. The retained native failure and passing
bounded configuration establish this distinction; readiness must not be falsified.

Directory/admin HTTP, optional SMTP health and three-host credential/bootstrap/
Data Protection/disable continuity are verified within this profile. Disabled
dispatchers and Quartz do not execute queued work or scheduled cleanup, so these
checks do not establish dependent asynchronous workflows, physical erasure
completion or default optional-worker topology. Operators must verify and enable
the required processors before relying on those features. The public recipe owns
the exact environment projection; do not duplicate it as another configuration
source here.

### Delivery Authority And Core Health

`EmailSettingDefinitions.DeliveryEnabled` defaults `email.delivery_enabled` to
`false`. `EmailDeliveryCapabilityResolver` evaluates persisted hierarchical
settings before resolving transport secrets. Disabled delivery neither resolves
SMTP credentials nor probes SMTP. Enabling delivery without a usable transport
is a distinct unconfigured/misconfigured state, not a requirement to install a
mail server to complete setup.

`UpdateInstanceSmtpSettingsCommandHandler` authorizes instance administration;
`InstanceSmtpSettingService` sends non-secret policy through
`IEmailDeliverySettingsWriter`. Saving transport configuration and enabling
delivery are separate operations. Disable uses preview/confirmation rather than
a direct `false` patch, preserving outstanding delivery commitments. Generic
settings paths retain the dedicated writer fence, while
`ConfigurationManifestInstanceSettingMutationBoundary` rejects SMTP setting keys.
Neither `.env`, setup projection nor manifest bootstrap is an alternate perpetual
delivery authority.
SMTP credentials remain in the selected secret authority, never in settings
rows or exported policy.

| `/health` check/result | Interpretation |
|---|---|
| `smtp`: Healthy, `smtp_disabled` | Intentional zero-email operation; no transport probe |
| `smtp`: Degraded, `smtp_configuration_unavailable` | Delivery enabled but transport unavailable or incomplete |
| `smtp`: Degraded, `smtp_unavailable` | Configured SMTP diagnostic returned a transport failure |
| `smtp`: Unhealthy, `email_capability_unavailable` | Capability read or unexpected diagnostic exception; do not classify as an ordinary SMTP outage |

`Explore.ServiceDefaults` maps Healthy and Degraded readiness to HTTP 200, and
Unhealthy to HTTP 503. A reported SMTP outage alone does not evict healthy core
traffic. Database, selected secret/security authority, erasure and other required
readiness checks still fail closed. `/alive` is liveness, not proof of delivery.
Dispatch backlog, retention and optional RabbitMQ have separate checks in
`/health`; there is no dedicated `/health/email` route.

### Setup And Compose Projection

`.env.example` is the curated baseline; the public environment reference is the
exhaustive catalogue. Compose leaves SMTP projections empty and defaults
`EMAIL_DISPATCH_RABBITMQ_ENABLED=false`, because base Compose has no broker.
Starting the optional `mail` profile does not persist SMTP policy or enable it.
Canonical setup metadata retains `MAILPIT_UI_PORT` (default 8025) and the supported
`MAIL_SMTP_*` inputs. `MAILPIT_TAG`, `MAILPIT_SMTP_PORT` and
`MAILPIT_MAX_MESSAGES` are not supported inputs: image identity, private SMTP and
the capture count are fixed by Compose rather than operator interpolation.

The actual Split services are `islamu-event-api`, `islamu-event-ui` and
`event-migrationservice`. Both web containers listen on 8080; their default host
ports are 7039 and 7002 respectively. Generated setup secrets live at
`/app/bootstrap/setup-secret` in API `setup_data`, versus
`/app/data/setup-secret` in Standalone. Do not assume changing authentication to
Local removes Compose's existing Keycloak dependencies.

Mailpit is a separately pulled optional capture service, not part of Standalone.
Compose pins `axllent/mailpit:v1.30.0@sha256:0059ef81e492a7192af3816281eed6859eb078bd7bdc58b76757c13e10e53a7d`.
SMTP has no host publication; the inbox is loopback-only. `mailpit_data` stores
capture with a fixed 500-message limit; no relay/forwarding or permissive SMTP
authentication switches are configured. Capture is not external delivery.
The source-free [Mailpit provenance](../../dev/active/email-optional-self-hosting/mailpit-provenance.md)
records exact-tag MIT evidence, not complete image-layer licensing, SBOM,
vulnerability or offline-redistribution certification. Mirroring/bundling needs
separate approval under the optional-service distribution boundary.

### Persistent Keys And Backup Boundaries

`AddApiHostServices` persists Data Protection keys through
`DataProtectionKeyContext` in the selected primary database. Combined Standalone
reuses that registration when no Redis cache connection is supplied; it does
not create `/app/data/dataprotection-keys/`. Its default SQLite database therefore
contains application state, colocated Local Identity and Data Protection keys.

`AddBffDataProtection` preserves an existing registration when Redis is absent;
with `ConnectionStrings:cache`, it selects Redis key
`islamu-event:data-protection-keys` and application name `islamu-event`. Shipped
Split Compose supplies Redis to the separate UI host, backed by `redis_data`;
the API key store remains the primary database. A separate BFF without Redis
does not inherit the API database registration automatically.

Preserve the actual primary database, any external Identity database, uploaded
media, selected secret/key authority and applicable Redis state as coordinated
backup units. Capture SQLite files only with a consistent database/volume backup
or stopped writer, including required WAL companions. Preserve the erasure
authority independently: a primary rollback must not also roll back newer
erasure facts. Persistence wiring alone proves neither crash safety nor a
successful restore, and retaining Data Protection keys alone does not guarantee
that every session survives. Follow [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md)
and verify recovery in an isolated environment.

### Multi-Platform Container Runtime Invariants

1. **OCI Manifest List Single-Tag Contract**: All deployable application containers (`Explore.API`, `Explore.Blazor`, `Event.MigrationService`, `Event.Standalone`) are distributed as multi-platform OCI image indexes (`linux/amd64` and `linux/arm64`). Operators pull a single image tag (e.g. `ghcr.io/islamu-ngo/event-standalone:latest`); the local container engine transparently resolves the matching native architecture layer.
2. **Execution Environment Parity**:
   * **Linux Bare-Metal/Cloud**: Native execution on 64-bit x86 and ARM64 instances (AWS Graviton, Hetzner CAX/ARM, Ampere Altra, Raspberry Pi 4/5).
   * **macOS**: Native execution inside container virtualization (Docker Desktop, OrbStack, Colima). Apple Silicon M-series nodes execute `linux/arm64` natively without Rosetta 2 emulation penalty.
   * **Windows**: Supported via Docker Desktop (WSL2 Linux VM backend). Native Windows Server container mode (`Windows Server Core` / `NanoServer`) is deliberately unsupported; bare Windows Server deployments must run via system services or IIS.
3. **Pure IL & Dynamic PGO Invariant**: Images distribute platform-agnostic Intermediate Language (IL) assemblies rather than Ahead-of-Time (AOT) or ReadyToRun (R2R) pre-compiled binaries. This retains full Tiered Compilation Dynamic Profile-Guided Optimization (Dynamic PGO) at runtime, allowing the JIT compiler to devirtualize and inline CQS command/query handler decorator chains according to host-specific traffic patterns.

---

## Related Specifications & Architecture Docs

* **[HOSTING_ARCHITECTURE.md](HOSTING_ARCHITECTURE.md)** — C# composition roots, assemblies, and startup lifecycle phases.
* **[CONFIGURATION.md](CONFIGURATION.md)** — C# `IOptions<T>` binding, validation, and hierarchical settings resolver.
* **[SECRETS.md](SECRETS.md)** — Environment-first and Infisical secret providers.
* **[SECURITY-MODEL.md](SECURITY-MODEL.md)** — Identity boundaries, fail-closed auth, and tenant isolation.
* **[Public Self-Hosting Documentation](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting)** — Complete operator runbooks and environment recipes.
