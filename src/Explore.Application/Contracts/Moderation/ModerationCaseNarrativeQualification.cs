using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

public sealed record RecordModerationCaseNarrativePolicySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required ModerationCaseTargetKind TargetKind { get; init; }
    public required ModerationCaseNarrativeTerms Terms { get; init; }
    public required ModerationCaseNarrativeOwnership Ownership { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationCaseNarrativePolicySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationCaseNarrativeAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

public static class ModerationCaseNarrativeQualification
{
    public static bool IsValid(ModerationQualificationMutation request)
    {
        if (request.Target?.Kind != ModerationScopeKind.Tenant
            || request.ExpectedQualificationRevision is < 1 or >= long.MaxValue - 1)
            return false;
        return request switch
        {
            RecordModerationCaseNarrativePolicySource source => Uuid(source.SourceId) && Enum.IsDefined(source.TargetKind)
                && Finite(source.ValidUntil) && Substantive(source),
            AdoptModerationCaseNarrativePolicySource source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
            WithdrawModerationCaseNarrativeAdoption source => Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
            _ => false
        };
    }

    private static bool Substantive(RecordModerationCaseNarrativePolicySource source)
    {
        if (source.Terms is null || source.Ownership is null) return false;
        try
        {
            source.Terms.Validate();
            source.Ownership.Validate(source.Ownership.GovernanceControllerHumanId);
            return source.Terms.Purpose != ModerationCaseNarrativePurpose.ParticipantInvestigation
                || source.TargetKind == ModerationCaseTargetKind.ParticipantIncident;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    private static bool Finite(DateTimeOffset instant) =>
        instant.Offset == TimeSpan.Zero && instant != DateTimeOffset.MinValue && instant != DateTimeOffset.MaxValue;
}
