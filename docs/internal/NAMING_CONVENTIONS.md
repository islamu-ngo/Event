# Naming Conventions

> Comprehensive naming patterns used across the codebase.
> Follow these exactly when creating new files, classes, or members.
> Last Updated: 2026-04-19

---

## Project Naming

| Project | Pattern | Purpose |
|---|---|---|
| `Explore.Domain` | `Explore.{Layer}` | Core domain entities |
| `Explore.Application` | `Explore.{Layer}` | Business logic / CQRS |
| `Explore.Persistence` | `Explore.{Layer}` | Data access / EF Core |
| `Explore.Infrastructure` | `Explore.{Layer}` | External services |
| `Explore.API` | `Explore.{Layer}` | Web API host |
| `Explore.Blazor` | `Explore.Blazor` | Blazor Server host |
| `Explore.Blazor.Client` | `Explore.Blazor.Client` | Blazor WASM client |
| `Explore.Secrets` | `Explore.{Library}` | Standalone library |
| `Event.Application.UnitTests` | `Event.{Layer}.{TestType}` | Test projects (legacy "Event" prefix) |
| `Event.Architecture.Tests` | `Event.Architecture.Tests` | Architecture fitness tests |

**Note:** Main projects use the `Explore.*` prefix (historical naming, will be renamed to `Event.*` in the future). Test projects already use `Event.*` prefix.

---

## File Naming

All source files use **PascalCase** matching the primary class/type name:

| File type | Pattern | Example |
|---|---|---|
| Entity | `{EntityName}.cs` | `Event.cs`, `Organization.cs` |
| Enum | `{EntityName}Enum.cs` | `EventTypeEnum.cs`, `EventStatusEnum.cs` |
| Interface | `I{Name}.cs` | `IEventRepository.cs`, `ITenantContext.cs` |
| DTO | `{Prefix}{EntityName}Dto.cs` | `CreateEventDto.cs`, `EventListDto.cs` |
| Command | `{Verb}{EntityName}Command.cs` | `CreateEventCommand.cs` |
| Query | `Get{EntityName}{Suffix}Request.cs` | `GetEventListRequest.cs` |
| Handler | `{CommandOrQuery}Handler.cs` | `CreateEventCommandHandler.cs` |
| Repository interface | `I{EntityName}Repository.cs` | `IEventRepository.cs` |
| Repository implementation | `{EntityName}Repository.cs` | `EventRepository.cs` |
| EF Configuration | `{EntityName}Configuration.cs` | `EventConfiguration.cs` |
| Controller | `{EntityName}Controller.cs` | `EventController.cs` |
| Capability controller | `{EntityName}{Capability}Controller.cs` | `EventModerationController.cs` |
| Controller family base | `{Family}ControllerBase.cs` | `WebhooksControllerBase.cs` |
| Blazor page | `{PageName}.razor` + `{PageName}.razor.cs` | `EventList.razor` |
| Blazor CSS | `{PageName}.razor.css` | `EventList.razor.css` |
| Client service | `{EntityName}Service.cs` | `EventService.cs` |
| Validator | `{Prefix}{EntityName}DtoValidator.cs` | `CreateEventDtoValidator.cs` |

---

## Namespace Conventions

**File-scoped namespaces** are required (C# 10+ style):

```
namespace Explore.Application.Features.Events.Handlers.Commands;
```

Namespace structure mirrors folder structure exactly:

| Layer | Namespace |
|---|---|
| Domain entities | `Explore.Domain` |
| Domain enums | `Explore.Domain.Enums` |
| Domain interfaces | `Explore.Domain.Interfaces` |
| Application DTOs | `Explore.Application.DTOs.{EntityName}` |
| Application features | `Explore.Application.Features.{EntityNames}.Handlers.{Commands\|Queries}` |
| Application requests | `Explore.Application.Features.{EntityNames}.Requests.{Commands\|Queries}` |
| Application contracts | `Explore.Application.Contracts.{Persistence\|Infrastructure\|Identity}` |
| Persistence repos | `Explore.Persistence.Repositories` |
| Persistence configs | `Explore.Persistence.Configurations.Entities` |
| API controllers | `Explore.API.Controllers` |
| API services | `Explore.API.Services` |
| Blazor client services | `Explore.Blazor.Client.Services` |
| Blazor client pages | `Explore.Blazor.Client.Pages.{Area}` |

---

## Entity Naming

### Primary Entities (Guid PK)

Singular noun, PascalCase. One class per file at `Explore.Domain/` root.

- `Event`, `Organization`, `Actor`, `User`, `Tenant`, `Location`, `StorageObject`, `Category`, `Tag`

### Lookup Tables (int PK)

Singular noun. These are reference data that rarely changes.

- `EventType`, `EventStatus`, `EventFormat`, `AudienceAge`, `AudienceGender`
- `Madhab`, `Language`, `VisibilityType`, `RegistrationMode`
- `Role`, `OrganizationPosition`, `ActorType`, `FileType`, `DidCustodyType`

### Link/Junction Tables (Composite PK)

Named as `{Parent}{Child}` (plural for many-to-many collections):

- `EventCategories` — Event ↔ Category
- `EventTags` — Event ↔ Tag
- `EventSessionLanguage` — EventSession ↔ Language
- `EventSessionSpeaker` — EventSession ↔ Speaker (User)
- `TagTypeTags` — TagType ↔ Tag
- `OrganizationMember` — Organization ↔ User (with Role via `RoleId`)
- `TenantUser` — Tenant ↔ User (with Role via `RoleId`)
- `RolePermission` — Role ↔ Permission

**Note:** Some junction table names are plural (`EventCategories`, `EventTags`) while others are singular (`OrganizationMember`, `EventSessionLanguage`). Follow the existing pattern when extending.

### Aspect Tables (1:1 Optional Extension)

Named as `{Parent}{Module}Aspect`:

- `EventIslamicAspect` — Islamic-specific fields for Event
- `EventTechAspect` — Tech-specific fields for Event

### Settings Entities

- `SystemSetting` — Instance-wide settings (global)
- `TenantSetting` — Per-tenant setting overrides
- `TenantSettingsDocument` — Typed JSONB tenant settings document, such as `tenant.branding`
- `AppSetting` — Application-level settings

---

## Enum Naming

Enums are named `{EntityName}Enum` and placed in `Explore.Domain/Enums/`. When an enum-backed concept is persisted, the database column should be a normalized lookup FK named `{LookupName}Id`, and API contracts should expose `{LookupName}Id`, `{LookupName}Code`, and `{LookupName}Name` rather than the enum wrapper:

```
EventTypeEnum.cs       → values: Conference = 1, Workshop = 2, ...
EventStatusEnum.cs     → values: Draft = 1, Published = 2, ...
AudienceAgeEnum.cs     → values: AllAges = 1, Children = 2, ...
```

Each enum value may map to a lookup table row's int `Id` for internal convenience, but EF should map the FK property and ignore enum wrapper properties on normalized entities.

---

## DTO Naming

DTOs follow a strict prefix/suffix pattern per entity:

| DTO Purpose | Pattern | Example |
|---|---|---|
| Full details (read) | `{EntityName}Dto` | `EventDto`, `OrganizationDto` |
| List view (read) | `{EntityName}ListDto` | `EventListDto`, `OrganizationListDto` |
| Create payload (write) | `Create{EntityName}Dto` | `CreateEventDto` |
| Update payload (write) | `Update{EntityName}Dto` | `UpdateEventDto` |

DTOs live in `Explore.Application/DTOs/{EntityName}/` — one folder per entity, all DTOs for that entity in the same folder.

---

## CQRS Naming

### Commands (Write Operations)

| Component | Pattern | Example |
|---|---|---|
| Command request | `{Verb}{EntityName}Command` | `CreateEventCommand` |
| Command handler | `{Verb}{EntityName}CommandHandler` | `CreateEventCommandHandler` |

Common verbs: `Create`, `Update`, `Delete`, `Add`, `Remove`

### Queries (Read Operations)

| Component | Pattern | Example |
|---|---|---|
| Query request | `Get{EntityName}{Suffix}Request` | `GetEventListRequest`, `GetEventDetailsRequest` |
| Query handler | `Get{EntityName}{Suffix}RequestHandler` | `GetEventListRequestHandler` |

Common suffixes: `List`, `Details`, `ById`, `By{RelatedEntity}`

### Folder Structure

Features are pluralized: `Events/`, `Organizations/`, `AudienceAges/`

```
Features/{EntityNames}/
├── Requests/
│   ├── Commands/    — {Verb}{EntityName}Command.cs
│   └── Queries/     — Get{EntityName}{Suffix}Request.cs
└── Handlers/
    ├── Commands/    — {Verb}{EntityName}CommandHandler.cs
    └── Queries/     — Get{EntityName}{Suffix}RequestHandler.cs
```

---

## Repository Naming

| Component | Pattern | Example |
|---|---|---|
| Interface | `I{EntityName}Repository` | `IEventRepository` |
| Implementation | `{EntityName}Repository` | `EventRepository` |
| Generic base | `IGenericRepository<T, TKey>` | `GenericRepository<T, TKey>` |

Interfaces in: `Explore.Application/Contracts/Persistence/`
Implementations in: `Explore.Persistence/Repositories/`

---

## Controller Naming

Controllers are named `{EntityName}Controller` and placed in `Explore.API/Controllers/`. Most map 1:1 with domain aggregates:

| Controller | Route |
|---|---|
| `EventController` | `api/event` |
| `OrganizationController` | `api/organization` |
| `EventSessionController` | `api/eventsession` |

Route convention: `api/[controller]` (controller name lowercased, no hyphens).

### Capability-Partitioned Families

An aggregate whose surface grows past one capability is split into `{EntityName}{Capability}Controller`
siblings that **share the original route**, stated explicitly rather than via the `[controller]` token:

| Family | Controllers | Shared route |
|---|---|---|
| Event | `EventController`, `EventLifecycleController`, `EventModerationController`, `EventCalendarController`, `EventManagementReadController` | `api/Event` |
| Registration order | `RegistrationOrderController`, `GuestRegistrationOrderController`, `AuthenticatedRegistrationOrderController` | `api/events/{eventId:guid}/registration-orders` |
| Webhooks | `WebhooksController`, `WebhookEndpointsController`, `WebhookMessagesController` | `api/webhooks` |
| Instance settings | `Instance{Governance,Presentation,Storage,Messaging,Authentication,Authorization}SettingsController` | `api/instance/settings` |
| Instance admin | `InstanceAdminController`, `InstanceTenant{Plan,Configuration,Lifecycle}Controller`, `InstanceDeploymentModeController` | `api/admin/instance` |

Rules for a partition:

- Use an **explicit** `[Route("...")]`; the `[controller]` token would change the URL.
- Carry every action's `Name = RouteNames.*` across verbatim — the route name pins the `operationId` and
  therefore the generated client method, so the split stays invisible to clients.
- Behavior shared across the family becomes a `{Family}ControllerBase`, never duplicated code.
- The OpenAPI `tags` array does change, because it derives from the class name. That is documentation
  grouping only and is the intended outcome.

---

## Blazor Page Naming

Pages use PascalCase and are organized by area:

| Area | Page pattern | Example |
|---|---|---|
| Event pages | `Pages/Event/{Action}.razor` | `EventList.razor`, `CreateEvent.razor` |
| Organization | `Pages/Organization/{Action}.razor` | `OrganizationDetails.razor` |
| Admin | `Pages/Admin/{Area}.razor` | `LookupTables.razor` |

Each page has a code-behind: `{PageName}.razor.cs` (partial class).
Optional scoped CSS: `{PageName}.razor.css`.

---

## Service Naming (Blazor Client)

Client services follow `{EntityName}Service` with interface `I{EntityName}Service`:

| Service | Purpose |
|---|---|
| `EventService` / `IEventService` | Event API calls |
| `OrganizationService` / `IOrganizationService` | Organization API calls |
| `LookupCacheService` / `ILookupCacheService` | Caches lookup table data |
| `AuthenticationStateProvider` | Framework-owned authentication presence; never a browser authority or claim-ID parser |
| `UiShellContextService` / `IUiShellContextService` | Server-confirmed tenant and shell capability context |
| `BffClient` | Low-level BFF HTTP calls with XSRF |
| `ImageStorageService` / `IImageStorageService` | S3 presigned URL uploads |

---

## Database Column Naming

EF Core is configured with **snake_case naming convention** (via `UseSnakeCaseNamingConvention()`):

| C# Property | Database Column |
|---|---|
| `EventTypeId` | `event_type_id` |
| `CreatedAt` | `created_at` |
| `IsDeleted` | `is_deleted` |
| `TenantId` | `tenant_id` |

Table names are also snake_case plurals of the entity name.

---

## Seed Data ID Conventions

- **Lookup tables** (int PK): IDs defined in enum files, seeded via `HasData()` in EF configurations
- **Main entities** (Guid PK): IDs defined in `SeedIds.cs` using deterministic **UUIDv7** format
- UUIDv7 pattern: `018e4e5c-xxxx-7xxx-8xxx-xxxxxxxxxxxx` (time-ordered, unique per entity type)
- Seed IDs must never change once used in production

---

## Test Naming

### Test Projects

| Project | Test type | Framework |
|---|---|---|
| `Event.Application.UnitTests` | Unit | TUnit |
| `Event.Domain.UnitTests` | Unit | TUnit |
| `Event.Architecture.Tests` | Architecture | TUnit |
| `Event.Persistence.IntegrationTests` | Integration | TUnit |
| `Event.API.IntegrationTests` | Integration | TUnit |
| `Explore.Blazor.Client.Tests` | Component | bUnit + TUnit |
| `Explore.Secrets.UnitTests` | Unit | TUnit |

### Test Class Naming

Test classes mirror the class under test with a `Tests` suffix:

- `CreateEventCommandHandlerTests` — Tests `CreateEventCommandHandler`
- `EventRepositoryTests` — Tests `EventRepository`
- `CleanArchitectureTests` — Architecture rule tests

### Test Method Naming

Methods use descriptive names: `{Method}_{Scenario}_{ExpectedResult}`

---

## Configuration Naming

### EF Core Configuration

| Component | Pattern | Location |
|---|---|---|
| Entity config class | `{EntityName}Configuration` | `Configurations/Entities/` |
| Implements | `IEntityTypeConfiguration<{EntityName}>` | — |

### DI Registration

| Registration file | Pattern |
|---|---|
| `PersistenceServicesRegistration.cs` | `ConfigurePersistenceServices()` |
| `InfrastructureServicesRegistration.cs` | `ConfigureInfrastructureServices()` |
| `Explore.Blazor.Client/Program.cs` | Direct service registration |
| `Explore.API/Program.cs` | Calls all registration methods |

---

## Constants and Settings Key Naming

Governance setting keys use dot-separated hierarchical names:

```
Deployment.Mode
Events.MaxSessionsPerEvent
Events.RequireApproval
Modules.Islamic.Enabled
Modules.Tech.Enabled
Branding.DisplayName
Domains.InstanceBaseDomain
```

Defined in `Explore.Domain/Constants/GovernanceSettingKeys.cs`.

---

## API Contract Naming (Routes, Route Names, Operation IDs)

> Governed by [docs/GOVERNANCE.md#api-contract-rules](GOVERNANCE.md#api-contract-rules).
> These rules are enforced by `ContractInvariantsTests` (Event.API.IntegrationTests),
> `ApiClientNamingTests` (Explore.Blazor.Client.Tests), and the auto-generated inventory
> at [API_CONTRACT_INVENTORY.md](API_CONTRACT_INVENTORY.md).

### Controller Route

Every controller carries **exactly one** `[Route]` attribute using the conventional template:

```csharp
[ApiController]
[Route("api/[controller]")]
public sealed class ActorController : ControllerBase { ... }
```

Banned:
- Multiple `[Route]` attributes on the same controller.
- URL-segment versioning — `/api/v{version:apiVersion}/...`, `/api/v0.1/...` etc. must 404 at runtime.
- Hard-coded absolute URLs or non-`api/` prefixes.

Versioning is **multi-reader, non-URL**: media-type (`Accept: application/json;v=0.1`), query (`?api-version=0.1`), custom header (`X-Api-Version: 0.1`).

### Route Names (HATEOAS + HttpGet Name=)

The single source of truth is `Explore.API/Hateoas/RouteNames.cs` (static `RouteNames` class).
Every GET action that participates in HATEOAS **must** declare `[HttpGet(Name = RouteNames.XXX)]`.

| Concern | Rule |
|---|---|
| Constant name | PascalCase verb+subject — `GetActors`, `GetActorById`, `SearchEvents` |
| Constant value | Same as constant name (string literal) — `"GetActors"` |
| Scope | Unique **per API deployment** (enforced by test) |
| Collision | No two route-name constants may share a value |
| Usage | Reference via `RouteNames.GetActors`, never hard-code the string |
| Grouping | Organize into `#region` blocks per aggregate (Organization / Event / Actor / …) |

### Operation IDs (OpenAPI)

Operation IDs power client-generation (NSwag) and OpenAPI tooling. They are governed as product artifacts.

**Format:** `{ControllerShortName}_{ActionName}` — PascalCase segments separated by a single underscore.

| Example endpoint | Operation ID |
|---|---|
| `GET  /api/actor` (list) | `Actor_GetActors` |
| `GET  /api/actor/{id}` | `Actor_GetActorById` |
| `POST /api/actor` | `Actor_CreateActor` |
| `PUT  /api/actor/{id}` | `Actor_UpdateActor` |
| `DELETE /api/actor/{id}` | `Actor_DeleteActor` |
| `POST /api/event/{id}/publish` | `Event_PublishEvent` |

Required invariants (enforced by `ContractInvariantsTests`):

1. **Every operation has an `operationId`.** No null/empty/whitespace values in the exported OpenAPI document.
2. **Operation IDs are unique** across the entire document.
3. **Operation IDs alias to Route Names by policy**, not by framework coupling. When a GET action declares `[HttpGet(Name = RouteNames.X)]` the operationId should be semantically equivalent (e.g., route name `GetActors`, operationId `Actor_GetActors`). Maintain this alignment deliberately.
4. **No placeholder / collision-fallback names.** The following are banned and fail CI:
   - Bare HTTP verbs: `GETAsync`, `POSTAsync`, `PUTAsync`, `PATCHAsync`, `DELETEAsync`, `GET`, `POST`, `PUT`, `PATCH`, `DELETE`
   - Any operationId matching `\d+$` or generated client method matching `\d+Async$` (e.g., `TenantGET2Async`, `Status7Async`)
   - operationIds equal to raw HTTP verbs in any casing
5. **Generated client method names** (`IEventApiClient` via NSwag) must match regex `^[A-Z][A-Za-z0-9]+Async$` and must not collide. NSwag derives method names from operationId; fixing operationIds upstream fixes client names automatically.

### Client-Ergonomics Bar

Operation IDs (and therefore generated client methods) are **API consumer surface**. They must read as a product API, not generator output.

| Concern | Good | Bad |
|---|---|---|
| Collection vs single | `Actor_GetActors` / `Actor_GetActorById` | `Actor_Get` / `Actor_Get2` |
| Mutation reads as business action | `Event_PublishEvent` / `Event_ArchiveEvent` | `Event_Post` / `Event_Put2` |
| Verb on noun, not noun on verb | `Registration_CancelRegistration` | `Cancel_Registration` (inverted) |
| No raw verbs | `Tenant_UpdateTenant` | `TenantPUT` / `TenantPUTAsync` |

### Endpoint Classification (tagging)

Every action is classified as one of `Public`, `Authenticated`, `Admin` via the `[EndpointClassification(EndpointClass.X)]` attribute (`Explore.API.Attributes`). The attribute is read by `EndpointClassificationTransformer` and emitted as the `x-endpoint-class` operation extension in `/openapi/event-api.json`. Used as the single source of truth for OpenAPI audience filters and Cerbos scaffolding. Controller-level attribute is inherited by actions; action-level attribute overrides. Enforced by `EndpointClassificationArchitectureTests`. See [docs/GOVERNANCE.md#api-contract-rules](GOVERNANCE.md#api-contract-rules) for the classification decision table.

### Authoring Checklist for a New Controller Action

1. Declare a single `[Route("api/[controller]")]` on the controller — never URL-segment versioning.
2. For GETs that participate in HATEOAS, add `RouteNames.{Name}` constant and use `[HttpGet(Name = RouteNames.{Name})]`.
3. Ensure the action produces a stable, policy-conformant `operationId` via the `{Controller}_{Action}` convention.
4. Declare `[ProducesResponseType]` for success + error cases.
5. Pick an Endpoint Classification (Public / Authenticated / Admin).
6. Run `Event.API.IntegrationTests`, then regenerate the inventory with `Explore.ApiContractInventory`.
7. Regenerate the NSwag client only as a discrete, reviewed step — never mixed with feature PRs.

---

## Summary: Quick Rules

1. **Entities** = singular PascalCase, one per file at Domain root
2. **Enums** = `{Entity}Enum` in `Domain/Enums/`
3. **DTOs** = `{Prefix}{Entity}Dto` in `Application/DTOs/{Entity}/`
4. **Commands** = `{Verb}{Entity}Command` in `Features/{Entities}/Requests/Commands/`
5. **Queries** = `Get{Entity}{Suffix}Request` in `Features/{Entities}/Requests/Queries/`
6. **Handlers** = mirror command/query name + `Handler` suffix
7. **Repositories** = `I{Entity}Repository` (interface) / `{Entity}Repository` (impl)
8. **Controllers** = `{Entity}Controller`, route `api/[controller]`
9. **Blazor pages** = `{Action}{Entity}.razor` or `{Entity}{Action}.razor` (varies by area)
10. **Client services** = `{Entity}Service` / `I{Entity}Service`
11. **DB columns** = snake_case (automatic via EF Core convention)
12. **Namespaces** = file-scoped, mirror folder structure exactly
