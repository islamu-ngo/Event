using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationAnalytics;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class RegistrationAnswerAnalyticsLinkPolicy : ILinkPolicy<RegistrationAnswerAnalyticsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationAnswerAnalyticsDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
                LinkRelations.Self,
                RouteNames.GetRegistrationAnswerAnalytics,
                new { eventId = dto.EventId, formId = dto.FormId, formVersionId = dto.FormVersionId },
                HttpMethods.Get,
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Events.ManageRegistrations,
                ResourceKinds.Event,
                dto.EventId.ToString("D"),
                facts: new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId));
    }
}

/// <summary>Analytics rows carry no affordances of their own; the contract's empty defaults apply.</summary>
public sealed class RegistrationAnswerAnalyticsCollectionLinkPolicy : ICollectionLinkPolicy<RegistrationAnswerAnalyticsDto>;
