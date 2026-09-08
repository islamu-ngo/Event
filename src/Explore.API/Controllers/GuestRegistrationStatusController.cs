// ABOUTME: Exposes private post-confirmation guest status and cancellation through the existing capability header.
// ABOUTME: Uses a separate HAL family and private generic errors without granting checkout or attendee calendar access.

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Application.Features.RegistrationOrders.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/events/{eventId:guid}/guest-registration-orders/{orderId:guid}")]
public sealed class GuestRegistrationStatusController(
    ISender sender,
    IResourceAssembler<GuestRegistrationStatusDto, GuestRegistrationStatusDto> assembler,
    TimeProvider timeProvider) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor MissingStatus = new(
        "Registration order not found", "Registration order not found.");

    private static readonly CommandFailurePolicy CancellationFailures = CommandFailurePolicy
        .ValidatedBy(new ApiValidationProblemDescriptor(
            "registrationOrder", "Registration cancellation failed", "The registration could not be cancelled."))
        .NotFound(MissingStatus, "registration_order_not_found")
        .Conflict("Registration cancellation unavailable", "This registration cannot be cancelled.",
            "guest_registration_cancellation_ineligible");

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [HttpPost("cancellation", Name = RouteNames.CancelConfirmedGuestRegistration)]
    [EndpointSummary("Cancel an eligible confirmed guest registration")]
    [EndpointDescription("Rechecks the exact event, order and capability header against current server eligibility. Successful cancellation and authorized duplicates return no content. Does not authorize paid refunds, staff correction or checkout.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult> CancelConfirmed(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = "X-Registration-Order-Capability")] string? capability,
        CancellationToken cancellationToken = default) =>
        CancellationFailures.Map(this, await sender.Send(
            new CancelConfirmedGuestRegistrationCommand(eventId, orderId, capability), cancellationToken),
            onSuccess: NoContent);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("status", Name = RouteNames.GetGuestRegistrationStatus)]
    [EndpointSummary("Read private guest registration status")]
    [EndpointDescription("Returns a PII-free confirmed guest status only for the exact event, order and capability header until the disclosed access deadline. Does not authorize checkout, changes or private calendar details.")]
    [ProducesResponseType(typeof(HalResource<GuestRegistrationStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<GuestRegistrationStatusDto>>> GetStatus(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = "X-Registration-Order-Capability")] string? capability,
        CancellationToken cancellationToken = default)
    {
        GuestRegistrationStatusDto? status = await sender.Send(
            new GetGuestRegistrationStatusQuery(eventId, orderId, capability), cancellationToken);
        if (status is null)
        {
            return this.ToNotFoundProblem(MissingStatus);
        }

        HalResource<GuestRegistrationStatusDto> resource = await assembler.ToResource(status, HttpContext);
        return timeProvider.GetUtcNow() >= status.StatusAccessUntil
            ? this.ToNotFoundProblem(MissingStatus)
            : Ok(resource);
    }
}
