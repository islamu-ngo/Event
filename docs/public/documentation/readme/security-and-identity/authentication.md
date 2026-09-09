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

## Browser Authentication Flow

The browser communicates strictly with `Explore.Blazor` over HTTPS regardless of the selected provider:

* The client never stores raw JWT access tokens in browser `localStorage` or `sessionStorage` (mitigating XSS token theft).
* Authentication state is tracked via an encrypted `SameSite=Lax` session cookie managed by the BFF.
* Local login posts through an antiforgery-protected BFF endpoint. The BFF stores the returned bearer token only in server-side authentication properties. Public Local registration is not available.
* The API validates Local and Keycloak tokens with isolated bearer schemes. A token signed or issued for one authority cannot authenticate through the other.
* Direct Google sign-in and Google sign-in brokered by Keycloak use separate provider account namespaces. A brokered login remains bound to the Keycloak issuer and subject; a provider hint does not turn it into a direct Google account. Keep the configured issuer stable when diagnosing account-linking failures.

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
to `local`, use a canonical UUIDv7 subject as the username, and supply
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

* **[Authorization & Access Control](authorization.md)** — Learn how MediatR handlers evaluate Local RBAC or Cerbos policies.
* **[Docker Standalone](../self-hosting/docker-standalone.md)** — Run Local Identity without a separate identity container.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Deploy Keycloak and configure the `event-blazor` client.
* **[Troubleshooting Keycloak Errors](../configuration-and-operations/troubleshooting-and-health.md#recipe-1-keycloak-invalid-parameter-redirect_uri-or-infinite-login-loop)** — Resolve redirect URI mismatches and login loops.
* **[Admin Hierarchy & Roles](../administration-and-branding/admin-hierarchy.md)** — Map Keycloak users to Instance, Tenant, and Event roles.
