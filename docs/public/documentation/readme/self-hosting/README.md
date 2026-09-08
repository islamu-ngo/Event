---
description: >-
  Choose and operate the supported standalone, split, Coolify, or Aspire/cloud
  path.
---

# Self-Hosting

Select a topology by operational needs, then follow its dedicated runbook. The project is pre-1.0: pin versions and image digests, review release/API changes, and test backup restoration before upgrades.

## Deployment paths

| Path                                                       | Best fit                                                | Primary constraint                                                     |
| ---------------------------------------------------------- | ------------------------------------------------------- | ---------------------------------------------------------------------- |
| [Deployment Tiers & Sizing](deployment-tiers.md)           | Hardware capacity and infrastructure sizing             | Choose based on monthly attendee volume                                |
| [Docker Standalone](docker-standalone.md)                  | Smallest deployment and lowest operating load           | One replica, durable SQLite/local volume, initial `linux/amd64` target |
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

Kubernetes, Helm, ActivityPub infrastructure, first-party PDS/AppView hosting, and initial `linux/arm64` packaging are not implemented deployment options.

## Shared production gate

Every path must define durable state, migrations, identity, authorization, tenant binding, secrets, TLS/DNS, health, backups, restore rehearsal, upgrade, and rollback. Continue with [Configuration & Operations](../configuration-and-operations/) after choosing a topology.

{% hint style="info" %}
**Sustaining Community Infrastructure:**
ISLAMU Event is 100% free and open-source under the AGPL-3.0-or-later. If deploying ISLAMU Event saves your community or organization operational and licensing fees, please consider [becoming a sponsor](../contributing/sponsorship.md) or [donating directly via Stripe](https://donate.stripe.com/14A6oIesc0Oc2KYg35aR200).
{% endhint %}
