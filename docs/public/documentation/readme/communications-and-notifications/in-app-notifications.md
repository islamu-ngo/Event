---
description: Operate the durable tenant-scoped inbox, fanout, SSE, and Web Push hints.
---

# In-App Notifications Architecture

The in-app inbox is the authoritative, durable source of truth for user-visible notifications. Realtime transports (SSE and Web Push) act as lightweight hints informing browser clients to refresh their inbox state.

---

## 1. Notification Model & Available Actions

Notifications are authenticated, user-owned, and strictly [tenant-scoped](../security-and-identity/multi-tenancy.md). Supported operations include:

* **Read**: Retrieve unread counts, paginated lists, and message details.
* **Status Updates**: Mark individual messages or all as read.
* **Lifecycle Management**: Archive/unarchive, snooze/unsnooze, and soft delete.
* *Note*: The platform does not implement mark-as-unread or permanent hard deletion through the user API.

---

## 2. Outbox Fanout & Deduplication

Event publication, ticket confirmations, and moderation alerts utilize the **Transactional Outbox Pattern** (see [Architecture & Request Flows](../getting-started/architecture-and-request-flows.md#2-write-command-flow)):
* Business transactions commit the domain state change and the notification outbox entry in a single atomic database transaction.
* Background workers process fanout with deterministic deduplication keys, ensuring idempotent processing even during network interruptions.
* **Server-Sent Events (SSE)** and **Browser Web Push** deliver non-authoritative wake-up signals; the client always fetches the canonical message from the API.

When a push service reports a retired endpoint (`404` or `410`), the dispatch
failure and subscription deactivation are stored together. A database failure
does not leave half-completed cleanup, and another tenant's subscription is
unaffected. The durable inbox remains available independently of push delivery.

---

## 3. Multi-Channel Boundaries

An in-app notification triggers an external email only when the originating intent explicitly mandates external delivery (see [Email SMTP](email-smtp.md)):
* General activity and subscription alerts remain strictly in-app.
* Critical flows (such as ticket delivery, password recovery, or heavy moderation decisions via [Coop & Osprey](../integrations-and-ai/coop-and-osprey.md)) explicitly track both in-app and SMTP delivery states.

---

## Related Guides & Next Steps

* **[Email SMTP Configuration](email-smtp.md)** — Configure MailKit transactional email dispatch.
* **[Listmonk Integration](listmonk.md)** — Synchronize attendee newsletters with self-hosted Listmonk.
* **[Architecture & Request Flows](../getting-started/architecture-and-request-flows.md)** — Learn how outboxes ensure zero message loss.
* **[Ticketing & Check-In](../events-and-ticketing/ticketing-and-check-in.md)** — Attendee confirmation and lost-ticket notifications.
