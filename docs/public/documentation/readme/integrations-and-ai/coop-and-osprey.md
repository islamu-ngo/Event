---
description: Integrate durable moderation case intake and advisory AI signals without delegating local decisions.
---

# Coop & Osprey Moderation Integrations

ISLAMU Event integrates with **Coop** (distributed review queue) and **Osprey** (AI-assisted signal evaluation coordinator) to support community moderation workflows. Neither external system ever replaces local tenant policy, human administrative authority, or auditable database state.

---

## 1. Coop Review Queue Mirroring

* **Architecture**: When an attendee reports an event or user profile, the platform generates a metadata-first moderation ticket. If the `moderation` profile is enabled (see [Docker Compose Profiles](../self-hosting/docker-compose.md#optional-service-profiles)), the ticket is mirrored to Coop via authenticated HMAC envelopes (see [Webhooks](webhooks.md)).
* **Local Decision Finality**: Moderators can review tickets in Coop, but the ultimate execution of decisions (suspending an event, issuing warnings, or banning users) commits exclusively within ISLAMU Event via signed callback verification.

### Coop response limits

Configure the facade to return uncompressed JSON. The mirror accepts at most
1 MiB, JSON depth 32 and 256 items per collection, counting unknown fields too.
`Reporting:Coop:ResponseMaxBytes`, `ResponseMaxDepth` and
`ResponseMaxCollectionItems` may lower these limits; startup rejects values
outside 1 KiB..1 MiB, 1..32 and 1..256 respectively. Each encoded scalar is
limited to 2,000 UTF-8 bytes; case IDs and URLs must fit 200 and 500 characters.
Oversized identities are rejected, not shortened.

Budget violations and compressed responses produce non-retryable
`coop_response_*` failures without echoing the response. Correct the facade
contract before redriving affected work. A 503 or network outage remains
retryable, and caller cancellation remains cancellation. An empty successful
response still tracks the local case ID; a rejected response does not claim a
successful remote mirror. Response redirects are not followed.

---

## 2. Osprey Advisory AI Signals

Osprey acts strictly as an **advisory, signal-only evaluation coordinator**:
* Analyzes event text for policy violations and assigns priority scores to assist human moderators.
* **Hard Limitations**: Osprey cannot execute enforcement decisions, cannot close moderation tickets, cannot dispatch attendee notifications, and cannot override [Authorization Policies](../security-and-identity/authorization.md).
* Model outputs are never presented as religious rulings, ethical certifications, or automated bans.

---

## 3. Configuration & Startup Flags

Configured via environment variables (see [Environment Variables Reference](../configuration-and-operations/environment-variables.md#external-moderation-coop--osprey)):
* `REPORTING_MODE`: `LocalOnly` (default), `Coop`, `Osprey`, or `Composite`.
* `REPORTING_COOP_ENDPOINT_URL`, `REPORTING_COOP_API_KEY`.
* `REPORTING_OSPREY_ENDPOINT_URL`, `REPORTING_OSPREY_API_KEY`.

---

## Related Guides & Next Steps

* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md#external-moderation-coop--osprey)** — Configure moderation endpoints and API keys.
* **[Docker Compose Optional Profiles](../self-hosting/docker-compose.md#optional-service-profiles)** — Launch the moderation container profile.
* **[Authorization & Access Control](../security-and-identity/authorization.md)** — Understand role permissions for incident moderators.
* **[In-App Notifications](../communications-and-notifications/in-app-notifications.md)** — Alert moderators and users about ticket updates.
