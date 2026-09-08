using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Features.EventAddOns.Requests.Commands;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route(
    "api/events/{eventId:guid}/registration-orders/" +
    "{registrationOrderId:guid}/add-ons")]
public sealed class RegistrationOrderAddOnController(
    IMediator mediator,
    IResourceAssembler<
        RegistrationOrderAddOnSummaryDto,
        RegistrationOrderAddOnSummaryDto> assembler,
    TimeProvider timeProvider) : ControllerBase
{
    private const string CapabilityHeader = "X-Registration-Order-Capability";
    private static readonly ApiNotFoundProblemDescriptor Unavailable = new(
        "Add-on order unavailable",
        "The add-on order was not found or is unavailable.",
        "registration_order_add_on_unavailable");

    [HttpGet("", Name = RouteNames.GetRegistrationOrderAddOns)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [ProducesResponseType(
        typeof(HalResource<RegistrationOrderAddOnSummaryDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<HalResource<RegistrationOrderAddOnSummaryDto>>> Get(
        Guid eventId,
        Guid registrationOrderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            mediator.Send(
                new GetRegistrationOrderAddOnsQuery(
                    eventId,
                    registrationOrderId,
                    capability),
                cancellationToken));

    [HttpPost("", Name = RouteNames.ReserveRegistrationOrderAddOns)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(EndpointClass.Authenticated)]
    public Task<ActionResult<HalResource<RegistrationOrderAddOnSummaryDto>>> Reserve(
        Guid eventId,
        Guid registrationOrderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] ReserveEventAddOnsRequest request,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            mediator.Send(
                new ReserveRegistrationOrderAddOnsCommand(
                    eventId,
                    registrationOrderId,
                    request.CatalogId,
                    request.Selections.Select(selection =>
                        new EventAddOnSelection(
                            selection.CatalogItemId,
                            selection.Quantity)),
                    Guid.CreateVersion7(),
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken));

    [HttpPost(
        "{registrationOrderAddOnLineId:guid}/fulfillment",
        Name = RouteNames.FulfillRegistrationOrderAddOn)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(EndpointClass.Authenticated)]
    public Task<ActionResult<HalResource<RegistrationOrderAddOnSummaryDto>>> Fulfill(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderAddOnLineId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            mediator.Send(
                new FulfillRegistrationOrderAddOnCommand(
                    eventId,
                    registrationOrderId,
                    registrationOrderAddOnLineId,
                    Guid.CreateVersion7(),
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken));

    [HttpPost(
        "{registrationOrderAddOnLineId:guid}/refunds",
        Name = RouteNames.RefundRegistrationOrderAddOn)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(EndpointClass.Authenticated)]
    public Task<ActionResult<HalResource<RegistrationOrderAddOnSummaryDto>>> Refund(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderAddOnLineId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RefundEventAddOnRequest request,
        CancellationToken cancellationToken) =>
        ResourceAsync(
            mediator.Send(
                new RefundRegistrationOrderAddOnCommand(
                    eventId,
                    registrationOrderId,
                    registrationOrderAddOnLineId,
                    Guid.CreateVersion7(),
                    request.Quantity,
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken));

    private async Task<ActionResult<HalResource<RegistrationOrderAddOnSummaryDto>>> ResourceAsync(
        Task<RegistrationOrderAddOnSummaryDto?> pending)
    {
        RegistrationOrderAddOnSummaryDto? dto = await pending;
        return dto is null
            ? this.ToNotFoundProblem(Unavailable)
            : Ok(await assembler.ToResource(dto, HttpContext));
    }
}
