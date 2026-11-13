---
description: Comprehensive bidirectional guide for scaling from Standalone to Split topology (and downscaling back), plus SQLite to PostgreSQL data migration on Coolify, Dokploy, and Docker Compose.
---

# Scaling & Migration: Standalone ↔ Split Topology & SQLite ↔ PostgreSQL

> **The Zero-Lock-In Guarantee:**  
> **Start with Standalone.** It is the easiest, lowest-overhead, and most cost-effective way to run ISLAMU Event. If your traffic spikes or you need high-availability clustering down the road, you can migrate to a Split Topology and PostgreSQL in under 15 minutes with **zero data loss**. If traffic normalizes and you want to reduce your cloud bill, you can seamlessly downscale back to Standalone on a $4/month VPS.

---

## 1. The "Start with Standalone" Philosophy

A common hesitation among self-hosters and system operators is the **"just-in-case" dilemma**:
> *"I only host a few events a month today, but what if we blow up and need to handle 50,000 attendees next year? Shouldn't I deploy a full multi-container cluster with PostgreSQL, Redis, Keycloak, and Cerbos on day one just in case?"*

Deploying a multi-container cluster "just in case" brings unnecessary complexity:
* Multi-container orchestration overhead (managing 4 to 8 services simultaneously).
* Higher memory footprints requiring costly cloud VPS tiers ($20–$60+/month).
* Complex networking, connection pooling, and multi-service health monitoring.

**ISLAMU Event is architecturally unified.** The standalone Docker image (`Event.Standalone`) runs the **exact same compiled application code, CQRS commands, business invariants, and Blazor WebAssembly UI** as the enterprise multi-container deployment. 

```mermaid
flowchart LR
    A["Standalone Tier (1 Container)<br/>1 vCPU / 2GB RAM (~$4/mo)<br/>SQLite Embedded Database"] <-->|"Bidirectional Migration<br/>Zero Data Loss"| B["Split Topology (BFF + API)<br/>4+ vCPUs / 8GB RAM<br/>PostgreSQL + Redis"]
```

Because both topologies share the identical domain model, switching between them is **fully supported, bidirectional, and lossless**.

---

## 2. Topology Comparison & Invariants

| Dimension | Standalone (`Event.Standalone`) | Split Deployment (`Explore.Blazor` + `Explore.API`) |
|---|---|---|
| **Process Model** | 1 unified process (API + BFF/UI co-located in memory) | 2 separate processes (BFF Edge + API Backend) |
| **Recommended Database** | SQLite (embedded file at `/app/data/islamu_event.db`) | PostgreSQL (dedicated server/container) |
| **Minimum Hardware** | **1 vCPU, 1.5 – 2 GB RAM** (Runs on budget $4/mo VPS) | **2–4 vCPUs, 4–8 GB RAM** |
| **Public Ingress** | Points directly to `Event.Standalone` on port `8080` | Points to `Explore.Blazor` on port `8080`; YARP proxies `/api/*` to API |
| **Public URLs / Routing** | Identical (`https://events.example.org/...`) | **Identical** — No frontend or client reconfiguration required |
| **Database Access** | Standalone process accesses database directly | Only `Explore.API` touches the database (`Explore.Blazor` has 0 DB dependencies) |
| **Scalability (Replicas)** | SQLite mode: strictly `1` replica | `Explore.Blazor`: Scale N replicas; `Explore.API`: Scale N replicas (PostgreSQL) |

---

## 3. Scale-Up Runbook: Standalone (SQLite) ➔ Split Topology (PostgreSQL)

Use this pathway when your organization grows, concurrent ticket sales surge, or you need multi-container high availability.

### High-Level Flow
1. **Stop Writes & Flush SQLite WAL**: Ensure the SQLite database is completely quiesced.
2. **Initialize PostgreSQL Schema**: Run `Event.MigrationService` to apply clean PostgreSQL migrations.
3. **Migrate Relational Data**: Stream records from SQLite into PostgreSQL (remapping `ie_*` tables to `islamu_event.*`).
4. **Reset Auto-Increment Sequences**: Ensure primary key sequences are synchronized.
5. **Cut Over Deployment**: Point ingress to `Explore.Blazor` and launch `Explore.API`.

---

### Step-by-Step Execution

#### Step 1: Quiesce Traffic & Snapshot SQLite
Stop the standalone container to prevent in-flight writes, and flush WAL pages:
```bash
# Gracefully stop the standalone instance
docker stop islamu-event-standalone

# Checkpoint WAL and create a clean, consistent snapshot
sqlite3 ./data/islamu_event.db "PRAGMA wal_checkpoint(TRUNCATE);"
sqlite3 ./data/islamu_event.db ".backup './data/clean_migration_backup.db'"
```

#### Step 2: Initialize Target PostgreSQL Schema
Before migrating rows, let the official migrator create the exact schema, constraints, and indexes in PostgreSQL:
```bash
docker run --rm \
  --network host \
  -e Database__Provider=PostgreSql \
  -e Database__Host=localhost \
  -e Database__Port=5432 \
  -e Database__Name=islamu_event_db \
  -e Database__Schema=islamu_event \
  -e Database__Migrator__Username=postgres \
  -e Database__Migrator__Password=YourSecurePassword \
  ghcr.io/islamu/event-migrationservice:latest
```

#### Step 3: Stream Data from SQLite to PostgreSQL

> [!IMPORTANT]
> **Understanding the Schema Remapping Invariant:**
> * In **SQLite**, tables are prefixed with `ie_` in the default schema (e.g., `ie_users`, `ie_events`, `ie_orders`).
> * In **PostgreSQL**, tables live in the `islamu_event` schema without the `ie_` prefix (e.g., `islamu_event.users`, `islamu_event.events`).
>
> Your migration tool must strip the `ie_` prefix and target the `islamu_event` schema.

**Option A: Using `pgloader` (Fastest for large databases)**
Create a file named `migrate.load`:
```load
LOAD DATABASE
     FROM sqlite:///data/clean_migration_backup.db
     INTO postgresql://postgres:YourSecurePassword@localhost:5432/islamu_event_db

WITH data only,
     reset sequences,
     truncate,
     prefetch rows = 5000,
     batch rows = 1000

SET search_path to 'islamu_event'

-- Remap SQLite table names (strip 'ie_' and map into 'islamu_event' schema)
ALTER TABLE NAMES MATCHING ~/ie_(.*)/
  SET SCHEMA 'islamu_event',
  RENAME TO \1

CAST
     type integer to boolean drop default using zero-is-false,
     type text to uuid using text-to-uuid;
```

Run pgloader:
```bash
pgloader migrate.load
```

**Option B: Using Native SQLite SQL Dump + Import**
If pgloader is not installed, generate an INSERT dump from SQLite:
```bash
# Dump data only (skipping schema DDL and SQLite internal tables)
sqlite3 ./data/clean_migration_backup.db <<EOF > /tmp/sqlite_dump.sql
.mode insert
.output /tmp/data_only.sql
SELECT * FROM ie_users;
SELECT * FROM ie_events;
-- repeat or dump all tables
.exit
EOF

# Sed remapping: replace 'INSERT INTO "ie_' with 'INSERT INTO "islamu_event"."'
sed -i 's/INSERT INTO "ie_/INSERT INTO "islamu_event"."/g' /tmp/data_only.sql

# Load into PostgreSQL
psql -h localhost -U postgres -d islamu_event_db -f /tmp/data_only.sql
```

#### Step 4: Synchronize PostgreSQL Sequences
Because records were inserted with explicit primary key IDs, update the PostgreSQL sequences so new registrations or events don't trigger duplicate key collisions:
```sql
DO $$
DECLARE
    rec RECORD;
BEGIN
    FOR rec IN 
        SELECT sequence_schema, sequence_name, table_schema, table_name, column_name
        FROM information_schema.columns 
        JOIN information_schema.sequences 
          ON column_default LIKE ('%' || sequence_name || '%')
        WHERE table_schema = 'islamu_event'
    LOOP
        EXECUTE format('SELECT setval(''%I.%I'', COALESCE((SELECT MAX(%I) FROM %I.%I), 1))',
            rec.sequence_schema, rec.sequence_name, rec.column_name, rec.table_schema, rec.table_name);
    END LOOP;
END $$;
```

#### Step 5: Launch Split Services
Start the `islamu-event-api` and `islamu-event-ui` services (see platform-specific tutorials below). Test user sign-in and verify your events.

---

## 4. Downscale Runbook: Split Topology (PostgreSQL) ➔ Standalone (SQLite)

Use this pathway when you want to **dramatically cut infrastructure costs**. For example, your major annual convention is over, and your community now runs 1-2 small local workshops a month. You can move from a $40/month cluster to a $4/month VPS (1 vCPU, 2 GB RAM) with **all historical events, attendees, and settings intact**.

### Step 1: Drain Writes on API
```bash
# Gracefully stop the split containers
docker stop islamu-event-ui islamu-event-api
```

### Step 2: Initialize Clean SQLite Database
Start a fresh standalone container briefly or run migrations to create the schema with `ie_*` tables:
```bash
# Create target data directory
mkdir -p ./standalone_data/storage

# Run standalone once to initialize clean SQLite schema
docker run --rm \
  -v $(pwd)/standalone_data:/app/data \
  -e Database__Provider=Sqlite \
  -e Database__Database=/app/data/islamu_event.db \
  ghcr.io/islamu/event-standalone:latest --dry-run-migrations-only || true
```

### Step 3: Stream Data from PostgreSQL to SQLite
Using `pg_dump` or standard SQL scripts:
```bash
# Export PostgreSQL tables with 'ie_' prefix for SQLite
psql -h localhost -U postgres -d islamu_event_db -c \
  "\copy (SELECT * FROM islamu_event.users) TO '/tmp/users.csv' WITH CSV HEADER;"

# Import CSV into SQLite
sqlite3 ./standalone_data/islamu_event.db <<EOF
.mode csv
.import /tmp/users.csv ie_users
EOF
```
*(Alternatively, use `pgloader` with reverse mapping or the C# data pump script described in the technical reference).*

### Step 4: Transfer Media Uploads
Copy any local or S3 media assets into `./standalone_data/storage` so that image uploads, banners, and attachments continue to resolve locally.

### Step 5: Launch Standalone
```bash
docker run -d \
  --name islamu-event-standalone \
  --restart unless-stopped \
  -p 8080:8080 \
  -v $(pwd)/standalone_data:/app/data \
  --env-file .env \
  ghcr.io/islamu/event-standalone:latest
```
Your entire platform is now running inside a single container using under 300MB of RAM!

---

## 5. Platform-Specific Tutorials

### Tutorial A: Coolify Walkthrough

[Coolify](https://coolify.io) is the premier self-hosted PaaS. Here is how to handle scaling directly inside the Coolify UI:

#### Starting on Coolify with Standalone
1. In your Coolify Project, click **+ New** ➔ **Application** ➔ **Docker Image**.
2. **Image**: `ghcr.io/islamu/event-standalone:latest`.
3. **Ports Exposes**: `8080`.
4. Under **Storage / Persistent Directories**, add a mount:
   * **Source**: `standalone_data`
   * **Destination**: `/app/data`
5. Set your domain (e.g., `events.yourorg.com`) and deploy. Coolify automatically manages Traefik TLS termination!

#### Promoting to Split Topology in Coolify
1. In the same project, add a **PostgreSQL Database** resource. Note down the internal credentials.
2. Execute the data migration (Step 3 in Section 3) via the Coolify terminal or an ephemeral container.
3. Add two new Docker Image applications:
   * **API Application**: Image `ghcr.io/islamu/event-api:latest`. Set `Database__Provider=PostgreSql` and link PostgreSQL connection strings.
   * **UI Application**: Image `ghcr.io/islamu/event-ui:latest`. Set `API_ENDPOINT=http://<api-service-name>:8080/`.
4. Change your custom domain `events.yourorg.com` to point to the **UI Application**.
5. Stop the Standalone application. Your upgrade is complete!

---

### Tutorial B: Dokploy Walkthrough

[Dokploy](https://dokploy.com) provides an intuitive, lightweight Docker management interface.

#### Starting on Dokploy with Standalone
1. Create a new **Application** ➔ select **Docker**.
2. **Docker Image**: `ghcr.io/islamu/event-standalone:latest`.
3. Add a volume mapping: Volume Name `standalone_data` ➔ `/app/data`.
4. Configure domain `events.yourorg.com` on Port `8080`.
5. Deploy.

#### Promoting to Split Topology in Dokploy
1. Under Dokploy **Databases**, create a new **PostgreSQL** instance.
2. Create a new **Compose** deployment in Dokploy using the official ISLAMU Event `docker-compose.yml`.
3. Run the migration script against the new PostgreSQL container.
4. Move your domain mapping from the standalone container to the `islamu-event-ui` service.
5. Stop the standalone container.

---

### Tutorial C: Docker Compose (Plain VPS)

#### Starting with Standalone (`docker-compose.standalone.yml`)
```yaml
version: '3.8'

services:
  islamu-event:
    image: ghcr.io/islamu/event-standalone:latest
    container_name: islamu-event
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"
    environment:
      - Database__Provider=Sqlite
      - Database__Database=/app/data/islamu_event.db
      - PUBLIC_BASE_URL=https://events.example.org
    volumes:
      - event_data:/app/data

volumes:
  event_data:
```

#### Upgrading to Split Compose (`docker-compose.split.yml`)
When ready to scale, simply swap compose configurations:
```yaml
version: '3.8'

services:
  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: islamu_event_db
      POSTGRES_USER: event_user
      POSTGRES_PASSWORD: SecretPassword123
    volumes:
      - pgdata:/var/lib/postgresql/data

  api:
    image: ghcr.io/islamu/event-api:latest
    restart: unless-stopped
    depends_on:
      - postgres
    environment:
      - Database__Provider=PostgreSql
      - Database__Host=postgres
      - Database__Port=5432
      - Database__Name=islamu_event_db
      - Database__Runtime__Username=event_user
      - Database__Runtime__Password=SecretPassword123
      - PUBLIC_BASE_URL=https://events.example.org

  ui:
    image: ghcr.io/islamu/event-ui:latest
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"
    depends_on:
      - api
    environment:
      - API_ENDPOINT=http://api:8080/
      - PUBLIC_BASE_URL=https://events.example.org

volumes:
  pgdata:
```

---

## 6. Frequently Asked Questions (FAQ)

### Will my users lose their login sessions during migration?
Active browser cookie sessions will expire when transitioning between in-process Standalone Data Protection and external Split Data Protection rings. Users will be asked to sign in once with their existing credentials. No passwords, MFA seeds, or accounts are lost.

### Can I run the Split Topology but keep SQLite?
**Yes!** If you want to separate the UI and API containers (for instance, to place the UI on an edge node while keeping the API internal), you can keep SQLite. Simply mount the SQLite volume into `Explore.API` with `Hosting:ReplicaCount=1`. The `Explore.Blazor` UI container requires **no database connection at all**.

### What about Privacy Erasure data?
If you used `PrivacyErasureAuthority:Topology=EmbeddedSqlite` in Standalone, the erasure database is stored in `/app/data/privacy_erasure_authority.db`. You can either keep it as an embedded SQLite file in Split mode, or migrate it to PostgreSQL via `Event.MigrationService`.

---

## Related Documentation
* **[Docker Standalone Quickstart](docker-standalone.md)** — Deploy the standalone image in 60 seconds.
* **[Deployment Tiers & Sizing](deployment-tiers.md)** — Understand compute and memory requirements.
* **[Coolify with Cerbos & Traefik](coolify-cerbos-traefik.md)** — Production authorization hosting on Coolify.
* **[Backup, Restore & Upgrade](../configuration-and-operations/backup-restore-upgrade.md)** — Automated snapshotting and rollback strategies.
