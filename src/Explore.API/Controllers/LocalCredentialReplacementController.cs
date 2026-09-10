
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/auth/local/credential-replacement")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
[Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.LocalCredentialReplacement)]
public sealed class LocalCredentialReplacementController(ISender sender) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new ApiValidationProblemDescriptor(
            ErrorKey: "newPassword", Title: "Password replacement failed",
            FallbackDetail: "The proposed password could not be accepted."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Conflict(title: "Password replacement conflict", fallbackDetail: "The credential operation changed.",
            failureCodes: [FailureCodes.ConcurrencyConflict]);

    [HttpPost(Name = RouteNames.CompleteLocalCredentialReplacement)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [EndpointSummary("Replace a first-use Local password")]
    [EndpointDescription("Accepts only a short-lived replacement challenge; completion requires a fresh ordinary login.")]
    public async Task<ActionResult> Complete(
        [FromBody] LocalCredentialReplacementRequestDto body,
        CancellationToken cancellationToken = default)
    {
        LocalCredentialReplacementAuthority? authority = User.TryGetLocalCredentialReplacementAuthority();
        if (authority is null)
        {
            return Failures.Map(this, BaseCommandResponse.Authentication<Guid>());
        }
        BaseCommandResponse<Guid> response = await sender.Send(
            new CompleteLocalCredentialReplacementCommand(request: new LocalCredentialReplacementRequest(
                authority: authority, newPassword: body.NewPassword)), cancellationToken);
        return Failures.Map(this, response, onSuccess: NoContent);
    }
}
