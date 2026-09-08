// ABOUTME: Exposes ordinary protected Local password change separately from anonymous recovery and first-use replacement.
// ABOUTME: Derives current session authority from the validated principal and never returns replacement session credentials.

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/auth/local/password")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class LocalPasswordController(ISender sender) : ControllerBase
{
    [HttpPost(Name = RouteNames.ChangeLocalPassword)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [RequestSizeLimit(16384)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EndpointSummary("Change Local password")]
    [EndpointDescription("Requires an ordinary current Local session and current password. Does not use email delivery or first-use replacement authority; success requires a fresh login.")]
    public async Task<ActionResult> Change(
        [FromBody] LocalPasswordChangeRequestDto body, CancellationToken cancellationToken = default) =>
        LocalIdentityLifecycleFailurePolicy.Instance.Map(this,
            await sender.Send(new ChangeLocalPasswordCommand(body, User.TryGetLocalSessionAuthority()), cancellationToken),
            onSuccess: NoContent);
}
