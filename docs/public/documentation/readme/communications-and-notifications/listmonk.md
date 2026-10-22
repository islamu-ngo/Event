---
description: Synchronize registration subscribers to an optional external Listmonk instance.
---

# Listmonk Integration

The Listmonk integration synchronizes eligible attendee registration subscribers into configured mailing lists within a self-hosted [Listmonk](https://listmonk.app) newsletter server. It is strictly an opt-in outbound subscriber synchronization bridge, not the primary notification store or [Email SMTP](email-smtp.md) dispatch worker.

---

## 1. Configuration & Secret Binding

Operators configure Listmonk parameters via [Environment Variables](../configuration-and-operations/environment-variables.md#listmonk-newsletter--subscriber-sync):

| Setting | Purpose |
|---|---|
| `LISTMONK_ENABLED` | Set `true` to activate the subscriber synchronization worker. |
| `LISTMONK_INSTANCE_URL` | Base URL of the external Listmonk instance (e.g. `https://newsletter.example.org`). |
| `LISTMONK_DEFAULT_LIST_ID` | Numerical ID of the default attendee mailing list in Listmonk. |
| `LISTMONK_PRECONFIRM_SUBSCRIPTIONS` | Set `true` if opt-in consent is pre-verified during event checkout. |
| `LISTMONK_SYNC_ON_REGISTRATION` | Automatically sync subscriber details upon successful ticket registration. |
| `LISTMONK_API_USERNAME` | Listmonk API username. |
| `LISTMONK_API_KEY` | Listmonk API token (bound via [Secrets Management](../configuration-and-operations/secrets.md)). |

---

## 2. Synchronization & Dead-Letter Recovery

* **Asynchronous Processing**: Attendee synchronization executes in the background without blocking ticket checkout latency.
* **Health Probes**: The worker registers a dedicated readiness check at `/health` to verify connectivity to the Listmonk API without echoing authentication tokens (see [Health Check Endpoints](../configuration-and-operations/troubleshooting-and-health.md#health-check-endpoints-reference)).
* **Dead-Letter Handling**: If Listmonk is temporarily unreachable, sync tasks enter a bounded retry queue with exponential backoff before landing in dead-letter storage for manual operator review.

If Event cannot establish whether Listmonk accepted a request, the task is parked rather than automatically sent again. An authorized operator must establish acceptance, establish definite non-acceptance before requesting a retry, or retain the dead letter. A timeout alone is not evidence of non-acceptance. Event records this decision separately from provider error text, together with its actor, evidence reference and time; changing an error message cannot make a task eligible for recovery.

This pre-release change introduces typed delivery evidence. Reprovision development databases containing old Listmonk queue work; do not convert old error messages into recovery decisions or blindly replay uncertain deliveries. Stop dispatch before a schema change or restore. To roll back, restore a matching application/database backup: dropping the new column loses recovery evidence and is not a safe live-queue downgrade.

---

## 3. Privacy & Consent Governance

Enabling Listmonk does not automatically subscribe all users. The platform requires explicit attendee consent during event checkout. When an attendee exercises their Right-to-Erasure, the outbox automatically triggers a deletion request to Listmonk to scrub their email address (see [Privacy Erasure & GDPR Compliance](../security-and-identity/privacy-erasure.md)).

---

## Related Guides & Next Steps

* **[Email SMTP Delivery](email-smtp.md)** — Configure transactional ticket delivery emails.
* **[In-App Notifications](in-app-notifications.md)** — Authoritative platform inbox for user alerts.
* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md#listmonk-newsletter--subscriber-sync)** — Full reference for Listmonk environment settings.
* **[Privacy Erasure & GDPR](../security-and-identity/privacy-erasure.md)** — Learn how external newsletter subscribers are handled during account deletion.
