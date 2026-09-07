using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Identity;

/// <summary>
/// Adapts the ambient HTTP principal to <see cref="IUserContext"/> for callers that cannot see a
/// <c>ClaimsPrincipal</c> directly — Application handlers and services. Callers that already hold a principal
/// should use <see cref="PlatformIdentityPrincipalExtensions"/> instead of taking this dependency.
/// <para>
/// All claim semantics live in those extensions; this type contributes only the ambient-context lookup, so the
/// documented <c>sub → nameidentifier → sid → internal_user_id</c> chain cannot drift between layers.
/// </para>
/// </summary>
public class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    private System.Security.Claims.ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => User?.GetPlatformUserId();

    public string? Email => User?.GetEmail();

    public string? Username => User?.GetUsername();

    public Guid GetRequiredUserId() => User?.GetRequiredPlatformUserId()
        ?? throw new UnauthorizedAccessException("User is not authenticated or user ID is not available in the token.");
}
