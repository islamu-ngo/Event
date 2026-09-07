namespace Explore.Application.Authorization;

public sealed record AuthorizationContext(
    string? ResourceId,
    IAuthorizationFacts? Facts = null);
