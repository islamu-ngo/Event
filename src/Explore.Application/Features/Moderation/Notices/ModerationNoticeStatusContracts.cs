using System.Text.Json.Serialization;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Moderation.Notices;

public sealed record GetModerationNoticeStatusQuery(Guid DecisionId, Guid EffectId)
    : IQuery<ModerationNoticeStatusResult>;

/// <summary>
/// A recorded calendar floor is not an eligibility deadline. Null facts are unavailable, not
/// evidence that notification never happened. This resource grants no complaint or review action.
/// </summary>
public sealed record ModerationNoticeStatusDto(
    Guid DecisionId, Guid EffectId, string NotificationProof,
    DateTimeOffset? NotifiedAt, DateTimeOffset? RemedyFloorUntil,
    int? OriginalContestationCalendarMonths, DateTimeOffset? OriginalRetainUntil)
{
    [JsonIgnore] public Guid QualifiedRecipientId { get; init; }
    public override string ToString() => nameof(ModerationNoticeStatusDto);
}

/// <summary>Only the single-use terminal projection owns the qualified representation.</summary>
public sealed class ModerationNoticeStatusResult(
    ModerationNoticeReadOutcome outcome, ModerationNoticeStatusProjection? projection = null)
{
    public ModerationNoticeReadOutcome Outcome { get; } = outcome;
    [JsonIgnore] public ModerationNoticeStatusProjection? Projection { get; } = projection;
    public override string ToString() => nameof(ModerationNoticeStatusResult);
}
