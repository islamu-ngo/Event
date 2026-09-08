using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class EventOrganizerClaimDetailLinkPolicy : ILinkPolicy<EventOrganizerClaimDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventOrganizerClaimDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventOrganizerClaim,
            new { eventId = dto.EventId, claimId = dto.Id },
            HttpMethods.Get,
            "Organizer claim",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ViewOrganizerClaims, ResourceDescriptors.EventOrganizerClaim, dto);
        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            HttpMethods.Get,
            "Event");
        if (IsReviewable(dto.StatusId))
        {
            yield return new LinkDefinition(
                LinkRelations.WithdrawClaim,
                RouteNames.WithdrawEventOrganizerClaim,
                new { eventId = dto.EventId, claimId = dto.Id },
                HttpMethods.Post,
                "Withdraw organizer claim",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.WithdrawOrganizerClaim, ResourceDescriptors.EventOrganizerClaim, dto);
            yield return new LinkDefinition(
                LinkRelations.ReviewClaim,
                RouteNames.ReviewEventOrganizerClaim,
                new { eventId = dto.EventId, claimId = dto.Id },
                HttpMethods.Post,
                "Review organizer claim",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ReviewOrganizerClaim, ResourceDescriptors.EventOrganizerClaim, dto);
        }
    }

    private static bool IsReviewable(int statusId) => statusId is
        (int)EventOrganizerClaimStatusEnum.Pending or
        (int)EventOrganizerClaimStatusEnum.EvidenceRequired;
}

public sealed class EventOrganizerClaimCollectionLinkPolicy : ICollectionLinkPolicy<EventOrganizerClaimDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventOrganizerClaimDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventOrganizerClaim,
            new { eventId = dto.EventId, claimId = dto.Id },
            HttpMethods.Get,
            "Organizer claim",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ViewOrganizerClaims, ResourceDescriptors.EventOrganizerClaim, dto);
        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            HttpMethods.Get,
            "Event");
        if (IsReviewable(dto.StatusId))
        {
            yield return new LinkDefinition(
                LinkRelations.WithdrawClaim,
                RouteNames.WithdrawEventOrganizerClaim,
                new { eventId = dto.EventId, claimId = dto.Id },
                HttpMethods.Post,
                "Withdraw organizer claim",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.WithdrawOrganizerClaim, ResourceDescriptors.EventOrganizerClaim, dto);
            yield return new LinkDefinition(
                LinkRelations.ReviewClaim,
                RouteNames.ReviewEventOrganizerClaim,
                new { eventId = dto.EventId, claimId = dto.Id },
                HttpMethods.Post,
                "Review organizer claim",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Events.ReviewOrganizerClaim, ResourceDescriptors.EventOrganizerClaim, dto);
        }
    }

    private static bool IsReviewable(int statusId) => statusId is
        (int)EventOrganizerClaimStatusEnum.Pending or
        (int)EventOrganizerClaimStatusEnum.EvidenceRequired;
}
