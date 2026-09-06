ABOUTME: System architecture summary for the current codebase, not a theoretical template.
ABOUTME: Captures key runtime patterns and boundaries that are not obvious from one file.

# Technical Architecture

> **Audience:** Contributors | Architects | Operators | Integrators | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-03
> **Source Anchors:** `src/Explore.API/Program.cs`, `src/Explore.Application/`, `src/Explore.Domain/`, `src/Explore.Persistence/`, `src/Explore.Infrastructure/`, `src/Explore.Blazor/`, `src/Explore.AppHost/`

## System Profile & Codebase Scale (2026-09-03 Checkpoint)

| Metric / Dimension | Value as of 2026-09-03 | Architectural Context |
|---|---|---|
| **Runtime & SDK** | .NET 10 (`net10.0`) | Pinned preview SDK in `global.json`, C# 13 features |
| **Architectural Style** | Clean Architecture + CQRS + BFF | Compile-enforced assembly boundaries + MediatR pipeline |
| **Backend Production LOC** | **500,837 LOC** | API 73k, Application 224k, Domain 55k, Persistence 81k, Infrastructure 67k |
| **Frontend / Client LOC** | **378,000 LOC** | `Explore.Blazor.Client` (incl. 182,524 LOC generated `EventApiClient.g.cs`) |
| **Feature Slices** | **126 slices** | CQRS vertical slices under `Explore.Application/Features/` |
| **Controllers** | **172 controllers / 32,418 LOC** | Clean REST endpoints; route capability partitioning |
| **CQRS Handlers** | **625 handlers / 64,926 LOC** | Average 104 LOC per handler; command & query separation |
| **Test Suite Scale** | **32 test projects / 1,783 test files** | **455,176 total test LOC**; backend-only test LOC: **338,497** |
| **Backend Test:Code Ratio** | **≈ 0.68** (338.5k test / 500.8k prod) | Comprehensive unit, integration, contract, and benchmark coverage |
| **Architecture Tests** | **89 rule files / 22,792 LOC** | NetArchTest guardrails enforcing conventions and boundaries |
| **Database Engines** | 5 supported providers | PostgreSQL, SQLite, SQL Server, MariaDB, MySQL via EF Core |
| **Migrations** | Generated migrations × 5 providers | Independent histories across application, data protection, and privacy |
| **Tenant Isolation** | 339 EF Core global query filters | Governed bypass catalogue (`TenantFilterBypassReasons.cs`, 8.6 KB) |
| **Authorization** | Cerbos PDP + local fallback | 61 policy YAMLs + SafeMode latch + batch link planning |
| **Durable Outbox** | 6 specialized outboxes | Email, WebPush, IntegrationSync, Webhook, PdsSync, PolicyChange |
| **Health Checks** | 26 health-check classes | Storage, database, Redis, Cerbos, recovery, Quartz readiness |
| **Observability** | OpenTelemetry via Aspire Defaults | 6 named meters, 5 named trace sources, Prometheus scraping |
| **Configuration Validation** | 107 `ValidateOnStart` registrations | Fail-closed startup options validation via `IValidateOptions` |
| **Secrets Management** | Dedicated `Explore.Secrets` (48 files) | Infisical external provider, replica convergence, rotation-aware factories |
| **Caching** | `HybridCache` (100 files) + Output Cache | L1 memory + L2 Redis with tag-based invalidation (96 endpoints) |
| **Rate Limiting** | 69 ASP.NET rate-limit policies | Granular per-endpoint sliding/fixed/token window policies |
| **Job Scheduling** | Quartz.NET clustered store | 15 scheduled recurring jobs, `QRTZ_` tables on all 5 providers |
| **CI/CD Workflows** | 21 GitHub Actions workflows | SHA-pinned actions, locked NuGet restore, 150 centrally pinned packages |
| **Code Hygiene** | **0 TODO / 0 FIXME / 0 HACK** | 0 `[Obsolete]`, 0 `NotImplementedException`, ~95% ABOUTME coverage |
| **ADRs Documented** | 26 ADRs | Complete architectural decisions recorded in `docs/internal/adr/` |

## Ticketing Recovery And Deployment Capability Authority

Ticketing recovery follows a durable state-machine pattern rather than
inferring readiness from process startup. `TicketingRecoveryManifest` is an
immutable tenant-qualified value; `TicketingRecoveryCheckpoint` is the
serializable persisted authority. Reopening follows
`RecoveryOnly -> Validated -> AuthorityRotated -> WorkersOpen -> SalesOpen`.
The persistence adapter advances admission capability/credential generations,
writes one reissue intent per ticket, and fences queue work in the same local
transaction. Provider calls remain outside that transaction; in-flight work is
`Unknown` until an authenticated operator resolves it against provider truth.

`ITicketingRecoveryOperatorStore` is the Application port.
`TicketingRecoveryRepository` is the EF Core adapter.
`TicketingRecoveryOperatorService` coordinates the existing Quartz
`ISchedulerOperations` port, while `TicketingRecoveryHealthCheck` exports only
closed status/count/age dimensions. Typed configuration and its validator live
in `Explore.Secrets`; key material comes only from Infisical or environment.

Deployment approval is a separate read model. The embedded
`Deployment/ticketing-capabilities.json` artifact is the single machine source
of truth. `TicketingDeploymentCapabilityCatalog` validates its closed status
vocabulary and refuses any non-disabled protected delayed-payout entry.
Application maps it to immutable DTOs, the API publishes one anonymous
read-only endpoint, and Blazor consumes only the generated API client. Browser
code displays server-owned status and never promotes `test-only` locally.

## Hosting: Topology

There are three application composition roots: `Explore.API` owns the Split API host, `Explore.Blazor` owns the Split BFF host, and `Event.Standalone` owns the optional Combined host. `Explore.AppHost` is the local Aspire orchestrator that selects and wires those hosts.

`Hosting:Topology` selects the AppHost deployment topology. `Split` is the default when the setting is omitted; `Standalone` is an explicit optional mode, and invalid values fail at startup.

| Topology | Processes and localhost endpoint | Startup and readiness ownership |
|---|---|---|
| Split default | `Explore.API` at `https://localhost:7039` and `Explore.Blazor` at `https://localhost:7177` | AppHost waits for migrations before both hosts and makes the BFF wait for API readiness. Each host owns its own process startup. |
| Standalone / Combined | one `Event.Standalone` process at `https://localhost:7180`; AppHost also publishes a dynamic internal HTTP endpoint | AppHost waits for migration completion and selected infrastructure; the combined root then starts API initialization before Blazor initialization and owns API workers, health, and shutdown exactly once. The Combined BFF profile does not register YARP or remote-API readiness. |

AppHost exposes the optional Standalone HTTP endpoint through
`WithHttpEndpoint(name: "http")` (dynamic/non-guaranteed internal HTTP), with HTTPS canonical on
`https://localhost:7180`. Direct `Event.Standalone` launch profiles reserve
`http://localhost:5180` (and `https://localhost:7180` for the HTTPS profile).

In the Combined topology, browser `/api/*` requests stay in one process. The BFF classifies its cookie session only to enforce antiforgery, obtains the server-held access token, strips untrusted privileged headers, and dispatches through the in-process API transport. API `MultiAuth` revalidates the bearer token and remains the sole controller principal; no loopback or YARP self-proxy is used. Requests without a valid BFF session keep the existing external bearer/API-key API flow.

To roll back a topology change, relaunch AppHost with `Hosting:Topology=Split` (or omit it); this does not roll back data or migrations. Standalone deliberately does not select SQLite or add a standalone `docker-compose.yml` deployment. Canonical controller paths remain `/api/...`; version negotiation uses media type, `?api-version=`, or `X-Api-Version`, never `/api/v1/...`.

Split/Standalone is a process-composition choice only: it changes where BFF and API execute, not API contracts, authorization policy, token semantics, or versioning. The API keeps canonical `/api/*` versioning through non-URL headers and query values (`Accept: application/json;v=...`, `api-version`, `X-Api-Version`), and Standalone never adds route-based version segments.

| API versioning input | Support | Notes |
|---|---|---|
| `/api/...` plus `Accept` media-type parameter, `?api-version=`, or `X-Api-Version` | Supported | The same API pipeline parses these in Split and Standalone. |
| `/api/v1/...`, `/api/v0.1/...`, or any topology-specific versioned route | Unsupported | URL version segments are never added; routes and HAL links remain canonical. |

Container packaging is explicit. The repository `docker-compose.yml` describes the Split deployment; Standalone is the single `Event.Standalone` image run directly with an env file and defaults to SQLite. AppHost remains the local topology selector. Selecting Standalone through AppHost does not automatically change the database provider; database selection always remains an explicit structured provider contract.

## Layer Boundaries & Compile-Time Enforcement

The platform enforces Clean Architecture through **separate .NET project assemblies** with compile-time boundary enforcement, reinforced by 89 architecture test files (22,792 LOC):

1. **`Explore.Domain` (55k LOC)**: Entities, enums, domain rules, state machines, and lifecycle specifications. References only `Event.Wire.Contracts`.
2. **`Explore.Application` (224k LOC)**: CQRS requests/handlers (MediatR), DTOs, FluentValidation validators, application contracts. Depends only on `Explore.Domain`. Cannot reference Persistence, Infrastructure, or API.
3. **`Explore.Persistence` (81k LOC) + `Explore.Infrastructure` (67k LOC)**: EF Core DbContext, repositories, provider primitives, migrations, email, storage, Keycloak, webhooks. Depend on `Explore.Application`.
4. **`Explore.API` (73k LOC)**: REST API host, composition root for all backend layers, HTTP middleware, controllers, Swagger/OpenAPI.
5. **`Explore.Blazor` & `Explore.Blazor.Client` (378k LOC)**: Isolated BFF server and WebAssembly client. The client has **zero** source or project dependencies on Domain, Application, or Persistence; its backend boundary is strictly the generated `IEventApiClient` contract (182,524 LOC).

### Compile-Time vs Test-Time Enforcement
In many frameworks, Clean Architecture layers are merely logical packages or folders within a single project module, requiring reflection-based test rules (like ArchUnit) to detect layer leaks. In ISLAMU Event, each layer is a distinct compiled .NET assembly. If a developer accidentally attempts to access a persistence repository from `Explore.Domain` or an internal domain entity from `Explore.Blazor.Client`, the code **cannot compile**. Compile-time gating gives instant feedback on every keystroke in the IDE.

### Intentional Unified Domain & Persistence Model
Unlike purist architectures that maintain two distinct class hierarchies (Domain Aggregate Roots vs ORM Entities) connected by hundreds of mappers, ISLAMU Event **intentionally unifies domain entities and EF Core persistence entities** in `Explore.Domain`. Domain entities contain EF Core mapping annotations and navigation properties while directly housing rich behavior (`Publish`, `Cancel`, `ApplyScheduleTimeZone`, `RecalculateScheduleSummaryFromSessions`) and delegating to domain rule engines. This deliberate design eliminates 200+ duplicate mirror classes and 140+ mappers, enables native LINQ expressions and query projections directly from domain entities, and powers 339 EF Core global query filters without translation layers. See full rationale in [DOMAIN.md](DOMAIN.md#architectural-rationale-unified-domain--persistence-model-pragmatic-ddd).

The API dependency direction is inward: API -> Infrastructure/Persistence -> Application -> Domain. Blazor has no project or source dependency on those layers; its backend boundary is the generated API client.

## Primary Relational Namespace

The provider-specific EF composition boundary owns physical names. PostgreSQL
and SQL Server apply `Database:Schema` (default `islamu_event`) and retain clean
table names. SQLite, MariaDB, and MySQL apply the fixed non-configurable `ie_`
prefix; deployment instances isolate through distinct SQLite files or
MariaDB/MySQL databases. Quartz scheduler state is co-located in the same
database under the `QRTZ_` table prefix on every provider, created by idempotent
DDL rather than an EF Core migration.

## Persistence Capability Ladder

Persistence code follows one ordered capability ladder:

1. **EF Core LINQ and set-based APIs** are the default for reads and mutations.
   Repositories stay provider-neutral and return Domain entities or
   domain-owned atomic outcomes.
2. **Public provider APIs and translations** are used only when EF Core cannot
   express the required database behavior portably.
3. **Provider primitives** isolate the remaining capability gap. Parameterized
   SQL, provider-name checks, lock commands, and migration-generator coupling
   live only under `Explore.Persistence/Database/ProviderPrimitives` or
   `Explore.Persistence/Schema/ProviderPrimitives`.

`EFCore.NamingConventions` owns ordinary snake-case identifiers.
`RelationalModelNamespace` applies the configured PostgreSQL/SQL Server schema
or the fixed `ie_` SQLite/MariaDB/MySQL prefix. Finalized EF metadata, rather
than source literals, resolves tables, columns, keys, indexes, and constraints.
`MySqlModelIdentifierPolicy` shortens identifiers over 64 characters with a
stable hash so generated MariaDB and MySQL names remain distinct.

The approved primitive set includes transaction/session named locks, row
fences, skip-locked queue claims, projection locks, provider database clocks,
and configurable-schema migration generators. Adding another exception
requires a demonstrated EF capability gap, a parameterized implementation,
real-engine tests for every affected provider, an architecture allowlist, and
operator documentation. Ordinary repository branching or raw SQL is not an
exception path.

Application and Data Protection migrations have independent generated
histories for all five providers. Retained privacy-erasure authority has its
own topology-specific histories. Migration and model-snapshot files are
generated artifacts: change model/configuration code, then regenerate; never
patch generated migration C# manually.

ATProto transient authentication adds a deliberately instance-owned persistence
boundary, separate from tenant-filtered business entities and the generic
idempotency cache. Its encrypted payload and assertion-replay tables use
immutable rows, unique digest claims and candidate-qualified conditional
deletion. [ADR-014](adr/ADR-014-atproto-session-trust-bridge.md#relational-transient-storage-authority)
owns the tenant-binding, ciphertext, expiry and uncertain-consume contract.

## Request Flow
1. HTTP request enters the middleware pipeline (exception handling → security headers → correlation ID → logging → compression → HATEOAS → routing → timeouts → auth → rate limiting → authorization → output cache → ETag → idempotency).
2. Controller receives request, dispatches MediatR command/query.
3. MediatR pipeline behaviors execute: `PerformanceBehavior` (>500ms warning), `AuthorizationBehavior` (resource-level permission checks via `IAuthorizedRequest` / `[AuthorizeResource]`; uses reflection caching and emits OpenTelemetry activity spans).
4. Handler orchestrates validation (manually instantiated validators), repository calls, mapping.
5. Persistence layer returns entities; handlers map to DTO/response contracts.
6. Controller delegates to an `IResourceAssembler<TDto, TListDto>` — by default the generic `HalResourceAssembler<TDto, TListDto>` over `ResourceAssemblerBase` — for HATEOAS HAL wrapping with authorization-aware link generation.

## BFF Model (Blazor)
1. Browser authenticates via OIDC through BFF endpoints.
2. Session/cookie state remains in server-controlled flow.
3. BFF forwards API calls to backend (token forwarding + tenant header propagation where needed).
4. `Explore.Blazor.Client` focuses on UI and typed service calls; it is not a token authority.
5. Backend/domain payloads in every Blazor runtime and test project use generated `IEventApiClient` models, never locally mirrored Application or Domain types.
6. BFF-owned security state such as cookies, antiforgery, circuit tokens, and Data Protection keys remains inside the BFF and does not grant access to API persistence.

## Multi-Tenancy Model
1. Runtime mode is resolved from governance settings (`SingleTenant` / `MultiTenant`).
2. In `SingleTenant`, default tenant is used for all requests.
3. In `MultiTenant`, tenant is resolved from header/domain/subdomain fallback chain.
4. EF query filters enforce tenant isolation centrally in `ExploreDbContext`.
5. **Hierarchical Settings**: Governance settings follow a 5-tier resolution cascade: User → Group → Organization → Tenant → Instance. Resolution is performed in batch via `HierarchicalSettingsResolver` with support for instance-level locks and single-tenant bypass.

## Authorization Architecture
1. Endpoint-level auth is handled via ASP.NET attributes/policies. `[AuthorizeResource]` attribute pairs a resource kind with a domain action constant from `AuthorizationActions`.
2. Resource-level auth is handled in the `AuthorizationBehavior` MediatR pipeline. Checks route to `IAuthorizationProvider` which resolves to Cerbos PDP or local fallback.
3. `AuthorizationActions` (string constants) and `ResourceKinds` (string constants) form the canonical action/resource catalogs shared by commands, link policies, and Cerbos policies.
4. `IAuthorizableResourceDescriptor<T>` + `ResourceDescriptors` extract resource metadata (kind, id, attributes, scope) from DTOs — eliminating manual attribute dictionaries in HATEOAS link policies.
5. HATEOAS capability planning uses a 4-phase pipeline: candidate links → normalized `AuthorizationCheck` with dedup key → batch evaluate unique checks → map decisions back to links. Fail-closed on batch failure.
6. Runtime authorization provider routes checks by configuration: tenant BYO Cerbos first, otherwise the instance provider setting. Instance and BYO Cerbos failures deny rather than falling through to local RBAC; local fallback is used only when local mode is selected.
7. SafeMode is a provider-instance latch via `ActivateSafeMode()` — once activated for BYO PDP/resolver failures, only instance-admin traffic is allowed through that fallback provider instance. Recreate the provider instance after the PDP/configuration recovers to leave safe mode. Logs `LogCritical` once.
8. Cerbos policies reference JSON schemas (`_schemas/`) for principal and resource attribute contracts. Schema enforcement is `warn` by default.

## API Representation
1. HAL/HATEOAS wrappers are used for discoverable responses.
2. `Prefer: return=minimal` can reduce link payload where clients do not need hypermedia.
3. OpenAPI is exposed in development for inspection and generated at build time for client generation.
4. API-local OpenAPI transformers and Swashbuckle transition filters adjust schemas to reflect HAL structure.
5. API versioning is read from three non-URL sources combined (`ApiVersionReader.Combine`): media-type parameter (`Accept: application/json;v=0.1`), query string (`?api-version=0.1`), and custom header (`X-Api-Version: 0.1`). URL-segment versioning (e.g. `/api/v0.1/controller`) is intentionally unsupported — every endpoint has exactly one canonical path so that `operationId`, `RouteNames`, and HAL link generation stay stable across versions.

## MCP Adapter Boundary
1. The initial Model Context Protocol adapter is an optional `Explore.API` presentation adapter, not a new authority for AI tools.
2. MCP hosting uses ASP.NET Core Streamable HTTP through the official C# MCP SDK, with stateless transport selected explicitly.
3. The adapter is mapped by default through `Mcp:Enabled=true` at `/mcp`; self-hosted deployments can still unmap it with startup `Mcp:Enabled=false` or disable it at runtime with `mcp.enabled=false`.
4. MCP tools, resources, and prompts must be registry-backed. Tool definitions and JSON schemas come from `IAiToolContractRegistry`; first-class projected MCP proposal tools add only the `conversationId`/`summary` envelope around registry payload fields, and mutating tools follow the existing proposal/confirmation path.
5. MCP endpoint mapping is anonymous at the transport edge so official SDK authorization filters can expose only explicitly anonymous-safe registry discovery. Scoped tools, resources, prompts, proposals, and conversation data remain tenant-resolved and authenticated through `[Authorize]`, API-key/bearer principals, MediatR authorization, and HAL/API confirmation; no key or an invalid key can use only anonymous-safe capabilities.
6. Stateful MCP sessions, runtime legacy SSE transport, sampling, elicitation, roots, completions, progress/list-changed notifications, resource subscriptions, and client-specific compatibility shims are ADR-gated protocol changes, not incidental adapter tweaks.
7. MCP logs, health, metrics, and errors must not expose prompts, provider responses, tool payloads, tenant IDs, provider endpoint URLs, API keys, or raw provider exceptions.

See [ADR-010](adr/ADR-010-mcp-adapter-hosting-strategy.md) for the hosting, transport, auth, tenancy, and disable-path decision.

## Specification Pattern (Query Composition)
Complex filtering uses a custom Specification Pattern:
1. `IQuerySpecification<T>` composes `IFilterSpecification<T>` + `ISortSpecification<T>` via immutable builder.
2. `EventQuerySpecification` chains filters using AND composition: direct filters (`EventFilter`), subquery filters for junction tables (`EventSubqueryFilter`), module-conditional aspect filters (`IslamicAspectFilter`, `TechAspectFilter`), presence filters (`AspectPresenceFilter`), and projection-backed custom-property filters (`EventCustomPropertyProjectionFilter`).
3. Layer 2 typed aspect filters compose through explicit `EventQuerySpecification.And(...)` overloads. Do not route sector-standard filters such as madhab, gender mode, prayer-relative timing, tech skill level, or aspect presence through Layer 3 custom-property projections.
4. Layer 3 projection filters stay generic (`ExactMatch`, `TextSearch`, option, range, boolean, and existence checks) and only target governed custom-property projection rows.
5. Filters are applied to `IQueryable<T>` in the repository — module-specific filters are silently ignored when modules are disabled.
6. `ToCacheKeySuffix()` generates deterministic cache keys from active filter/sort state.

## Event Data Layers
1. Layer 1 stores universal semantics directly on `Event`, `EventSession`, and other first-class related entities.
2. Layer 2 stores sector-standard semantics in typed 1:1 schema such as `EventIslamicAspect`, `EventTechAspect`, and `EventSessionIslamicAspect`.
3. Layer 3 stores tenant-specific long-tail extensions through governed custom-property entities and event/session template/runtime rows.
4. `Event` is the event/program container; `EventSessionGroup` organizes tracks, devrooms, stages, and program sections; `EventSession` is the scheduled content item for talks, workshops, panels, classes, and activities; `EventAgendaItem` covers logistics such as breaks, meals, prayer slots, and transitions.
5. Layer 3 must not redefine or replace Layer 2 semantics; reserved namespaces and collision rules are part of the custom-properties architecture.
6. If a custom property becomes sector-standard or discovery-critical, promote it into typed Layer 2 schema instead of adding sector-specific factories to `EventCustomPropertyProjectionFilter` or `EventSessionCustomPropertyProjectionFilter`.
7. `EventCustomPropertyProjection`, `EventSessionCustomPropertyProjection`, and aggregate event-with-sessions read views are derived query models only; source of truth remains typed schema plus event-local and session-local custom-property rows.

## Proximity Discovery Status

Current public discovery is area-only. Tenant configuration may publish stable area IDs and coarse centroids, but the runtime does not expose exact venue points, calculate event distance, or support “near you” semantics. `LocationPii` remains the private source for address-derived coordinates and generic location DTOs remain coordinate-free.

[ADR-013](adr/ADR-013-postgis-proximity-discovery.md) records a **proposed, separately approved** PostGIS phase. That phase would add an explicitly governed tenant-scoped `LocationDiscoveryPoint` using `geography(Point,4326)`, a GiST-backed `ST_DWithin` query, and minimum distance to an eligible future public event-session occurrence. A first-party private/no-store POST would carry a rounded transient origin; origins and points would never enter URLs, settings, logs, traces, analytics, or shared caches.

No PostGIS extension, spatial entity/index, proximity endpoint, or readiness check is implemented today. If the planned capability is later enabled but unavailable, the product falls back to honest area-only behavior; there is no browser, Haversine, or in-memory exact-distance fallback.

## Caching Architecture (3 Layers)
1. **Output Cache** (HTTP response level): `LookupData` (1h), `ListData` (30s, varies by `Authorization` header), `DetailData` (60s, varies by `Authorization` header), `PublicData` (1h, no auth variance). Applied via `[OutputCache]` on endpoints.
2. **HybridCache** (application level, L1 in-memory + L2 distributed): 30min default expiration, 5min local, 10MB max payload. Used in MediatR handlers with read-through and explicit invalidation patterns.
3. **ETag Middleware** (conditional requests): RecyclableMemoryStream-based, SHA256 weak ETags on JSON/HAL responses, returns `304 Not Modified`. Skips bodies larger than 256KB.

## MediatR Pipeline Behaviors
1. `PerformanceBehavior` — logs any request taking >500ms as a warning.
2. `AuthorizationBehavior` — checks `IAuthorizedRequest` interface or `[AuthorizeResource]` attribute. Optionally enhanced by `ISecureRequest` for dynamic resource context. Throws `AuthorizationException` on deny. Uses `ConcurrentDictionary` reflection caching for attribute lookups and emits activity spans via the `Explore.Authorization` ActivitySource for distributed tracing.

## Contract Value Semantics

Handwritten immutable contracts follow the [canonical record-selection policy](GOVERNANCE.md#canonical-record-selection-policy); [RECORD_CONTRACTS.md](RECORD_CONTRACTS.md) is the contributor implementation guide. Concrete Application MediatR requests, immutable DTO/payload snapshots, valid-state command results, and structurally eligible generated browser response/value contracts use record semantics. EF entities, persisted outbox lifecycle rows, generated protocol inputs and HAL/inherited/file/exception shapes, and Blazor edit/component state remain classes. This is a shallow immutability boundary: every published collection-bearing handwritten record exposes a read-only/immutable shape and copies mutable input, while generated records preserve NSwag collection shapes and keep only System.Text.Json extension data settable.

NSwag remains the sole OpenAPI-to-C# source generator. `Explore.Blazor.Client` applies the repository-owned SDK-Roslyn transformer before final byte normalization; it adds no package dependency or copied template and produces byte-identical output for identical input. `GeneratedClientRecordArchitectureTests` ratchets the compiled record/init surface and exact mutable-class manifest alongside the cross-layer `PublishedCollectionContractArchitectureTests`.

HTTP adapters supply the current tenant and user from the authenticated principal, resolved tenant context, authoritative route, or trusted adapter. Body identifiers are retained only when they name the operation's legitimate target and are independently authorized server-side.

## Idempotency
Write operations support `Idempotency-Key` header for safe retries. `IdempotencyMiddleware` caches responses by `(Key, TenantId)` in PostgreSQL and replays them within 24 hours. Entity: `IdempotencyRecord` in Domain layer, persisted via `IIdempotencyRepository`.

## Federation Status
Implemented today:
- AT Protocol confidential-client OAuth for already-linked platform accounts, with a server-private trust bridge and encrypted DID-keyed sessions.
- Database-first event/RSVP publication through immutable `PdsSyncOutbox` intent, fenced leases, stable record keys, retry/reconciliation, and URI/CID settlement.
- One globally leased, exact-collection Jetstream consumer with optional DID curation for canonical community event/RSVP records, tombstones, quarantine evidence, and durable cursor state.
- Internal CQRS import of each visible community event into one tenant-local `Event` and one `EventSession`. The fenced persistence transaction also owns canonical state, tenant presentation, cursor/snapshot settlement, and optional `StorageObject` linkage for a CID-verified thumbnail.
- Lossless accepted-record preservation in `AtprotoRecord.RecordJson`; only semantically compatible lexicon values are promoted into local aggregate fields, while producer-specific and future extensions remain canonical JSON.
- Tenant-governed typed event discovery, safe source HAL, administrator controls, user consent, and delivery-status client surfaces.

Not fully implemented today:
- Complete ActivityPub gateway endpoint surface.
- First-party ATProto PDS/AppView hosting and ActivityPub interoperability expected by third-party federated servers.

## AT Protocol Ownership

1. `Explore.Blazor` owns CarpaNet confidential-client OAuth, protected single-use state, canonical callback/handoff, and the server cookie. PDS credentials and private key material never enter the browser.
2. `Explore.API` owns the server-private bootstrap/session trust boundary, first-party JWT validation, ATProto HTTP/HAL contracts, and hosted-worker registration.
3. `Explore.Application` owns effective capability and self-consent resolution, exhaustive public event/RSVP snapshots, deterministic untruncated description rendering, durable publication planning, and the fenced delivery processor. For inbound events it also owns `ImportAtprotoFederatedEventCommand`, manual FluentValidation, semantic import-plan mapping, and optional thumbnail staging orchestration.
4. `Explore.Persistence` owns encrypted-session metadata persistence, immutable `PdsSyncOutbox` intent, fenced lease/settlement state, globally canonical Jetstream record/quarantine/presentation/cursor state, and atomic synchronization of tenant-local `Event`, implicit `EventSession`, and optional `StorageObject` rows.
5. `Explore.Infrastructure` owns the hardened CarpaNet OAuth/PDS adapters, encrypted session envelope protection, generated-record mapping/validation, the fixed-endpoint two-collection Jetstream client, and CID/MIME/size-verified `com.atproto.sync.getBlob` acquisition through registered storage.
6. Quartz `pds-sync-drain` invokes the Infrastructure one-pass drain for committed outbound intent; the API-hosted Infrastructure `AtprotoJetstreamSubscriber` holds one global fenced lease for canonical inbound materialization.
7. `Explore.Blazor.Client` consumes generated safe DTOs. HAL link presence gates Edit/Delete, federated source/RSVP/retry/sync, and instance-governance actions. Generic tenant-setting controls instead use server-derived `EffectiveSettingDto.CanEdit` and `Reason` metadata for writability and explanation. Neither mechanism inspects local roles or claims, and resource actions are never inferred from source type.

Outbound delivery remains database-first: capability, self-consent, linked session, source version, and `EventLocationDisclosurePurpose.Public` are rechecked immediately before remote I/O. Remote failure changes only delivery state; it never rolls back or deletes the committed local event.

## Location Data Model & EventLocation Disclosure Architecture

Venue visibility and privacy are strictly **event-scoped decisions**, not intrinsic properties of a physical building. The platform uses a three-tier location architecture where raw physical venues are separated from per-event disclosure policies, and session schedules are mediated exclusively through event-scoped location placements.

```mermaid
classDiagram
    class Location {
        +Guid Id
        +Guid TenantId
        +string FullName
        +string Country
        +string City
        +string Timezone
        +int LocationKindId
        +int LocationPrivacyStateId
        +Guid OwnerUserId
    }

    class LocationRoom {
        +Guid Id
        +Guid LocationId
        +Guid TenantId
        +string Name
        +int Capacity
    }

    class LocationPii {
        +Guid LocationId
        +string StreetAddress
        +string PostalCode
        +double Latitude
        +double Longitude
        +DateTime PiiErasedAtUtc
    }

    class EventLocation {
        +Guid Id
        +Guid TenantId
        +Guid EventId
        +Guid LocationId
        +bool IsToBeAnnounced
        +bool ShowVenueName
        +bool ShowCity
        +bool ShowCountry
        +bool ShowRoomName
        +bool ShowStreetAddress
        +bool ShowPostcode
        +bool ShowCoordinates
        +int FullDetailsAudienceId
        +DateTime RevealFullDetailsFromUtc
        +bool NeedsPrivacyReview
        +int PolicyVersion
    }

    class EventSession {
        +Guid Id
        +Guid TenantId
        +Guid EventId
        +Guid EventLocationId
        +Guid RoomId
        +DateTimeOffset StartTime
        +DateTimeOffset EndTime
    }

    class EventAgendaItem {
        +Guid Id
        +Guid TenantId
        +Guid EventId
        +Guid EventLocationId
        +Guid RoomId
        +TimeOnly StartTime
    }

    Location "1" *-- "0..1" LocationPii : 1:1 Shared PK
    Location "1" *-- "0..*" LocationRoom : Sub-venues / Rooms
    Location "1" <-- "0..*" EventLocation : Physical Venue Link (Optional)
    EventLocation "1" <-- "0..*" EventSession : Mediated Location Reference
    EventLocation "1" <-- "0..*" EventAgendaItem : Mediated Location Reference
```

### Architectural Roles & Responsibilities

1. **`Location` (Physical Venue Master Record):**
   - Represents the physical real-world place (building, community center, private home, mosque).
   - Tenant-scoped and reusable across many events over time.
   - Exact address details and geographic coordinates reside in the 1:1 shared-PK extension table `LocationPii`, subject to strict privacy erasure lifecycle (`pii_erased_at_utc`).
   - Sub-venues / rooms reside in `LocationRoom` (e.g. "Main Auditorium", "Room B").

2. **`EventLocation` (Per-Event Association & Disclosure Authority):**
   - First-class aggregate mediating an `Event` and either a physical `Location` (`LocationId != null`) or an explicit To-Be-Announced placeholder (`IsToBeAnnounced = true`, `LocationId = null`).
   - Owns the 7 granular disclosure flags: `ShowVenueName`, `ShowCity`, `ShowCountry`, `ShowRoomName`, `ShowStreetAddress`, `ShowPostcode`, and `ShowCoordinates`.
   - Controls audience gating (`FullDetailsAudienceId`: `Public`, `RegisteredAttendeesOnly`, `TicketHoldersOnly`, `StaffOnly`) and timed reveal (`RevealFullDetailsFromUtc`).
   - Maintains an immutable audit trail (`EventLocationDisclosureAudit`, `EventLocationExactReadAudit`).
   - Two events sharing the same physical venue disclose independently; tightening one event's privacy never affects the other.

3. **`EventSession` & `EventAgendaItem` (Mediation Invariant):**
   - Sessions and agenda items reference **`EventLocationId`** as their canonical location anchor, with an optional sub-room reference (`RoomId`).
   - Database check constraints (`ck_event_session_physical_location_requires_event_location`, `ck_event_agenda_item_physical_location_requires_event_location`) strictly enforce that `location_id IS NULL OR event_location_id IS NOT NULL`. No write path can bypass the event's disclosure authority to attach an unmediated venue.
   - When public or attendee agendas are queried, session locations are dynamically evaluated through the event's `EventLocation` policy before serialization, preventing undisclosed physical addresses or coordinates from leaking to unauthenticated clients.

### Disclosure Evaluation Engine

- `EventLocationDisclosureEvaluator` is pure and synchronous. It takes immutable facts — placement, location, room, effective governance, requester authority, server time — and returns a result. It performs no I/O, so disclosure is decidable and exhaustively testable.
- `IEventLocationDisclosureService` is the only request-scoped authority. `ResolveManyAsync` loads placements, rooms, registration coverage, governance, and batched management authorization within a bounded budget (one query per surface, one batched authorization) and then calls the evaluator per row. Every query handler and projection converges here; `EventLocationDisclosureConvergenceTests` fails the build if a new surface reaches the evaluator directly.
- Two authorities sit beside it because they run without an HTTP requester: `FanoutAttendeeLocationAuthorizationService` resolves one explicit background recipient, and `PublicEventLocationDisclosureEvaluator` supplies fixed public-only authority to federation snapshots. Both feed the same pure evaluator instead of reimplementing disclosure.
- Purpose DTOs (`EventLocationPublicDto`, `EventLocationAttendeeDto`, `EventLocationManagementDto`) have no public constructor. They can only be materialized from a disclosure result whose purpose matches, which makes a contradictory response shape unrepresentable rather than merely discouraged.

## Outbox Pattern

The system uses a transactional outbox for reliable asynchronous event delivery:

1. Domain changes write an `OutboxMessage` to the same database transaction as the business entity change.
2. `OutboxProcessor` (BackgroundService) polls for pending messages, dispatches via `IOutboxMessageDispatcher`, and manages retry/dead-letter lifecycle.
3. Delivery guarantee is **at-least-once** — consumers must be idempotent.
4. Retry uses exponential backoff: `InitialRetryDelaySeconds × 2^retryCount`, capped at `MaxRetryDelaySeconds`.
5. After `MaxRetryCount` exhausted, messages are dead-lettered and remain in the database for manual inspection.
6. Optimistic concurrency via `TryMarkAsProcessing` prevents duplicate processing across workers.

Handlers, controllers, automation executors, and sequence processors create durable intent only. They must not send SMTP, publish RabbitMQ, or schedule Quartz jobs directly. Side effects are owned by approved background workers, scheduler functions, or Infrastructure dispatch components.

Event publication currently writes one general outbox message in the same transaction: the internal `EventPublishedNotificationFanoutRequested` message for subscription fanout. `CompositeOutboxMessageDispatcher` routes it to `EventPublishedNotificationFanoutService`, which creates idempotent durable in-app notifications for eligible active actor subscribers. Retired external `EventPublished` broker rows are not produced and fail closed if encountered.

Notification refresh uses a one-way authenticated SSE endpoint at `GET /api/notification/stream`. The stream emits minimal unread-count refresh hints only; durable `Notification` rows and the authenticated notification APIs remain the source of truth. The Blazor notification bell consumes the stream through browser `EventSource` and keeps polling as fallback.

Specialized outbox variants exist for specific subsystems:
- `PdsSyncOutbox` — AT Protocol federation sync (DID, Collection, RecordKey, PdsHost).
- `PolicyChangeOutbox` — authorization policy change propagation (SettingScope).
- `EmailDispatchOutbox` — Basic Dispatch Mode email delivery state for registration confirmation and future lifecycle email workflows. The selected primary database owns delivery state; Quartz schedules drain execution on every provider, with HostedService available as a scheduler-free trigger. SMTP/RabbitMQ are transports only.
- `IntegrationSyncOutbox` — durable external integration sync intent for Listmonk and future providers. Handlers enqueue provider/resource payload snapshots; Quartz invokes the Infrastructure drain, whose tenant-qualified lease token and observed start time fence every settlement. An unkeyed provider call that may have been accepted is parked for operator recovery instead of retried.
- `WebPushDispatchOutbox` — VAPID web push notification dispatch queue (Endpoint, P256dh, Auth, Retries).
- `IncomingWebhookEffectOutbox` — provider incoming webhook effect reconciliation outbox for Coop callback repairs.

### Lifecycle email delivery architecture

The workstream at `dev/active/email-responsibility-architecture/` separates one recipient occurrence into `NotificationIntent` (business meaning), `NotificationDelivery` (channel authorization/outcome), and `EmailDispatchOutbox` (SMTP execution). The selected primary database remains the only SMTP ledger; the scheduler and RabbitMQ carry pointers only. Application-owned transactions atomically persist all recipient channel rows, while fanout mutations persist one immutable occurrence and a PII-free pointer for a resumable worker.

Report-decision execution adds a separate decision-owned durability seam. Each local or Coop `EventReportDecision` owns one `EventReportDecisionExecution`; conditional PostgreSQL updates fence enforcement and completion leases. Light/heavy actions must resolve the exact source-bound `EventModerationRecord` before the execution enters `CompletionPending`. Case/report mutation, organizer warnings, reporter outcome intent/deliveries, and execution completion then commit in one serializable transaction. This prevents a response-loss retry from repeating moderation or sending an outcome before truthful enforcement.

The target schema uses tenant-aware composite keys and explicit recipient authority. Dispatch revalidates current eligibility and may narrow the immutable policy/consent/preference/disclosure snapshot, never broaden it. `ProviderHandoff` is the suppression fence; uncertainty after that fence settles as `Unknown` and is never blindly retried. Phase 0B's specialized `IncomingWebhookEffectOutbox` Coop repair is independent of the recipient schema lane and blocks only provider convergence.

See [OUTBOX_PATTERN.md](OUTBOX_PATTERN.md) for full entity model, configuration, and monitoring details.

## Background Services

| Service | Purpose | Polling |
|---|---|---|
| `OutboxProcessor` | General outbox message dispatch with retry/dead-letter | Configurable (default 5s) |
| Quartz `pds-sync-drain` | Fenced, bounded-parallel AT Protocol event/RSVP delivery from committed `PdsSyncOutbox` rows, including retry/reconciliation and URI/CID settlement | Configurable interval, default 5s |
| `AtprotoJetstreamSubscriber` | One globally leased, allowlisted consumer for canonical community event/RSVP materialization, tombstones, quarantine, and cursor advancement | Capability-aware reconnect loop with bounded backoff |
| Quartz `email-dispatch-drain` | Default Basic Dispatch Mode trigger for draining `EmailDispatchOutbox` through the shared drain service | Cron `*/10 * * * * ?`, every 10s |
| `EmailDispatchProcessor` | Hosted-service fallback trigger over the same EmailDispatch drain service | Configurable fallback |
| `CompositeOutboxMessageDispatcher` | Dispatch component used by `OutboxProcessor` to route internal notification fanout, moderation fanout, and report provider sync messages | Invoked per outbox message |
| `EmailDispatchRabbitMqPointerPublisherService` | Optional RabbitMQ producer loop that publishes pointer-only messages for due `EmailDispatchOutbox` rows after durable storage exists | Configurable polling, default 5s |
| Quartz `integration-sync-drain` | Invokes `IntegrationSyncDrainService` for tenant-bound Listmonk synchronization with stale-lease recovery and provider-outcome parking | Configurable interval, default 5s |
| `NotificationFanoutProcessor` | Durable in-app notification fanout for event publication | Configurable polling |
| Quartz webhook drains | `local-webhook-delivery-drain`, incoming intake/effect, bulk replay, and provider publication each invoke one bounded durable-service pass | Existing feature intervals |

These background services and scheduler triggers use optimistic locking or durable claim semantics for multi-worker safety and are availability-gated where dependent services are required.

### Periodic Maintenance Sweeps Run On Quartz

Retention, cleanup, and reconciliation sweeps are **Quartz jobs**, not hosted-service timer loops. Each job in
`Explore.API/Scheduling/MaintenanceSweepJobs.cs` performs one pass and nothing else; enablement, initial delay,
interval, cancellation, exception containment, and per-execution DI scope belong to the scheduler.

| Job | Work |
|---|---|
| `idempotency-cleanup` | Expired idempotency replay-cache rows |
| `ai-retention-cleanup` | AI conversation retention across tenants |
| `email-dispatch-retention-cleanup` | Email dispatch content retention |
| `webhook-retention-cleanup` | Webhook message and attempt retention |
| `registration-retention-cleanup` | Per-tenant registration answer/PII deadlines |
| `storage-reconciliation` | Storage object state vs. provider |
| `privacy-erasure-credential-cleanup` | Expired provider credentials and locators |
| `organizer-payment-readiness-reconciliation` | Stale organizer payment connections |

Because trigger state lives in the `QRTZ_` tables, a restart resumes the existing cadence rather than
restarting every interval from zero, and missed occurrences during downtime collapse into one next run. With
clustering enabled each sweep runs on exactly one node instead of on every node.

The hosted services that remain are deliberate: `OutboxProcessor` is the durable side-effect authority whose
fencing is coupled to its own loop, `ManagedControlPlaneRegistrationWorker` is a retry-until-registered
bootstrap that returns on success, and the rest are queue- or event-driven rather than interval-driven. See
`docs/OPERATIONS.md` for the operator-facing job catalog and upgrade note.

## Local Runtime Endpoints
- Split default API: `https://localhost:7039`
- Split default Blazor: `https://localhost:7177`
- Standalone Combined host: `https://localhost:7180`
- Docker API: `http://localhost:7039`
- Docker Blazor: `http://localhost:7002`

## Related
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md)
- [OUTBOX_PATTERN.md](OUTBOX_PATTERN.md)
- [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md)
- [FOOTER_MANAGEMENT.md](FOOTER_MANAGEMENT.md)
- [SECRETS.md](SECRETS.md)
