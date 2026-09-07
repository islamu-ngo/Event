using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.Hateoas;
using Explore.Application.Constants;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Features.PrivacyErasure.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/privacy-erasure")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class PrivacyErasureController(IMediator mediator) : ControllerBase
{
    [HttpGet("status", Name = RouteNames.GetPrivacyErasureStatus)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.PrivacyErasureReceipt)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PrivacyErasureStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PrivacyErasureStatusDto>> GetStatus(
        CancellationToken cancellationToken = default)
    {
        string? claim = User.FindFirst(PrivacyErasureReceiptAuthenticationHandler.IntentIdClaim)?.Value;
        if (!Guid.TryParse(claim, out Guid intentId))
        {
            return Unauthorized();
        }

        PrivacyErasureStatusDto? status = await mediator.Send(
            new GetPrivacyErasureStatusQuery(intentId),
            cancellationToken);
        return status is null ? Unauthorized() : Ok(status);
    }
}
