using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Features.Geocoding.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Route("api/geocoding")]
public sealed class GeocodingController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<AddressSuggestionDto, AddressSuggestionDto> assembler)
    : ControllerBase
{
    [HttpPost("address-suggestions", Name = RouteNames.GetAddressSuggestions)]
    [EndpointSummary("Get private address suggestions")]
    [EndpointDescription("Returns bounded tenant-visible local address suggestions. Provider selection and credentials are server-owned.")]
    [Consumes("application/json")]
    [Produces("application/hal+json", "application/json")]
    [ProducesResponseType(
        typeof(HalResource<AddressSuggestionsResponseDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AddressSuggestionsPolicy)]
    public async Task<ActionResult<HalResource<AddressSuggestionsResponseDto>>>
        GetAddressSuggestions(
            [FromBody] AddressSuggestionsRequestDto request,
            CancellationToken cancellationToken = default)
    {
        AddressSuggestionsResponseDto response = await mediator.Send(
            new GetAddressSuggestionsQuery(tenantContext.TenantId, request),
            cancellationToken);
        HalCollectionResource<AddressSuggestionDto> suggestionResources =
            await assembler.ToCollectionResource(
                response.Suggestions,
                RouteNames.GetAddressSuggestions,
                HttpContext);
        var resource = new HalResource<AddressSuggestionsResponseDto>(
            response,
            suggestionResources.Links)
        {
            Embedded = new Dictionary<string, object>
            {
                ["items"] = suggestionResources.Embedded.Items
            }
        };
        return Ok(resource);
    }
}
