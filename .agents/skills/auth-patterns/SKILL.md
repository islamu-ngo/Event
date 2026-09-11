---
name: auth-patterns
description: "Load for authentication or authorization changes involving BFF cookies/tokens, JWT validation, claims/user ID extraction, policies, handler access checks, impersonation, or 401/403 bugs; not for UI affordance gating alone."
type: pattern
enforcement: suggest
priority: critical
---
<!-- ABOUTME: Authentication and authorization rules for BFF cookies, JWT validation, Keycloak claims, endpoint protection, and HAL-based UI affordances. -->
<!-- ABOUTME: Keeps tokens out of the browser, preserves claim extraction order, and aligns API, handler, and client authorization behavior. -->

## Must-Read Docs
- [../../../docs/internal/SECURITY-MODEL.md](../../../docs/internal/SECURITY-MODEL.md)
- [../../../docs/internal/API.md](../../../docs/internal/API.md)
- [../../../docs/internal/AUTHORIZATION.md](../../../docs/internal/AUTHORIZATION.md)
- [../../../docs/internal/AUTHORIZATION_PATTERNS.md](../../../docs/internal/AUTHORIZATION_PATTERNS.md)
- [../../../docs/internal/QUICK_REFERENCE.md](../../../docs/internal/QUICK_REFERENCE.md)
- [resources/local-authorization-provider.md](resources/local-authorization-provider.md)

## Top 5 Invariants
1. The browser never sees tokens because the BFF stores them in HttpOnly cookies and forwards a `Bearer` token to the API.
2. User ID extraction follows `sub` then `nameidentifier` then `sid`, and a missing user identifier yields `401 Unauthorized`.
3. JWT validation checks both `aud` and `azp` with a five-minute clock skew, and authorized audiences include `islamu-event-api` and `islamu-event-blazor`.
4. Endpoint defaults are `GET` with `[AllowAnonymous]`, writes with `[Authorize]`, while resource/action checks route through `AuthorizationBehavior` to `RuntimeAuthorizationProvider` (which delegates to Cerbos gRPC PDP or `FallbackAuthorizationService`).
5. HATEOAS authorization follows the Candidate, Normalize, Batch, and Materialize pipeline and fails closed, leveraging `AuthorityProfile` pre-resolution in local mode and making `_links` the only client-side source of truth for action gating.

## Top 5 Anti-Patterns
1. Storing tokens in `localStorage` or `sessionStorage` bypasses the BFF boundary and weakens browser-side security.
2. Gating UI actions with role or claim inspection instead of HAL `_links` drifts from the server authorization contract.
3. Logging raw JWTs leaks secrets into traces, logs, and support artifacts.
4. Disabling the `Tenant` query filter during runtime request handling creates cross-tenant authorization and data-isolation bugs.
5. Validating only `aud` or only `azp` allows unauthorized clients to present otherwise valid tokens.
6. Writing a local claim-extraction helper creates a second identity chain that will silently disagree with the first about who the caller is.
7. Resolving `IUserContext` from `HttpContext.RequestServices` inside a controller hides an ambient dependency and is rejected from compiled controller calls by `ApiCompiledBoundaryTests`.

## Minimal Examples
```csharp
// Identity derivation is a pure function of the principal and already has one authority.
// Do not write another extraction helper — see resources/user-id-extraction.md.
using Explore.Application.Authentication;

Guid? userId = principal.GetPlatformUserId();           // sub -> nameidentifier -> sid -> internal_user_id
Guid required = principal.GetRequiredPlatformUserId();  // throws UnauthorizedAccessException

// In a controller, ExploreControllerBase already exposes CurrentUserId / RequiredUserId.
// For provider-linked accounts, inject IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery.
// Provider binding wins over GUID/internal-user claims; an unlinked account has no email fallback.
Guid? resolved = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
```

```csharp
public sealed record UpdateEventCommand(Guid EventId, string Title)
    : IAuthorizedRequest<BaseCommandResponse<Guid>>;

public sealed class UpdateEventHandler(IEventRepository repository)
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateEventCommand request,
        CancellationToken cancellationToken)
    {
        Event entity = await repository.GetRequiredAsync(request.EventId, cancellationToken);
        entity.Rename(request.Title);
        await repository.UpdateAsync(entity, cancellationToken);
        return new BaseCommandResponse<Guid>(entity.Id, true, "Updated");
    }
}
```

## Verification Hooks
- `dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/AuthorizationParityTests/*" --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1`
- `dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet`
- `dotnet build --configuration Release --verbosity quiet`

## Related Skills
- [../clean-architecture-rules/SKILL.md](../clean-architecture-rules/SKILL.md)
- [../cqrs-mediatr-guidelines/SKILL.md](../cqrs-mediatr-guidelines/SKILL.md)
- [../blazor-bff-patterns/SKILL.md](../blazor-bff-patterns/SKILL.md)
