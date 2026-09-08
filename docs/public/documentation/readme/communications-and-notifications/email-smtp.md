---
description: Configure optional SMTP delivery, private local capture, and delivery health without making email a core dependency.
---
<!-- ABOUTME: Operator guide to optional email and persisted SMTP delivery authority. -->
<!-- ABOUTME: Separates secret provisioning, guarded delivery changes, private Mailpit capture and external receipt. -->

# Email SMTP Delivery

Email is optional. A new instance starts with delivery disabled and can complete
Local setup, sign-in and core operations without an SMTP server. Transactional
messages use durable dispatch rather than duplicating every
[in-app notification](in-app-notifications.md).

Local initial setup uses setup-secret authority; later account creation/reset
uses instance-administrator authority. Temporary passwords are handed over
privately and must be replaced before ordinary sign-in, not emailed for
verification. Keycloak and AT Protocol still own their own verification and
authentication policies. Public operator contact email remains a separate legal
disclosure requirement.

The setup profile's **Support email** is public site contact information, stored
as `branding.support_email` and available through existing branding readback.
It is separate from both operator legal contact and account credential email.
Entering, changing or clearing support contact does not change the SMTP sender
(`email.from_address`) or delivery intent (`email.delivery_enabled`). Configure
sender identity and enable delivery separately through SMTP administration.

---

## 1. SMTP Configuration

### Persisted Settings Are The Authority

1. As an instance administrator, open `/settings/instance` and its SMTP section.
2. Save the complete non-secret transport group: host, port, security mode, sender
   address/name, timeout and certificate policy. For external delivery, use the
   provider's TLS requirements and keep certificate validation enabled.
3. Provision any required credentials through the selected
   [secret authority](../configuration-and-operations/secrets.md). With Environment
   and shipped Compose, the inputs are `MAIL_SMTP_USERNAME` and
   `MAIL_SMTP_PASSWORD`. Keep both absent for anonymous local capture; do not
   leave stale provider bindings attached to an unauthenticated test transport.
4. Explicitly enable delivery using the offered administrator action. Saving the
   host alone does not enable it. Use **Test Connection** after saving and
   enabling, then verify an actual permitted message separately.

The persisted `email.delivery_enabled` setting defaults to `false`. SMTP
environment mappings, a setup manifest or starting a container cannot override a
persisted disable. Instance configuration manifests reject SMTP setting keys;
they are not a bootstrap bypass. To disable active delivery, use preview and
confirmation and resolve any outstanding delivery commitments reported by that
workflow. Do not edit settings rows to bypass it.

Tenant overrides require delegated access. A tenant-owned transport needs its
own coherent sender/configuration and credentials; it must not inherit another
scope's credentials. Use only server-offered administration actions.

`.env.example` is intentionally a small zero-email baseline. The
[environment catalogue](../configuration-and-operations/environment-variables.md)
documents the advanced inputs separately; it is not an instruction to configure
every optional integration.

---

## 2. Delivery Behavior & Retry Guarantees

Inspect the `smtp` and `email-dispatch` entries in `/health`. There is no
`/health/email` endpoint. `/alive` checks process liveness, not delivery.

| SMTP condition | Expected readiness posture |
|---|---|
| Delivery disabled | Healthy (`smtp_disabled`); no SMTP probe or credential resolution |
| Enabled but transport incomplete/unavailable | Degraded (`smtp_configuration_unavailable`) |
| Configured SMTP diagnostic reports connection failure | Degraded (`smtp_unavailable`) |
| Capability read or unexpected diagnostic exception | Unhealthy (`email_capability_unavailable`); investigate authority/runtime failure |

Healthy and Degraded return HTTP 200 when no required check is Unhealthy. Required
database, security or authority failures still fail readiness/startup; an email
outage is not permission to bypass them. A connection test proves connectivity,
not delivery to a recipient.

Dispatch health separately reports backlog, retries and ambiguous outcomes.
Transient failures can be retried under the configured bounds. An `Unknown`
outcome is not proof that nothing was sent: use authorized reconciliation with
provider evidence, not blind replay. Do not put addresses, message content,
credentials or raw provider errors in support logs.

---

## 3. Optional Private Mailpit Capture

The shipped Compose `mail` profile is local capture only, not an external relay
or a dependency of base startup:

```bash
docker compose --profile mail up -d mailpit
```

Configure these values through instance SMTP settings, then explicitly enable
delivery:

| Setting | Local capture value |
|---|---|
| Host | `mailpit` |
| Port | `1025` |
| Security | `None` (private local capture only) |
| Sender address | A syntactically valid test address you control |
| Credentials | None; remove stale username/password bindings |

Open `http://127.0.0.1:8025` on the Docker host. The inbox is loopback-bound;
`MAILPIT_UI_PORT` changes that host port, not the binding address. SMTP has **no
published host port**: `localhost:1025` is not the Compose endpoint. The API
reaches `mailpit:1025` on its container network. A separate Standalone container
must deliberately share that network; the profile does not attach it
automatically. Keep a remote inbox private through a secured tunnel rather than
publishing it on all interfaces.

Captured mail persists in `mailpit_data` with a fixed 500-message cap. This is a
count limit, not a time-based deletion guarantee. Treat the inbox and volume as
private message storage. No relay/forwarding is configured, so captured messages
do not reach real external inboxes. Stopping capture is not the same as disabling
Event delivery; use the guarded disable workflow first if you intend to stop
sending.

Compose pins the immutable image
`axllent/mailpit:v1.30.0@sha256:0059ef81e492a7192af3816281eed6859eb078bd7bdc58b76757c13e10e53a7d`.
It is an optional, separately licensed operator pull, not bundled Standalone
content. The exact-tag Mailpit license is MIT; retain applicable notices if you
convey it. This is not a complete license/security certification of every image
layer. Mirroring, bundling, preloading or air-gapped redistribution needs separate
distribution review and approval.

## 4. Production Verification Checklist

1. Decide whether email is wanted at all. Verify zero-email core behavior before
   enabling an optional transport.
2. Save production SMTP policy and provision credentials through their authority.
3. Enable delivery explicitly and inspect `/health` plus **Test Connection**.
4. Trigger a permitted transactional delivery to an address you control, using
   the offered ticket or notification action rather than assuming Local account
   registration sends verification mail.
5. Verify external inbox receipt, sender identity and TLS/provider status. A
   captured Mailpit message is evidence of local handoff only.

---

## Related Guides & Next Steps

* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md)** — Complete configuration catalogue, separate from the curated baseline.
* **[In-App Notifications](in-app-notifications.md)** — Understand the relationship between in-app and email channels.
* **[Listmonk Integration](listmonk.md)** — Synchronize community newsletters to self-hosted Listmonk.
* **[Ticketing & Check-In](../events-and-ticketing/ticketing-and-check-in.md)** — Manage email-based lost ticket capability links.
