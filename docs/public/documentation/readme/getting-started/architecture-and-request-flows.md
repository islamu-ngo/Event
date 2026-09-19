---
description: Understand browser routing, application operations, authority, tenancy, and durable effects.
---

# Architecture & Request Flows

ISLAMU Event follows [Clean Architecture](../contributing/clean-architecture.md). The Domain and Application layers own business rules; Persistence and Infrastructure implement data and provider concerns; `Explore.API` composes the runtime; and `Explore.Blazor` (Backend-for-Frontend / BFF) manages the browser session.

---

## 1. Browser Request Flow

1. The browser connects over HTTPS to the Blazor BFF (`Explore.Blazor`).
2. The BFF owns the encrypted session cookie and obtains or refreshes access tokens via [Keycloak Authentication](../security-and-identity/authentication.md).
3. The BFF proxies requests to `Explore.API`, forwarding the bearer JWT and resolved [Multi-Tenant Context](../security-and-identity/multi-tenancy.md).
4. The API evaluates caller permissions and invokes the requested application operation before reading or changing stored data.
5. The API response embeds dynamic [HAL Links](../security-and-identity/authorization.md#the-golden-rule-of-client-ui-affordances) indicating which follow-up actions the user is authorized to perform right now.

> [!NOTE]
> The browser client never inspects user roles or JWT claims to invent action buttons. Action affordances are strictly driven by server-issued HAL links.

---

## 2. Write (Command) Flow

1. **Endpoint Boundary**: Authentication and high-level route policies run first.
2. **Resource Authorization**: Evaluates the caller, tenant boundary, and target entity state via [Authorization (Local RBAC or Cerbos)](../security-and-identity/authorization.md).
3. **Domain Validation**: The application operation validates the business rules.
4. **Atomic Settlement**: A single serializable transaction commits state changes to PostgreSQL or SQLite.
5. **Transactional Outbox**: Side effects (such as [Transactional Emails](../communications-and-notifications/email-smtp.md), [Outgoing Webhooks](../integrations-and-ai/webhooks.md), or [AT Protocol Federation](../federation-and-open-protocols/at-protocol-and-bluesky-jetstream.md)) are written to outbox tables within the same database transaction.
6. **HAL Affordance**: The response returns the updated resource with freshly computed `_links`.

---

## 3. External Callback Flow

Incoming provider callbacks (such as Stripe payment confirmations, registration webhooks, or moderation events) use dedicated signature verification:
* The payload signature is verified before processing (see [Webhooks & Callbacks](../integrations-and-ai/webhooks.md)).
* Intake is idempotently recorded before effects are applied to domain state.
* Browser return URLs or client redirects are **never** treated as payment truth (see [Paid Events & Payouts](../events-and-ticketing/paid-events-and-payouts.md)).

---

## 4. Operational Probes & Health

* **`/alive`**: Confirms that the Kestrel web host process is executing.
* **`/health`**: Evaluates active connections to PostgreSQL, Keycloak, storage, and policy engines (see [Troubleshooting & Health](../configuration-and-operations/troubleshooting-and-health.md#health-check-endpoints-reference)).
* **`/metrics`**: Exposes Prometheus-compatible operational measurements.

---

## 5. Durable Authority Patterns

* **Notifications**: The [In-App Notification Inbox](../communications-and-notifications/in-app-notifications.md) remains the authoritative state; Web Push and SSE merely notify the client to pull updates.
* **Commercial Truth**: Signed webhook events and ledger reconciliation establish payment state (see [Paid Events & Payouts](../events-and-ticketing/paid-events-and-payouts.md)).
* **Federation**: Local lifecycle state strictly governs outbound publication; cursor settlements commit atomically (see [AT Protocol Federation](../federation-and-open-protocols/at-protocol-and-bluesky-jetstream.md)).
* **Privacy Erasure**: Account deletion establishes an immutable anti-resurrection fence before triggering external background deletions (see [Privacy Erasure & GDPR Compliance](../security-and-identity/privacy-erasure.md)).

---

## Related Guides & Next Steps

* **[Self-Hosting Overview](../self-hosting/README.md)** — Select the optimal deployment topology for your organization.
* **[Authentication Architecture](../security-and-identity/authentication.md)** — Learn how OIDC tokens, cookies, and Keycloak realms interact.
* **[Authorization & Access Control](../security-and-identity/authorization.md)** — Understand Local RBAC vs. Cerbos PDP and HAL affordance gating.
* **[Clean Architecture Guide](../contributing/clean-architecture.md)** — Deep dive into Domain, Application, and Persistence boundaries.
