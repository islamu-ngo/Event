---
description: Operator runbook for automated backups, disaster recovery restores, and safe upgrades.
---

# Backup, Restore & Upgrade Runbook

A backup is only as good as its last verified restore. In ISLAMU Event, database upgrades and data persistence involve multiple durable authorities that must remain synchronized to prevent data corruption or resurrection of erased user accounts.

---

## 1. What Must Be Backed Up

To guarantee a complete recovery point, your backup routine must capture all six durable authorities:

| Asset | Storage Location | Why It Matters |
|---|---|---|
| **Primary Application Database** | PostgreSQL DB or `/app/data/islamu_event.db` | Events, orders, registrations, tenants, settings, and outbox queues. |
| **Privacy Erasure Authority** | PostgreSQL schema or `/app/data/privacy_erasure_authority.db` | Typed immutable erasure records and monotonic counter driving GDPR compliance. |
| **Identity Provider (Keycloak)** | PostgreSQL database `keycloak` | User accounts, realms, client definitions, and password hashes. |
| **ASP.NET Data Protection Keys** | Named volume `data_protection_keys` | Encryption keyring preserving active browser sessions and auth cookies. |
| **Object Storage Media** | Local volume `local_storage_data` or S3 bucket | Event banners, attendee avatars, and uploaded attachments. |
| **Environment Configuration** | Root `.env` or Infisical vault | Secret keys, client secrets, and runtime parameters. |

> [!CAUTION]
> **Anti-Resurrection Rule**: Never restore an older backup of the Primary Application Database without also restoring the corresponding Privacy Erasure Authority store. Restoring an old database alone will resurrect deleted attendee profiles and personal records!

---

## 2. Backup Procedures

### PostgreSQL Deployments (Docker Compose Split Topology)

Automate this script via a daily cron job:

```bash
#!/usr/bin/env bash
set -euo pipefail

BACKUP_DIR="/var/backups/islamu-event/$(date +%Y-%m-%d_%H%M%S)"
mkdir -p "$BACKUP_DIR"

echo "Backing up Primary Application Database..."
docker compose exec -T postgres pg_dump -U postgres -d islamu_event -F c -b -v -f /tmp/app_db.dump
docker cp "$(docker compose ps -q postgres)":/tmp/app_db.dump "$BACKUP_DIR/app_db.dump"

echo "Backing up Keycloak Database..."
docker compose exec -T keycloak-db pg_dump -U postgres -d keycloak -F c -b -v -f /tmp/keycloak_db.dump
docker cp "$(docker compose ps -q keycloak-db)":/tmp/keycloak_db.dump "$BACKUP_DIR/keycloak_db.dump"

echo "Backing up Data Protection Keys and Local Media..."
tar -czf "$BACKUP_DIR/data_protection_keys.tar.gz" -C /var/lib/docker/volumes/event_data_protection_keys/_data .
tar -czf "$BACKUP_DIR/local_storage.tar.gz" -C /var/lib/docker/volumes/event_local_storage_data/_data .

echo "Backing up Environment Configuration..."
cp .env "$BACKUP_DIR/.env.backup"

echo "Backup completed successfully at $BACKUP_DIR"
```

### SQLite Deployments (Docker Standalone Topology)

For single-container SQLite deployments, use the safe `.backup` command:

```bash
BACKUP_DIR="./backups/$(date +%Y-%m-%d_%H%M%S)"
mkdir -p "$BACKUP_DIR"

# Perform online atomic backup inside container
docker exec islamu-event-standalone sqlite3 /app/data/islamu_event.db ".backup '/app/data/app_backup.db'"
docker exec islamu-event-standalone sqlite3 /app/data/privacy_erasure_authority.db ".backup '/app/data/erasure_backup.db'"

# Copy out to backup directory
docker cp islamu-event-standalone:/app/data/app_backup.db "$BACKUP_DIR/islamu_event.db"
docker cp islamu-event-standalone:/app/data/erasure_backup.db "$BACKUP_DIR/privacy_erasure_authority.db"
docker cp islamu-event-standalone:/app/data/dataprotection-keys "$BACKUP_DIR/dataprotection-keys"

# Clean up temporary files inside container
docker exec islamu-event-standalone rm -f /app/data/app_backup.db /app/data/erasure_backup.db
```

---

## 3. Disaster Recovery & Restore Procedure

### Step 1: Prepare Clean Target Environment
Stop running application containers to prevent active writes during restore:

```bash
docker compose down
```

### Step 2: Restore Relational Databases

```bash
# Start only the database containers
docker compose up -d postgres keycloak-db

# Restore primary application database
docker cp ./app_db.dump "$(docker compose ps -q postgres)":/tmp/app_db.dump
docker compose exec -T postgres dropdb -U postgres --if-exists islamu_event
docker compose exec -T postgres createdb -U postgres islamu_event
docker compose exec -T postgres pg_restore -U postgres -d islamu_event -v /tmp/app_db.dump

# Restore Keycloak database
docker cp ./keycloak_db.dump "$(docker compose ps -q keycloak-db)":/tmp/keycloak_db.dump
docker compose exec -T keycloak-db dropdb -U postgres --if-exists keycloak
docker compose exec -T keycloak-db createdb -U postgres keycloak
docker compose exec -T keycloak-db pg_restore -U postgres -d keycloak -v /tmp/keycloak_db.dump
```

### Step 3: Restore Data Protection Keys & Media

Extract the keys into their respective persistent volumes:

```bash
tar -xzf data_protection_keys.tar.gz -C /var/lib/docker/volumes/event_data_protection_keys/_data
tar -xzf local_storage.tar.gz -C /var/lib/docker/volumes/event_local_storage_data/_data
```

### Step 4: Run Migrations & Start Services

```bash
docker compose run --rm event-migrationservice
docker compose up -d
```

---

## 4. Upgrade Runbook (Pre-1.0 Releases)

Because the project is pre-1.0 and in active development, breaking schema changes may occur between minor versions. Follow this strict procedure when updating your instance:

1. **Review Release Notes**: Check the latest release notes and `API_CHANGELOG.md` for breaking changes or new required environment variables.
2. **Take a Full Backup**: Execute the backup script described above before pulling new images.
3. **Pull Pinned Images**: Update image tags in your `docker-compose.yml` (avoid using `latest` in production):
   ```bash
   docker compose pull
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
Canonically equivalent accents match, while accents, Arabic marks, joiners, and emoji
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
* **[Docker Standalone Runbook](../self-hosting/docker-standalone.md)** — Backup and vacuum mechanics for SQLite databases.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Diagnose migration lock timeouts and database connection errors.
