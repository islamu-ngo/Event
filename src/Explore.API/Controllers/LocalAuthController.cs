using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/auth/local")]
[ApiController]
[AllowAnonymous]
[EndpointClassification(EndpointClass.Public)]
public sealed class LocalAuthController(ISender sender) : ControllerBase
{
    [HttpPost("login", Name = RouteNames.LoginLocalIdentity)]
    [SuppressIdempotencyResponseStorage]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Sign in with Local Identity")]
    [EndpointDescription("Validates local credentials and returns a short-lived platform access token.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LocalAuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LocalAuthResponseDto>> Login(
        [FromBody] LocalAuthRequestDto request,
        CancellationToken cancellationToken = default)
    {
        LocalAuthResponseDto response = await sender.Send(
            new LocalLoginCommand(request),
            cancellationToken);
        return LocalAuthenticationResultMapper.Map(this, response);
    }
}
