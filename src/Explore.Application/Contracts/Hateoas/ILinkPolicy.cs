namespace Explore.Application.Contracts.Hateoas;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Hateoas;

/// <summary>
/// Defines link generation policy for a specific DTO type.
/// Link policies determine which links should be included for a resource
/// based on the resource state and user authorization.
/// </summary>
/// <typeparam name="TDto">The DTO type this policy applies to.</typeparam>
public interface ILinkPolicy<in TDto> where TDto : class
{
    /// <summary>
    /// Gets the link definitions for a resource.
    /// </summary>
    /// <param name="dto">The resource data.</param>
    /// <param name="user">The current user's claims principal (null if anonymous).</param>
    /// <returns>Collection of link definitions to generate.</returns>
    /// <remarks>
    /// Defaults to no links. Emitting nothing is the fail-closed answer, so a policy that has no detail
    /// affordances can simply not implement this rather than writing an empty body — and a policy that
    /// forgets to implement it withholds affordances rather than publishing unguarded ones.
    /// </remarks>
    IEnumerable<LinkDefinition> GetLinks(TDto dto, ClaimsPrincipal? user) => [];
}

/// <summary>
/// Defines link generation policy for collection/list DTOs.
/// Separate from item policy as collections often have different link requirements.
/// </summary>
/// <typeparam name="TDto">The list DTO type this policy applies to.</typeparam>
public interface ICollectionLinkPolicy<in TDto> where TDto : class
{
    /// <summary>
    /// Gets the link definitions for a collection item.
    /// </summary>
    /// <param name="dto">The item data.</param>
    /// <param name="user">The current user's claims principal (null if anonymous).</param>
    /// <returns>Collection of link definitions for this item.</returns>
    /// <remarks>Defaults to no links, for the same fail-closed reason as <see cref="ILinkPolicy{TDto}.GetLinks"/>.</remarks>
    IEnumerable<LinkDefinition> GetItemLinks(TDto dto, ClaimsPrincipal? user) => [];

    /// <summary>
    /// Gets the link definitions for the collection itself (create, search, etc.).
    /// </summary>
    /// <param name="user">The current user's claims principal (null if anonymous).</param>
    /// <returns>Collection of link definitions for the collection.</returns>
    /// <remarks>
    /// Defaults to no links. Prefer overriding the <see cref="ICollectionAuthorizationContext"/> overload:
    /// this parameterless form has no trusted collection owner to authorize against, so any affordance it
    /// emits cannot be fact-scoped.
    /// </remarks>
    IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];

    /// <summary>
    /// Gets collection link definitions using canonical server-resolved authorization metadata.
    /// </summary>
    /// <param name="user">The current user's claims principal (null if anonymous).</param>
    /// <param name="authorizationContext">Canonical resource metadata for the requested collection owner.</param>
    /// <returns>Collection of link definitions for the collection.</returns>
    IEnumerable<LinkDefinition> GetCollectionLinks(
        ClaimsPrincipal? user,
        ICollectionAuthorizationContext? authorizationContext) =>
        GetCollectionLinks(user);
}

/// <summary>
/// Exposes canonical resource metadata to collection link policies without coupling it to route values.
/// </summary>
public interface ICollectionAuthorizationContext
{
    string AuthorizationResourceId { get; }

    /// <summary>
    /// Closed typed policy facts for the collection owner, resolved server-side. Returning <c>null</c>
    /// means no trusted context exists and fact-dependent capabilities must fail closed.
    /// </summary>
    IAuthorizationFacts? AuthorizationFacts { get; }
}
