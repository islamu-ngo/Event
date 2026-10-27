ABOUTME: Explicit Environment, Infisical, and Development/Testing User Secrets authority.
ABOUTME: Covers bootstrap selection, safe diagnostics, health monitoring, and rotation boundaries.

# Secrets Management

## Actor ownership reads

Application workflows establish the original user and selected tenant before
`IActorOwnershipReader` projects ownership facts through EF Core. Its PostgreSQL
Runtime profile grants only `Actor.Id/UserId/GroupId/OrganizationId/IsDeleted`
and `TenantUser.TenantId/ActorId/IsDeleted` SELECT. Existing membership storage
rights remain separately owned; this read profile does not install them.
Tenant membership filters and RLS remain active. Actor linkage metadata itself is
not user-row-isolated: a stolen Runtime credential can enumerate these fixed
Actor identifiers without Application admission. It cannot use this profile to
read private profiles, moderation or audit fields, write Actor data, or delegate
permissions. Installation and readiness reject drift rather than repairing it;
the reader retains the caller's selected context, connection and transaction.

## Consumer-isolated acquisition and orchestration

`SecretAcquisitionPolicy` admits the compiled consumer's exact roots before
authentication. API composition selects `Api`; Standalone explicitly selects
`Combined`; the migration service selects `Migration`. Combined admits the API
and BFF union, not provider administration. The separate BFF loader retains its
three exact frontend roots. Runtime source options preserve the consumer selected
by composition rather than a consumer supplied by vault content.

The cached Infisical provider independently repeats exact-root admission before
universal authentication, as well as before secret listing. Path failure
diagnostics retain only the finite admitted root and HTTP status; project,
environment, credentials, access token and provider body remain absent.
The native loopback transport cases are authored; executable assurance is
deferred with the programme, not implied by source inspection.

Shared startup, cached-provider and binding reads disable recursion, imports and
reference expansion. Every returned `secretPath` must equal the requested path;
missing provenance, duplicate mapped keys and reference expressions fail closed.
Binding resolution checks registry-owned paths and selected environment before
cache lookup and before client authentication. Retained references undergo the
same admission. Changing a registry default does not authorize an old persisted
coordinate. `keycloak.db_password` is no longer a runtime secret definition.
Canonical BFF authentication writes remain the separate bounded write-only
capability; API read permission does not expand to BFF roots.

API Keycloak metadata lives at `/runtime/api/authentication/keycloak`, lifecycle
authority at `/runtime/api/keycloak/lifecycle`, and Cerbos decision metadata at
`/runtime/api/authorization/cerbos`. Database and other declared runtime roots
remain explicit in `SecretAcquisitionPolicy`. Data Protection ring and admission
folders are deliberately absent from bulk configuration: the separate Phase 9
loader and process-only pin/floor controls remain authoritative.

AppHost does not call the vault or combine runtime, provisioning and infrastructure
folder results. Operators provision infrastructure separately and inject only
resource-owned inputs into its container declarations. Its final project
environment projection suppresses ambient parent secrets, provider admin/database
fields and sibling machine identities. API, BFF, Combined and migration projects
receive independent `INFISICAL_<CONSUMER>_CLIENT_ID/CLIENT_SECRET` pairs. Shared
universal-auth credentials are not a projection fallback. All Aspire modes,
including `local-full`, preserve the selected provider.

The orchestrator is still a trusted process: it observes injected launch inputs
and manages its children. This is not isolation from a compromised AppHost, OS
account, debugger or shared Development User Secrets file. Operators must enforce
independent provider ACLs, prevent identity reuse across consumers, and deny
runtime identities `/provisioning/*`, `/infrastructure/*`, `/keycloak` and
`/cerbos`. Event does not create or prove these external ACLs. Provider containers
receive their own verifier/database/bootstrap values; foreground administration
and external operator-signed Cerbos publication remain separate operations.

For Infisical-backed local data resources, inject their bootstrap database roles
and stable ports separately and provision matching runtime/migrator coordinates
in the vault. AppHost no longer reads runtime database credentials to construct
child environments. An unavailable vault, missing identity or mismatched database
coordinate is a startup failure, not permission to use local generated values.
See the [public cutover procedure](../public/documentation/readme/configuration-and-operations/infisical.md#consumer-isolation-cutover).

`Explore.Secrets` resolves values from exactly one deployment-selected authority.
`Environment` and `Infisical` are supported for deployments. `UserSecrets` is a
local Development/Testing authority. A missing, unsupported, disallowed, or failed
authority stops required startup work and never falls back to another source.

## External Data Protection Admission

`Explore.ServiceDefaults/DataProtection` owns the shared framework adapter because
Split BFF must not reference backend Domain/Application/Secrets assemblies.
`ExternalDataProtectionSource` bypasses general configuration aggregation and
reads only the selected authority. `Environment` uses process variables directly;
`UserSecrets` reads `event-shared-secrets` only in Development/Testing; Infisical
uses Universal Auth and exact, nonrecursive, reference-unexpanded folder reads.
Missing or failed authority never falls back to appsettings, DB, Redis or files.
The API, BFF and Combined composition roots explicitly select `api`, `bff` and
`combined`. API and Combined retain discriminator `islamu-event` because they own
the same durable ciphertext contracts; Split BFF uses `islamu-event:bff:v1`.
Rings are separately provisioned. Sharing backend material across API/Combined
is an explicit stopped-host topology move, not automatic sharing with the BFF.

For partition `api`, `bff` or `combined`, the exact selected-authority roots are:

| Authority location | One allowed value |
| --- | --- |
| `/runtime/<partition>/data-protection` | `EVENT_DP_<PARTITION>_RING` |
| `/admission/<partition>/data-protection` | `EVENT_DP_<PARTITION>_ADMISSION` |

Uppercase the partition in variable names. Each Infisical folder contains exactly
that one value; the runtime identity has read-only access to its two roots, never
write/delete or access to another partition. The reader checks returned folder
and key provenance, refuses redirects/references/compressed responses, and bounds
the response before JSON materialization. HTTPS is required outside loopback
Development/Testing. Existing Infisical bootstrap names apply, including
`INFISICAL_ENV` / `SecretProvider__Infisical__Environment`.

### Independent inventory and restore floor

The ring is JSON `Version=1`, `Partition`, positive `Generation`, `Documents`.
Each document is an unwrapped framework AES-256-CBC/HMACSHA256 key or explicit
per-key revocation XML. Custom descriptor/decryptor types, recursive DP wrapping,
duplicate IDs/properties, unknown JSON members and malformed dates are rejected.
The entire UTF-8 ring is at most 32 KiB and contains at most 16 documents,
**including revocations**. Admission JSON has its own 32 KiB bound and at most
16 inventory entries. Exceeding either bound halts; it never evicts old keys.

The separate admission record has `Version`, `Partition`, `Generation`,
`RingSha256`, `ActiveKeyId`, `NotBeforeUtc`, `NotAfterUtc`, and `Keys`.
Each key entry has `Id`, the SHA-256 of the exact canonical XML
(`XElement.ToString(SaveOptions.DisableFormatting)`), and `Revoked`.
The record lists **all historical keys**, including expired and revoked keys.
The runtime requires equality between this inventory and candidate key IDs,
material fingerprints and revocation records. It verifies every nonrevoked
descriptor by actual authenticated encryption/decryption and proves that the
framework's active round-trip used exactly `ActiveKeyId`.

The record is trusted only when its exact UTF-8 SHA-256 equals
`EVENT_DP_<PARTITION>_ADMISSION_SHA256`, and its generation meets
`EVENT_DP_<PARTITION>_MINIMUM_GENERATION`. These two expectations are read
**only from process environment**, even with Infisical/User Secrets selected.
Operators must inject them from a separately access-controlled, forward-only
deployment record that is NOT restored with ring/admission/application backups.
The floor must never decrease; retain the latest approved pin through recovery.
Putting these variables beside the candidate in the same rollbackable `.env`
does not satisfy this prerequisite. No application can detect an administrator
restoring both its input and its supposed independent expectation.

Initial completeness is an operator custody obligation: inventory every prior
writer, ring, replica, protected-data owner and retained backup before approving
the initial pin. A fresh deployment may explicitly declare that it has no prior
protected state. The candidate ring, an active-key round-trip and the tool's
generated inventory cannot independently prove that declaration. After initial
admission, the operator tool preserves every entry and all revocations; there is
no delete or un-revoke command. A missing historical inventory blocks adoption,
not a reason to manufacture a passing receipt.

### Serving, renewal and cache fencing

`ExternalDataProtectionProvider` owns an isolated framework service provider with
a read-only `IXmlRepository`, no logging sinks, no automatic generation and no
key encryptor. The application exposes its guarded `IDataProtectionProvider`;
outer-host DB/Redis/file persistence options cannot alter the isolated provider.
`StoreElement` rejects, including attempts to generate or revoke at runtime.
The startup hosted service admits before serving. Cached protectors check the
admission lease and active-key expiry on every operation. New protection also
checks the framework payload key ID; a default-key switch fences the process.
Expired retained keys still decrypt under a current admitted ring. Revoked or
missing keys fail; time-limited capability expiry is separately enforced.

Admission leases are at most 24 hours. The operator tool issues 12-hour leases,
shortened by active expiry or the next prepared activation. `external-data-protection`
readiness rereads authority and independent expectations, compares them to the
loaded snapshot and tests the active key. Any change/failure irreversibly fences
that process until restart, including already-created protectors. Warnings start
seven days before active expiry or two hours before lease expiry. Health output
contains only partition, generation, admission hash, counts and deadlines, never
XML, provider exceptions or protected values.

There is no cross-process invalidation claim: checking one replica does not
refresh another. Before changing a pin, active key or revocation, stop/drain every
old replica; route traffic only after every replacement reports the exact expected
generation/hash. Health checks must actually be scheduled by deployment readiness.
A process not probed can retain its snapshot until lease expiry, so emergency
revocation requires an explicit full drain, not merely publishing new material.

### Protected purpose and lifetime inventory

The conservative inventory below is about retained ciphertext, not key expiry.
No expiration date authorizes deleting a key. Existing data, outbox items and
restorable backups must all be included in any later separately approved retirement.

| Owner / purpose | Protection lifetime and retention consequence |
| --- | --- |
| `EventResourceDestinationProtector`: `EventResource / ExternalDestination / v1 / tenant / resource` | No cryptographic expiry; retain indefinitely while any destination or backup needs it. |
| `RegistrationSensitiveValueProtector`: `Explore.RegistrationSensitiveAnswerValue / v1` | No cryptographic expiry; sensitive answer rows and backups remain dependent. |
| `EventReportEvidenceProtector`: `ISLAMU.EventReporting.Evidence.v1` | No cryptographic expiry; retained reports/evidence and backups remain dependent. |
| `RegistrationProviderCallbackReceiptProtector`: `Explore.RegistrationProviderCallbackReceipt / v1` | No DP expiry; receipt/application validation is separate. Conservatively retain. |
| `AdmissionDeliveryEnvelopeProtector`, `AdmissionRecoveryDeliveryEnvelopeProtector`, `AdmissionRecoveryRequestEnvelopeProtector` | Versioned credential/contact/recovery purposes; delivery disclosure/deadline checks and durable request/outbox retention are application-owned, not DP expiry. |
| `ConfigurationImportArtifactStore`, `ConfigurationDirectTransferChunkRepository` | `ISLAMU.Event.ConfigurationImportArtifact.v1` / `ISLAMU.Event.ConfigurationDirectTransferChunk.v1`; stored expiry and cleanup are application-owned; backup copies remain relevant. |
| `EventDiscoveryCursorProtector`: `Explore.EventDiscoveryContinuation / v1` | Cursor envelope expiry/current-policy validation is outside raw DP; replicas must share the backend partition. |
| `EventResourceCursorProtector`: `EventResource.AudienceCursor.v1` | Application-enforced 15-minute envelope lifetime. |
| `IdempotencyMiddleware`: `Explore.API.IdempotencyReplay` | Stored response receipt expires normally after 24 hours (shorter recovery bounds where supplied); DP is not the receipt/reopen authority. |
| `AnonymousRegistrationChallengeService`: `Explore.AnonymousRegistrationChallenge.v1` | 120-second challenge with a separate 24-hour recovery allowance; not a key-retirement clock. |
| `DataProtectionAddressSelectionProtector`: `ISLAMU.Geocoding.AddressSelection / v1 / purpose / tenant / configuration fingerprint` | Application expiry from `SelectionLifetimeSeconds`. |
| `EmailUnsubscribeTokenService`: `ISLAMU.Email.Unsubscribe.v1` | Time-limited DP, 180-day default with caller-selected lifetime. |
| `EmailDeliveryDisableTokenService`: `ISLAMU.Email.DeliveryDisable.v1` | Time-limited DP, five minutes, bound to the actor and impact snapshot. |
| `PrivacyErasureProviderLocatorProtector`: `ISLAMU.PrivacyErasure.ProviderLocator.v1` | Caller-selected time-limited DP lifetime, not a universal upper retention bound. |
| BFF authentication cookies, OIDC state, antiforgery, Blazor circuit state | Framework scheme/purpose isolation; cookie default is 12 hours with sliding renewal; protocol settings own other lifetimes. Not all framework state has a fixed cryptographic expiry. |
| `LocalIdentityLifecycleTokenProviders` in API/Combined | Distinct Identity verification/recovery providers validate token lifespans of 30/15 minutes; default Identity providers are also registered. |
| `BffSelfCallTokenService`, `SetupSecretCookieProtector`, `LocalCredentialChallengeCookie` | Distinct versioned BFF purposes; time-limited DP: 2 minutes, 30 minutes and at most 5 minutes respectively. |
| `RegistrationPaymentCheckoutTicketStore` | `registration-payment-checkout-cookie-v2`; application expiry is 5 minutes. |
| `ApiBackedOAuthStateStore`, `AtprotoBrowserProof`, `AtprotoTenantSessionHandoffStore` | `oauth-state-v2`, origin-bound proof `v1`, `tenant-handoff-v2`; browser proof 15 minutes with 2-minute handoff budget, state/handoff expiry also checked against API-owned records. |

Combined contains both backend and BFF purposes within one process trust boundary.
Split BFF cannot decrypt API data even if a key is accidentally copied because its
discriminator differs. API/Combined changes require operator-provisioned matching
material, not application fallback to each other's roots.

The native tool is `eng/security/Event.DataProtection.Operator`. Operational
provisioning, overlap, restore and independently protected floor procedures are
in [the public secrets runbook](../public/documentation/readme/configuration-and-operations/secrets.md#external-data-protection-rings).
No live authority, replica or complete historical inventory was provisioned by
authoring these source changes.

## Keycloak Runtime Credential Ownership

The Keycloak BFF client secret is deployment-owned runtime material. Event
resolves it through `ISecretResolver` from exactly one selected authority and
never stores, rotates, returns or logs it. A reviewed create receipt records
only a value-free binding generation; it does not persist plaintext, provider
coordinates, or a secret-derived hash. If that generation changes before
apply, the plan is rejected before Keycloak administrator authentication or
provider mutation.

The resolved secret may seed a proven-absent confidential BFF client exactly
once. Bearer-only API client payloads omit `secret`. Existing-client secret
endpoints are outside application authority. Rotation is coordinated externally:
update Keycloak and the selected environment/Infisical authority, restart all
affected replicas, run read-only inspection, then verify a fresh user sign-in.
Event provides this guidance but performs no live rotation or fallback copy.

## Request-Scoped Keycloak Administration

Current operator routes use native inspection/plan/apply/reconcile operations,
not the retired bootstrap/doctor/rotation DTOs. Server-derived operator
authority and the resolved deployment binding select the target. Administrative
plan/apply/reconcile require a fresh complete username/password pair; no pair
is loaded from settings, receipts or background work. Public-only inspection
without a pair cannot perform administrator reads or writes. Partial pairs
are rejected by manually constructed validators.

Apply contexts preserve the supplied credential strings exactly instead of
trimming a provider password or username. Bearer authentication belongs to
each request message, never shared default headers. The typed administrator
client uses connection-time DNS vetting with original TLS hostname validation;
it permits operator-owned private Keycloak targets selected by the server,
but rejects metadata targets and disables proxies, redirects and cookies.

`KeycloakOperatorPanel` clears both fields and Apply confirmation after every
terminal action, navigation and disposal. It retains credential-free receipts
for ambiguous external completion: refresh or reconcile with a new pair rather
than retrying an unconfirmed provider mutation or claiming remote rollback.
Runtime BFF secret rotation remains deployment-owned as described above.

## Dedicated Keycloak Lifecycle Sender

`KeycloakAccountAuthorityLifecycleEmailService` uses same-realm
`client_credentials`, never administrator password grant, `admin-cli`, the BFF
client secret or credentials retained from an operator request. Its fixed
instance-only registry key is `keycloak.lifecycle_client_secret`, environment
alias `KEYCLOAK_LIFECYCLE_CLIENT_SECRET`, with Infisical authority rooted at
`/runtime/api/keycloak/lifecycle`. Resolution has no source or tenant fallback.

`KeycloakLifecycleGrantProfile` pins the deployment policy to Keycloak 26.1.2.
The dedicated confidential client must disable browser, implicit and password
flows and have only the non-composite `realm-management/manage-users` mapping,
with no realm roles or other client grants. The offline realm-export validator
in `eng/tools/keycloak-lifecycle-grants.cs` rejects extra or incompatible
authority without printing exported credentials. This is the minimum available
role for that provider version; it is broader than email actions, so isolation
of the dedicated service credential remains consequential.

Fresh token-response admission checks exact issuer/client/service-account
identity, expiry and the complete effective role set before fixed lifecycle
handoff. It is a permission check on the transport-authenticated OAuth response,
not a JWT authentication API for caller-supplied tokens. Token responses are
bounded to 64 KiB and values never enter audit receipts or logs. The sender's
typed transport preserves TLS identity and blocks metadata/proxies/redirects.
HTTP requires the explicit local-URL option; production should retain HTTPS.

Native authority regression uses the pinned real provider: the minimal grant
reaches lifecycle endpoint validation without sending mail, while removal
returns `403`. Source fixtures separately cover positive audit/delegation and
rejected foreign/missing/excess grants. Execution remains part of the final
programme gate; these are authored tests, not passing receipts.

## Authentication Configuration Is Not Credential Custody

`AuthProviderConfigurationDto` returns nonsecret provider configuration and
deployment-owned credential metadata only. It has no Google or Keycloak
client-secret value member. Google ownership comes from the instance-only
`ISecretResolver` outcome; Keycloak ownership uses the existing bounded connection
resolution. `ResolutionStatus` distinguishes resolved, unconfigured, unavailable,
unauthorized and invalid outcomes without provider diagnostics or values. Other
ownership capabilities may omit this optional status.

Ordinary authentication PATCH cannot accept a Google credential. Its write
contract rejects unmapped members and the configuration service no longer writes
`auth.google_client_secret` into SystemSetting. The retired internal credential
readback endpoint and service method are removed; onboarding HAL points to the
public metadata endpoint. The BFF reads that metadata at startup and refresh,
using only its own selected-authority credential projection. It has no
last-known-secret fallback or setup-secret-triggered credential reader.

Runtime Google, Keycloak BFF and ATProto frontend bindings must use the registry's
exact instance coordinates and selected environment. Admission happens before
the credential cache or source dispatch, so a mutated reference cannot recover
an older cached value. This does not delete legacy settings/bindings: removal
requires the maintenance cutover and exact cleanup transaction. A resolved
credential proves acquisition, not a successful user sign-in or completed
multi-process convergence.

## Local Identity Signing Authority

Local access-token and credential-replacement challenge issuance and validation
use the same selected deployment `Authentication:Local:JwtKey` authority
(`AUTHENTICATION_LOCAL_JWT_KEY` alias). Both paths require Base64 material of at
least 256 bits. An explicitly blank canonical declaration refuses signing rather
than borrowing the alias. `LocalJwtTokenGenerator` does not read database secret
bindings: its selected identity-writing context must not gain private
`SecretBinding` access merely to sign a response. This does not change generic
`SecretResolver` binding precedence or authorize runtime database key rotation.

The original identity-synchronization context reads only bootstrap status, mode,
provider kind and completing-user ID, ordered by generation, creation time and
row ID. `IIdentitySynchronizationBootstrapReader` projects these facts without
materializing configured-administrator fingerprints. Identity correlation checks
erasure presence through `ExistsBySubjectAsync`, selecting only `SubjectId`, not
the saga's receipt, lifecycle state or fence payload. The Local Identity writer
inventory grants exactly these seven bootstrap columns and the erasure subject
column for SELECT, with no DML on either source. A stolen writer credential can
enumerate this metadata and erased-subject IDs; it still cannot read those
private payloads. Existing partial profiles fail readiness rather than receiving
an inferred union of grants; use a freshly provisioned development profile.

## Instance Onboarding Keys

The eight `INSTANCE_BOOTSTRAP_*` keys follow the same single-authority rule as
every other value here. They come from the deployment environment or from the
one selected secret authority, never from source defaults, appsettings
fallbacks, or a second provider. A missing or unreadable key fails startup
closed.

The subject, DID, issuer pairing and generation are selectors, not authentication
proof. External identities still require a real sign-in with the exact provider
claim. Configured Local bootstrap additionally resolves
`authentication.local.bootstrap_password` from
`INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` (Infisical `/api/bootstrap` or `/api`); it is instance-only,
bootstrap-classified and has no live-rotation path or source default.

That secret creates only the initial temporary credential during incomplete
setup. The native receipt coordinates Identity creation with application
linkage, administrator grants and setup completion. Private first-use
replacement and a fresh login are required before ordinary session authority.
Completed-state reconciliation does not reread or replay leftover bootstrap
passwords; use normal credential administration rather than editing this secret
to reset an established account.

The opt-in Development-only `AgentBrowser` profile resolves
`authentication.local.agent_browser_persona_password` from
`AGENT_BROWSER_PERSONA_PASSWORD` through the same selected authority (Infisical
`/api`, Environment, or Development User Secrets). The instance-scoped,
bootstrap-classified value is required only while a persona's native first-use
replacement is unfinished. The configured administrator additionally needs
`INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` until its own replacement completes, and
the Local JWT signing authority remains required on every launch. Once all
personas are complete, startup does not reread initialization passwords or
restore changed credentials. Production Setup neither activates nor exports
the agent-only credential.

The agent profile's dedicated Redis container also resolves
`redis.agent_browser_password` (`AGENT_BROWSER_REDIS_PASSWORD`) from the selected
authority's `/api` Infisical folder, explicit environment injection, or
Development User Secrets. It is a separate instance-scoped secret, not a
derivative of the persona or database password, and has no source default.

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
      "Paths": ["/smtp"]
    }
  }
}
```

### Local Development Environment

Copy `.env.example` to the ignored repository-root `.env`. Select exactly one
`SECRET_PROVIDER`: `Environment`, `Infisical`, or `UserSecrets`. Environment mode
reads the documented process variables. Infisical mode uses `INFISICAL_*` process
environment variables or, in Development and Testing environments, can read bootstrap
Universal Auth credentials (`Infisical:Url`, `Infisical:ProjectId`, `Infisical:ClientId`,
`Infisical:ClientSecret`, `Infisical:Environment`) directly from the shared User Secrets store
owned by `src/Explore.Secrets/Explore.Secrets.csproj`. In Development/Testing, if `SECRET_PROVIDER`
is omitted from process variables or `.env`, it also falls back to `SECRET_PROVIDER` configured in
User Secrets. User Secrets mode reads all application secrets directly from the shared store
through your IDE or the `dotnet user-secrets --project src/Explore.Secrets` CLI.
Both User Secrets mode and Infisical bootstrap via User Secrets are rejected unless the
host environment is Development or Testing. In Production, Infisical bootstrap credentials
must strictly come from process environment variables.

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
> The contributor default Aspire profile is `local-full`. It starts local infrastructure but never changes the explicitly selected secret provider.
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

For an Infisical-backed Aspire profile, select `Infisical` in `.env`, provide
`INFISICAL_URL`, `INFISICAL_PROJECT_ID` and `INFISICAL_ENV`, and independently
provide the API/BFF or Combined pair plus the migration pair documented in
`.env.example`. Direct-process `INFISICAL_CLIENT_ID/CLIENT_SECRET` are not
AppHost projection inputs.
For Environment mode, leave every `INFISICAL_*` value blank and provide each
required consumer variable directly.

If startup reports `secret_authority_unauthorized`, `secret_authority_invalid`, or
`secret_authority_unavailable`, repair the selected deployment authority and restart
the affected host. Do not print provider responses, run commands that echo secret
values, or add a lower-source value to make startup appear healthy. A mode change is
an explicit deployment cutover and requires a full restart of the affected replicas.

| Secret authority | Direct Standalone | Aspire Standalone | Aspire Split | Single-replica Compose | Multi-replica Split |
|---|---|---|---|---|---|
| `Environment` | Supported with explicit process injection | Explicit selection in every local profile | Explicit selection in every local profile | Supported through the explicit Compose allow-list | Supported only when the deployment injects one consistent value set and one explicit shared setup secret into every replica |
| `UserSecrets` | Development/Testing only | Supported for local Development profiles | Supported for local Development profiles | Rejected | Rejected |
| `Infisical` | Inject the Combined identity into direct-process bootstrap fields | Separate Combined and migration identities; local resources need operator injection | Separate API, BFF and migration identities; local resources need operator injection | Separate API, BFF and migration identities | Replicas share their consumer's authority, never another consumer's permissions |
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
| `DATABASE_GLOBAL_USERNAME`, `DATABASE_GLOBAL_PASSWORD` | `Database:Global:Username`, `Database:Global:Password` | API-owned bounded cross-tenant capabilities only |
| `DATABASE_INSTANCE_KEY_READ_USERNAME/PASSWORD` | `Database:InstanceKeyRead:Username/Password` | API instance-key metadata read pool |
| `DATABASE_INSTANCE_KEY_WRITE_USERNAME/PASSWORD` | `Database:InstanceKeyWrite:Username/Password` | API fixed instance-key policy/revocation pool |
| `DATABASE_INSTANCE_KEY_ISSUANCE_USERNAME/PASSWORD` | `Database:InstanceKeyIssuance:Username/Password` | API self-owned issuance/metadata-recovery pool |
| `DATABASE_PLATFORM_KEY_REPORT_USERNAME/PASSWORD` | `Database:PlatformKeyReport:Username/Password` | API historical aggregate report pool |
| `DATABASE_MIGRATOR_USERNAME`, `DATABASE_MIGRATOR_PASSWORD` | `Database:Migrator:Username`, `Database:Migrator:Password` | `Event.MigrationService` only |

Global credentials are explicit startup-owned inputs, available from the selected
authority as flat keys or structured `Database:Global:*` keys. Infisical stores
them under `/database`; the username is public configuration and the password
is secret. `BindGlobal` requires its own username/password for server databases:
it never substitutes Runtime, Migrator, or root `Database:Username/Password`.
Shared endpoint/schema metadata does not imply shared credentials.

The four instance-key authorities are separate from Global and from each other.
Their structured values and explicit environment aliases bind without another
role's credential fallback; explicit empty values remain unavailable. Backend
projection masks unselected aliases, and Compose forwards these pairs only to
the API database environment anchor. Infisical uses the exact `/database` root;
BFF consumers receive none of these credentials.

`InstanceApiKeyDbContextFactories` checks the common database/namespace/TLS
target and distinct server logins/passwords before creating four independent
pools. Startup admission opens every purpose and verifies ordinary-role RLS
readiness, execute-only access, expected function owner/body/search path, no
PUBLIC execution and no direct private-table/column access. Missing capabilities
fail the advertised profile; no request repairs or installs them.

Migration-owned installers create `event_instance_key_read_executor`,
`event_instance_key_write_executor`, `event_instance_key_issuance_executor` and
`event_platform_key_report_executor`. Provision each configured login with only
its corresponding executor membership (inheritance enabled, role switching
disabled); never grant owner/migrator membership or combine these executor
memberships. Read metadata is hash/IP-redacted. Management admits only fixed
policy/revocation columns, issuance inserts a self-owned key and digest-only
receipt, and reporting returns aggregates including inactive historical tenants
and null-tenant keys. Authentication/scanner/Global rights remain unchanged.
The source and bindings are authored; native provider tests, provisioning
execution and generated catalogue refresh remain deferred to final assurance.

Persisted machine footer link/group PATCH uses the existing Runtime login and
connection, not a fifth API-key management pool. The migration-owned
`PostgresMachineMutationAuthorityContract` installs
`event_machine_footer_owner`, `event_machine_footer_migrator` and
`event_machine_footer_runtime`. Provision only the last role to the ordinary
Runtime login, with inheritance enabled and role switching disabled. Never
grant its owner/migrator roles to a consumer or its executor to Global,
authentication, scanner, or any of the four key-management/report logins.
No new secret or environment key is required.

That executor exposes only the fixed
`machine_footer_patch_authority` function for an exact tenant, group and
optional link in the caller's Serializable transaction. Its non-login owner
has minimal key identity/status/expiry/scope and owner/target-fence columns;
no secret/hash, usage, generic setting or enumeration capability is granted.
Admission rejects extra frozen-purpose role membership/function access,
altered function identity/body/search path/owner, PUBLIC execution and direct
executor key-table access. Missing execution or failed readiness denies the
mutation; the API never installs or repairs privileges. Apply through the
migration service and provision the Runtime membership before enabling these
machine operations. Tests and provisioning execution are still unrun.

Room updates have a separate same-Runtime purpose:
`event_machine_room_owner`, `event_machine_room_migrator` and
`event_machine_room_runtime`, installed by
`PostgresMachineRoomMutationAuthorityContract`. Provision only
`event_machine_room_runtime` to the ordinary Runtime login with inheritance
enabled and role switching disabled. Its fixed
`machine_room_update_authority` function accepts an exact room and optional
destination, derives the stored source, and validates current persisted-key
and owner authority after room/location contention. Its minimal columns do
not include credentials or usage. Do not grant its executor to a frozen
Global/authentication/scanner/key-management/report profile, grant it table
rights, or make it inherit footer execution. A Runtime login may receive both
distinct executor memberships when both operations are enabled; neither
executor role may borrow the other's function. No environment key or pool
is added. Provisioning and native execution remain unrun.

Global uses an independent login and connection pool for bounded capabilities
such as public tenant discovery. It is not a universal `BYPASSRLS` authority:
PostgreSQL Global must remain `NOSUPERUSER NOBYPASSRLS` with only the specific
capability grants needed. DDL and capability installation remain migration-only.
Rotate credentials through the selected authority and restart the consuming
API processes so their pools are rebuilt; never promote Runtime to Global or
inject Migrator credentials into API processes.

PostgreSQL request and Global pools inspect effective role privileges and
model-derived policy coverage before binding a connection. The migrator stamps
each tenant policy with a v2 source-condition fingerprint and digests of the stored
USING/CHECK expression trees. Deparsed SQL is not used because PostgreSQL renders
relation names differently under different connection search paths. Runtime
compares those catalog values without issuing DDL;
missing, altered, or extra applicable permissive policies reject acquisition.
Apply the migration-owned policy installer to replace older stamps before restarting consumers. A failed
admission cannot be caught and followed by EF commands on that same connection.

Initial tenant resolution performs preliminary configuration, slug/domain and
configured-bootstrap directory reads in an independently disposed service scope.
Those readers retain their selected Runtime authority and existing bounded
public lookup capability; the scope closes before the request tenant is bound
or authentication is forwarded. Resolved, exempt and single-tenant paths do not
instantiate those preliminary repositories. This prevents a routing dependency
from freezing the ordinary request lease at a null tenant.

Aspire local PostgreSQL resources supply their parent login only to migration
work. API and Standalone receive separately selected Runtime and Global
credentials through secret parameters; Global projection does not overwrite
Runtime database metadata. Provision those logins and their bounded capability
grants under migration authority before consumers start.

PDS settlement uses `IPdsSettlementDbContextFactory` to capture the persisted
claim tenant before exposing a Global lease. Its private claim, ownership,
presentation and Event writes remain constrained by the ordinary tenant policy;
shared record and disclosure epochs commit in the same Serializable transaction.
The migration-owned `event_pds_settlement_global` group enumerates its required
column/table grants and inherits only the bounded disclosure executor. Grant
that group to the selected Global login, never the default Runtime login.
Lookup acquisition through `IGlobalExploreDbContextFactory` remains sealed-null.

Moderation compensation additionally requires `event_atproto_moderation_global`
on the selected Global login. Grant only this execute group, not
`event_atproto_moderation_owner`, `event_atproto_moderation_migrator`, private
table access or role-switch authority. Its fixed reads/writes join the handler's
Serializable transaction. Installation rejects unexpected owner members,
execute-group parent memberships and ownership of protected relations by either
the owner or execute group.

The operations join the same
Serializable transaction, bind the suspended subject and exact publication
lineage, and expose policy/session metadata without credential envelopes.
Ordinary planning and delivery gates use `IAtprotoSessionBindingReader` on the
already sealed exact Runtime lease. Only the transport reads/decrypts credentials.

PDS queue admission separately acquires a sealed-null Global lease through
`IPdsSyncQueueAdmissionDbContextFactory`. Grant `event_pds_admission_global`
to that selected Global login, with inheritance enabled and role switching
disabled. Its fixed function atomically admits at most 100 due or expired
claims in creation-time/ID order, checks record dependencies and returns only
outbox, tenant, user, lease-token and fence identifiers. The non-login owner has
only eligibility-column reads and fixed lease/status writes; neither payload
nor session material is read. Global never receives generic queue-table access
or owner/migrator membership. The drain binds each claim's exact scope-local
tenant before resolving its ordinary Runtime delivery graph; settlement retains
its separate purpose acquisition and transaction.

IntegrationSync enumeration also uses that sealed-null Global pool, through
`IIntegrationSyncQueueDiscovery`. The migration-owned
`event_integration_sync_discovery_runtime` group can execute a bounded function
returning tenant and outbox IDs only; it has no queue-table read or claim-write
grant. Grant it to the selected Global login, with inheritance enabled and role
switching disabled, never to Runtime or to either owner/migrator group. The drain
binds each discovered tenant before resolving the Runtime repository for due-row
reload, claim, recovery or execution. Subscriber payloads and durable handoff
tokens remain confined to those exact tenant leases.

Email admission uses `IEmailDispatchAdmissionDbContextFactory` for a sealed-null
Global lease and the migration-owned `event_email_admission_global` executor.
Grant only that executor to the selected Global login, with inheritance enabled
and role switching disabled. Fixed functions serialize admission and stale
recovery under the existing claim gate; they preserve global/per-tenant limits,
priority rounds and reminder backlog hysteresis without returning message
content. `EmailDispatchAdmission` closes Global before hydrating ownership IDs
through exact Runtime leases, rechecking status, token and admission time.
Cancellation after admission leaves a durable recoverable claim. Recovery keeps
SMTP in-doubt work Unknown across outbox, attempt, receipt and delivery ledgers.

ATProto audience and recovery-policy resolution use
`IAtprotoFederationGlobalReadRepository` on a separate sealed-null Global lease.
EF reads the dedicated instance and tenant federation-setting entities; the
complete tenant override audience is loaded in one query, not a per-tenant loop.
Only event enablement, backfill enablement and backfill mode belong in these
tables. Validation profiles, publication consent and private generic settings
remain separate. Instance locks, deployment mode and backfill precedence remain
Application decisions. Scoped reads retain the caller's context and transaction;
the independent Global reader cannot borrow an existing caller transaction.

`PostgresAtprotoFederationSettingPermissions` installs a fixed, model-checked
column privilege manifest. Runtime and Global read the dedicated columns;
tenant writes remain tenant-constrained, and only Global can insert or update
instance settings. The separate Global SELECT policy permits complete tenant
override discovery without granting private setting-table reads or bypassing RLS.
Pre/post admission rechecks the configured login, original physical connector,
transaction, model, live privileges and stamped policies. Unexpected grants,
owner-role reachability or altered policies fail closed rather than being repaired.

Do not grant or restore `event_atproto_governance_runtime`. Installation and read
admission require the retired governance owner, migrator and runtime roles, the
mapped former governance routine and its `atproto_governance_overrides` policy
to be absent. PostgreSQL roles are cluster-wide, so merely creating another
database on the same old cluster does not establish a clean transition. These
checks inspect catalogs only: startup never drops roles, routines, policies or
application data. See [the development transition](BACKUP_RESTORE_UPGRADE.md)
before applying the generated schema changes to any retained database.

Pre-tenant API-key and scanner authentication readers acquire independent
sealed-null Global leases. The Migrator installs fixed column permissions through
`event_api_key_authentication_global` and `event_scanner_authentication_global`,
inherited only by the declared Global login with role switching disabled. The
API-key profile reads its 20 mapped credential/metadata columns, including the
one-way secret hash, and updates only `LastUsedAt` and `LastUsedIp`. The scanner
profile reads its 16 mapped columns, including lookup digest/version, with no
writes. No table-wide grant, Runtime authentication-profile grant, raw key or
HMAC-key read, credential-management permission or identity-write authority is
added. Every trusted service holding Global can enumerate these fixed columns
directly; exact lookup is a C# restriction, not a database guarantee against
credential theft. Protect and rotate Global accordingly.

Ordinary tenant-key storage is a separate fixed Runtime descriptor: metadata
SELECT18 excludes `SecretHash` and `LastUsedIp`; INSERT20 supports EF's complete
new-key image; UPDATE7 covers `Id` fencing, name/scopes/expiry/status and update
audit fields. Digest-only issuance receipts have SELECT/INSERT6, with no DELETE.
Supporting global lookups expose only credit-period/owner-type `Id` and status
`Id`/`IsUsable`; status `UPDATE(Id)` supports the existing replay row fence.
Their descriptive text remains unreadable, and no tenant policy is invented
for these global reference tables. The fixed manifest verifies their complete
physical shape and rejects extra columns/grants without repairing them.
The parent-existence check has only Runtime `Tenant.Id` SELECT; it receives no
tenant descriptive/status/audit columns, tenant writes or `User`/PII reads.
Tenant roots remain non-RLS, so this fixed read exposes IDs including inactive
roots. Key/receipt tenant isolation still requires the exact selected nonnull
tenant. Source catalogues recognize only this mapped direct Migrator-origin
parent-key grant; no inherited role or broader source inventory is admitted.
Dormant policies for unrelated retained owners can coexist on the non-RLS
Tenant root; Runtime/PUBLIC-applicable policies are rejected without repair.
A Runtime-only restrictive policy intersects the existing nullable-tenant policy
and requires an exact nonnull session tenant on keys and receipts; it does not
open Global access. Metadata entities are projected without fetching private
fields, and management persists only intended policy/status changes, never the
redacted hash/IP placeholders. API key DTOs retain `LastUsedAt` and have no
`LastUsedIp` field. Authentication admission recognizes only this compiled storage
manifest; unexpected columns, private reads, widened grants or policy drift are
refused, not repaired. C# still authorizes owner selection, issuance and receipt
recovery. A stolen Runtime database credential can choose a tenant session and
insert credential hashes or alter permitted key policy/status outside those API
checks; its status-key UPDATE also permits direct status lookup key tampering
subject to database constraints, not edits to usability or descriptive fields.
Treat and rotate Runtime as sensitive application write authority, not
as an independently business-authorized principal. A stolen Runtime credential
can enumerate tenant IDs, not their descriptive or private user data, through
this parent-key permission.

Scanner management shares that fixed storage installer, not the Global
authentication role. Its Runtime descriptor declares SELECT/INSERT for all 16
mapped scanner fields and UPDATE only for `Id` (the check-in row fence), the
three revocation fields and `ConcurrencyStamp`. Existing repository entity reads
include the lookup digest/version; neither is the raw scanner token or its HMAC
key. The restrictive nonnull-tenant policy also covers scanners, so bypassing
EF's tenant filter does not reveal another tenant's rows. Revocation persists
only its declared fields and original concurrency stamp, including detached
entities; it never rewrites the credential or issuance identity. Authentication
readiness verifies the exact storage manifest instead of forbidding legitimate
Runtime writes. Broad grants, grant options and digest UPDATE remain refused.
The transport supplies the authenticated platform User ID, not an Actor ID.
The Application service resolves its tenant-associated personal Actor through
the fixed ownership reader inside the original issue/revoke transaction before
persisting either actor foreign key. Ownership facts do not grant authorization;
the existing event-management authorization remains tied to the platform User.
Issuance and EF tenant-reference validation additionally read only event
`Id`/`TenantId`/`IsDeleted` and participation-configuration
`Id`/`TenantId`/`IsDeleted`/`ParticipationHandlingModeId`. These declared reference
intersections confer no event content reads or event/configuration writes; the
retained private-read owners recognize precisely these columns, not live ACLs.
The four instance-key machine purposes retain their separate scanner-access
denial. A stolen Runtime credential can select tenant sessions, read their
scanner digests, insert scanners and revoke or fence them outside C# workflow
authorization; this is the already documented application-credential threat
boundary, not independent database business authority.

Admission target storage has a separate fixed Runtime inventory: SELECT and
INSERT for its nine mapped identity, scope, status and concurrency columns, and
UPDATE only for `AdmissionOperationalStatusId` and `ConcurrencyStamp`. It grants
neither target-key UPDATE nor private event-content access. C# target creation,
scanner issuance, check-in and stop workflows retain authorization and domain
rules. `AdmissionTargetTransaction` acquires cooperative tenant/target
coordination before opening the original single-attempt Serializable transaction,
so a waiting workflow establishes its snapshot after the previous owner commits.
Repositories and the unit of work check the original context, tenant, target,
connector and physical transaction. No privileged target-lock routine is needed.
SQLite additionally takes its existing database writer reservation; its named
coordination is process-local and assumes the documented single-instance topology.
Direct database edits can bypass this cooperative protocol, including target
creation or status changes with a stolen Runtime credential. A known acknowledged
commit followed by cleanup failure is not reclassified as an unknown commit and
is never automatically replayed; genuine settlement uncertainty remains unknown.

EF queries retain exact ordinal key matching and paired scanner digest/version
candidates; multiple scanner matches fail as ambiguity. Inactive/expired key rows
remain visible to the authentication handler's persisted-credential precedence.
Usage updates retain their strict interval compare-and-set. Without a caller
transaction, pre-commit work uses the configured execution strategy and retains
the original connector; one COMMIT and disposal run outside that retry delegate.
An existing caller transaction remains caller-owned. A commit-acknowledgement or
cleanup failure is propagated with its original causes, not converted to a false
update result or a claim of rollback.

The former authentication owner/migrator roles and three lookup/usage routines
must be absent. Installation and readiness reject retained artifacts or privilege
drift without dropping or repairing them. Use an explicitly disposable fresh
development cluster or a separately reviewed retained-data transition; do not
delete retained data merely to pass readiness.

Membership discovery continues to use `event_user_membership_global` with
inheritance enabled and role switching disabled; its owner/migrator groups and
direct private-table grants remain unavailable to Runtime and Global. It returns the complete
active membership/Tenant/TenantStatus graph for one server-resolved user; checking
a chosen target tenant uses an independent exact Runtime lease instead.
Standalone external-login lookup uses the exact provider/account pair through
EF. `PostgresExternalLoginReadPermissions` grants the declared Global login
SELECT on the mapped UserExternalLogin properties Id, UserId,
AuthenticationProviderId, ProviderKey, ProviderDisplayName, CreatedAt, CreatedBy,
UpdatedAt and UpdatedBy. It grants no table-wide SELECT, writes or grant options.
Runtime remains denied. The required repository enrollment comes from the
existing selected deployment configuration; PostgreSQL rejects missing enrollment
or another session even if it has copied column permissions. The reader and
installer reject excess privileges, including system columns and the full
transitive role-membership graph. Only the Global login itself may hold binding
read privileges within that graph; another member role's binding privileges are
rejected even when INHERIT and SET are disabled. This also closes ADMIN OPTION
delegation through direct or nested membership. Transactional identity
synchronization retains its same-context reader.

All consumers sharing the Global credential can query those nine binding
columns; factory aliases do not isolate them at the database level. No new secret
authority or connection credential is introduced. The installer accepts only the
declared Migrator session. It rejects the obsolete external-login function or any
of its three legacy roles with `external_login_requires_fresh_development_database`.
Use a fresh isolated development PostgreSQL instance/cluster containing none of
those obsolete capability roles, then provision its database and declared
accounts. Roles are cluster-wide: creating only a new database in the old cluster
does not remove them and cannot satisfy admission while they remain. There is
no automatic object cleanup, data deletion or in-place compatibility path.
Claims enrichment resolves authority directly through
`IAdministrativeAuthorityRepository`, without constructing the ordinary
`IAdminContext` before post-authentication tenant binding.

Reference catalogue initialization uses the existing Runtime credential, not
Global. `PostgresLookupCatalogueReadPermissions` installs direct, non-grantable
column SELECT for seventeen fixed EF entities. Every type exposes Id, MasterCode,
FullName and Description; EventType additionally exposes TenantId, AudienceAge
exposes MinAge/MaxAge, and Role exposes RoleScopeId/IsSystem. The remaining types
are EventFormat, EventStatus, AudienceGender, Madhab, Language, VisibilityType,
ApprovalStatus, RegistrationMode, OrganizationPosition, ActorType, DidCustodyType,
FileType, TagType and OwnerType. Future mapped properties do not silently join
this permission inventory: closed-model admission rejects shape drift.

The catalogue reader requires an identity-free, tenantless Runtime context,
original Serializable transaction and unchanged connection. It rechecks tenant
policy readiness before EF materialization. Column permission checks reject
table-wide access, writes, system columns, grant options and transitive membership
paths, including dormant ADMIN delegation. Runtime membership with Global or either
email account is forbidden in both directions. Global retains no catalogue-source
privileges. Email policy independently owns the email accounts' Role.Id,
RoleScopeId and MasterCode projections; catalogue admission does not attest those
grants. Runtime cannot borrow either email mutation role, and email sessions cannot
execute the Runtime catalogue reader. RolePermission and UserPii remain private.
Existing actor/conversation guards recognize only the six
declared direct Runtime Role columns; they do not relax private-source authority.

The obsolete `global_lookup_catalogue` function, `event_global_lookup_owner`,
`event_global_lookup_migrator`, `event_global_lookup_runtime` roles and dedicated
EventType catalogue policies are not installed. Their presence fails with
`global_lookup_requires_fresh_development_database`. As with external-login
discovery, use a fresh isolated development PostgreSQL instance/cluster without
those roles and provision the declared accounts there. No automatic cleanup,
permission repair, compatibility path or new secret is introduced.

Grant `event_admin_authority_global` to Global with inheritance enabled and role
switching disabled. Its four fixed-search-path functions return only an exact
user's platform-administrator Boolean and tenant/organization/group scope IDs.
They retain revoked, inactive and soft-delete eligibility checks without adding
tenant lifecycle or participation-approval restrictions. Optional tenant
selection is explicit, not taken from the request lease. Global cannot read the
underlying private grant/membership tables or inherit the owner/migrator groups.
`AdminContext` uses this reader for complete enumeration, while scoped checks,
transactional external-login binding and platform-assignment convergence retain
their existing owning repositories.

Verified ATProto actor consolidation retains the onboarding transaction and
its already-bound request lease. Grant `event_actor_consolidation_runtime`
only to the selected backend login that performs that operation, with
inheritance enabled and role switching disabled. Unlike independently acquired
discovery readers, this exact actor-pair function must execute on the owning
transaction: acquiring another pool would split reference moves from source
retirement and disclosure epochs. The function moves only identity/event/
series/speaker/subscription actor references, checks cross-tenant collisions,
and grants no direct cross-tenant table DML. The caller still verifies DID
evidence and current target management authority. Do not grant the owner or
migrator groups to Runtime.

SQLite has no database credentials. Its `DATABASE_NAME` value is a
persisted local file path and is deployment configuration, not a secret. Do not
give runtime services the migrator role, and never expose any database role to the
Blazor client.

Managed tenant domain collision checks use `IManagedTenantDomainRepository`
on an independent Global lease. Grant `event_managed_domain_global` to Global
with inheritance enabled and role switching disabled. The function returns at
most one exact-host match from the two domain-setting keys, preserving JSON
string encoding, case and trailing-dot matching; it never exposes arbitrary
tenant settings. Domain mutation/namespace locking stays with its existing
owner.

Notification fanout admission and health require `event_fanout_admission_global`
on Global, not Runtime, with inheritance enabled and role switching disabled.
Admission preserves global/per-tenant capacity, priority/fairness, reminder
hysteresis and the existing global/tenant/event/occurrence advisory lock keys.
Global results contain fenced ledger identities, not recipient cursors or
occurrence snapshots. `NotificationFanoutAdmission` hydrates those identities
through separate exact Runtime leases, then the processor binds each new page
scope to its persisted claim tenant before resolving repositories. Cancellation
after admission leaves expiring recoverable claims without starting recipient
materialization. Health exposes fixed aggregate counts only. Never grant
either capability's owner/migrator groups to an application login.

Web Push dispatch discovery, claim/recovery and four scalar health counts
require `event_webpush_admission_global` on Global with inheritance enabled
and role switching disabled. Discovery returns only tenant/dispatch IDs;
discovery and recovery batches are bounded to 1000. The definer reads ledger
predicate columns, not payloads, subscription secrets, recipients or
preferences. The drain acquires fresh exact Runtime leases for active-claim
hydration, erasure/preferences/subscription checks and settlement, preserving
claim-token fences, attempt accounting and existing recovery semantics.
Endpoint uniqueness/upsert has a separate authority contract.

Queue-drain readiness requires `event_queue_drain_health_global` on Global
with the same execution-only membership shape. Its fixed function returns
13 integer counts for IntegrationSync, incoming messages, replay operations,
provider publication and PDS queues. Predicate-column grants exclude payloads,
recipient IDs and arbitrary row projection. The integration stale cutoff stays
independent of webhook/PDS expiry observations; status, null-time, soft-delete,
ambiguity and supersession predicates remain unchanged.

Incoming webhook message/effect discovery and renewal require
`event_incoming_webhook_global` on Global with inheritance enabled and role
switching disabled. The four fixed functions return claim identities, not
payload, receipt or effect-envelope content. Message claims/recovery append
attempt evidence atomically; renewal checks exact tenant/ID/token/fence/
generation/state and strict lease expiry. Existing
`IncomingWebhookTenantExecutor` retains fresh exact Runtime execution and
settlement. Never grant the capability's owner/migrator groups or private-table
access to the application login.

Instance operations report synchronization counts use
`IEventReportOperationsRepository` on independent Global leases. Grant
`event_report_operations_global` to Global with inheritance enabled and role
switching disabled. Its scalar function reads only link state/creation and
parent ID/tenant/soft-delete columns, preserves the parent-report soft-delete
filter and strict `CreatedAt < cutoff`, and never projects report/case/content
or provider URLs. Tenant-specific report operations stay on their original
repository and owning transactions.

Email operational health reuses `event_email_dispatch_admission_global` through
the existing admission definer. The `_health` function returns six counts,
oldest-due time, a 1–100 tenant backlog sample and three processor flags.
Soft-deleted/redacted/tenant-paused rows are excluded; global pause remains a
reported flag. Rate override is exposed only as a boolean. Instance operations
and email health consume this independent snapshot rather than generic
cross-tenant repository queries; claim/recovery algorithms are unchanged.

Resource-audit retention requires `event_resource_audit_retention_runtime`
membership on Global only, despite the execution group's historical suffix.
It exposes bounded owner discovery through the existing directory join; the
definer reads audit tenant keys, not attribution. Each deletion batch acquires
a fresh exact Runtime context and rereads governance in the same Serializable
transaction. The sweep captures time once, pages 100 owners, deletes at most
1000 rows per batch, retains the inclusive cutoff and zero-retention purge,
and propagates failure without silently deleting held evidence.

Local webhook delivery requires `event_local_webhook_admission_global` on
Global for bounded discovery/governance/fair claims/recovery. Governance exposes
exactly seven delivery keys, preserving instance locks, tenant overrides and
startup ceilings. Fresh exact Runtime scopes hydrate payload/credentials,
settle and manually retry; token/fence/expiry/active-endpoint checks remain.
Recovery records failed-attempt evidence atomically. Grant execution membership
with inheritance enabled and role switching disabled, not private-table
privileges or owner/migrator membership. HTTP transport remains a separate
workstream boundary.

Registration finalization requires `event_registration_finalization_global`
on Global with inheritance enabled and role switching disabled. Its claim
function exposes only effect/order/tenant/token/fence identities and owns only
queue columns, not payment or fulfillment graphs. Claims/recovery retain
committed effect identity and advance attempt/fence under row locks atomically.
The drain binds each fresh ordinary scope to the persisted claim tenant before
resolving lifecycle/settlement/admission services, then clears/disposes it.
Financial transitions stay in their original owning transactions; settlement
retains exact identity/token/fence/state and strict expiry checks. Runtime
must receive neither the Global capability nor owner/migrator membership.

Payment reconciliation requires `event_payment_reconciliation_global` on
Global for bounded claims and aggregate health. Fresh exact Runtime scopes
hydrate and settle payment attempts. Settlement locks the reconciliation row
and checks payment-attempt ID, token/fence/attempt/expiry plus every captured
dispatch identity field before economic writes. Unknown-dispatch recovery and
settlement share one transaction, retaining provider idempotency identity.
Grant inheritance-enabled, role-switch-disabled execution membership only;
do not grant owner/migrator membership or private-table access. Provider outcome
mapping remains with the existing reconciliation service.

Instance visitor-policy validation requires `event_visitor_policy_global` on
the selected Global login. Its parameterless functions read only the compiled
visitor-authority setting keys and live account-required participation. They
execute on the writer's owning serializable connection, so an uncommitted
instance lock cannot be missed by a separate reader pool. Runtime receives no
execution membership; Global receives no private-table or owner/migrator
membership. Instance mutation composition must acquire Global before resolving
the writer, unit of work and ordered mutation locks.

SMTP policy reconciliation uses `event_email_policy_runtime` on Runtime and
`event_email_policy_global` on Global. Each is an execute-only membership for
the parameterless fixed `EmailDeliverySettingKeys.All` override reader. This
purpose-bound cross-tenant read must use the mutation's current transaction:
an independent pool would miss uncommitted instance locks and compromise the
delivery-policy revision calculation. Neither group grants table access,
owner/migrator membership or a change to the immutable tenant lease. Provision
memberships with inheritance enabled and role switching disabled.

Paid-policy instance ceiling validation requires execute-only
`event_paid_policy_ceiling_runtime` membership on the policy-mutation Runtime
login, with inheritance enabled and role switching disabled. The bounded
active-policy reader runs on the owning connection and transaction, including
manifest writes. Its fixed aggregates retain persisted identity, all four
ordered collections and exact `long` minor-unit amounts. Model-owned tenant
policy and owner read-policy fingerprints are checked before projection;
missing or changed authority fails closed. The group grants no policy-table
DML, owner/migrator membership or tenant lease changes.

Instance accounting uses `event_instance_storage_admin_global` on Global only.
The parameterless repair primitive discovers scopes, fences counters,
recalculates custody-derived totals and reads the final report in one transaction.
Owner DML is limited to counter accounting/fences; objects, sessions and
tombstones remain read-only. No tenant lease retargeting or per-tenant commits
are permitted. Other providers use the same single transaction with parameterized
counter statements and the existing custody totals builder.

The storage reconciliation custody cutover adds separate Global-only execution
groups: `event_storage_reconciliation_global` for bounded metadata observations
and exact-snapshot quarantine, and `event_local_storage_quarantine_global` for
physical orphan inspection, durable admission, claims and fenced settlement.
Neither is the accounting capability. Their NOLOGIN owners have only the
specific custody reads and fixed metadata/work transitions; Global receives
function execution, not ordinary storage-table CRUD. Runtime receives no new
membership or storage bypass. Keep execution inheritance enabled, role
switching disabled, and owner/migrator membership absent. Readiness must reject
missing or altered capability ownership, signatures, search path, grants and
policies rather than falling back to ordinary repositories. Generated provider
migrations and native final assurance for this cutover are still queued.

The email-policy execution groups also expose a bounded tenant-control snapshot
and fixed revision transition, not arbitrary control DML. Each chunk contains
at most 500 tenants; expected revision fences and transaction baselines prevent
double increments. Only revision, suppression and audit fields change;
pause state and token buckets remain intact, including during rollback.

### Policy mutation acquisition

Bootstrap effect replay uses `event_manifest_replay_runtime` on Runtime and
`event_manifest_replay_global` on Global. These grant only the fixed
`configuration_manifest_replay_results(uuid)` reader, qualified by an applied
bootstrap operation and bounded to 256 safe tenant-result records. Its owner
reads only result metadata and operation ID/status/mode. Do not grant table
access or owner/migrator membership to either execution group.

Instance import snapshots use `event_configuration_import_snapshot_global`
on Global only. The three fixed functions read exact-tenant manifest-catalog
overrides, stored branding documents, and the exact active instance/tenant
paid-policy aggregate. The nonlogin owner has only the required SELECT columns
and fixed owner-only RLS predicates; the execution group has no table access.
Installation and execution reject unsafe memberships, table ownership,
privileges, or changed policy fingerprints.

`ConfigurationInstanceImportExecutor` acquires the sealed-null Global owner
before constructing repositories, resolvers, locks, and the unit of work.
Snapshot calls reuse that connection and any current transaction, so apply's
post-write fidelity read sees its uncommitted state. The snapshot role does
not authorize import writes or replace the existing session/target/digest
checks and single serializable apply transaction.

Instance import session and protected-artifact operations use
`event_configuration_import_state_global`. Its fixed session reader locks the
exact instance row; updates retain immutable token/artifact/target fields and
compare the original revision. Artifact reads require an instance session or
applied/rollback receipt link, while new rollback artifacts can be stored before
their receipt is inserted. Payloads remain Data Protection ciphertext. Native
operations enlist in the existing owner transaction; they do not open another
connection or commit individual steps. Fresh instance sessions use a fixed native create operation:
Uploaded state, revision 1, instance target, no preview/cancellation/consumption
metadata, and a lifetime of at most one hour. The protected artifact's metadata
must match and its PostgreSQL `xmin` must belong to this staging transaction;
an already-linked artifact cannot be attached to another session. Only the
non-login function owner receives the exact session INSERT columns. Global
keeps execution permission without direct session INSERT access.
The state owner also receives SELECT on the artifact's `xmin` system column
for the transaction check, not a table-wide SELECT grant. Its privilege audit
includes system columns and rejects any other undeclared column permission.
Cancellation and expiry persist the terminal session transition before deleting
its ciphertext in the same transaction. The fixed native delete requires a
Cancelled/Expired instance-session link whose `xmin` is this transaction,
rejects any active/foreign session or retained receipt snapshot link, and cannot
delete an arbitrary artifact. Its non-login owner alone receives DELETE on the
artifact table; Global receives only function execution. A delete failure rolls
back the terminal session transition too.

Coordinated publication-policy import uses
`event_configuration_import_publication_global` for the five fixed policy
settings. Its snapshot includes existing tenant overrides across the instance,
including retired or unmapped tenants, so policy compilation retains its full
ceiling checks. Reads and writes take the same transaction-owned setting locks;
the Global group receives function execution only. Tenant overrides retain
forced RLS; installation does not enable tenant-style RLS on instance settings.

Ordinary scalar instance imports use `event_configuration_import_scalar_global`
for catalog-admitted non-specialized Boolean/String settings. Fixed operations
retain creation identity and audit, derive registry metadata, and return the
old value for deferred effects. Specialized policy/resource/privacy/operator
keys remain outside this group; it has no direct table grants.

Instance import receipts and their fresh bound effects require
`event_configuration_import_receipt_global`. Successful writes require the
existing serializable owner transaction, with PostgreSQL `xmin` provenance
binding session consumption, rollback snapshot, receipt and exact Pending effect.
The group cannot rewrite receipts or process arbitrary outbox messages. Failed
evidence can open a new transaction on the same selected connection only after
the selected changes have rolled back; artifact metadata is read without
granting access to its protected payload.

Paid-policy imports use `event_configuration_import_paid_policy_global` for
fixed active-aggregate reads, bounded pages of all active tenant policies, and
revision persistence. Validation includes unmapped tenants before narrowing the
instance ceiling. Native writes compare the original identity/version, retire
only that active row, preserve its history and creation audit, and insert the
Domain-created revision with normalized children. These operations reuse the
sealed owner connection and existing serializable transaction; policy aggregates
remain detached from EF tracking. The Global group receives function execution
only, not table access or membership in the Runtime ceiling capability.

Tenant name/audit imports require `event_configuration_import_tenant_identity_global`.
The purpose repository operation changes only `FullName`, `UpdatedAt` and
`UpdatedBy`; it does not give imported entities generic mutation authority.
Native parent fences and terminal discovery revision finalization share the
same serializable owner transaction. Sorted parent/epoch locking and expected
identity revisions retain disclosure invalidation without Runtime enrollment.
The tenant-owned revision relation retains forced RLS and owner-only boundaries;
the Global group has function execution only.

Tenant branding imports require `event_configuration_import_branding_global`.
`ApplyBrandingImportAsync` admits only the manifest branding key and its fixed
catalog schema/defaults. Native create/update uses the sealed owner's existing
serializable transaction; updates compare persisted identity/stamp and preserve
creation audit, while both paths generate a fresh concurrency stamp. Snapshot
rereads see the uncommitted persisted result without publishing it to shared
cache. Column-limited nonlogin ownership and forced owner-boundary RLS keep
the Global execution group outside direct document-table access.

Portable resource-setting imports require `event_configuration_import_resource_global`.
The resource writer uses fixed-key system snapshots and exact selected-tenant
snapshot reads; an instance-only import issues no tenant query. Its shared
Domain validation retains system locks and raw-value non-widening checks.
Native SetValue writes compare original persisted state, retain metadata/audit/
locks, and defer notifications to the import's bound effect. SQL and C# both
require the existing serializable owner transaction. Forced RLS remains on
tenant overrides; instance-setting RLS is not changed.

Ordinary manifest tenant scalars require `event_configuration_import_tenant_setting_global`.
This native batch excludes coordinated publication, resource, email, privacy,
operator and visitor-policy keys. It routes before the unrelated email-policy
wrapper, acquires the existing ordered setting locks, honors system/tenant
locks, and compares every original row field before upsert. Updates retain
identity/creation audit; creates use UUIDv7. Disclosure enrollment stays on the
existing serializable owner transaction. Forced tenant RLS and column-limited
ownership grant Global only the fixed upsert function, not table access.

Instance upload, cancellation, receipt, history and forward-rollback handlers enter
`IConfigurationInstanceImportExecutor`, not the Runtime-scoped apply service.
Direct-transfer promotion uses the same executor for instance staging; tenant
promotion retains its tenant-scoped session service. The executor obtains a
fresh selected Global owner before resolving repositories, while the session
manager owns atomic artifact/session creation and cancellation. Runtime needs
no instance staging-table write grant for these handlers.
Rollback session creation reads the immutable retained receipt/artifact and
creates fresh staging state on the same selected Global owner. The session
manager owns that staging transaction; the executor does not nest another
transaction around it. Apply separately owns its serializable mutation.
Preview and cancellation share normalization of the UUIDv7 operation prefix
on rollback tokens before authorizing their underlying secret. Cancellation
retains token verification, including idempotent replay, and removes only the
fresh session's ciphertext, not its source receipt's retained snapshot.
Apply still requires that exact prefix
to match its explicit source operation before the initial artifact read and
again inside the mutation transaction. A mismatched source leaves the session
PreviewReady without a receipt or setting mutation. Runtime tenant authority remains
unchanged throughout these operations.

Current editable legal source uses `event_configuration_import_legal_source_global`.
Imported draft/revision persistence additionally requires
`event_configuration_import_legal_mutation_global`. Its fixed operations read
the complete exact-target aggregate, then append validated Domain source with
original-stamp/version/lifecycle comparisons on the same transaction. The group
has no direct private-table privileges and cannot publish or rewrite history.
It reads the current version and bounded localizations, including unpublished
drafts, without review identities, publication history, or acceptance records.
Documents and their versions/localizations/publications have model-owned tenant
filters and RLS. Parent-derived policies follow the declared navigation path
through explicit FK joins to the terminal tenant predicate, so a permissive
parent policy cannot authorize foreign children.

Location privacy review health uses `event_location_review_global` on Global
only. `EventLocationPrivacyReviewReader` acquires an independent Global lease
and calls the parameterless `location_privacy_review_depth` function. Its owner
has SELECT on only `NeedsPrivacyReview` and `IsDeleted`; execution returns one
count and never locations, tenant identities or address data. Runtime remains
tenant-bound. Portable providers count the same active review predicate through
the independently acquired Global context. This capability does not authorize
governance mutation or grant access to location tables.

Governance uses `event_location_governance_runtime` and
`event_location_governance_global` for the fixed override/projection readers and
correction writer. Runtime is restricted to a non-null target matching the
canonical leased tenant session; instance or foreign-target execution requires
inherited Global authority. All calls enlist in the existing setting transaction.
The owner reads only policy facts and location kind, updates only review/version/
actor/time/stamp fields, and inserts the matching fixed audit/outbox pair.
The existing C# policy determines which projections need correction.

`LocationPrivacyGovernanceStore` batches at most 500 corrections per call without
independent commits and rolls back the owning transaction on invalid/stale targets.
Raw corrections explicitly enlist disclosure revisions; tracked instance-setting
saves retain the existing all-tenant enrollment and parent fences. Native clock
parameters and serialized audit clocks pass through the same PostgreSQL timestamp
conversion. Contract installation owns its separate migration transaction.
Neither execution group receives private-table access or owner/migrator membership.

Policy mutation composition acquires its lease before constructing the selected
native handler graph. Instance setting commands use the independent Global
pool; explicit target-tenant administration uses an exact Runtime lease.
Global therefore needs the instance-setting object privileges for the admitted
core as well as its specific function memberships. Authorization is still the
outer native decorator, and the scope choice never grants user permission.
The scope-local context is single-assignment and DI owns its disposal; no
existing request lease is changed. Authentication configuration acquires the
same boundary before resolving its writer, unit of work and ordered locks.
Manifest roots retain their existing exclusions and transaction ownership.

Child execution uses `ITenantContextAccessor.BindExecutionTenant`, not
`SetTenant`: the latter is the request-resolution API and writes shared
`HttpContext.Items`. Execution facts are single-assignment, take precedence
inside that child scope, and reject replacement or clearing. They never change
the caller's HTTP tenant facts. Overlapping execution scopes therefore retain
their distinct targets without borrowing or retargeting an existing lease.

### Privacy-erasure authority credentials

Subject disposal requires `event_subject_erasure_global` on the selected Global
login only, with inheritance enabled and role switching disabled. The trusted
adapter independently rereads the committed retained fact, then the native
functions check the application saga tuple. Necessary provider locators feed
the existing application protector; they are not public output or telemetry.
Protected work, fixed disposal, custody/accounting, mirrors and outbox remain
in one serializable application transaction. Retained authority stays committed
if local settlement rolls back. Grant neither private tables nor owner/migrator
membership to Global, and do not grant this capability to Runtime.

| Compose / Infisical key | Direct .NET key | Consumer |
|---|---|---|
| `ERASURE_DATABASE_RUNTIME_USERNAME`, `ERASURE_DATABASE_RUNTIME_PASSWORD` (flat `.env` or Infisical `/database/erasure`) | `Database:Erasure:Runtime:Username`, `Database:Erasure:Runtime:Password` | API only, and only for `ExternalDatabase` |
| `ERASURE_DATABASE_MIGRATOR_USERNAME`, `ERASURE_DATABASE_MIGRATOR_PASSWORD` (flat `.env` or Infisical `/database/erasure`) | `Database:Erasure:Migrator:Username`, `Database:Erasure:Migrator:Password` | `Event.MigrationService` only |

For `ExternalDatabase`, endpoint metadata uses the same `ERASURE_DATABASE_*` names in both authorities: `ERASURE_DATABASE_HOST`, `PORT`, `NAME`, `TLS_MODE`, and `TRUST_SERVER_CERTIFICATE` (or structured `PrivacyErasureAuthorityDatabase:*`); the provider is fixed to PostgreSQL. The `ERASURE_DATABASE_` prefix remains the supported contract inside `/database/erasure`. Recursive startup reads use each response item's actual `secretPath`, not the requested parent path. Child values remain in their own section and cannot publish primary database flat aliases, including when `/database/identity` is also requested explicitly. Missing or out-of-scope response provenance fails closed. Use separate
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
`INFISICAL_*` deployment inputs, or in Development/Testing loaded from User Secrets) supplies secret-zero Universal Auth credentials.
API composition projects the validated provider selection and the actual authenticated
source's URL, project, client, credential, environment and requested paths into
runtime `SecretProviderOptions`. Runtime binding does not silently append a root
path or select a provider supplied by vault contents. Bootstrap credentials still
come from the process environment in Production, not arbitrary merged configuration.
Development/Testing additionally supports bootstrap configuration and shared User Secrets.

### Configuration boundary upgrade

API composition validates obsolete `PrivacyErasure:Durability:Mode` inputs before
projecting either bootstrap configuration or the selected Environment, Infisical
or development User Secrets authority. Its double-underscore form is also rejected.
Operators must explicitly retain the intended supported topology through
`ERASURE_DATABASE_TOPOLOGY` or `PrivacyErasure:Authority:Topology`; deleting an obsolete selector without choosing
the intended authority is not a migration. Follow the existing privacy-erasure
backup/restore/reset policy before intentionally changing topology. This repair
performs no automatic authority migration or database schema change.

The isolated Blazor Infisical provider validates exact requested roots before
authenticating: `/runtime/bff/oidc/keycloak`, `/runtime/bff/oidc/google`, and the
separate ATProto consumer `/runtime/bff/atproto/oauth`. Descendants and legacy
`/keycloak`, `/blazor`, `/atproto` requests are rejected. Every acquisition sets
`recursive=false` and `expandSecretReferences=false`. Returned `secretPath` must
equal the requested root; missing provenance, unresolved `${...}` references,
duplicate configuration keys and keys outside the consumer's closed mapping
fail startup. There is no load-failure suppression mode.

The Keycloak mapping permits endpoint, realm, client ID and the BFF client secret;
Google permits its client ID and secret; ATProto permits only
`ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS`. Restrict the BFF machine identity to these
exact roots independently of API/provisioning identities. Supply database,
session-encryption and session-JWT credentials only to their owning backend.
Nonsecret API routing and provider selection remain deployment configuration.
This is provider admission enforcement, not a claim that process environment
variables or a standalone host's shared backend configuration are scrubbed.

### Authentication coordinated-restart evidence

`InstanceAuthenticationCredentialCutoversController` exposes the metadata-only
native operations under `/api/admin/instance/authentication-credential-cutovers`:
GET `current`, GET `{operationId}`, POST the collection to prepare, POST
`activations`, POST `fence`, GET `{operationId}/cleanup-targets`, and POST `cleanup`.
Secure requests use the instance-setting
permission boundary and current persisted administrator authority. Inputs cannot
select the current instance, actor, authority profile or clock. Preparation
snapshots the explicitly approved replacement inventory with a server-owned
15-minute deadline. Reads recover retained exact receipts; an empty current
operation returns 204. Every response is private/no-store, and mutations suppress
generic idempotency response storage so cached transport replies cannot replace
current authorization or feature-owned recovery.

The BFF GET `/bff/auth/credential-acquisition-receipts` requires authentication and
a fresh generated instance-overview GET capability. It compares local acquisition
authority/profile metadata with the independently selected API profile before
returning fixed value-free local receipts. It does not consult local role claims,
enroll replicas, write cutover state or attest credential contents. Missing
capability denies inspection; mismatched or absent local acquisition rejects it.
Overview capabilities are absent until bootstrap completes and the deployment
profile is valid. Metadata refresh does not establish fleet convergence.

`AUTHENTICATION_CREDENTIAL_PROFILE_REVISION` is an optional, nonsecret
deployment-owned UUID. It must be nonempty and valid before credential maintenance
is allowed. Change it whenever the approved provisioning or binding profile changes,
and restart every owning API/BFF process. Compose operators explicitly forward it
through their service overrides; it is not a secret or an Infisical-returned key.
`AuthenticationCredentialProfileReader` reads that explicit process environment
input and the validated selected provider locally, without credential acquisition
or health HTTP. Transaction owners must compare both fields with the persisted
approved operation; a request field cannot substitute for this independent read.
For the Environment authority, `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`
are optional BFF runtime inputs. The catalogue classifies the client ID as public
and the credential as Secret; its secret-binding projection includes the actual
Google registry definition. Explicitly forward only to the owning processes.

`AuthenticationCredentialAcquisitionReceipts` records successful BFF acquisition
with a fresh process boot identity and a random identity for each acquired
Google/Keycloak consumer. Named OIDC installation is recorded separately:
an existing options-cache or scheme entry is not proof that the newly acquired
credential was installed. Metadata refresh reuses the process acquisition;
it does not obtain a new credential or invent a new generation.
Receipts contain no credential values, credential digests or provider coordinates.
They are correlation evidence, not cryptographic proof of credential contents.

Start replacement processes before approving exact boot/generation membership.
The operator independently verifies which instance, deployment and replica each
directly inspected process represents. Approval freezes that inventory; later
activation cannot enroll another process or consumer.

The native provider tests exercise actual authorization-code and refresh-token
exchanges against disposable Keycloak, including rejection of a retired client
credential. `BffAuthenticationCredentialReplicaCutoverTests` separately launches
two production BFF OS processes, holds a real proxied request through SIGTERM,
releases it and requires the old PID to exit successfully while its replacement
remains usable. Factory disposal, scheme installation and a shutdown log alone
are not termination evidence. These tests do not attest a deployed fleet: the
operator must verify its exact process inventory and termination before fencing.

`RelationalAuthenticationCredentialCutoverCoordinator` retains receipts and one
current-operation head per instance. A same-instance composite foreign key
prevents a head from referencing another instance's receipt. Each transaction
flushes an optimistic head update before inspecting evidence, then commits
evidence or predecessor supersession with that update. A competing replacement
or stale callback cannot commit using the old head stamp. Actual expiry is
persisted even when the invocation returns an expiry error; already-terminal
callbacks roll back without changing the head. No remote provider work runs
inside these transactions.

The coordinator resolves the authenticated actor through `IAdminContext`, then
holds the current completed bootstrap and real principal/provider-identity/admin
grant/role row fences in its own serializable context through commit.
`IInstanceAdministratorCommitAuthority` exposes the existing issuance fence
without duplicating administrator semantics. Revocation uses ordinary updates
or deletes of those same rows; an earlier boolean admin check is insufficient.
Preparation cannot forge its creator, and fence submission cannot forge its
attesting actor. Every attempt reads the independent selected profile again;
callers no longer supply an allegedly authoritative profile revision.

`ReadyForCleanup` requires all approved consumer/process acquisitions and the
explicit old-process, old-writer and old-session fence. It is not a completed
cleanup result. Keep retained operation history through rollback/replacement;
do not drop receipt tables to recover an interrupted cutover. Authorization,
fleet verification and exact legacy deletion remain separate owning boundaries.

Inspection returns only exact row IDs, logical keys and current operation/head
stamps. The coordinator rechecks the completed current bootstrap, persisted
administrator and selected authority profile inside a serializable transaction.
It projects no settings values, provider coordinates or validation diagnostics.
Settings targets are only `auth.google_client_secret` and
`auth.keycloak_client_secret`. Binding targets are only noncanonical, unqualified
instance references under `auth.google_client_secret` and
`keycloak.blazor_client_secret`. Canonical references, tenants, qualified rows and
infrastructure credentials remain untouched. Ordinal post-filtering prevents a
provider's permissive SQL collation from expanding those exact logical keys.

Cleanup approval supplies `operationId`, `deploymentRevision`,
`expectedHeadStamp`, `expectedOperationStamp`, `approvedSettingIds` and
`approvedBindingIds`. Both ID arrays are required, contain at most two unique
nonempty IDs each, and must match the complete current inspected inventory.
Missing, extra or changed targets conflict without deleting anything. The
coordinator flushes the head compare-and-swap before reading active evidence,
holds bootstrap/administrator/profile fences, deletes approved IDs and saves
`Cleaned` with nonsecret row counts in the same transaction. Cancellation after a
physical delete rolls back the rows, receipt and head together. No remote
acquisition, verification or provider deletion runs inside that transaction.

A committed-cleanup retry reauthorizes the current operator and verifies the
operation's deployment revision, then returns retained counts. It never applies
the retry's row IDs, reopens cleanup after expiry or deletes again after a later
replacement/profile change. Generic idempotency response storage is suppressed.

Tracked persistence permanently rejects writes or renames involving either
retired settings key and rejects noncanonical runtime binding writes. This is not
a database privilege fence for raw SQL or an obsolete executable: their
termination and revoked write authority remain part of the independently
verified operator fence before cleanup.

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
| `/runtime/bff/atproto/oauth/ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | `auth.atproto.oauth_client_private_jwks`; resolves to BFF `Atproto:OAuthClientPrivateJwks` |
| `/atproto/ATPROTO_SESSION_ENCRYPTION_KEYRING` | `auth.atproto.session_encryption_keyring`; consumed by Infrastructure session-envelope protection |
| `/atproto/ATPROTO_SESSION_JWT_PRIVATE_JWKS` | `auth.atproto.session_jwt_private_jwks`; consumed by API first-party session JWT signing/validation |
| `/keycloak/REALM_NAME` | `Keycloak:RealmName` |
| `/keycloak/KEYCLOAK_CLIENT_ID` | Nonsecret browser/BFF client metadata mapped to `Keycloak:ClientId` for API onboarding detection. |
| `/runtime/bff/oidc/keycloak/KEYCLOAK_BLAZOR_CLIENT_SECRET` | Deployment-owned Blazor BFF runtime credential resolved through the selected authority; Event never persists or rotates it |
| `/runtime/bff/oidc/google/GOOGLE_CLIENT_SECRET` | `auth.google_client_secret`; instance-only deployment-owned Google BFF runtime binding |
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
| `/database/DATABASE_GLOBAL_USERNAME` | Independent bounded-capability database username |
| `/database/DATABASE_GLOBAL_PASSWORD` | Startup-owned Global database password |
| `/database/DATABASE_LOCAL_IDENTITY_WRITER_USERNAME` | Dedicated application-side Local Identity workflow login; distinct from Runtime, Global and Migrator |
| `/database/DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD` | Dedicated Local Identity workflow password; never a sibling-role fallback |
| `/database/DATABASE_MIGRATOR_USERNAME` | Migrator database username |
| `/database/DATABASE_MIGRATOR_PASSWORD` | Migrator database password |
| `/database/ERASURE_DATABASE_TOPOLOGY` | Privacy erasure topology: `EmbeddedSqlite`, `CoLocated`, `ExternalDatabase` |
| `/database/IDENTITY_DATABASE_TOPOLOGY` | Identity database topology: `colocated` or `external` |
| `/database/identity/IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME` | External Identity lifecycle writer login |
| `/database/identity/IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD` | External Identity lifecycle writer password |
| `/database/erasure/ERASURE_DATABASE_PROVIDER` | External authority provider (fixed to `PostgreSql`) |
| `/database/erasure/ERASURE_DATABASE_HOST` | External authority PostgreSQL host |
| `/database/erasure/ERASURE_DATABASE_PORT` | External authority PostgreSQL port (default: `5432`) |
| `/database/erasure/ERASURE_DATABASE_NAME` | External authority PostgreSQL database name |
| `/database/erasure/ERASURE_DATABASE_TLS_MODE` | External authority TLS mode: `Prefer`, `Required`, `Disabled` |
| `/database/erasure/ERASURE_DATABASE_TRUST_SERVER_CERTIFICATE` | `false` (default: strict CA verification) or `true` (accept self-signed certs) |
| `/database/erasure/ERASURE_DATABASE_RUNTIME_USERNAME` | External authority runtime username (function-execution role) |
| `/database/erasure/ERASURE_DATABASE_RUNTIME_PASSWORD` | External authority runtime password |
| `/database/erasure/ERASURE_DATABASE_MIGRATOR_USERNAME` | External authority migrator username (schema/admin role) |
| `/database/erasure/ERASURE_DATABASE_MIGRATOR_PASSWORD` | External authority migrator password |
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

The three ATProto rows use the same uppercase name as their default environment-variable name as well as their Infisical key. Private credentials use only the authoritative registry names; there are no .NET double-underscore credential aliases. Storage accepts the documented `STORAGE_S3_*` variables only under Environment authority. Primary database bootstrap uses discrete structured fields rather than a connection string. SMTP uses the authoritative `MAIL_SMTP_*` names. Registration-provider credentials are tenant-scoped secret definitions and must be bound through `SecretBinding`; use the bounded `Qualifier` field when several tenant connections need distinct API tokens or webhook secrets for the same key. Browser contracts never carry secret values or provider source coordinates.

Stripe secrets are instance-scoped, server-only, and optional while paid events are disabled. `Payments:Stripe:Mode=Test` requires a platform key beginning `sk_test_`; `Live` requires `sk_live_`. The Connect endpoint uses only the dedicated webhook binding, never the platform key or an outgoing-webhook secret. Rotate platform and endpoint secrets deliberately with the matching Stripe mode and endpoint configuration; retain no secret value in logs, support artifacts, browser DTOs, OpenAPI, or the DBML reference.

Keycloak itself consumes `KEYCLOAK_ADMIN` and `KEYCLOAK_ADMIN_PASSWORD` to
create its initial administrator. Event never reads them as application
runtime secrets; do not store them in governance settings or copy them into
support artifacts.

The checked-in Keycloak realm export contains no client secret and is not
mounted or imported by normal Compose/AppHost startup. Compose and local Aspire
pass the deployment value to API and BFF only. If an operator approves creation
of an absent confidential client, the API resolves that value for the one
foreground Admin REST request. AppHost never generates, persists or renders a
replacement value.

External-Keycloak privileged inspection accepts a one-time Keycloak administrator
username/password through the advanced setup or administration form. Treat that
credential as operator input for exactly one foreground request, not as a
platform-managed secret. Event must not read `KEYCLOAK_ADMIN*`, Infisical
administrator entries, User Secrets or application configuration to satisfy an
empty form, and must not save the input to appsettings, environment variables,
Infisical paths, database governance settings, logs, traces, screenshots or
support bundles.

Paid-event hosted onboarding never returns payment platform secrets, provider account identifiers, or connection identifiers to the browser. `Payments:OrganizerDirect:ProviderCode` and `ConnectPlatformId` are server configuration, not secret values, but are still omitted from browser readiness contracts.

Keycloak onboarding and administrator reads always redact the runtime client secret. They may expose only configured/source/editability metadata plus nonsecret authority and client ID. Secret values remain in the selected deployment authority; database rows contain only binding metadata, and setup writes do not persist a replacement value.

Keycloak runtime connection resolution reads the endpoint, realm, BFF client ID
and BFF client secret from the selected deployment authority once for the
request. There is no database-secret reader, source fallback or
application-managed rotation branch. Rotate an existing client credential in
the deployment authority and Keycloak under an operator-controlled maintenance
window, restart affected replicas, and then re-inspect. Event never writes the
replacement to application storage.

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

The external [Configurator terminal](https://github.com/ISLAMU/Configurator/blob/HEAD/docs/public/terminal.md)
owns the offline UI and protected writer. Event retains runtime secret binding
and bootstrap authority. The bundled terminal is removed. Native protected-save
support follows Configurator's own package/host evidence; currently restricted
publication is Linux-only and fails closed on unsupported hosts or filesystems.
Never substitute plaintext stdout or ordinary file writes when protection is
unavailable.

Configurator keeps private answer input masked, blocks private clipboard/history
surfaces and clears private state on cancellation/disposal. Its general Core
handoff can still create immutable .NET strings; runtime, kernel and storage
erasure are not guaranteed. Do not treat local preparation as resistant to a
compromised account, debugger, memory dumper or crash collector.

The protected, create-only output is deployment input, not a runtime secret
authority. Move values into the selected deployment authority and remove the
local artifact when no longer required. Event's readiness API remains value-free;
external preparation does not authorize secret readback.


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

### Localization configured-status metadata

`LocalizationSecretStatusReader` uses EF Core `AnyAsync` on the original selected
context. C# applies the exact localization key, empty qualifier, instance scope
and selected-tenant scope; the result is a Boolean, never a secret value or
provider reference. PostgreSQL installs fixed SELECT access only to
`SecretBinding.SettingKey`, `Qualifier`, `SettingScopeId` and `ScopeId` for the
declared Runtime and Global accounts. There are no binding-write or table-wide
read grants and no status stored routine.

Installation requires the former localization routine and execution roles to be
absent. It does not drop existing database objects or repair permission drift.

`SecretBinding` retains its existing non-RLS state. Adding RLS just for this
projection would break other fixed binding owners and their source attestations;
it is not needed to move scope selection into the application. Consequently a
stolen Runtime or Global database credential can enumerate those four metadata
columns across binding rows, bypassing C# scope selection. It still cannot use
this grant to read private binding/reference columns or obtain secret values
from the selected external authority. This is an intentional application-owned
authority boundary, not a claim of database-enforced metadata row isolation.

Reporting provider secrets are server-side tenant settings. API keys and webhook secrets for Osprey and Coop must never be returned in browser DTOs, HAL links, health checks, logs, metrics, traces, screenshots, issue templates, or support bundles; browser/control-plane surfaces may expose only configured/source/editability metadata. Routing update actions are write-only for secret values: supplying a new Osprey/Coop API key or webhook secret rotates that tenant value, while omitting the field or sending it blank preserves the currently stored secret. There is no implicit clear-secret endpoint and no readback path; confirm rotation through configured flags, provider readiness checks, and secret-provider audit trails.

Current migrated surface: Cerbos authorization settings expose endpoint and Admin API credential ownership metadata. `AUTHORIZATION_PROVIDER` is non-secret deployment intent, while `CERBOS_ADMIN_USERNAME` and `CERBOS_ADMIN_PASSWORD` are server-side deployment secrets resolved from environment configuration or Infisical. The browser normally sees only configured flags and ownership metadata. During an explicit setup sync, an operator may instead submit a complete one-time pair; it exists only in the Blazor server circuit and request pipeline, overrides deployment credentials for that call, is cleared after the call, and is never written to `SystemSetting`, returned by an API, or logged. `CERBOS_ADMIN_PASSWORD_HASH` is the Cerbos server verifier and cannot authenticate an Admin API client; keep the matching plaintext password only in deployment secrets or enter it for one sync. Reporting provider secret keys are registered as sensitive hierarchical settings for the moderation routing foundation. Listmonk API username/key values are registered server-side secret bindings; admin updates are write-only and browser DTOs expose configured flags only. Stripe `payments.stripe.platform_secret_key` and `payments.stripe.webhook_secret` are instance/server-only definitions for self-hoster-owned platform credentials. Promotion lookup resolves the qualified instance-only `promotions.code_lookup_hmac_key` binding for every digest operation. SMTP, S3, OAuth, localization/TMS, and AI keys still have area-specific storage/UI paths and must not be documented as fully migrated until their resolvers use the shared ownership metadata consistently.

Web Push VAPID keys are deployment configuration. Infisical `/api/webpush` (or legacy `/api`) `PRIVATE_KEY` / `VAPID_PRIVATE_KEY` maps to `WebPush:VapidPrivateKey`; it is a server-only secret and must never appear in browser configuration, API responses, HAL links, logs, traces, health data, screenshots, or support artifacts. `VAPID_PUBLIC_KEY` is intentionally public and is returned by `GET /vapid-public-key` as plain text and by `GET /api/notification/web-push/config`. Browser subscription endpoints and `p256dh`/`auth` material are stored tenant-scoped and are never echoed by subscription status DTOs.

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

The in-process audit entry can carry lifecycle context, but the default structured
sink exports only bounded operation, provider, status, failure reason and validated
correlation identifiers. It does not export key paths, user identities or raw
provider error messages, including at Debug level. The decorator accepts
server-issued GUID correlation from request items or the trace identifier,
not caller-supplied correlation headers.

## DI Registration

Database role credentials bind only from `Database:<Role>:Username` and
`Database:<Role>:Password` or that role's explicit `DATABASE_<ROLE>_*` aliases.
Generic root credentials and sibling roles are never fallback authorities.
An explicit empty structured value masks its alias; valid password bytes are
preserved. `BindUsername` resolves a selected login without acquiring its password.

Server-provider validation rejects the retired `explore` password after
case-insensitive, whitespace-trimmed comparison, and passwords equal to the role
username ignoring case. Other password bytes remain unchanged. This validation
also applies when constructing a connection string. SQLite remains credential-free.
Rotate affected database passwords and update the selected secret authority
before restarting; Event does not rotate database credentials automatically.

The Keycloak operator component clears its administrator credential fields and
apply confirmation on navigation and disposal. Disposal removes the navigation
subscription; a queued navigation render checks disposal before updating UI.

Fresh Keycloak administrator usernames and passwords are opaque request values.
`KeycloakOperationApplyContext` rejects null or whitespace-only inputs but
preserves every character of a valid pair for provider authentication. Do not
reuse metadata normalization for credentials.

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

`PostgresSetupLiveStoragePermissions` installs Runtime SELECT and INSERT rights
on the EF-mapped enrollment, issuance-claim and secret-binding-operation tables.
Only enrollment capability/lifecycle/expiry and operation settlement fields,
their concurrency stamps and update audit columns receive UPDATE rights.
Issuance claims remain immutable; no DELETE, Global-role membership, workflow
routine or tenant-policy bypass is granted. Production migration and native test
bootstrap invoke the same migrator-only installer on its original transaction.
Setup Live uses `ISetupSecretBindingIdentityReader` to project only the ID of
an unqualified instance `setup.signing` or `setup.encryption` binding. Runtime
receives SELECT only on binding ID, key, qualifier and scope identity columns;
provider coordinates and source references remain unreadable. Readiness and
write ports continue to consume that ID and the canonical key, not a partially
populated binding entity or a copied provider reference.
The actual Runtime isolation regression checks both selected and absent tenant
contexts; Runtime cannot invoke the installer to grant itself privileges.
Application admission, replay, dispatch and settlement retain their existing
serializable owner and authorization boundaries.

Setup Live can submit a value only after the target reports the exact binding
as `Ready`. The target writes through its already-selected authority; the
current live-write authority is Infisical. Environment and User Secrets are not
live-write providers, and absence or provider failure never falls back to
appsettings, another provider, or stored coordinates.

The enrollment capability, raw value, and target-local Infisical coordinates
are never saved in a profile, database row, portable artifact, log, trace,
metric, response, or support bundle. The API exposes value-free readiness and
receipts only and has no readback route. External Configurator has no activated
live workflow; its release metadata belongs to that repository. Retained Event
backend and client transport are not an activation mechanism.

## Related

- [CONFIGURATION.md](CONFIGURATION.md) — application settings
- [SELF_HOSTING.md](SELF_HOSTING.md) — environment variable reference
- [OPERATIONS.md](OPERATIONS.md) — health checks and metrics
