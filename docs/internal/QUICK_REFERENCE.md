ABOUTME: High-signal implementation rules that are easy to miss when reading code quickly.
ABOUTME: Focuses on non-inferable constraints and project-specific behavior.

# Quick Reference

> **Audience:** Contributors | Developers | Architects | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-03
> **Source Anchors:** `docs/internal/GOVERNANCE.md`, `docs/internal/ARCHITECTURE.md`, `docs/internal/DOMAIN.md`

## Critical Rules
1. Repositories return entities, not DTOs; mapping happens in handlers. Domain entities in `Explore.Domain` intentionally double as EF Core persistence entities (avoiding 200+ duplicate mirror classes and 140+ mappers while enabling native LINQ expression projections and powering 339 global query filters; see [DOMAIN.md](DOMAIN.md#architectural-rationale-unified-domain--persistence-model-pragmatic-ddd)).
2. Validators are manually instantiated in handlers/services (not injected as `IValidator<T>`).
3. Link/junction writes go through repositories, not direct navigation collection mutation.
4. Use `Guid` for core aggregates, `int` for most lookup IDs, `long` only for size/cursor style fields.
5. Do not rely on implicit business defaults in entities; set values in handler or EF config.
6. Keep file-scoped namespaces for new C# files.
7. Avoid deleting seemingly unused `using` statements blindly.
8. `GET` endpoints are typically `[AllowAnonymous]`; write endpoints are protected with `[Authorize]`.
9. User ID fallback order is `sub` -> `nameidentifier` -> `sid` -> `internal_user_id`, GUID-parseable values only. `Explore.Application.Authentication.PlatformIdentityPrincipalExtensions` is the **single** authority for it; `IUserContext` delegates there. Never re-derive identity from raw claims.
10. HAL responses are default; `Prefer: return=minimal` can remove link-heavy payloads.
11. Most create/update commands use `BaseCommandResponse<TId>`; many delete commands currently use `bool`.
12. Tenant isolation is enforced centrally by global query filters in `ExploreDbContext` and backed by PostgreSQL Row-Level Security (RLS) defense-in-depth (`FORCE ROW LEVEL SECURITY`); do not bypass casually.
13. Exception handling uses chained `IExceptionHandler` (not middleware); **every** error response is RFC 7807 ProblemDetails — handler-generated failures included, via `CommandFailurePolicy` or `MapCommandResponse`. Never return a raw `BaseCommandResponse` as a failure body.
14. Rate limiting is disabled in `Testing` environment (all policies replaced with `NoLimiter`).
15. Middleware pipeline order in `Program.cs` is critical — do not rearrange without understanding dependencies.
16. HATEOAS link policies use `yield return` pattern; each entity has separate detail and collection policies. Register families with `AddHalResource<...>`, which supplies the default `HalResourceAssembler<TDto,TListDto>` — do not write an assembler subclass that only forwards constructor arguments.
17. `EventQuerySpecification` is an immutable builder — every `With*()` call returns a new instance.
18. Module-conditional filters (Islamic, Tech) are silently ignored when the module is disabled for the tenant.
19. ETag middleware uses weak ETags (SHA256) — only on `application/json` and `application/hal+json` responses.
20. Named route constants in `RouteNames` must match `[HttpGet(Name = "...")]` attribute values on controller actions.
21. **HAL links are the single source of truth for UI**: Clients must gate action affordances (Edit, Delete, etc.) by checking for the presence of the corresponding link in the `_links` object, never by local role/claim inspection.
22. Normalized lookup DTOs expose `*Id`, `*Code`, and `*Name`; do not expose persisted enum wrappers in API contracts.
23. **Blazor is fully isolated from API implementation layers**: `Explore.Blazor`, `Explore.Blazor.Client`, and their tests must not reference Domain, Application, Infrastructure, or Persistence. Backend communication and backend/domain models come only from the generated API client contracts (`EventApiTagClients.g.cs` / per-tag clients).
24. **EF Core migrations and model snapshots are generated artifacts**: Never hand-edit them. Correct the entity/configuration or migration-generation extension, delete the unapplied development migration, and regenerate it with `dotnet ef migrations`.
25. **External behavior research is clean-room only**: Implementation context may contain neutral functional requirements and repository-native design material, never third-party source, snippets, ASTs, SQL, migrations, tests, comments, or assets. Independently design the implementation's structure, sequence, and organization and record provenance under [`docs/legal/IP_GOVERNANCE.md`](legal/IP_GOVERNANCE.md).
26. **Controllers never resolve services from the container.** `HttpContext.RequestServices` is banned in `Explore.API/Controllers`; take a constructor dependency, or read the request principal directly. Enforce the rule through compiled or runtime API contracts, never a historical source allowlist.
27. **Periodic work belongs to the Quartz.NET scheduler**, not to a hand-rolled `BackgroundService` timer loop. Register a sweep with `AddSweepJob<TJob>`; a job is one pass and nothing else. Queue-driven drains and the durable `OutboxProcessor` are deliberate exceptions.
28. **Controllers are partitioned by route capability.** A controller that accumulates several capabilities gets split, keeping every route template and `Name = RouteNames.*` verbatim so operationIds and the generated client do not move. Shared behavior across a split family becomes an explicit domain-family base class (e.g. `RegistrationOrderControllerBase`), never a global generic CRUD base class and never copied code.
29. **Dependencies must preserve outbound licensing options**: Do not add a library, package, image, asset, or generated component whose terms prevent ISLAMU-owned material from being offered under any outbound license the Project Steward may select under the CLA. Third-party material always retains its own terms; commercial or exceptional use requires documented approval for each distribution mode.
30. **No Python/JavaScript tooling & scripting as last resort**: Agents must not run or author Python or Node/JS scripts. Rely on native editing tools (`apply_patch`, `replace_file_content`) and standard Bash commands. Creating scripts is an absolute last resort (high ROI only) and belongs in `eng/` (C# / Bash). `.ci/scripts/` is strictly for CI/CD pipelines.
31. **Secrets Source of Truth**: Secrets, passwords, API tokens, connection strings, and encryption keys must never be hard-coded or defined in `Explore.AppHost` (`AppHost.cs`), test fixtures, controllers, or code. Secrets reside strictly in **Infisical**, explicit environment injection documented by **`.env.example`**, or the explicitly selected shared **.NET User Secrets** authority in **Development/Testing only**. User Secrets are rejected elsewhere and never act as fallback. Tests and local hosting bind dynamically through an approved authority or secret provider mocks.
32. **Self-Contained Interaction & Zero Plan-Opening Overhead**: Prompts, feedback requests, milestone reports, and approval inquiries MUST be completely self-contained and immediately actionable without requiring the developer to open `dev/active/<task>/...`. Bare internal codes (`P04/P06`, `T02.1`, `P03 gates`) are forbidden in isolation. Every approval or decision prompt must provide an inline Decision Brief with plain-English feature names, clear context, trade-offs, and recommended options.
33. **The 3-Ring Progressive Verification Model & Yak-Shaving Quarantine**:
    - **Ring 1 (Inner Loop / Sliced)**: In-progress subtasks run in-memory TUnit sliced tests (`--treenode-filter "/*/*/*<TestClass>/*"`) targeting `Event.Domain.UnitTests` or `Event.Application.UnitTests` (< 2s, 0 Docker containers, 0 network I/O). 90%+ of domain/normalization/validation invariants belong in Domain unit tests.
    - **Ring 2 (Phase Exit Gate)**: Verify the modified project against a single canonical provider in < 15s.
    - **Ring 3 (Plan Exit Gate)**: The full 5-database matrix, migrations, and architecture tests run strictly at plan exit before PR creation.
    - **Yak-Shaving Quarantine**: Agents are strictly forbidden from fixing unrelated pre-existing test suite rot or fixture failures encountered during feature tasks. Prove if it reproduces on untouched base, log it in `*-context.md` / `dev/backlog/`, quarantine it, and proceed with the assigned scope.

## Multi-Tenancy Reminder
Runtime tenant resolution:
1. trusted `X-Tenant-Slug` header from the BFF
2. custom domain
3. subdomain
4. unresolved multi-tenant request fails closed (`404`)

Single-tenant fallback default tenant ID: `018e4e5c-7f00-7000-8000-000000000001`.

Governance settings resolution uses a **5-tier cascade**: User → Group → Organization → Tenant → Instance. Instance-level locks prevent higher-tier overrides unless in single-tenant mode.

Database defense-in-depth (PostgreSQL):
- PostgreSQL enforces tenant isolation via Row-Level Security (RLS) across all tenant-scoped tables (`ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY`).
- `PostgresTenantSessionInterceptor` sets the connection session variable `app.current_tenant_id` on every connection open.
- Model-derived policies fail closed (`0` rows visible, cross-tenant inserts rejected with 42501) when `app.current_tenant_id` is missing or empty, protecting against raw SQL queries or inadvertent `IgnoreQueryFilters()` calls.
- Runtime database roles must have `NOBYPASSRLS` and must not have `SUPERUSER`.

## Auditing And Soft Delete
1. Auditable entities use `CreatedAt/By` and `UpdatedAt/By`.
2. Soft-deletable entities use `IsDeleted` (and often `DeletedAt/By`).
3. Named query filter `SoftDelete` is used so deleted rows stay hidden unless explicitly requested.

## Build And Test Baseline

1. Build: `dotnet build --configuration Release --verbosity quiet`
2. Follow the **3-Ring Progressive Verification Model** (`AGENTS.md` §8):
   - **Ring 1 (< 2s)**: In-memory TUnit slicing (`--treenode-filter`) for subtasks.
   - **Ring 2 (< 15s)**: Single-project, single canonical provider run for phase exits.
   - **Ring 3 (Plan Exit)**: Multi-provider matrix and full suites run once at workstream exit.
3. Use `AGENTS.md` for the exact current project list.
4. Enforce the **Yak-Shaving Quarantine Rule**: quarantine pre-existing test rot outside task scope.

## Common Failure Patterns
1. DTO changed but NSwag client not regenerated.
2. Wrong tenant context while debugging list/query results.
3. Repository returning DTOs and leaking app-layer concerns.
4. Assuming one command-response pattern across all legacy features without checking local feature conventions.
5. Changing middleware pipeline order in Program.cs without understanding dependencies (e.g., rate limiting after auth).
6. Adding a normalized lookup table without stable IDs/codes in `LookupTableSeeder` and matching DTO metadata projection.

## Controller-Authoring Standard (Forward Policy)

Every new controller action MUST have:

1. **Explicit route template** — `[HttpGet("resource/{id:guid}", ...)]` not bare `[HttpGet]`
2. **Explicit route name** — `Name = RouteNames.Xxx` on every `[Http*]` attribute
3. **Explicit endpoint class** — one of `[EndpointClassification(EndpointClass.Public)]`, `Authenticated`, `Admin`, or `PublicTransactional` on controller or action
4. **Explicit response typing** — `[ProducesResponseType<T>]` for success + error responses
5. **No overloaded semantics** — one action per HTTP verb + route template combination

6. **No container access** — no `HttpContext.RequestServices`; dependencies arrive through the constructor
7. **No private failure switch** — declare a `CommandFailurePolicy` (or use `MapCommandResponse`) instead of a per-action `switch` over `FailureCode`
8. **Identity from the principal** — `CurrentUserId` / `RequiredUserId` from `EventControllerBase`, or `mediator.ResolveCurrentUserIdAsync(User, ct)` when the provider subject is not a platform user id
9. **No generic CRUD or lookup base controllers** — keep controllers concrete and explicit; do not introduce `CrudControllerBase<...>` or `LookupControllerBase<...>`. Controllers are HTTP presentation adapters that dispatch MediatR; reuse mechanics via `EventControllerBase` (identity, concurrency stamps), domain-family bases, or composition (`CommandFailurePolicy`, `IResourceAssembler`, extension methods), keeping action signatures declarative, explicit, and customizable for backlog features.

Enforced through compiled architecture contracts, runtime endpoint metadata,
and focused HTTP behavior tests. Raw controller-source inventories are not an
acceptable enforcement seam.

## API Rate Limiting Quick Reference

| Policy | Key | Limit | Window |
|---|---|---|---|
| `global` | IP | 200 tokens | Refill 40/10s |
| `authenticated` | User ID | 200 requests | 60s sliding |
| `write` | User ID | 30 requests | 60s fixed |
| `setup_secret` | IP | 5 requests | 60s fixed |
| `public_transactional` | Effective remote IP | 10 requests | 60s fixed, queue 0 |

All disabled in `Testing` environment.

## Caching Quick Reference

| Layer | Scope | Duration | Applied At |
|---|---|---|---|
| Output Cache `LookupData` | HTTP response | 1 hour | Controller `[OutputCache]` |
| Output Cache `ListData` | HTTP response | 30 seconds | Controller `[OutputCache]` |
| Output Cache `DetailData` | HTTP response | 60 seconds | Controller `[OutputCache]` |
| HybridCache | Domain entity/DTO | 30 min (5 min local) | MediatR handler |
| ETag | Conditional request | N/A (304 check) | Middleware |

## Request Timeout Quick Reference

| Policy | Duration |
|---|---|
| `Default` | 30 seconds |
| `Lookup` | 10 seconds |
| `Complex` | 60 seconds |

## Correct vs Wrong (Top 10 Violations)

| # | Rule | Correct | Wrong |
|---|---|---|---|
| 1 | Repository return type | `Task<Event?> GetByIdAsync(Guid id)` returning the aggregate | `Task<EventDto?> GetByIdAsync(Guid id)` (leaks mapping into the repo) |
| 2 | Validator lifetime | `var validator = new CreateEventValidator(); var result = await validator.ValidateAsync(cmd, ct);` | Constructor-injected `IValidator<T>` (DI-bound) |
| 3 | ID types | `public int CountryId` (lookup), `public Guid Id` (aggregate), `public long Size` (bytes/cursor) | `public long CountryId` for a lookup FK |
| 4 | GET auth attribute | `[HttpGet("events", Name = RouteNames.ListEvents)] [AllowAnonymous]` | `[HttpGet]` with no explicit route template or name |
| 5 | User ID extraction | `User.GetPlatformUserId()` / `User.GetRequiredPlatformUserId()` from `PlatformIdentityPrincipalExtensions` | `User.FindFirst(ClaimTypes.NameIdentifier)?.Value`, or resolving `IUserContext` from `HttpContext.RequestServices` |
| 6 | UI action gating | `@if (dto.HasHalLink("edit")) { <AppButton /> }` driven by API `_links` | `@if (authState.User.IsInRole("Admin"))` (local claim check) |
| 7 | Tenant filter override | `ctx.Events.IgnoreQueryFilters([QueryFilterNames.SoftDelete])` (named filter only) | `ctx.Events.IgnoreQueryFilters()` (drops Tenant filter — security bug) |
| 8 | Specification builder | `var spec = new EventQuerySpecification().WithTitle(x).WithDate(y);` returns new instances | `spec.Title = x; spec.Date = y;` (mutates existing instance) |
| 9 | Command response | `Task<BaseCommandResponse<Guid>> Handle(CreateEventCommand cmd, CancellationToken ct)` | `Task<EventDto> Handle(CreateEventCommand cmd)` (missing wrapper + no CT) |
| 10 | HAL link policy | `yield return new LinkDefinition("edit", Url.Link(RouteNames.EditEvent, new { id })!, HttpMethods.Put);` | `links.Add(new LinkDefinition(...))` (list mutation instead of `yield return`) |
| 11 | Command failure body | `return TicketingFailures.Map(this, response);` (declared `CommandFailurePolicy` → ProblemDetails) | `return BadRequest(response);` (raw command object as the error body) |
| 12 | HAL registration | `services.AddHalResource<CategoryDto, CategoryListDto, CategoryDetailLinkPolicy, CategoryCollectionLinkPolicy>();` | Three raw `AddScoped` calls, or an empty `CategoryResourceAssembler` subclass that only forwards its constructor |
| 13 | Periodic worker | Quartz `IJob` doing one pass, registered via `AddSweepJob<TJob>` | `BackgroundService` with `while (!ct.IsCancellationRequested) { …; await Task.Delay(interval, ct); }` |
| 14 | Controller abstraction | Concrete class inheriting `EventControllerBase` + MediatR dispatch + `CommandFailurePolicy` | `CrudControllerBase<...>` / `LookupControllerBase<...>` (generic action inheritance anti-pattern) |

These are enforced by `Event.Architecture.Tests` and the `.agents/rules/` path-scoped rule files — see [`.agents/rules/README.md`](../../.agents/rules/README.md).
