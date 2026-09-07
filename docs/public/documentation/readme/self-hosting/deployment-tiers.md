---
description: Understand infrastructure sizing tiers and scaling profiles for self-hosters.
---

# Deployment Tiers & Capacity Planning

ISLAMU Event is architected to scale from a single lightweight VM to a distributed, multi-datacenter topology without changing the underlying code. The platform defines three primary deployment tiers based on community size, concurrency, and security requirements.

---

## Tier Summary Matrix

| Metric | Tier 1: Humble | Tier 2: Community | Tier 3: Ummah-Scale |
|---|---|---|---|
| **Target Scale** | Up to 1,000 attendees / month | 1,000 – 50,000 attendees / month | 50,000+ concurrent attendees |
| **Topology** | Standalone or Minimal Compose | Split Compose or Managed Containers | Distributed Kubernetes / Container Apps |
| **Database** | SQLite or Single PostgreSQL | PostgreSQL + Read Replica | Multi-Node PostgreSQL Cluster (HA) |
| **Authorization** | Local database-backed RBAC | External Cerbos PDP (Fail-closed) | High-Availability Cerbos Cluster |
| **Storage** | Local volume mount | S3-Compatible (MinIO / R2 / S3) | Distributed S3 with Multi-Region CDN |
| **Minimum Hardware** | 1 vCPU, 2 GB RAM | 4 vCPUs, 8 GB RAM | 8+ vCPUs, 16+ GB RAM (per node) |

> [!TIP]
> **⚙️ Real-World Reference Setup (ISLAMU's Production Topology):**
> Our primary production deployment is orchestrated using a **Coolify Cloud** subscription connected to a **Hetzner CPX32** cloud server (see our [Coolify Deployment Guide](coolify-cerbos-traefik.md)):
> - **Server Hardware & Compute:**
>   - **CPU:** 4 vCPUs
>   - **RAM:** 8 GB
>   - **Disk (Local):** 160 GB
>   - **Attached Storage:** 50 GB (1 Volume)
>   - **Management:** Server connected to [Coolify Cloud](https://coolify.io)
> - **Object Storage:** Hetzner Object Storage resource (S3-compatible)
>   - **Location:** City: Falkenstein | Country: Germany | Network zone: `eu-central`
> - **Deployed Services (via Coolify):**
>   - `islamu-event-api` Docker image (ASP.NET Core REST API & Background Workers)
>   - `islamu-event-ui` Docker image (Blazor WebAssembly BFF)
>   - `postgres` for ISLAMU Event (Primary application database)
>   - `postgres` for Cerbos (Dedicated policy storage backend)
>   - `cerbos` Docker image (Policy Decision Point for fine-grained authorization)
>   - `keycloak` Phase Two Docker image (`phasetwo/phasetwo-keycloak` for multi-tenancy & OIDC identity)
>   - `cockroachdb` (Dedicated database backend for Phase Two Keycloak)
>   - `infisical` (Secrets management engine for dynamic runtime configuration)
>   - SQLite Privacy Erasure Authority store (GDPR anti-resurrection fence)
> - **Workload Handled:** Comfortably supports multi-tenant organizations, concurrent ticket launches and check-ins, and continuous background outbox email delivery with sub-45ms API responses.
>
> 💡 **Latency & Datacenter Placement Note:**
> It is best to choose a server location close to where you are or where your main public is located to have the lowest latency. Co-locating your compute server and object storage near your primary audience minimizes round-trip latency, accelerates media uploads/downloads, and ensures the fastest responsiveness.

---

## Tier 1: Humble (Single VM / Self-Hosted)

**Recommended For:**
- Individual community centers, local mosques, student clubs, or small non-profits.
- Low operational overhead and single-server simplicity.

**Typical Stack:**
- **Deployment Mode**: `Docker Standalone` (or minimal Compose).
- **Relational Data**: Single SQLite file or small co-located PostgreSQL container.
- **Authorization**: `AUTHORIZATION_PROVIDER=local` (uses fast internal database role/permission checks; no external Cerbos PDP required).
- **Identity**: Co-located Keycloak container or external community OIDC provider.
- **Media**: Local persistent filesystem volume mounted at `/app/data` or `/app/storage-data/local`.

---

## Tier 2: Community (Production Multi-Tenant)

**Recommended For:**
- Regional organizations, multi-chapter NGOs, or umbrella event organizers hosting multiple tenant communities on one instance.
- Requires strict policy controls and multi-tenant isolation.

**Typical Stack:**
- **Deployment Mode**: `Docker Compose` split topology (or Coolify).
- **Relational Data**: Dedicated PostgreSQL server with automated daily snapshot backups.
- **Authorization**: `AUTHORIZATION_PROVIDER=cerbos` running as a dedicated sidecar or external PDP.
  - Runtime queries evaluate fine-grained tenant policies via gRPC.
  - Fail-closed behavior guarantees that PDP unavailability denies unauthorized access.
- **Storage**: S3-compatible object store (e.g., Cloudflare R2, MinIO, or AWS S3) with metadata-backed verification.
- **Webhooks & Messaging**: Background outbox dispatchers running alongside Redis / PostgreSQL outbox tables.
- **Redis**: Optional. Paid ticket checkout no longer needs it. The redirect that hands a buyer to the payment provider carries its own encrypted, short-lived cookie, so a split stack with no Redis container still sells tickets.

> [!IMPORTANT]
> **Running more than one UI replica?** Each replica must be able to read cookies the others issued. Give them a shared Data Protection key ring, either by mounting the same key directory into every UI container or by pointing them all at the same Redis instance. Keep the application name identical across replicas. If you choose Redis for the key ring, Redis is back on your critical path: plan its availability accordingly. A single-replica deployment needs neither.
>
> Database-backed key storage on the API side does not cover the UI host. The UI container makes its own key ring choice.

---

## Tier 3: Ummah-Scale (High-Availability & Enterprise)

**Recommended For:**
- Global conventions, national federations, high-traffic ticket launches, or strict data residency compliance.
- Zero-downtime upgrades, horizontal autoscaling, and independent failure domains.

**Typical Stack:**
- **Deployment Mode**: Clustered container orchestrators (Azure Container Apps, Nomad, or Kubernetes).
- **Relational Data**: Independent, isolated database clusters:
  1. *Application DB Cluster*: Stores events, orders, tickets, and tenants.
  2. *Identity DB*: Dedicated Keycloak user database.
  3. *Policy DB*: Dedicated PostgreSQL storage for Cerbos policy revisions.
  4. *Privacy Erasure Authority*: Dedicated external PostgreSQL database enforcing GDPR anti-resurrection fences.
- **Authorization**: Clustered Cerbos PDPs behind an internal load balancer running with `h2c`.
- **Observability**: Centralized Prometheus metrics, OpenTelemetry distributed tracing, and structured log aggregation (Loki / Elasticsearch).

---

## Capacity & Hardware Recommendations

| Component | Minimum (Tier 1) | Recommended (Tier 2) | High-Scale (Tier 3) |
|---|---|---|---|
| **CPU** | 1 Core | 4 Cores | 8+ Cores |
| **RAM** | 2 GB | 8 GB | 16 – 32 GB |
| **Disk Type** | NVMe / SSD | NVMe SSD with IOPS guarantees | Dedicated Managed Storage |
| **Network** | 100 Mbps | 1 Gbps | 10 Gbps |

> [!NOTE]
> Sizing figures are baseline guidelines based on typical community workloads. Actual memory and CPU requirements depend on active concurrent attendees, background outbox workers, media upload traffic, and whether services run on a single host or across independent container nodes.

---

## Related Guides & Next Steps

* **[Docker Standalone Runbook](docker-standalone.md)** — Deploy Tier 1 single-container setup with SQLite.
* **[Docker Compose Runbook](docker-compose.md)** — Deploy Tier 2 production split stack with PostgreSQL and Keycloak.
* **[Coolify with Cerbos & Traefik](coolify-cerbos-traefik.md)** — Deploy Cerbos PDP and production stack on Coolify.
* **[Environment Variables Reference](../configuration-and-operations/environment-variables.md)** — Review all baseline and advanced configuration dials.
* **[Backup, Restore & Upgrade](../configuration-and-operations/backup-restore-upgrade.md)** — Operational runbook for database dumps, restore rehearsal, and version migrations.
