---
description: Operate Local Identity, Keycloak, or passwordless AT Protocol authentication.
---

# Authentication Architecture

Each deployment has exactly one **primary authentication provider**:

* **Local Identity** uses ASP.NET Core Identity inside ISLAMU Event. It is the
  recommended default for standalone and localhost deployments.
* **AT Protocol** is the second choice for an average self-hoster with public
  HTTPS. Users authorize through AT Protocol/Bluesky, so the host does not
  manage their passwords. It is not first because OAuth cannot complete on
  localhost.
* **Keycloak** is the advanced choice for serious hosting teams and SaaS
  operators that need centralized SSO/federation, 2FA/MFA, and identity
  lifecycle administration.

AT Protocol may instead remain a separate optional linked-login capability
while Local Identity or Keycloak is primary. See
[Authentication Providers](../configuration-and-operations/authentication-providers.md)
for the five supported states.

---

## Account Matching and Recovery

Accepting a sign-in provider does not automatically trust it to attach a new
identity to an existing Event account by email. Automatic matching is disabled
by default. A deployment operator may explicitly allow exact OIDC issuer
addresses with `IdentityCorrelation:TrustedIssuers`; see the
[environment reference](../configuration-and-operations/environment-variables.md#3-authentication-providers-and-local-identity).
Only that issuer's verified email can match one eligible account. Local-owned
accounts are never automatically adopted this way.

An already linked issuer and subject always resolve the same Event account,
even if the email changes or the operator removes the issuer from matching
trust. Removing trust stops new email-based matches; it does not unlink users.
Ordinary permitted first signup remains available without matching trust,
including unverified or absent email, but a verified address already owned by
another account cannot be used to bypass ownership through separate signup.
AT Protocol continues to support
email-free signup through a verified DID. Mail settings do not manufacture or
erase a provider's verification statement.

An ambiguous or conflicting identity is not silently merged or used to select
another account. Recover access through the original sign-in provider or
contact the instance operator. Event does not currently offer a general
self-service account-merge or explicit-linking screen.

Account matching uses verified identity-address claims, not the editable contact
email displayed in a profile. Supported identity addresses are unique across
the instance; contact addresses may be shared. Verified-address matching ignores
letter case and surrounding whitespace without trusting additional issuers.
A provider changing its verified
address to one already claimed by another account does not move its linked Event
account. Event discards that provider's obsolete address proof while preserving
proof supplied independently by another linked identity. Mail recipient
selection uses supported identity-address claims.

After ordinary account erasure, authenticating again with the same external
provider identity or released email may automatically create a fresh Event
account. It does not recover the old account ID, profile, permissions, private
history or consent. Existing matching and address-conflict rules still apply.
Old-account replay protection remains intact; fingerprint keys are not required
for ordinary signup, erasure or startup. See [Privacy Erasure](privacy-erasure.md).

## Profile Names

Your sign-in provider can supply initial first, last and display names when an
Event account is created. After creation, signing in again preserves the names
you edited in Event instead of replacing them with the provider's current
profile. This also applies when an eligible existing account gains another
external sign-in identity.

Changing names does not verify an address or change the identity claims used
for mail delivery. Previous registration contact details and recorded consent
retain their captured values.

## Browser Authentication Flow

The browser communicates strictly with `Explore.Blazor` over HTTPS regardless of the selected provider:

* The client never stores raw JWT access tokens in browser `localStorage` or `sessionStorage` (mitigating XSS token theft).
* Authentication state is tracked via an encrypted `SameSite=Lax` session cookie managed by the BFF.
* Local login posts through an antiforgery-protected BFF endpoint. The BFF stores the returned bearer token only in server-side authentication properties. Public Local registration is not available.
* The API validates Local and Keycloak tokens with isolated bearer schemes. A token signed or issued for one authority cannot authenticate through the other.
* Direct Google sign-in and Google sign-in brokered by Keycloak use separate provider account namespaces. A brokered login remains bound to the Keycloak issuer and subject; a provider hint does not turn it into a direct Google account. Keep the configured issuer stable when diagnosing account-linking failures.

During configured-administrator setup, the default tenant remains unpublished
until activation. Only the authenticated account matching the configured
provider identity can synchronize at `POST /api/user/sync` while the tenant is
Provisioning. In multi-tenant deployments, that request needs the default
tenant's exact slug in `X-Tenant-Slug`; other slugs and unrelated accounts
remain denied. The administrator can read their private session and authority
after claiming, but this does not make public tenant pages available.

After authentication or signout, the application returns only to a local path
beginning with `/`. Invalid return destinations fall back to the home page;
external destinations and literal control characters are not accepted. Normal
local query parameters remain supported. This restriction concerns the return
into the application, not the authorized redirect to an identity provider.

Challenge and signout diagnostics omit supplied provider selectors and request
or return URLs. For support, share the reported error code and correlation ID
rather than complete authentication links or query strings.

### Local Identity

Local Identity provides username/password or email/password sign-in without an external identity container. Passwords are hashed by ASP.NET Core Identity and failed attempts use bounded lockout. Public self-registration is closed: the former API and BFF registration routes have been removed, with no replacement public signup endpoint.

Direct login requests use `identifier` and `password`; the former `email`
request member is not an alias. A username does not contain `@`; an email-shaped
identifier uses the account's actual stored address. Accounts without email
keep it absent rather than receiving an invented address.

Login failures use bounded machine-readable error codes. Do not include login
request bodies or successful token responses in support logs. Their ordinary
diagnostic text omits credentials, but explicit JSON body capture does not.

A correct password is not sufficient when credential setup is incomplete. Local sign-in also requires an explicit ready state in the selected Identity database. Missing, invalid, or unfinished credential state blocks sign-in even for a verified email address; disabling email delivery does not bypass this check. Records without state are not automatically treated as ready. This sign-in check alone does not revoke existing sessions.

When instance email delivery is enabled, Local sign-in requires the stored Local verification flag. Supervised bootstrap can establish that flag as administrative provenance; this does not invent an address or prove mailbox delivery. Missing SMTP configuration or a delivery outage does not bypass the gate. Tenant email settings cannot override the instance sign-in policy. When instance delivery is disabled, unverified Local accounts may sign in, but their addresses remain unverified; disabling delivery never proves mailbox ownership. An invalid or unreadable instance policy blocks unverified sign-in until the configuration is repaired.

Event's delivery setting does not control Keycloak or AT Protocol verification, password recovery, or sign-in. Lifecycle actions follow the account's actual linked provider, not the instance's default provider. Local enrollment remains an administrator or setup operation; recovery never creates an account.

The first-run Local wizard applies the standard English locale and UTC timezone
defaults when no profile override is supplied, just as external-provider setup does.

#### Local verification and recovery

Use the verification, recovery or password-change action offered by the server in
sign-in or account security settings. Local verification and recovery require
usable instance email delivery; a tenant mail server does not replace it.
Disabling delivery or an SMTP outage never verifies an address or bypasses the
existing sign-in gate. Keycloak and AT Protocol recovery remain with their provider.

Verification and email-change links expire after 30 minutes, and password-recovery
links after 15 minutes. Repeating a request while its operation remains live reuses
that operation without extending its deadline or invalidating a link in transit.
Request responses are deliberately generic: acknowledgement does not confirm that
an account exists or that a message was sent. Rate and operation limits may prevent
additional mail.

Open the private link and explicitly submit the requested action. Verification,
email change and password recovery cannot be exchanged for one another. The
browser removes the link's private fragment before forwarding the form, and does
not save the token in browser storage. Do not paste a complete private link into
logs or support tickets. Successful completion does not sign you in; use a fresh
ordinary login.

Changing an address requires a current Local session and verification of the
proposed address. Ordinary password change requires the current password and
works without SMTP. It is separate from first-use temporary-password replacement.
Password changes reject an unchanged password and revoke stale Local sessions.

In Standalone, these signed-in account actions use your existing protected
session. If current account permissions cannot be checked, the form stays
unavailable; return to sign in and retry rather than submitting credentials to
an API URL manually.

Email delivery is not guaranteed by an accepted request. Pending work stays
bounded by its original expiry, and uncertain SMTP acceptance is not automatically
resent. Restoring email does not revive an expired link. For Local accounts with
no eligible address, use the existing administrator credential-reset process
rather than guessing another account or substituting an external provider.

Local sign-in requires its application account and active personal profile to
already be linked; signing in does not create or repair that account. An external
identity cannot claim a Local account merely by using the same email address.
An already-established explicit provider link remains separate from email matching.

Configured administrator sign-in does not select an existing account by a supplied
user ID. Without an exact provider link it creates a new application account;
reuse of a Local-owned account requires that link to remain present when the
claim transaction checks it. A link removed before that transaction starts is
not accepted from an earlier lookup.

Instance administrators can use the administrative API to create Local accounts,
issue supervised reset credentials and inspect or reconcile interrupted operations.
The Local accounts section provides the same server-authorized operations.
Tenant administrators cannot change these shared credentials. The storage flow keeps
new credentials pending until the matching application account is committed,
then marks them as requiring a private password change; neither state permits
ordinary sign-in. Retrying an interrupted linking operation preserves its account
identifiers and never returns another temporary password. A valid temporary
password can now produce a restricted, at-most-five-minute replacement challenge,
not an ordinary signed-in session. Native replacement preserves the 12–128-character
login limits, rejects reuse of the temporary password, and requires a fresh login
after the change. Creation returns a generated temporary password only once;
neither status reads nor retried operations return it again. Record the operation
ID, hand over the password privately, and do not persist credential-bearing
responses in logs or scripts. If the response is lost, inspect the operation,
explicitly reconcile pending creation if needed, then issue a separately authorized
reset with a new operation ID. Status reads never complete setup themselves.
Every administrative retry checks current instance administrator access, including
reconciliation; a previous successful response does not retain revoked access.
Protected API
requests now recheck Local credentials: an old token is rejected after a committed
reset, a broken account binding or a change to its verification fact. Tokens issued
before the required session-stamp contract are rejected; sign in again. Unverified
tokens also stop working when instance email delivery is enabled. If current
authority cannot be read, the API denies the request with HTTP 401. The browser
session is also checked on cookie-authenticated requests and subsequent interactive
activity. Invalid current authority rejects the cookie or makes the interactive
session anonymous without executing that activity. A separate fresh login for the
same account is preserved. An idle tab is not immediately notified by a push;
validation occurs when it next sends activity. Sign in again after replacing the
temporary password. An unavailable authority check fails closed.
Do not change database state manually to
bypass setup. Reset is not email verification and does not remove an existing
lockout. A repeated reset request must never reveal the temporary password again;
an operator who loses that handover will need a separately authorized new reset.

For browser sign-in, a valid temporary password opens the private password-change
page. Choose and confirm a new password, then sign in again when returned to the
login page. Entering this restricted step clears the browser's previous local
session. The short-lived challenge stays in a protected HttpOnly cookie, not in
page data or browser-readable storage. If the step expires, return to sign in;
the page never renews its deadline. Validation errors clear both password fields
and let you retry while the challenge is still current.

Direct API clients can submit the short-lived challenge as a Bearer token to
`POST /api/auth/local/credential-replacement`, with a JSON body containing only
`newPassword`. An ordinary access token cannot replace the challenge. Successful
replacement returns an empty HTTP 204 response, not a signed-in session; sign in
again using the new password. Password validation returns HTTP 400 and allows a
corrected request while the challenge remains valid. Invalid, expired or consumed
authority returns HTTP 401; a concurrent operation conflict returns HTTP 409.
If the challenge expires during the database updates, replacement rolls back
without changing the password. Sign in with the temporary password to obtain
a fresh challenge rather than retrying the expired one.
Do not log or persist challenges or password request bodies. Reusing an
`Idempotency-Key` cannot replay successful replacement.

Administrative identity resolution checks the current exact external-login link,
so removing that link cannot leave a cached administrator identity mapping.
This does not log the person out of their external identity provider.

Configure:

* `AUTHENTICATION_PROVIDER=local`
* `AUTHENTICATION_LOCAL_JWT_KEY` with a Base64-encoded key of at least 256 bits
* `IDENTITY_DATABASE_TOPOLOGY=colocated` for normal standalone operation

Generate a signing key with `openssl rand -base64 64` and store it through the selected [secret authority](../configuration-and-operations/secrets.md). Never commit it.

For database isolation, set `IDENTITY_DATABASE_TOPOLOGY=external` and provide the `IDENTITY_DATABASE_*` provider, database, runtime, and migrator settings. PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL are supported. The migration service applies a context-owned credential schema with a separate migrations history.

#### First-run Local administrator

During incomplete setup, enter the deployment's setup secret and select Local
authentication. Use the Local enrollment action offered by the wizard, provide
a username and temporary password, and complete the required site and
directory-operator information. Account email is optional; legally required
public contact information is a separate input.

Completion creates the account and administrator linkage but does not sign the
browser in. Sign in with the temporary password, replace it privately, then
sign in again. This setup-authorized action does not reopen public registration.
After an interrupted attempt, refresh setup status so the wizard can recover
the original operation reference rather than start a competing operation.

For headless setup, select `ConfiguredAdministrator`, set the bootstrap provider
to `local`, use a UUIDv7 subject as the username, and supply
`INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` through the selected secret authority.
The active authentication provider must also be Local. The password has no
source default and does not become a reset mechanism after setup completes.
See [environment variables](../configuration-and-operations/environment-variables.md#8-first-run-setup--administrator-bootstrap)
for the selectors and optional profile fields.

### Keycloak

Set `AUTHENTICATION_PROVIDER=keycloak` and provide the documented `KEYCLOAK_*` authority and confidential BFF client settings.

Lifecycle-email failure logs report the action and HTTP status without account
identifiers, credentials or provider response bodies. Use authorized operation
results and delegation records when investigating a particular account.

Direct Keycloak operator API clients submit planning `intent` as `RepairClient`,
`CreateClients`, or `CreateRealm`, together with fresh `administratorUsername` and
`administratorPassword` fields. Regenerate typed clients after upgrading: the
credential request schemas now use the `Dto` suffix. JSON field names and routes
are unchanged. Provider credentials do not replace current setup or instance-admin
authority, and receipt operations remain bound to their actor and setup generation.
Never capture credential request bodies in logs; redacted diagnostic text does not
redact explicit JSON serialization.

#### Provider credential retries

The existing credential-bearing Keycloak and Cerbos management POST operations
return private, non-cacheable responses and do not use generic idempotency replay.
If a request outcome is uncertain, retry through the existing authorized operation:
each retry checks current setup or instance-administrator authority and the current
provider outcome. An earlier successful response is not reused. The internal
setup authentication-configuration read that can contain provider secrets is also
private and non-cacheable. Existing provider reconciliation remains the recovery
path for interrupted provider work; no route, request format, or client contract
changes with this HTTP boundary.

* Production operators must ensure:
  * Proper TLS termination and reverse-proxy header forwarding (`X-Forwarded-Proto: https`).
  * Explicit registration of valid redirect URIs in the Keycloak Admin Console (see [Troubleshooting Redirect Errors](../configuration-and-operations/troubleshooting-and-health.md#recipe-1-keycloak-invalid-parameter-redirect_uri-or-infinite-login-loop)).
  * Secure client secret storage via [Secrets Management](../configuration-and-operations/secrets.md).

---

## Switching the Primary Provider

Instance administrators can switch among Local Identity, Keycloak, and AT Protocol without invalidating already-issued sessions:

1. Create and verify an administrator account with the target provider.
2. Configure the target provider and confirm that it can authenticate.
3. Select it as the primary provider and save.

The server rejects a switch that would leave the administrator without usable target credentials. New login discovery and challenges use only the selected primary provider. Existing sessions continue through their original validation/refresh scheme until normal expiry. This is session continuity, not dual-primary authentication.

AT Protocol remains independently enabled or disabled when Local Identity or
Keycloak is primary. It is forced on, and Google SSO is forced off, when AT
Protocol is primary.

---

## Direct API Authentication

External programmatic clients and integration workers authenticate using either:

* **Bearer Token**: `Authorization: Bearer <jwt_access_token>` issued by the active Local or Keycloak authority.
* **API Key**: `X-API-Key: <key>` (hashed with SHA-256 in the database).

> [!NOTE]
> Do not supply both headers simultaneously. Authentication establishes *who* the caller is; [Authorization](authorization.md) and [Multi-Tenancy](multi-tenancy.md) boundaries still evaluate independently on every request.

### API Key Issuance And Lost-Response Recovery

Creating a key through `POST /api/ExternalApiKey` now requires exactly one
`Idempotency-Key` header. Its value must contain 1..128 ASCII characters from
`A-Z`, `a-z`, `0-9`, `.`, `_`, `:`, and `-`; case matters. For example, use
`Idempotency-Key: key-create-20261005-01` for one creation intent. Missing,
duplicate, comma-combined, whitespace, and non-ASCII values fail validation.
The header is required in OpenAPI; C# SDK callers pass it as the first argument
to `CreateExternalApiKeyAsync(operationKey, dto)`.

Keep that operation key and the same request policy until the outcome is known.
The operation key is not an API credential and does not grant authority.
Identity and current tenant come from the server's authenticated context, not
the creation body. User-owned tenant keys require active tenant membership;
organization/group keys require current persisted management permission;
linked tenants must be active. These requirements apply on recovery too.

Instance administrators can issue, list, and revoke global keys from the
administrator host without selecting a tenant. Tenant, personal, organization,
and group issuance still requires the correct resolved tenant; the server never
invents one from the creation body.

The first acknowledged creation returns HTTP `200` with
`disclosureStatus = "Issued"` and the raw `apiKey`. Save it directly in your
approved secret store before leaving the creation dialog. The service persists
only the secret hash and a digest-only receipt, not a recoverable response.
There is no endpoint that can show that secret again.

If a connection fails or a response is lost:

1. Retry with the same operation key and unchanged policy. Do not generate a
   new key merely because the first response was lost. The creation dialog
   preserves its operation key and freezes its canonical request for this
   retry while it remains open; failure guidance does not display server
   response bodies or exception text.
2. An authorized retry of a committed operation returns HTTP `200` with
   `disclosureStatus = "PreviouslyIssued"`, stable `id`/`keyId`, and
   `apiKey = null`. This confirms creation, not secret recovery. No second
   credential is minted. A retry that had not committed can instead complete
   issuance and return `"Issued"`.
3. If you never received or saved the secret, select Done to refresh the
   parent list, find the key by its metadata, and revoke it using the existing
   HAL-provided revoke action. Then deliberately start a replacement intent
   with a new operation key. Do not automatically create a replacement as part
   of a retry. If revoke is unavailable, restore legitimate management
   authority rather than bypass the action gate.

The server compares normalized policy, including name/description, owner-type
and organization/group targets, scopes, expiry, and all credit/rollover fields.
Changing policy for the same operation returns `409 Conflict`; use a new intent
only for a deliberate new issuance. Loss of owner authority returns `403`
without recovery metadata; a missing authenticated platform-user binding
returns `401`. A removed, revoked, expired, or otherwise unusable issued key
returns `404`. Deleting a credential does not free its old operation key:
the retained receipt still prevents duplicate issuance.

#### Deployment, Retention, And Rollback

The new executable requires the generated issuance-receipt table migration.
Receipt rows have no expiry and do not cascade away with key/user deletion;
they contain operation/input digests and identifiers, not raw credentials.
Retain them as duplicate-issuance evidence. They are not a credential backup,
and reverting the executable cannot restore discarded raw material.

Before reverting to code that captures generic replay responses, assess that
secret-storage boundary explicitly. Historical generic key-route replay
records must be inspected for secret-bearing responses. Purging those records
and rotating actually exposed keys require explicit operator authorization;
this remediation does not authorize or perform either operation. Preserve
receipt evidence during rollback planning rather than treating its removal as
a harmless cleanup.

This is the bounded key issuance change dated 2026-10-05, not a declaration of
release security readiness. Final provider/migration proof remains pending,
and the other 25 remediation phases remain launch blockers.

---

## AT Protocol Sign-In

Optional or primary [AT Protocol Authentication](../federation-and-open-protocols/at-protocol-and-bluesky-jetstream.md) enables users to sign in using their Bluesky handle (`@handle.bsky.social`) or Decentralized Identifier (DID):
* Links strictly by the DID verified with the personal data server.
* JIT-creates a passwordless account when AT Protocol is primary.
* Never creates accounts through opportunistic email matching.
* Does not implicitly grant event publication or federation consent.
* Requires an existing exact DID-linked account when optional under Local Identity or Keycloak.
* Operates under the same provider-neutral [Authorization](authorization.md) rules as Local Identity and Keycloak users.

---

## Acceptance Testing Checklist

1. Verify Local sign-in issues only an HttpOnly BFF cookie to the browser and the former public registration routes cannot create accounts.
2. Confirm invalid Local credentials return generic guidance and repeated failures lock the account.
3. For Keycloak, verify login redirects back to the Blazor application and refresh works.
4. Switch providers and confirm new-login discovery changes while an existing session remains usable.
5. Verify logout invalidates the local BFF cookie and invokes provider logout when applicable.
6. Verify an invalid API key returns `401 Unauthorized` with ProblemDetails.
7. Enable instance email delivery and confirm unverified Local sign-in is refused, even without SMTP. Disable delivery and confirm successful sign-in does not mark the address verified.

---

## Related Guides & Next Steps

* **[Authorization & Access Control](authorization.md)** — Learn how application operations evaluate Local RBAC or Cerbos policies.
* **[Docker Standalone](../self-hosting/docker-standalone.md)** — Run Local Identity without a separate identity container.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Deploy Keycloak and configure the `event-blazor` client.
* **[Troubleshooting Keycloak Errors](../configuration-and-operations/troubleshooting-and-health.md#recipe-1-keycloak-invalid-parameter-redirect_uri-or-infinite-login-loop)** — Resolve redirect URI mismatches and login loops.
* **[Admin Hierarchy & Roles](../administration-and-branding/admin-hierarchy.md)** — Map Keycloak users to Instance, Tenant, and Event roles.
