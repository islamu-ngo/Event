<!-- ABOUTME: Canonical documentation for the Privacy Erasure Authority, its concrete meaning, and storage topologies. -->
<!-- ABOUTME: Explains EmbeddedSqlite, CoLocated, and ExternalDatabase modes and guides self-hosters through topology selection. -->

# Privacy Erasure Authority & Storage Topologies

> **Audience:** Operators | Self-Hosters | Contributors | AI agents
> **Status:** Implemented
> **Owner:** Security / Platform
> **Last Verified:** 2026-08-27
> **Source Anchors:** `src/Explore.Application/Configuration/PrivacyErasureOptions.cs`, `src/Explore.Application/Services/RetainedAuthorityPrivacyErasureWorkflow.cs`, `src/Explore.Application/Services/PrivacyErasureApplier.cs`, `src/Explore.Persistence/Privacy/ErasureAuthority/EmbeddedPrivacyErasureAuthorityStorage.cs`, `src/Explore.Persistence/Privacy/ErasureAuthority/Repositories/EmbeddedPrivacyErasureAuthorityRepository.cs`, `src/Explore.Persistence/Privacy/ErasureAuthority/Repositories/EfCorePrivacyErasureAuthorityRepository.cs`, `src/Explore.Persistence/Privacy/ErasureAuthority/PrivacyErasureAuthorityDbContext.cs`, `src/Explore.API/BackgroundServices/PrivacyErasureStartupGate.cs`, `src/Explore.API/HealthChecks/PrivacyErasureReadinessHealthCheck.cs`

---

## 1. Concrete Meaning of Privacy Erasure Authority

The **Privacy Erasure Authority** is the single platform-wide engine responsible for orchestrating User erasure, enforcing anti-resurrection fences, maintaining payload-free audit proofs, and settling external provider side-effects.

Rather than executing ad-hoc SQL deletions, the platform operates on an **authority-first, replayable contract**:

```text
erasure request -> append authority fact (committed first)
                   |
                   v
         establish user fence
                   |
                   v
 serializable application transaction:
   +--> apply local PII hard-deletes & anonymization
   +--> update local application mirror & checkpoint
   +--> materialize protected provider outbox work
   +--> invalidate cache authority
   +--> persist receipt hash
                   |
                   v
 return 202 Accepted + short-lived receipt
                   |
                   v
 post-commit specialized provider outbox settlement
```

### Core Invariants

1. **Authority-First Ordering**: An immutable, policy-versioned authority fact is recorded and committed *before* local PII disposal or remote provider work begins.
2. **User Fencing**: Establishing a fence prevents raced writes, background workers, or cache rematerializers from recreating PII for a pending or erased subject.
3. **Atomic Local Settlement**: Local PII hard-deletes, anonymizations, actor tombstones, application mirror/checkpoint updates, protected provider outbox materialization, and receipt hash persistence execute within a single serializable application transaction.
4. **Truthful Asynchronous Contract**: Initial requests return `202 Accepted` alongside a short-lived `ErasureReceipt` authentication token. Status polling (`GET /api/privacy-erasure/status`) requires the receipt token and returns non-cacheable (`private, no-store`) bounded phase codes.
5. **Startup & Restore Replay**: At application startup, the startup gate replays all authority facts missing from the local checkpoint before serving traffic.
6. **Bounded Retention**: The authority publishes a PII-free high-water/floor state. Compaction deletes only an expired contiguous prefix, preserves and pseudonymizes held evidence, and advances the floor in the same transaction.

### Configuration Portability Privacy Boundary

Configuration-manifest and tenant-package exports are not subject-data export
or erasure artifacts. Their closed registry excludes PII, users, events,
registrations, orders, payments, application data, operational state, provider
bindings, topology, and all secret material. Import receipts retain UUIDs,
digests, stable codes, selected section names, fidelity state, and bounded
effect status, never configuration values or actor display data. Protected
rollback snapshots follow their own short configuration-recovery window and do
not replace erasure-authority, primary-database, or application-data retention
policies.

Legal-document portability copies reviewed Markdown source into a new target
draft. It never copies publication or user-acceptance history, so migration
cannot fabricate consent or resurrect retired legal evidence.

---

## 2. Storage Topologies: `EmbeddedSqlite`, `CoLocated`, `ExternalDatabase`

The platform workflow code is **100% identical** regardless of deployment choice. The configuration setting `PrivacyErasure:Authority:Topology` (`ERASURE_DATABASE_TOPOLOGY`) selects one of three durable persistence topologies for storing the authority ledger. The former non-durable `None` mode is rejected because its in-memory watermark could not remain consistent with the persisted replay checkpoint across restart.

| Feature / Guarantee | `EmbeddedSqlite` Mode | `CoLocated` Mode | `ExternalDatabase` Mode |
|---|---|---|---|
| **Authority Database Placement** | Dedicated local SQLite file, default `/app/data/privacy_erasure_authority.db` | Primary application PostgreSQL/SQLite database | Separate, independently managed PostgreSQL database instance |
| **Supported Primary DBs** | **All 5 providers** (PostgreSQL, SQLite, SQL Server, MariaDB, MySQL) | **PostgreSQL or SQLite only** | **All 5 providers** (Authority DB itself is separate PostgreSQL) |
| **Connection Credentials** | None; protective filesystem permissions only | Application database credentials | Structured endpoint plus separate function-only runtime and migrator roles |
| **`restoreReplayProtection` Health Flag** | `true` when its dedicated file is kept outside the primary restore | `false`; authority follows primary restore lifecycle | `true` when the external database has an independent restore lifecycle |
| **Rollback Resilience (Local Tx Failure)** | **Yes** — authority append commits before the application transaction | **Yes** — authority append commits before the application transaction | **Yes** — authority append commits before the application transaction |
| **Stale Application Restore Protection** | **Yes** — when the authority file is not overwritten by the primary restore | **No** — not guaranteed beyond primary restore fidelity | **Yes** — untouched external authority replays missing erasures against restored primary DB |
| **Concurrency Ceiling** | Exactly one writer/API replica; private cache, WAL, bounded busy timeout | Application-primary limits | Normal PostgreSQL deployment limits and function ACLs |
| **Operator Backup Units** | Primary database backup **plus** the dedicated authority-file backup | One primary database backup containing authority rows | Primary database backup **plus** an independently managed external PostgreSQL authority backup |
| **Target Use Case** | Local development, CI, and single-replica self-hosting (Default) | Single-database deployments and operationally simple upgrades | Multi-replica/HA production and independently operated compliance storage |

### Primary Database vs. Privacy Authority Compatibility Matrix

The primary application persistence (users, events, registrations, ASP.NET Data Protection keys) fully supports all five database engines. The Privacy Erasure Authority topology compatibility is as follows:

| Primary Database Provider | `EmbeddedSqlite` (Default) | `CoLocated` | `ExternalDatabase` |
|---|:---:|:---:|:---:|
| **PostgreSQL** | ✅ Supported | ✅ Supported | ✅ Supported (separate PostgreSQL instance) |
| **SQLite** | ✅ Supported | ✅ Supported | ✅ Supported (separate PostgreSQL instance) |
| **SQL Server** | ✅ Supported | ❌ Fails Closed | ✅ Supported (separate PostgreSQL instance) |
| **MariaDB** | ✅ Supported | ❌ Fails Closed | ✅ Supported (separate PostgreSQL instance) |
| **MySQL** | ✅ Supported | ❌ Fails Closed | ✅ Supported (separate PostgreSQL instance) |

#### Why does `CoLocated` only support PostgreSQL and SQLite?
The Privacy Erasure Authority is not a generic CRUD table; it requires strict engine-level concurrency and writer serialization guarantees:
- **PostgreSQL**: Implements engine-native row-level locking (`SELECT ... FOR UPDATE`), atomic monotonic sequence generators, and isolated schema migration histories (`CoLocatedPostgresPrivacyErasureAuthorityRepository`) to ensure the erasure fact commits before the application transaction.
- **SQLite**: Leverages SQLite's native single-writer, private cache, and WAL lock guarantees on the shared local database file (`EmbeddedPrivacyErasureAuthorityRepository`).
- **SQL Server, MariaDB, and MySQL**: Engine-native authority repositories with verified locking semantics, sequence generators, and atomic fences have not been implemented. Rather than allowing unverified concurrency abstractions that could risk race conditions during GDPR erasures, the platform **fails closed**. Operators using SQL Server, MariaDB, or MySQL should use `EmbeddedSqlite` (the default) or `ExternalDatabase`.

#### Why does `ExternalDatabase` require a separate PostgreSQL database?
`ExternalDatabase` is the high-compliance, multi-replica/HA deployment mode. It is architected around PostgreSQL-specific security controls:
1. **Zero-Table-Access Runtime Role**: The runtime application role has no direct table permissions (no `SELECT`, `INSERT`, `UPDATE`, or `DELETE` on raw ledger tables).
2. **`SECURITY DEFINER` Stored Functions**: Append, replay, state reads, retention evaluation, and compaction execute through hardened PostgreSQL functions with strict parameter validation and a fixed `search_path`.
3. **Role Separation**: Migration DDL runs under a dedicated `migrator` role, while runtime traffic uses the restricted `runtime` role.
4. Standardizing the external compliance server on PostgreSQL provides an enterprise-hardened, audit-ready compliance sink without maintaining parallel function-level security definitions for other engines.

#### Fail-Closed and Secret-Safe Startup Guarantee
If an unsupported combination is configured (e.g. `Database:Provider=SqlServer` with `PrivacyErasure:Authority:Topology=CoLocated`), the application **fails closed immediately in-memory during Dependency Injection composition** before opening any sockets or performing adapter/database I/O. The resulting error message clearly indicates the configuration problem and bounded remediation options, and **never exposes passwords, connection strings, host credentials, or usernames in exception traces or logs**. Unsupported or removed authority contracts have no fallback, translation, dual write, or compatibility shim.

---

## 3. Why `EmbeddedSqlite` Is The Default

The authority must survive a stale primary-database restore. A separate local
file is the smallest deployment that provides an independent restore boundary
without requiring a second database server.

### 1. Small Self-Hosting Footprint

`EmbeddedSqlite` requires no authority server or credential. It still uses the
same authority-first workflow, fencing, checkpoints, receipts, provider
outboxes, and replay gate as `ExternalDatabase`.

### 2. Architectural Integrity & Application Rollback Safety

The authority append commits through its dedicated context before the primary
application transaction. If local mutation rolls back, restart/retry sees the
retained fact and reapplies the erasure.

### 3. Independent Restore Boundary

Back up the embedded authority file separately and never overwrite it during a
primary restore. This preserves the facts needed to re-erase data resurrected
by an older primary backup.

### 4. Explicit Operational Ceiling

Embedded authority startup requires `WriterReplicaCount=1`, a local durable
path, private cache, WAL, and a bounded busy timeout. Multi-replica/HA
deployments must choose `ExternalDatabase`, while `CoLocated` is suitable when
single-database operating simplicity is preferred.

---

## 4. Self-Hoster Decision & Selection Guide

Use this decision matrix to select the right topology for your environment:

- **EmbeddedSqlite** when local one-writer, file-backed isolation is acceptable (works with all 5 primary databases).
- **CoLocated** when you want single-database operation with PostgreSQL or SQLite and do not require independent authority backup-restore.
- **ExternalDatabase** when you need isolated compliance storage and independent authority recovery on a dedicated PostgreSQL instance.

### Guidance Summary

- **Choose `EmbeddedSqlite` if**:
  - You are running local development, automated CI test suites, or single-container self-hosting (`docker-compose.yml`).
  - You run exactly one writer/API replica and can provide local durable storage.
  - You use any primary database (PostgreSQL, SQLite, SQL Server, MySQL, MariaDB).
  - You can back up and restore the authority file independently from the primary database.

- **Choose `ExternalDatabase` if**:
  - You operate enterprise multi-tenant or production SaaS environments with multiple API replicas.
  - You maintain independent backup/restore schedules for primary vs compliance databases.
  - You require absolute guarantees that restoring an application database backup from 30 days ago will automatically re-erase all accounts deleted during those 30 days upon service startup.

- **Choose `CoLocated` if**:
  - You are using **PostgreSQL** or **SQLite** as your primary database.
  - You want a single primary database with fewer components.
  - Your topology or platform operations already treat app and authority recovery together.
  - You do not require independently operated compliance database recovery.

---

## 5. Migration Ownership and History Separation

For deployed installations, `Event.MigrationService` owns application, Data
Protection, and **exactly one** authority migration path selected by the
topology. Provider-native application and Data Protection migration assemblies
and their distinct history tables remain separate; they are not combined with
each other or with the selected authority path. The documented standalone
image is the in-process exception: it applies those same three migration
responsibilities before binding HTTP.

## 6. Retained Floor, Holds, and Recovery

`PrivacyErasureAuthorityState` contains only `HighWaterSequence` and
`RetainedFloorSequence`. The high-water mark is the greatest sequence ever
allocated. The floor is the greatest sequence whose original replay identity
is no longer required. Both values are monotonic and the floor can never exceed
the high-water mark.

Every newly appended fact receives `RetentionExpiresAtUtc` derived from
`MaximumBackupHorizon + AuthorityRetentionSafetyMargin`. Maintenance is exposed
only through the internal `IPrivacyErasureAuthorityMaintenance` boundary; there
is no public HTTP endpoint and operators must never run direct table DML.

- `EvaluateRetentionAsync` is the non-mutating dry run. It reports the expired
  contiguous prefix, held count, current floor, and projected floor.
- `CompactExpiredIntentsAsync` performs deletion, hold pseudonymization, and
  floor movement atomically under the provider's writer lock/transaction.
- The caller must explicitly supply the complete PII-free set of held authority
  sequences, including an explicit empty set when policy confirms no holds.
- An expired held row keeps its sequence and timestamps but replaces live
  intent/subject identifiers with random audit tokens. The floor may advance to
  that row, never past it in the same pass. After the hold is released, a later
  pass deletes it without moving the floor backward.
- Once compaction has run, the retained-authority lifecycle migration is
  forward-only. Recovery uses a matched primary and independently protected
  authority backup; an EF `Down` migration must not discard the floor or later
  erasure evidence.

Startup and readiness compare the primary checkpoint with this state. A
checkpoint below the floor fails closed as
`stale_restore_below_retained_floor`; a checkpoint above the high-water mark
fails as `checkpoint_ahead_of_authority`; a missing next sequence fails as
`sequence_gap_detected`. These bounded codes never include subject identifiers,
connection data, or raw exceptions.

## 7. Configuration Reference

Set the following environment variables in `.env` (or in Infisical under `/database/erasure`):

```dotenv
# Topology Selection: EmbeddedSqlite (default) | CoLocated | ExternalDatabase
ERASURE_DATABASE_TOPOLOGY=EmbeddedSqlite

ERASURE_EMBEDDED_PATH=/app/data/privacy_erasure_authority.db
ERASURE_WRITER_REPLICA_COUNT=1
ERASURE_BUSY_TIMEOUT_SECONDS=30

# CoLocated reuses primary application credentials. External-only values are required
# ONLY when ERASURE_DATABASE_TOPOLOGY=ExternalDatabase.
ERASURE_DATABASE_HOST=privacy-erasure-db
ERASURE_DATABASE_PORT=5432
ERASURE_DATABASE_NAME=privacy_erasure
ERASURE_DATABASE_TLS_MODE=Required
ERASURE_DATABASE_TRUST_SERVER_CERTIFICATE=false
ERASURE_DATABASE_RUNTIME_USERNAME=erasure_app
ERASURE_DATABASE_RUNTIME_PASSWORD=...
ERASURE_DATABASE_MIGRATOR_USERNAME=erasure_admin
ERASURE_DATABASE_MIGRATOR_PASSWORD=...
```

The embedded path must be absolute and local; URI/network paths are rejected,
busy timeout must be `1..300`, and writer count must equal one. Mount it on a
dedicated volume with restrictive permissions and back it up separately.
Compose maps these aliases to `PrivacyErasureAuthorityEmbedded:Path`,
`WriterReplicaCount`, and `BusyTimeoutSeconds`; direct .NET environment
configuration uses double underscores for those section separators.
Raw authority connection strings and `PrivacyErasure:Durability:Mode` are
unsupported and block startup.

---

## 8. Participant Admission Readiness Erasure

Participant readiness stores no answer values, names, contact details, addresses, or consent text. It retains only tenant/order/assignment/participant identifiers, a linked subject identifier, canonical consent-record reference, bounded timestamps, and approval/revocation actor identifiers.

During local user erasure, `UserLocationPrivacyErasureRepository` clears `SubjectUserId`, completion time, consent reference, and consent-grant time before unlinking the registration participant. This invalidates readiness immediately and prevents the restrictive subject foreign key from blocking erasure. A restored or raced worker cannot recreate active admission from the old projection because subject ownership and completion must be re-established from fresh canonical evidence.

Approval and revocation facts remain PII-free operational audit state. Typed registration answers, participant PII, and file evidence continue through their existing subject-owned erasure paths; readiness never copies their payloads.

The readiness API and UI do not expose a roster. A caller must name one exact event/order/participant/assignment tuple and prove subject, purchaser, organizer, or opaque capability authority. The response omits names, contact data, answers, consent text, actor identifiers, tenant identifiers, and capability material. Browser-visible status and support copy comes from closed vocabularies; scanner/support guidance never identifies which private requirement, payment fact, consent record, or approval is missing.

## 9. Ticket Transfer Privacy And Erasure

Ticket transfer records are deliberately payload-minimal. They retain tenant/event/ticket/order/line/assignment lineage, source and recipient participant references, recipient subject reference, bounded status and timestamps, hop/generation counters, and a one-way claim-capability digest. They do not copy names, email addresses, phone numbers, postal addresses, registration answers, consent text, payment instruments, provider payloads, or admission credential plaintext.

Holder changes update the existing subject-reference authority and participant-readiness linkage under the same transaction fence. Commerce and append-only check-in evidence remain attached to their original lawful records rather than being copied to the recipient. The browser and API publish only closed transfer/support codes and HAL actions; generic unavailable outcomes do not reveal whether a participant, ticket, transfer, account, or capability exists.

User erasure follows the existing subject-reference cleanup workflow. Removing a linked user cannot resurrect a consumed claim or old credential because transfer acceptance consumes the digest and rotates the ticket generation atomically. Historical non-PII operational lineage may remain under the platform retention policy, while linked participant and subject-owned PII continue through their canonical erasure paths.

One-time claim and credential plaintext are never persisted and therefore require no database erasure path. They are displayed only in the immediate browser response, are excluded from URLs and diagnostics, and disappear with component/session state. Operators cannot recover either plaintext value from transfer status, outbox, telemetry, or administrative surfaces.

## 10. Related Documentation

- [Backup, Restore, and Upgrade Runbook](BACKUP_RESTORE_UPGRADE.md)
- [Self-Hosting Guide](SELF_HOSTING.md)
- [Configuration Reference](CONFIGURATION.md)
- [Secrets Management](SECRETS.md)
- [Security Model](SECURITY-MODEL.md)
- [Operations Guide](OPERATIONS.md)
