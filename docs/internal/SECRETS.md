ABOUTME: Explicit Environment, Infisical, and Development/Testing User Secrets authority.
ABOUTME: Covers bootstrap selection, safe diagnostics, health monitoring, and rotation boundaries.

# Secrets Management

`Explore.Secrets` resolves values from exactly one deployment-selected authority.
`Environment` and `Infisical` are supported for deployments. `UserSecrets` is a
local Development/Testing authority. A missing, unsupported, disallowed, or failed
authority stops required startup work and never falls back to another source.

## Instance Onboarding Keys

The seven `INSTANCE_BOOTSTRAP_*` keys follow the same single-authority rule as
every other value here. They come from the deployment environment or from the
one selected secret authority, never from source defaults, appsettings
fallbacks, or a second provider. A missing or unreadable key fails startup
closed.

They select an administrator; they don't authenticate one. The subject, DID,
issuer pairing, and generation are selectors only. Actual privilege is granted
only after a real sign-in presents the exact provider claim.

Diagnostics stay value-free. Logs, health output, and support evidence carry
status and reason codes, never the configured subject, DID, email, profile
names, or any fingerprint value.

## Provider Types

| Provider | Enum | Status | Auth Method |
|---|---|---|---|
| Environment | `0` | Implemented | Explicit environment injection |
| Infisical | `1` | Implemented | Universal Auth (ClientId + ClientSecret) |
| User Secrets | `2` | Development/Testing only | Shared local .NET store |

Vault, Azure Key Vault, and AWS Secrets Manager have no enum, options, factory,
or deployment scaffolding. Any such configured string is invalid and fails closed.

When `Provider = Environment`, secrets come exclusively from process environment
variables. When `Provider = UserSecrets`, secrets come exclusively from the shared
`event-shared-secrets` .NET store and the process must run as Development or Testing.
When `Provider = Infisical`, only Infisical results are authoritative. Missing or
failed values never fall through to appsettings, environment, User Secrets, or a
different provider.

Optional Photon address geocoding does not use an API key, password, token, or
client credential. Its endpoint and bounded runtime settings are ordinary
startup configuration, not secrets. Do not create a Photon secret binding or
place address queries, normalized provider records, coordinates, selection
tokens, tenant/user identifiers, or dataset contents in any secret provider.
If a future contracted geocoder requires credentials, add a separate reviewed
secret definition and resolver in that provider's approved implementation
phase; never reuse the Photon configuration surface.

## Configuration

### SecretProvider Section

```json
{
  "SecretProvider": {
    "Provider": "Environment",
    "FailFast": true,
    "Infisical": {
      "Url": "",
      "ProjectId": "",
      "ClientId": "",
      "ClientSecret": "",
      "Environment": "dev",
      "Paths": ["/"]
    }
  }
}
```

### Local Development Environment

Copy `.env.example` to the ignored repository-root `.env`. Select exactly one
`SECRET_PROVIDER`: `Environment`, `Infisical`, or `UserSecrets`. Environment mode
reads the documented process variables. Infisical mode uses only `INFISICAL_*`
secret-zero bootstrap credentials. User Secrets mode reads the shared store owned
by `src/Explore.Secrets/Explore.Secrets.csproj`; populate it with the same documented
environment-style keys (for example `MAIL_SMTP_PASSWORD`) through your IDE or the
`dotnet user-secrets --project src/Explore.Secrets` CLI. It is rejected unless the
host environment is Development or Testing.

AppHost loads the repository `.env`, so Aspire profiles honor
`SECRET_PROVIDER=UserSecrets` directly. Direct project launches do not load that
file. For direct Standalone development, export only the non-secret selector and
host environment before launch:

```bash
export SECRET_PROVIDER=UserSecrets
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/Event.Standalone
```

EF design-time factories follow the same boundary. After populating all structured
database fields required by the selected provider in the shared store, run the EF
command from a shell with `SECRET_PROVIDER=UserSecrets` and
`DOTNET_ENVIRONMENT=Development`. A clean shell defaults to Production and rejects
User Secrets by design.

User Secrets is loaded once with `reloadOnChange=false`. Any value addition,
replacement, or removal requires a full restart of AppHost and every affected child
or direct host. The five-minute resolver cache bound does not reload the backing
User Secrets file.

> [!IMPORTANT]
> The contributor default Aspire profile is `local-full`. It starts local infrastructure and uses Environment unless `UserSecrets` is explicitly selected, so contributors should not need Infisical credentials.
> Maintainer profiles intentionally differ:
> - `local-core` starts local PostgreSQL/Redis and uses the authority selected in `.env` for external platform values.
> - `local-lite` starts the migration worker, API, and Blazor and uses the same selected authority.
> To switch away from Infisical, explicitly select `Environment` and inject every required value through `.env` or the deployment platform.

### Docker Compose Environment Files

The repository root `.env.example` mirrors the supported Infisical folder layout and documents which service consumes each key. Copy it to `.env` for local Compose runs; `.env` is intentionally ignored by git.

Docker Compose uses `.env` for interpolation before starting containers. The Compose file then passes explicit `environment:` entries into each service. Do not rely on a broad `env_file: .env` import because it would place unrelated secrets into containers that do not need them.

### Authority Cutover And Immediate Recovery

Set `SECRET_PROVIDER=Environment` or `SECRET_PROVIDER=Infisical` in `.env`; do
not configure a secondary source. Validate and start each supported topology with
the normal deployment-owned inputs:

```bash
# Split Compose
docker compose config --quiet
docker compose up -d

# Aspire local profiles
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj --launch-profile local-full

# Direct Standalone image after building it
docker run --env-file .env islamu-event-standalone
```

For an Infisical-backed Aspire profile, select `Infisical` in `.env` and provide
the documented `INFISICAL_URL`, `INFISICAL_PROJECT_ID`, `INFISICAL_CLIENT_ID`,
`INFISICAL_CLIENT_SECRET`, and `INFISICAL_ENV` inputs before starting the AppHost.
For Environment mode, leave every `INFISICAL_*` value blank and provide each
required consumer variable directly.

If startup reports `secret_authority_unauthorized`, `secret_authority_invalid`, or
`secret_authority_unavailable`, repair the selected deployment authority and restart
the affected host. Do not print provider responses, run commands that echo secret
values, or add a lower-source value to make startup appear healthy. A mode change is
an explicit deployment cutover and requires a full restart of the affected replicas.

| Secret authority | Direct Standalone | Aspire Standalone | Aspire Split | Single-replica Compose | Multi-replica Split |
|---|---|---|---|---|---|
| `Environment` | Supported with explicit process injection | Supported in `local-core`/`local-lite`; `local-full` defaults to local Environment mode | Supported in `local-core`/`local-lite`; `local-full` defaults to local Environment mode | Supported through the explicit Compose allow-list | Supported only when the deployment injects one consistent value set and one explicit shared setup secret into every replica |
| `UserSecrets` | Development/Testing only | Supported for local Development profiles | Supported for local Development profiles | Rejected | Rejected |
| `Infisical` | Supported with all five `INFISICAL_*` bootstrap inputs | Supported in `local-core`/`local-lite` | Supported in `local-core`/`local-lite` | Supported with the same explicit bootstrap inputs | Supported when every replica uses the same project/environment authority and deployment-owned setup secret |
| Vault / Azure Key Vault / AWS Secrets Manager | Fail closed | Fail closed | Fail closed | Fail closed | Fail closed |

`SECRET_PROVIDER` has no deployment default. Compose and direct hosts reject a
missing or unsupported value. Environment mode clears Infisical bootstrap inputs;
Infisical mode requires URL, project, client ID, client secret, and environment.
Runtime and migrator processes receive only their role-specific database credentials.
The checked-in Compose topology is single-replica; a multi-replica split deployment
must additionally set `Hosting:ReplicaCount` and provide one shared `SETUP_SECRET`.

### SMTP Transport Ownership

Delivery requires explicit `email.delivery_enabled=true` governance; SMTP credentials
do not enable it. Disabled transports resolve no credentials. Instance-hosted SMTP uses
instance bindings; tenant-owned hosts use exact tenant bindings through the existing
binding repository and `ResolveTenantBindingAsync`, never the tenant-to-instance fallback.
The materialized credential scope is checked again before constructing the transport.
Username and password must either both be absent (anonymous SMTP) or both be present.
Authority failures yield degraded capability and do not permit anonymous fallback.

Only bounded state, enabled intent, and ownership scope enter `EmailDeliveryCapability`.
No extra SMTP credential cache sits above the selected secret authority. The shared
resolver's documented freshness and coordinated-restart requirements below still apply.

### Runtime resolution outcomes and bounded freshness

Runtime bindings return one of five value-free outcomes. `Resolved` is the only
outcome carrying secret material in process memory. `Unconfigured` means the
selected authority has no usable binding/value; `Unavailable` means the selected
provider could not be reached; `Unauthorized` means provider authentication or
authorization failed; and `Invalid` means the binding metadata or selected source
is incompatible with the deployment authority. Provider failures never become a
clean miss and never activate another source.

Required capabilities fail closed for every non-`Resolved` outcome. Optional
integrations remain disabled or degraded within that capability and expose only the
bounded outcome in health data. Logs and metrics contain source/status categories,
not values, exception bodies, environment names, paths, keys, project identifiers,
binding identifiers, or tenant identifiers.

Successful runtime resolutions use the existing process-local memory cache for at
most five minutes. Cache identity includes setting scope, tenant/instance identity,
qualifier, selected source, and binding identity. A safe binding metadata mutation
invalidates the matching entries immediately; provider failures and authorization
failures are never cached as successful absence. Each replica owns its cache, so a
deployment must allow the five-minute freshness bound or restart/drain replicas
after an urgent authority change. Rotation activation and replica acknowledgement
are documented separately with their owning runbook.

The `secret-resolver` readiness check reports `providerState=available` for the
selected Environment/User Secrets authority after its environment gate passes, or
after successful Infisical authentication. A
selected Infisical authority that is incomplete or cannot authenticate is
`Unhealthy` with the value-free `providerState=unavailable`; it is never load-balancer
ready as an optional/unconfigured provider. Follow [Secret provider unavailable](TROUBLESHOOTING.md#secret-provider-unavailable)
without printing provider responses or testing with commands that echo credentials.

### Primary database credentials

The database contract is structured; do not store or inject a raw connection
string. Endpoint metadata uses `DATABASE_PROVIDER`, `DATABASE_HOST`,
`DATABASE_PORT`, `DATABASE_NAME`, `DATABASE_SCHEMA`, `DATABASE_TLS_MODE`, and
`DATABASE_TRUST_SERVER_CERTIFICATE`. MariaDB and MySQL automatically infer
server flavor from `DATABASE_PROVIDER` and default to modern LTS versions (`11.4`
for MariaDB, `8.4` for MySQL); operators on custom engine versions can optionally
supply `DATABASE_SERVER_VERSION`.

`DATABASE_SCHEMA` is non-secret namespace metadata. PostgreSQL and SQL Server
use it as the application and Data Protection schema and keep clean table names
such as `users`. SQLite, MariaDB, and MySQL always apply the fixed `ie_` prefix
and do not use this field for table placement. Prefer a separate SQLite file or
MariaDB/MySQL database for each deployment instance; never invent or store a
configurable prefix secret.

| Compose key | Direct .NET key | Consumer |
|---|---|---|
| `DATABASE_RUNTIME_USERNAME`, `DATABASE_RUNTIME_PASSWORD` | `Database:Runtime:Username`, `Database:Runtime:Password` | API/runtime processes only |
| `DATABASE_MIGRATOR_USERNAME`, `DATABASE_MIGRATOR_PASSWORD` | `Database:Migrator:Username`, `Database:Migrator:Password` | `Event.MigrationService` only |

SQLite has no database credentials. Its `DATABASE_NAME` value is a
persisted local file path and is deployment configuration, not a secret. Do not
give runtime services the migrator role, and never expose either role to the
Blazor client.

### Privacy-erasure authority credentials

| Compose / Infisical key | Direct .NET key | Consumer |
|---|---|---|
| `DATABASE_RUNTIME_USERNAME`, `DATABASE_RUNTIME_PASSWORD` (in `/database/erasure`) or `DATABASE_ERASURE_RUNTIME_USERNAME` | `Database:Erasure:Runtime:Username`, `Database:Erasure:Runtime:Password` | API only, and only for `ExternalDatabase` |
| `DATABASE_MIGRATOR_USERNAME`, `DATABASE_MIGRATOR_PASSWORD` (in `/database/erasure`) or `DATABASE_ERASURE_MIGRATOR_USERNAME` | `Database:Erasure:Migrator:Username`, `Database:Erasure:Migrator:Password` | `Event.MigrationService` only |

For `ExternalDatabase`, endpoint metadata is supplied under `/database/erasure` in Infisical (or via `DATABASE_ERASURE_HOST`, `PORT`, `DATABASE_NAME`, `TLS_MODE`, and `TRUST_SERVER_CERTIFICATE` / `PrivacyErasureAuthorityDatabase:*`); the provider is fixed to PostgreSQL. Use separate
roles: runtime receives only authority append/read/state/evaluate function
execution and still has zero table or sequence access, while the migrator owns
schema, lifecycle functions, grants, and destructive compaction execution. The
usernames must be different; configuration binding and PostgreSQL provisioning
both fail closed when one login is shared across these trust boundaries.
Never pass either
authority credential to
`Explore.Blazor` or `Explore.Blazor.Client`. Rotate the migrator credential
independently of runtime. These values are unused in `EmbeddedSqlite` topology;
that mode has no database username/password and protects its dedicated local
file with filesystem permissions. Its nonsecret deployment fields are
`PrivacyErasureAuthorityEmbedded:Path` (default
`/app/data/privacy_erasure_authority.db`), `WriterReplicaCount=1`, and
`BusyTimeoutSeconds=30`.

There is one Infisical bootstrap schema: `SecretProvider:Provider=Infisical`
selects the authority and `SecretProvider:Infisical:*` (projected from the documented
`INFISICAL_*` deployment inputs) supplies secret-zero Universal Auth credentials.

For full local runs, keep `SECRET_PROVIDER=Environment` and leave `INFISICAL_*` blank
so local structured `DATABASE_*`, Keycloak, Cerbos, and storage values remain
authoritative. Infisical loads primary database configuration directly from `/database`
using `DATABASE_*` keys mapped to the structured `Database:*` configuration section.

### SecretRefresh Section

| Key | Default | Purpose |
|---|---|---|
| `Enabled` | `true` | Enable periodic secret refresh |
| `RefreshInterval` | `00:05:00` | Polling interval |
| `InitialDelay` | `00:00:10` | Delay before first refresh |
| `BaseBackoffDelay` | `00:00:05` | Base delay for exponential backoff |
| `MaxBackoffDelay` | `00:05:00` | Maximum backoff cap |
| `JitterFactor` | `0.1` | Randomization factor to prevent thundering herd |
| `UnhealthyThreshold` | `3` | Consecutive failures before unhealthy status |

Backoff formula: `BaseBackoffDelay × 2^(failures - 1)`, capped at `MaxBackoffDelay`, plus jitter.

### Encryption Section

| Key | Purpose |
|---|---|
| `CurrentKeyVersion` | Active encryption key version (integer) |
| `KeyVersions` | Dictionary of version → key material |
| `MasterKeyEnvironmentVariable` | Env var holding the master key |

Encryption uses AES-256-GCM with 12-byte nonce and 16-byte authentication tag.

### ATProto OAuth Session Envelopes

AT Protocol authentication has three purpose-separated, instance-only key rings:

| Secret key | Consumer | Purpose |
|---|---|---|
| `auth.atproto.oauth_client_private_jwks` | Blazor BFF | P-256/ES256 OAuth `private_key_jwt` assertions and short-lived bootstrap assertions. |
| `auth.atproto.session_encryption_keyring` | Infrastructure | AES-256-GCM encryption of the complete persisted CarpaNet OAuth session. |
| `auth.atproto.session_jwt_private_jwks` | API | P-256/ES256 signing and validation of short-lived first-party ATProto session JWTs. |

Never reuse a key between these purposes. Key IDs may be persisted or advertised where the protocol requires them; private key values, OAuth tokens, DPoP keys, and decrypted session JSON must remain inside the owning server process.

`auth.atproto.session_encryption_keyring` is an instance-only secret used exclusively for durable CarpaNet OAuth sessions. Its value is strict JSON with one active AES-256 key and zero or more retired read keys:

```json
{"keys":[{"kid":"2026-07","k":"<base64url-encoded-32-byte-key>","status":"active"},{"kid":"2026-06","k":"<base64url-encoded-32-byte-key>","status":"retired"}]}
```

Key IDs are persisted as metadata; key material is never stored in the application database. The encrypted envelope contains the complete token set and private DPoP JWK. AES-GCM associated data binds the ciphertext to the tenant, user, provider, subject DID, normalized PDS URI, OAuth client signing-key ID, and envelope version, so copying a row across any of those boundaries fails authentication.

To rotate, publish a new active key and mark the previous active key retired. A successfully restored session is rewritten under the active key. Keep each retired key available until no session row references its key ID; removing it earlier forces affected users to authenticate again. Unknown keys, malformed key rings, ciphertext tampering, or binding mismatches fail closed as reauthentication and must never be repaired by inventing session data or restoring plaintext credential columns.

OAuth client and session-JWT signing rings follow the same overlap rule: exactly one active signing key, with previous keys retained as retired verification/session keys until their pinned OAuth sessions or issued JWTs have expired or been revoked. Rotate one purpose at a time, verify readiness, and only then remove an unused retired key. Removing an in-use session-encryption or OAuth-client key deliberately invalidates the affected sessions and requires those users to sign in again.

Only the BFF OAuth-client signing ring is evaluated by the `atproto-authentication` readiness check. Validate the encryption and session-JWT rings with a controlled sign-in/session refresh before removing retired keys; their consumers otherwise fail closed when persisting/restoring a session or issuing/validating a first-party JWT.

### Promotion Code Lookup HMAC Key

Promotion lookup uses the dedicated registry key `promotions.code_lookup_hmac_key`. It is instance-only, server-only, non-bootstrap secret material; it must not share a key with encryption, signing, webhook, payment, or provider credentials. Each usable key requires a qualified `SecretBinding` whose qualifier is exactly `v{version}`, such as `v1`. The default initial source coordinates are `/promotions/PROMOTIONS_CODE_LOOKUP_HMAC_KEY` for Infisical and `PROMOTIONS_CODE_LOOKUP_HMAC_KEY` for environment-variable bindings. Overlapping versions must point to distinct immutable source coordinates; do not repoint both `v1` and `v2` bindings at one environment variable whose value is replaced.

The value must be standard Base64 that decodes to at least 32 bytes. Infrastructure normalizes the attendee-entered code, scopes the input to tenant and event, and computes HMAC-SHA256. The database stores only the digest and positive lookup-key version as private persistence metadata. Management and checkout read contracts never expose either value or an internal promotion-code identifier; they expose only a masked display label. Organizer code creation and code rotation return the organizer-entered plaintext once, and the Studio component clears it after acknowledgement, context change, failure, or disposal.

Rotate the lookup trust root in this order:

1. Create the new secret value at a distinct external coordinate and add its instance binding under the next qualifier, for example `v2`, without changing or deleting `v1`.
2. Set `Promotions:CodeLookup:ActiveKeyVersion` to the new positive version and restart the API replicas so new publish/code-rotation writes pin that version.
3. Confirm controlled create/apply behavior without copying raw codes, digests, binding coordinates, or secret material into logs, screenshots, health output, or support artifacts.
4. Retain every older qualified key while any active promotion-code row references its `LookupKeyVersion`. Remove an old binding only after an authoritative database/administrative check shows no active code uses it.

Changing the HMAC key value in place under an existing qualifier invalidates lookup for every active code pinned to that version and is not a supported rotation. A missing, malformed, or too-short qualified key fails closed: code publishing/rotation or application does not fall back to another source or compute an unkeyed digest.

### Rotation Section

| Key | Default | Purpose |
|---|---|---|
| `Enabled` | `false` | Enable only the existing options-driven local HTTP/database rotation boundaries |
| `GracePeriod` | `00:00:30` | Keep the previous local HTTP client available for in-flight work after validated activation |
| `MaxConcurrentRotations` | `5` | Parallel rotation limit |

Rotation is a deployment protocol, not a successful options callback. Each local
HTTP/database activation returns a value-free acknowledgement containing an opaque
attempt ID, replica ID, consumer category, bounded status, and timestamp. It never
revokes provider credentials and never claims that other replicas converged.

| Consumer family | Mode | Candidate/activation evidence | Revoke and rollback rule |
|---|---|---|---|
| Promotion/admission HMAC versions and ATProto key rings | `overlap-rollout` | Publish a distinct version, validate it, activate locally, and require the same attempt acknowledgement from every declared replica. | Keep the previous version valid until all replicas acknowledge and pinned data/sessions no longer reference it; reactivate the previous version on any failure. |
| Generic options-driven HTTP clients | `overlap-rollout` (local boundary only) | Validate the candidate client before atomic local swap; deployment tooling must collect every replica acknowledgement. | Keep the old provider credential valid through the grace/acknowledgement window; rollback locally on validation failure. |
| Primary database, Keycloak database, Stripe, Svix, SMTP, S3, analytics, localization, Cerbos, registration providers, Listmonk, AI, and managed control-plane credentials | `coordinated-restart` | Validate at the source, enter maintenance, restart every consumer with one deployment attempt, and require readiness from every replica. | Restore the previous deployment value and restart on failure; revoke only after all old processes are stopped and readiness converges. |
| First-run setup secret | `unsupported-live` | Complete/lock setup or replace the deployment-owned value while setup remains active, then restart. | Never infer live rotation from a process callback; use the setup recovery contract. |

For overlap providers, missing or rejected acknowledgement leaves the rollout
`Pending`; at the declared stale deadline it becomes `FailedClosed`, and the stale
replica must be drained before recovery. Providers without overlap require a
coordinated maintenance restart. One healthy replica is never convergence evidence.
Break glass means restoring the previous provider value and restarting/draining the
declared replica set; it does not mean adding a fallback source or revoking the old
credential early.

## Key Mapping

Infisical uses `SCREAMING_SNAKE_CASE` with path-based sections. The provider maps bidirectionally:

| Infisical Path + Key | .NET Configuration Key |
|---|---|
| `/atproto/ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | `auth.atproto.oauth_client_private_jwks`; resolves to BFF `Atproto:OAuthClientPrivateJwks` |
| `/atproto/ATPROTO_SESSION_ENCRYPTION_KEYRING` | `auth.atproto.session_encryption_keyring`; consumed by Infrastructure session-envelope protection |
| `/atproto/ATPROTO_SESSION_JWT_PRIVATE_JWKS` | `auth.atproto.session_jwt_private_jwks`; consumed by API first-party session JWT signing/validation |
| `/keycloak/REALM_NAME` | `Keycloak:RealmName` |
| `/keycloak/KEYCLOAK_CLIENT_ID` | Nonsecret browser/BFF client metadata mapped to `Keycloak:ClientId` for API onboarding detection. |
| `/keycloak/KEYCLOAK_BLAZOR_CLIENT_SECRET` | Blazor BFF `Keycloak:ClientSecret` and Compose `keycloak-init` client-secret sync input |
| `/keycloak/KEYCLOAK_API_CLIENT_SECRET` | Optional legacy/future Compose `keycloak-init` sync input for deployments that intentionally make the API resource-server client confidential; not needed by the current bearer-only API audience client |
| `/keycloak/KEYCLOAK_SMTP_*` | Optional Compose `keycloak-init` realm SMTP bootstrap. Leave `KEYCLOAK_SMTP_HOST` blank to preserve existing Keycloak SMTP settings; set host/port/from to apply deployment-managed SMTP. |
| `/api/CONTROL_PLANE_REGISTRATION_CREDENTIALS` | `management.control_plane_registration_credentials` | Directional managed control-plane registration credentials. This key is instance-only and its binding stores only deployment-owned source metadata. |
| `/api` or `/cerbos` + `AUTHORIZATION_PROVIDER` | Non-secret `Authorization:Provider` deployment intent. Blank keeps manual Local-first onboarding; `local` or `cerbos` makes the provider deployment-owned and skips the choice page. |
| root or AI path + `AI_TOOL_PROPOSALS_ENABLED` | `AiProvider:ToolProposalsEnabled` |
| `/database/DATABASE_PROVIDER` | Primary database provider: `PostgreSql`, `Sqlite`, `SqlServer`, `MariaDb`, `MySql` |
| `/database/DATABASE_HOST` | Primary database host |
| `/database/DATABASE_PORT` | Primary database port |
| `/database/DATABASE_NAME` | Primary database name or SQLite file path |
| `/database/DATABASE_SCHEMA` | Application schema (default: `islamu_event`; PostgreSQL and SQL Server) |
| `/database/DATABASE_TLS_MODE` | TLS mode: `Prefer`, `Required`, `Disabled` |
| `/database/DATABASE_TRUST_SERVER_CERTIFICATE` | `false` (default: strict CA verification) or `true` (accept self-signed certs) |
| `/database/DATABASE_SERVER_VERSION` | Optional override for MariaDB/MySQL (defaults: MariaDB `11.4`, MySQL `8.4`; e.g. `10.11`, `8.0`) |
| `/database/DATABASE_RUNTIME_USERNAME` | Runtime database username |
| `/database/DATABASE_RUNTIME_PASSWORD` | Runtime database password |
| `/database/DATABASE_MIGRATOR_USERNAME` | Migrator database username |
| `/database/DATABASE_MIGRATOR_PASSWORD` | Migrator database password |
| `/database/ERASURE_TOPOLOGY` | Privacy erasure topology: `EmbeddedSqlite`, `CoLocated`, `ExternalDatabase` |
| `/database/erasure/DATABASE_PROVIDER` | External authority provider (fixed to `PostgreSql`) |
| `/database/erasure/DATABASE_HOST` | External authority PostgreSQL host |
| `/database/erasure/DATABASE_PORT` | External authority PostgreSQL port (default: `5432`) |
| `/database/erasure/DATABASE_NAME` | External authority PostgreSQL database name |
| `/database/erasure/DATABASE_TLS_MODE` | External authority TLS mode: `Prefer`, `Required`, `Disabled` |
| `/database/erasure/DATABASE_TRUST_SERVER_CERTIFICATE` | `false` (default: strict CA verification) or `true` (accept self-signed certs) |
| `/database/erasure/DATABASE_RUNTIME_USERNAME` | External authority runtime username (function-execution role) |
| `/database/erasure/DATABASE_RUNTIME_PASSWORD` | External authority runtime password |
| `/database/erasure/DATABASE_MIGRATOR_USERNAME` | External authority migrator username (schema/admin role) |
| `/database/erasure/DATABASE_MIGRATOR_PASSWORD` | External authority migrator password |
| storage path + `STORAGE_S3_*` | `Storage:S3*` (for example `/storage/STORAGE_S3_ENDPOINT` → `Storage:S3Endpoint`) |
| `/smtp/MAIL_SMTP_HOST` | `smtp.host` secret binding default; Development seed maps it to `email.smtp_host` when no SMTP setting exists |
| `/smtp/MAIL_SMTP_PORT` | `smtp.port` secret binding default; Development seed maps it to `email.smtp_port` when no SMTP setting exists |
| `/smtp/MAIL_SMTP_USERNAME` | `smtp.username` secret-bearing SMTP username |
| `/smtp/MAIL_SMTP_PASSWORD` | `smtp.password` secret-bearing SMTP password |
| `/smtp/MAIL_SMTP_FROM_ADDRESS` | `smtp.from_address` secret binding default; Development seed maps it to `email.from_address` when no SMTP setting exists |
| `/smtp/MAIL_SMTP_FROM_NAME` | `smtp.from_name` secret binding default; Development seed maps it to `email.from_name` when no SMTP setting exists |
| `/cerbos/CERBOS_USE_POLICY_SCOPE` | `Cerbos:UsePolicyScope` |
| `/reporting/OSPREY_API_KEY` | `reporting.osprey_api_key` tenant Osprey provider credential |
| `/reporting/OSPREY_WEBHOOK_SECRET` | `reporting.osprey_webhook_secret` tenant Osprey callback/signing secret when a deployment uses one |
| `/reporting/COOP_API_KEY` | `reporting.coop_api_key` tenant Coop provider credential |
| `/reporting/COOP_WEBHOOK_SECRET` | `reporting.coop_webhook_secret` tenant Coop callback HMAC secret |
| `/stripe/STRIPE_PLATFORM_SECRET_KEY` | `payments.stripe.platform_secret_key` instance/server-only Stripe platform secret owned by each self-hoster |
| `/stripe/STRIPE_WEBHOOK_SECRET` | `payments.stripe.webhook_secret` instance/server-only Stripe webhook signing secret owned by each self-hoster |
| `/promotions/PROMOTIONS_CODE_LOOKUP_HMAC_KEY` | `promotions.code_lookup_hmac_key` instance/server-only HMAC material; each binding uses a positive `v{version}` qualifier |
| `/registration-providers/REGISTRATION_PROVIDER_API_TOKEN` | `registration_provider.api_token` tenant provider API/OAuth credential binding. Google Forms uses this for the OAuth access token or refresh-token envelope. |
| `/registration-providers/REGISTRATION_PROVIDER_WEBHOOK_SECRET` | `registration_provider.webhook_secret` tenant callback signing secret binding for providers that use shared callback secrets. Google Forms Pub/Sub does not use it because callback authentication is Google OIDC. |
| `/integrations/listmonk/LISTMONK_API_USERNAME` | `integrations.listmonk.api_username` |
| `/integrations/listmonk/LISTMONK_API_KEY` | `integrations.listmonk.api_key` |
| `/registration-providers/REGISTRATION_PROVIDER_API_TOKEN` | `registration_provider.api_token` |
| `/registration-providers/REGISTRATION_PROVIDER_WEBHOOK_SECRET` | `registration_provider.webhook_secret` |
| `/api/VAPID_SUBJECT` | `WebPush:VapidSubject` |
| `/api/VAPID_PUBLIC_KEY` | `WebPush:VapidPublicKey` |
| `/api/VAPID_PRIVATE_KEY` | `WebPush:VapidPrivateKey` |
| Environment authority + `STORAGE_S3_*` | consumed by the S3 resolver only when Environment is selected |

The three ATProto rows use the same uppercase name as their default environment-variable name as well as their Infisical key. Private credentials use only the canonical registry names; there are no .NET double-underscore credential aliases. Storage accepts the documented `STORAGE_S3_*` variables only under Environment authority. Primary database bootstrap uses discrete structured fields rather than a connection string. SMTP uses the canonical `MAIL_SMTP_*` names. Registration-provider credentials are tenant-scoped secret definitions and must be bound through `SecretBinding`; use the bounded `Qualifier` field when several tenant connections need distinct API tokens or webhook secrets for the same key. Browser contracts never carry secret values or provider source coordinates.

Stripe secrets are instance-scoped, server-only, and optional while paid events are disabled. `Payments:Stripe:Mode=Test` requires a platform key beginning `sk_test_`; `Live` requires `sk_live_`. The Connect endpoint uses only the dedicated webhook binding, never the platform key or an outgoing-webhook secret. Rotate platform and endpoint secrets deliberately with the matching Stripe mode and endpoint configuration; retain no secret value in logs, support artifacts, browser DTOs, OpenAPI, or the DBML reference.

Compose Keycloak bootstrap consumes `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` only inside the one-shot `keycloak-init` container. Those credentials are not application runtime secrets and must not be stored in governance settings or copied into support artifacts. The init logs redact client secret values.

The checked-in Keycloak realm exports never contain the confidential Blazor
BFF client secret. Compose and local Aspire read
`KEYCLOAK_BLAZOR_CLIENT_SECRET` from the deployment environment and fail closed
when the selected topology requires it but it is absent. AppHost forwards the
deployment value as a secret parameter to `keycloak-init`, the API, and the BFF;
it never generates, persists, or renders a replacement value.

External-Keycloak setup bootstrap accepts a one-time Keycloak admin or service-account username/password through the setup UI. Treat that credential as operator input for a single setup request, not as a platform-managed secret. ISLAMU must not save it to appsettings, environment variables, Infisical paths, database governance settings, logs, traces, screenshots, or support bundles. After a successful bootstrap, only the runtime Keycloak OIDC values and BFF client secrets are stored according to the normal authentication secret ownership model.

Paid-event hosted onboarding never returns payment platform secrets, provider account identifiers, or connection identifiers to the browser. `Payments:OrganizerDirect:ProviderCode` and `ConnectPlatformId` are server configuration, not secret values, but are still omitted from browser readiness contracts.

Keycloak onboarding and administrator reads always redact the runtime client secret. They may expose only configured/source/editability metadata plus nonsecret authority and client ID. Secret values remain in the selected deployment authority; database rows contain only binding metadata, and setup writes do not persist a replacement value.

Keycloak configuration writes and secret rotation derive ownership/configured state from authoritative server-side configuration instead of trusting client-supplied ownership metadata. A new confidential BFF client requires a secret; a blank write is valid only when the server already resolves an effective secret that was redacted from the browser read. Deployment-managed rotation returns operator action guidance and never writes a replacement to application storage.

### Onboarding And Setup Credentials

The `/setup` route is a pre-authentication operator gateway, not a browser-owned secret workflow. Browser-supplied setup, tenant, authorization, and provider-administration headers are stripped before proxying. The BFF/server replaces them only from trusted server-owned state, and the API validates the resulting authority again.

The following values must never be persisted in browser storage, returned in browser-facing DTOs, or copied into logs, traces, screenshots, diagnostics, or support artifacts:

- access, refresh, and provider tokens;
- the setup secret or a recoverable derivative;
- temporary provider administrator usernames/passwords or service-account credentials;
- raw provider request or response bodies.

An explicit `SETUP_SECRET` is always authoritative until onboarding completes and locks setup mode. When it is empty on a declared single replica, the API writes one random secret to `SETUP_SECRET_FILE` with `0600` permissions and logs only a `docker cp` retrieval instruction. `Hosting:ReplicaCount > 1` fails startup unless one deployment-owned explicit `SETUP_SECRET` is present. The platform default is `/tmp/islamu-event/setup-secret`; split Compose overrides it to the `setup_data` volume at `/app/bootstrap/setup-secret`, and standalone uses `/app/data/setup-secret`. The file is deleted when onboarding completes; setting a non-empty `SETUP_SECRET` also removes and overrides any generated file. After validation, the BFF keeps the secret in a protected, HttpOnly 30-minute rolling session and requires re-entry after 30 minutes without setup activity.

Retrieve the generated fallback only from the Docker host, copy it to an owner-protected local file, enter it at `/setup`, then remove the local copy. Do not include the generated file in backups. An unmounted temp file may be replaced with a new secret after container recreation; the old value immediately stops working. Read-only containers and rolling or multi-replica API deployments must use an explicit shared `SETUP_SECRET` from their platform secret manager.

Rerunning verification or completion does not grant permission to read a stored secret back. Application-managed credentials remain write-only and are rotated through the owning server operation. Deployment-managed credentials remain authoritative in their configured environment/secret provider; rotate them there, refresh or restart as required, and confirm only through configured/readiness metadata. Do not overwrite a deployment-managed value from onboarding to repair drift.

See [SELF_HOSTING.md](SELF_HOSTING.md#first-run-setup-secret) for the operator flow and [TROUBLESHOOTING.md](TROUBLESHOOTING.md#onboarding-recovery-matrix) for recovery without disclosing credentials.

### Terminal Assistant Secret Boundary

The sole human terminal target is `Event.SetupAssistant.Terminal`; the machine
CLI has no interactive or secret-entry mode. The Terminal.Gui target accepts no
secret arguments, environment transport, redirected standard streams, clipboard
paste, history, or alternate console renderer. It owns one bounded mutable
buffer, masks the visible field, clears state across every terminal path, and
returns only diagnostic code, readiness counts, and artifact digest.

The Terminal.Gui field stores bullet count only; the real input never enters
its `Text` or undo history. Terminal composition sends a nonsecret placeholder
through the canonical string-based Core workflow, then replaces it with
clearable URL-safe UTF-8 bytes. Mutable secret buffers are zeroed after the
write attempt. .NET, the operating system, and storage hardware still cannot
guarantee physical erasure of every runtime, kernel, or device-level copy; do
not treat this process as resistant to a compromised local account, debugger,
memory dumper, or crash-dump collector.

On Linux, macOS, and FreeBSD it writes a new owner-only `0600` file with
create-new/no-overwrite semantics. Unsupported protected-write platforms fail
closed. The generated file is deployment input: move its values into the
selected deployment secret authority, then remove the local file when it is no
longer required.


## Ownership Model

Secrets use a platform-wide ownership model so environment variables and external secret providers are not permanent live overrides by accident. The ownership source is metadata; browser-facing DTOs expose only configured/source/editability flags and never raw values, ciphertext, provider tokens, or resolved secret coordinates.

| Mode | Source types | UI behavior | Runtime meaning |
|---|---|---|---|
| Deployment-managed | `EnvironmentVariable` or `Infisical` metadata binding | Read-only configured/source/status metadata; rotate outside the app | Values remain in the selected deployment authority and changes require provider refresh or redeploy/restart. |
| Request/job bootstrap | Purpose-bound credential supplied for one setup request or job | Never persisted or readable through status UI | The credential expires with the request/job and cannot become a runtime fallback. |

Application databases store only non-secret binding/reference metadata. The
`SecretResolver` dispatches through one selected source and never falls back to a
second source after absence or failure. APIs and UI expose only value-free status;
they do not prefill, persist, or read back deployment secret values.

Reporting provider secrets are server-side tenant settings. API keys and webhook secrets for Osprey and Coop must never be returned in browser DTOs, HAL links, health checks, logs, metrics, traces, screenshots, issue templates, or support bundles; browser/control-plane surfaces may expose only configured/source/editability metadata. Routing update actions are write-only for secret values: supplying a new Osprey/Coop API key or webhook secret rotates that tenant value, while omitting the field or sending it blank preserves the currently stored secret. There is no implicit clear-secret endpoint and no readback path; confirm rotation through configured flags, provider readiness checks, and secret-provider audit trails.

Current migrated surface: Cerbos authorization settings expose endpoint and Admin API credential ownership metadata. `AUTHORIZATION_PROVIDER` is non-secret deployment intent, while `CERBOS_ADMIN_USERNAME` and `CERBOS_ADMIN_PASSWORD` are server-side deployment secrets resolved from environment configuration or Infisical. The browser normally sees only configured flags and ownership metadata. During an explicit setup sync, an operator may instead submit a complete one-time pair; it exists only in the Blazor server circuit and request pipeline, overrides deployment credentials for that call, is cleared after the call, and is never written to `SystemSetting`, returned by an API, or logged. `CERBOS_ADMIN_PASSWORD_HASH` is the Cerbos server verifier and cannot authenticate an Admin API client; keep the matching plaintext password only in deployment secrets or enter it for one sync. Reporting provider secret keys are registered as sensitive hierarchical settings for the moderation routing foundation. Listmonk API username/key values are registered server-side secret bindings; admin updates are write-only and browser DTOs expose configured flags only. Stripe `payments.stripe.platform_secret_key` and `payments.stripe.webhook_secret` are instance/server-only definitions for self-hoster-owned platform credentials. Promotion lookup resolves the qualified instance-only `promotions.code_lookup_hmac_key` binding for every digest operation. SMTP, S3, OAuth, localization/TMS, and AI keys still have area-specific storage/UI paths and must not be documented as fully migrated until their resolvers use the shared ownership metadata consistently.

Web Push VAPID keys are deployment configuration. Infisical `/api/VAPID_PRIVATE_KEY` maps to `WebPush:VapidPrivateKey`; it is a server-only secret and must never appear in browser configuration, API responses, HAL links, logs, traces, health data, screenshots, or support artifacts. `VAPID_PUBLIC_KEY` is intentionally public and is returned by `GET /vapid-public-key` as plain text and by `GET /api/notification/web-push/config`. Browser subscription endpoints and `p256dh`/`auth` material are stored tenant-scoped and are never echoed by subscription status DTOs.

## ISecretProvider Interface

| Method | Returns | Purpose |
|---|---|---|
| `InitializeAsync` | `Task` | One-time provider setup |
| `GetSecretAsync` | `Task<string?>` | Single secret by key |
| `GetSecretWithMetadataAsync` | `Task<SecretWithMetadata?>` | Secret with version and timestamps |
| `GetSecretsByPathAsync` | `Task<IDictionary>` | All secrets under a path |
| `RefreshAsync` | `Task` | Force refresh from provider |
| `GetHealthAsync` | `Task<HealthCheckResult>` | Provider health status |

Properties: `ProviderType`, `SupportsRefresh`.

## Health Check

Registered as `secret_provider` with tag `secrets`.

| Status | Condition |
|---|---|
| Healthy | Fewer than `UnhealthyThreshold` consecutive failures |
| Degraded | 1–2 consecutive failures |
| Unhealthy | ≥ `UnhealthyThreshold` consecutive failures |

Health data includes: provider type, supports refresh, consecutive failure count, last successful refresh timestamp.

## Metrics

Meter name: `Explore.Secrets`

| Instrument | Type | Purpose |
|---|---|---|
| `secrets_refresh_total` | Counter | Total refresh attempts |
| `secrets_refresh_failures_total` | Counter | Failed refresh attempts |
| `secrets_refresh_duration_seconds` | Histogram | Refresh operation duration |
| `secrets_consecutive_failures` | UpDownCounter | Current consecutive failure count |
| `secrets_last_refresh_timestamp_seconds` | Gauge | Unix timestamp of last successful refresh |

## Refresh Service

A `BackgroundService` using `PeriodicTimer` that:

1. Waits `InitialDelay` before first poll.
2. Calls `ISecretProvider.RefreshAsync()` at each `RefreshInterval`.
3. On success, calls `IConfigurationRoot.Reload()` to propagate changes.
4. On failure, applies exponential backoff with jitter.
5. Logs structured warnings with correlation IDs.

## Audit Decorator

Wraps `ISecretProvider` for value-free provider lifecycle mutations only. Secret reads do not create a persistent audit trail.

Audit entries track: Operation, ProviderType, KeyPattern (redacted), Timestamp, UserId (extracted via `sub` → `nameidentifier` → `sid` fallback), CorrelationId.

## DI Registration

| Method | Purpose |
|---|---|
| `AddSecretProvider` | Core provider registration |
| `AddSecretManagement` | Full setup (provider + refresh + health + metrics) |
| `AddSecretObservability` | Health checks + metrics |
| `AddSecretMetrics` | Prometheus metrics only |
| `AddSecretHealthCheck` | Health check only |
| `AddSecretRefreshService` | Background refresh service |
| `AddRotationAwareHttpClientFactory` | HttpClient with rotating credentials |
| `AddRotationAwareDbContextFactory<T>` | DbContext with rotating connection strings |
| `AddConnectionRotation<T>` | Generic connection rotation |

Typical startup: `services.AddSecretManagement(configuration)` registers everything.

## Configuration Manifest Is Not A Secret Store

`CONFIGURATION_MANIFEST_MODE`, `CONFIGURATION_MANIFEST_PATH`, and
`CONFIGURATION_MANIFEST_HOST_DIRECTORY` are non-secret deployment metadata. Do not
store the manifest body in `.env`, Infisical, command-line arguments, or image
environment layers. Mount the JSON file separately as read-only configuration.
The `.env` path and host-directory values select where the owning process sees
the file; they neither mount the file nor carry instance or tenant business
values.

The independent instance and tenant catalogs exclude credentials, tokens,
connection strings, encryption/signing material, provider secrets, secret
references, PII, and payment operational state. Keep those values in Infisical
or the documented `.env` bindings. Unknown, sensitive, or non-catalog keys fail
closed before any instance or tenant state is written. Export remains a
secret-free configuration artifact, never a database or secret backup.

## Setup Live Secret-Binding Boundary

Setup Live can submit a value only after the target reports the exact binding
as `Ready`. The target writes through its already-selected authority; the
current live-write authority is Infisical. Environment and User Secrets are not
live-write providers, and absence or provider failure never falls back to
appsettings, another provider, or stored coordinates.

The enrollment capability, raw value, and target-local Infisical coordinates
are never saved in a profile, database row, portable artifact, log, trace,
metric, response, or support bundle. The API exposes value-free readiness and
receipts only and has no readback route. Release activation remains disabled by
`eng/setup-assistant/generated/setup-live-release-capabilities.json`.

## Related

- [CONFIGURATION.md](CONFIGURATION.md) — application settings
- [SELF_HOSTING.md](SELF_HOSTING.md) — environment variable reference
- [OPERATIONS.md](OPERATIONS.md) — health checks and metrics
