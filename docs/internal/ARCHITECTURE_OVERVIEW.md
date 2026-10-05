ABOUTME: High-level visual system architecture, C4 container diagram, and project map.
ABOUTME: Explains how all services, layers, and hosting topologies interact across the platform.

# System Architecture & Component Overview

> **Audience:** Contributors | Developers | Architects | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-03
> **Source Anchors:** `Explore.API/Program.cs`, `Explore.Blazor/Program.cs`, `Explore.AppHost/AppHost.cs`, `Event.Standalone/Program.cs`, `docs/DEVELOPER_GUIDE.md`, `docs/REQUEST_FLOWS.md`

This document provides a high-level visual and technical overview of how all subsystems, projects, and external services in the **ISLAMU Event** platform interact.

---

## 1. High-Level System Context & Container Map

The following diagram illustrates the complete runtime topology of ISLAMU Event, including the browser client, identity providers, authorization engines, backend APIs, background workers, and external integrations.

```mermaid
flowchart TB
    subgraph Users ["Users & Clients"]
        Attendee["Event Attendee / Public User"]
        Organizer["Event Organizer / Admin"]
        MCPClient["AI Agent / MCP Client"]
    end

    subgraph Presentation ["Presentation & BFF Layer"]
        WASM["Explore.Blazor.Client<br/>(WebAssembly in Browser)<br/>MudBlazor UI + HAL Affordances"]
        BFF["Explore.Blazor<br/>(ASP.NET Core Host / BFF)<br/>Cookie Auth + YARP Proxy<br/>https://localhost:7177"]
    end

    subgraph Security ["Identity & Access Control"]
        Keycloak["Keycloak (OIDC Provider)<br/>User Accounts & Sessions"]
        Cerbos["Cerbos PDP (Policy Engine)<br/>gRPC / Local Fallback RBAC"]
    end

    subgraph Backend ["Application & Domain Backend"]
        API["Explore.API<br/>(REST API Host)<br/>Controllers + Native CQS Pipeline<br/>https://localhost:7039"]
        Standalone["Event.Standalone<br/>(Combined API + BFF)<br/>Optional Single Process<br/>https://localhost:7180"]
        MCPAdapter["MCP Server Adapter<br/>Stateless HTTP (/mcp)<br/>Tool Proposal Engine"]
    end

    subgraph Persistence ["Persistence & Scheduling"]
        EF["ExploreDbContext<br/>(Pooled Factory + Named Filters)"]
        DB[(PostgreSQL / SQLite / MariaDB / SQL Server<br/>Relational Data + Outbox)]
        Quartz["Quartz.NET Scheduler<br/>Outbox Dispatcher Jobs"]
        Redis[("Redis / HybridCache<br/>L1 Memory + L2 Distributed")]
    end

    subgraph Infrastructure ["External Services & Storage"]
        S3[("Object Storage<br/>MinIO / S3 / Local")]
        Mailpit["SMTP Server / Mailpit<br/>Email Delivery"]
        Webhooks["Outgoing Webhooks<br/>Svix / Local Dispatcher"]
        Federation["ATProto PDS<br/>Event Federation Sync"]
    end

    Attendee -->|HTTPS| WASM
    Organizer -->|HTTPS| WASM
    WASM <-->|JSON via BFF Proxy| BFF
    BFF <-->|OIDC Auth Code| Keycloak
    BFF -->|YARP + Bearer Token + X-Tenant-Slug| API
    MCPClient <-->|Streamable HTTP /mcp| MCPAdapter
    MCPAdapter --> API

    API <-->|Check Permissions| Cerbos
    API <-->|L1/L2 Cache| Redis
    API -->|Commands & Queries| EF
    EF -->|SQL Queries & Transactions| DB

    Quartz -->|Poll Outbox Table| DB
    Quartz -->|Send Emails| Mailpit
    Quartz -->|Deliver Webhook Payloads| Webhooks
    Quartz -->|Sync PDS Records| Federation
    API <-->|Upload / Download Assets| S3
```

---

## 2. Clean Architecture & Project Dependency Graph

ISLAMU Event adheres strictly to **Clean Architecture** with inward-pointing dependencies. The core domain is entirely isolated from UI frameworks, database engines, and third-party libraries.

```mermaid
graph TD
    subgraph Core ["Core Business Logic"]
        Domain["Explore.Domain<br/>(Pure Entities, Enums, Rules)"]
        Application["Explore.Application<br/>(CQRS Handlers, DTOs, Contracts)"]
    end

    subgraph Outer ["Infrastructure & Presentation"]
        Persistence["Explore.Persistence<br/>(EF Core, Repositories, Migrations)"]
        Infrastructure["Explore.Infrastructure<br/>(Email, Storage, Webhooks, Federation)"]
        APIHost["Explore.API<br/>(REST Host, Middleware, Composition Root)"]
        BFFHosting["Event.Web.BffHosting<br/>(Shared BFF Primitives)"]
        BlazorBFF["Explore.Blazor<br/>(BFF Server Host)"]
        BlazorClient["Explore.Blazor.Client<br/>(WASM UI & Pages)"]
    end

    Application -->|Depends on| Domain
    Persistence -->|Implements Contracts in| Application
    Persistence -->|Maps Entities from| Domain
    Infrastructure -->|Implements Contracts in| Application
    Infrastructure -->|Uses Entities from| Domain
    APIHost -->|Wires DI for| Application
    APIHost -->|Wires DI for| Persistence
    APIHost -->|Wires DI for| Infrastructure

    BlazorBFF -->|Hosts & Serves| BlazorClient
    BlazorBFF -->|Uses| BFFHosting
    BlazorClient -.->|Calls via Generated IEventApiClient only| APIHost
```

### Project Catalog & Responsibilities (2026-09-03 Checkpoint: 500,837 Backend Prod LOC)

| Project | Clean Architecture Layer | Scale (LOC) | Key Responsibilities | Dependencies |
|---|---|---|---|---|
| **`Explore.Domain`** | Domain | **55,000 LOC** | Entities (`Event`, `Organization`, `User`, `OutboxMessage`), status enums, marker interfaces (`ITenantEntity`, `IAuditableEntity`, `ISoftDeletable`). **Architectural note:** Intentionally unifies domain and EF Core persistence models to eliminate 200+ duplicate mirror classes and 140+ mappers while enabling native LINQ expression projections and direct global query filtering. | *None (Zero external dependencies; references only `Event.Wire.Contracts`)* |
| **`Explore.Application`** | Application | **224,000 LOC** | CQRS feature slices (126 slices, 625 handlers, Commands/Queries), native CQS handlers, FluentValidation rules, DTOs, specification query filters, and interface contracts (`IEventRepository`, `ITenantContext`). | `Explore.Domain` |
| **`Explore.Persistence`** | Persistence / Data | **81,000 LOC** | `ExploreDbContext` (split partials, 339 query filters), EF Core entity configurations, named query filters, repository implementations across 5 database providers, and data protection key stores. | `Explore.Application`, `Explore.Domain` |
| **`Explore.Infrastructure`** | Infrastructure | **67,000 LOC** | 6 specialized outboxes, email dispatching (SMTP), object storage (S3/MinIO), outgoing webhooks (Svix/Local), ATProto transport, and moderation adapters. | `Explore.Application`, `Explore.Domain` |
| **`Explore.API`** | Presentation / API Host | **73,000 LOC** | Composition root, 172 ASP.NET Core controllers (32,418 LOC), middleware pipeline (tenant resolution, rate limiting, HATEOAS, auth), HAL resource assemblers, and OpenAPI generation. | `Explore.Application`, `Explore.Persistence`, `Explore.Infrastructure`, `Explore.Domain` |
| **`Explore.Blazor`** | Presentation / BFF Host | Shared Host | Blazor Server host, OIDC authentication challenge endpoints, session cookie management, and YARP reverse proxy to `Explore.API`. | `Event.Web.BffHosting`, `Explore.Blazor.Client` |
| **`Explore.Blazor.Client`** | Presentation / UI Client | **378,000 LOC** | Interactive Blazor WebAssembly frontend, MudBlazor pages, components, dialogs, design system tokens, and NSwag-generated `IEventApiClient` consumption (182,524 LOC generated client). | *None (Generated client boundary)* |
| **`Event.Standalone`** | Optional Combined Host | Composition Root | Single-process host combining `Explore.API` and `Explore.Blazor` with an in-process direct pipeline and SQLite defaults for minimal resource footprints. | `Explore.API`, `Explore.Blazor` |
| **`Explore.AppHost`** | Orchestrator | .NET Aspire orchestration project for spinning up local development topologies (databases, Keycloak, Cerbos, Redis, Mailpit). | Aspire SDK |

---

## 3. Hosting Topologies: Split vs Standalone

ISLAMU Event supports two distinct hosting models configured via `Hosting:Topology`:

### 1. Split Topology (Default & Production Scalable)
In this mode, the frontend BFF and the backend API run in completely separate OS processes (or containers).

```mermaid
sequenceDiagram
    autonumber
    actor User as Browser (WASM)
    participant BFF as Explore.Blazor (BFF :7177)
    participant Keycloak as Keycloak (:8080)
    participant API as Explore.API (:7039)
    participant DB as PostgreSQL (:5432)

    User->>BFF: 1. Request Page / Action (HTTP Cookie)
    Note over BFF: BFF verifies session cookie & antiforgery
    BFF->>API: 2. YARP Proxy forwards request<br/>+ Authorization: Bearer <token><br/>+ X-Tenant-Slug: <slug>
    Note over API: API validates JWT, resolves tenant, executes CQRS
    API->>DB: 3. Execute EF Core Query / Command
    DB-->>API: 4. SQL Results
    API-->>BFF: 5. Response JSON (HAL format with _links)
    BFF-->>User: 6. Response JSON to WASM Client
```

### 2. Standalone Topology (Combined Single Process)
In this mode, `Event.Standalone` hosts both Blazor UI and API within a **single executable process**.
- Browser requests to `/api/*` bypass YARP network hops.
- The BFF middleware sanitizes session cookies and dispatches in-process directly to the API pipeline.
- Ideal for minimal self-hosting, personal instances, or lightweight single-server deployments (e.g. SQLite storage).

---

## 4. Core Subsystem Architecture

### A. Multi-Tenancy Resolution & Hierarchy
Multi-tenancy is baked into the foundation of the platform:
1. **Hierarchy**: `Instance` (platform owner) ➔ `Tenant` (community/portal) ➔ `Organization` (event organizer) ➔ `Group` ➔ `User`.
2. **Resolution Pipeline**:
   - `ApiTenantResolutionMiddleware` extracts the tenant from (1) `X-Tenant-Slug` header from BFF, (2) custom domain mapping, or (3) subdomain.
   - The resolved `TenantContext` is injected into `ExploreDbContext`.
   - EF Core **Named Query Filters** automatically append `WHERE TenantId = @currentTenant` to all tenant-scoped entities (`Event`, `Organization`, `Location`, etc.).

### B. The 3-Layer Event Domain Model
Events are structured across three distinct abstraction layers to provide maximum flexibility without schema chaos:
1. **Layer 1: Universal Semantics**: Stored directly on core entities (`Event`, `EventSession`, `EventAgendaItem`, `Organization`). Includes title, slug, start/end dates, timezone, description, and status.
2. **Layer 2: Sector-Standard Aspects**: 1:1 typed optional extensions for specific verticals:
   - `EventIslamicAspect`: Prayer-relative timing, madhab targeting, halal food certification, gender segregation modes.
   - `EventTechAspect`: Tech stack tags, speaker GitHub links, skill level, devroom track.
3. **Layer 3: Governed Custom Properties**: Dynamic tenant-specific custom fields and templates defined via admin governance. Promoted to Layer 2 if they become standard.

### C. Three-Tier Location & Privacy Disclosure Model
To prevent sensitive venue addresses or private home locations from leaking to unauthenticated users or across unrelated events:
1. **`Location` (Physical Venue Master)**: Tenant-scoped physical building, room sub-divisions (`LocationRoom`), and exact coordinates/address (`LocationPii` with erasure lifecycle).
2. **`EventLocation` (Per-Event Policy)**: First-class aggregate mediating an `Event` and a `Location` (or explicit `IsToBeAnnounced` placeholder). Owns 7 field-level disclosure flags, audience gating (`FullDetailsAudienceId`), and timed reveal (`RevealFullDetailsFromUtc`).
3. **`EventSession` / `EventAgendaItem` (Mediation Invariant)**: Point to `EventLocationId` rather than unmediated physical venues, guaranteeing that session schedules always inherit and obey event-level disclosure rules.

### D. Authorization Engine (Cerbos + Local Fallback)
1. **Declarative Attributes**: Handlers and endpoints are tagged with `[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]`.
2. **CQS Decorator Pipeline**: `AuthorizationCommandHandlerDecorator` and `AuthorizationQueryHandlerDecorator` intercept requests before the handler runs.
3. **Provider Cascade**:
   - Checks **Tenant BYO Cerbos** if configured by the tenant.
   - Falls back to **Instance Cerbos PDP** (gRPC).
   - Falls back to **Local RBAC (`FallbackAuthorizationService`)** if Cerbos is unreachable or in local development mode.
4. **HATEOAS Link Policies**: When serializing responses, `ResourceAssemblerBase` evaluates candidate action links in a single batch against Cerbos and attaches allowed action URLs to `_links`.

### E. Transactional Outbox Pattern
To guarantee consistency without distributed transactions (2PC):
1. Any command that generates an external effect (e.g. `CreateEventCommandHandler`) writes an `OutboxMessage` row in the same database transaction.
2. The HTTP request returns immediately (fast response time).
3. `Quartz.NET` background scheduler polls pending outbox records, locks them, deserializes the JSON payload, and calls the appropriate dispatcher (Email, Webhooks, Federation sync).
4. Handles automatic retries with exponential backoff and dead-letter queues.

### F. Multi-Tier Caching
1. **Tier 1 (HTTP Output Cache)**: Response-level caching in `Explore.API` for public listings (`30s`), lookup tables (`1h`), and detail pages (`60s`), varying by `Authorization` header.
2. **Tier 2 (Application HybridCache)**: L1 in-memory + L2 distributed (Redis) cache used within native CQS handlers with deterministic cache keys generated by `ToCacheKeySuffix()`.
3. **Tier 3 (Conditional ETag Middleware)**: Computes SHA256 weak ETags on JSON responses, returning `304 Not Modified` to save bandwidth.

---

## 5. Summary & Further Reading

- To see the step-by-step code and network flow for specific operations, read [REQUEST_FLOWS.md](REQUEST_FLOWS.md).
- To start implementing new features or bug fixes, follow the blueprints in [CONTRIBUTOR_RECIPES.md](CONTRIBUTOR_RECIPES.md).
- For strict coding standards and invariants, consult [QUICK_REFERENCE.md](QUICK_REFERENCE.md).
