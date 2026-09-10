
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
[Route("api/auth/local/email-verifications")]
[ApiController]
public sealed class LocalEmailVerificationController(ISender sender) : ControllerBase
{
    [HttpPost(Name = RouteNames.RequestLocalEmailVerification)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [RequestSizeLimit(16384)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EndpointSummary("Request Local email verification")]
    [EndpointDescription("Requests current-address verification without revealing account eligibility. A proposed address requires a current ordinary Local session and cannot select another account.")]
    public async Task<ActionResult> RequestVerification(
        [FromBody] LocalEmailVerificationRequestDto body, CancellationToken cancellationToken = default) =>
        LocalIdentityLifecycleFailurePolicy.Instance.Map(this,
            await sender.Send(new RequestLocalEmailVerificationCommand(body, User.TryGetLocalSessionAuthority()), cancellationToken),
            onSuccess: Accepted);

    [HttpPost("consume", Name = RouteNames.ConfirmLocalEmail)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [RequestSizeLimit(16384)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [EndpointSummary("Confirm Local email")]
    [EndpointDescription("Consumes an exact verification or email-change operation and synchronizes its linked profile. Does not sign in or issue session credentials.")]
    public async Task<ActionResult> Consume(
        [FromBody] LocalEmailConfirmationRequestDto body, CancellationToken cancellationToken = default) =>
        LocalIdentityLifecycleFailurePolicy.Instance.Map(this,
            await sender.Send(new ConfirmLocalEmailCommand(body), cancellationToken), onSuccess: NoContent);
}
