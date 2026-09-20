namespace Explore.API.Hateoas.Assemblers;

using System.Security.Claims;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Application.Hateoas;

/// <summary>
/// Resource assembler for User entities.
/// Converts UserDto to HAL resources with appropriate links.
/// Note: User uses same DTO for detail and list views.
/// </summary>
public sealed class UserResourceAssembler : ResourceAssemblerBase<UserDto, UserDto>
{
    private readonly IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities> _lifecycleCapabilities;

    public UserResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<UserDto> detailLinkPolicy,
        ICollectionLinkPolicy<UserDto> collectionLinkPolicy,
        IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities> lifecycleCapabilities)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
        _lifecycleCapabilities = lifecycleCapabilities;
    }

    protected override async Task<IReadOnlyList<LinkDefinition>> GetDetailLinkDefinitionsAsync(
        UserDto dto, ClaimsPrincipal? user, HttpContext httpContext)
    {
        var existing = await base.GetDetailLinkDefinitionsAsync(dto, user, httpContext);
        var authority = user?.TryGetLocalSessionAuthority();
        if (authority?.LocalSubjectId != dto.Id) return existing;
        var capabilities = await _lifecycleCapabilities.QueryAsync(new GetLocalIdentityLifecycleCapabilitiesQuery(authority), httpContext.RequestAborted);
        return [.. existing, .. LocalIdentityLifecycleLinkPolicy.GetLinks(capabilities)];
    }

    /// <summary>
    /// Override to provide embedded resources for user details.
    /// </summary>
    protected override Dictionary<string, object>? GetEmbeddedResources(
        UserDto dto,
        Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        // Users don't embed other resources by default
        return null;
    }
}
