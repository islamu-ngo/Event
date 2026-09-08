using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ParticipantReadinessLinkPolicy :
    ILinkPolicy<ParticipantReadinessDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        ParticipantReadinessDto dto,
        ClaimsPrincipal? user)
    {
        var routeValues = new
        {
            eventId = dto.EventId,
            orderId = dto.RegistrationOrderId,
            participantId = dto.ParticipantId,
            assignmentId =
                dto.RegistrationTicketAssignmentId,
        };
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetParticipantReadiness,
            routeValues,
            HttpMethods.Get);

        if (dto.CanComplete)
        {
            yield return new LinkDefinition(
                LinkRelations.CompleteParticipantReadiness,
                RouteNames.CompleteParticipantReadiness,
                routeValues,
                HttpMethods.Post,
                "Complete participant readiness",
                RequiresAuth: true);
        }
        if (dto.CanApprove)
        {
            yield return new LinkDefinition(
                LinkRelations.ApproveParticipantReadiness,
                RouteNames.ApproveParticipantReadiness,
                routeValues,
                HttpMethods.Post,
                "Approve participant readiness",
                RequiresAuth: true);
        }
        if (dto.CanRevoke)
        {
            yield return new LinkDefinition(
                LinkRelations.RevokeParticipantReadiness,
                RouteNames.RevokeParticipantReadiness,
                routeValues,
                HttpMethods.Post,
                "Revoke participant readiness",
                RequiresAuth: true);
        }
    }
}

public sealed class ParticipantReadinessCollectionLinkPolicy :
    ICollectionLinkPolicy<ParticipantReadinessDto>;
