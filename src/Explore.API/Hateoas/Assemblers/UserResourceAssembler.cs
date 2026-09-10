// ABOUTME: Assembles current-account HAL with existing profile links and native Local lifecycle discovery.
// ABOUTME: Resolves password/email affordances from current persisted authority, never client claims or primary-provider guesses.

namespace Explore.API.Hateoas.Assemblers;

using System.Security.Claims;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Application.Hateoas;
using MediatR;

/// <summary>
/// Resource assembler for User entities.
/// Converts UserDto to HAL resources with appropriate links.
/// Note: User uses same DTO for detail and list views.
/// </summary>
public sealed class UserResourceAssembler : ResourceAssemblerBase<UserDto, UserDto>
{
    private readonly ISender _sender;

    public UserResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<UserDto> detailLinkPolicy,
        ICollectionLinkPolicy<UserDto> collectionLinkPolicy,
        ISender sender)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
        _sender = sender;
    }

    protected override async Task<IReadOnlyList<LinkDefinition>> GetDetailLinkDefinitionsAsync(
        UserDto dto, ClaimsPrincipal? user, HttpContext httpContext)
    {
        var existing = await base.GetDetailLinkDefinitionsAsync(dto, user, httpContext);
        var authority = user?.TryGetLocalSessionAuthority();
        if (authority?.LocalSubjectId != dto.Id) return existing;
        var capabilities = await _sender.Send(new GetLocalIdentityLifecycleCapabilitiesQuery(authority), httpContext.RequestAborted);
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
