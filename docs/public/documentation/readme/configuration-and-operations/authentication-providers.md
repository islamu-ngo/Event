---
description: Configure Local Identity, Keycloak, or passwordless AT Protocol authentication.
---

# Authentication Providers

ISLAMU Event supports three primary authentication authorities:

- **Local Identity** - embedded ASP.NET Core Identity with platform-issued JWTs.
- **Keycloak** - an external OpenID Connect authority.
- **AT Protocol** - passwordless sign-in authorized against each user's personal data server.

Exactly one provider is primary for new sign-ins. AT Protocol can also remain an
optional login method while Local Identity or Keycloak is primary.

Setup keeps the selected provider's ready, action-required, unavailable, failed
and restart-required states visible. Opening advanced configuration does not
change authority or silently fall back to Local. After completion, use the
provider-management action offered by the getting-started checklist; ordinary
sign-in replaces setup authority for status and journey reads.

## Recommended Choice for Self-Hosters

1. **Local Identity is the recommended default**, especially for Docker
   Standalone. It is embedded, works on localhost and private networks, and
   needs no separate identity service. This is why the standalone image
   defaults to Local Identity.
2. **AT Protocol is the second-best choice for the average self-hoster** who
   has a public HTTPS domain. Users authorize with their AT Protocol account
   (commonly their Bluesky handle), so the host does not collect or manage
   their passwords. It is not the first default because decentralized OAuth
   callbacks require a publicly reachable HTTPS origin and therefore do not
   work on localhost-only installations.
3. **Keycloak is recommended for serious hosting teams and SaaS operators**.
   It carries the highest operational cost, but offers the most advanced
   centralized identity lifecycle, SSO/federation, multi-factor/2FA options,
   policies, and enterprise administration.

You can start with Local Identity and deliberately switch later after linking
the administrator to the target provider. The server prevents a switch that
would remove every usable administrator sign-in path.

## Supported Provider States

| Primary provider | AT Protocol login | Result |
|---|---:|---|
| `local` | `false` | Local Identity only |
| `local` | `true` | Local Identity plus AT Protocol |
| `keycloak` | `false` | Keycloak only |
| `keycloak` | `true` | Keycloak plus AT Protocol |
| `atproto` | `true` | AT Protocol only |

`AUTHENTICATION_PROVIDER=atproto` requires
`ATPROTO_LOGIN_ENABLED=true`. The application rejects the contradictory
`false` combination. Google SSO is disabled in AT Protocol-only mode.

## Keycloak Account Claims

Keycloak must issue the same canonical account `sub` in the ID token and API
access token, with its configured issuer. A successful browser callback alone is
not enough if the API access token has no subject. Session IDs and platform user
IDs are not substitutes.

The supplied realm exports include Keycloak's built-in **Subject** mapper
(`oidc-sub-mapper`) on the `islamu-event-blazor` client, with inclusion enabled for
access tokens, ID tokens and introspection. Automatic Keycloak setup also checks
and repairs this mapper. For an already imported realm, use the realm doctor's
Subject mapper check and additive realm sync (after confirming your backup), or
enable the native Subject mapper directly in Keycloak. Then sign out and sign in
again to obtain new tokens; restarting Event or replacing the export file alone
does not repair an existing realm or change issued tokens. Keep the existing API audience
mapper and email-verification mapping. Do not add a hard-coded subject or mark an
email verified to work around sign-in failures.

## Safe Keycloak Connection And Inspection

A correctly configured deployment connects with its existing runtime BFF
credential. The application resolves the Keycloak endpoint, realm, client ID and
client secret from the deployment's selected secret authority; it does not copy
the secret into the application database or ask an administrator to re-enter it.
If that authority is unavailable or unauthorized, repair the selected authority
and restart the affected replicas rather than adding a fallback value.

Basic discovery is read-only and needs no Keycloak administrator account.
Advanced inspection is a separate request and requires credentials entered
freshly in that form. Those credentials are used only for the foreground
request and are not read from deployment configuration, retained as a session,
or written to logs and support artifacts.

For an existing realm, Event does not change realm settings, users, roles,
shared client scopes, sessions, existing-client flow/type settings, or client
secrets. It recognizes effective native and inherited subject/audience mappings
without creating duplicates. Ordinary browser refresh does not require
`offline_access`; missing offline-token policy is not treated as a launch
failure. Unsupported prerequisites are shown as manual Keycloak steps.

### Approved operations and interrupted requests

Before Event sends an approved Keycloak change, it stores a credential-free
operation receipt. The receipt binds the exact instance, authority, realm,
client targets, reviewed change digest and current setup or administrator
authority. A changed target or approval cannot reuse it.

If the response is lost, Event reports **outcome unknown** and blocks another
operation for that realm. Do not click Apply again or repeat the change
manually. Run read-only reconciliation first. Cancellation can prevent work
that has not been sent; after transmission it records your request but cannot
undo Keycloak.

Back up the application database together with Keycloak before an approved
change. Settled receipts are retained for at least 30 days. Unresolved receipts
are retained until reconciliation and are never replayed automatically.

## Passwordless AT Protocol Onboarding

1. Start first-run setup and choose **AT Protocol** as the primary provider.
2. Configure the public instance URL used by AT Protocol OAuth metadata.
3. Save the provider configuration.
4. Enter the administrator's AT Protocol handle on the focused sign-in page.
5. Authorize the request at the account's personal data server.
6. Return to the setup wizard and complete instance onboarding.

No local password, Local Identity account, or Keycloak realm is created. The
OAuth return creates one passwordless platform account for the verified DID.
Administrator authority is granted only while the original setup-secret
session completes onboarding; OAuth success by itself cannot claim the
instance.

The instance still needs its server-only AT Protocol confidential-client ES256
key ring. Store that key ring through the selected secret authority. It signs
OAuth client assertions; it is not a user password and is never sent to the
browser.

## Runtime Behavior

The browser receives an encrypted HttpOnly BFF cookie. Provider access tokens,
OAuth session material, and platform bearer tokens remain server-side.

In AT Protocol-only mode:

- `/auth/providers` advertises only the ready AT Protocol handle flow;
- the login page opens the handle field immediately;
- Local Identity login and registration fail closed;
- unlinked verified DIDs are provisioned without a local password;
- repeated or concurrent first login converges on one account.

Existing sessions continue under the provider that issued them until normal
expiry. Changing the primary provider controls new sign-in admission; it does
not reinterpret an existing cookie as a different authority.

## Email Verification Is Explicit

A successful provider sign-in does not by itself verify an email address.
ISLAMU Event records provider verification only when the authenticated identity
explicitly supplies `email_verified=true`. Missing, malformed, and false claims
remain unverified, including for Keycloak and Google. Configure the provider's
claim mapping if applications need its verified-mailbox evidence; do not replace
missing evidence with a blanket verified default.

AT Protocol identities without an email address remain valid passwordless
identities. Event's outbound-email setting does not change provider verification
or take over the provider's recovery workflow.

## Switching Providers Safely

Use **Administration -> Instance Settings -> Authentication and Authorization
Providers**. The selector offers Local Identity, Keycloak, and AT Protocol.

Before switching:

- confirm the current administrator already has an exact account binding for
  the target provider;
- keep the target provider healthy and reachable;
- do not disable the only provider linked to the current administrator;
- keep AT Protocol enabled while it is primary.

The server performs the authoritative self-lockout check. The confirmation
dialog is guidance, not authorization.

## Break-Glass Recovery

If every interactive administrator path is lost but the target DID is already
linked, follow
[Lost Instance Administrator Access](troubleshooting-and-health.md#recipe-7-lost-instance-administrator-access).
The recovery tool does not create accounts, resolve handles, change onboarding
state, or grant tenant authority.

## Related

- [Environment Variables](environment-variables.md)
- [Troubleshooting & Operational Health](troubleshooting-and-health.md)
- [First-Run Administration](../administration-and-branding/admin-guide.md)
