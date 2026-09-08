using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class OptionalQuestionnaireLinkPolicy : ILinkPolicy<OptionalQuestionnaireDto>
{
    public IEnumerable<LinkDefinition> GetLinks(OptionalQuestionnaireDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetOptionalQuestionnaire, new { eventId = dto.EventId });
        yield return new LinkDefinition(LinkRelations.Event, RouteNames.GetEventById, new { id = dto.EventId });
    }
}

public sealed class OptionalQuestionnaireCollectionLinkPolicy(OptionalQuestionnaireLinkPolicy detail)
    : ICollectionLinkPolicy<OptionalQuestionnaireDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(OptionalQuestionnaireDto dto, ClaimsPrincipal? user) =>
        detail.GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
