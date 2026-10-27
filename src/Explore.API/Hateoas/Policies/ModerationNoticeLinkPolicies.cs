using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Moderation.Notices;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationNoticeDetailLinkPolicy(TimeProvider clock) : ILinkPolicy<ModerationNoticeDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationNoticeDto dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || dto.QualifiedRecipientId == Guid.Empty
            || clock.GetUtcNow() >= dto.RetainUntil) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetModerationNotice, new { id = dto.Id }, RequiresAuth: true);
        yield return new(LinkRelations.Collection, RouteNames.GetModerationNotices, RequiresAuth: true);
        if (dto.Audience == nameof(Explore.Domain.ModerationNoticeAudience.ContentRepresentative))
            yield return new("response-availability", RouteNames.GetModerationContentResponseAvailability,
                new { noticeId = dto.Id }, RequiresAuth: true);
        if (dto.QualifiedDecisionId != Guid.Empty && dto.QualifiedEffectId != Guid.Empty)
            yield return new("notification-status", RouteNames.GetModerationNoticeStatus,
                new { decisionId = dto.QualifiedDecisionId, effectId = dto.QualifiedEffectId }, RequiresAuth: true);
        if (dto.CanAcknowledge && Explore.Domain.ModerationNoticeRetention.CanNotifyAt(
            clock.GetUtcNow(), dto.ContestationCalendarMonths, dto.RetainUntil))
            yield return new("acknowledge", RouteNames.AcknowledgeModerationNotice,
                new { id = dto.Id }, "POST", RequiresAuth: true);
    }
}

public sealed class ModerationNoticeCollectionLinkPolicy(TimeProvider clock) : ICollectionLinkPolicy<ModerationNoticeDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationNoticeDto dto, ClaimsPrincipal? user)
    {
        if (user?.GetPlatformUserId() is null && user?.GetProviderIdentity() is null
            || dto.QualifiedRecipientId == Guid.Empty
            || clock.GetUtcNow() >= dto.RetainUntil) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetModerationNotice, new { id = dto.Id }, RequiresAuth: true);
        if (dto.QualifiedDecisionId != Guid.Empty && dto.QualifiedEffectId != Guid.Empty)
            yield return new("notification-status", RouteNames.GetModerationNoticeStatus,
                new { decisionId = dto.QualifiedDecisionId, effectId = dto.QualifiedEffectId }, RequiresAuth: true);
    }
}
