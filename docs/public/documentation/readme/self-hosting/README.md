---
description: >-
  Choose and operate the supported standalone, split, Coolify, or Aspire/cloud
  path.
---

# Self-Hosting

Select a topology by operational needs, then follow its dedicated runbook. The project is in active pre-release development with no official release yet: pre-built images and versioned tags will become available upon initial release.

## Deployment paths

| Path                                                       | Best fit                                                | Primary constraint                                                     |
| ---------------------------------------------------------- | ------------------------------------------------------- | ---------------------------------------------------------------------- |
| [Deployment Tiers & Sizing](deployment-tiers.md)           | Hardware capacity and infrastructure sizing             | Choose based on monthly attendee volume                                |
| [Docker Standalone](docker-standalone.md)                  | Smallest deployment and lowest operating load           | One replica, durable SQLite/local volume, multi-platform (`linux/amd64`, `linux/arm64`) |
| [Docker Compose](docker-compose.md)                        | Split services and a server database                    | One-shot migration service must complete before API/UI                 |
| [Coolify with Cerbos & Traefik](coolify-cerbos-traefik.md) | Existing Coolify/Traefik operators using Cerbos         | Cerbos runbook only, not a whole-platform one-click template           |
| [.NET Aspire & Cloud](dotnet-aspire-and-cloud.md)          | Development orchestration or adopter-owned cloud design | No turnkey Azure/AWS template or universal responsibility model        |

## Which Topology Should You Choose?

| Decision Factor | Standalone Container (`Event.Standalone`) | Docker Compose Split Stack |
|---|---|---|
| **Ideal For** | Individual mosques, local non-profits, lowest RAM | Multi-tenant organizations, high-traffic ticket releases |
| **Database** | Built-in SQLite (zero external dependencies) | PostgreSQL 16 server (dedicated container) |
| **Container Count** | **1 container** | **3–6 containers** (API, UI, Migrator, PostgreSQL, Keycloak) |
| **Resource Footprint** | Lowest RAM (runs on 2 GB VM) | Standard RAM (recommended 4–8 GB VM) |
| **Horizontal Scaling** | Single replica only | Multiple API replicas behind load balancer |
| **Operational Effort** | Minimal: single container to run and back up | Standard: container network and migration lifecycle |
| **Backup Mechanics** | Single volume / atomic SQLite `.backup` copy | `pg_dump` dumps for app and Keycloak DBs |

> [!TIP]
> **Our Recommendation:**
> - **We recommend Docker Standalone** if you are deploying for a single community, university club, or mosque, and want near-zero DevOps maintenance.
> - **We recommend Docker Compose** if you plan to host multiple independent communities (`multi_tenant`), expect high concurrent ticket check-ins, or want to decouple your database from your application processes.

For authentication, use this order unless your requirements say otherwise:

1. **Local Identity** for the default standalone experience, localhost, and the
   lowest operational burden.
2. **AT Protocol** for an average public-HTTPS self-hosted instance that wants
   users to authenticate through AT Protocol/Bluesky instead of the host
   managing passwords. It ranks second only because it cannot complete OAuth on
   localhost.
3. **Keycloak** for serious hosting teams and SaaS operators that need the most
   advanced SSO/federation, 2FA/MFA, and centralized identity administration.

See [Authentication Providers](../configuration-and-operations/authentication-providers.md)
for the exact runtime matrix and safe switching procedure.

Kubernetes, Helm, ActivityPub infrastructure, and first-party PDS/AppView hosting are not implemented deployment options.

## One supported build

Self-hosters, contributors and hosted deployments use the same supported package
graph. There is no commercial edition switch or Lucky Penny library license key.
CI and the API/UI Dockerfiles enforce locked dependency restoration. Before
upgrading existing build or deployment configuration, remove the
[obsolete edition inputs](../configuration-and-operations/environment-variables.md#removed-edition-inputs).
No replacement flag or database migration is needed; provider credentials and
the selected hosting topology remain unchanged.

## First-Run Onboarding & Operator Identity

All self-hosted topologies feature a guided first-run web wizard at `/setup`:

1. **Decoupled Startup:** The server process boots cleanly without requiring operator legal identity environment variables up front. Optional `INSTANCE__OPERATORIDENTITY__*` variables can be provided to pre-seed initial defaults, but they are not required to start the container.
2. **Setup Wizard Configuration (`/setup`):** Operators use the temporary setup secret to select the deployment mode (`SingleTenant` or `MultiTenant`), configure initial administrator credentials, and complete the operator's legal identity (legal name, jurisdiction, contact email, and legal disclosure URLs).
3. **Fail-Closed Consumer Protections:** Completed instances with missing or incomplete operator identity will start, but will fail closed for consumer-facing legal operations: public legal notices return HTTP 503 (`Unavailable`), and paid ticket checkout activation is blocked until identity requirements are satisfied.
4. **Post-Launch Maintenance (`/settings/instance`):** Once onboarding completes, the setup wizard locks permanently. Authorized administrators maintain and update operator legal details under **Settings → Instance → Operator Identity** (`/settings/instance?section=operator-identity`).

## Shared production gate

Every path must define durable state, migrations, identity, authorization, tenant binding, secrets, TLS/DNS, health, backups, restore rehearsal, upgrade, and rollback. Continue with [Configuration & Operations](../configuration-and-operations/) after choosing a topology.

{% hint style="info" %}
**Sustaining Community Infrastructure:**
ISLAMU Event is 100% free and open-source under the AGPL-3.0-or-later. If deploying ISLAMU Event saves your community or organization operational and licensing fees, please consider [becoming a sponsor](../contributing/sponsorship.md) or [donating directly via Stripe](https://donate.stripe.com/14A6oIesc0Oc2KYg35aR200).
{% endhint %}
