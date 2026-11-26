using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Hateoas;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.API.Hateoas.Policies;

public sealed class LearningGuidancePolicyLinkPolicy : ILinkPolicy<LearningGuidancePolicyDto>
{
    public IEnumerable<LinkDefinition> GetLinks(LearningGuidancePolicyDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Self, RouteNames.GetLearningGuidancePolicy, null, "GET", RequiresAuth: true);
        if (dto.CanEdit)
            yield return new("edit", RouteNames.UpdateLearningGuidancePolicy, null, "PATCH", RequiresAuth: true);
    }
}

public sealed class LearningGuidancePolicyCollectionLinkPolicy : ICollectionLinkPolicy<LearningGuidancePolicyDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(LearningGuidancePolicyDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
