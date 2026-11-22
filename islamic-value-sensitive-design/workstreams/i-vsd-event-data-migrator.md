# I-VSD: Event Data Migrator — Relational Data Pump & Resilient Commons Export

Last Updated: 2026-11-05 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject / Workstream: event-data-migrator
- Report kind: implementation-plan-review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-05
- Reviewed input: design EDM-20261105-1 on develop and working-tree artifacts.
- Decision authority: user requirement for zero-friction bidirectional database migration (SQLite ↔ PostgreSQL/MySQL/SQL Server), dual-topology packaging (dedicated Docker image for split deployments and in-process CLI inside `Event.Standalone`), and two-scope data export/import (`Full` disaster recovery and `PublicCommons` open catalog resilience).
- Binding: [plan](../../dev/active/event-data-migrator/event-data-migrator-plan.md), [tasks](../../dev/active/event-data-migrator/event-data-migrator-tasks.md), [context](../../dev/active/event-data-migrator/event-data-migrator-context.md).
- Plan SHA-256: e9b2503fcfaa244aa0dcd782adc25f245334b94e1a53fe23ef70bc23712d0891
- Tasks SHA-256: 29dc922c44f2eb7d8b6d56431f6297d879d25c5518ee6bcc3662ed5e5f3ba2b9

---

## Executive Summary

The `event-data-migrator` workstream introduces `Event.DataMigrator`, an enterprise-grade relational data pump, streaming migrator, and portable archive engine for ISLAMU Event. It resolves a foundational lifecycle challenge for community platforms: moving seamlessly between lightweight single-node deployments (\$4/month VPS with SQLite) and horizontally scaled split topologies (PostgreSQL, MySQL, SQL Server) without data loss, downtime surprises, or vendor lock-in.

From an Islamic Value-Sensitive Design (I-VSD) perspective, data persistence is not merely a technical mechanism—it is a sacred trust (*Amanah*), a privacy boundary (*Satr*), and a guarantee of community autonomy (*Hurriyyah*). 

1. **Amanah (Custodianship & Data Integrity)**: When a community entrusts its event history, registrations, and institutional records to an instance, the system must guarantee zero silent truncation, zero dropped foreign keys, and zero unverified writes. Migrations must employ transactional boundary guards, deterministic entity topological ordering, sequence synchronization, and pre/post-migration row checksums.
2. **Satr (Privacy & Concealment)**: For communities publishing periodic open data dumps to protect against instance disappearance or hostility, the `PublicCommons` export scope enforces a mathematically verified privacy fence. Personal identification information (PII), email addresses, credentials, payment tokens, attendance rosters, and private administrative metadata are systematically redacted at the entity mapper level before serialization.
3. **Hurriyyah (Freedom from Monopolistic Lock-In)**: Proprietary platforms trap communities by making data extraction technically prohibitive or lossy. Bidirectional streaming between any supported database engine and portable, open JSON Lines / tarball archives ensures that communities remain free agents, capable of migrating their platform to any hosting provider or database engine at any time.
4. **Adalah (Equitable Access)**: Small, low-resource mosques and voluntary associations must not be penalized for lacking DevOps expertise. Shipping the migration engine both as a standalone container (`ghcr.io/islamu-ngo/event-data-migrator`) and embedded within the single `Event.Standalone` image ensures that operators of minimal setups have access to the exact same enterprise data pump capabilities through simple CLI commands.

---

## Scope & Claim Boundary

This evaluation reviews the architecture, data structures, and operational boundaries of `Event.DataMigrator`. It covers:
- Direct cross-database streaming migration between SQLite, PostgreSQL, MySQL/MariaDB, and SQL Server.
- Bulk transfer mechanisms, foreign key constraint suppression, and identity sequence resets.
- Archive serialization (`.tar.gz` containing `.jsonl` streams and signed `manifest.json`).
- Two-tier export scoping: `Full` (system disaster recovery) and `PublicCommons` (open community catalog).
- Dual packaging in `Event.DataMigrator` standalone image and `Event.Standalone` unified binary.

**Claim Boundary**: This report evaluates ethical design, privacy guarantees, and architectural resilience. It does not replace empirical runtime qualification or cryptographic certification of encryption keys at rest.

---

## Value-Sensitive Invariants & Findings

### IVSD-F001 — Amanah: Zero Silent Data Loss Across Heterogeneous Relational Models

- **Lifecycle**: Accepted / Enforced by Design.
- **Severity**: Critical.
- **Principle**: *Amanah* (Trust and Faithful Custodianship).
- **Context**: SQLite stores UUIDs as text or 16-byte blobs, booleans as integers (`0`/`1`), and datetimes as ISO-8601 text without strict timezone offsets, whereas PostgreSQL enforces native `uuid`, native `boolean`, and `timestamp with time zone`. SQL Server and MySQL have differing identifier limits, casing conventions, and collation rules.
- **Risk**: Naive bulk row copying (e.g., raw SQL inserts or un-mapped string casting) can result in timezone shifting, boolean inversion, GUID byte-order mangling, or silent truncation of Unicode characters.
- **Architectural Mitigation (IVSD-M001)**:
  - The migration engine must leverage Entity Framework Core's compiled entity models and CLR type converters via `ExploreDbContext` rather than raw database-to-database table mirroring.
  - Entities are read as typed domain entities via `AsNoTracking()` from the source `DbContext` and attached/added to the target `DbContext`.
  - The target `DbContext` enforces `RelationalModelNamespace` (translating between `ie_*` prefixed SQLite/MySQL tables and `islamu_event` schema-qualified PostgreSQL/SQL Server tables) and standard property value converters automatically.
  - Post-migration verification must validate exact table row counts and aggregate entity ID checksums prior to declaring the migration successful.

### IVSD-F002 — Satr: Privacy Protection in PublicCommons Data Exports

- **Lifecycle**: Accepted / Enforced by Design.
- **Severity**: Critical.
- **Principle**: *Satr* (Privacy, Concealment of Faults, and Protection of the Vulnerable).
- **Context**: ISLAMU Event empowers communities to publish automated periodic exports of their public commons (event catalogs, lecture series, schedules, venue accessibility details, organizer profiles) so that community knowledge survives server outages or organizational changes.
- **Risk**: An export could inadvertently include attendee email addresses, attendee registration lists, payment references, hashed passwords, IP addresses, private moderator notes, or GDPR deletion tombstones, causing severe privacy violations and harassment.
- **Architectural Mitigation (IVSD-M002)**:
  - The exporter must define a strict compile-time white-list for the `PublicCommons` scope.
  - Only explicitly designated public aggregate roots and value objects (`Event`, `Schedule`, `Location`, `Organizer`, `Category`, `Tag`, `PublicExperience`) are admitted into the export stream.
  - Sensitive entities (`User`, `IdentityUser`, `UserCredential`, `PaymentTransaction`, `TicketReservation`, `AuditLog`, `PrivacyErasureRecord`, `OutboxMessage`) are strictly forbidden and barred at the type level.
  - String sanitizers strip internal system URLs, private tenant identifiers, and contact details not explicitly designated for public display.
  - Automated Ring 1 invariant-breaker tests must scan every serialized JSON line of a `PublicCommons` archive with regex patterns for emails, UUIDv7 identities, hash patterns, and payment tokens, failing the build if any forbidden field is detected.

### IVSD-F003 — Hurriyyah: Unconditional Freedom from Platform and Provider Lock-In

- **Lifecycle**: Accepted / Enforced by Design.
- **Severity**: High.
- **Principle**: *Hurriyyah* (Freedom, Self-Determination, and Anti-Monopoly).
- **Context**: Cloud providers and proprietary SaaS platforms often establish asymmetrical barriers: data import is effortless, but data export is crippled, expensive, or lossy, effectively holding community history hostage.
- **Risk**: A community wishing to migrate from a commercial hosting provider to a local self-hosted server—or wishing to downscale from an expensive PostgreSQL cluster to a minimal SQLite VPS—finds itself trapped due to lack of reverse migration tooling.
- **Architectural Mitigation (IVSD-M003)**:
  - Full bidirectional symmetry: SQLite → PostgreSQL, PostgreSQL → SQLite, MySQL → PostgreSQL, SQL Server → SQLite, and all other combinations are first-class, equally supported, and tested continuously.
  - The archive format is standard, unencumbered JSON Lines (`.jsonl`) packaged in standard tarballs (`.tar.gz`) or ZIP archives with an open schema `manifest.json`.
  - The archive can be unpacked and inspected with standard Unix tools (`tar`, `jq`, `zcat`) without requiring proprietary binaries.

### IVSD-F004 — Adalah: Operational Parity for Low-Resource Environments

- **Lifecycle**: Accepted / Enforced by Design.
- **Severity**: High.
- **Principle**: *Adalah* (Justice, Equity, and Removal of Unfair Hardship).
- **Context**: Organizations with dedicated DevOps engineers can orchestrate multi-container Kubernetes jobs, while volunteer-led local groups rely on simple Docker or Coolify/Dokploy setups on a single virtual server.
- **Risk**: Requiring a separate, multi-gigabyte Docker image or complex orchestration for data migration penalizes resource-constrained communities and discourages them from upgrading or backing up their data.
- **Architectural Mitigation (IVSD-M004)**:
  - Single-binary distribution: `Event.DataMigrator` logic is compiled into both its own dedicated container image (`ghcr.io/islamu-ngo/event-data-migrator`) AND bundled directly inside `Event.Standalone`.
  - An operator running `Event.Standalone` can invoke `docker run --rm -v ... ghcr.io/islamu-ngo/event-standalone:latest data-migrator ...` using their existing downloaded image without pulling extra software.
  - Streaming memory consumption is strictly bounded using chunked pagination (batch size: 500 items, configurable via `--batch-size`), ensuring the migrator operates reliably on servers with as little as 512 MB of RAM.

---

## Traceability to Implementation Phases

| Finding | Core Principle | Implementation Mitigation | Verification Plan |
|---|---|---|---|
| **IVSD-F001** (Data Integrity) | *Amanah* | Entity Framework Core typed streaming, topological dependency ordering, foreign key suppression, sequence reset, row count validation. | Phase 1 & 2: `EntityTopologicalSorterTests`, `CrossProviderDataPumpTests`. Sliced TUnit tests verifying zero dropped entities and preserved foreign key relationships. |
| **IVSD-F002** (Public Privacy) | *Satr* | Two-scope export architecture (`Full` vs `PublicCommons`), strict entity whitelist, automatic PII scrubber. | Phase 3: `PublicCommonsRedactionTests`. Invariant-breaker tests verifying zero PII, zero emails, zero credentials, zero payment details in commons archives. |
| **IVSD-F003** (Anti-Lock-In) | *Hurriyyah* | Fully bidirectional streaming matrix, open `.jsonl.tar.gz` export format with schema manifest. | Phase 3 & 5: Round-trip migration tests: SQLite → PostgreSQL → SQLite with byte-for-byte entity state preservation. |
| **IVSD-F004** (Operational Equity) | *Adalah* | Dual distribution: standalone CLI/Docker image + embedded entry point in `Event.Standalone`, low memory streaming footprint. | Phase 4: `StandaloneHostCliIntegrationTests`, verification of memory ceiling under 256MB during multi-thousand entity streaming. |

---

## Conclusion & Disposition

The `event-data-migrator` workstream fulfills the requirements of Islamic Value-Sensitive Design. It honors *Amanah* by treating community history as an inviolable trust, upholds *Satr* by strictly protecting individual privacy in open data initiatives, champions *Hurriyyah* by eliminating vendor lock-in, and realizes *Adalah* by democratizing enterprise data mobility for low-resource self-hosters.

Disposition: **Plan-Aligned**. Approved for execution planning.
