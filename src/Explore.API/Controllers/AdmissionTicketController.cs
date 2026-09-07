using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Requests.Commands;
using Explore.Application.Features.AdmissionTickets.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Route("api/tickets")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class AdmissionTicketController(IMediator mediator) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor TicketNotFound = new(
        "Admission ticket not found",
        "The requested admission ticket was not found.");

    [HttpGet("", Name = RouteNames.GetCurrentAdmissionTickets)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [EndpointSummary("List current account admission tickets")]
    [ProducesResponseType(typeof(HalCollectionResource<AdmissionTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<AdmissionTicketDto>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AdmissionTicketDto> tickets = await mediator.Send(
            new GetCurrentAdmissionTicketsQuery(),
            cancellationToken);
        HalResource<AdmissionTicketDto>[] resources = tickets.Select(Resource).ToArray();
        return Ok(HalCollectionResource<AdmissionTicketDto>.Create(
            resources,
            1,
            Math.Max(1, resources.Length),
            resources.Length,
            new Dictionary<string, HalLink>
            {
                [LinkRelations.Self] = HalLink.Create("/api/tickets")
            }));
    }

    [HttpGet("{ticketId:guid}", Name = RouteNames.GetCurrentAdmissionTicket)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [EndpointSummary("Get an account admission ticket")]
    [ProducesResponseType(typeof(HalResource<AdmissionTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<AdmissionTicketDto>>> Detail(
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        AdmissionTicketDto? ticket = await mediator.Send(
            new GetCurrentAdmissionTicketQuery(ticketId),
            cancellationToken);
        return ticket is null
            ? this.ToNotFoundProblem(TicketNotFound)
            : Ok(Resource(ticket));
    }

    [HttpPost("{ticketId:guid}/qr", Name = RouteNames.ReissueCurrentAdmissionTicketQr)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [EndpointSummary("Explicitly reissue an account admission ticket for QR delivery")]
    [ProducesResponseType(typeof(AdmissionTicketQrDeliveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdmissionTicketQrDeliveryDto>> Qr(
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        AdmissionTicketQrDeliveryDto? delivery = await mediator.Send(
            new ReissueCurrentAdmissionTicketQrCommand(ticketId),
            cancellationToken);
        return delivery is null
            ? this.ToNotFoundProblem(TicketNotFound)
            : Ok(delivery);
    }

    [HttpPost("{ticketId:guid}/print", Name = RouteNames.ReissueCurrentAdmissionTicketPrint)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [EndpointSummary("Explicitly reissue an account admission ticket for print delivery")]
    [ProducesResponseType(typeof(AdmissionTicketPrintDeliveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdmissionTicketPrintDeliveryDto>> Print(
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        AdmissionTicketPrintDeliveryDto? delivery = await mediator.Send(
            new ReissueCurrentAdmissionTicketPrintCommand(ticketId),
            cancellationToken);
        return delivery is null
            ? this.ToNotFoundProblem(TicketNotFound)
            : Ok(delivery);
    }

    private static HalResource<AdmissionTicketDto> Resource(AdmissionTicketDto ticket)
    {
        string path = $"/api/tickets/{ticket.TicketId:D}";
        var links = new Dictionary<string, HalLink>
        {
            [LinkRelations.Self] = HalLink.CreateAction(path, HttpMethods.Get),
            ["registration-order"] = HalLink.CreateAction(
                $"/api/events/{ticket.EventId:D}/registration-orders/{ticket.RegistrationOrderId:D}",
                HttpMethods.Get)
        };
        if (string.Equals(ticket.StatusCode, "ACTIVE", StringComparison.Ordinal))
        {
            links["qr-code"] = HalLink.CreateAction(path + "/qr", HttpMethods.Post);
            links["print"] = HalLink.CreateAction(path + "/print", HttpMethods.Post);
        }

        return new HalResource<AdmissionTicketDto>(ticket, links);
    }
}
