---
name: api-controllers
description: Apply when editing Explore.API controller actions and route contracts.
paths:
  - "src/Explore.API/Controllers/**/*.cs"
related_skills: [cqrs-guidelines, auth-patterns]
related_docs: [docs/internal/API.md, docs/internal/GOVERNANCE.md, docs/internal/AUTHORIZATION.md, docs/internal/QUICK_REFERENCE.md]
minimum_tests: [Event.API.IntegrationTests, Event.Architecture.Tests]
related_intents: [add-get-endpoint, add-write-endpoint, openapi-contract-change, add-hal-link]
---

<!-- ABOUTME: Path-scoped rules for Explore.API controller actions and route contracts. -->
<!-- ABOUTME: Twin copies live at .agents/rules/api-controllers.md and .omo/rules/api-controllers.md; update both paths. -->

# API Controller Rules

## Applies To
- `src/Explore.API/Controllers/**/*.cs`

## Path-Specific Constraints
- **Logic Thinning**: Controller actions must be limited to: dispatching MediatR, assembling HAL links (via policies), and returning the `ActionResult`. No business logic. A rule that validates or normalizes *domain* input belongs in the command handler, so every caller — HTTP, MCP, internal — is held to it.
- **Contract Integrity**: Every action MUST use a `RouteNames.Xxx` constant that matches the HATEOAS policy reference. The route name is what pins the generated `operationId`, and therefore the generated client method — never rename or drop one while moving an action.
- **Error Metadata**: Explicitly include `[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]` (and other error codes) for all public endpoints. Error bodies are always ProblemDetails; never declare `BaseCommandResponse<T>` for a 4xx/5xx status.
- **No Service Location**: `HttpContext.RequestServices` is banned here. Take a constructor dependency, or read the request principal directly.
- **Identity**: use `CurrentUserId` / `RequiredUserId` from `EventControllerBase` (formerly `ExploreControllerBase`), which project `PlatformIdentityPrincipalExtensions` over `ControllerBase.User`. For provider-linked accounts, inject `IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>` and use `identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken)`. Provider bindings take precedence over GUID/internal-user claims; an unlinked account has no email fallback. Do not parse claims in a controller; purpose-bound schemes (API key, setup secret, managed control plane, ATProto session, erasure receipt) are the only exceptions and are allowlisted by name.
- **Tenant Authority**: current tenant/user facts come from `ITenantContext`, the established principal helpers, an authoritative route/persisted resource, or a trusted adapter; never accept them from a body. Body IDs may name legitimate targets only when the server independently authorizes and tenant-checked that target. Removed authority members get no alias or compatibility reader.
- **Failure Mapping**: declare a `CommandFailurePolicy` per capability (`ValidatedBy(...).NotFound(...).Conflict(...)`) and call `Policy.Map(this, response)`. Policies are immutable and compose, so a variant is the base policy plus one rule. Do not write a private `switch` over `FailureCode`.
- **Capability Partition**: keep a controller to one route capability. When it accumulates several, split it — preserving every route template, verb, `Name = RouteNames.*`, authorization attribute, and endpoint classification — and lift genuinely shared behavior into an explicit family base class (see `InstanceSettingsControllerBase`, `WebhooksControllerBase`, `RegistrationOrderControllerBase`).
- **No Generic CRUD/Lookup Bases**: Controllers MUST remain concrete classes. Never introduce generic base classes (such as `CrudControllerBase<...>` or `LookupControllerBase<...>`) to abstract actions. Generic action inheritance creates an inflexible "framework inside a framework", obscures route names, breaks OpenAPI/NSwag metadata fidelity (`[EndpointSummary]`, `[EndpointDescription]`, operation IDs), and restricts future customization for unique backlog requirements. Deduplication belongs in `EventControllerBase` (identity, concurrency stamps), domain-family base classes (split capabilities sharing an exact domain workflow protocol), or composition (`CommandFailurePolicy`, `IResourceAssembler`, extension methods).
- **Immutable Collection Adapters**: Nested record contracts snapshot collection input. Controllers replace immutable extension data through `SetItem`, record copies, or a new snapshot rather than mutating an indexer; convert read-only binary memory only at the framework file-result boundary.

## Must Read
- [docs/internal/QUICK_REFERENCE.md#controller-authoring-standard-forward-policy](../../docs/internal/QUICK_REFERENCE.md#controller-authoring-standard-forward-policy)
- [docs/internal/API.md](../../docs/internal/API.md)

## Verification
- Build: `dotnet build --configuration Release --verbosity quiet`
- Tests: `Event.API.IntegrationTests`, `Event.Architecture.Tests`
- `ApiCompiledBoundaryTests` rejects request-container service location from compiled controller calls and constructors. Principal-extension tests and HTTP ProblemDetails contracts own identity and failure behavior; line counts and historical source allowlists are not gates.
- A controller split must leave `schemas/openapi_islamu-event.json` semantically unchanged apart from `tags`. Verify by diffing operationIds and the `components` section, then regenerating the NSwag client and confirming no method name moved.

## Related
- Intents: `add-get-endpoint`, `add-write-endpoint`, `openapi-contract-change`, `add-hal-link`
- Agents: `architect-agent.md`, `presentation-engineer-agent.md`, `quality-verifier-agent.md`
- Rules: `application-layer.md`, `api-hateoas.md`
