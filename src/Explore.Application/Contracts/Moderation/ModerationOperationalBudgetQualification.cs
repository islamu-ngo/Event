using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

public sealed record RecordModerationOperationalBudgetSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required ModerationOperationalBudgetTerms Terms { get; init; }
    public required Guid OperatorHumanId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationOperationalBudgetSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationOperationalBudgetAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

public static class ModerationOperationalBudgetQualification
{
    public static bool IsValid(ModerationQualificationMutation request)
    {
        if (request.Target?.Kind != ModerationScopeKind.Tenant
            || request.ExpectedQualificationRevision is < 1 or >= long.MaxValue - 1)
            return false;
        return request switch
        {
            RecordModerationOperationalBudgetSource source => Uuid(source.SourceId)
                && source.OperatorHumanId != Guid.Empty && Finite(source.ValidUntil) && ValidTerms(source.Terms),
            AdoptModerationOperationalBudgetSource source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
            WithdrawModerationOperationalBudgetAdoption source => Uuid(source.AdoptionId)
                && source.ExpectedAdoptionRevision == 1,
            _ => false
        };
    }

    private static bool ValidTerms(ModerationOperationalBudgetTerms? terms)
    {
        if (terms is null) return false;
        try { terms.Validate(); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    private static bool Finite(DateTimeOffset instant) =>
        instant.Offset == TimeSpan.Zero && instant != DateTimeOffset.MinValue && instant != DateTimeOffset.MaxValue;
}
