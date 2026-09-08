using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Identity;

/// <summary>
/// Resolves the machine principal context from the current <see cref="HttpContext.User"/> when the request
/// was authenticated via an external API key. Returns <c>null</c> for JWT, anonymous, or absent contexts.
/// </summary>
public sealed class MachinePrincipalAccessor : IMachinePrincipalAccessor, IMachinePrincipalExecutionAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private ApiKeyPrincipalContext? _executionPrincipal;

    public MachinePrincipalAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ApiKeyPrincipalContext? Current
    {
        get
        {
            if (_executionPrincipal is not null)
            {
                return _executionPrincipal;
            }

            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            return user.TryGetApiKeyPrincipalContext();
        }
    }

    public bool IsMachineCaller => Current is not null;

    public void SetPrincipal(ApiKeyPrincipalContext principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (_executionPrincipal is not null)
        {
            throw new InvalidOperationException("A machine principal is already bound to this execution scope.");
        }

        _executionPrincipal = principal;
    }

    public void Clear()
    {
        _executionPrincipal = null;
    }
}
