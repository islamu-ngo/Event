using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.Notices;

public sealed record GetModerationNoticesQuery(Guid? AfterId = null, int Limit = 25)
    : IQuery<ModerationNoticeReadResult>;

public sealed record GetModerationNoticeQuery(Guid Id) : IQuery<ModerationNoticeReadResult>;

/// <summary>
/// Original approved explanation, not a current effective-rights decision. In particular,
/// original remaining-action and recourse text never grants an executable capability.
/// </summary>
public sealed record ModerationNoticeDto(
    Guid Id, string Audience, DateTimeOffset CapturedAt,
    string OriginalDecisionState, string? OriginalSanctionState,
    ModerationNoticeScope Scope, DateTimeOffset StartsAt, DateTimeOffset? EndsAt,
    DateTimeOffset? ReviewAt, ModerationNoticeRule Rule, ModerationNoticeRule Reason,
    string RuleMeaning, string ReasonMeaning, ImmutableArray<string> Facts,
    string OriginalRemainingActions, string OriginalRecourse, string Automation, string Retention,
    string SnapshotHash, int ContestationCalendarMonths,
    DateTimeOffset? NotifiedAt, DateTimeOffset? RemedyUntil, DateTimeOffset RetainUntil,
    ImmutableArray<ModerationNoticeEffect> OriginalEffects,
    ImmutableArray<ModerationNoticeConsequence> OriginalConsequences)
{
    [JsonIgnore] public Guid QualifiedRecipientId { get; init; }
    [JsonIgnore] public Guid QualifiedDecisionId { get; init; }
    [JsonIgnore] public Guid QualifiedEffectId { get; init; }
    [JsonIgnore] public bool CanAcknowledge { get; init; }
    public override string ToString() => nameof(ModerationNoticeDto);
}

public sealed record ModerationNoticePage(ImmutableArray<ModerationNoticeDto> Items)
{
    public override string ToString() => nameof(ModerationNoticePage);
}

/// <summary>Contains no notice data. Only the private single-use projection can disclose it.</summary>
public sealed class ModerationNoticeReadResult(
    ModerationNoticeReadOutcome outcome, ModerationNoticeReadProjection? projection = null)
{
    public ModerationNoticeReadOutcome Outcome { get; } = outcome;
    [JsonIgnore] public ModerationNoticeReadProjection? Projection { get; } = projection;
    public override string ToString() => nameof(ModerationNoticeReadResult);

    public static BaseCommandResponse<Guid> Failure(ModerationNoticeReadOutcome outcome) => outcome switch
    {
        ModerationNoticeReadOutcome.Unauthenticated => BaseCommandResponse.Authentication<Guid>(),
        ModerationNoticeReadOutcome.Invalid => BaseCommandResponse.Validation<Guid>(["Invalid notice read coordinates."]),
        ModerationNoticeReadOutcome.NotFound => BaseCommandResponse.NotFound<Guid>(),
        _ => BaseCommandResponse.Failure<Guid>("ModerationNoticeUnavailable")
    };
}
