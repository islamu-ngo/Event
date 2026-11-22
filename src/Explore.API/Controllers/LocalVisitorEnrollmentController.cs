using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/auth/local/enrollment")]
[ApiController]
[AllowAnonymous]
[EndpointClassification(EndpointClass.Public)]
public sealed class LocalVisitorEnrollmentController(
    ICommandHandler<ReserveLocalVisitorEnrollmentCommand, LocalVisitorEnrollmentReservationCommandResponse> reserve,
    ICommandHandler<RegisterLocalVisitorCommand, BaseCommandResponse<Guid>> register) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new ApiValidationProblemDescriptor(
            ErrorKey: "request", Title: "Local enrollment rejected",
            FallbackDetail: "The enrollment request could not be accepted."))
        .Forbidden(title: "Original enrollment proof required",
            detail: "Restart enrollment through the original browser flow.",
            failureCodes: [FailureCodes.LocalEnrollmentProofInvalid])
        .Conflict(title: "Local enrollment unavailable",
            fallbackDetail: "Enrollment is closed or the original operation changed.",
            failureCodes: [FailureCodes.LocalEnrollmentClosed]);

    [HttpPost("reserve", Name = RouteNames.ReserveLocalVisitorEnrollment)]
    [TenantReadBootstrap]
    [SuppressIdempotencyResponseStorage]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Reserve a browser-bound Local enrollment")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LocalVisitorEnrollmentReservation), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> Reserve(
        [FromBody] LocalVisitorEnrollmentReservationRequestDto request,
        [FromHeader(Name = LocalVisitorBrowserProof.HeaderName)] string browserProof,
        CancellationToken cancellationToken = default)
    {
        var response = await reserve.ExecuteAsync(
            new ReserveLocalVisitorEnrollmentCommand(request.OperationId, browserProof), cancellationToken);
        return response.IsSuccess ? Ok(response.Reservation) : Failures.Map(this, response);
    }

    [HttpPost("register", Name = RouteNames.RegisterLocalVisitor)]
    [TenantReadBootstrap]
    [SuppressIdempotencyResponseStorage]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Register the originally reserved Local visitor")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> Register(
        [FromBody] LocalVisitorRegistrationRequestDto request,
        [FromHeader(Name = LocalVisitorBrowserProof.HeaderName)] string browserProof,
        CancellationToken cancellationToken = default)
    {
        var response = await register.ExecuteAsync(new RegisterLocalVisitorCommand(request, browserProof), cancellationToken);
        return response.IsSuccess ? Ok(response) : Failures.Map(this, response);
    }
}
