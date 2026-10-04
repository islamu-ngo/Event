---
description: Understand the GDPR Right-to-Erasure workflow, anti-resurrection fences, and authority storage topologies.
---

# Privacy Erasure & GDPR Compliance

Under data privacy regulations such as the GDPR, when an attendee or user requests account deletion, the platform must guarantee that their personal identifiable information (PII) is completely destroyed and **cannot be accidentally resurrected** by database restore operations or distributed retries.

ISLAMU Event implements an **Authority-First, Anti-Resurrection Architecture** to enforce this guarantee.

---

## The Anti-Resurrection Workflow

The erasure process follows a strict sequence to prevent data leaks or partial deletions:

```mermaid
sequenceDiagram
    autonumber
    actor User as User / Data Subject
    participant API as Explore.API
    participant AuthStore as Privacy Erasure Authority
    participant AppDB as Primary Application DB
    participant Outbox as Provider Cleanup Outbox

    User->>API: DELETE /api/user (Idempotency-Key: UUIDv7)
    API->>AuthStore: Commit erasure fact for the old internal account
    API->>AppDB: Establish old-subject fence
    API->>AppDB: Serializable Settlement (Purge PII, anonymize foreign keys)
    API->>Outbox: Enqueue async cleanup (Keycloak user, avatars in S3, Stripe customer)
    API-->>User: 202 Accepted (Location: /api/privacy-erasure/status + ErasureReceipt)
```

1. **Anti-Resurrection Fence**: A retained old-subject fact and local fence prevent stale work from recreating the erased internal account. This does not prohibit new registration with the same external identity.
2. **Authority Fact**: The erasure event is recorded in a dedicated, isolated authority store *before* local data disposal begins.
3. **Serializable Settlement**: In one atomic transaction, the user’s personal data is scrubbed, registrations are anonymized, and foreign keys are safely unlinked.
4. **Asynchronous External Cleanup**: Background outbox workers delete the user from Keycloak, purge media from S3/local storage, and notify external payment gateways.
5. **Single-Use Receipt**: The user receives a `202 Accepted` response with an opaque `ErasureReceipt` capability token. The receipt can be used to query `/api/privacy-erasure/status` until completion, after which all trace of the receipt is destroyed.

---

## Verified Identity Addresses

Account erasure removes the account's verified identity address claims and all
associated verification proofs, including proofs already invalidated. These
are removed before the sign-in bindings and profile PII in the same local
transaction. Another account's claims and proofs are unaffected; no address or
verification history is copied into the retained erasure authority.

A failed local purge remains fenced and can be replayed. When a primary
database backup restores these records, startup replay purges them again before
traffic is admitted, provided the retained authority is preserved independently.
Releasing an address claim does not lift the erased account's anti-resurrection
fence or authorize a delayed synchronization to recreate its personal data.

## Fresh Registration After Erasure

Ordinary erasure removes profile PII, provider bindings and verified-address
proofs. It does not create an external-identity registration ban. Otherwise
admissible external authentication may automatically create a fresh account,
including with the same provider identity or released email. The new account
does not inherit the old account's ID, private data, permissions or consent.

Startup still validates and replays retained old-subject erasure facts before
admitting traffic. It does not erase a legitimate fresh account merely because
its provider identity resembles an erased account. Preserve the retained
authority and supported backup horizon; a fresh signup does not bypass an
invalid restore or an unavailable authority.

## Reserved Future Moderation Keys

`PRIVACY_ERASURE_IDENTITY_FENCE_KEY` and
`PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID` remain optional reserved configuration.
They are not required for ordinary enrollment, erasure or API startup. Their
approved Infisical folder is `/api`, not a separate `/privacy` folder.

If a future moderation feature uses identity recognition, its key must be stable,
held in the explicitly selected approved authority and preserved across restart
and restore. The explicit key provider still rejects missing/invalid material;
no ephemeral or predictable replacement is generated. Live rotation is not
implemented. The configuration names alone do not enable banning.

Future moderation is a separate lifecycle: an existing banned account may
authenticate into restricted pages with a remaining-ban timer and deletion.
An identity deleted during an active ban must instead receive Account suspended
before a replacement account is provisioned. Capture and retention need a
defined moderation purpose and expiry; erasure records are not automatically
moderation records. That banning behavior is not implemented in this change.

## Choosing an Authority Storage Topology

The Privacy Erasure Authority store must be configured in your environment via `PRIVACY_ERASURE__AUTHORITY__TOPOLOGY`:

| Topology | Storage Mechanism | Best Fit | Primary Constraint |
|---|---|---|---|
| **`EmbeddedSqlite`** | Dedicated SQLite database at `/app/data/privacy_erasure_authority.db` | Single-server Compose and Standalone | Single API container writer; requires volume persistence |
| **`CoLocated`** | Shared table schema within primary PostgreSQL DB | Minimal dev environments | No independent protection if primary DB is restored from stale backup |
| **`ExternalDatabase`** | Completely isolated external PostgreSQL instance | High-availability enterprise clusters | Requires managing a secondary PostgreSQL database |

> [!TIP]
> **Our Recommendation:**
> - **We recommend `EmbeddedSqlite`** for standard self-hosters. It runs with zero operational overhead, uses dedicated storage, and guarantees that even if your primary PostgreSQL database is restored to a state from last week, the SQLite erasure store will prevent previously deleted users from being resurrected!
> - **We recommend `ExternalDatabase`** only for enterprise multi-node clusters running multiple API replicas that cannot share a local SQLite file.

---

## The Golden Rule of Disaster Recovery

> [!CAUTION]
> **Never Restore the Primary Database Without the Erasure Store!**  
> If you restore an older database snapshot (e.g., from 3 days ago) to recover from corruption, any user who requested account deletion yesterday would normally be restored ("resurrected") into the database.
> 
> When ISLAMU Event starts, the **`PrivacyErasureStartupGate`** automatically replays all facts from the Privacy Erasure Authority against the application database before HTTP traffic is allowed. If an erased user is found in the restored database, the gate immediately re-purges their records and re-establishes the anti-resurrection fence!

---

## Related Guides & Next Steps

* **[Backup, Restore & Upgrade](../configuration-and-operations/backup-restore-upgrade.md)** — Production backup scripts and disaster recovery rehearsals.
* **[Docker Standalone Runbook](../self-hosting/docker-standalone.md)** — Operate the embedded SQLite erasure authority on a single server.
* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md#7-privacy-erasure-authority-gdpr--anti-resurrection)** — Configure erasure topology and busy timeout dials.
* **[Custom Properties Governance](../events-and-ticketing/custom-properties.md)** — Learn how long-tail attendee answers are scrubbed during account deletion.
