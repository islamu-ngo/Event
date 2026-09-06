---
description: Operate primary or linked AT Protocol login, governed publication, exact-collection ingestion, and tenant-gated discovery.
---
<!-- ABOUTME: Guides operators through AT Protocol login, publication and governed event ingestion. -->
<!-- ABOUTME: Explains database-backed login state, browser binding and key-persistence recovery requirements. -->

# AT Protocol & Bluesky Jetstream

ISLAMU Event implements a selective **AT Protocol (Bluesky)** federation integration. It is designed specifically for cross-platform event syndication and calendar interoperability, rather than operating as a general-purpose social network host.

---

## 1. Authentication & Publication

* **Primary or Linked Login**: AT Protocol can be the primary passwordless identity provider, creating an account for a verified Decentralized Identifier (DID). When optional alongside Local Identity or Keycloak, sign-in requires an existing exact DID link. Neither mode matches accounts by email or grants authorization roles (see [Authentication Architecture](../security-and-identity/authentication.md#at-protocol-sign-in)).
* **Outbox-Backed Publication**: Outbound event publication is queued strictly via the transactional outbox after the primary database commit succeeds (see [Architecture & Request Flows](../getting-started/architecture-and-request-flows.md#2-write-command-flow)).
* **Supported Records**:
  * Calendar Events: Published to the user's repository under `community.lexicon.calendar.event`.
  * Attendance Intent: Published under `community.lexicon.calendar.rsvp` with status `#going`.

### Login state and browser binding

OAuth login state and one-time cross-domain handoffs use the primary database, not Redis or process-local memory. The BFF accesses them through a private authenticated API; no database credentials belong in the BFF. Existing durable OAuth sessions remain separate.

Use HTTPS for the public instance and every configured tenant login origin, including local development. Keep the same browser throughout login. A protected host-only proof cookie lasts fifteen minutes and supports parallel login attempts without being rewritten. State lasts at most ten minutes (or the shorter configured SDK lifetime), reserving two minutes for handoff. A handoff lasts at most two minutes and never outlives browser proof. Near proof expiry, wait for the returned `Retry-After` before starting another attempt; do not clear cookies underneath other pending logins.

For a custom-domain login, the canonical callback redirects an opaque code back to the initiating domain without signing the browser in on the canonical host. Opening that code in another browser cannot sign it in or consume the legitimate destination handoff.

If login state or a handoff expires while the API response is in transit, sign-in is rejected even if the browser proof is still valid. Start a fresh login; a successfully consumed code cannot be recovered or retried.

For Redis-free single-node hosting, persist the existing native BFF Data Protection key directory. Replicas need the same persistent key directory and application discriminator, with restricted permissions and encryption at rest. An explicitly configured cache connection still selects Redis for those protection keys; remove that selection when choosing a completely Redis-free deployment. Keep the OAuth signing key ring available across replicas and retain keys needed by outstanding flows.

Restart in-flight logins after upgrading from the old transient backend or losing their cookies/keys. The memory-store and configurable handoff-lifetime options are removed, without compatibility aliases. After an API/database outage or a lost consume response, restore dependencies and begin a new login rather than retrying the old callback.

### Readiness, cleanup, and retention

The `atproto-authentication` readiness check validates local configuration and signing material, then exercises a private authenticated create/read/consume round trip using random, non-secret probe data. It has a two-second transport deadline, no automatic retries or hedging, and caches the completed result for ten seconds. It does not contact a user's PDS or authorization-server discovery service; a healthy check is not a guarantee that every external provider can complete login.

Disabled AT Protocol is healthy. An unavailable AT Protocol primary provider makes this check unhealthy (`/health` returns `503`). When the primary provider is explicitly Local Identity or Keycloak, an optional AT Protocol failure is degraded (`200`) and does not itself remove that host from service. Other failing readiness checks can still return `503`. `/alive` is independent of this store probe. Follow the [health troubleshooting guide](../configuration-and-operations/troubleshooting-and-health.md#at-protocol-readiness-recovery) after an outage.

Keep the `atproto-transient-cleanup` scheduler job running even after disabling AT Protocol login. With the global scheduler enabled, it runs every minute without overlapping itself. Each pass deletes at most 500 rows per batch and five batches from each of the transient and assertion-replay tables: at most ten delete calls and 5,000 rows total. It captures one time, stops early when a batch is short, and adds no 24-hour grace period. There is no separate cleanup configuration switch.

OAuth state expires within ten minutes, handoffs within two minutes, and synthetic probes after thirty seconds. Assertion replay claims remain through the complete assertion acceptance window, including five seconds of clock skew. Expired authentication material is denied even when cleanup is delayed. Cleanup removes active database rows; it does not erase older backups. Apply your backup retention and access-control policy separately, and retain the shared protection keys needed for valid sessions.

Keep every BFF and API host clock synchronized within five seconds of trusted UTC and monitor that bound. Two hosts can then differ by at most ten seconds; cleanup retains replay claims for ten additional seconds so a faster host cannot reopen an assertion still accepted by a slower one. This does not extend assertion validity or OAuth-state/handoff lifetimes. If a host drifts outside the bound, restore clock synchronization before returning it to authentication traffic.

Monitor `explore.atproto.transient.operations`, `explore.atproto.transient.cleanup_runs`, and `explore.atproto.transient.cleanup_rows`. Operation outcomes and cleanup success/failure use fixed labels only; row counts describe completed passes, not partial work from a failed pass. A lost delete acknowledgement stops the pass rather than retrying another batch; subsequent scheduled passes resume. Never add tenant/user identifiers, locators, assertions, payloads, or key material to labels or support reports.

---

## 2. CarpaNet Jetstream Ingestion & Cursor Settlement

External community events are ingested via the Bluesky Jetstream firehose:
* **Exact Collections**: Subscriptions ingest only recognized [Lexicons](lexicons.md):
  * `community.lexicon.calendar.event`
  * `community.lexicon.calendar.rsvp`
* **Atomic Cursor Settlement**: Inbound event records and the stream playback cursor commit atomically in a single transaction. A cursor never advances without durable local record creation, preventing dropped events during worker restarts.
* **No Echo Loops**: Ingested records are never re-broadcast to the outbound outbox.

---

## 3. Multi-Tenant Governance

Ingested external events are subject to tenant federation policies (see [Multi-Tenancy](../security-and-identity/multi-tenancy.md)):
* Tenant administrators control whether external federated events appear in local community search listings.
* Federated records display clear attribution links back to their origin Bluesky post.

---

## Related Guides & Next Steps

* **[Lexicons Reference](lexicons.md)** — Review JSON schemas and field definitions for calendar records.
* **[Authentication Architecture](../security-and-identity/authentication.md#at-protocol-sign-in)** — Understand primary passwordless login and optional linked-account sign-in.
* **[Multi-Tenancy Architecture](../security-and-identity/multi-tenancy.md)** — Tenant boundaries and discovery policies.
* **[Architecture & Request Flows](../getting-started/architecture-and-request-flows.md)** — Transactional outbox pattern for event delivery.
