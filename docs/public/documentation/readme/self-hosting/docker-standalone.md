---
description: Deploy and operate the single-container standalone distribution with durable SQLite storage.
---
<!-- ABOUTME: Operator runbook for the single-process ISLAMU Event distribution. -->
<!-- ABOUTME: Covers persistent storage, Local Identity defaults, first-run setup, proxying, and backup. -->

# Docker Standalone Self-Hosting

The standalone image (`Event.Standalone`) is the simplest and lowest-overhead operational topology for ISLAMU Event. It packages the API, background workers, Blazor WebAssembly BFF/UI, health endpoints, and in-process database migrations into **a single non-root container process**.

---

## 1. When to Choose Standalone

| Advantage | Consideration |
|---|---|
| **Zero External Infrastructure**: Runs on built-in SQLite persistence; no PostgreSQL server or Redis required. | **Single Replica**: SQLite requires exactly one running container instance (no horizontal multi-container scaling). |
| **Single-Process Footprint**: Runs API, BFF/UI, and SQLite in one container without auxiliary database servers. | **Local-First Storage**: Media and database files live in a mounted Docker volume. |
| **Instant Onboarding**: In-process migrations apply automatically before the HTTP port opens. | **Initial Platform Target**: Built for `linux/amd64`. |

SQLite coordinates transaction-owned work inside the application process.
Failed operations release that coordination when their transaction and connection
are cleaned up, so later checkout or configuration requests are not left waiting
on an abandoned lock. This does not change the one-replica requirement.

---

## 2. Quick Run & Production Deployment

### Step 1: Create a Persistent Volume

ISLAMU Event Standalone requires persistent storage mounted at `/app/data` to retain the primary database, privacy-erasure authority, Data Protection keys, and uploaded media:

```bash
docker volume create event_standalone_data
```

### Step 2: Prepare Configuration (`.env`)

Create a minimal `.env` file containing your production settings:

> [!TIP]
> **Authentication recommendation:** Keep the default embedded Local Identity
> for the easiest standalone deployment and for localhost/private-network use.
> Choose AT Protocol second when the instance has a public HTTPS domain and you
> want Bluesky/AT Protocol accounts to handle password authentication instead
> of storing user passwords yourself. AT Protocol is not the default because
> its OAuth callback cannot operate on localhost. Choose Keycloak for
> professional or SaaS operations that need advanced SSO/federation, 2FA/MFA,
> and centralized identity administration.

```env
ASPNETCORE_ENVIRONMENT=Production
DATABASE_PROVIDER=sqlite

# Base Application URLs
PUBLIC_URL=https://events.example.org

# Authentication (Local Identity; the standalone default)
# See: ../security-and-identity/authentication.md
AUTHENTICATION_PROVIDER=local
AUTHENTICATION_LOCAL_JWT_KEY=replace-with-output-from-openssl-rand-base64-64
IDENTITY_DATABASE_TOPOLOGY=colocated

# Operator Legal Identity (Required for Production startup)
# See: ../configuration-and-operations/environment-variables.md#9-operator-legal-identity-production-gate
INSTANCE__OPERATORIDENTITY__OPERATORID=01912a7e-1234-7000-8000-000000000001
INSTANCE__OPERATORIDENTITY__PUBLICNAME=Community Events Foundation
INSTANCE__OPERATORIDENTITY__LEGALNAME=Community Events Foundation Non-Profit
INSTANCE__OPERATORIDENTITY__OFFICIALINSTANCE=false
INSTANCE__OPERATORIDENTITY__OPERATORKIND=community
INSTANCE__OPERATORIDENTITY__JURISDICTION=US-CA
INSTANCE__OPERATORIDENTITY__PUBLICCONTACTEMAIL=contact@example.org
INSTANCE__OPERATORIDENTITY__WEBSITEURL=https://example.org
INSTANCE__OPERATORIDENTITY__LEGALNOTICEURL=https://example.org/legal
INSTANCE__OPERATORIDENTITY__TERMSURL=https://example.org/terms
INSTANCE__OPERATORIDENTITY__PRIVACYURL=https://example.org/privacy
```

### Step 3: Run the Container

```bash
docker run -d \
  --name islamu-event-standalone \
  --restart unless-stopped \
  --env-file .env \
  --mount source=event_standalone_data,target=/app/data \
  -p 8080:8080 \
  ghcr.io/islamu-ngo/event-standalone:latest
```

*(Alternatively, build from source: `docker build -t islamu/event-standalone -f src/Event.Standalone/Dockerfile .`)*

---

## 3. Container Startup & File Layout

When the container launches:
1. It applies migrations and seeding for the primary SQLite database (`/app/data/islamu_event.db`).
2. It initializes the separate [GDPR Privacy-Erasure authority store](../security-and-identity/privacy-erasure.md) (`/app/data/privacy_erasure_authority.db`).
3. It initializes the ASP.NET Core Data Protection keyring (`/app/data/dataprotection-keys/`).
4. It starts the internal Kestrel web server and binds port `8080`.

Verify container startup logs:

```bash
docker logs -f islamu-event-standalone
```

---

## 4. First-Run Setup Wizard

Once the container is healthy:

1. Retrieve the single-use setup secret generated inside the persistent volume:
   ```bash
   docker cp islamu-event-standalone:/app/data/setup-secret ./setup-secret
   cat ./setup-secret
   # Permanently delete the local copy after use:
   rm -f ./setup-secret
   ```
2. Navigate to `http://localhost:8080/setup` (or `https://events.example.org/setup` behind your reverse proxy).
3. Paste the setup secret and finalize your instance details (see [Administration Guide](../administration-and-branding/admin-guide.md)).
4. Keep **Local Identity** selected unless you intentionally operate Keycloak.
   When the wizard opens sign-in, choose **Create an account** to establish the
   first local administrator credentials.
5. Once completed, the setup flow is permanently locked.

---

## 5. Reverse Proxy Configuration

In production, place the standalone container behind a TLS-terminating reverse proxy on port `8080`.

### Caddy Example
```caddy
events.example.org {
    reverse_proxy 127.0.0.1:8080
}
```

### Nginx Example
```nginx
server {
    listen 443 ssl http2;
    server_name events.example.org;

    ssl_certificate /etc/letsencrypt/live/events.example.org/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/events.example.org/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## 6. Backup and Recovery

To back up the standalone deployment, take an atomic snapshot of the SQLite database files and the volume (see [Backup, Restore & Upgrade Guide](../configuration-and-operations/backup-restore-upgrade.md)):

```bash
# Safely vacuum/backup SQLite files
docker exec islamu-event-standalone sqlite3 /app/data/islamu_event.db ".backup '/app/data/backup_app.db'"
docker exec islamu-event-standalone sqlite3 /app/data/privacy_erasure_authority.db ".backup '/app/data/backup_erasure.db'"

# Copy out of container to secure backup storage
docker cp islamu-event-standalone:/app/data/backup_app.db ./backup_app.db
docker cp islamu-event-standalone:/app/data/backup_erasure.db ./backup_erasure.db
```

> [!CAUTION]
> **Privacy-Erasure Isolation**: Always restore *both* `islamu_event.db` and `privacy_erasure_authority.db` together. Restoring an old primary application database without the erasure authority database can accidentally resurrect erased user data that was legally deleted under GDPR! (See [Privacy Erasure](../security-and-identity/privacy-erasure.md)).

---

## Related Guides & Next Steps

* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Complete the web onboarding wizard at `/setup`.
* **[Deployment Tiers & Sizing](deployment-tiers.md)** — Review capacity benchmarks and hardware sizing.
* **[Docker Compose Runbook](docker-compose.md)** — Scale up to split PostgreSQL and Keycloak containers when ready.
* **[Troubleshooting & Operational Health](../configuration-and-operations/troubleshooting-and-health.md)** — Fast solutions for setup secret retrieval, TLS issues, and container errors.
