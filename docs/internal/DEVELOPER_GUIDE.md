ABOUTME: Primary orientation and mental model guide for human contributors.
ABOUTME: Explains the big picture, role-based learning paths, and the 8 unbreakable system invariants.

# Developer Orientation & Mental Model Guide

> **Audience:** Contributors | Developers | Evaluators | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-08-16
> **Source Anchors:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/REQUEST_FLOWS.md`, `docs/CONTRIBUTOR_RECIPES.md`, `docs/QUICK_REFERENCE.md`

Welcome to **ISLAMU Event**! If you are a developer looking to contribute to this codebase without needing an AI agent or getting lost in 80+ reference documents, this guide is your starting point.

---

## 1. The 5-Minute Mental Model

ISLAMU Event is a self-hostable, multi-tenant event management and discovery platform built with **.NET 10**, **C# 13**, and **Blazor WebAssembly**. 

```
                               ┌─────────────────────────────┐
                               │   Browser (Client WASM)     │
                               │   Interactive UI & Pages    │
                               └──────────────┬──────────────┘
                                              │ HTTP / Cookies
                                              ▼
┌─────────────────────────┐    ┌─────────────────────────────┐    ┌─────────────────────────┐
│   Keycloak (OIDC)       │◄───┤  Explore.Blazor (BFF Host)  │───►│   Cerbos PDP (Authz)    │
│   Identity & Auth       │    │  Auth Session & YARP Proxy  │    │   Fine-Grained Policies │
└─────────────────────────┘    └──────────────┬──────────────┘    └────────────┬────────────┘
                                              │ HTTP + Bearer Token            │
                                              ▼                                │
                               ┌─────────────────────────────┐                 │
                               │   Explore.API (Backend)     │◄────────────────┘
                               │   Middleware + Native CQS   │
                               └──────────────┬──────────────┘
                                              │ EF Core (Multi-Tenant)
                                              ▼
                               ┌─────────────────────────────┐
                               │   PostgreSQL / SQLite / DB  │
                               │   Data + Outbox Tables      │
                               └──────────────┬──────────────┘
                                              │ Polling / Dispatch
                                              ▼
                               ┌─────────────────────────────┐
                               │   Quartz / Outbox Worker    │
                               │   Emails, Webhooks, Sync    │
                               └─────────────────────────────┘
```

### Core Architecture Concepts
1. **Clean Architecture with Inward Dependencies**:
   - `Explore.Domain`: Entities and business rules (zero external dependencies).
   - `Explore.Application`: Native CQS commands/queries, handlers, validators, DTOs, and repository interfaces.
   - `Explore.Persistence` & `Explore.Infrastructure`: EF Core DbContext, repositories, S3 storage, email delivery, and external integrations.
   - `Explore.API`: The backend host, controllers, middleware pipeline, and composition root.
2. **Backend-for-Frontend (BFF) Pattern**:
   - The browser never stores raw API tokens in `localStorage`.
   - `Explore.Blazor` (Server) handles OIDC login with Keycloak and stores tokens in secure, encrypted HTTP-only session cookies.
   - It proxies API requests to `Explore.API` via YARP, injecting the Bearer token and `X-Tenant-Slug` header automatically.
   - `Explore.Blazor.Client` (WebAssembly) runs in the browser and calls endpoints using a generated typed client (`IEventApiClient`).
3. **HATEOAS / HAL-Driven UI Affordances**:
   - The frontend **never** inspects user roles or permissions to show or hide "Edit" / "Delete" buttons.
   - The API evaluates Cerbos permissions on the fly and embeds hypermedia affordances (`_links.edit`, `_links.delete`) in the response JSON.
   - If `_links.edit` exists, the UI renders the Edit button; if absent, the button is not rendered.
4. **Multi-Tenancy by Default**:
   - Every tenant data query is automatically isolated at the database level using EF Core named global query filters (`TenantId == CurrentTenantId`).
5. **Transactional Outbox Pattern**:
   - Side effects (emails, webhooks, search indexing, federation sync) are never executed inline during HTTP requests.
   - Instead, an `OutboxMessage` row is saved in the exact same database transaction as the entity mutation, and background workers (Quartz) dispatch them reliably.

---

## 2. Role-Based Onboarding Roadmaps

Depending on what you want to work on, follow these curated paths:

### 🅰️ Backend Developer Roadmap
If you are adding business logic, database entities, background workers, or API endpoints:
1. **Read Core Architecture**: [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) — understand the Clean Architecture layers and dependency directions.
2. **Understand Execution Flows**: [REQUEST_FLOWS.md](REQUEST_FLOWS.md) — see how Commands and Queries flow through native CQS handlers and EF Core.
3. **Follow the Blueprints**: [CONTRIBUTOR_RECIPES.md](CONTRIBUTOR_RECIPES.md) — step-by-step recipes for creating entities, CQRS handlers, and API controllers.
4. **Learn EF Core Invariants**: [CODEBASE_INSIGHTS.md](CODEBASE_INSIGHTS.md#2-dbcontext-pooling-property-injection-and-partial-class-decomposition) — understand pooled DbContext factory, property injection, and named query filters.
5. **Testing**: [TESTING.md](TESTING.md) — write unit tests with TUnit and integration tests with Testcontainers.

### 🅱️ Frontend / Blazor UI Developer Roadmap
If you are building pages, dialogs, forms, or navigation in Blazor:
1. **Read UI Architecture**: [BLAZOR.md](BLAZOR.md) — understand the Blazor Server BFF + WASM Client hybrid architecture.
2. **Master Design System & CSS**: [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) — learn the 6-tier CSS `@layer` system, `var(--isl-*)` tokens, and MudBlazor component wrappers.
3. **Understand Action Affordances**: [REQUEST_FLOWS.md](REQUEST_FLOWS.md#flow-2-query-read--hateoas-affordance-flow) — learn how HAL `_links` control button visibility.
4. **DTO Sync Workflow**: [CONTRIBUTING.md](CONTRIBUTING.md#dto-change-workflow-api--blazor-client) — understand how the NSwag generated client (`IEventApiClient`) stays in sync with API OpenAPI schemas.

### 🅲 Full-Stack Contributor Roadmap
1. Start with this guide and [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md).
2. Trace [REQUEST_FLOWS.md](REQUEST_FLOWS.md) Flow 1 (Command) and Flow 2 (Query).
3. Use [CONTRIBUTOR_RECIPES.md](CONTRIBUTOR_RECIPES.md) to implement your feature across all layers from Domain to Razor components.
4. Verify your change using the [Pull Request Checklist](CONTRIBUTING.md#pull-request-checklist).

---

## 3. The 8 Unbreakable Invariants ("Gotchas")

These are the strict architectural rules enforced by CI and architecture tests. Violating them will break the build or fail code review:

| # | Rule | Why It Matters | What to Do Instead |
|---|---|---|---|
| 1 | **Repositories return Entities, NEVER DTOs** | Repositories belong to persistence/domain boundaries; DTO projection belongs in Application handlers. | Return `Event`, map to `EventDto` inside `GetEventDetailsRequestHandler`. |
| 2 | **Validators validate DTOs and are manually instantiated** | FluentValidation validators live under `Explore.Application/DTOs/<Entity>/Validators/`, inherit `AbstractValidator<TDto>`, and are never in DI. Commands wrap the DTO (`request.EventDto` / `request.Dto`). | In handler: `var validator = new CreateEventDtoValidator(...); var result = await validator.ValidateAsync(request.EventDto, ct);`. |
| 3 | **UI buttons are gated by HAL `_links` presence** | Security decisions stay on the server. Clients must never inspect JWT claims/roles to toggle UI controls. | Use `@if (eventDto.Links?.ContainsKey("edit") == true) { <MudButton ... /> }`. |
| 4 | **DbContext uses Property Injection for Scoped Services** | `ExploreDbContext` uses a pooled context factory for performance. Constructor injection of scoped services fails. | Properties `TenantContext` and `CurrentUserService` are set post-resolution; always handle potential `null` during migrations. |
| 5 | **Strict Primary Key Types** | ID types must follow domain standards across all tables. | Use `Guid` (UUIDv7) for aggregate roots, `int` for static lookup tables, `long` for outbox cursors. |
| 6 | **Never Hand-Edit EF Core Migrations** | Migrations and model snapshots are generated compiler artifacts. | Modify entity configuration, then run `dotnet ef migrations add <Name>`. |
| 7 | **Strict CSS `@layer` & Logical Properties** | CSS isolation prevents style collisions across MudBlazor and custom components. | Never use bare `.mud-*` selectors outside `mudblazor-overrides.css`; use CSS logical properties (`margin-inline-start` instead of `margin-left`). |
| 8 | **DTO Change Sequence (API → Client)** | The Blazor WASM client generates API client code from `schemas/openapi_islamu-event.json`. | 1. Update Application/API DTOs<br>2. Build `Explore.API` (regenerates OpenAPI schema)<br>3. Build `Explore.Blazor.Client` (regenerates NSwag client)<br>4. Update Razor pages. |

---

## 4. Codebase Navigation Compass

Here is where the most common code files live in the repository:

```
src/
├── Explore.Domain/                    # Domain Layer
│   ├── *.cs                           # Aggregates (Event.cs, Organization.cs, User.cs)
│   ├── Enums/                         # Status, type, and format enums
│   ├── Interfaces/                    # Marker interfaces (ITenantEntity, IAuditableEntity, ISoftDeletable)
│   └── OutboxMessage.cs               # Transactional outbox entity
│
├── Explore.Application/               # Application Layer
│   ├── DTOs/                          # Data Transfer Objects (e.g., DTOs/Event/CreateEventDto.cs)
│   │   └── <Entity>/Validators/       # DTO Validators (e.g., CreateEventDtoValidator.cs)
│   ├── Features/                      # CQRS Slices grouped by aggregate (Commands/Queries/Handlers)
│   │   └── Events/                    # e.g., Requests/Commands/CreateEventCommand.cs, Handlers/...
│   └── Contracts/                     # Interfaces (Persistence, Infrastructure, Outbox)
│
├── Explore.Persistence/               # Persistence Layer
│   ├── ExploreDbContext.cs            # DbContext (split into .DbSets.cs, .QueryFilters.cs, .SaveChanges.cs)
│   ├── Configurations/                # EF Core entity type configurations
│   └── Repositories/                  # Concrete repository implementations
│
├── Explore.Infrastructure/            # Infrastructure Layer
│   ├── Email/                         # SMTP & Mailpit email dispatcher
│   ├── Storage/                       # S3 / MinIO / Local storage service
│   └── Webhooks/                      # Svix & local webhook dispatchers
│
├── Explore.API/                       # API Host (Composition Root)
│   ├── Controllers/                   # REST API Controllers (EventsController.cs, etc.)
│   ├── Middleware/                    # Tenant resolution, HATEOAS, rate limiting, exception handling
│   ├── Hateoas/                       # Resource assemblers and HAL link builders
│   └── Program.cs                     # API startup & middleware pipeline configuration
│
├── Explore.Blazor/                    # Blazor Server Host (BFF)
│   ├── Endpoints/                     # Auth login/logout and session management
│   ├── Program.cs                     # YARP proxy and OIDC configuration
│   └── App.razor                      # Host HTML shell
│
└── Explore.Blazor.Client/             # Interactive UI Client (Blazor WebAssembly)
    ├── Pages/                         # Routable pages (Events/EventList.razor, EventDetail.razor)
    ├── Components/                    # Reusable UI components & dialogs
    └── Services/                      # Typed API client wrappers & UI state managers
```

---

## 5. Next Steps

- **Want to understand how components interact?** Continue to [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md).
- **Want to trace real execution flows?** Read [REQUEST_FLOWS.md](REQUEST_FLOWS.md).
- **Ready to write code?** Follow the recipes in [CONTRIBUTOR_RECIPES.md](CONTRIBUTOR_RECIPES.md).
- **Need local setup instructions?** See [GETTING_STARTED.md](GETTING_STARTED.md).
- **Changing configuration portability?** Follow
  [CONFIGURATION_MANIFEST.md](CONFIGURATION_MANIFEST.md#contributor-guide). The
  closed registry, schema generator, OpenAPI inventory, and generated NSwag
  client must move together; do not hand-edit generated contracts or add a
  compatibility reader for v1alpha1.
