using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Provides access to the current authenticated user's information from HTTP context.
/// Implements Clean Architecture by abstracting HTTP concerns from the Application layer.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current user's ID from the canonical platform identity authority.
    /// </summary>
    public Guid? UserId
    {
        get => _httpContextAccessor.HttpContext?.User.GetPlatformUserId();
    }

    /// <summary>
    /// Gets whether a user is currently authenticated.
    /// </summary>
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
}
