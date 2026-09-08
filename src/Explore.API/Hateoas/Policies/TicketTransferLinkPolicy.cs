using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class TicketTransferLinkPolicy :
    ILinkPolicy<TicketTransferDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        TicketTransferDto dto,
        ClaimsPrincipal? user)
    {
        var itemValues = new
        {
            eventId = dto.EventId,
            admissionTicketId = dto.AdmissionTicketId,
            transferId = dto.Id,
        };
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTicketTransfer,
            itemValues,
            HttpMethods.Get);

        if (dto.CanOffer)
        {
            yield return new LinkDefinition(
                LinkRelations.OfferTicketTransfer,
                RouteNames.OfferTicketTransfer,
                new
                {
                    eventId = dto.EventId,
                    admissionTicketId =
                        dto.AdmissionTicketId,
                },
                HttpMethods.Post,
                "Offer ticket transfer",
                RequiresAuth: true);
        }
        if (dto.CanAccept)
        {
            yield return new LinkDefinition(
                LinkRelations.AcceptTicketTransfer,
                RouteNames.AcceptTicketTransfer,
                itemValues,
                HttpMethods.Post,
                "Accept ticket transfer",
                RequiresAuth: true);
        }
        if (dto.CanCancel)
        {
            yield return new LinkDefinition(
                LinkRelations.CancelTicketTransfer,
                RouteNames.CancelTicketTransfer,
                itemValues,
                HttpMethods.Delete,
                "Cancel ticket transfer",
                RequiresAuth: true);
        }
        if (dto.CanCorrect)
        {
            yield return new LinkDefinition(
                LinkRelations.CorrectTicketTransfer,
                RouteNames.CorrectTicketTransfer,
                itemValues,
                HttpMethods.Post,
                "Correct transferred ticket",
                RequiresAuth: true);
        }
        if (dto.CanReissue)
        {
            yield return new LinkDefinition(
                LinkRelations.ReissueTransferredTicket,
                RouteNames.ReissueTransferredTicket,
                itemValues,
                HttpMethods.Post,
                "Reissue transferred ticket",
                RequiresAuth: true);
        }
    }
}

public sealed class TicketTransferCollectionLinkPolicy :
    ICollectionLinkPolicy<TicketTransferDto>;
