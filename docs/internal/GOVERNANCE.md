ABOUTME: Project governance rules that normalize architecture, naming, and contribution standards for this repository.
ABOUTME: Replaces generic template guidance with repo-specific constraints and links to the authoritative docs.

# Code Conventions & Governance

> **Standards and Conventions for .NET Clean Architecture Projects**
>
> This document defines naming conventions, organizational patterns, and architectural decisions.
> For code examples and implementation patterns, see the relevant skills in `.agents/skills/`.

**Last Updated**: 2026-08-24

---

## Table of Contents

1. [Critical Rules Summary](#critical-rules-summary)
2. [Naming Conventions](#naming-conventions)
3. [File Organization](#file-organization)
4. [Layer Responsibilities](#layer-responsibilities)
5. [Design Principles](#design-principles)
6. [Pattern Selection Guide](#pattern-selection-guide)
7. [Decision Framework](#decision-framework)
8. [Canonical Record-Selection Policy](#canonical-record-selection-policy)
9. [API Contract Rules](#api-contract-rules)
10. [CI/CD Governance](#cicd-governance)

---

## Critical Rules Summary

These rules are **non-negotiable**. Violations break architectural integrity.

| # | Rule | Rationale |
|---|------|-----------|
| 1 | Repositories return **entities**, never DTOs | Single responsibility; mapping belongs in handlers |
| 2 | Validators use **manual instantiation**, not DI | Fine-grained control; consistent pattern |
| 3 | Navigation properties are **readonly for writes** | Tenant isolation; explicit repository operations |
| 4 | Use `Guid` for core aggregates, `int` for most lookups, `long` only for large size/cursor fields | Matches current domain and persistence conventions |
| 5 | No default values in entity properties | Explicit initialization; clear value origins |
| 6 | Do not delete seemingly unused `using` statements blindly | Verify with build/tests before cleanup |
| 7 | Create/update commands typically return `BaseCommandResponse<Guid>` | Matches current CQRS response contracts |
| 8 | GET = AllowAnonymous, Write = Authorize | Public discovery; protected writes |
| 9 | UserId extraction fallback is `sub` -> `nameidentifier` -> `sid` | Provider compatibility; claim name variations |
| 10 | Use file-scoped namespaces | C# 10+ convention; cleaner code |
| 11 | Source code is self-documenting; legacy ABOUTME retired for code | Clean Architecture naming + XML doc summaries; see [policy below](#header--file-metadata-policy-natural-metadata--code-retirement) |

**For detailed examples**: See `QUICK_REFERENCE.md` and relevant skills.

---

## Naming Conventions

### General Rules

| Element | Convention | Example |
|---------|------------|---------|
| Public members | PascalCase | `Title`, `CreatedAt` |
| Private fields | _camelCase with underscore | `_eventRepository`, `_resourceAssembler`, `_mediator`, `_userContext`, `_cache` |
| Parameters | camelCase | `eventId`, `createDto` |
| Constants | PascalCase | `DefaultPageSize` |
| Interfaces | IPascalCase | `IEventRepository` |
| Async methods | Suffix with Async | `GetEventsAsync()` |

### Entity Naming

| Type | Pattern | Example |
|------|---------|---------|
| Core entity | Singular noun | `Event`, `Organization` |
| Lookup/enum entity | Singular noun | `EventType`, `ApprovalStatus` |
| Join table | Combined names | `EventCategories`, `EventTags` |
| Aspect table | Parent + Details | `EventIslamicDetails` |

### DTO Naming

| Type | Pattern | Purpose |
|------|---------|---------|
| Full details | `{Entity}Dto` | Read operations with all fields |
| List view | `{Entity}ListDto` | Minimal fields for lists |
| Create payload | `Create{Entity}Dto` | Write operations (no ID) |
| Update payload | `Update{Entity}Dto` | Write operations (ID required) |

### Handler Naming

| Type | Pattern | Example |
|------|---------|---------|
| Command | `Create{Entity}Command` | `CreateEventCommand` |
| Command Handler | `Create{Entity}CommandHandler` | `CreateEventCommandHandler` |
| Query | `Get{Entity}ListRequest` | `GetEventListRequest` |
| Query Handler | `Get{Entity}ListRequestHandler` | `GetEventListRequestHandler` |

### Repository Naming

| Type | Pattern | Example |
|------|---------|---------|
| Interface | `I{Entity}Repository` | `IEventRepository` |
| Implementation | `{Entity}Repository` | `EventRepository` |
| Generic base | `IGenericRepository<T, TId>` | Base interface |

---

## File Organization

### Solution Structure

```
Event.sln
├── Event.Domain/
├── Event.Application/
├── Explore.Persistence/
├── Explore.Infrastructure/
├── Explore.API/
├── Explore.Blazor/
├── Explore.Blazor.Client/
├── Explore.AppHost/
├── Event.Domain.UnitTests/
├── Event.Application.UnitTests/
├── Event.Architecture.Tests/
├── Explore.Secrets.UnitTests/
├── Event.Persistence.IntegrationTests/
├── Event.API.IntegrationTests/
├── Explore.Blazor.IntegrationTests/
├── Explore.Blazor.Client.Tests/
├── docs/
└── dev/
```

See [CODEBASE_STRUCTURE.md](CODEBASE_STRUCTURE.md) for the full directory map and notable subfolders.

### Application Layer Organization

```
Event.Application/
├── Features/{Feature}/
│   ├── Requests/
│   │   ├── Commands/           # Create, Update, Delete
│   │   └── Queries/            # Get, List, Search
│   └── Handlers/
│       ├── Commands/           # Command handlers
│       └── Queries/            # Query handlers
├── Behaviors/
├── Authorization/
├── Contracts/
├── DTOs/
├── Responses/
├── Telemetry/
└── Profiles/
```

### Domain Layer Organization

```
Event.Domain/
├── Entities/                   # Core business entities
├── Enums/                      # Enum definitions
├── Interfaces/                 # Domain interfaces
│   ├── ITenantEntity.cs
│   ├── IAuditableEntity.cs
│   └── ISoftDeletable.cs
├── Specifications/
└── ValueObjects/
```

### Persistence Layer Organization

```
Explore.Persistence/
├── Configurations/
├── QueryFilters/
├── Repositories/
├── Services/
└── ExploreDbContext.cs
```

### Header & File Metadata Policy (Natural Metadata & Code Retirement)

The repository previously mandated a two-line `ABOUTME:` comment summary on source files under the theoretical premise that agents would inspect the top two lines before loading full files. In practice, modern AI agent workflows discover code via **knowledge graphs** (`code-review-graph` MCP), **ripgrep**, and **Clean Architecture folder/naming conventions** rather than sequential file-peeking. The synthetic comment prefix generated substantial text-search pollution (18,000+ matches), comment rot, and tooling friction.

#### 1. Retirement of ABOUTME for Source Code
- **No Headers on Code**: The mandatory `ABOUTME:` convention is **retired** for all source code (`.cs`, `.razor`, `.css`, `.csproj`, shell scripts).
- **Agent Instruction**: AI agents must **never** generate `ABOUTME:` comments in new or modified source code files.
- **Legacy Files**: Existing legacy `ABOUTME:` lines in source code do not require immediate churn and may be cleaned up opportunistically.
- **Standard Code Documentation**: Use idiomatic C# XML documentation comments (`/// <summary>`) for complex public APIs, domain invariants, or non-inferable architectural behavior.

#### 2. Natural Documentation Metadata
Documentation files rely on standard, human-readable metadata structures rather than synthetic comment tags:

| Documentation Boundary | Format | Location | Purpose |
|---|---|---|---|
| **Public Documentation** (`docs/public/**`) | GitBook YAML frontmatter `description: "..."` | Top of file | Serves as page subtitle, search snippet, and SEO meta-description. Strictly exempt from AI comment tags. |
| **Internal Engineering Docs** (`docs/internal/**`) | Markdown blockquote metadata block (`> **Audience:** ...`) | Immediately below title | States audience, status, owner, last-verified date, and source anchors. |
| **Agent Skills** (`.agents/skills/**/SKILL.md`) | YAML frontmatter `description: "..."` | Top of file | Parsed dynamically by agent harnesses for just-in-time skill discovery. |
| **Architectural Decision Records** (`docs/internal/adr/**`) | Standard ADR title & context metadata | Top of file | Records architectural context, options, and decisions. |

---

## Layer Responsibilities

### What Goes Where

| Concern | Layer | Location |
|---------|-------|----------|
| Entity definition | Domain | `Event.Domain/` |
| Business rules | Domain | Entity methods, value objects |
| Repository interface | Application | `Contracts/Persistence/` |
| Command/Query | Application | `Features/{Feature}/Requests/` |
| Handler logic | Application | `Features/{Feature}/Handlers/` |
| DTO definition | Application | `DTOs/` and feature folders |
| Validation rules | Application | Feature validators or DTO validator folders |
| Repository implementation | Persistence | `Repositories/` |
| EF configuration | Persistence | `Configurations/` |
| External service impl | Infrastructure | Service classes |
| API endpoint | Presentation | `Controllers/` |
| UI component | Presentation | `Components/` |

### Dependency Rules

| Layer | Can Reference | Cannot Reference |
|-------|--------------|------------------|
| Domain | Nothing | Application, Infrastructure, Presentation |
| Application | Domain | Infrastructure, Presentation |
| Infrastructure | Domain, Application | Presentation |
| Presentation | All | (Entry point) |

### Aggregate Lifecycle Authority

Lifecycle state changes are semantic aggregate operations. Application coordinators acquire tenant-qualified locks, ask the tracked aggregate to accept an expected-state transition, and then persist it through an entity-first repository. Persistence may provide locking, transaction, query, and storage primitives, but it must not expose status-transition commands, reconstruct Domain transition rules, or update lifecycle status columns directly. API HAL policies consume the same Domain decision surface as commands and workers; transport status strings are representations, never an authorization or lifecycle authority.

---

## Design Principles

### SOLID Application

| Principle | How We Apply It |
|-----------|-----------------|
| **S**ingle Responsibility | One handler per command/query; one repository per entity |
| **O**pen/Closed | Extend via new handlers, not modifying existing |
| **L**iskov Substitution | All repository implementations honor interface contracts |
| **I**nterface Segregation | Small, focused interfaces per entity type |
| **D**ependency Inversion | Application defines interfaces; Infrastructure implements |

### Additional Principles

| Principle | Application |
|-----------|-------------|
| **DRY** | Generic repository base; shared validation patterns |
| **KISS** | Simple handlers; one responsibility per class |
| **YAGNI** | Don't add abstractions until needed |
| **Explicit > Implicit** | Clear initialization; obvious dependencies |
| **Fail Fast** | Validate early; return errors immediately |

---

## Pattern Selection Guide

### When to Use Each Pattern

| Scenario | Pattern | Reasoning |
|----------|---------|-----------|
| Simple CRUD | CQRS + Generic Repository | Standard approach |
| Complex query | Custom repository method | Optimize for specific needs |
| Cross-entity operation | Domain service | Coordinate multiple entities |
| External integration | Infrastructure service | Isolate external dependencies |
| Validation | FluentValidation | Consistent, testable rules |
| Mapping | AutoMapper | Convention-based, less boilerplate |

### When NOT to Use Patterns

| Anti-Pattern | Why to Avoid |
|--------------|--------------|
| Repository for simple lookups | Over-engineering |
| Domain service for single-entity ops | Should be in handler |
| Generic repository for complex queries | Loses optimization |
| MediatR behaviors for one-off logic | Use handler directly |

---

## Decision Framework

### Choosing The Secrets-Authority Contribution Intent

| Change | Primary intent | Additive secondary intent |
|--------|----------------|---------------------------|
| Deployment-owned secret source, resolver policy, persistence removal, rotation, safe visibility, or topology convergence | [`secrets-authority`](../../.agents/contract/intents.yaml) | Apply the path-matched bootstrap, migration, API, HAL, OpenAPI, Blazor, or CI intent declared by `secrets-authority.routing` |

The primary intent owns Tier 1 authority, confidentiality, reset, convergence, and greenfield-removal constraints. Secondary obligations remain additive except for the single declared OpenAPI compatibility conflict: secrets-authority removal of obsolete secret-bearing compatibility controls that workstream, while every other OpenAPI gate still applies.

### Choosing The Strong-Typing And Reflection Remediation Contribution Intent

| Change | Primary intent | Scope and constraints |
|--------|----------------|-----------------------|
| Mixed product-source and test strong-typing remediation, runtime-dispatch elimination, typed DID/identity/authorization boundaries, and assurance audit | [`strong-typing-refactor`](../../.agents/contract/intents.yaml) | Governs cross-layer strong-typing refactoring, invariant-disposition before deletion, no backward-compatibility, and zero-drift generated artifacts |

### Choosing ID Types

| Type | Use When | Examples |
|------|----------|----------|
| `Guid` | Main entities, distributed systems | Event, Organization, User |
| `int` | Lookup tables, enums, sequential IDs | EventType, ApprovalStatus |
| `long` | Large sequences, file sizes, cursors | Pagination, storage metrics |

### Choosing Validation Location

| Location | Use When |
|----------|----------|
| DTO Validator | Input validation, format checks |
| Handler | Business rules requiring data access |
| Entity | Invariants that must always hold |
| Controller | Quick format/auth checks |

### Choosing Query Strategy

| Strategy | Use When |
|----------|----------|
| Repository method | Standard queries, reusable |
| Handler-specific query | One-off, optimized for use case |
| Specification pattern | Complex, composable filters |

---

## Canonical Record-Selection Policy

Use records for handwritten contracts whose meaning is immutable data with value equality. Apply this policy by horizontal Clean Architecture ownership only: Domain, then Application, then API/OpenAPI, then generated client/Blazor; do not organize the migration as feature vertical slices.

See [RECORD_CONTRACTS.md](RECORD_CONTRACTS.md) for the layer-by-layer implementation workflow, persistence boundary, generated-client policy, and focused verification commands.

### Selection

- A concrete handwritten MediatR request defaults to a sealed record. A retained class needs a current, reasoned shrinking-baseline entry.
- Use a positional record for a short, stable contract with semantically distinct parameters. Use a nominal record with `init`/`required` members for long or optional contracts, named-construction safety, attributes, or PATCH presence semantics.
- Use a `readonly record struct` only for a small, self-contained value with correct copy/value semantics; use a sealed record class for reference-bearing value data when appropriate.

### Intentional Classes

Retain classes for EF entities and outbox lifecycle entities; services, handlers, controllers, and validators; mutable Blazor edit/component state; and generated protocol shapes whose framework behavior requires mutation, HAL identity, inheritance, file/exception semantics, or request-body population. Plain response/value contracts emitted by NSwag are transformed into nominal record classes through the repository-owned Roslyn pipeline. The exact mutable generated-response inventory lives in `eng/tools/Explore.GeneratedContracts/mutable-generated-contracts.txt`; each entry has observed post-construction UI or service mutation. Immutable `BaseCommandResponse<T>` results and their concrete payload-bearing descendants use valid-state record factories. These are exclusions by semantics or ownership, not record-policy debt.

### Immutability, Equality, and Logging

Records are shallowly immutable. Every published collection-bearing handwritten record must expose a serializer-compatible read-only or immutable member and defensively copy mutable inputs. Generated nominal records preserve NSwag collection shapes and keep only `[JsonExtensionData] AdditionalProperties` publicly settable because System.Text.Json AOT requires mutable extension-data population; all other eligible generated properties are `init`. Their generator-owned `PrintMembers` implementation omits every member value so accidental interpolation cannot enumerate PII, tokens, or free text. `ReadOnlyMemory<byte>` preserves base64 wire behavior for binary payloads; read-only lists/dictionaries and immutable collections preserve JSON arrays/objects, PATCH presence, HAL extension data, and caller ownership. Adapters replace immutable collections (`SetItem`, record copy, or a new snapshot) instead of mutating through an indexer. Record equality does not make array or list contents sequence-equal; test sequence semantics explicitly where they matter. Never log, interpolate, or destructure a whole record; log only bounded, approved scalar diagnostics.

### Trusted Authority

`UserId` and `TenantId` that represent the current caller or current tenant are never body authority. Introduce them from established server principal, tenant, route, or trusted-adapter context only when the request's authorization or business intent uses them. A body may carry a legitimate target ID only for an operation that manages that target, and the server must authorize it independently.

### Executable Contract Classification And TDD

Record-policy tests derive the current contract surface from compiled types,
serialization behavior, and generator-owned machine manifests rather than
historical source inventories. `PublishedCollectionContractArchitectureTests`
classifies published collection contracts across Domain, Application, API, and
Blazor; `published-collection-contract-dispositions.json` contains only
current, reasoned framework or ownership exceptions. Generated-client tests
verify policy-derived eligibility, compiled `init` semantics, value-free
diagnostic printing, AOT extension-data behavior, protected framework classes,
and the generator-owned mutable-state manifest. These structured manifests
must classify the complete current machine contract, but must not pin prose,
source tokens, or obsolete debt. Before a behavioral conversion, write focused
tests for consumed equality, one-fact `with` variants, caller-mutation
isolation, JSON construction, PATCH omitted/clear/replacement behavior, and
authority trust boundaries.

---

## Code Quality Standards

### Required in Every File

- File-scoped namespace
- All necessary using statements preserved
- Async suffix on async methods
- CancellationToken passed through

### Required in Every Entity

- Primary key property
- TenantId (for tenant-scoped entities)
- Audit fields (CreatedAt, UpdatedAt)
- Soft delete support (IsDeleted)

### Required in Every Controller

- Concrete controller class — **generic CRUD or lookup base controllers (`CrudControllerBase<...>`, `LookupControllerBase<...>`) are strictly banned**. Controllers are HTTP transport adapters; generic action inheritance creates an inflexible "framework inside a framework", obscures route names, and breaks OpenAPI metadata fidelity.
- Inherit `EventControllerBase` for request identity (`CurrentUserId`/`RequiredUserId`) and strong ETag concurrency parsing (`TryParseConcurrencyStamp`), or a domain-family base class when split controllers share an exact multi-step domain protocol.
- Route attribute with version (`[Route("api/[controller]")]`)
- Explicit OpenAPI and route name attributes on every action (`Name = RouteNames.Xxx`, `[EndpointSummary]`, `[EndpointDescription]`, `[ProducesResponseType]`)
- Authorization attributes matching the action's `EndpointClassification`
- Proper HTTP status codes with ProblemDetails for error bodies (via `CommandFailurePolicy` or `MapCommandResponse`)

---

## API Contract Rules

> **Scope**: Every controller action in `Explore.API` is part of a **governed product artifact** — the OpenAPI document at `/openapi/event-api.json`. Both server consumers (integrations, webhooks) and the generated `IEventApiClient` (used by `Explore.Blazor` and `Explore.Blazor.Client`) depend on this contract being stable, unambiguous, and ergonomic.
>
> These rules are enforced by tests in `Event.API.IntegrationTests/Features/ContractInvariantsTests.cs` and `Explore.Blazor.Client.Tests/ApiClientNamingTests.cs`. Violations fail CI.

### Versioning Strategy (Multi-Reader, Non-URL)

Version is negotiated via **three equal-status readers**, never via URL segment:

| Reader | Syntax | Primary consumer |
|---|---|---|
| Media type | `Accept: application/json;v=0.1` | REST-pure clients, HATEOAS navigation |
| Query string | `?api-version=0.1` | Webhooks, ad-hoc tools, browser debugging |
| Custom header | `X-Api-Version: 0.1` | Service-to-service integrations that cannot set media type |

The URL-segment pattern (`/api/v0.1/...`) is **banned**. It is the root cause of duplicate OpenAPI operations, it clutters HATEOAS links, and it prevents content negotiation from being the source of truth. Controllers carry exactly one `[Route("api/[controller]")]` attribute. Runtime requests to `/api/v{n}/...` must return 404.

### Endpoint Classification

Every controller action must carry exactly one of the following classifications, enforced by an architecture test:

| Class | Semantics | Authorization | Rate limit baseline |
|---|---|---|---|
| **Public** | Safe for unauthenticated read, with only the exact reviewed public authentication/callback exceptions below. Not a general anonymous mutation class. | `[AllowAnonymous]` | `global`; reviewed auth lifecycle uses `public_transactional` |
| **Authenticated** | Any logged-in user. Tenant-scoped or user-scoped write, or privileged read. | `[Authorize]` (no roles required) | `authenticated` or `write` |
| **Admin** | Operator / setup / diagnostics. Not exposed to the generated client. | `[Authorize(Roles=...)]` or `[SetupSecretRequired]` | `setup_secret` or `authenticated` |
| **PublicTransactional** | Anonymous tenant mutation for narrowly scoped guest flows. Unsafe verbs only. | `[AllowAnonymous]`; browser traffic is protected at the BFF boundary, not with API antiforgery metadata. | `public_transactional` |

The classification lives in controller action metadata via the `[EndpointClassification(EndpointClass.X)]` attribute (`Explore.API.Attributes`) and is the single source of truth for OpenAPI tagging (injected as `x-endpoint-class` operation extension by `EndpointClassificationTransformer`), client-generation filters, and Cerbos policy scaffolding. Every controller action must carry exactly one classification (class-level attribute is inherited by actions; action-level attribute overrides). Enforced by `EndpointClassificationArchitectureTests` in `Event.Architecture.Tests`.

`PublicTransactional` endpoints must explicitly use `[EnableRateLimiting("public_transactional")]`. The policy is a fixed window of 10 requests per 60 seconds per effective remote IP, with queue limit 0. The existing challenge issuance/proof-guarded guest-start boundary uses the dedicated `anonymous_registration` policy. Ordinary `POST` actions require an enforced `[RequireIdempotencyKey]`. The API receives no antiforgery metadata: browser traffic crossing the BFF is protected by BFF antiforgery validation.

#### Exact Local authentication lifecycle exceptions

The following four `Public`, `[AllowAnonymous]` POST actions are reviewed authentication operations, not safe reads and not a blanket exception for Local-named controllers:

| Exact action | Server authority |
|---|---|
| `LocalEmailVerificationController.RequestVerification` | Bounded, non-enumerating native current-address admission; a proposed address requires fresh ordinary Local session authority and cannot select another account. |
| `LocalEmailVerificationController.Consume` | Exact native email verification/change purpose, token, operation, subject, actor, external link and generation; one-use mutation and token-authorized mirror synchronization only. |
| `LocalPasswordRecoveryController.RequestRecovery` | Bounded non-enumerating native lookup restricted to verified email and a Ready linked Local identity; missing/ineligible targets remain uniformly accepted. |
| `LocalPasswordRecoveryController.Consume` | Exact recovery-purpose native token and receipt; one password mutation, then only original-token-authorized mirror repair on retry. |

All four require effective `PrivateNoStore`, `SuppressIdempotencyResponseStorage`, a 16384-byte request limit, the enabled `public_transactional` rate policy, and their exact named POST routes. They neither require generic idempotency keys nor issue ordinary sessions. Suppression must not be paired with a cosmetic required-key marker. The existing exact login, admission-recovery, callback and setup dispositions remain independently reviewed; none authorizes arbitrary anonymous POSTs.

Both authorization-surface and endpoint-classification guards use the same compiled `ReviewedAnonymousEndpointGovernance` recognition, including exact controller/action identity and effective safeguards. Native API/BFF `LocalIdentityLifecycleHttpTests` own wrong-purpose/extra-authority rejection, one-use behavior, session separation, private responses and browser antiforgery. Handler-owned and trusted-worker authorities are separately enumerated in [Authorization](AUTHORIZATION.md#reviewed-handler-and-worker-authorities); endpoint metadata alone is never MediatR authorization.

#### Exact capability cancellation: no key and no generic replay

`GuestRegistrationStatusController.CancelConfirmed` remains `PublicTransactional` at `POST api/events/{eventId:guid}/guest-registration-orders/{orderId:guid}/cancellation`, named `CancelConfirmedGuestRegistration`. It is the reviewed no-key/no-replay exception: effective `[AllowAnonymous]`, `PrivateNoStore`, `SuppressIdempotencyResponseStorage`, enabled `public_transactional` rate limiting, its exact POST identity, and absence of API antiforgery and required-key metadata are mandatory. An unrelated suppressed POST cannot acquire this disposition.

The handler enters `AnonymousCancellationService`, whose Serializable transaction freshly validates the exact tenant/event/order capability and live deadline under native fences before current eligibility, aggregate transition, admission revocation and exact consumed-hold release. Completed duplicates return 204 only after fresh capability/deadline validation. A missing/expired proof still fails with generic private 404, even with a previously successful key; an ordinary Local session cannot substitute. `GuestRegistrationStatusHttpTests` cancellation partials, `AnonymousCancellationConcurrencyTests` and `GuestRegistrationStatusBffTests` own runtime evidence.

`IdempotencyMiddleware` bypasses suppression before checking `RequireIdempotencyKey`. Adding that attribute to cancellation would therefore claim enforcement that does not occur; removing suppression would incorrectly permit cached authority. Native challenge issuance is different: its exact controller validates the intended start key itself while suppressing response storage. Neither case licenses generic credential/cancellation response replay or broad suppression exceptions.

### Operation IDs

- **Every action has an `operationId`.** `operationId` is the filename of the generated client method (plus `Async` suffix). Missing IDs cause NSwag to emit placeholder names (`GET`, `GET2`, `TenantDELETE2`) that break ergonomics and block the HATEOAS link-name alignment.
- **Operation IDs are unique across the whole document.** Uniqueness is a tested invariant, not an emergent behavior of good naming.
- **Naming pattern:** `{ControllerShortName}_{ActionName}` (PascalCase, underscore-separated). Example: `Tenant_GetById`, `EventRegistration_ListByEvent`. This is policy, not physics — see [NAMING_CONVENTIONS.md](NAMING_CONVENTIONS.md) for rationale and exceptions.
- **Route Name ↔ Operation ID alignment is intentional.** `[HttpGet(Name = "Tenant_GetById")]` and its derived `operationId` should match. They are kept aligned by convention, not by runtime equality — both are allowed to drift independently if a specific action needs it, but drift must be documented on the action.

### Banned Names

The following method names on `IEventApiClient` are always defects. CI fails if any appear:

- Any method whose name matches the regex `\d+Async$` (e.g. `Foo2Async`, `TenantGET3Async`).
- Any method whose name equals one of `GETAsync`, `POSTAsync`, `PUTAsync`, `DELETEAsync`, `PATCHAsync` — with or without a digit suffix.
- Any operationId equal to a raw HTTP verb (`GET`, `POST`, etc.) or a raw verb followed by digits.

These are NSwag's collision-disambiguation fallbacks and carry zero semantic information.

### Client-Ergonomics Bar

Generated client methods must be **readable without the OpenAPI doc**:

- **Collections vs single**: `ListEventsAsync` not `GetEventsAsync`; `GetEventAsync(id)` not `GetEventsAsync(id)`.
- **Mutations are business actions**: `PublishEventAsync`, `ApproveRegistrationAsync` — not `PutEvent2Async`.
- **No verb-only names**: A method called `GetAsync` on a 100+ entity client is a contract defect regardless of whether the compiler accepts it.

The ergonomics bar is a governed quality gate. Violations block release, though they do not block routine dev builds (they surface as schema-diff warnings in CI and fail only the explicit `ApiClientNamingTests`).

### NSwag Trailing-Parameter Convention

Every method generated by NSwag (`SingleClientFromOperationId` mode) ends with three optional parameters:

```csharp
string? api_version = null, string? x_Api_Version = null, CancellationToken cancellationToken = default
```

Callers passing `CancellationToken` must use a **named argument** (`cancellationToken: ct`) to avoid routing the token into the `api_version` string parameter. NSubstitute mock setups must insert `Arg.Any<string?>(), Arg.Any<string?>()` before `Arg.Any<CancellationToken>()`. This convention is stable across regenerations and must not be hand-modified in `EventApiClient.g.cs`.

**Stabilization plan**: `dev/active/api-contract-stabilization/api-contract-stabilization-plan.md`.

### Contract Ownership & Change Control

- **OpenAPI changes require PR review.** The checked-in `schemas/openapi_islamu-event.json` is an artifact of CI regeneration — it must never be hand-edited. Any PR touching controller signatures, route attributes, `[ApiVersion]`, or `[ProducesResponseType]` is implicitly a contract change.
- **Schema-diff is surfaced in CI.** Before 1.0 it is non-blocking (visibility only). At 1.0 it flips to blocking for breaking diffs.
- **Regeneration is a discrete step.** Do not regenerate `Explore.Blazor.Client/Clients/EventApiClient.g.cs` casually. Regenerate only when the API-side contract is stable and a tracked change-set justifies it.

### Authoring Checklist (new controller action)

1. Add `[HttpGet(Name = "X_Y")]` (or equivalent verb attribute) with a stable name.
2. Add `[ProducesResponseType]` for every possible response shape, including 400/401/403/404.
3. Pick an **Endpoint Classification** and apply the matching authorization attribute(s).
4. Confirm API contract tests still pass (`dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet`).
5. If the generated client needs regeneration, open a separate contract PR — do not mix contract and feature work.

## CI/CD Governance

GitHub Actions gates, branch-protection policy, environment settings, artifact retention, fork PR rules, and generated-artifact review rules live in [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md).

Key rules:

- Keep required workflow/job names stable unless branch protection is migrated in the same change.
- Required checks must be always-present or have a documented no-op path; do not make a path-skipped workflow required without a wrapper gate.
- OpenAPI and NSwag client drift is governed CI evidence. Contributors regenerate through the documented command path and commit the generated artifacts.
- Production deployment approval, branch restrictions, environment secrets, secret scanning, and push protection are GitHub repository or organization settings, not application configuration.

---

## Related Documentation

- **[QUICK_REFERENCE.md](QUICK_REFERENCE.md)** - Critical rules with examples
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - System architecture overview
- **[NAMING_CONVENTIONS.md](NAMING_CONVENTIONS.md)** - OperationId naming policy and exceptions
- **[TEMPLATE_GLOSSARY.md](TEMPLATE_GLOSSARY.md)** - Placeholder definitions

## Code Review Checklist

### Command Handler PR Review

Before approving any command handler PR, verify:

- **Multi-step writes → `IUnitOfWork`**: If the handler performs more than one write operation (across any repository or service), confirm `IUnitOfWork.ExecuteInTransactionAsync` wraps all writes.
- **Pre-validation outside lambda**: Validation, authorization checks, and read-only pre-fetches must be outside the transaction lambda.
- **Post-commit side effects after lambda**: Cache invalidation, metrics, external notifications must happen *after* `ExecuteInTransactionAsync` returns — never inside the lambda.
- **Retry-safety**: All `Guid.NewGuid()` / timestamps used as IDs must be generated before the lambda (captured via closure). No HTTP calls, broker publishes, or emails inside the lambda.
- **No nested transactions**: Handlers must not call services that internally use `IUnitOfWork` while already inside a transaction.

See `dev/active/unitofwork-pattern/unitofwork-pattern-context.md` for the canonical reference implementation.

---

## Implementation Reference

For detailed code patterns and examples, see:

| Skill | Content |
|-------|---------|
| `clean-architecture-rules` | Layer boundaries, dependency rules |
| `cqrs-mediatr-guidelines` | Commands, queries, handlers |
| `dotnet-efcore-guidelines` | DbContext, repositories, queries |
| `blazor-ui-conventions` | Component patterns, state management |
| `auth-patterns` | User ID extraction, authorization |

---

## AI Contribution Routing

Every change — human or agent — routes through the Contribution Contract before editing. The contract answers **eight** deterministic questions (intent, rules, must-read files, may-change paths, must-run tests, docs-to-update, PR checklist, forbidden-without-approval). See [`AGENTS.md`](../../AGENTS.md) §1 and [`.agents/contract/README.md`](../../.agents/contract/README.md).

### Intent Classification (Decision Table)

| Signal You Observe | Primary Intent | Must-Read Starts With |
|---|---|---|
| Adding read endpoint | `add-get-endpoint` | `docs/API.md`, `.agents/rules/api-controllers.md` |
| Adding/modifying mutation endpoint | `add-write-endpoint` | `docs/API.md`, `.agents/rules/api-controllers.md`, `auth-patterns` |
| Adding HAL affordance / link-based button | `add-hal-link` | `.agents/rules/api-hateoas.md`, `auth-patterns` |
| New MediatR command/query | `add-cqrs-handler` | `cqrs-mediatr-guidelines`, `.agents/rules/application-layer.md` |
| New EF Core migration | `add-ef-migration` | `dotnet-efcore-guidelines`, `.agents/rules/efcore-migrations.md` |
| Repository query change | `update-repository-query` | `dotnet-efcore-guidelines`, `.agents/rules/efcore-persistence.md` |
| Repository-wide test architecture refactor | `test-suite-rationalization` | `docs/TESTING.md`, `.agents/rules/tests.md`, `refactor-safely` |
| Blazor component affordance gated by HAL links | `blazor-component-affordance` | `blazor-ui-conventions`, `blazor-bff-patterns` |
| 401/403 BFF/API auth issue | `bff-auth-bug` | `auth-patterns`, `blazor-bff-patterns`, `docs/SECURITY-MODEL.md` |
| Cerbos policy change | `cerbos-policy-change` | `docs/AUTHORIZATION_PATTERNS.md`, `auth-patterns` |
| OpenAPI contract / breaking change | `openapi-contract-change` | `docs/API.md`, `docs/API_CHANGELOG.md` |
| Setup-time external provider or infrastructure bootstrap | `external-infrastructure-bootstrap` | `docs/SECURITY-MODEL.md`, `docs/SECRETS.md`, `auth-patterns` |

If no intent matches, stop and propose a new one per `.agents/contract/README.md`. Do not improvise.

### Path → Rule File (Auto-Loaded)

| File You Edit | Rule File |
|---|---|
| `Explore.API/Controllers/**/*.cs` | [`.agents/rules/api-controllers.md`](../../.agents/rules/api-controllers.md) |
| `Explore.API/Hateoas/**/*.cs` | [`.agents/rules/api-hateoas.md`](../../.agents/rules/api-hateoas.md) |
| `Explore.Application/**/*.cs` | [`.agents/rules/application-layer.md`](../../.agents/rules/application-layer.md) |
| `Explore.Domain/**/*.cs` | [`.agents/rules/domain.md`](../../.agents/rules/domain.md) |
| `Explore.Persistence/**/*.cs` (non-migration) | [`.agents/rules/efcore-persistence.md`](../../.agents/rules/efcore-persistence.md) |
| `Explore.Persistence/Migrations/**/*.cs` | [`.agents/rules/efcore-migrations.md`](../../.agents/rules/efcore-migrations.md) |
| `Explore.Blazor/**/*` (BFF) | [`.agents/rules/blazor-server.md`](../../.agents/rules/blazor-server.md) |
| `Explore.Blazor.Client/**/*` (WASM) | [`.agents/rules/blazor-client.md`](../../.agents/rules/blazor-client.md) |
| `**/*Tests/**/*.cs`, `**/*UnitTests/**/*.cs`, `**/*IntegrationTests/**/*.cs` | [`.agents/rules/tests.md`](../../.agents/rules/tests.md) |

### Context Maintenance

- Benchmark scenarios live in `.agents/benchmarks/cold-start-tasks.yaml` to measure cold-start agent success.

If a rule in `.agents/rules/` appears to conflict with `QUICK_REFERENCE.md` or this file, the canonical doc wins and the rule file must be fixed per [`AGENTS.md`](../../AGENTS.md) §4.
