---
description: Operate Local Identity, Keycloak, or passwordless AT Protocol authentication.
---
<!-- ABOUTME: Operator guide for Local Identity, Keycloak, and AT Protocol primary providers. -->
<!-- ABOUTME: Explains BFF sessions, passwordless JIT identity, storage, and safe provider switching. -->

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
* Local login and registration post through antiforgery-protected BFF endpoints. The BFF stores the returned bearer token only in server-side authentication properties.
* The API validates Local and Keycloak tokens with isolated bearer schemes. A token signed or issued for one authority cannot authenticate through the other.
* Direct Google sign-in and Google sign-in brokered by Keycloak use separate provider account namespaces. A brokered login remains bound to the Keycloak issuer and subject; a provider hint does not turn it into a direct Google account. Keep the configured issuer stable when diagnosing account-linking failures.

### Local Identity

Local Identity provides email/password registration and sign-in without an external identity container. Passwords are hashed by ASP.NET Core Identity and failed attempts use bounded lockout. New email addresses remain unverified until an email-verification workflow is configured; the platform never treats registration alone as proof of email ownership.

Configure:

* `AUTHENTICATION_PROVIDER=local`
* `AUTHENTICATION_LOCAL_JWT_KEY` with a Base64-encoded key of at least 256 bits
* `IDENTITY_DATABASE_TOPOLOGY=colocated` for normal standalone operation

Generate a signing key with `openssl rand -base64 64` and store it through the selected [secret authority](../configuration-and-operations/secrets.md). Never commit it.

For database isolation, set `IDENTITY_DATABASE_TOPOLOGY=external` and provide the `IDENTITY_DATABASE_*` provider, database, runtime, and migrator settings. PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL are supported. The migration service applies a context-owned credential schema with a separate migrations history.

### Keycloak

Set `AUTHENTICATION_PROVIDER=keycloak` and provide the documented `KEYCLOAK_*` authority and confidential BFF client settings.

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

1. Verify Local registration and sign-in issue only an HttpOnly BFF cookie to the browser.
2. Confirm invalid Local credentials return generic guidance and repeated failures lock the account.
3. For Keycloak, verify login redirects back to the Blazor application and refresh works.
4. Switch providers and confirm new-login discovery changes while an existing session remains usable.
5. Verify logout invalidates the local BFF cookie and invokes provider logout when applicable.
6. Verify an invalid API key returns `401 Unauthorized` with ProblemDetails.

---

## Related Guides & Next Steps

* **[Authorization & Access Control](authorization.md)** — Learn how MediatR handlers evaluate Local RBAC or Cerbos policies.
* **[Docker Standalone](../self-hosting/docker-standalone.md)** — Run Local Identity without a separate identity container.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Deploy Keycloak and configure the `event-blazor` client.
* **[Troubleshooting Keycloak Errors](../configuration-and-operations/troubleshooting-and-health.md#recipe-1-keycloak-invalid-parameter-redirect_uri-or-infinite-login-loop)** — Resolve redirect URI mismatches and login loops.
* **[Admin Hierarchy & Roles](../administration-and-branding/admin-hierarchy.md)** — Map Keycloak users to Instance, Tenant, and Event roles.
