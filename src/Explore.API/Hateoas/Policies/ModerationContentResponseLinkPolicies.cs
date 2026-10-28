using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationContentResponseDetailLinkPolicy(TimeProvider clock)
    : ILinkPolicy<ModerationContentResponseAvailability>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationContentResponseAvailability dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || clock.GetUtcNow() >= dto.QualifiedUntil
            || clock.GetUtcNow() >= dto.RetainUntil) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetModerationContentResponseAvailability,
            new { noticeId = dto.NoticeId }, RequiresAuth: true);
        yield return new("submit-response", RouteNames.SubmitModerationContentResponse,
            new { noticeId = dto.NoticeId }, "POST", RequiresAuth: true);
    }
}

public sealed class ModerationContentResponseCollectionLinkPolicy(TimeProvider clock)
    : ICollectionLinkPolicy<ModerationContentResponseAvailability>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationContentResponseAvailability dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || clock.GetUtcNow() >= dto.QualifiedUntil
            || clock.GetUtcNow() >= dto.RetainUntil) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetModerationContentResponseAvailability,
            new { noticeId = dto.NoticeId }, RequiresAuth: true);
    }
}
