using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>Authenticated, value-free vocabulary shared by operator identity forms.</summary>
[ApiVersion("0.1")]
[Route("api/operator-identity-metadata")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class OperatorIdentityMetadataController(
    IQueryHandler<GetOperatorIdentityFormOptionsQuery, OperatorIdentityFormOptionsDto> query,
    IResourceAssembler<OperatorIdentityFormOptionsDto, OperatorIdentityFormOptionsDto> assembler) : EventControllerBase
{
    [HttpGet(Name = RouteNames.GetOperatorIdentityFormOptions)]
    [PrivateNoStore]
    [EndpointSummary("Get Operator Identity Form Options")]
    [EndpointDescription("Returns value-free operator kinds, country display choices, shared identity field constraints and accessible label/help identifiers. Registration authorities are explicitly unsupported. No identity values or edit authority are returned.")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<OperatorIdentityFormOptionsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HalResource<OperatorIdentityFormOptionsDto>>> Get(CancellationToken cancellationToken = default)
    {
        var dto = await query.QueryAsync(new GetOperatorIdentityFormOptionsQuery(), cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }
}
