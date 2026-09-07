using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
public sealed class TicketPurchaseController(IMediator mediator) : ControllerBase
{
    private const string CapabilityHeader = "X-Registration-Order-Capability";
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly ApiValidationProblemDescriptor PurchaseValidationProblem = new(
        "registrationOrder",
        "Registration order request failed",
        "Registration order request failed.");

    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Registration order not found",
        "Registration order was not found.");

    private static readonly CommandFailurePolicy Failures =
        CommandFailurePolicy
            .ValidatedBy(PurchaseValidationProblem)
            .NotFound(
                NotFoundProblem,
                "registration_order_not_found",
                "ticket_purchase_order_unavailable",
                "ticket_purchase_policy_unavailable")
            .Conflict(
                "Ticket purchase conflict",
                "Ticket purchase authority could not be reserved.",
                "ticket_purchase_ceiling_exceeded",
                "ticket_purchase_operation_conflict")
            .Forbidden(
                "Purchase authority denied",
                "Purchase authority could not be established.",
                "ticket_purchase_authority_unavailable")
            .Unavailable(
                "Ticket purchase unavailable",
                "Ticket purchase authority is temporarily unavailable.",
                "ticket_purchase_unavailable");

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [PrivateNoStore]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control")]
    [HttpPost(
        "{orderId:guid}/purchase-authority",
        Name = RouteNames.ReserveAuthenticatedPurchaseAuthority)]
    [EndpointSummary("Reserve authenticated ticket purchase authority")]
    [EndpointDescription(
        "Reserves the server-derived order quantity against the authenticated account ceiling. Actor context is verified server-side.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<TicketPurchaseGovernanceResource>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<TicketPurchaseGovernanceResource>>>
        ReserveAuthenticatedPurchaseAuthority(
            Guid eventId,
            Guid orderId,
            [FromBody] ReserveTicketPurchaseRequest? request,
            [FromHeader(Name = IdempotencyKeyHeader)] string operationKey,
            CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return this.ToValidationProblem(
                PurchaseValidationProblem,
                "A ticket-purchase payload is required.");
        }

        BaseCommandResponse<Guid> response = await mediator.Send(
            new ReserveAuthenticatedTicketPurchaseCommand(
                eventId,
                orderId,
                request.RequestedPurchaserActorId,
                operationKey),
            cancellationToken);
        return Map(
            response,
            eventId,
            orderId,
            TicketPurchaseAccessMode.AuthenticatedAccount,
            RouteNames.ReserveAuthenticatedPurchaseAuthority);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [PrivateNoStore]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control")]
    [HttpPost(
        "guest/{orderId:guid}/purchase-authority",
        Name = RouteNames.ReserveGuestPurchaseAuthority)]
    [EndpointSummary("Reserve guest ticket purchase authority")]
    [EndpointDescription(
        "Uses the opaque order capability and either persisted verified-contact authority or honest order-scoped name-only controls. Name-only access has no hard cross-order per-person guarantee.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<TicketPurchaseGovernanceResource>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<TicketPurchaseGovernanceResource>>>
        ReserveGuestPurchaseAuthority(
            Guid eventId,
            Guid orderId,
            [FromHeader(Name = CapabilityHeader)] string? capability,
            [FromBody] ReserveTicketPurchaseRequest? request,
            [FromHeader(Name = IdempotencyKeyHeader)] string operationKey,
            CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return this.ToValidationProblem(
                PurchaseValidationProblem,
                "A ticket-purchase payload is required.");
        }

        BaseCommandResponse<Guid> response = await mediator.Send(
            new ReserveGuestTicketPurchaseCommand(
                eventId,
                orderId,
                request.AccessMode,
                capability,
                operationKey),
            cancellationToken);
        return Map(
            response,
            eventId,
            orderId,
            request.AccessMode,
            RouteNames.ReserveGuestPurchaseAuthority);
    }

    private ActionResult<HalResource<TicketPurchaseGovernanceResource>> Map(
        BaseCommandResponse<Guid> response,
        Guid eventId,
        Guid orderId,
        TicketPurchaseAccessMode accessMode,
        string routeName)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!response.IsSuccess)
        {
            return Failures.Map(this, response);
        }

        bool hardCrossOrderCeiling =
            accessMode != TicketPurchaseAccessMode.NameOnly;
        var resource = new TicketPurchaseGovernanceResource
        {
            OrderId = orderId,
            AccessMode = accessMode,
            SupportsHardCrossOrderCeiling = hardCrossOrderCeiling,
            EnforcementScopeCode = hardCrossOrderCeiling
                ? "stable-authority"
                : "order",
        };
        return Ok(new HalResource<TicketPurchaseGovernanceResource>(resource)
            .WithSelfLink(Url.Link(
                routeName,
                new { eventId, orderId })!));
    }
}
