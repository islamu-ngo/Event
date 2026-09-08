using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Features.Admissions.Requests.Commands;
using Explore.Application.Features.Admissions.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route(
    "api/events/{eventId:guid}/admission-tickets/" +
    "{admissionTicketId:guid}/transfers")]
[ApiController]
public sealed class TicketTransferController(
    IMediator mediator,
    IResourceAssembler<
        TicketTransferDto,
        TicketTransferDto> assembler) :
    ControllerBase
{
    private const string CapabilityHeader =
        "X-Ticket-Transfer-Capability";
    private static readonly ApiNotFoundProblemDescriptor
        TransferNotFound = new(
            "Ticket transfer unavailable",
            "Ticket transfer was not found or is unavailable.",
            "ticket_transfer_unavailable");

    [HttpGet(
        "{transferId:guid}",
        Name = RouteNames.GetTicketTransfer)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [ProducesResponseType(
        typeof(HalResource<TicketTransferDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<TicketTransferDto>>> Get(
            Guid eventId,
            Guid admissionTicketId,
            Guid transferId,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken)
    {
        TicketTransferDto? transfer =
            await mediator.Send(
                new GetTicketTransferQuery(
                    eventId,
                    admissionTicketId,
                    transferId,
                    capabilityToken),
                cancellationToken);
        if (transfer is null)
        {
            return this.ToNotFoundProblem(
                TransferNotFound);
        }

        return Ok(await assembler.ToResource(
            transfer,
            HttpContext));
    }

    [HttpPost("", Name = RouteNames.OfferTicketTransfer)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(TicketTransferOfferResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        TicketTransferOfferResponse>> Offer(
            Guid eventId,
            Guid admissionTicketId,
            CancellationToken cancellationToken)
    {
        TicketTransferOfferDto? result =
            await mediator.Send(
                new OfferTicketTransferCommand(
                    eventId,
                    admissionTicketId),
                cancellationToken);
        if (result is null)
        {
            return this.ToNotFoundProblem(
                TransferNotFound);
        }

        var response = new TicketTransferOfferResponse
        {
            Transfer = await assembler.ToResource(
                result.Transfer,
                HttpContext),
            ClaimCapability =
                result.ClaimCapability,
        };
        return CreatedAtRoute(
            RouteNames.GetTicketTransfer,
            new
            {
                eventId,
                admissionTicketId,
                transferId = result.Transfer.Id,
            },
            response);
    }

    [HttpPost(
        "{transferId:guid}/accept",
        Name = RouteNames.AcceptTicketTransfer)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(TicketTransferCredentialResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        TicketTransferCredentialResponse>> Accept(
            Guid eventId,
            Guid admissionTicketId,
            Guid transferId,
            AcceptTicketTransferRequest request,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken)
    {
        TicketTransferAcceptanceDto? result =
            await mediator.Send(
                new AcceptTicketTransferCommand(
                    eventId,
                    admissionTicketId,
                    transferId,
                    request.RecipientParticipantId,
                    capabilityToken),
                cancellationToken);
        return await CredentialResponseAsync(result);
    }

    [HttpDelete(
        "{transferId:guid}",
        Name = RouteNames.CancelTicketTransfer)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(HalResource<TicketTransferDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<TicketTransferDto>>> Cancel(
            Guid eventId,
            Guid admissionTicketId,
            Guid transferId,
            CancellationToken cancellationToken)
    {
        TicketTransferDto? result =
            await mediator.Send(
                new CancelTicketTransferCommand(
                    eventId,
                    admissionTicketId,
                    transferId),
                cancellationToken);
        if (result is null)
        {
            return this.ToNotFoundProblem(
                TransferNotFound);
        }

        return Ok(await assembler.ToResource(
            result,
            HttpContext));
    }

    [HttpPost(
        "{transferId:guid}/correction",
        Name = RouteNames.CorrectTicketTransfer)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(TicketTransferCredentialResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        TicketTransferCredentialResponse>> Correct(
            Guid eventId,
            Guid admissionTicketId,
            Guid transferId,
            CancellationToken cancellationToken) =>
        await CredentialResponseAsync(
            await mediator.Send(
                new CorrectTicketTransferCommand(
                    eventId,
                    admissionTicketId,
                    transferId),
                cancellationToken));

    [HttpPost(
        "{transferId:guid}/reissue",
        Name = RouteNames.ReissueTransferredTicket)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(TicketTransferCredentialResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        TicketTransferCredentialResponse>> Reissue(
            Guid eventId,
            Guid admissionTicketId,
            Guid transferId,
            CancellationToken cancellationToken) =>
        await CredentialResponseAsync(
            await mediator.Send(
                new ReissueTransferredTicketCommand(
                    eventId,
                    admissionTicketId,
                    transferId),
                cancellationToken));

    private async Task<ActionResult<
        TicketTransferCredentialResponse>>
        CredentialResponseAsync(
            TicketTransferAcceptanceDto? result)
    {
        if (result is null)
        {
            return this.ToNotFoundProblem(
                TransferNotFound);
        }

        return Ok(new TicketTransferCredentialResponse
        {
            Transfer = await assembler.ToResource(
                result.Transfer,
                HttpContext),
            Credential = result.Credential,
        });
    }
}
