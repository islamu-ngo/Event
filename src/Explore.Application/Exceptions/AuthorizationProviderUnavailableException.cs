namespace Explore.Application.Exceptions;

public sealed class AuthorizationProviderUnavailableException : AuthorizationException
{
    public AuthorizationProviderUnavailableException(string resource, string action)
        : base(resource, action)
    {
    }
}
