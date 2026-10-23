using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>Public authoring vocabulary with no tenant values, current authority or mutation affordances.</summary>
[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/reference-catalogue")]
[EndpointClassification(EndpointClass.Public)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
public sealed class ModerationReferenceCatalogueController(
    IQueryHandler<GetModerationReferenceCatalogueQuery, ModerationReferenceCatalogueView> query,
    IResourceAssembler<ModerationReferenceCatalogueView, ModerationReferenceCatalogueView> assembler) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet(Name = RouteNames.GetModerationReferenceCatalogue)]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType<HalResource<ModerationReferenceCatalogueView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<ModerationReferenceCatalogueView>>> Get(
        CancellationToken cancellationToken = default)
    {
        var view = await query.QueryAsync(new(), cancellationToken);
        return Ok(await assembler.ToResource(view, HttpContext));
    }
}
