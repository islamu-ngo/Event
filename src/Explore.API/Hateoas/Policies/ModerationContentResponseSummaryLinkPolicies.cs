using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationContentResponseSummaryDetailLinkPolicy(TimeProvider clock)
    : ILinkPolicy<ModerationContentResponseSummary>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationContentResponseSummary dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || clock.GetUtcNow() >= dto.QualifiedUntil || clock.GetUtcNow() >= dto.RetainUntil) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetModerationContentResponseStatement,
            new { responseId = dto.Id }, RequiresAuth: true);
        yield return new("collection", RouteNames.GetModerationContentResponses,
            new { caseId = dto.CaseId }, RequiresAuth: true);
    }
}

public sealed class ModerationContentResponseSummaryCollectionLinkPolicy(TimeProvider clock)
    : ICollectionLinkPolicy<ModerationContentResponseSummary>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationContentResponseSummary dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || clock.GetUtcNow() >= dto.QualifiedUntil || clock.GetUtcNow() >= dto.RetainUntil) yield break;
        yield return new("statement", RouteNames.GetModerationContentResponseStatement,
            new { responseId = dto.Id }, RequiresAuth: true);
    }
}
