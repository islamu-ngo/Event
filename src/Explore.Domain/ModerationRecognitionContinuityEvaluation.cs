using System.Collections.Immutable;

namespace Explore.Domain;

public enum ModerationRecognitionContinuityFailure
{
    None = 0, NativeBoundaryRequired = 1, SubjectUnavailable = 2, SettlementRequired = 3,
    KeyUnavailable = 4, SourceUnavailable = 5, CanonicalUnavailable = 6, StoreUnavailable = 7,
    JurisdictionUnavailable = 8, ActionRecoveryRequired = 9
}

/// <summary>Native entity result. A zero failure is not permission to enroll or enforce.</summary>
public sealed record ModerationRecognitionContinuityEvaluation
{
    public ModerationRecognitionContinuityFailure ActionFailure { get; init; }
    public ModerationRecognitionContinuityFailure AdmissionFailure { get; init; }
    public bool AdmissionExcluded { get; init; }
    public bool ActionRecoveryStaged { get; init; }
    public ImmutableArray<ModerationRecognitionContinuityBinding> Bindings { get; init; } = [];
    public ImmutableArray<ModerationRecognitionCaptureItem> CurrentActions { get; init; } = [];
    public DateTimeOffset? CurrentUntil { get; init; }
    public string? Generation { get; init; }

    public static ModerationRecognitionContinuityEvaluation Closed(ModerationRecognitionContinuityFailure failure) =>
        new() { ActionFailure = failure, AdmissionFailure = failure };
    public override string ToString() => nameof(ModerationRecognitionContinuityEvaluation);
}
