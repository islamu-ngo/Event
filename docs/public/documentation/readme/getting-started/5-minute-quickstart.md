---
description: Evaluate ISLAMU Event locally in under five minutes using Docker.
---
<!-- ABOUTME: Routes Docker evaluation through the real zero-email setup requirements. -->
<!-- ABOUTME: Distinguishes durable Standalone startup, Split dependencies and optional mail capture. -->

# 5-Minute Quickstart

The fastest way to evaluate ISLAMU Event on your local machine or testing server is using Docker. You do not need to install the .NET SDK or any compilers.

---

## Prerequisites

You only need:
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine (v24+)
- A web browser

---

## Option 1: Instant Single-Container Standalone (Fastest)

First prepare the private `.env` from the
[Standalone configuration step](../self-hosting/docker-standalone.md#step-2-prepare-configuration-env):
select the secret authority, generate the Local signing key, and supply operator
legal identity and your URL. An unconfigured production container is not a
working quickstart. SMTP is not required.

Keep the recipe's [bounded SQLite processing profile](../self-hosting/docker-standalone.md#bounded-sqlite-processing-profile),
including disabled Local webhooks. Default webhook readiness currently has a
SQLite timestamp-query limitation. This evaluation profile also leaves queued
dispatch and scheduled jobs off; it is not verification of workflows that depend
on those processors. The Standalone guide explains the operational limits.

Run the image with durable storage:

```bash
docker run -d \
  --name islamu-event-quickstart \
  --env-file .env \
  --mount source=event_quickstart_data,target=/app/data \
  -p 127.0.0.1:8080:8080 \
  ghcr.io/islamu-ngo/event-standalone:latest
```

Check `docker logs islamu-event-quickstart` and
`curl --fail http://localhost:8080/health`. Once initialization succeeds, open:
- **URL**: [http://localhost:8080](http://localhost:8080)
- **Setup Wizard**: [http://localhost:8080/setup](http://localhost:8080/setup)

Retrieve the generated setup secret to begin the setup wizard:
```bash
umask 077
docker cp islamu-event-quickstart:/app/data/setup-secret ./setup-secret
cat ./setup-secret
```

If you supplied `SETUP_SECRET`, use that explicit value instead. Validate the
secret in the wizard, choose **Continue Local setup**, and enter the initial
administrator username and temporary password; credential email is optional.
Do not use public **Create an account** registration. Complete mandatory private
password replacement, then sign in afresh. Delete the host secret copy after use
with `rm -f ./setup-secret`; never include it in logs or tickets.

For later accounts, current instance administrators use the
[Local accounts screen](../administration-and-branding/admin-guide.md#local-accounts)
and private temporary-password handover. Email delivery remains optional.

---

## Option 2: Full Split Topology with Docker Compose

If you want to evaluate the full stack with independent PostgreSQL and Keycloak services:

```bash
# 1. Clone the repository
git clone https://github.com/islamu-ngo/Event.git
cd Event

# 2. Copy the curated baseline
cp .env.example .env
chmod 600 .env
```

Complete the [Compose preparation](../self-hosting/docker-compose.md#2-prerequisites--preparation)
before starting: the template's empty secrets must be provisioned, and your
operator identity and provider settings must be valid. The full environment
catalogue is separate from this baseline.

```bash
# 3. Apply database migrations; require a successful exit
docker compose run --build --rm event-migrationservice

# 4. Start the base services, without mail
docker compose up -d --build
```

The current Split dependency graph starts Keycloak even when Local is selected.
It is larger than Standalone and still needs its infrastructure credentials.
Retrieve the Split setup secret from
`islamu-event-api:/app/bootstrap/setup-secret`, not the Standalone path; follow
the [Compose setup runbook](../self-hosting/docker-compose.md#option-a-interactive-setup-wizard).
Keycloak and AT Protocol retain their own account-verification policy when used.

### Accessing Endpoints

| Service | Endpoint |
|---|---|
| **Web Interface (BFF/UI)** | [http://localhost:7002](http://localhost:7002) |
| **REST API** | [http://localhost:7039](http://localhost:7039) |
| **Keycloak Administration** | [http://localhost:8080](http://localhost:8080) |
| **Mailpit (only with optional `mail` profile)** | [http://127.0.0.1:8025](http://127.0.0.1:8025) |

Mailpit is absent from base startup. To deliberately test captured mail, use the
[optional private SMTP guide](../communications-and-notifications/email-smtp.md#3-optional-private-mailpit-capture).
Starting the profile does not enable delivery: persisted instance SMTP settings
and `email.delivery_enabled` remain authoritative. Capture does not send mail to
external inboxes. Disabled email can be Healthy; a reported SMTP outage degrades
email without making otherwise healthy core readiness fail. Required database,
security and authority failures still fail.

---

## Related Guides & Next Steps

* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Walk through the setup wizard and manage organizations.
* **[Docker Standalone Runbook](../self-hosting/docker-standalone.md)** — Deploy the single-container image with SQLite volume persistence.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Deploy the production split stack with PostgreSQL and Keycloak.
* **[Architecture & Request Flows](architecture-and-request-flows.md)** — Understand browser BFF routing, MediatR CQRS, and persistence.
* **[Troubleshooting & Health](../configuration-and-operations/troubleshooting-and-health.md)** — Fast solutions for setup secret recovery and container issues.
