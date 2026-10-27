using System.Collections.Immutable;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>Only surviving action coordinates; no old subject, identifier, role or private history.</summary>
public sealed record ModerationRecognizedAction(
    Guid BindingId, Guid SanctionId, Guid DecisionId, Guid FreshSubjectId, AuthorityScope Scope,
    ImmutableArray<ModerationCapability> Capabilities, DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt, DateTimeOffset? ReviewAt, DateTimeOffset RetainUntil)
{
    public override string ToString() => nameof(ModerationRecognizedAction);
}

public sealed record ModerationRecognitionContinuityResult
{
    public required ModerationRecognitionContinuityFailure ActionFailure { get; init; }
    public required ModerationRecognitionContinuityFailure AdmissionFailure { get; init; }
    public required bool AdmissionExcluded { get; init; }
    public required bool ActionRecoveryStaged { get; init; }
    public required ImmutableArray<ModerationRecognizedAction> Actions { get; init; }
    public DateTimeOffset? CurrentUntil { get; init; }
    public string? Generation { get; init; }
    public override string ToString() => nameof(ModerationRecognitionContinuityResult);
}
