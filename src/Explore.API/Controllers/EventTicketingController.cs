using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.DTOs.OrganizerPaymentConnections;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Features.EventTicketing.Requests.Queries;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Application.Features.OrganizerPaymentConnections.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/events/{eventId:guid}/ticketing")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class EventTicketingController(
    IMediator mediator,
    IResourceAssembler<EventTicketCatalogManagementDto, EventTicketCatalogManagementDto> assembler,
    IResourceAssembler<PaidEventPublicationPreflightDto, PaidEventPublicationPreflightDto> preflightAssembler,
    IResourceAssembler<EventOrganizerPaymentConnectionManagementDto, EventOrganizerPaymentConnectionManagementDto> paymentConnectionAssembler) : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor TicketingValidationProblem = new(
        "eventTicketing",
        "Event ticketing validation failed",
        "Event ticketing command failed.");

    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Resource not found",
        "The requested resource was not found.");

    private static readonly CommandFailurePolicy TicketingFailures = CommandFailurePolicy
        .ValidatedBy(TicketingValidationProblem)
        .NotFound(NotFoundProblem, "event_ticketing_not_found")
        .Conflict(
            "Event ticketing conflict",
            "Event ticketing configuration was updated by another request.",
            "event_ticketing_concurrency_conflict");

    private static readonly CommandFailurePolicy OrganizerPaymentFailures = CommandFailurePolicy
        .ValidatedBy(TicketingValidationProblem)
        .NotFound(NotFoundProblem, "organizer_payment_event_not_found");

    [HttpGet("", Name = RouteNames.GetEventTicketCatalogManagement)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<EventTicketCatalogManagementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventTicketCatalogManagementDto>>> Get(Guid eventId, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new GetEventTicketCatalogManagementQuery(eventId), cancellationToken);
        if (dto is null)
        {
            return this.ToNotFoundProblem(NotFoundProblem);
        }

        var result = new ObjectResult(await assembler.ToResource(dto, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }

    [HttpPost("draft", Name = RouteNames.CreateEventTicketCatalogDraft)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CreateDraft(Guid eventId, [FromBody] CreateEventTicketCatalogDraftCommand command, CancellationToken ct) => SendCreated(new CreateEventTicketCatalogDraftCommand { EventId = eventId, CurrencyCode = command.CurrencyCode }, RouteNames.GetEventTicketCatalogManagement, new { eventId }, ct);

    [HttpPost("draft:clone", Name = RouteNames.CloneEventTicketCatalogDraft)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CloneDraft(Guid eventId, CancellationToken ct) => SendCreated(new CloneEventTicketCatalogDraftCommand { EventId = eventId }, RouteNames.GetEventTicketCatalogManagement, new { eventId }, ct);

    [HttpPost("ticket-types", Name = RouteNames.CreateEventTicketType)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CreateType(Guid eventId, [FromBody] ManageEventTicketTypeDto ticketType, CancellationToken ct) => SendCreated(new CreateEventTicketTypeCommand { EventId = eventId, TicketType = ticketType }, RouteNames.GetEventTicketCatalogManagement, new { eventId }, ct);

    [HttpPut("ticket-types/{ticketTypeId:guid}", Name = RouteNames.UpdateEventTicketType)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateType(Guid eventId, Guid ticketTypeId, [FromBody] ManageEventTicketTypeDto ticketType, CancellationToken ct) => SendOk(new UpdateEventTicketTypeCommand { EventId = eventId, TicketTypeId = ticketTypeId, TicketType = ticketType }, ct);

    [HttpDelete("ticket-types/{ticketTypeId:guid}", Name = RouteNames.DeleteEventTicketType)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeleteType(Guid eventId, Guid ticketTypeId, CancellationToken ct) => SendOk(new DeleteEventTicketTypeCommand { EventId = eventId, TicketTypeId = ticketTypeId }, ct);

    [HttpPost("capacity-pools", Name = RouteNames.CreateEventCapacityPool)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CreatePool(Guid eventId, [FromBody] ManageEventCapacityPoolDto capacityPool, CancellationToken ct) => SendCreated(new CreateEventCapacityPoolCommand { EventId = eventId, CapacityPool = capacityPool }, RouteNames.GetEventTicketCatalogManagement, new { eventId }, ct);

    [HttpPut("capacity-pools/{capacityPoolId:guid}", Name = RouteNames.UpdateEventCapacityPool)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdatePool(Guid eventId, Guid capacityPoolId, [FromBody] ManageEventCapacityPoolDto capacityPool, CancellationToken ct) => SendOk(new UpdateEventCapacityPoolCommand { EventId = eventId, CapacityPoolId = capacityPoolId, CapacityPool = capacityPool }, ct);

    [HttpDelete("capacity-pools/{capacityPoolId:guid}", Name = RouteNames.DeleteEventCapacityPool)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeletePool(Guid eventId, Guid capacityPoolId, CancellationToken ct) => SendOk(new DeleteEventCapacityPoolCommand { EventId = eventId, CapacityPoolId = capacityPoolId }, ct);

    [HttpGet("publication-preflight", Name = RouteNames.GetPaidEventPublicationPreflight)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<PaidEventPublicationPreflightDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<PaidEventPublicationPreflightDto>>> Preflight(Guid eventId, CancellationToken ct)
    {
        PaidEventPublicationPreflightDto dto = await mediator.Send(new GetPaidEventPublicationPreflightQuery(eventId), ct);
        var result = new ObjectResult(await preflightAssembler.ToResource(dto, HttpContext)) { StatusCode = StatusCodes.Status200OK };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }

    [HttpPut("commercial-disclosures", Name = RouteNames.UpdateEventTicketCatalogCommercialDisclosures)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateCommercialDisclosures(Guid eventId, [FromBody] UpdateEventTicketCatalogCommercialDisclosuresCommand command, CancellationToken ct) => SendOk(new UpdateEventTicketCatalogCommercialDisclosuresCommand
    {
        EventId = eventId,
        MerchantDisclosureText = command.MerchantDisclosureText,
        RefundPolicyDisclosureText = command.RefundPolicyDisclosureText,
        SupportContactDisclosureText = command.SupportContactDisclosureText
    }, ct);

    [HttpGet("payment-connection", Name = RouteNames.GetEventOrganizerPaymentConnection)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<EventOrganizerPaymentConnectionManagementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventOrganizerPaymentConnectionManagementDto>>> GetPaymentConnection(Guid eventId, CancellationToken ct)
    {
        EventOrganizerPaymentConnectionManagementDto? dto = await mediator.Send(new GetEventOrganizerPaymentConnectionQuery(eventId), ct);
        if (dto is null)
        {
            return this.ToNotFoundProblem(NotFoundProblem);
        }

        var result = new ObjectResult(await paymentConnectionAssembler.ToResource(dto, HttpContext)) { StatusCode = StatusCodes.Status200OK };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }

    [HttpPost("payment-connection/onboarding", Name = RouteNames.StartEventOrganizerPaymentOnboarding)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<OrganizerPaymentOnboardingLinkResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<OrganizerPaymentOnboardingLinkResult>>> StartPaymentOnboarding(Guid eventId, CancellationToken ct)
    {
        if (!TryGenerateAbsoluteRouteUrl(RouteNames.ReturnEventOrganizerPaymentOnboarding, eventId, out Uri? returnUrl)
            || !TryGenerateAbsoluteRouteUrl(RouteNames.RefreshEventOrganizerPaymentOnboarding, eventId, out Uri? refreshUrl))
        {
            return this.ToCommandValidationProblem(
                BaseCommandResponse.Failure<OrganizerPaymentOnboardingLinkResult>(
                    "organizer_payment_onboarding_navigation_invalid",
                    "Payment onboarding navigation URLs could not be generated.",
                    ["Payment onboarding navigation URLs could not be generated."]),
                TicketingValidationProblem);
        }

        BaseCommandResponse<OrganizerPaymentOnboardingLinkResult> response = await mediator.Send(new CreateOrganizerPaymentOnboardingLinkCommand(eventId, returnUrl, refreshUrl), ct);
        return response.IsSuccess ? Ok(response) : OrganizerPaymentFailures.Map(this, response);
    }

    [HttpGet("payment-connection/onboarding/return", Name = RouteNames.ReturnEventOrganizerPaymentOnboarding)]
    [PrivateNoStore]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult ReturnPaymentOnboarding(Guid eventId) => RedirectToStudioTicketing(eventId);

    [HttpGet("payment-connection/onboarding/refresh", Name = RouteNames.RefreshEventOrganizerPaymentOnboarding)]
    [PrivateNoStore]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult RefreshPaymentOnboarding(Guid eventId) => RedirectToStudioTicketing(eventId);

    [HttpPost("publish", Name = RouteNames.PublishEventTicketCatalog)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> Publish(Guid eventId, CancellationToken ct) => SendOk(new PublishEventTicketCatalogCommand { EventId = eventId }, ct);

    private async Task<ActionResult<BaseCommandResponse<Guid>>> SendOk<T>(T command, CancellationToken ct)
        where T : IRequest<BaseCommandResponse<Guid>>
    {
        BaseCommandResponse<Guid> response = await mediator.Send(command, ct);
        return response.IsSuccess ? Ok(response) : TicketingFailures.Map(this, response);
    }

    private async Task<ActionResult<BaseCommandResponse<Guid>>> SendCreated<T>(
        T command,
        string route,
        object values,
        CancellationToken ct)
        where T : IRequest<BaseCommandResponse<Guid>>
    {
        BaseCommandResponse<Guid> response = await mediator.Send(command, ct);
        return response.IsSuccess ? CreatedAtRoute(route, values, response) : TicketingFailures.Map(this, response);
    }

    private bool TryGenerateAbsoluteRouteUrl(
        string routeName,
        Guid eventId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        string? value = Url.RouteUrl(routeName, new { eventId }, Request.Scheme);
        return Uri.TryCreate(value, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static RedirectResult RedirectToStudioTicketing(Guid eventId) => new($"/studio/events/{eventId:D}/tickets");
}
