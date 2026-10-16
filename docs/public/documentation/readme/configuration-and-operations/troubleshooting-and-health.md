---
description: Practical troubleshooting guide, symptom matrix, and step-by-step recovery recipes.
---
<!-- ABOUTME: Operator troubleshooting and recovery guidance for deployment and runtime failures. -->
<!-- ABOUTME: Explains readiness responses and optional email without exposing private diagnostics. -->

# Troubleshooting & Operational Health

## Human mutation admission contracts

Pure route projection keeps platform settings governance separate from tenant
BYO authorization endpoints. Ordinary tenant-resource projection may select BYO
before instance mode, while Local projection requires co-located decision sources.
These projections describe evaluator selection, not an authorization grant or
proof that native commit-time admission has been installed.

Source observations use the normal hierarchical setting precedence rules.
An empty tenant override set still counts as tenant context: missing SMTP
delegation keeps tenant SMTP changes locked, rather than granting delegation
because no override row exists. Instance-only settings evaluation is separate.

Human commit identity and source-fence contracts are internal prerequisites for
commit-time revalidation. They do not enable an operator setting, install database
locks or activate new mutation protection by themselves. Native provider enrollment
and each mutation consumer must be delivered and verified before that protection
can be relied on. Existing request authentication remains the entry boundary.

## Persisted API-key evidence

Persisted API-key authentication can supply server-owned evidence of the key row
and its authenticated scope snapshot. Configured clients and internal machine
execution do not have this evidence, even if their claims look equivalent.
Copying or editing claims cannot transfer persisted-key authentication custody.
This evidence does not itself grant a mutation or replace commit-time checks.

## Credential lifetime evidence

The API can retain request-local evidence of a validated human credential's
original expiry. Editing claims or authentication ticket expiry does not renew
that credential, and copying a principal does not transfer its evidence. This
capture mechanism preserves existing provider clock skew; it does not itself
change token lifetime configuration or authorize an administrative action.

## Keycloak token audience and purpose

API requests require an access token whose aud contains KEYCLOAK__AUDIENCE
(default islamu-event-api) and whose signed payload typ is Bearer. ID tokens
and tokens addressed only to the browser client are rejected even when azp
names an approved client. Update the realm resource-audience mapper before
rollout; the retired ValidAudiences arrays no longer grant acceptance.

Optionally set Keycloak:PermittedClients through indexed configuration such as
KEYCLOAK__PERMITTEDCLIENTS__0 to restrict originating clients as well. This list
does not replace the API audience. Omit it for no client restriction; do not
use a blank placeholder entry. Direct-process indexed environment variables
must be explicitly forwarded by a container deployment to reach the API.

## Conditional JSON responses

Small successful JSON responses may return a weak ETag for conditional requests.
Responses exceeding 256 KiB, streaming or flushed responses, non-JSON content
and private-no-store endpoints do not receive middleware-generated validators.
A missing ETag on those responses is intentional: the API forwards the content
without retaining the entire response in memory. A matching If-None-Match returns
a bodyless 304, allowing the client to reuse its cached representation.

## File response sandboxing

Storage downloads, public storage images and event-resource content return a
Content-Security-Policy that includes sandbox alongside resource-loading and
framing restrictions. Reverse proxies must preserve this response policy rather
than replacing it with a weaker generic API policy. Ordinary JSON endpoints
retain the base API policy without the file sandbox directive.

## Support-access audit failures

Support-access audit storage failures do not replace an already completed HTTP
response. Watch the audit persistence failure metric and warning to detect gaps;
warnings intentionally omit provider exceptions and their private details.

## Server-owned correlation IDs

Use the response's `X-Correlation-ID` when reporting a request failure. The API
generates a fresh compact UUID for each request. Incoming `X-Correlation-ID`
and `X-Request-ID` values are ignored, not echoed; clients must capture the
returned ID instead of expecting their supplied value to appear in API logs.

## JWT failure diagnostics

Invalid bearer tokens still receive a 401 challenge. Provider error descriptions
and error URLs are omitted; failed authentication uses the generic `invalid_token`
challenge code. Operators can correlate the native `authentication_failed` and
`authentication_challenge` events without exposing token contents or private
request paths in logs.

## Storage readiness failure reasons

The API no longer imports ambient logging scopes. Keep `FromLogContext` out of
API Serilog configuration: scope fields can overwrite native diagnostic values.
Events retain their own arguments and producer category.

Storage readiness uses `provider_unavailable` when the selected provider cannot
be resolved and `policy_unavailable` when its policy cannot be resolved. These
stable reasons replace exception subtype names. Check the selected provider
registration and storage policy respectively; private exception details are not
part of the health response.

This guide provides fast diagnostic procedures and step-by-step recovery recipes for common issues encountered during deployment, authentication, database migrations, and runtime operations.

---

## Setup Readiness

The setup overview refreshes one server journey snapshot. Open the application,
configure authentication and authorization, enter the site name, and finish setup.
There is no URL field or address confirmation. Authorized setup establishes the
address internally from the BFF request, including HTTPS, port and path prefix.
Behind a proxy, preserve the public Host and configure trusted forwarded headers,
or set `PUBLIC_BASE_URL` and restart. Anonymous requests never establish a permanent
address. Subdomain routing requires an explicit base domain when enabled;
path-based multi-tenancy does not.

If email delivery is enabled but secure public links cannot be constructed, setup
shows a non-blocking warning to configure `PUBLIC_BASE_URL`. Background email uses
this override or the established address; recovery links still require HTTPS.
A deployment-managed provider is not ready merely because deployment
selected it: pending, failed and restart-required states require the displayed
repair action. Setup never silently selects a replacement provider.

If Finish setup reports both “The settings field is required” and an invalid-body
error, this can be a JSON contract mismatch rather than a missing form field.
Update and restart the API and BFF together, then reload setup. Response-only URL
ownership metadata is no longer included in the submitted site profile.

If the snapshot is unavailable, contradictory or changed while being read, repair
the selected source and refresh; do not proceed using an older screen's actions.
Custom setup clients must replace `/api/system/onboarding-preflight` with
`/api/instanceonboarding/journey`, using active setup or administrator authority.
Completion and sign-in recovery rules are unchanged. The journey generation
tracks saved setup state, not a guarantee of continued external availability.
Completion checks readiness again before taking the setup write lock; changed
saved settings still require a refresh.

## Getting-Started And Identity Controls

An unavailable checklist is not a ready result. Use its Refresh status action and
inspect the bounded reason before proceeding; do not infer permission from a
readiness label. After sign-in, status/journey reads should use the normal session,
not old setup authority. A 429 response requires inspecting request volume and the
configured limiter, not repeatedly pressing Refresh.

If kind/country choices are unavailable, existing identity values are retained.
Refresh after restoring the metadata service; do not invent a replacement choice
registry. An omitted Save action means the document did not grant editing.
A revision conflict requires reviewing the authoritative document before saving.

## Quick Diagnostic Flow

When diagnosing an unexpected failure, follow this four-step sequence:

```mermaid
graph LR
    A[1. Check Containers<br>docker compose ps] --> B[2. Check Logs<br>docker compose logs]
    B --> C[3. Probe Health Endpoints<br>curl /alive and /health]
    C --> D[4. Validate Config<br>docker compose config]
```

1. **Check Container Status**:
   ```bash
   docker compose ps
   ```
2. **Inspect Container Logs**:
   ```bash
   docker compose logs --tail=100 -f event-api
   docker compose logs --tail=100 -f event-ui
   docker compose logs --tail=100 -f keycloak
   ```
3. **Query Health Endpoints**:
   ```bash
   curl -i http://localhost:7039/alive
   curl -i http://localhost:7039/health
   ```
4. **Verify Configuration Syntax**:
   ```bash
   docker compose config --quiet
   ```

---

## Common Issues & Recovery Recipes

Analytics bridge warnings identify the failed operation and exception type, but
intentionally omit page paths, user identifiers, event payloads and raw browser
exception text. Use the operation to check analytics configuration and consent;
do not add sensitive payloads to shared support logs to diagnose a failed call.

### Recipe 1: Keycloak "Invalid Parameter: redirect_uri" or Infinite Login Loop

#### Why this happens
Keycloak validates the redirect URI sent by the browser against its client whitelist. If your reverse proxy terminates TLS but does not forward `X-Forwarded-Proto: https` and `X-Forwarded-Host`, Keycloak believes the request came over unencrypted HTTP or an internal container IP and rejects the callback.

#### Step-by-Step Fix:
1. **Fix Reverse Proxy Headers**: Ensure your reverse proxy (Caddy, Traefik, or Nginx) forwards the client headers to Keycloak:
   - *Nginx*:
     ```nginx
     proxy_set_header Host $host;
     proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
     proxy_set_header X-Forwarded-Proto https;
     ```
   - *Caddy*:
     ```caddy
     reverse_proxy keycloak:8080 {
         header_up X-Forwarded-Proto {scheme}
         header_up X-Forwarded-Host {host}
     }
     ```
2. **Configure Keycloak Client Allowed URIs**:
   - Log into Keycloak Admin Console at `https://auth.example.org/admin`.
   - Navigate to **Clients** $\to$ **`event-blazor`**.
   - Under **Valid Redirect URIs**, enter: `https://events.example.org/*`
   - Under **Web Origins**, enter: `https://events.example.org`
   - Click **Save** and test logging in again.

---

### Recipe 2: API Fails at Startup with `Instance:OperatorIdentity Validation Failure`

#### Why this happens
In production (`ASPNETCORE_ENVIRONMENT=Production`), ISLAMU Event implements a fail-closed legal compliance check. An instance will refuse to open HTTP ports if the operator legal identity variables are blank.

#### Step-by-Step Fix:
1. Open your `.env` file.
2. Ensure the following six legal identity keys are populated with real values:
   ```env
   INSTANCE__OPERATORIDENTITY__OPERATORID=01912a7e-1234-7000-8000-000000000001
   INSTANCE__OPERATORIDENTITY__PUBLICNAME=Community Events Foundation
   INSTANCE__OPERATORIDENTITY__LEGALNAME=Community Events Foundation Non-Profit
   INSTANCE__OPERATORIDENTITY__PUBLICCONTACTEMAIL=contact@example.org
   INSTANCE__OPERATORIDENTITY__TERMSURL=https://events.example.org/terms
   INSTANCE__OPERATORIDENTITY__PRIVACYURL=https://events.example.org/privacy
   ```
3. Restart the stack:
   ```bash
   docker compose restart event-api
   ```

---

### Recipe 3: Headless Onboarding Fails with `GenerationDrift`

#### Why this happens
When using headless automated onboarding (`INSTANCE_BOOTSTRAP_MODE=ConfiguredAdministrator`), the platform guards against configuration replay attacks using a monotonic generation counter. If you edit bootstrap parameters without incrementing the generation counter, startup halts immediately.

#### Step-by-Step Fix:
1. Inspect the current generation number in `.env`:
   ```env
   INSTANCE_BOOTSTRAP_BINDING_GENERATION=1
   ```
2. Increment the value to a strictly higher integer (e.g., from `1` to `2`).
3. Confirm that `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` exactly matches the user's UUID (`sub` claim) from Keycloak.
4. Restart `event-api`:
   ```bash
   docker compose restart event-api
   ```

---

### Recipe 4: Database Migration Lock or Connection Refused

#### Why this happens
If `event-api` starts before PostgreSQL is ready or before `event-migrationservice` completes, the API may timeout or log database lock errors.

#### Step-by-Step Fix:
1. Check PostgreSQL container health:
   ```bash
   docker compose ps postgres
   docker compose logs --tail=50 postgres
   ```
2. Manually run the one-shot migration service and confirm it exits with code `0`:
   ```bash
   docker compose run --rm event-migrationservice
   ```
3. Once migrations succeed, start the API:
   ```bash
   docker compose up -d event-api event-ui
   ```

---

### Recipe 5: All Authenticated Actions Return `403 Forbidden` (Cerbos Fail-Closed)

#### Why this happens
When `AUTHORIZATION_PROVIDER=cerbos` is selected, ISLAMU Event enforces **fail-closed security**. If the Cerbos PDP container is unreachable, unhealthy, or has not loaded policies, all actions are denied immediately. It does not fall back to local RBAC.

#### Step-by-Step Fix:
1. Check Cerbos PDP health endpoint:
   ```bash
   curl http://localhost:3592/_cerbos/health
   ```
2. If Cerbos is running but policies are missing, upload policies via `cerbosctl`:
   ```bash
   docker run --rm -v "$PWD/cerbos/policies:/policies:ro" \
     ghcr.io/cerbos/cerbosctl:0.51.0 \
     --server=localhost:3593 --plaintext \
     put policy -R /policies
   ```
3. If Cerbos is experiencing an extended outage and you need immediate emergency access, switch to local RBAC in `.env`:
   ```env
   AUTHORIZATION_PROVIDER=local
   ```
   Then restart `event-api`:
   ```bash
   docker compose restart event-api
   ```

---

Setup wizard reads are rate-limited per route. Refreshing status or readiness
cannot consume the authorization-provider configuration route's budget, while
setup mutations remain on a stricter shared budget. If a custom setup client
receives `429`, honor `Retry-After`; do not replace the missing authoritative
response with locally inferred provider state.

### Recipe 6: Lost Setup Secret Recovery

During first-run setup, the protected BFF session can read branding and save the
installation profile before a directory is public. Signing in with Local,
Keycloak, or ATProto does not itself grant administrator rights; normal branding
changes still require an administrator.

If profile save loses setup authority after sign-in, refresh setup status and
return to `/setup` to renew the session while setup is incomplete. Keep browser
cookies enabled and use the normal form so write requests retain antiforgery
protection. Do not add authorization, provider, tenant, or setup-secret headers,
or copy secrets/tokens into browser storage.

A completed installation permanently rejects setup authority. The BFF rechecks
persisted authority before renewing or synchronizing it and clears setup state
when the API reports `410` / `setup_already_completed`. Use normal sign-in instead;
retained cookies and restarts cannot reopen setup. Share only bounded reason codes
with support, never secrets or raw provider responses.

#### Why this happens
If you left `SETUP_SECRET=` blank in `.env`, the container generated an ephemeral single-use secret inside the volume upon first boot.

#### Step-by-Step Fix:
1. Copy the setup secret out of the container to your local terminal:
   ```bash
   docker compose exec event-api cat /app/data/setup-secret
   ```
2. Open `http://localhost:7002/setup` in your browser and paste the secret.
3. Once onboarding completes, the setup secret file is permanently deleted automatically.

---

### Recipe 7: Lost Instance Administrator Access

Use this break-glass procedure only when an existing platform account is
already linked to the exact AT Protocol DID that should regain instance
administration. It cannot create or link an account.

1. Back up the application database.
2. Stop API, UI, worker, and migration replicas while keeping the database
   reachable.
3. Check out the exact source revision deployed to the instance.
4. Export the same structured migrator secret authority used by the deployment.
   Do not place a connection string or password on the command line.
5. Run:
   ```bash
   dotnet run --file eng/tools/EmergencyAdminProvisioner.cs -- \
     --grant-did 'did:plc:replace-with-the-exact-linked-did' \
     --apply
   ```
6. Run the same command a second time. The expected result is
   `instance-administrator-recovery: already-present`.
7. Restart every replica, sign out, and sign in again so cached authority and
   session claims are refreshed.

Exit codes are `0` for granted/already present, `64` for invalid command input,
`65` for an unknown/ineligible DID, invalid role authority, or pending
migrations, `70` for configuration/database failure, and `130` for
operator cancellation.

The tool does not create users, resolve handles, modify onboarding state, grant
tenant authority, or contact a personal data server. Its output intentionally
contains no DID, user/tenant ID, database locator, secret, or exception text.
Production container images do not carry this source/SDK tool; run it from the
matching checkout on a trusted host with database access.

If the previous platform administrator is compromised, add `--reassign` to the
command. This verifies and grants the replacement first, then removes every
other platform-administrator grant in the same serializable transaction.
Without `--reassign`, the operation is additive.

---

## Health Check Endpoints Reference

The platform exposes standardized, sanitized health endpoints:

| Endpoint | Method | Purpose | Healthy Response |
|---|---|---|---|
| `/alive` | `GET` | **Liveness Probe**: Confirms the process is running. | `200 OK` with sanitized JSON status |
| `/health` | `GET` | **Readiness Probe**: Checks required infrastructure and reports optional-service health. | `200 OK` with sanitized JSON status |

Both `Healthy` and `Degraded` readiness return HTTP 200. `Unhealthy` returns HTTP 503. Inspect the individual checks to distinguish an optional email incident from a core dependency failure.

### Optional Email Readiness

| Situation | Health result | Operator action |
|---|---|---|
| Outbound email is disabled, including an unconfigured installation with delivery left off | `smtp` is `Healthy`; no SMTP connection is attempted | No mail server is needed for this readiness check. |
| Email is explicitly enabled, but configuration is incomplete, SMTP connection/authentication fails, or the network probe times out | `smtp` is `Degraded`; `/health` stays HTTP 200 if core checks are healthy | Check the instance's delivery setting, SMTP address, port, TLS mode, and credentials through the configured [secret authority](secrets.md). |
| Required configuration/secret authority, database access, or health-check composition fails | `Unhealthy`; `/health` returns HTTP 503 | Restore the failing core dependency. Optional email does not bypass these checks. |

The SMTP readiness probe and instance connection test use **instance SMTP settings**. They do not validate a tenant's separate mail server.

The `email-dispatch` check is `Healthy` when its worker is intentionally disabled. A selected but disabled scheduler is `Degraded`, while failures reading the dispatch database remain `Unhealthy`. HTTP 200 alone does not confirm that email can be delivered.

Scheduler failure diagnostics use fixed categories rather than job keys or
provider exception details. `scheduler_unavailable` leaves deadline registration
unscheduled and cancellation unsuccessful, preserving the existing sweep fallback.
Default scheduler-control logs omit operator identity and caller correlation;
deploy a separate access-controlled scheduler audit sink if identity-bearing
accountability records are required.

Startup registration logs expose completion, `registration_pending`, or
`registration_retry_failed` without provider diagnostics or registration attempt
identifiers. Svix event-type synchronization reports aggregate counts or
`event_type_sync_failed`. Shutdown cancellation is not reported as a provider
failure; these diagnostic changes do not change worker retries or sync results.

When delivery is disabled or its configuration is unavailable, optional notifications
are recorded as skipped rather than kept for a later flood of email. In-app delivery
continues. Required email can be parked while its delivery requirements are unavailable;
an operator hold is a separate reason and must not be released by restoring SMTP.
If a capability-held message offers a park action, using it places that message
under an explicit operator hold. Repeating the hold does not replace its original
reason or time. Follow the actions offered by the server; a message already being
sent cannot be taken back by parking it.
Do not assume that correcting SMTP has sent a parked message. Inspect its delivery
state, and never automatically replay a message whose acceptance is unknown. A send
already admitted before a policy change may still complete.

Old managed-administrator invitation messages are retired and always skipped,
including after email or administrator access is restored. Managed provisioning
now links an existing Local administrator instead of producing an invitation.
A skipped audit entry is not a sent email.

### Deliberately Disabling Email

Use the disable action advertised in the instance SMTP or current tenant email
settings. Tenant controls respect instance delegation and individual settings
locks. If no action is offered, changing a generic setting or resetting an override
does not bypass that restriction.

1. Request the disable preview and review its affected scopes and policy revision.
   Disabling instance delivery does not necessarily disable a tenant that uses its
   own independently enabled transport.
2. Type `DISABLE EMAIL DELIVERY` exactly and confirm before the preview expires.
   Cancelling leaves delivery unchanged. The confirmation expires after five
   minutes and is valid only for the administrator, scope and impact that produced it.
3. If policy, scope or administrator authority changes, obtain a new preview.
   A stale confirmation returns a conflict instead of silently applying different
   impact; do not automatically retry it.

Disable preserves SMTP configuration and locks, and takes effect at new send
admission. A send already admitted may still finish. Re-enabling delivery does not
replay skipped optional history or release operator holds and unknown outcomes.
Keep confirmation tokens out of logs and support tickets; the preview contains no
SMTP credentials or message content.

Example partial response when outbound email is disabled and core checks are healthy (other checks omitted):

```json
{
  "status": "Healthy",
  "message": "Ok",
  "totalDuration": 18,
  "checks": [
    {
      "name": "smtp",
      "status": "Healthy",
      "description": "smtp_disabled",
      "duration": 1,
      "data": { "enabled": false, "state": "Disabled" }
    }
  ]
}
```

*Note: Health responses never disclose passwords, connection strings, or PII.*

### AT Protocol readiness recovery

Inspect the `atproto-authentication` entry on the browser-facing host's
`/health` response. Disabled AT Protocol is healthy. A failed AT Protocol
primary is unhealthy (`503`); a failed optional AT Protocol login with an
explicit Local Identity or Keycloak primary is degraded (`200`). That optional
failure does not itself take the host out of service, although other checks
can still fail. `/alive` does not depend on the AT Protocol store probe.

1. For `state_store_unavailable`, restore the private API connection, primary
   database availability/current migrations, and shared OAuth signing keys.
   Do not expose or copy key values, assertions, callbacks, or database rows
   into a support report.
2. Wait more than ten seconds for the cached readiness result to expire and
   request `/health` again. The probe has a two-second deadline, uses random
   non-secret data, and does not automatically retry or hedge requests.
3. Start a new login from the handle form. Do not retry an old callback or
   consume request after a lost response. A healthy store probe does not
   guarantee a user's external PDS or discovery service is available.
4. If expired rows accumulate, confirm the global scheduler is enabled and
   `atproto-transient-cleanup` is running every minute. It remains needed when
   login is disabled. Monitor its fixed success/failure and completed-row
   counters; never extend expiry to recover an old login.

See [AT Protocol readiness, cleanup, and retention](../federation-and-open-protocols/at-protocol-and-bluesky-jetstream.md#readiness-cleanup-and-retention)
for cleanup limits, shared protection-key persistence, and backup-retention
boundaries.

---

## Idempotency Receipt Payload Scrubbing

Expired replay receipts cannot be reacquired. Cleanup removes retained response
payloads while keeping the operation identity and request fingerprint as replay
rejection fences. An unavailable response returns conflict; it does not authorize
the original write to execute again. Do not delete receipts to recover a replay.

Subject erasure removes attribution and request fingerprint material without
reopening the original receipt identity.

On PostgreSQL, install the current native receipt capability before enabling
traffic or cleanup. Discovery exposes only tenant identities; each tenant's
scrubbing uses separate ordinary database authority. Dry-run and scrub mode
share one bounded pass budget, including suspended owners and receipts whose
tenant-directory row no longer exists. Invalid cleanup settings fail startup.
Intentionally disabled cleanup is `Degraded`; payloads remain stored, but expired
responses remain unavailable. Monitor cleanup run outcomes and eligible/scrubbed
row counts without exporting keys, response bodies or provider exceptions.

## Email Content Retention

Retention discovers bounded tenant identities through Global authority, then
redacts each tenant through a separate Runtime scope. Migrate the PostgreSQL
discovery capability before enabling cleanup. Missing discovery authority fails
the pass; restore the declared grants instead of giving Global email-table reads.

Dry-run uses the same selected-tenant scope. Each tenant's outbox, attempts,
receipts and notification deliveries redact in one transaction. A failed tenant
remains counted while other tenants continue; cancellation still propagates.
Sent/skipped cutoffs, batch limits and unresolved-message preservation remain
unchanged. Failure warnings expose only `retention_cleanup_failed`, not exception
types, provider text or recipient details.

## Inventory Hold Expiry

Run application migrations before enabling hold-expiry jobs so the PostgreSQL
identity-only discovery function and Global executor enrollment exist. Global
discovery must not receive private business-table grants.

Each sweep discovers at most 100 due holds and 100 interrupted recoveries.
Each order runs in a fresh Runtime scope bound to its original tenant before
inventory and lifecycle services are resolved. A scheduled pointer cannot
retarget an existing request or reuse another tenant's database context.

If discovery fails closed, repair the declared grants and source policies rather
than granting Global broad reads. If work remains after an interruption, rerun the
reconciliation sweep: expiry is conditional, payment ambiguity is preserved and
lifecycle recovery can complete previously expired holds without expiring live
or foreign-tenant work.

## Related Guides & Next Steps

If `tenant_lease_identity_changed` rejects an operation, create a fresh database
context for the newly selected tenant. Do not rebind an open context or retry the
same context with a different tenant; its connection lease retains the original identity.

Secrets audit logs retain bounded operation, provider and failure categories with
validated correlation identifiers, not key paths, user identities or provider
error payloads, even at Debug level. Failed secret-provider health probes report
`provider_unavailable` without private exception types or content. Use these
categories and current provider readiness to diagnose failures.

* **[Environment Variables Reference](environment-variables.md)** — Fix missing legal identity keys or secret provider errors.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Production container restart and migration lifecycle.
* **[Keycloak Authentication](../security-and-identity/authentication.md)** — Configure reverse-proxy redirect URIs and client origins.
* **[Authorization Guide](../security-and-identity/authorization.md)** — Understand Cerbos fail-closed behavior vs. Local RBAC.
* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Complete initial setup wizard with the recovered setup secret.
