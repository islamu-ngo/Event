using Explore.API.Middleware;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services.Registration;

namespace Explore.API.Authentication;

public sealed class HttpRegistrationCreationPrincipal(IHttpContextAccessor httpContext)
    : IRegistrationCreationPrincipal
{
    public RegistrationCreationPrincipal? GetCurrent()
    {
        var context = httpContext.HttpContext;
        // The existing account authority rejects ambiguous and purpose-bound identities,
        // including API keys. A key's subject or owner is never an authenticated buyer.
        return context?.User.GetPlatformUserId() is { } userId && userId != Guid.Empty
            ? new(userId, IdempotencyRequestIdentityFactory.ComputePrincipalFingerprint(context))
            : null;
    }
}
