---
description: Selective AT Protocol publication and ingestion with explicit lexicon and privacy governance.
---

# Federation & Open Protocols

ISLAMU Event federates selected event data through explicit AT Protocol (Bluesky) contracts. Local domain capabilities, attendee consent, tenant policies, vendored lexicons, and lifecycle state remain the authoritative source of truth.

---

## In this Section

* **[AT Protocol & Bluesky Jetstream](at-protocol-and-bluesky-jetstream.md)** — Linked-user OAuth, governed event publication, CarpaNet Jetstream firehose ingestion, cursor settlement, and tenant discovery.
* **[Lexicons](lexicons.md)** — Vendored lexicon schemas (`community.lexicon.calendar.*`), exact collection mappings, and version governance.

---

## Explicit Architecture Boundaries

The platform intentionally does not implement:
* ActivityPub / ActivityStreams / WebFinger protocols.
* First-party Personal Data Server (PDS) or AppView hosting.
* Bidirectional relay bridges or unmoderated social post streams.
* Dynamic runtime lexicon discovery or wildcard ingestion.

---

## Related Guides & Next Steps

* **[AT Protocol Authentication](../security-and-identity/authentication.md#at-protocol-sign-in)** — Choose primary passwordless login or optional exact-DID-linked sign-in.
* **[Multi-Tenancy Isolation](../security-and-identity/multi-tenancy.md)** — Control which community tenants participate in open federation.
* **[Architecture & Outbox Workflows](../getting-started/architecture-and-request-flows.md)** — Understand transactional outbox guarantees for federated records.
* **[Privacy Erasure & GDPR](../security-and-identity/privacy-erasure.md)** — How federated posts and RSVPs are handled upon account erasure.
