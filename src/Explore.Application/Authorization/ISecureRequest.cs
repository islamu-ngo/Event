namespace Explore.Application.Authorization;

/// <summary>
/// Optional companion interface for commands decorated with <see cref="AuthorizeResourceAttribute"/>.
/// When a request implements this interface, the <see cref="Behaviors.AuthorizationBehavior{TRequest,TResponse}"/>
/// pulls the resource identifier and the request-declared authorization facts from the request instance
/// instead of using the static default (<c>typeof(TRequest).Name</c>).
/// <para>
/// This is NOT a third authorization path — it enhances Path 2 (<c>[AuthorizeResource]</c>).
/// Facts declared here are request-scoped context, not authority: the
/// <see cref="AuthorizationResourceContextResolver"/> overrides them with entity-loaded facts wherever a
/// server-side lookup exists, and any capability whose provider requires facts denies when none survive.
/// </para>
/// </summary>
public interface ISecureRequest
{
    /// <summary>
    /// The specific resource identifier for the authorization check (e.g., an OrganizationId).
    /// Returns null to fall back to typeof(TRequest).Name.
    /// </summary>
    string? ResourceId => null;

    /// <summary>
    /// Closed, typed policy facts for this request. Returns null when the resolver alone establishes them.
    /// </summary>
    IAuthorizationFacts? AuthorizationFacts => null;
}
