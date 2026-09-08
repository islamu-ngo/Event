// Caught by API middleware and mapped to HTTP 403 Forbidden.

namespace Explore.Application.Exceptions;

/// <summary>
/// Thrown when Cerbos denies an authorization request.
/// The API exception handler maps this to HTTP 403 Forbidden.
/// </summary>
public class AuthorizationException : ApplicationException
{
    public string Resource { get; }
    public string Action { get; }

    public AuthorizationException(string resource, string action)
        : base($"Access denied: cannot perform '{action}' on resource '{resource}'")
    {
        Resource = resource;
        Action = action;
    }

    public AuthorizationException(string message) : base(message)
    {
        Resource = string.Empty;
        Action = string.Empty;
    }
}
