---
description: Deploy and operate the production split service topology with Docker Compose.
---
<!-- ABOUTME: Operator runbook for the repository's Split Compose topology. -->
<!-- ABOUTME: Covers zero-email setup, actual service ports and storage, and optional private mail capture. -->

# Docker Compose Self-Hosting

Docker Compose separates the API, browser-facing BFF/UI and infrastructure into
distinct containers. The repository stack supports zero-email operation, but it
is not a production-hardening preset: review port exposure, credentials, Keycloak
startup mode and backup topology before opening it to users. Its embedded erasure
authority and API replica setting default to one writer; do not simply scale the
API without changing that topology.

---

## 1. Architecture & Service Topology

The repository `docker-compose.yml` declares these base services. Host ports below
are defaults; use the service names and container ports for container-network
connections.

| Service | Container port / default host access | Role |
|---|---|---|
| `islamu-event-ui` | `8080` / `http://localhost:7002` | Browser UI and BFF; built from the repository |
| `islamu-event-api` | `8080` / `http://localhost:7039` | API and background workers; built from the repository |
| `event-migrationservice` | No published port | One-shot database migration and seeding worker |
| `postgres` | `5432`, container network only | Primary application database |
| `redis` | `6379`, container network only | Cache and the UI's Data Protection key persistence |
| `keycloak` | `8080` / `http://localhost:8080` | External identity provider; shipped command uses `start-dev` |
| `keycloak-db` | `5432`, container network only | Keycloak database |
| `keycloak-init` | No published port | One-shot realm/client configuration |
| `privacy-erasure-authority-volume-init` | No published port | Sets dedicated authority-volume ownership/permissions |

Local is the curated authentication default, but the current Compose dependency
graph still starts Keycloak and its initialization services. Selecting Local does
not remove them. For a genuinely infrastructure-minimal deployment, use
[Standalone](docker-standalone.md).

### Optional Service Profiles

Additional capabilities can be enabled dynamically via Docker Compose profiles:

| Profile | Services Included | Purpose |
|---|---|---|
| `storage` | `minio`, `minio-init` | S3-compatible local object storage (see [Storage Guide](../integrations-and-ai/storage.md)). |
| `authz` | `cerbos` (`:3592`, gRPC `:3593`) | External Policy Decision Point (see [Authorization Guide](../security-and-identity/authorization.md)). |
| `webhooks` | `svix` (`:8071`) | Scalable outbound webhook delivery engine (see [Webhooks Guide](../integrations-and-ai/webhooks.md)). |
| `mail` | `mailpit` | Optional capture: inbox `127.0.0.1:8025`; SMTP `mailpit:1025` on the container network only |

Mailpit is not an API dependency. Base Compose supplies no RabbitMQ broker and
defaults `EMAIL_DISPATCH_RABBITMQ_ENABLED=false`. Other optional profiles are
listed in the Compose file and the environment reference.

---

## 2. Prerequisites & Preparation

1. **Host Requirements**:
   - OS: Linux (Ubuntu 22.04+ / Debian 12 recommended)
   - CPU: 2+ vCPUs
   - RAM: 4 GB minimum (8 GB recommended for full stack with Keycloak and Cerbos)
   - Disk: 20 GB SSD storage
   - Software: Docker Engine 24+ and Docker Compose v2.20+

2. **Clone and Prepare Environment**:

```bash
git clone https://github.com/islamu-ngo/Event.git
cd Event

# Copy the curated baseline, then supply real deployment values
cp .env.example .env
chmod 600 .env
```

3. **Generate Required Secrets**:

Select one secret authority (`SECRET_PROVIDER=Environment` or `Infisical`) and
provision the values it owns. For Environment, fill the empty database runtime and
migrator passwords, Local JWT signing key, and the Keycloak database, administrator
and client credentials used by the shipped dependencies. Use distinct generated
values; do not reuse one password across roles.

```bash
# Generate a Local JWT signing key
openssl rand -base64 64
# Generate an individual database, administrator or client secret
openssl rand -hex 32
```

Set your public URLs and complete the `INSTANCE__OPERATORIDENTITY__*` section.
Supply `INSTANCE__OPERATORIDENTITY__OFFICIALORIGIN` as an HTTPS origin even when
the instance is unofficial. Select a supported `OPERATORKINDCODE` matching the
operator's legal status, such as `unincorporated_association` or
`registered_organization`; `community` is not accepted.
Check database runtime/migrator role grants and align Keycloak realm/client values
with your imported realm. No SMTP configuration is required for Local setup.
Legal contact email is still required; it is not an account-verification channel.

`.env.example` intentionally omits advanced settings. Use the separate
[complete environment reference](../configuration-and-operations/environment-variables.md)
for those inputs. Do not treat environment or manifest bootstrap as an override
of persisted `email.delivery_enabled` or guarded SMTP settings.

4. **Verify Configuration**:

Validate the syntax of your rendered Compose configuration:

```bash
docker compose config --quiet
```

---

## 3. Database Migration & Startup Sequence

> [!IMPORTANT]
> Run `event-migrationservice` successfully before starting the web services.
> Compose declares the API's migration dependency with `required: false`; do not
> interpret a rendered configuration or a TCP health check as proof of applied
> schema.

### Step 1: Run Database Migrations

Apply the application and Data Protection schemas, privacy-erasure authority and
initial seeds:

```bash
docker compose run --build --rm event-migrationservice
```

Require exit code 0 and inspect any failure before starting the API. For upgrades,
take verified backups first; this command is not a rollback or restore guarantee.

### Step 2: Start the Stack

Start all core services in detached mode:

```bash
docker compose up -d --build
```

To add only the optional mail capture service later:

```bash
docker compose --profile mail up -d mailpit
```

This does not configure or enable application email. Follow the
[SMTP guide](../communications-and-notifications/email-smtp.md#3-optional-private-mailpit-capture)
for the separate administrator action.

### Step 3: Check Health & Readiness

Check service state and the application readiness response separately:

```bash
docker compose ps
curl --fail http://localhost:7039/alive
curl --fail http://localhost:7039/health
```

The web-container Compose checks test TCP reachability, not the full readiness
body. Disabled email is intentional and can be Healthy. Enabled but unconfigured
email or a reported SMTP failure is Degraded (HTTP 200 when no required check is
Unhealthy), not a blanket HTTP 503. Required database, security and authority
failures still fail readiness or startup. Completed one-shot helpers should have
exit code 0, not a perpetual running/healthy state.

---

## 4. First-Run Setup & Administrator Onboarding

Once containers are running, navigate to the web onboarding wizard or configure headless administrator bootstrapping:

### Option A: Interactive Setup Wizard
1. Access the web interface at `http://localhost:7002/setup`.
2. Retrieve the generated setup secret:
   ```bash
   umask 077
   docker compose cp islamu-event-api:/app/bootstrap/setup-secret ./setup-secret
   cat ./setup-secret
   ```
   Use your explicit `SETUP_SECRET` instead if one was supplied. Keep the value
   private; startup logs report its location, not its contents.
3. Validate it and choose **Continue Local setup**. Enter instance details, the
   initial administrator username and temporary password; credential email is
   optional. Do not look for public Local **Create an account** registration.
4. Complete mandatory private password replacement with that temporary
   credential, then sign in afresh. No SMTP verification message is required.
5. Completed setup is locked and the generated file is removed. Delete the host
   copy with `rm -f ./setup-secret`.

The setup profile's **Support email** is public site identity. It is persisted
separately from credential email, legal operator contact and SMTP sender policy;
changing it neither overwrites the SMTP From address nor enables delivery.

Subsequent Local creation/reset is available to current instance administrators at
`/settings/instance?section=local-accounts`, not to tenant administrators. Hand over
the one-time generated temporary password privately; the recipient must replace
it before ordinary sign-in. See the [Local accounts guide](../administration-and-branding/admin-guide.md#local-accounts)
for operation-status recovery without repeating password disclosure.

With an external provider selected, follow its sign-in and verification flow
after validating setup authority. Event's email setting does not change
Keycloak or AT Protocol verification requirements.

### Option B: Headless Automated Onboarding
Select `INSTANCE_BOOTSTRAP_MODE=ConfiguredAdministrator` and provide the exact
provider binding before startup. These settings prepare authority; they are not
a reusable backdoor into a completed instance.

| Input | Required value |
|---|---|
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | `local`, `keycloak` or `atproto`, matching your authentication selection |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | Local canonical UUIDv7 (also its username), exact Keycloak subject or canonical AT Protocol DID |
| `INSTANCE_BOOTSTRAP_BINDING_GENERATION` | Positive integer |
| `INSTANCE_BOOTSTRAP_ADMIN_EMAIL` | Required for external providers; optional for Local |
| `INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME`, `INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME` | Both or neither |
| `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` | Local only; temporary credential supplied through the selected secret authority |

Local bootstrap provisions without SMTP and still requires private first-use
replacement. External-provider bootstrap remains pending until the configured
identity signs in and matches its provider claims. Under `Interactive`, leave
these configured-administrator inputs unset. See the
[authentication guide](../security-and-identity/authentication.md) for provider
boundaries.

---

## 5. Reverse Proxy & TLS Configuration

In production, restrict the shipped API/UI port publications and terminate TLS
at a reverse proxy. A host proxy reaches the UI's published port `7002`; a proxy
on the Compose network reaches `islamu-event-ui:8080`. Configure trusted proxy
addresses as well as forwarding headers. Do not expose the inbox, Redis or
database services publicly. Replace Keycloak's evaluation `start-dev` posture
with your production identity-provider deployment.

### Recommended Port Exposure Map

| Service | Internal Container Port | Exposed to Public Internet? | Reverse Proxy Routing |
|---|---|---|---|
| `islamu-event-ui` (BFF) | `8080` | **Yes (via Reverse Proxy)** | `https://events.example.org` |
| `islamu-event-api` | `8080` | Only if deliberately published | Internal BFF backend; do not bypass BFF routing accidentally |
| `keycloak` | `8080` | **Yes (via Reverse Proxy)** | `https://auth.example.org` |
| `postgres` | `5432` | **NO (Isolated network)** | None |
| `cerbos` | `3592` / `3593` | **NO (Internal gRPC)** | None |

### Reverse Proxy Recipes

#### Caddy (Recommended for Auto-HTTPS)
```caddy
events.example.org {
    reverse_proxy islamu-event-ui:8080
}

auth.example.org {
    reverse_proxy keycloak:8080 {
        header_up X-Forwarded-Proto {scheme}
        header_up X-Forwarded-Host {host}
    }
}
```

#### Traefik
For a Traefik proxy on the same container network, add labels to
`islamu-event-ui` in your deployment override:
```yaml
services:
  islamu-event-ui:
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.event-ui.rule=Host(`events.example.org`)"
      - "traefik.http.routers.event-ui.entrypoints=websecure"
      - "traefik.http.routers.event-ui.tls.certresolver=letsencrypt"
      - "traefik.http.services.event-ui.loadbalancer.server.port=8080"
```

#### Nginx
```nginx
server {
    listen 443 ssl http2;
    server_name events.example.org;

    ssl_certificate /etc/letsencrypt/live/events.example.org/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/events.example.org/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:7002;
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

## 6. Volume Persistence & Backup Safeguards

Preserve the actual named volumes and selected secret authority:

| Volume | Contents |
|---|---|
| `postgres_data` | Primary database, colocated Local Identity and API Data Protection keys |
| `keycloak_data` | Keycloak database |
| `redis_data` | Redis persistence, including the separate UI's Data Protection keys |
| `local_storage_data` | Local uploads |
| `privacy_erasure_authority_data` | Default independent SQLite erasure authority |
| `setup_data` | Generated setup secret while interactive setup is active |
| `mailpit_data` | Optional captured mail; private and capped at 500 messages |

There is no `data_protection_keys` filesystem volume in this Compose file. The
API uses database-backed keys; the separate UI uses Redis key
`islamu-event:data-protection-keys`. Keep signing keys in their selected secret
authority too. External Identity or erasure topologies introduce their own backup
units.

Use coordinated, consistent database/Redis/media backups, not a copy of live
database files with unknown WAL state. Keep erasure evidence independently of
primary rollback; do not roll newer authority facts back with an older primary
backup. Preserve ownership and access restrictions on restore. Persistent storage
alone does not prove crash recovery or survival of every browser session. Rehearse
recovery in isolation using [Privacy Erasure](../security-and-identity/privacy-erasure.md)
and the [backup runbook](../configuration-and-operations/backup-restore-upgrade.md).

---

## 7. Production Acceptance Checklist

Before opening your instance to users, verify:

- [ ] Long-running services are running; migration/init helpers exited 0.
- [ ] `/health` has no required Unhealthy checks; intentional email disable or SMTP-only degradation is understood.
- [ ] TLS certificate is valid and redirects HTTP $\to$ HTTPS.
- [ ] Selected-provider login completes; Local temporary credentials require replacement before normal access.
- [ ] Public event listing is readable anonymously.
- [ ] Authenticated write action displays HAL affordances in the UI.
- [ ] Zero-email core operation works, or, if email is enabled, SMTP and recipient receipt have been tested separately.
- [ ] Database backups are automated and verified in an isolated test restore.

---

## Related Guides & Next Steps

* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md)** — Comprehensive catalog of all baseline and advanced configuration keys.
* **[Backup, Restore & Upgrade Guide](../configuration-and-operations/backup-restore-upgrade.md)** — Production PostgreSQL dump scripts and recovery rehearsal.
* **[Troubleshooting & Operational Health](../configuration-and-operations/troubleshooting-and-health.md)** — Step-by-step recipes for Keycloak, database lock, and migration issues.
* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Initialize your organization and customize branding.
