using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Waitlist;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class FairReturnWaitlistLinkPolicy :
    ILinkPolicy<FairReturnWaitlistDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        FairReturnWaitlistDto dto,
        ClaimsPrincipal? user)
    {
        var values = new
        {
            eventId = dto.EventId,
            registrationOrderId =
                dto.RegistrationOrderId,
            registrationOrderLineId =
                dto.RegistrationOrderLineId,
        };
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetFairReturnWaitlist,
            values,
            HttpMethods.Get);
        if (dto.CanJoin && dto.AllocationOpen)
        {
            yield return new LinkDefinition(
                LinkRelations.JoinFairReturnWaitlist,
                RouteNames.JoinFairReturnWaitlist,
                values,
                HttpMethods.Post,
                "Join waitlist",
                RequiresAuth: true);
        }
        if (dto.CanLeave && dto.WithdrawalOpen)
        {
            yield return new LinkDefinition(
                LinkRelations.LeaveFairReturnWaitlist,
                RouteNames.LeaveFairReturnWaitlist,
                values,
                HttpMethods.Delete,
                "Leave waitlist",
                RequiresAuth: true);
        }
        if (dto.CanAcceptOffer
            && dto.AllocationOpen
            && dto.OfferId.HasValue)
        {
            yield return new LinkDefinition(
                LinkRelations.AcceptFairReturnOffer,
                RouteNames.AcceptFairReturnOffer,
                new
                {
                    dto.EventId,
                    dto.RegistrationOrderId,
                    dto.RegistrationOrderLineId,
                    offerId = dto.OfferId.Value,
                },
                HttpMethods.Post,
                "Accept waitlist offer",
                RequiresAuth: true);
        }
        if (dto.CanWithdrawSupply
            && dto.WithdrawalOpen
            && dto.SupplyId.HasValue)
        {
            yield return new LinkDefinition(
                LinkRelations.WithdrawFairReturnSupply,
                RouteNames.WithdrawFairReturnSupply,
                new
                {
                    dto.EventId,
                    dto.RegistrationOrderId,
                    dto.RegistrationOrderLineId,
                    supplyId = dto.SupplyId.Value,
                },
                HttpMethods.Delete,
                "Withdraw fair-return supply",
                RequiresAuth: true);
        }
    }
}

public sealed class
    FairReturnWaitlistCollectionLinkPolicy :
    ICollectionLinkPolicy<FairReturnWaitlistDto>;
