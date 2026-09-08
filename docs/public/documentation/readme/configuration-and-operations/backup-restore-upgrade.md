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

## 1. What Must Be Backed Up

Inventory the stores your selected topology actually uses:

| Asset | Storage Location | Why It Matters |
|---|---|---|
| Primary database | Split `postgres_data`; Standalone `/app/data/islamu_event.db` | Application state, persisted email settings, outbox, colocated Local Identity and API/Standalone Data Protection keys; include migration histories and operational schemas |
| External Local Identity, if selected | Configured independent Identity database | Credentials and credential-operation state, coordinated with application identity mappings |
| Privacy-erasure authority | Default `/app/data/privacy_erasure_authority.db`; Split volume `privacy_erasure_authority_data` | Retained erasure facts and replay bounds, preserved independently of primary rollback |
| Keycloak, when used | `keycloak-db`, volume `keycloak_data` | Accounts, realms, clients and credentials; use the configured database name, not an assumed one |
| Split UI Data Protection keys | Redis key `islamu-event:data-protection-keys`, persisted in `redis_data` | The separate UI's protected-cookie key store; it is not the API's database key store |
| Media | Split `local_storage_data`; Standalone configured path, `/app/data/storage` in the Standalone runbook; or selected S3 bucket | Uploads and attachments consistent with database metadata |
| Secret/configuration authority | Private `.env` or selected Infisical authority, configuration manifests and bindings | Local signing keys, external credentials, deployment configuration and key versions required by protected data |
| Release inventory | Source revision, image digests, migration state and enabled profiles | Identifies the software and schema compatible with the recovery point |

There is no `data_protection_keys` filesystem volume in shipped Compose and no
Standalone `/app/data/dataprotection-keys/` directory. Default Standalone keys are
in its primary database. Split uses database keys for the API and Redis keys for
the separate UI. Preserve the selected secret authority too; Data Protection
keys do not replace Local JWT signing keys. Key preservation alone does not
guarantee that every session remains valid after recovery.

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

Database restoration includes API/Standalone Data Protection keys. For shipped
Split, restore the complete captured Redis persistence unit into clean
`redis_data` while Redis and the UI are stopped. Restore local media or S3 state
consistent with the application snapshot. Use your volume/storage restore tool,
not an invented filesystem keyring path. Preserve the deployment's non-root
ownership and access permissions, including authority directories `0700` and
files `0600`.

Restore the selected secret authority's required signing/encryption keys and
bindings without reviving compromised or revoked credentials. Credentials and
sessions still undergo current authorization checks; do not promise cookie
survival solely because key material was restored.

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
replay. Inspect `data-protection-keys` on the UI: Redis reachability alone does not
prove that its former keyring was restored. Check email intent separately:
persisted disabled email is intentional, while SMTP-only degradation can return
HTTP 200 with otherwise healthy core checks. Neither restoring configuration nor
starting Mailpit should silently enable delivery. Required authority, database
and security failures still fail closed.

---

## 4. Upgrade Runbook (Pre-1.0 Releases)

Because the project is pre-1.0 and in active development, breaking schema changes may occur between minor versions. Follow this strict procedure when updating your instance:

1. **Review Release Notes**: Check the latest release notes and `API_CHANGELOG.md` for breaking changes or new required environment variables.
2. **Take Verified Backups**: Capture all selected stores and preserve erasure-authority independence before changing software.
3. **Select A Compatible Revision**: Pin deployment images and review migration compatibility. The shipped Compose application services are built from source; `pull` alone does not upgrade them:
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

---

## Related Guides & Next Steps

* **[Privacy Erasure & Anti-Resurrection](../security-and-identity/privacy-erasure.md)** — Understand why primary database restores must replay against the erasure authority.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Production deployment and container lifecycle commands.
* **[Docker Standalone Runbook](../self-hosting/docker-standalone.md)** — Single-container storage and stopped-writer SQLite capture.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Diagnose migration lock timeouts and database connection errors.
