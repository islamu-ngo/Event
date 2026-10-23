---
description: Operate incoming callbacks and outgoing product webhooks as separate trust boundaries.
---

# Webhooks & Callbacks

Incoming provider callbacks and outgoing product webhooks represent completely separate trust boundaries in ISLAMU Event. They use different authentication, replay windows, delivery engines, and recovery runbooks.

---

## 1. Outgoing Delivery Modes

Configured via `WEBHOOKS_PROVIDER` in [Environment Variables](../configuration-and-operations/environment-variables.md#11-advanced-outgoing-webhooks-svix-infrastructure):

| Mode | Engine / Architecture | Best Fit |
|---|---|---|
| `Disabled` | Outgoing webhooks disabled entirely. | Small local deployments with no external subscribers. |
| `Local` | Built-in in-process dispatcher signing Svix-compatible HMAC envelopes. | Single-container Standalone or lightweight Compose setups. |
| `Svix` | Self-hosted [Svix v1.96.1](https://svix.com) with PostgreSQL and Redis. | High-throughput multi-tenant production clusters. |
| `Composite` | Explicit routing between Local and Svix per event type. | Hybrid enterprise migrations. |
| `DryRun` | Validates payloads and records outbox work without contacting endpoints. | Staging and test verification. |

> [!NOTE]
> To run self-hosted Svix, start Compose with the `webhooks` profile: `docker compose --profile webhooks up -d` (see [Docker Compose Profiles](../self-hosting/docker-compose.md#optional-service-profiles)).

---

## 2. Local Dispatcher Security & SSRF Protection

When running in `Local` mode:
* Signs outgoing payloads using standard HMAC-SHA-256 signatures (`webhook-signature` headers).
* Applies an eight-step exponential retry policy with jitter.
* Enforces strict Server-Side Request Forgery (SSRF) protections: private IP ranges (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), loopback (`127.0.0.1`), link-local (`169.254.0.0/16`), and cloud metadata endpoints (`169.254.169.254`) are blocked by default.

---

## 3. Incoming Provider Callbacks

Incoming routes include:
* **Payment Webhooks**: Stripe Connect payment intents and refund receipts (see [Paid Events & Payouts](../events-and-ticketing/paid-events-and-payouts.md)).
* **Moderation Webhooks**: Signal evaluation callbacks from [Coop & Osprey](coop-and-osprey.md).
* **Operational Intake**: Svix endpoint status events.

Every incoming callback verifies HMAC signatures, evaluates idempotency keys, and records intake to the database before executing any business transitions.

---

## Related Guides & Next Steps

Svix readiness exposes whether environment and version coordinates are configured,
not their raw values. Health consumers must use `environmentConfigured`,
`providerVersionConfigured`, and `capabilityPolicyVersionConfigured`; the former
`environment`, `providerVersion`, and `capabilityPolicyVersion` fields are removed.
Provider selection is normalized to a supported mode or `unknown`. Readiness still
validates the exact configured conformance profile before admitting the provider.

Publication-worker diagnostics identify dispatch and reconciliation failures by
fixed categories rather than provider exception types or details. Inspect governed
publication state for recovery decisions; raw provider failures are not exported
through technical logs. Canceling a worker operation does not emit a failure log.

* **[Docker Compose Runbook](../self-hosting/docker-compose.md#optional-service-profiles)** — Launch the Svix webhook container profile.
* **[Paid Events & Payouts](../events-and-ticketing/paid-events-and-payouts.md)** — Learn how Stripe webhooks settle ticket sales.
* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md#11-advanced-outgoing-webhooks-svix-infrastructure)** — Configure Svix Redis, DSN, and JWT signing keys.
* **[Secrets Management](../configuration-and-operations/secrets.md)** — Safely bind webhook signing secrets.
