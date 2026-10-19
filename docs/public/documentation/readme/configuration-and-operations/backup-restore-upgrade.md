---
description: Operator runbook for automated backups, disaster recovery restores, and safe upgrades.
---
<!-- ABOUTME: Operator recovery procedures for the actual Standalone and Split durable stores. -->
<!-- ABOUTME: Preserves independent erasure authority, database/Redis keys and consistent backup boundaries. -->

# Backup, Restore & Upgrade Runbook

A backup is only as good as its last verified restore. Capture coordinated
application, identity, key and media state, but keep newer privacy-erasure facts
independent of an older application recovery point. A persistent volume or a
successful file copy alone is not evidence of crash safety or recovery.

---

## PostgreSQL login identities during upgrade

Preserve the exact declared Migrator, Global and Runtime login names when
restoring or installing native database contracts. Supported punctuation in a
login name remains identity data. Do not strip punctuation or rename these
logins to accommodate SQL generation. This includes bootstrap installation as
well as navigation and tenant-preference contracts; dollar tags, apostrophes and
backslashes must preserve the original login identity.

Deploy matching native definitions with the application through the controlled
migrator before reopening traffic. Runtime rejects stale or changed definitions
rather than repairing them or selecting another database authority.

## Interrupted PostgreSQL lock reacquisition

If logs report `named_lock_acquisition_cleanup_unconfirmed`, the API could not
confirm a repeated lock acquisition on an already-held key. It deliberately keeps
the original session and earlier locks intact so another writer cannot enter
while the original operation is still settling. That acquisition failed; no new
lock lease or successful configuration change is implied.

A possibly retained extra lock count can block later work. Let the owning
operation unwind and release its earlier leases, then recycle that database
session through normal application lifecycle handling. Do not repeatedly retry
on the same session or forcibly close a connection while its earlier operation is
still running. A pooled connection being logically closed does not itself prove
that the backend ended or its lock state was already reset. No new database
permissions or deployment settings are required.

## 1. What Must Be Backed Up

Inventory the stores your selected topology actually uses:

For media, inventory all captured storage targets, including old roots/buckets
still used by existing files or pending cleanup. The current default alone is
not a complete backup inventory. Preserve the database's upload sessions,
producer records and cleanup records with their target metadata and required
credential references. Restore bound local files at the captured absolute mount
path; selecting a new root does not relocate them.

Before a storage-target schema upgrade, identify unbound development objects.
Accept historical mappings only with verified original-target and byte evidence,
or re-upload from a trusted source. Recreating a disposable environment requires
explicit approval. Keep unresolved data and pending producer records intact;
neither timeouts nor missing current bytes justify guessing a target or declaring
cleanup complete. See [storage target recovery](../integrations-and-ai/storage.md#existing-files-keep-their-original-target).

| Asset | Storage Location | Why It Matters |
|---|---|---|
| Primary database | Split `postgres_data`; Standalone `/app/data/islamu_event.db` | Application state, persisted email settings, outbox and colocated Local Identity; include migration histories and operational schemas. Historical DP tables are not runtime key authority. |
| External Local Identity, if selected | Configured independent Identity database | Credentials and credential-operation state, coordinated with application identity mappings |
| Privacy-erasure authority | Default `/app/data/privacy_erasure_authority.db`; Split volume `privacy_erasure_authority_data` | Retained erasure facts and replay bounds, preserved independently of primary rollback |
| Keycloak, when used | `keycloak-db`, volume `keycloak_data` | Accounts, realms, clients and credentials; use the configured database name, not an assumed one |
| External Data Protection ring and admission | Selected authority's exact partition folders/variables | Every retained key and revocation required by protected values and restorable backups |
| Independent DP admission pin/floor | Separately protected operator deployment control | Preserve the latest hash/generation independently of application and candidate-authority rollback |
| Media | Split `local_storage_data`; Standalone configured path, `/app/data/storage` in the Standalone runbook; or selected S3 bucket | Uploads and attachments consistent with database metadata |
| Secret/configuration authority | Private `.env` or selected Infisical authority, configuration manifests and bindings | Local signing keys, external credentials, deployment configuration and key versions required by protected data |
| Release inventory | Source revision, image digests, migration state and enabled profiles | Identifies the software and schema compatible with the recovery point |

No database, Redis snapshot or filesystem key directory supplies runtime Data
Protection authority. Provision the selected external ring and separate complete
admission record. Keep the current independently protected admission hash and
minimum generation when restoring older data; restoring them with the candidate
would defeat rollback rejection. Expired retained keys still decrypt, revoked
keys stay revoked, and missing keys cannot be reconstructed. Durable destinations,
registration answers and report evidence can require keys indefinitely.
Preserve old database/Redis ring state and backups offline; cutover never deletes
them. Data Protection does not replace Local JWT signing keys. See the
[external ring runbook](secrets.md#external-data-protection-rings).

If setup is incomplete, protect its generated secret in Split `setup_data` or
Standalone `/app/data/setup-secret` as confidential bootstrap state. Optional
`mailpit_data` contains private captured messages, capped at 500; it is not needed
for zero-email core recovery or proof of external delivery.

> [!CAUTION]
> **Do not roll erasure authority back with the primary database.** With
> `EmbeddedSqlite` or `ExternalDatabase`, retain the newest verified authority
> independently and replay its later erasures against the restored application.
> If the current authority is intact, leave it intact. A matching historical
> authority snapshot can omit erasures recorded after that backup.

`CoLocated` authority is restored inside the primary database and has no
independent protection against stale-primary resurrection
(`restoreReplayProtection=false`). `EmbeddedSqlite` and `ExternalDatabase`
provide that capability only while their authority remains outside the primary
rollback. A whole-volume backup may capture both SQLite files, but restoring
that whole volume over the newest authority defeats the separation.

---

## 2. Backup Procedures

### PostgreSQL Deployments (Docker Compose Split Topology)

This is a maintenance-window capture for the shipped Split stack, not a claim of
an atomic live snapshot across services. Block incoming traffic and stop all
writers, including other replicas, external integrations and optional workers.
Keep the same deployed revision and Compose configuration throughout capture.

```bash
set -euo pipefail
umask 077

BACKUP_DIR="/var/backups/islamu-event/$(date +%Y-%m-%d_%H%M%S)"
mkdir -p "$BACKUP_DIR"/{redis,media,erasure}
docker compose stop
docker compose up -d postgres keycloak-db

# Use each database container's configured owner/name, without printing secrets.
docker compose exec -T postgres sh -c \
  'exec pg_dump --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" --format=custom' \
  > "$BACKUP_DIR/app_db.dump"
docker compose exec -T keycloak-db sh -c \
  'exec pg_dump --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" --format=custom' \
  > "$BACKUP_DIR/keycloak_db.dump"

# These source containers remain stopped. Copy complete persistence units.
docker compose cp redis:/data/. "$BACKUP_DIR/redis/"
docker compose cp islamu-event-api:/app/storage-data/local/. "$BACKUP_DIR/media/"
docker compose cp islamu-event-api:/app/data/. "$BACKUP_DIR/erasure/"
```

Require each command to succeed. Preserve all Redis persistence files, including
its append-only file set, not just an assumed `dump.rdb`. Do not read Docker's
internal `/var/lib/docker/volumes/...` paths or guess the project-name prefix.
Verify dumps by restoring them to clean isolated databases; listing an archive
or checking its checksum does not establish restorability.

For external Identity/erasure databases, use their own authorized backup roles
and provider-native consistent dumps. For S3, use the selected provider's
snapshot/versioning procedure while preserving database-to-object consistency.
Record checksums, capture time, release revision, schema namespace and aggregate
authority/checkpoint bounds without credentials or message/person data. Back up
the selected secret authority securely and retain required versions. Encrypt and
copy the resulting artifacts off-host, then use the startup checks below to
resume the unchanged deployment.

### SQLite Deployments (Docker Standalone Topology)

The Standalone image is chiseled; it does not supply a shell or `sqlite3` for
in-container backup commands. For a stopped-writer capture using the
[Standalone runbook's storage layout](../self-hosting/docker-standalone.md):

```bash
BACKUP_DIR="./backups/$(date +%Y-%m-%d_%H%M%S)"
umask 077
mkdir -p "$BACKUP_DIR"
docker stop islamu-event-standalone
docker cp islamu-event-standalone:/app/data/. "$BACKUP_DIR/"
```

Keep any required WAL/SHM companions with their database file. Include media if
you chose a path outside `/app/data`, and back up the secret authority separately.
Restart only after successful capture. Treat primary and authority artifacts as
distinct restore units even when captured in one directory. Online backups need
separately provisioned SQLite-aware tooling and a coordinated consistency plan;
two sequential database backups do not create one atomic cross-store snapshot.

---

## 3. Disaster Recovery & Restore Procedure

### Step 1: Isolate The Target And Preserve Authority

Keep application traffic and all writers stopped. Select a clean recovery target
and a compatible application revision; do not overwrite the only surviving
databases, volumes or authority artifacts. Retain the newest verified erasure
authority first. If authority evidence is missing or inconsistent, keep the
application offline rather than bypassing replay.

### Step 2: Restore Relational Databases

For PostgreSQL, create empty target databases and provision the original owner
and runtime roles/grants before restoring. Use the configured database names and
schema, not `postgres`/`islamu_event` assumptions. Once only the target database
containers are running and `BACKUP_DIR` points to the verified capture:

```bash
docker compose exec -T postgres sh -c \
  'exec pg_restore --exit-on-error --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' \
  < "$BACKUP_DIR/app_db.dump"
docker compose exec -T keycloak-db sh -c \
  'exec pg_restore --exit-on-error --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' \
  < "$BACKUP_DIR/keycloak_db.dump"
```

Restore external Identity with the corresponding application identity mappings.
For SQLite, restore the primary database and its required companions into clean
storage without overwriting the independently retained authority. Never combine
a main file with unrelated WAL/SHM files from another recovery point.

### Step 3: Restore Keys, Media And Secret Authority

Database and Redis restoration do not provision current Data Protection authority.
Restore the external partition's retained material and admission record without
lowering the independently protected current hash/generation floor. Recover forward
if a candidate is older; keep newer revocations. Restore any needed Redis persistence
into clean `redis_data` with Redis/UI stopped, and media or S3 state consistent with
the application snapshot. Use your volume/storage restore tool, not an invented
filesystem keyring path. Preserve the deployment's non-root
ownership and access permissions, including authority directories `0700` and
files `0600`.

Restore the selected secret authority's required signing/encryption keys and
bindings without reviving compromised or revoked credentials. Credentials and
sessions still undergo current authorization checks; do not promise cookie
survival solely because key material was restored.

### Recover Keycloak operation receipts

The primary application database stores reviewed Keycloak operation receipts;
the Keycloak database or provider backup stores the resources those receipts
describe. Restore them from the same coordinated recovery point whenever
possible. A primary-database-only restore can bring back an `Applying` or
`OutcomeUnknown` receipt while Keycloak is already newer than the application
backup.

Before reopening public traffic:

1. Keep normal users and automated deployment actions stopped. Start only the
   restored databases, Keycloak and the application services needed for
   restricted administrator access.
2. Open the Keycloak operator panel in setup or instance administration and
   review every unresolved `Applying` or `OutcomeUnknown` receipt.
3. Submit fresh Keycloak administrator credentials and choose **Reconcile**.
   Reconciliation performs read-only checks against the receipt's captured
   realm, client or mapper identity; it does not resend the original mutation.
4. If the result remains unknown, correct provider reachability and choose
   **Reconcile** again. Never choose **Apply** as a retry, delete the receipt,
   edit its digest, reset the Keycloak volume, or delete and re-import the
   realm.
5. Reopen traffic only after the receipts settle and a fresh inspection shows
   the expected realm, clients and mappers. Verify a real user sign-in and keep
   the immutable provider IDs from the restored state.

An operation receipt that is absent from the restored primary database must not
be reconstructed from Keycloak or treated as proof that a write is safe to
repeat. Take a new coordinated backup after recovery so the application
receipts and Keycloak resources share a verified recovery point.

### Step 4: Migrate, Verify Replay, Then Reopen Traffic

```bash
docker compose run --rm event-migrationservice
docker compose up -d postgres redis keycloak-db keycloak
docker compose up -d islamu-event-api
curl --fail http://localhost:7039/health
```

Require migration exit code 0 and inspect API startup/readiness before starting
`islamu-event-ui`. Standalone runs its migrations and replay inside its single
process; keep its reverse proxy closed until those gates and recovery checks
succeed.

Inspect `privacy-erasure` readiness, not just the HTTP status. Verify replay has
caught up, the checkpoint is within retained authority bounds, erased test
canaries remain absent, and required cache/provider cleanup has converged.
`stale_restore_below_retained_floor`, `checkpoint_ahead_of_authority` or
`sequence_gap_detected` requires a verified recovery artifact, not editing
checkpoints or deleting facts. Keep traffic closed if these checks fail.

Then start the UI, verify selected-provider sign-in, tenant routing, public event
reads and media access, and review ambiguous outbox/provider outcomes before any
replay. Inspect `external-data-protection` on every API/BFF/Combined replica:
require the intended generation and admission hash, then test real retained
payloads. One healthy replica does not prove inventory completeness or cluster
convergence. Check email intent separately:
persisted disabled email is intentional, while SMTP-only degradation can return
HTTP 200 with otherwise healthy core checks. Neither restoring configuration nor
starting Mailpit should silently enable delivery. Required authority, database
and security failures still fail closed.

---

## 4. Upgrade Runbook (Pre-1.0 Releases)

Because the project is pre-1.0 and in active development, breaking schema changes may occur between minor versions. Follow this strict procedure when updating your instance:

1. **Review Release Notes**: Check the latest release notes and `API_CHANGELOG.md` for breaking changes or new required environment variables.
2. **Take Verified Backups**: Capture all selected stores and preserve erasure-authority independence before changing software.
3. **Select A Compatible Revision**: Review migration compatibility. The shipped Compose application services are built from source; `pull` alone does not upgrade them:
   ```bash
   docker compose pull
   docker compose build event-migrationservice islamu-event-api islamu-event-ui
   ```
4. **Run Migrations First**:
   ```bash
   docker compose run --rm event-migrationservice
   ```
   Confirm that migrations complete with exit code `0`.
5. **Restart Application Services**:
   ```bash
   docker compose up -d --remove-orphans
   ```
6. **Verify Health**:
   ```bash
   curl --fail http://localhost:7039/alive
   curl --fail http://localhost:7039/health
   ```

Apply the same replay, key-store and user-visible checks as a restore before
reopening traffic. Do not treat an older image as a schema rollback. If an
upgrade is not explicitly image-only reversible, use a tested recovery plan that
retains newer erasure facts. Keep the prior software and verified artifacts until
acceptance completes; a successful startup is not itself a restore rehearsal.

### Custom-property limits and maintenance downgrades

Separate SQL Server and MySQL migrations align five custom-property pattern
columns with the API's existing 2048-character limit. PostgreSQL already has that
alignment. SQLite's separate provider-model metadata migration changes no stored
data: it aligns length annotations and existing webhook UTC-converter metadata.

Reversing the SQL Server/MySQL alignment restores the 1000-character limit.
MySQL/MariaDB first validate all affected columns and reject oversized values,
including trailing spaces and multibyte text, before running the original
migration operations. They do not silently shorten patterns or change SQL mode.
MariaDB enables constraint checking for the maintenance session and leaves it on;
no server-wide setting changes.

Stop the API and all workers before running the downgrade, whether through EF or
an exported script. Keep them stopped until migration and verification finish.
Temporary validation checks must be removed before the columns can change, so
direct database writes during maintenance can bypass this protection. The schema
changes are not an all-or-nothing rollback: a later error may leave earlier
changes applied, or a failed cleanup may leave a temporary validation constraint.
On failure, keep traffic stopped, inspect actual schema and migration history,
and resolve the reported problem before retrying. Do not disable checks, shorten
patterns, alter history, or delete a validation constraint just to force success.
If a lossless downgrade is unavailable, use a forward correction or restore a
verified application backup with its matching software.

### Email-optional development migration consolidation

The current application history uses the supported provider Init followed by one
email-optional integration migration. Seven earlier unapplied development stages
are consolidated; their intermediate rollback targets no longer exist. External
Local Identity, Data Protection and privacy-erasure histories stay independent.

Do not edit migration-history rows to reuse a database created with retired Init
or feature migration IDs. Select an explicitly disposable application target for
a rebuild, or use a tested backup with its matching software. Preserve independent
Identity/key stores and the newest erasure authority before any recovery action.

Reversing the integration migration removes the whole feature's application
state, including lifecycle receipts, delivery controls and retention deadlines;
it is not a safe way to undo one setting or recover a populated instance.
A retained Local bootstrap row can also prevent restoration of the old constraint.
Prefer forward correction or the coordinated restore procedure above.

For a retained database already on the supported Init, check for duplicate
nonnull normalized Local email identities before the uniqueness transition.
There is no automatic account deduplication or historical guest-deadline backfill.
Run the migration service twice and require exit code zero both times, then
perform the restore/runbook checks before reopening traffic.

---

## Location Search Upgrade and Runtime Changes

The Unicode location-search upgrade replaces the development application's old
encoded search fields. Existing pre-release application databases require recreation
from the matching release; an incremental upgrade of the retired history is unsupported.
Stop writers, take a matching backup or confirm that the exact application target is
disposable, and identify the separate Identity, Data Protection, and retained privacy
authority stores before resetting anything. Preserve those independent histories and
never remove a shared database volume as a reset shortcut. Run the migration service
twice and require successful completion both times before starting the application.

Search retains complete accepted names and addresses (up to 500 UTF-16 code units).
Normalization-equivalent accents match, while accents, Arabic marks, joiners, and emoji
details remain significant. Search treats percent signs, underscores, backslashes,
and brackets literally. It does not promise transliteration, accent-free search,
German full case folding, Turkish linguistic casing, or identical sorting between
database engines. Suggestions remain restricted to authorized tenant addresses.

When changing .NET, the operating system, ICU/NLS, or globalization settings, test the
release's Unicode corpus with the new profile while traffic is stopped. Normalized
stored text can change even when the schema revision does not. Coordinate a supported
current-key rebuild or disposable application reset before restarting all readers and
writers on the same profile; mixed old/new normalizer operation is unsupported.
Rollback needs matching binaries and a matching backup, not only a code revert.

## Related Guides & Next Steps

* **[Privacy Erasure & Anti-Resurrection](../security-and-identity/privacy-erasure.md)** — Understand why primary database restores must replay against the erasure authority.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Production deployment and container lifecycle commands.
* **[Docker Standalone Runbook](../self-hosting/docker-standalone.md)** — Single-container storage and stopped-writer SQLite capture.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Diagnose migration lock timeouts and database connection errors.

## Public Event Response Freshness After Restore

Run the matching release's migration authority before enabling PostgreSQL public
event traffic. Public detail reads also need the timestamp-only response clock
installed by that migration. It keeps responses bounded by upcoming session
changes, enabled federation sources and location disclosure transitions, including
sources omitted from a filtered result.

The ordinary Runtime login receives execution permission, not raw event, session,
federation or private-address table access. A missing or altered clock contract
fails closed; do not grant source-table access, elevate Runtime, or disable
freshness checks to restore service. Reinstallation rejects altered routine bodies,
permissions and row-security policies rather than silently repairing them. Use the
reviewed deployment recovery procedure with matching binaries and database authority.

## Ticketing Recovery Readiness After Restore

For releases integrating the recovery fleet observer, enabled `ticketing-recovery`
readiness must observe recovery state across every tenant through the separately
configured Global database authority. A successful ordinary Runtime database
connection is not sufficient evidence: its tenant restrictions can hide unresolved
recovery work.

Run the matching release's migration authority before enabling traffic. On
PostgreSQL, it enrolls Global for an aggregate-only observation capability;
Global receives no direct recovery-table read or mutation permission. Do not
grant broad table access or elevated database roles to make readiness pass.
Repair missing or drifted authority through the reviewed deployment procedure.
Portable deployments also require the separately configured Global scope.

Missing observation authority reports Unhealthy rather than an empty healthy
fleet. A Failed recovery checkpoint or sufficiently old due work is Unhealthy;
unresolved recovery, ambiguous, poison, dead-letter, or threshold backlog work
can be Degraded. Only fixed counts and an age are published, never ticket,
tenant, operation, manifest, or private failure details. Disabled controls report
an explicit disabled Healthy result and do not certify restore readiness.

Release assurance must demonstrate foreign-tenant Failed checkpoints and Unknown
effects through the registered health check. A schema that cannot persist Unknown
effects has an unresolved recovery compatibility problem; do not weaken its
constraints or treat a substitute status as a successful restore proof.
