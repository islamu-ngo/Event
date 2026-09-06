ABOUTME: Practical troubleshooting guide for this repository's API, BFF, and tenant-aware runtime.
ABOUTME: Prioritizes repeat incidents and non-obvious checks over generic .NET advice.

# Troubleshooting

## Legal Identity And Readiness

| Symptom | Likely authority | Repair |
|---|---|---|
| API or Standalone stops with `Instance:OperatorIdentity` validation failure | Missing or malformed `INSTANCE__OPERATORIDENTITY__*` startup values | Correct the exact `.env` values and restart; do not move them into appsettings, branding, or Checkout governance |
| Tenant activation/reactivation is blocked | Tenant document is incomplete for `Activation` | Open Tenant Settings -> Legal Identity, complete the grouped legal/contact/link fields, and save using the current revision |
| Anonymous settings/shell returns `503` and `tenant_identity_unavailable` | Exact tenant document is missing, corrupt, or incomplete for `PublicDisclosure` | Repair the tenant identity document; do not add a public cache or branding fallback |
| Paid publication or Checkout activation is blocked | `PaidCommerce` intersection is incomplete | Repair the reported tenant identity, instance identity, organizer payment connection, policy, or payment-operation authority |
| Admin save reports a conflict | Another writer changed the document revision | Review the automatically reloaded authoritative values, reapply the intended edit, and save again |
| Earlier buyer acceptance becomes stale | Identity, organizer/provider lineage, policy, schedule, line, or money facts changed | Present the newly composed disclosure and collect a new explicit acknowledgement |

> **Audience:** Operators | Contributors | Admins
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-09
> **Source Anchors:** `Explore.API/Program.cs`, `Explore.Blazor/`, `Explore.Infrastructure/Services/Keycloak/KeycloakBootstrapService.cs`, `Explore.Infrastructure/StorageObjectDeletionService.cs`, `docs/SELF_HOSTING.md`, `docs/OPERATIONS.md`, `docs/BACKUP_RESTORE_UPGRADE.md`, `docs/CONFIGURATION.md`, `docs/SECRETS.md`

Use this page when you have a symptom. For planned work, installation, backup, restore, upgrade, or rollback procedures, use the linked runbooks instead of copying procedures into this file.

> [!IMPORTANT]
> **Production & Self-Hosting Operational Triage:**
> For Docker Compose, standalone containers, Traefik, Keycloak deployment, storage S3/MinIO, and reverse proxy troubleshooting, consult the authoritative **[Troubleshooting & Health Guide (Public Documentation)](../public/documentation/readme/configuration-and-operations/troubleshooting-and-health.md)**.
> This internal document is strictly scoped to C# development, EF Core migrations, MediatR pipeline debugging, and test suite execution.

## Paid Checkout Is Not Offered

1. Confirm the order HAL contains both `payment-acceptance` and `start-payment`; do not bypass a missing relation.
2. Check global/event stop-sale and complete server-owned `Payments:CheckoutGovernance` values.
3. Verify active instance/tenant policy, currency ceilings/review gates, catalog merchant/refund/support disclosures, organizer connection freshness, Stripe mode, platform identity, and official HTTPS origin.
4. A `payment_acceptance_stale` response means reload and re-acknowledge exact facts. `payment_acceptance_required` on an old attempt is intentional; do not backfill it.
5. Keep webhook intake and reconciliation running while sales are stopped. If restore introduced due work, follow `BACKUP_RESTORE_UPGRADE.md` rather than creating another Checkout.

## Headless Onboarding Won't Complete

| Symptom | Likely cause | Repair |
|---|---|---|
| Startup stops with a bootstrap matrix reason code | Mode missing/unknown, a configured key set under `Interactive`, a required key missing under `ConfiguredAdministrator`, or only one of the two profile names | Fix the environment or secret authority to satisfy one closed matrix exactly, then restart |
| Provider rejected at startup | `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` is anything other than `keycloak` or `atproto` | Set the exact lowercase key value |
| Generation rejected at startup | Value isn't a positive integer | Set a positive integer |
| Sign-in succeeds but no privilege is granted | The presented claim isn't the exact configured selector: wrong issuer for `keycloak`, wrong subject, or a non-canonical DID for `atproto` | Compare the configured subject with the provider's issued subject/DID and correct the configuration, not the database |
| Onboarding sits pending with a healthy instance | Expected. Pending is a healthy state until the configured administrator signs in | Sign in as that administrator |
| Bootstrap reports drift and refuses to proceed | Stored generation is same or lower than a changed selector, meaning the configuration moved without a new generation | Restore the previous configuration or raise `INSTANCE_BOOTSTRAP_BINDING_GENERATION` above the stored one |
| Configuration changed but nothing happened | Same generation with identical values converges silently | Nothing to repair |
| Corrected configuration before completion | A strictly higher generation replaces the pending binding | Restart and sign in with the corrected identity |
| Change refused after completion | Completion is final. Environment changes can't replay or transfer a completed instance | Use normal admin governance, or a separately approved operator workstream |
| HTTP never becomes ready | Startup order blocks: migrations and seeding, then the configuration manifest, then serializable preparation, then HTTP | Read the failing stage's reason code; earlier stages must pass first |
| BFF routes to setup instead of the app | The BFF follows configured routing for the current bootstrap state | Confirm the instance state rather than editing routes by hand |

Reason codes carry no identity values. If you need to prove which account is
configured, read your own environment or secret authority; the platform won't
echo it.

## Related Runbooks

| Need | Go To |
|---|---|
| Install, Compose topology, setup secret, Keycloak, MinIO, Cerbos, reverse proxy | [SELF_HOSTING.md](SELF_HOSTING.md) |
| Back up, restore, upgrade, roll back, or validate release safety | [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) |
| Runtime health, rate limiting, request timeouts, metrics, graceful shutdown | [OPERATIONS.md](OPERATIONS.md) |
| Configuration keys and environment-variable mapping | [CONFIGURATION.md](CONFIGURATION.md) |
| Secret-provider setup and secret naming | [SECRETS.md](SECRETS.md) |
| Auth/authz trust boundaries | [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md) |

## Quick Triage Order

1. Run the read-only doctor when diagnosing local or self-hosting setup drift:
   `dotnet run --project src/Explore.Diagnostic/Explore.Diagnostic.csproj -- --root .`
2. Check the selected topology endpoint: Split API `https://localhost:7039/health` and `/alive`, or Standalone `https://localhost:7180/health` and `/alive`.
3. Check MigrationService logs for application, Data Protection, the exactly one selected authority migration path, and seed failures; check API logs separately for runtime-provider validation. The standalone image is the documented in-process exception and reports the same migration responsibilities in its own log.
4. Verify deployment mode and tenant resolution behavior.
5. Verify auth session (`/auth/status`) and token forwarding through BFF.
6. Check rate limiting (`429`) and request timeout (`504`) before deeper debugging.
7. If the issue followed an upgrade or restore, stop and verify [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) rollback and validation steps before changing data.

## Aspire Split Or Standalone Topology

Symptoms:

- Aspire starts the unexpected API/BFF pair or omits the combined host.
- `https://localhost:7180` is unavailable after requesting Standalone.
- OIDC reports a callback/origin mismatch after a topology switch.

Checks:

1. `Hosting:Topology` accepts only `Split` and `Standalone`; the omitted value is `Split`. Use `Hosting__Topology=Standalone` before `aspire run` for the combined host. An unknown value is a deliberate AppHost startup failure, not a fallback.
2. In Split, inspect `explore-api` at `https://localhost:7039` and `explore-blazor` at `https://localhost:7177`. In Standalone, inspect only `event-standalone` at `https://localhost:7180` (AppHost HTTP via `WithHttpEndpoint(name: "http")` is dynamic); UI and `/api/*` share it. A direct `Event.Standalone` launch profile reserves `http://localhost:5180`.
3. Aspire Standalone waits for `Event.MigrationService`; the standalone Docker image instead runs the same migration bootstrap inside its one process before binding HTTP. If it is unhealthy, read that container's log; do not look for a migration helper or API-to-BFF YARP hop.
4. Confirm `CONTROL_PLANE_PUBLIC_ORIGIN` is the exact browser-facing admin origin. AppHost forwards it as `Bff__AdminHosts__0`; an admin host outside AppHost must configure that value explicitly. Refresh Keycloak's exact callback/web-origin/logout registration after changing the browser endpoint.
5. To roll back the local topology selection, stop the current AppHost run and relaunch with `Hosting__Topology=Split` (or omit it). This does not undo schema/data changes: preserve the database, run normal readiness checks, and use [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) for an actual database rollback.
6. `docker-compose.yml` is Split-only. The SQLite-default container path is `src/Event.Standalone/Dockerfile`, launched directly with `docker run --env-file .env`; no standalone Compose descriptor exists.

AppHost assigns the optional Standalone HTTP endpoint dynamically through `WithHttpEndpoint(name: "http")`; it remains internal/non-guaranteed, while HTTPS is `https://localhost:7180`. If running `Event.Standalone` directly, use its launch profile's reserved `http://localhost:5180` HTTP endpoint (or its `https://localhost:7180` HTTPS profile).

These checks cover the three application composition roots (`Explore.API`, `Explore.Blazor`, and `Event.Standalone`): AppHost selects the Split default or explicit Standalone, while the latter keeps browser `/api/*` traffic in-process after cookie antiforgery and trusted-header reconstruction. Keep canonical API paths as `/api/...` with non-URL API versioning (`Accept`, `?api-version=`, or `X-Api-Version`), never `/api/v1/...` (see [the support matrix](ARCHITECTURE.md#hosting-topology)).

The in-process Combined bridge remains the BFF/API trust boundary; a topology
switch never bypasses API authentication, authorization, or tenant isolation.

## Database Startup, Migration, Or Provider Failures

Inspect the bounded startup error without copying credentials or raw provider
exception text into support artifacts.

| Symptom | Cause | Safe correction |
|---|---|---|
| Missing `Database` section or invalid/numeric provider | Raw connection string or unrecognized provider input | Configure named `Database:Provider` plus structured fields; application and Data Protection support `PostgreSql`, `Sqlite`, `SqlServer`, `MariaDb`, and `MySql`. |
| Runtime works but MigrationService fails authentication | Runtime credentials were reused or migrator role is missing | Supply `Database:Migrator:Username/Password` only to MigrationService; keep `Database:Runtime:*` in API/runtime. |
| TLS validation fails | Certificate hostname/chain mismatch or unsafe trust combination | Use `TlsMode=Required` with a trusted CA and matching host. `TrustServerCertificate=true` is a controlled-development bypass and is invalid unless TLS is required. |
| MariaDB/MySQL fails before connecting | Missing or mismatched dialect metadata | Set the exact `ServerFlavor` and positive `ServerVersion` matching the engine. |
| PostgreSQL/SQL Server tables appear under the wrong schema | MigrationService and runtime received different `Database:Schema` values, or the schema changed without a data-move plan | Stop rollout, restore one shared schema value and grants for both roles, and run MigrationService against the intended target. Changing the value creates/selects another namespace; it does not rename or move tables. |
| SQLite/MariaDB/MySQL still creates `ie_` tables after setting `Database:Schema` | Expected flat-provider behavior | Keep the fixed prefix. Use a separate SQLite file or MariaDB/MySQL database per instance; prefix overrides are rejected. |
| Two instances sharing one database run each other's scheduled jobs | Both schedulers use the same `Scheduler:Quartz:SchedulerName`, so they contend for the same `QRTZ_` rows | Give each instance a distinct `Scheduler:Quartz:SchedulerName`, use separate databases, or set `Scheduler:Quartz:ClusteringEnabled=true` if they are meant to cooperate as one cluster. |
| SQLite configuration is rejected | In-memory, URI, network, or reserved authority path | Use a persisted absolute/local file, mount it into MigrationService and API, and keep it separate from `/app/data/privacy_erasure_authority.db`. |
| SQLite reports busy/readonly/not-a-database | Multiple writers, wrong mount permissions, inconsistent file copy, or network filesystem | Stop traffic, verify one replica, local durable storage, writable ownership, and WAL-aware restore, then rerun MigrationService. Do not delete the file as a repair. |
| MigrationService succeeds once but fails on repeat | Generated migration ownership/history drift or non-idempotent seed/model SQL | Stop rollout. Verify provider-specific application/Data Protection assemblies and distinct history tables, plus exactly one selected authority path; fix the EF model/generator and regenerate only unapplied migrations. Never patch generated files. |
| Startup fails reporting missing `QRTZ_` tables | `Scheduler:Quartz:ApplySchemaOnStartup` is `false` and no migration job created the scheduler schema | Set `ApplySchemaOnStartup=true`, or apply the scheduler DDL out of band. The scripts are idempotent and never drop existing scheduler state. |
| Embedded authority rejects startup | Non-local/symlink path, unsafe permissions, writer count not one, busy timeout outside `1..300`, or failed SQLite integrity/WAL check | Restore the dedicated authority file/volume and permissions; keep `WriterReplicaCount=1`. Never replace it with a primary backup. |
| `CoLocated` authority fails validation (`UnsupportedCoLocatedPrivacyErasureAuthorityMessage`) | `PrivacyErasure:Authority:Topology=CoLocated` configured with `SqlServer`, `MariaDb`, or `MySql` primary database | `CoLocated` supports only `PostgreSql` or `Sqlite`. Composition fails before adapter/database I/O; for SQL Server, MariaDB, or MySQL, select `PrivacyErasure:Authority:Topology=EmbeddedSqlite` (the default) or `ExternalDatabase`; no fallback, translation, dual write, or compatibility shim is applied. |
| External authority fails validation | Non-PostgreSQL provider, same physical target as primary, or incomplete structured role fields | Configure a distinct PostgreSQL target under `PrivacyErasureAuthorityDatabase:*` with separate runtime/migrator roles. |

For a clean install or upgrade, run `Event.MigrationService` before API start
and run it a second time in rehearsal. A deployed API does not own application
or Data Protection migration recovery.

## Build And Test Failures

Run from solution root:

```bash
dotnet build --configuration Release --verbosity quiet
dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity normal
```

Important:
- run tests with `--project` (not solution-level `dotnet test`).
- if failures are unclear, generate TRX:
  `dotnet test --project <project.csproj> --configuration Release -- --report-trx --report-trx-filename results.trx`

## OpenAPI / NSwag Drift

Symptoms:
- Blazor compile errors after DTO changes.
- Missing/old generated client types.

Checks:
1. Regenerate the governed OpenAPI document through the same API build-time generation path used by CI:

   ```bash
   dotnet build src/Explore.API/Explore.API.csproj --configuration Release --verbosity quiet
   ```

2. Rebuild `src/Explore.Blazor.Client`; its `GenerateApiClient` target regenerates `Clients/EventApiClient.g.cs`:

   ```bash
   dotnet build src/Explore.Blazor.Client/Explore.Blazor.Client.csproj --configuration Release --no-restore --verbosity quiet
   ```

3. Confirm only intentional generated artifacts changed:

   ```bash
   git diff -- schemas/openapi_islamu-event.json src/Explore.Blazor.Client/Clients/EventApiClient.g.cs
   ```

4. Commit `schemas/openapi_islamu-event.json` and `src/Explore.Blazor.Client/Clients/EventApiClient.g.cs` only when the API-surface change is intentional. Do not hand-edit either file.

If CI reports drift on an unrelated PR, check `.github/workflows/openapi-contract.yml` job summary. The guard has an internal no-op detector; unrelated changes should pass without regeneration.

## Registration Provider Integrations

Symptoms:

- Studio `/studio/events/{eventId}/integrations` is missing or health-only.
- A provider iframe is blank or returns `404` from the BFF embed route.
- Callback provider logs show retries, but ISLAMU shows parked items or no completion.
- Reconciliation queue rows expose issue codes such as `UNKNOWN_TUPLE`, `UNVERIFIABLE_EVIDENCE`, `BLOCKING_DRIFT`, `BELOW_MINIMUM_TRUST`, `STALE_OR_OUT_OF_ORDER`, or `MIRROR_SINK_UNSUPPORTED`.

Checks:

1. Confirm the event HAL contains `manage-registration-channels` for management or `view-registration-provider-health` for health-only visibility. The UI must not recreate hidden actions from roles or local claims.
2. For embeds, call the same-origin BFF route without query parameters. If it returns `404`, inspect the API launch descriptor lineage, mode, availability, and the connection approved origins. Only HTTPS origins are accepted; local/private/link-local/metadata targets are rejected.
3. For callbacks, remember `202 Accepted` is not completion. The callback endpoint acknowledges invalid, duplicate, unknown, stale, and parked evidence to avoid retry storms and tenant enumeration. Inspect provider health and queue resources instead.
4. For parked callbacks, verify the binding's exact capability tuple `(ProviderCode, DeploymentKind, ApiVersion, AdapterPolicyVersion, ConformanceEvidenceRevision)`, drift class, requirement sync mode, trust level, and Data Protection receipt availability. Do not change a provider name to force capability matching.
5. Health and queue responses are privacy-bounded. If you need raw provider payloads or attendee answers for diagnosis, do not add them to health/support artifacts; use an audited operator workflow outside this Phase 9 surface.
6. Docker-backed provider tests may be unavailable in local environments without Docker/Testcontainers. Record that as an environment caveat; do not weaken source docs or claim runtime provider proof from no-container tests.

## Auth And BFF Issues

### 401 on write endpoints

Checks:
- user is authenticated in BFF (`GET /auth/status`).
- YARP is forwarding bearer token to `/api/*`.
- API token contains expected audience (`islamu-event-api` or `islamu-event-blazor`) and valid issuer.

### OIDC redirect URI errors behind proxy

Cause:
- forwarded proto/host not propagated, so app computes wrong callback URL.

Check:
- proxy sends `X-Forwarded-Proto` and `X-Forwarded-Host`.
- forwarded headers middleware is active in Blazor server pipeline.
- API forwarded-header trust is configured for the reverse proxy; see [CONFIGURATION.md](CONFIGURATION.md) and [SELF_HOSTING.md](SELF_HOSTING.md).

### OIDC `invalid_redirect_uri` in local Aspire

Checks:

1. Run `aspire describe explore-blazor --format Table` and compare its browser-facing origin with the `redirect_uri` shown by Keycloak. Isolated Aspire runs intentionally use generated ports.
2. Confirm `keycloak-init` completed after the current AppHost launch. Local initialization reconciles exact login, origin, and logout values from the allocated Blazor HTTP/HTTPS ports; no Keycloak volume reset is required.
3. If a nonblank `KEYCLOAK_BLAZOR_REDIRECT_URIS`, `KEYCLOAK_BLAZOR_WEB_ORIGINS`, or `KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS` override is configured, it intentionally wins. Update all three exact allow-lists together; do not introduce wildcards or the `+` origin shortcut.

### AT Protocol provider is unavailable or OAuth fails closed

Symptoms:

- AT Protocol is omitted or reported not ready by the BFF provider endpoint.
- authorization fails before redirect, PAR, code exchange, refresh, revocation, or `getSession`.
- logs contain a bounded reason such as `provider_not_configured`, `invalid_public_url_or_callback`, `key_ring_unavailable`, `state_store_unavailable`, `session_store_unavailable`, or `secret_resolver_unavailable`.

Checks:

1. Confirm `Atproto:PublicUrl` is the exact browser-facing HTTPS origin and `Atproto:CallbackPath` matches the published client metadata. Remove credentials, non-root paths, queries, fragments, trailing-dot or Unicode host aliases, and ambiguous callback segments. Do not change the callback for an in-flight flow.
2. Confirm the instance secret provider can resolve `/atproto/ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS`. Do not print the value. Validate only that the ring has bounded canonical P-256/ES256 keys, unique `kid` values, one active key, and any still-needed older keys marked retired. Session persistence and first-party token issuance separately require `/atproto/ATPROTO_SESSION_ENCRYPTION_KEYRING` and `/atproto/ATPROTO_SESSION_JWT_PRIVATE_JWKS`.
3. `state_store_unavailable` means the state adapter is missing or the signed private create/read/consume probe failed; `session_store_unavailable` identifies a missing session adapter. `BffProviderReadinessService` checks local prerequisites before consulting its ten-second probe-result cache. The private transport has a two-second deadline and no retry/hedge policy. Restore registration, API/database connectivity, migrations, and the shared OAuth signing authority as appropriate, then wait beyond the cache window and verify a fresh login. This probe does not certify durable session operations or a user's PDS. Never enable an in-memory fallback. Caller cancellation remains cancellation, not a provider failure.
4. Verify public DNS for the PDS and authorization server from the BFF and Infrastructure network namespaces. Every answer must be public; a mixed public/private response is rejected. Production rejects loopback, RFC1918, link-local, unspecified, multicast, documentation, and benchmark ranges. Development loopback requires both Development environment and the explicit option.
5. Verify authorization-server metadata is HTTP 200 JSON, bounded, has the exact canonical issuer, and advertises PAR, `private_key_jwt`, ES256 assertion and DPoP algorithms, S256 PKCE, code and refresh grants, code response, issuer response parameter, URL client metadata, and `atproto` scope. Authorization, PAR, token, and optional revocation endpoints may retain their declared query, but redirects and unsafe endpoint hosts are rejected.
6. If a previously working flow fails after about five minutes, start a new discovery/login flow. Endpoint trust is deliberately short-lived. An expired mapping rejects OAuth-shaped POSTs instead of sending them without confidential authentication.
7. For `client_id_mismatch`, callback mismatch, ambiguous-form, invalid-DPoP, or missing-nonce failures, verify the remote server is preserving the published client ID/callback and AT Protocol DPoP contract. Do not relax form, assertion, proof, or nonce validation.
8. During key rotation, keep the previous key as retired until all sessions pinned to its `kid` are expired or revoked. An unknown pinned key must not be silently replaced with the new active key.
9. Do not enable CarpaNet logging or attach raw OAuth bodies to tickets. Never capture authorization codes, refresh/access tokens, assertions, DPoP proofs/nonces, private JWK material, provider response bodies, or user PDS identifiers. Report only the bounded failure category and redacted endpoint class.
10. PDS calls must go through the Infrastructure hardened core-client factory. If `getSession` appears to bypass DNS/redirect controls, check for accidental use of CarpaNet's returned OAuth client and replace it with the application-owned core client.

Transient-store and PDS recovery:

- OAuth state and handoffs use the primary database through the private API. Restore API/database access and the existing OAuth signing authority after an outage; do not retry a consume whose response was lost. Start a new login from the handle form. `atproto-authentication` now verifies a synthetic operational store round trip: disabled is Healthy, unavailable ATProto primary is Unhealthy, and unavailable optional ATProto with explicit Local Identity/Keycloak primary is Degraded. `/alive` does not execute this probe. Persisted encrypted OAuth sessions remain separate from transient state.
- If expired rows accumulate, verify the globally enabled scheduler owns `atproto-transient-cleanup` and inspect bounded cleanup run/row counters. The minute sweep remains registered when ATProto login is disabled; it deletes at most 2,500 rows per table per pass with no expiry grace. A failed probe can leave a tenantless synthetic row that expires after thirty seconds and is removed by this same sweep. Never extend row expiry or replay an old assertion to recover a flow. See [AT Protocol operations](OPERATIONS.md#at-protocol-operations) for exact metric labels and retention boundaries.
- Login requires HTTPS even on localhost. A near-expiry proof cookie causes a bounded `429 Retry-After`; wait for that deadline without clearing a valid cookie under other pending logins. Complete a handoff in the browser and origin that initiated the flow. A missing/wrong proof cannot complete a login and does not consume a valid destination handoff.
- Preserve the native BFF Data Protection key directory in Redis-free hosting; share it across replicas with the same application discriminator and appropriate at-rest protection. An explicitly configured Redis key store remains a Redis dependency until the operator removes that selection. Lost protection keys or the old transient-backend cutover require restarting in-flight logins; there is no memory fallback or compatibility reader.
- A PDS outage can prevent login, refresh, `getSession`, and best-effort remote revocation. Local sign-out must still clear the BFF cookie. Restore public DNS/TLS/PDS availability, then start a new login if refresh reports the durable session invalid, corrupt, revoked, expired, or bound to an unavailable retired key.
- To invalidate a compromised local session, revoke/delete it through the authorized session lifecycle and clear the BFF cookie. Do not edit encrypted session bytes or reuse another user's DID/PDS binding. If remote revocation is unavailable, local removal remains authoritative and the bounded outage result is retained for operators.

Recovery is configuration- and dependency-first: restore the secret resolver or durable stores, correct canonical public URLs/DNS/metadata, retain required retired keys, and start a new flow. Do not bypass endpoint discovery, private-client assertions, DPoP, response limits, or SSRF checks.

### AT Protocol events are missing, pending, retrying, or failed

Symptoms:

- no federated events appear in public discovery;
- a local My Events card remains `AT Protocol publication pending` or `delivery retrying`;
- a card reports `AT Protocol delivery needs attention`;
- an event exists locally but has no PDS record.

Checks:

1. Resolve the effective `federation.atproto_events_enabled` value and lock metadata. The same capability governs both inbound discovery and new eligible outbound enqueue. A disabled or locked-off tenant must show neither federated cards nor new publication work.
2. For inbound discovery, decide whether `Atproto:Jetstream:AllowedDids` should be empty for all public publishers of the exact community collections or contain the intended curated DIDs. With capability enabled, both modes are healthy. Confirm the subscriber lease, last safe cursor, quarantine counts, exact wanted collections, and discovery-cache invalidation without logging DID/rkey/payload values.
3. For outbound publication, confirm the owner enabled only their own `federation.atproto_publish_my_events`, has exactly one linked DID/session for the same tenant and PDS, and the event passed the effective `platform` or `community_lexicon` readiness profile. Administrator enablement never grants personal consent.
4. Confirm the local event publication and immutable outbox row committed. A request handler never calls a PDS, and no recovery procedure may synthesize a PDS record when the local Event is absent. Projection coverage/privacy/size failure intentionally creates no outbox row; do not truncate the description or omit sessions/EAV/aspects/lookups to bypass it.
5. For `pending` or `retrying`, inspect bounded lease age, retry count, next retry time, PDS worker health, and stable failure code. Expired processing leases are reclaimable. Do not manually clear fences or change stable record keys.
6. `session_unavailable`, `session_binding_mismatch`, or `reauth_required` requires the user to reconnect the same AT Protocol account, then update the local event to request publication again.
7. `record_conflict` or `remote_record_missing` means the PDS copy changed or disappeared. Update the local event to request safe reconciliation; do not issue an unfenced direct record write.
8. Provider rate-limit, timeout, or availability failures retry automatically within the row's configured bound. Dead-lettered rows retain only stable codes; raw provider bodies, tokens, DPoP material, and session envelopes must not enter logs, support artifacts, API responses, or the UI.
9. After successful delivery, verify the outbox URI/CID, canonical ownership/presentation, and local Event `AtprotoRecordId` were settled together. A crash after remote success should reconcile the same record key, never create a duplicate.

Local data remains authoritative during every PDS outage or permanent federation failure. Do not delete or roll back a valid local event merely because delivery failed.

### Keycloak `unauthorized_client` during login

Cause:
- the Blazor BFF confidential client secret does not match the `islamu-event-blazor` client secret stored in Keycloak.

Checks:
1. Confirm `KEYCLOAK_BLAZOR_CLIENT_SECRET` is set for the Compose environment. The realm export intentionally contains no confidential client secret.
2. Check `docker compose logs keycloak-init` for successful redacted sync messages. The log must not include raw secret values.
3. Rerun `docker compose run --rm keycloak-init` after changing or rotating `KEYCLOAK_BLAZOR_CLIENT_SECRET`.
4. For disposable Compose development, generate a value with `openssl rand -hex 32`; no default-secret escape hatch exists. Local Aspire generates its own persisted secret parameter when deployment configuration is absent.
5. If the client is missing, verify `docker/keycloak/realm-export.json` imported successfully and that `KEYCLOAK_REALM` matches the imported realm name. Existing Keycloak realms are not overwritten by startup import; reset the disposable Keycloak database volume before expecting realm-export changes to apply.
6. For external Keycloak setup, rerun `/onboarding/auth-provider` bootstrap mode with the same Blazor client ID and the intended runtime client secret. The setup flow updates existing clients by `clientId`; it does not require manually editing the Keycloak UI when the bootstrap credential has client-secret update permission.

### External Keycloak bootstrap fails before contacting Keycloak

Symptoms:
- setup page reports a bad Keycloak URL or unsafe host.
- API returns a safe failure code such as `keycloak_invalid_url` or `keycloak_unsafe_host`.

Checks:
1. Use an absolute `http://` or `https://` Keycloak base URL with no embedded username/password, query string, or fragment.
2. Do not use `localhost`, loopback, link-local, unspecified, or multicast IP literals from the setup form. Use the operator-facing Keycloak DNS name instead.
3. For Compose-managed local Keycloak, prefer the Compose `keycloak-init` service instead of the external bootstrap UI path.

### External Keycloak bootstrap authentication or permission failure

Symptoms:
- setup returns `keycloak_auth_failed`, `keycloak_realm_check_failed`, `keycloak_realm_create_failed`, `keycloak_client_create_failed`, or `keycloak_client_secret_update_failed`.
- Keycloak logs show Admin API `401`, `403`, or rejected client operations.

Checks:
1. Confirm the one-time bootstrap username/password or service-account secret is valid in Keycloak.
2. Confirm the credential can read the target realm, create the realm when using create mode, list clients, create clients, and update client secrets.
3. If using patch-existing mode, verify the realm already exists. Missing realms return `keycloak_realm_not_found`; switch to create mode only if the operator intends ISLAMU to create the realm.
4. If client creation fails with a conflict, rerun bootstrap after confirming the existing client ID is correct. The adapter locates clients by `clientId` before creation and treats existing realms as safe to patch.
5. Do not paste raw Keycloak Admin API response bodies, access tokens, admin passwords, client secrets, or setup secrets into issue reports. Use the safe failure code and Keycloak status code instead.

### External Keycloak bootstrap succeeds but login still fails

Checks:
1. Confirm the Keycloak authority saved by setup is `<base-url>/realms/<realm>` and is reachable from both API and Blazor BFF.
2. Confirm reverse-proxy public origin matches the Blazor client redirect URIs and web origins in Keycloak.
3. Trigger `/bff/auth/refresh-schemes` or restart the Blazor BFF if testing outside the setup UI. The onboarding UI calls the refresh path after successful bootstrap.
4. If `KEYCLOAK_BLAZOR_CLIENT_SECRET` is also configured as deployment-managed, confirm the saved application-managed value is not being overridden by deployment config.

### Post-onboarding Keycloak doctor or sync reports drift

Symptoms:
- the admin auth-provider panel reports missing `offline_access`, missing API audience mapper, missing redirect/web origin entries, or blocked sync operations.
- sync apply is unavailable or returns a blocked plan.

Checks:
1. Run the read-only realm doctor first. Basic mode should verify saved runtime config and OIDC discovery without admin credentials.
2. For drift-aware inspection, enter a temporary Keycloak admin or service-account credential with permission to read realm clients, scopes, roles, protocol mappers, and client settings. ISLAMU uses it only for the active request.
3. Review the sync preview before applying. The plan must be additive; it should not propose deleting a realm, user, group, unrelated client, redirect origin, or operator-managed customization.
4. Before sync apply, confirm a current Keycloak database backup. Apply blocks without backup confirmation and temporary admin credentials.
5. For client-secret rotation, check whether the secret is application-managed or deployment-managed. Deployment-managed secrets must be rotated in environment variables, Infisical, or the owning secret provider rather than overwritten from the app UI.
6. Do not paste temporary admin credentials, Keycloak access tokens, client secrets, raw Admin API response bodies, or screenshots containing secrets into support tickets. Use the safe finding/operation codes from the doctor or sync plan.

## Setup Secret Failures

Symptoms:
- onboarding blocked at `/setup`.
- setup calls return `410`, `400`, or `502/503`.

Checks:
1. API logs: setup mode active vs completed.
2. BFF endpoints:
    - `POST /bff/setup-secret`
    - `POST /bff/setup-secret/sync`
3. ensure browser-supplied `X-Setup-Secret` headers are stripped and ignored. The BFF should forward only resolver output from BFF-owned setup handshake/session state, protected setup cookie, or explicit local/development/bootstrap configuration fallback.
4. BFF setup sessions expire after 30 minutes without setup activity; successful status and synchronization calls extend the session by another 30 minutes. API setup authority does not expire relative to process startup.
5. if the setup page reports `Environment`, re-enter the configured `SETUP_SECRET`. If it reports `Generated`, use the exact logged `docker cp` instruction from the Docker host: split Compose reads `/app/bootstrap/setup-secret`, standalone reads `/app/data/setup-secret`, and a direct unmounted API defaults to `/tmp/islamu-event/setup-secret`. Raw values are never shown in startup output.
6. if startup says the generated path is not writable, provide an explicit `SETUP_SECRET` or a writable single-instance mount. Rolling or multi-replica deployments must use one shared explicit value from their platform secret manager.
7. use `GET /api/System/onboarding-preflight` to inspect non-sensitive launch blockers and operational warnings before retrying completion.
8. for first-run Compose setup, confirm the operator used the service names and ports from [SELF_HOSTING.md](SELF_HOSTING.md), not older `api`/`blazor` examples.

### Terminal Assistant Does Not Start Or Write

- `interactive-terminal-required`: run `Event.SetupAssistant.Terminal` directly
  in a TTY. Pipes, redirects, captured standard streams, and secret arguments are
  rejected deliberately.
- `protected-output-unavailable`: use Linux, macOS, or FreeBSD and a writable
  current directory. The target requires owner-only `0600` create-new semantics
  and has no plaintext fallback.
- Existing output file: choose a new safe filename or remove the old file only
  after preserving it deliberately; the assistant never overwrites.
- Resize-only screen: enlarge the terminal to at least 40 columns by 17 rows.
- Arabic text displays but shaping/order is poor: select a terminal/font with
  Unicode and RTL support. Terminal.Gui screen-reader, braille, RTL shaping, and
  scrollback erasure remain unverified and are stated in the UI.

## Secret Provider Unavailable

Symptoms:
- `secret-resolver` is unhealthy with `providerState=unavailable`.
- a required capability fails closed, or an optional integration reports
  `Unavailable`, `Unauthorized`, or `Invalid` instead of `Unconfigured`.

Checks:
1. Confirm `SECRET_PROVIDER` explicitly selects `Environment` or `Infisical` and
   that every runtime binding uses that same authority.
2. For `Environment`, verify the deployment platform forwards the documented
   variable to the affected process. Do not print it or use shell tracing.
3. For `Infisical`, verify network reachability and the deployment-owned
   `INFISICAL_URL`, project, client, and environment inputs at their source. Do not
   copy response bodies, coordinates, or credentials into logs or support tickets.
4. Repair the selected authority. Never add an environment/appsettings/database
   fallback to turn the failure into a clean miss.
5. Metadata writes invalidate local cache entries. For urgent changes, restart or
   drain every replica; otherwise allow the documented five-minute process-local
   freshness bound before declaring recovery.
6. Recheck `/health`. Recovery evidence is `providerState=available` and the
   capability-specific healthy state, never a raw secret read.

## Setup Live Enrollment Or Secret Write Failed

Symptoms:
- an enrollment is expired, revoked, or rejected as stale;
- readiness is not `Ready`, or a write returns a bounded authorization/provider
  failure;
- the client cleared its enrollment after cancellation or transport failure.

Checks:
1. Keep the release capability manifest disabled; backend availability is not
   release authorization and there are no saved profiles to recover.
2. Revoke the affected enrollment and clear the client's in-memory capability.
   Do not reuse a capability, bearer, operation key, or source identity.
3. Repair the deployment-selected Infisical authority and the target-local
   binding. Never add Environment, User Secrets, appsettings, or database
   fallback and never copy provider coordinates into a ticket.
4. Create a fresh enrollment, follow only the returned HAL affordance, confirm
   value-free readiness, and submit the value once. Confirmation is the bounded
   receipt/provider audit evidence; secret readback is intentionally impossible.

## Secret Rotation Partial Activation

Symptoms:
- one replica reports a local `Activated` acknowledgement while another is
  missing, `Rejected`, or `Failed`;
- the stale deadline passes before every declared replica acknowledges the same
  opaque attempt ID;
- a provider does not support simultaneous old/new credentials.

Checks:
1. Do not revoke the previous credential or call the deployment converged from one
   process. Local acknowledgements contain no secret values and prove only that
   replica's candidate/activate check.
2. For `overlap-rollout`, keep the previous provider credential valid, compare the
   declared replica set with acknowledgements for the same attempt, and rollback to
   the previous credential if any replica rejects or fails.
3. At the stale deadline, drain the missing replica and mark the rollout failed
   closed. Repair it under the previous credential before starting a new attempt.
4. For `coordinated-restart` or a provider without overlap, enter maintenance,
   restore the previous deployment value when needed, restart every consumer, and
   require readiness from the full replica set before reopening traffic.
5. For `unsupported-live`, stop the operation and follow the consumer-specific
   replacement/restart runbook. Never add a secondary secret source as break glass.
6. Revoke the previous provider credential only after convergence and any pinned
   version/session references have expired or been retired.

### Onboarding Recovery Matrix

Refresh before every recovery action. The setup UI is a projection of server status, provider verification, and preflight state; browser history or a cached task list is not recovery evidence.

| Scenario | Detection | Safe action | Operator, rotation, or backup requirement |
|---|---|---|---|
| Invalid secret or inactive BFF setup session | Validation fails, the BFF setup session has been idle for more than 30 minutes, or the API returns the setup-secret failure response before authentication. | Re-enter the explicit secret, or retrieve the current generated file again from the Docker host while onboarding is incomplete. Container recreation without a volume may replace the generated value. Do not retry blindly, search output for a raw value, or move it into browser storage. | Rotate an explicit secret at its owning source if disclosure is suspected. Restrict Docker-host access for a generated secret; its file is removed after onboarding. No data restore is required. |
| Provider verification interrupted | The task overview still reports provider verification incomplete/unknown after a redirect, timeout, or lost response. | Refresh authoritative state, then rerun the same verification. Existing provider resources are located and repaired additively. | Rotate temporary provider admin credentials when their handling is uncertain. Take a provider/database backup before any apply operation that changes shared provider state. |
| Authorization provider unavailable | Provider status or preflight reports the configured PDP/Admin API unavailable. | For application-managed configuration, restore Cerbos or explicitly save Local. For deployment-managed `cerbos`, fix `AUTHORIZATION_PROVIDER`/Cerbos deployment values and retry; the browser cannot override deployment intent. | Operator intervention is required. Cerbos remains fail-closed. Do not add or use an inventory endpoint or arbitrary policy-decision test as a recovery shortcut. |
| Blocking preflight check | Preflight classifies an item as blocking. | Fix the named dependency and refresh. Do not bypass completion. Ordinary warnings remain non-blocking; a serious warning may require explicit acknowledgement. | Follow the linked subsystem runbook. Restore from backup only when the blocker identifies corrupted or missing persisted state. |
| Completion submitted repeatedly | The server reports completed/already completed, the response was lost after launch, or retry reaches the completion guard. | Treat the completed state as authoritative and follow the mode-specific handoff. Completion is idempotent; do not reset resources to make it run again. | No secret rotation or restore is needed unless a separate compromise/failure is detected. |
| Refresh after partial success | The refreshed overview shows some tasks complete and a remaining provider/preflight failure. | Preserve successful resources, repair the failed dependency, and retry only the incomplete server-guarded operation. | Back up affected provider/database state before a repair apply. Destructive delete/reimport requires explicit approval. |
| Setup locked after completion | `/setup` reports completed/locked or setup-secret calls are rejected after launch. | Confirm the configured `SETUP_SECRET_FILE` is absent, remove any host copy made with `docker cp`, then use authenticated instance administration; in multi-tenant mode use `/admin/instance`, and use a tenant-scoped flow only for tenant onboarding. | Never unlock setup by editing database state. A genuine failed-launch recovery requires operator intervention and a verified backup/restore plan. |

## Tenant Resolution Problems

Symptoms:
- wrong tenant branding/data.
- tenant-scoped data appears empty.

Resolution order in API:
1. trusted `X-Tenant-Slug` header from the BFF
2. custom domain
3. subdomain
4. unresolved multi-tenant request returns `404`

Checks:
- trusted forwarded-header processing and normalized request host resolution.
- `deployment.mode` value (`SingleTenant` vs `MultiTenant`).
- for API-key callers, check whether the request hit `Tenant mismatch` (`404`) or `API key authentication failed` (`401`) in `ApiTenantPostAuthenticationMiddleware`.

## Missing HAL Links

If `_links` are missing:
- confirm request did not include `Prefer: return=minimal`.
- link pruning can be authorization-driven (user lacks action permission).
- permission-bound links are evaluated server-side through the HATEOAS authorization pipeline. If Cerbos/local authorization denies or batch evaluation fails, those links are omitted fail-closed.
- admin/sync affordances are also filtered server-side before link materialization. Do not re-enable hidden actions in Blazor by checking client-side roles or claims.
- for sync/history/status navigation links that are auth-only, verify the caller is authenticated and the endpoint is not returning a minimal representation.

## Control-Plane Operations Warnings

The instance control plane exposes warning codes with operator remediation text. Use the code first, then follow the linked status card or backing worker logs.

| Code | What it means | Operator action |
|---|---|---|
| `general_outbox_due_backlog_capped` | The general outbox due backlog reached the bounded reporting cap. | Verify the outbox worker is running and inspect due rows before raising the reporting cap. |
| `general_outbox_failures_present` | General outbox rows are failed or dead-lettered. | Fix the downstream handler or payload issue, then replay from an operator-approved path. |
| `email_provider_missing` | SMTP is not configured for platform email delivery. | Configure non-secret SMTP policy in instance settings and provision credentials in the selected secret authority. |
| `email_dispatch_dead_letters` | Email dispatch has dead-lettered rows. | Review dead-lettered dispatch rows, fix provider/configuration failures, and replay only after confirming recipients and payloads. |
| `email_dispatch_stale_processing` | Email dispatch rows are stuck in processing. | Check the worker lease/heartbeat and restart the worker before replaying stuck rows. |
| `email_dispatch_due_backlog` | Email dispatch due backlog exceeds the configured threshold. | Scale or restart dispatch processing and check SMTP provider throttling before increasing thresholds. |
| `moderation_reporting_provider_sync_failures` | Moderation reporting provider-sync links have failed rows at or above the configured threshold. | Review failed reporting provider sync records and provider configuration from an operator-approved path. Do not paste provider payloads, endpoint URLs, API keys, webhook secrets, raw provider errors, report evidence, or callback signatures into tickets or logs. |
| `moderation_reporting_provider_sync_stuck` | Moderation reporting provider-sync links have pending rows older than `Reporting:Health:StuckProviderSyncMinutes`. | Verify the report provider sync dispatcher is running, then check provider connectivity and tenant/instance routing locks without exposing provider identifiers or payload material. |
| `storage_provider_unavailable` | The configured storage provider reports unavailable. | Verify storage provider settings, credentials, bucket/root reachability, and health checks before upload-heavy operations. |
| `deployment_mode_not_multi_tenant` | Control-plane overview was reached outside multi-tenant mode. | Switch to a configured multi-tenant deployment; do not toggle mode casually from runtime settings. |
| `public_host_missing` | No public origin or instance base domain is configured. | Set `PublicBaseUrl` or the instance base domain before creating tenant DNS records. |
| `authentication_provider_missing` | No authentication provider is configured. | Complete authentication-provider setup and verify OIDC discovery before tenant onboarding. |
| `authorization_provider_missing` | Manual provider intent is unsaved, or explicit Cerbos reconciliation has not completed. | With blank intent, save Local or configure Cerbos. With deployment-managed Cerbos, repair the PDP/Admin API configuration and retry background reconciliation. |

## Cerbos / Authorization Provider Issues

### Deployment Provider Selection

- `AUTHORIZATION_PROVIDER` accepts blank, `local`, or `cerbos`. Invalid explicit values fail startup options validation.
- Blank means manual onboarding. Cerbos endpoint or credential variables are bootstrap prerequisites and do not select the provider; Local RBAC remains the default until the operator opens the advanced Cerbos disclosure and saves it.
- `local` is deployment-owned, skips the provider-choice page, and performs no Cerbos call.
- `cerbos` is deployment-owned, skips the automatic choice page while background work is pending or ready, and selects Cerbos at runtime immediately. The API worker retries PDP verification before publishing the package to the instance Admin API. Until both succeed, configured status remains false and authorization fails closed. After the retry bound is exhausted, use the instance authorization task to view safe failure guidance and retry after fixing deployment settings.

### Instance Cerbos PDP Unavailable

- When the instance provider is `cerbos`, runtime authorization fails closed. It does **not** fall back to local RBAC.
- Check the PDP gRPC endpoint and app readiness health. Switch to local mode only as an explicit operator action.

### BYO Tenant PDP Failure

- Any BYO PDP failure activates provider-instance safe mode and denies non-instance-admin checks.
- There is no fail-open configuration; the `cerbos.failure_mode` setting was deleted. A BYO PDP outage always fails closed into safe mode.
- BYO config resolver failures also activate provider-instance safe mode.

### Blank Custom PDP Endpoint

- `cerbos.mode=custom_endpoint` with a blank PDP endpoint remains BYO mode. It preserves the tenant failure mode and any explicit BYO Admin API config instead of falling back to the instance PDP.
- Configure the PDP endpoint, change the tenant mode, or use manual ZIP/Admin API package operations while runtime authorization remains in safe mode.

### Admin API Package Sync Failure

- Use the safe issue code first: Admin API not configured, auth failure, unavailable/rejected package, reload failure, or package-status unknown.
- Verify endpoint TLS/safety and credentials without logging or copying raw secrets into support artifacts.
- For blank/application-managed provider intent, if Admin API sync is unavailable, download the manual ZIP package from setup/admin UI and install it with `cerbosctl put`.
- With explicit `AUTHORIZATION_PROVIDER=cerbos`, missing or rejected Admin API credentials make reconciliation fail safely and leave readiness blocked. Fix the deployment secrets, or enter a complete one-time username/password pair in the locked remediation page and retry. The pair is not saved and is cleared after the request.
- If onboarding navigation appears stuck, inspect runtime authorization first: HAL capability planning must call only the Cerbos gRPC PDP. Admin API calls belong only to explicit sync/status operations and time out after 10 seconds; a 100-second `GET /admin/policies` inside `hateoas.capability_planning` indicates an outdated deployment.
- Troubleshooting scope is endpoint verification, package download/sync, and the configured local fallback only. There is no supported Cerbos resource-inventory or arbitrary policy-decision test API.

## 429 / 504 Responses

`429`:
- triggered by API rate limiting policies (`Global`, `Authenticated`, `Write`, `PublicIngestion`, `SetupSecret`, `AnalyticsRelay`, `AiAssistant`, `EventOpenGraphImage`).
- inspect `Retry-After`, `X-RateLimit-Limit`, and `X-RateLimit-Remaining` headers and caller behavior.
- Open Graph saturation appears as `429` from `/api/event/public/{slugCode}/og-image` with rate-limit headers. `Retry-After` may be absent because concurrency rejection has no fixed retry period.
- If sustained render load justifies more parallel work, raise `RateLimiting:EventOpenGraphImage:ConcurrencyLimit` carefully while watching API CPU and memory. Each API process applies its own limit.

`504`:
- request timeout policy exceeded (`Default`, `Lookup`, `Complex`).
- verify endpoint timeout category and long-running query behavior.

## Analytics And Tracking Issues

Symptoms:
- pageviews are missing in dashboards.
- relay requests return `204`, `4xx`, or appear absent.
- analytics appears disabled for one tenant but not another.

Checks:
1. Confirm resolved `analytics.enabled`, `analytics.provider`, `analytics.transport_mode`, and `analytics.endpoint_url` for the affected tenant.
2. If using `direct` or `proxy`, inspect browser network failures and CSP violations before assuming provider-side breakage.
3. If using `relay`, confirm the browser can reach `POST /api/a/t` and that the endpoint is not being rate-limited.
4. Remember that analytics failures should degrade to no-op behavior; if user-facing routes break, the issue is larger than analytics transport.
5. If the operator wants an emergency stop, switch to provider `none` or disable analytics rather than removing scripts manually.

## Outbox Processor Issues

**Symptoms:** Messages stuck in `Pending` status, events not being dispatched.

Checks:
1. Verify `OutboxProcessor:Enabled` is `true` in configuration.
2. Check logs for `OutboxProcessor` — look for polling activity and dispatch errors.
3. Query `outbox_messages` table: `SELECT status, COUNT(*) FROM outbox_messages GROUP BY status`.
4. If messages are `DeadLettered`, check `last_error` column for root cause. Dead-lettered messages stay in DB indefinitely for manual review.
5. Verify `IOutboxMessageDispatcher` registration — the general outbox path should resolve `CompositeOutboxMessageDispatcher`, which routes internal notification fanout, moderation fanout, and report provider sync messages. Retired external broker event types should fail closed instead of being marked complete.
6. Check `MaxRetryCount` setting — messages retry with exponential backoff before dead-lettering.

## Privacy Erasure Issues

**Symptoms:** `DELETE /api/user` returned `202` but the receipt-status route still reports `provider_pending`, `/health` shows privacy-erasure degradation, replay lag grows, or provider work/dead letters remain uncleared.

Checks:
1. Query `/health` and inspect the `privacy-erasure` check only. It reports `topology`, `restoreReplayProtection`, `authorityHighWater`, `authorityRetainedFloor`, `replayCaughtUp`, `replayReasonCode`, `providerDue`, `providerUnknown`, `providerDeadLettered`, `cacheConvergenceIncomplete`, and `cacheConvergenceDeadLettered`. Do not copy subject ids, locators, payloads, endpoints, or exception text into tickets.
2. For user-facing status, call `GET /api/privacy-erasure/status` with `Authorization: ErasureReceipt <receipt>`. Missing, invalid, wrong, and expired receipts all return `401` and must be treated identically.
3. If `replayCaughtUp` is false, the retained authority checkpoint is behind the replay fence; allow replay to finish before retrying the request. The API should still return bounded status, not raw replay detail.
4. If `providerUnknown` is non-zero, use the provider-work reconciliation path; `Unknown` is the expected ambiguous-ack state. `providerDeadLettered` and `providerDue` indicate operator action or worker lag, not user-visible failure detail.
5. If `cacheConvergenceIncomplete` or `cacheConvergenceDeadLettered` is non-zero, the erased user may still be visible through stale cache until the outbox-backed cache invalidation work catches up; do not republish subject data to force it.
6. For the default `EmbeddedSqlite` topology, verify the absolute local `PrivacyErasureAuthorityEmbedded:Path`, its dedicated volume, `WriterReplicaCount=1`, and `BusyTimeoutSeconds` in `1..300`. A network filesystem, symlink, missing WAL companion during restore, failed `quick_check`, or permissions broader than directory `0700` / files `0600` is an authority-storage incident; stop the writer and restore the authority backup independently of the primary database.
7. For `ExternalDatabase`, verify the distinct structured PostgreSQL endpoint and runtime/migrator roles under `PrivacyErasureAuthorityDatabase:*`. Do not substitute a raw connection string or point it at the primary physical database.
8. Verify the topology's operator backup units and reported `restoreReplayProtection` value:

   | Topology | Backup units | Expected `restoreReplayProtection` |
   |---|---|---:|
   | `EmbeddedSqlite` | Primary database and dedicated authority-file backups | `true` when the authority file is excluded from the primary restore |
   | `CoLocated` | One primary database backup containing authority rows | `false` |
   | `ExternalDatabase` | Primary database and independently managed external PostgreSQL authority backups | `true` when the external authority has an independent restore lifecycle |

9. Interpret replay codes without bypassing the gate:
   - `replay_pending`: wait for the normal startup replay to reach the high-water mark.
   - `stale_restore_below_retained_floor`: the primary restore is older than retained authority evidence. Keep the newest authority unchanged and restore a verified primary backup at or above the floor.
   - `checkpoint_ahead_of_authority`: authority rollback or loss is possible. Keep traffic stopped and restore a verified authority artifact that reaches at least the checkpoint and preserves every later fact.
   - `sequence_gap_detected`: treat the authority as corrupt/incomplete. Restore verified authority evidence; never renumber, synthesize, or delete facts/checkpoints.
10. Before any retention apply, run the dry-run evaluation with the complete explicit PII-free legal-hold sequence set. Confirm the projected floor is not newer than every primary backup the operator still claims is restorable.

## Email Dispatch Issues

**Symptoms:** Registration confirmation email does not arrive, `email-dispatch` readiness is degraded/unhealthy, or RabbitMQ dispatch/DLQ counts grow.

Checks:
1. For local development, open Mailpit at the Aspire-discovered UI endpoint or Compose default `http://localhost:8025`. Non-isolated Aspire normally uses SMTP `localhost:1025`; isolated Aspire assigns dynamic ports, so run `aspire describe mailpit --apphost Explore.AppHost/Explore.AppHost.csproj --format Json` and verify API `email.smtp_port` matches the current Mailpit SMTP endpoint. Compose uses `mailpit:1025` from API containers.
2. Check `/health`: `smtp` covers configured SMTP/Mailpit connectivity, `email-dispatch` covers Basic Dispatch trigger readiness, `email-dispatch-retention-cleanup` covers the retention worker posture, and `email-dispatch-rabbitmq` covers optional broker topology only when RabbitMQ mode is enabled. If Mailpit is stopped in FullLocal, API `/health` should return HTTP 503 with `smtp` Unhealthy. The SMTP readiness probe is bounded to five seconds; the 2026-07-04 local proof returned in `5.014s`.
3. Inspect HAL-gated EmailDispatch admin status before changing rows. Use only emitted `_links`. `Unknown` never exposes generic replay: reconcile it as `Delivered` or `NotDelivered` only with provider evidence, or use `resolve-without-replay` when the outcome cannot be proven and the work must be abandoned. Redacted and `Processing` rows expose no mutation affordances.
4. Query `email_dispatch_outbox` by status and tenant. `Unknown` rows are inspectable crash-window outcomes; `DeadLettered` rows require operator review; `Skipped` rows are terminal preference/compliance outcomes.
   - For planned lifecycle rows, compare linked `NotificationDelivery` and immutable occurrence/policy versions. Do not replay `ContentRedactedAt`, consent-withdrawn, superseded, tenant-deleted, or post-handoff `Unknown` work until the authorized reconciliation surface says it is replayable.
   - If one tenant dominates the backlog, inspect fair-selection, per-tenant concurrency/rate limits, and high/low watermarks before increasing global throughput. Optional reminders should defer before required cancellation/moderation work.
   - Rows with `last_failure_category = 'smtp_rate_deferred'` did not call SMTP and did not consume an attempt. Compare `email_dispatch_processor_states.smtp_refill_at` with the tenant row in `email_dispatch_tenant_controls`; the later exhausted refill boundary controls `next_attempt_at`. Do not manually create attempt/receipt evidence for these rows.
   - An unfenced stale lease becomes `RetryScheduled`; a stale row with the current `provider_handoff_started` attempt or a processing receipt becomes `Unknown`. If those classifications look wrong, inspect the exact outbox lease/attempt graph before using a HAL replay action.
   - If `globalPaused=true`, inspect `GET /api/admin/email-dispatch/control`. Resume only after the incident is contained. A temporary global rate override is visible as a boolean in public health and as its sanitized value in the authenticated control resource.
   - For a compromised tenant sender, pause that tenant immediately with `PUT /api/admin/email-dispatch/tenants/{tenantId}/pause?reason=...`; use the tenant suppression/redaction lifecycle when tenant deletion or compromise requires queued content removal. Do not pause every tenant unless the provider/instance itself is unsafe.
   - Distinguish tenant SMTP failure from instance failure: one tenant failing while others send points to tenant override/governance or tenant pause; all tenants failing plus `smtp` health failure points to instance credentials, DNS/TLS, provider outage, or global pause/rate control.
   - Before changing retention, set `EmailDispatchRetention:DryRun=true`, restart the worker, compare only aggregate eligible counts/cutoff, then restore mutating mode. Dry-run must not be used as proof of message delivery.
5. In RabbitMQ mode, verify broker connectivity, dispatch/DLX/parking topology, and bounded logs. Broker payloads must contain only pointer fields, never recipient, subject, body, SMTP credentials, provider IDs, or raw errors.
6. Use `docs/EMAIL_NOTIFICATIONS.md` for focused Mailpit and RabbitMQ verification commands.

**Coop callback retained but no moderation decision occurs:**

1. Check `/health/webhooks/coop-effects` for disabled processing, due backlog, stale leases, or a PostgreSQL readiness failure.
2. Use the authenticated tenant-scoped `GET /api/admin/incoming-webhook-effects/status` surface. Do not query or copy raw callback bytes into tickets or logs.
3. `Pending`/`Failed` waits for `NextAttemptAt`; `Processing` with an expired lease is recovered by the next claim; `DeadLettered` requires the permanent input/configuration problem to be corrected before operator redrive.
4. Confirm the retained inbox message still has payload bytes and its replay window has not expired. Cleanup deliberately makes redrive unavailable after that boundary.
5. Use only the HAL `redrive` action and the row's current processing generation. Generation conflict means another operator/worker already changed the row; reload status instead of retrying stale input.
6. Do not directly invoke `ProcessCoopDecisionCallbackCommand` or mutate pointer/receipt rows. The durable worker is the authority for fenced execution and command-success settlement.

Duplicate callbacks, duplicate pointers, dispatcher replay, and expired-worker replay should settle idempotently. If a completed report case appears reopened, treat that as a moderation state-machine incident: retain the safe effect/audit identifiers, stop redrive, and investigate the decision command's stale/out-of-order guard without exporting provider payloads or raw errors.

## Aspire Detached Lifecycle Issues

**Symptoms:** `aspire start --format Json --isolated` or `aspire run --detach --format Json --isolated` returns AppHost/CLI PIDs and a dashboard URL, but an immediate `aspire ps --format Json` returns `[]`, `aspire describe` reports no running AppHost, and the returned AppHost PID no longer exists.

Checks:
1. Confirm the foreground proof path works first: `aspire run --apphost Explore.AppHost/Explore.AppHost.csproj --isolated`. Use that path for launch runtime evidence.
2. Inspect the detached child log printed in the JSON output. The 2026-07-04 local `13.4.6` reproduction reached resource readiness and logged `Notifying AppHost startup readiness`, then the process disappeared before `aspire ps` could inspect it.
3. Verify `Explore.Blazor` builds if AppHost startup fails during the embedded admin-shell compilation; control-plane host routing lives under `Explore.Blazor/Components/ControlPlane/`.
4. Treat repeated empty `aspire ps` after detached startup as an Aspire CLI/tooling lifecycle issue, not as proof that the API or Blazor cannot run. Foreground FullLocal startup, health, and public smoke are the current trusted readiness evidence.
5. Re-test detached mode only after updating the Aspire CLI or when explicitly investigating CLI lifecycle behavior.

## Footer Settings Issues

**Symptoms:** Footer not rendering, template changes not visible, governance locks not applying.

Checks:
1. Verify `footer.enabled` setting is `true` for the tenant.
2. Check `footer.template` value matches a known template: `standard-3-col`, `standard-2-col`, `minimal`, `community`.
3. In multi-tenant mode, check instance governance locks (`footer.lock_tenant_template`, `footer.lock_tenant_link_groups`, etc.) — locked settings prevent tenant overrides.
4. If social links not showing, verify `footer.show_social_links` is `true` and `footer.social_links` JSON array is valid.
5. After changing footer settings via API, the public config endpoint (`GET /api/footer/config`) may be output-cached — wait or clear cache.

## Secret Provider Issues

**Symptoms:** Application fails to start, secrets not loaded, connection strings missing.

Checks:
1. Check `SecretProvider:Provider` — it must explicitly be `Environment` or `Infisical`.
2. Provider configuration is validated at startup. Check logs for `SecretProviderOptionsValidator` errors before changing secret values.
3. For Infisical, verify the deployment-owned `INFISICAL_*` bootstrap inputs. Do not paste their values into logs, tickets, commands, or application configuration.
4. Check health endpoint: `/health` includes the `secret_provider` check — `Degraded` after 1-2 failures, `Unhealthy` after 3+.
5. If refresh is enabled, check `secrets_refresh_failures_total` Prometheus metric for recurring failures.
6. Key mapping: Infisical/domain secret names use canonical `SCREAMING_SNAKE_CASE` names such as `STORAGE_S3_ACCESS_KEY_ID`; private credentials do not use .NET double-underscore aliases. Primary and external-authority database credentials are discrete structured role values such as `DATABASE_RUNTIME_USERNAME` / `DATABASE_RUNTIME_PASSWORD`, not URL-form connection strings; see [SECRETS.md](SECRETS.md).
7. `secret_authority_unauthorized`, `secret_authority_invalid`, and
   `secret_authority_unavailable` are intentionally value-free. Repair the selected
   provider and restart; never switch authority or add an environment value as an
   emergency fallback without an explicit deployment change.

## Storage Readiness Or Upload Failures

Symptoms:
- API `/health` reports the `storage` check as unhealthy.
- Upload sessions fail before bytes are accepted.
- Metadata-backed downloads return missing-object behavior.

Checks:
1. Confirm the selected provider in instance storage settings. Local-first deployments should not require the Compose `storage` profile or S3 credentials.
2. For local-first storage, verify the API process can read/write `Storage:Local:RootPath`. Compose defaults to `/app/storage-data/local` mounted on the `local_storage_data` volume.
3. For optional S3-compatible mode, verify non-secret `s3.*` governance points to the intended endpoint/bucket and both credentials resolve from the selected authority.
4. Use the instance storage provider test action or `/health` response failure code; do not expose host filesystem paths, bucket names, object keys, access keys, or raw provider errors in tickets.
5. If metadata exists but downloads fail, run reconciliation in dry-run mode and compare the reported missing-object/orphan counts before changing lifecycle state.

## Storage Reconciliation Drift

Symptoms:
- `storage-reconciliation` health is degraded or operators see recurring dry-run drift counts.
- Local files exist on disk without matching metadata.
- Metadata points to missing backing objects.

Checks:
1. `StorageReconciliation:DryRun` defaults to `true`; dry-run reports drift without mutation.
2. Before enabling mutations, confirm backups include the selected primary database plus `local_storage_data` or the selected S3-compatible object store from the same release manifest. Back up the privacy-erasure authority independently.
3. To quarantine missing metadata/object mismatches, set `StorageReconciliation:DryRun=false` and only the needed quarantine flag.
4. To physically delete delete-eligible objects, also set `StorageReconciliation:DeleteQuarantinedObjects=true`; provider delete is idempotent, but metadata is soft-deleted afterward.
5. If mutations ran against the wrong environment, stop write traffic, turn dry-run back on, restore database and object-storage backups together, then rerun dry-run reconciliation.

## Heavy Moderation Image Deletion Pending

Symptoms:
- Heavy event moderation returns a pending image-deletion retry failure after the event was redacted.
- Storage delete metrics show failed provider deletes for event-owned `delete_requested` rows.

Checks:
1. Treat the event redaction as committed. Public event reads and image foreign keys should already be unavailable; do not restore event content to retry image deletion.
2. Check `/health` storage readiness and provider configuration for the selected local or S3-compatible provider.
3. Re-run the heavy-redaction command for the same event after provider readiness is fixed. The command is idempotent for already-heavy-redacted events and retries remaining event-owned `delete_requested` image rows.
4. If command retry is not possible, use storage reconciliation only after verifying backups and intentionally enabling destructive mutation flags. Reconciliation can delete eligible `delete_requested` metadata through the provider abstraction.
5. Support tickets may include bounded failure category, provider name, tenant id, event id, and storage object id. Do not include object keys, filenames, filesystem paths, S3 endpoints, bucket names, credentials, raw provider responses, or raw exception text.

## Stripe Checkout Or Payment Reconciliation

Symptoms:
- Payment remains `Created`, `Processing`, `Unknown`, or `NeedsReconciliation`.
- Checkout issue returns `429` or a bounded unavailable response.
- `/health` reports `payment-reconciliation` as Degraded or Unhealthy.
- Split BFF cannot open the constant Checkout route.

Checks:
1. Verify `PublicBaseUrl` is the public HTTPS base URL with no query, fragment, or credentials. A normalized application subpath is supported and must match the deployed PathBase. An invalid value defers new Checkout dispatch but does not stop free registration or authoritative reconciliation.
2. Verify `Payments__Stripe__Mode` matches both the `sk_test_`/`sk_live_` platform secret and signed webhook `livemode`; verify the Connect endpoint uses its dedicated webhook secret and pinned API version.
3. Verify `Payments__Stripe__AllowedCheckoutHosts__0` is an exact host such as `checkout.stripe.com`, not a wildcard, URL, or suffix pattern.
4. In Split topology, verify `ConnectionStrings:cache` reaches Redis. Payment tickets do not use the general distributed-cache memory fallback; restore Redis and issue a fresh ticket. Standalone does not require Redis for this flow.
5. Confirm Quartz contains `payment-reconciliation-drain` and the scheduler is enabled. Inspect bounded aggregate job output and `/health`; do not place order, account, provider object, request, capability, or secret values in tickets or logs.
6. Leave `Unknown` attempts in place. Restore provider connectivity and allow same-idempotency reconciliation; never recreate blindly and never use `/payments/checkout/success` or `/cancel` navigation as payment evidence.
7. Treat `parked`, `payment_reconciliation_money_mismatch`, duplicate succeeded attempts, or persistent configuration blocking as operator review conditions. Stop new paid sales for the affected scope while preserving webhook intake and reconciliation.

## Fair Return Waitlist Orchestration

- **Queue does not allocate:** confirm paid-sale control is active, the policy is enabled, and supply and entry match all ten commercial-equivalence dimensions. Do not relax one dimension or rewrite snapshots.
- **Effect remains `processing`:** compare its lease expiry with database UTC. An expired lease is reclaimed by `fair-return-orchestration`; do not clear the fence or operation UUID manually.
- **Effect repeats `replacement_payment_unknown`:** preserve the stable provider idempotency key and reconcile the referenced `PaymentAttempt`. Never create a second replacement payment or refund intent.
- **Refund is absent:** verify `ReplacementPaymentSettledAt` exists on `WaitlistPaymentIntent`. The missing refund is correct until that fence is durable.
- **Dead letters:** inspect fixed failure code and aggregate health counts. Durable rows contain no participant or provider payload; correlate through support-safe operation/effect UUIDs.
- **Browser action missing:** inspect HAL `_links` and paid-sale stop state. UI role/claim checks are not an authority source.

## Upgrade Or Restore Regressions

Symptoms:
- API starts but data is missing or from the wrong environment.
- Keycloak login works but users or clients are missing.
- Object downloads fail after a restore.
- Migrations ran during startup and rollback is being considered.

Checks:
1. Stop write traffic before repeated restore attempts.
2. Compare the release manifest, database dump timestamp, object storage snapshot, and secret/config snapshot from [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md).
3. Verify the configured application primary database, the independent privacy-erasure authority, and Keycloak PostgreSQL were restored from their intended snapshots. For embedded authority SQLite, restore the database together with its WAL state while all writers are stopped; never replace it with the primary SQLite file.
4. Verify `Storage:Local:RootPath` points to restored local data, or that non-secret `s3.*` governance points to the restored bucket and credentials resolve from the selected authority when S3-compatible mode is selected.
5. If MigrationService already ran, do not manually edit provider-specific migration files, snapshots, or history tables; decide rollback vs corrective migration using the rollback matrix in [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md).

## Configuration Manifest Startup Failed

The owning process exits before traffic when an explicit manifest cannot be
read, validated, or applied. In split topology inspect
`event-migrationservice`; in Standalone inspect the combined host. Do not
enable bootstrap on API replicas.

| Stable code family | Check |
|---|---|
| `configuration_manifest_mode_invalid` | Use exact `Off`, `ValidateOnly`, or `Bootstrap` casing |
| `configuration_manifest_path_invalid` | Supply an absolute path visible inside the owning process |
| `configuration_manifest_file_missing` / `configuration_manifest_file_unreadable` | Confirm the read-only mount, non-root directory traversal, and file read permission |
| `configuration_manifest_file_not_regular` / `configuration_manifest_file_symlink_not_allowed` | Mount one regular file; symlinks, directories, and device files are rejected |
| `configuration_manifest_empty` / `configuration_manifest_too_large` | Supply non-empty JSON within the documented 4 MiB bound |
| `configuration_manifest_json_*` / `configuration_manifest_duplicate_property` | Correct strict UTF-8 JSON and remove duplicate or unknown properties |
| `configuration_manifest_instance_already_bootstrapped` | Restore the recorded instance section unchanged and use Day 2 administration for instance changes; reset only a disposable development instance |
| `configuration_manifest_tenant_conflict` / `configuration_manifest_paid_policy_*` | Correct the new tenant section or current instance authority, then rerun the unchanged instance section |
| contract, catalog, or preflight failure | Validate against the shipped schema, then correct unsupported keys, document versions, locks, or unsafe complete policy state |

The diagnostic intentionally reports a digest prefix and stable code rather
than the full path or values. Use `docker compose config` to confirm that only
`event-migrationservice` has the manifest environment and read-only mount.
After correcting a pre-commit failure, run `ValidateOnly`, then rerun the same
topology owner. Existing tenants remain wholesale skips. A same-section rerun
uses fresh Day 2 instance authority and never replays historical instance
values. If an operation ID was already recorded as applied, do not delete audit
rows; restart normally so pending outbox effects can be delivered.

If the original source is lost, use the operation ID, manifest name, digest,
and changed-key codes to locate the reviewed file in operator version control
or backup; audit intentionally stores no values. After database restore, restore
the matching manifest and bootstrap/audit state from the same recovery point
before traffic. If failure evidence cannot be persisted, treat startup as
failed and restore database availability. The complete state/action matrix is
in [SELF_HOSTING.md](SELF_HOSTING.md#operator-stateaction-matrix).

Set `CONFIGURATION_MANIFEST_MODE=Off` and recreate the owning process to
disable future startup processing. This preserves applied data and audit
evidence. Removing a convention file is a no-op only when no explicit path is
configured.

## Configuration Import Or Tenant Migration Failed

Use the session/operation ID and stable code; never copy artifact contents,
access tokens, transfer nonces, proofs, or configuration values into support
logs.

| Stable code or state | Safe recovery |
|---|---|
| `configuration_import_expired` / `configuration_import_cancelled` / `configuration_import_replayed` | Create a fresh session from the retained source and preview again |
| `configuration_import_stale_preview` | Refresh and review the new target revision, mappings, warnings, and approvals; do not retry apply unchanged |
| `configuration_import_target_mismatch` / `configuration_import_token_invalid` | Confirm the authenticated route and header-only capability; do not disclose whether another tenant/session exists |
| `configuration_import_contract_invalid` / `configuration_import_apply_blocked` | Validate against the v1alpha2 schema and select only session-advertised sections; resolve every blocking/external-setup/legal-review item |
| `configuration_import_apply_failed` | No selected section committed. Repair the named dependency and start with a fresh preview |
| Effect `Pending` / `Processing` | Configuration committed. Restore outbox processing; do not replay the import |
| Effect `DeadLettered` | Repair the bounded downstream effect and use outbox recovery with the operation ID |
| `configuration_import_snapshot_unavailable` / `configuration_import_rollback_unavailable` | Use a retained artifact and current-state review, or restore database and protected artifact authority from one consistent point; never edit history |
| Direct transfer interrupted | Resume at the reported offset before expiry, or cancel and create a new mutually approved session |

A rollback is a new forward operation: create its session, preview the protected
snapshot against current authority, then apply. If target locks, policies, or
legal requirements changed, the rollback can correctly block. Direct-transfer
promotion also creates an ordinary import session and cannot bypass this step.

## Local URLs

- API: `https://localhost:7039`
- Swagger: `https://localhost:7039/swagger`
- Scalar: mapped by `MapScalarApiReference()` in Development/Testing API runs
- Blazor (dotnet): `https://localhost:7177`
- Blazor (docker compose): `http://localhost:7002`
