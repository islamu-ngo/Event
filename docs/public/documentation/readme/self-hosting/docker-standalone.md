---
description: Deploy and operate the single-container standalone distribution with durable SQLite storage.
---
<!-- ABOUTME: Operator runbook for the single-process ISLAMU Event distribution. -->
<!-- ABOUTME: Covers persistent storage, Local Identity defaults, first-run setup, proxying, and backup. -->

# Docker Standalone Self-Hosting

The standalone image (`Event.Standalone`) is the simplest and lowest-overhead operational topology for ISLAMU Event. It packages the API, background workers, Blazor WebAssembly BFF/UI, health endpoints, and in-process database migrations into **a single non-root container process**.

You can complete Local administrator setup and use the core without SMTP,
Mailpit or a credential email address. Public operator contact and legal identity
are still required. Email delivery starts disabled and is an optional,
administrator-controlled capability.

---

## 1. When to Choose Standalone

| Advantage | Consideration |
|---|---|
| **Zero External Infrastructure**: Runs on built-in SQLite persistence; no PostgreSQL server or Redis required. | **Single Replica**: SQLite requires exactly one running container instance (no horizontal multi-container scaling). |
| **Single-Process Footprint**: Runs API, BFF/UI, and SQLite in one container without auxiliary database servers. | **Local-First Storage**: Media and database files live in a mounted Docker volume. |
| **Instant Onboarding**: In-process migrations apply automatically before the HTTP port opens. | **Initial Platform Target**: Built for `linux/amd64`. |

---

## 2. Quick Run & Production Deployment

### Step 1: Create a Persistent Volume

ISLAMU Event Standalone requires persistent storage mounted at `/app/data` to retain the primary database, privacy-erasure authority, Data Protection keys, and uploaded media:

```bash
docker volume create event_standalone_data
```

### Step 2: Prepare Configuration (`.env`)

Create a private `.env` file containing your production settings. The repository
`.env.example` is a curated baseline, not a ready-to-run secret file or an
exhaustive reference. Its PostgreSQL host, port and role settings must not be
carried into SQLite configuration. Use the small Standalone projection below and
the [environment reference](../configuration-and-operations/environment-variables.md)
for advanced settings. Restrict the file to its operator (`chmod 600 .env`).

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
SECRET_PROVIDER=Environment
DATABASE_PROVIDER=sqlite
DEPLOYMENT_MODE=SingleTenant
LOCAL_STORAGE_ROOT_PATH=/app/data/storage

# Bounded SQLite profile: optional processing stays off
Webhooks__Enabled=false
OutboxProcessor__Enabled=false
NotificationFanoutProcessor__Enabled=false
Scheduler__Quartz__Enabled=false
EmailDispatchProcessor__Enabled=false
EmailDispatchRabbitMq__Enabled=false
MCP_ENABLED=false

# Base Application URLs
PUBLIC_BASE_URL=https://events.example.org

# Authentication (Local Identity; the standalone default)
# See: ../security-and-identity/authentication.md
AUTHENTICATION_PROVIDER=local
AUTHORIZATION_PROVIDER=local
AUTHENTICATION_LOCAL_JWT_KEY=replace-with-output-from-openssl-rand-base64-64
IDENTITY_DATABASE_TOPOLOGY=colocated

# Operator Legal Identity (Required for Production startup)
# See: ../configuration-and-operations/environment-variables.md#9-operator-legal-identity-production-gate
INSTANCE__OPERATORIDENTITY__OPERATORID=01912a7e-1234-7000-8000-000000000001
INSTANCE__OPERATORIDENTITY__PUBLICNAME=Community Events Foundation
INSTANCE__OPERATORIDENTITY__LEGALNAME=Community Events Foundation Non-Profit
INSTANCE__OPERATORIDENTITY__ISOFFICIALINSTANCE=false
INSTANCE__OPERATORIDENTITY__OFFICIALORIGIN=https://events.example.org
INSTANCE__OPERATORIDENTITY__OPERATORKINDCODE=unincorporated_association
INSTANCE__OPERATORIDENTITY__JURISDICTIONCOUNTRYCODE=US
INSTANCE__OPERATORIDENTITY__PUBLICCONTACTEMAIL=contact@example.org
INSTANCE__OPERATORIDENTITY__WEBSITEURL=https://example.org
INSTANCE__OPERATORIDENTITY__LEGALNOTICEURL=https://example.org/legal
INSTANCE__OPERATORIDENTITY__TERMSURL=https://example.org/terms
INSTANCE__OPERATORIDENTITY__PRIVACYURL=https://example.org/privacy
```

#### Bounded SQLite processing profile

Keep the optional-processing settings above for this Standalone recipe. Local
webhooks are otherwise enabled by default, and their current SQLite readiness
queries can fail with an unsupported timestamp comparison. The event-directory
query correction does not repair webhook readiness. Disabling SMTP alone does
not avoid that separate failure.

This bounded profile is verified for directory and administrator HTTP access,
email health, and persisted credential, bootstrap and key continuity. It does
not run webhook delivery, queued outbox/email/notification processing or Quartz
jobs. Scheduled maintenance and cleanup therefore do not run, and workflows
that need asynchronous completion must not be treated as operational under
these settings. Read-time privacy expiry is not a substitute for physical cleanup.

Before relying on those features, verify their database/worker configuration
separately and deliberately enable the required processors. Enabling persisted
email delivery does not start processors disabled here. Do not hide the webhook
failure by changing readiness results or disabling database/security checks.

Replace the example legal identity, URLs and JWT-key placeholder before starting.
The operator kind must match your actual legal status; `community` is not an
accepted code. The HTTPS `OFFICIALORIGIN` is required even for an unofficial
instance. For localhost evaluation, use your intended operator HTTPS origin for
that identity field and `http://localhost:8080` for `PUBLIC_BASE_URL`.
Generate the signing key with `openssl rand -base64 64`, store it only in the
selected secret authority, and retain it across recreation. Do not add SMTP
credentials to make setup pass. With Infisical, select and configure that authority
explicitly; it does not fall back to environment secrets.

### Step 3: Run the Container

```bash
docker run -d \
  --name islamu-event-standalone \
  --restart unless-stopped \
  --env-file .env \
  --mount source=event_standalone_data,target=/app/data \
  -p 127.0.0.1:8080:8080 \
  ghcr.io/islamu-ngo/event-standalone:latest
```

*(Alternatively, build from source: `docker build -t islamu/event-standalone -f src/Event.Standalone/Dockerfile .`)*

---

## 3. Container Startup & File Layout

When the container launches:
1. It applies migrations and seeding for the primary SQLite database (`/app/data/islamu_event.db`).
2. It initializes the separate [GDPR Privacy-Erasure authority store](../security-and-identity/privacy-erasure.md) (`/app/data/privacy_erasure_authority.db`).
3. It persists Data Protection keys in the primary SQLite database, not a separate key directory. With the configuration above, uploaded media lives at `/app/data/storage`.
4. It starts the internal Kestrel web server and binds port `8080`.

Verify container startup logs:

```bash
docker logs -f islamu-event-standalone
```

Check readiness at `http://localhost:8080/health` and liveness at `/alive`.
Disabled email can report Healthy. Enabled but unconfigured email, or a reported
SMTP connection failure, reports Degraded without making otherwise healthy core
readiness return HTTP 503. Required database, security and authority failures
still block startup or report Unhealthy; investigate those rather than disabling
their checks. A successful readiness response is not proof of email receipt.
The expected healthy-core result assumes the bounded optional-processing
configuration above, not the unchanged default Local-webhook configuration.

---

## 4. First-Run Setup Wizard

Once the container is healthy:

1. Retrieve the generated setup secret from the Docker host in a private terminal:
   ```bash
   umask 077
   docker cp islamu-event-standalone:/app/data/setup-secret ./setup-secret
   cat ./setup-secret
   ```
   If you supplied `SETUP_SECRET` explicitly, use that value instead; a generated
   file is not expected. Never paste it into logs or support tickets.
2. Navigate to `http://localhost:8080/setup` (or `https://events.example.org/setup` behind your reverse proxy).
3. Validate the secret, choose **Continue Local setup**, and complete the instance
   details. Enter the initial administrator's username and temporary password;
   credential email is optional. This setup-authorized provisioning does not
   require an existing sign-in or public **Create an account** flow.
4. Sign in with that temporary credential and complete the required private
   password replacement, then sign in afresh. The temporary credential cannot
   establish an ordinary session. No verification message is needed for Local
   administrative handover.
5. After completion the setup flow is locked and the generated secret file is
   removed. Delete your host copy with `rm -f ./setup-secret`.

The profile's **Support email** is public site identity, not the account's
credential email or an SMTP From address. Saving it does not configure a sender
or enable delivery; use the separate SMTP administration when email is wanted.

Create subsequent Local accounts through
`/settings/instance?section=local-accounts`, using current instance-administrator
access. That screen collects email and profile details, but hands over the
generated temporary password privately rather than mailing it. Creation and reset
both require the recipient to replace the credential before normal sign-in.
Tenant administration alone cannot provision these shared credentials. See the
[Local accounts runbook](../administration-and-branding/admin-guide.md#local-accounts)
for one-time disclosure and lost-response recovery.

If you deliberately select Keycloak or AT Protocol, use that provider's sign-in
and verification procedures. Turning off Event email does not turn off the
provider's verification or account policy. For automated initial provisioning,
see [configured administrator setup](docker-compose.md#option-b-headless-automated-onboarding).

### Optional Email

Leave email disabled for zero-email operation. To add it later, save non-secret
SMTP settings in instance administration and explicitly enable delivery there;
credentials belong in the selected secret authority. Existing persisted
`email.delivery_enabled` remains authoritative across restarts: environment
values, a setup manifest and starting a mail container are not bypasses. Disabling
active delivery uses the guarded preview/confirmation workflow.

Mailpit is not included in this image. The optional Compose `mail` profile is
private local capture for applications on its container network; its host SMTP
port is deliberately not published. A separately launched Standalone container
does not automatically share that network. Use the
[SMTP and local capture guide](../communications-and-notifications/email-smtp.md)
only when you intentionally add email.

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

Back up the primary database (including Local Identity and Data Protection keys),
media and independent privacy-erasure authority consistently. Preserve the
selected signing/secret authority separately. The shipped container is chiseled:
do not assume it contains a shell or the `sqlite3` command.

For a simple stopped-writer capture into an access-restricted host directory:

```bash
umask 077
mkdir standalone-backup
docker stop islamu-event-standalone
docker cp islamu-event-standalone:/app/data/. ./standalone-backup/
# Restart only after the copy completes successfully.
docker start islamu-event-standalone
```

> [!CAUTION]
> **Capture together does not mean roll back together.** Keep the newest verified
> privacy-erasure authority independently of any primary database rollback. Do not
> replace it with the older authority copy from a historical whole-volume backup;
> that can discard later erasures. Include required SQLite WAL companions when
> preserving files and follow the [Privacy Erasure](../security-and-identity/privacy-erasure.md)
> replay gates before reopening traffic.

Store the capture encrypted outside the container host and rehearse recovery in
isolation. A persistent volume is not a backup, and key persistence alone does not
prove crash recovery, a consistent live snapshot or survival of every session.

---

## Related Guides & Next Steps

* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Complete the web onboarding wizard at `/setup`.
* **[Deployment Tiers & Sizing](deployment-tiers.md)** — Review capacity benchmarks and hardware sizing.
* **[Docker Compose Runbook](docker-compose.md)** — Scale up to split PostgreSQL and Keycloak containers when ready.
* **[Troubleshooting & Operational Health](../configuration-and-operations/troubleshooting-and-health.md)** — Fast solutions for setup secret retrieval, TLS issues, and container errors.
