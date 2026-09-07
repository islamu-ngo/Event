using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class PublicExperienceController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILinkPolicy<EventDiscoveryItemDto> _eventDiscoveryLinkPolicy;
    private readonly IHateoasLinkGenerator _linkGenerator;

    public PublicExperienceController(
        IMediator mediator,
        ILinkPolicy<EventDiscoveryItemDto> eventDiscoveryLinkPolicy,
        IHateoasLinkGenerator linkGenerator)
    {
        _mediator = mediator;
        _eventDiscoveryLinkPolicy = eventDiscoveryLinkPolicy;
        _linkGenerator = linkGenerator;
    }

    [HttpGet("settings", Name = RouteNames.GetPublicExperienceSettings)]
    [AllowAnonymous]
    [EndpointSummary("Get Public Experience Settings")]
    [EndpointDescription("Returns effective home-page and white-label settings for the current tenant context.")]
    [ProducesResponseType(typeof(PublicExperienceSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicExperienceSettingsDto>> GetSettings(CancellationToken cancellationToken = default)
    {
        var settings = await _mediator.Send(new GetPublicExperienceSettingsQuery(), cancellationToken);
        return settings.IsAvailable
            ? Ok(settings)
            : IdentityUnavailable(settings.UnavailableCode);
    }

    [HttpGet("shell", Name = RouteNames.GetPublicExperienceShell)]
    [AllowAnonymous]
    [EndpointSummary("Get Public Experience Shell")]
    [EndpointDescription("Returns the typed public shell read model for the current tenant context.")]
    [ProducesResponseType(typeof(PublicExperienceShellDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [OutputCache(PolicyName = "PublicExperienceShell")]
    public async Task<ActionResult<PublicExperienceShellDto>> GetShell(CancellationToken cancellationToken = default)
    {
        var shell = await _mediator.Send(new GetPublicExperienceShellQuery(), cancellationToken);
        return shell.IsAvailable
            ? Ok(shell)
            : IdentityUnavailable(shell.UnavailableCode);
    }

    [HttpGet("~/api/public-experience/home", Name = RouteNames.GetHomeDiscovery)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get Home Discovery")]
    [EndpointDescription("Returns the tenant-aware event discovery sections for the public home page.")]
    [ProducesResponseType(typeof(HomeDiscoveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "PublicHomeDiscovery")]
    public async Task<ActionResult<HomeDiscoveryDto>> GetHomeDiscovery(
        [FromQuery] Guid? areaId = null,
        [FromQuery] string? mode = null,
        CancellationToken cancellationToken = default)
    {
        var home = await _mediator.Send(new GetHomeDiscoveryQuery(areaId, mode), cancellationToken);
        AddSourceLinks(home);
        return Ok(home);
    }

    private ObjectResult IdentityUnavailable(string? code)
    {
        const string fallbackCode = "tenant_identity_unavailable";
        Response.Headers.CacheControl = "no-store";
        return Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Public experience identity is unavailable.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = string.IsNullOrWhiteSpace(code) ? fallbackCode : code
            });
    }

    private void AddSourceLinks(HomeDiscoveryDto home)
    {
        if (HttpContext.Items.TryGetValue(HateoasConstants.MinimalResponseKey, out var minimal)
            && minimal is true)
        {
            return;
        }

        foreach (var item in EnumerateDiscoveryItems(home))
        {
            LinkDefinition? definition = _eventDiscoveryLinkPolicy
                .GetLinks(item, User)
                .FirstOrDefault(link => link.Rel == "source");
            if (definition is null)
            {
                continue;
            }

            HalLink? link = _linkGenerator.GenerateLink(definition, HttpContext);
            if (link is not null)
            {
                item.AdditionalProperties = item.AdditionalProperties.SetItem(
                    "_links",
                    new Dictionary<string, HalLink> { ["source"] = link });
            }
        }
    }

    private static IEnumerable<EventDiscoveryItemDto> EnumerateDiscoveryItems(HomeDiscoveryDto home) =>
        home.Hero
            .Concat(home.UpcomingInArea)
            .Concat(home.Spotlight?.Items ?? [])
            .Concat(home.MostViewedInArea)
            .Concat(home.MostViewedOnline)
            .Concat(home.CuratedSections.SelectMany(section => section.Items))
            .Concat(home.RecentlyAdded)
            .Distinct();
}
