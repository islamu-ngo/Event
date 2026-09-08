// ABOUTME: Exposes anonymous native Local recovery admission and purpose-bound one-use completion.
// ABOUTME: Keeps public account eligibility private and never issues an ordinary session on recovery.

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Features.Authentication.Local;
using Explore.Application.Features.Authentication.Local.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/auth/local/password-recoveries")]
[ApiController]
public sealed class LocalPasswordRecoveryController(ISender sender) : ControllerBase
{
    [HttpPost(Name = RouteNames.RequestLocalPasswordRecovery)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [RequestSizeLimit(16384)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EndpointSummary("Request Local password recovery")]
    [EndpointDescription("Accepts recovery requests uniformly for missing, non-Local, and ineligible targets. Delivery requires a verified address and ready Local credential binding.")]
    public async Task<ActionResult> RequestRecovery(
        [FromBody] LocalPasswordRecoveryRequestDto body, CancellationToken cancellationToken = default) =>
        LocalIdentityLifecycleFailurePolicy.Instance.Map(this,
            await sender.Send(new RequestLocalPasswordRecoveryCommand(body), cancellationToken), onSuccess: Accepted);

    [HttpPost("consume", Name = RouteNames.CompleteLocalPasswordRecovery)]
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
    [EndpointSummary("Complete Local password recovery")]
    [EndpointDescription("Consumes only the specified recovery operation. Completion requires a fresh ordinary login and never returns session credentials.")]
    public async Task<ActionResult> Consume(
        [FromBody] LocalPasswordRecoveryCompletionRequestDto body, CancellationToken cancellationToken = default) =>
        LocalIdentityLifecycleFailurePolicy.Instance.Map(this,
            await sender.Send(new CompleteLocalPasswordRecoveryCommand(body), cancellationToken), onSuccess: NoContent);
}
