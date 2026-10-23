using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Moderation.Notices;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationNoticeStatusDetailLinkPolicy : ILinkPolicy<ModerationNoticeStatusDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationNoticeStatusDto dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || dto.QualifiedRecipientId == Guid.Empty) yield break;
        // R closes original payload proof, not independently qualified minimal status.
        // No full-text, acknowledgement, complaint or support capability is inferred.
        yield return new(LinkRelations.Self, RouteNames.GetModerationNoticeStatus,
            new { decisionId = dto.DecisionId, effectId = dto.EffectId }, RequiresAuth: true);
    }
}

public sealed class ModerationNoticeStatusCollectionLinkPolicy : ICollectionLinkPolicy<ModerationNoticeStatusDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationNoticeStatusDto dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || dto.QualifiedRecipientId == Guid.Empty) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetModerationNoticeStatus,
            new { decisionId = dto.DecisionId, effectId = dto.EffectId }, RequiresAuth: true);
    }
}
