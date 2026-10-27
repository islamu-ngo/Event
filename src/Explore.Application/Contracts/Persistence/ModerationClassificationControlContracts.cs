using Explore.Application.Features.Moderation.Policy;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Selects a non-punitive control from an independently accepted complete material
/// review. These coordinates identify intent; native acquisition supplies authority.
/// </summary>
public sealed record RecordModerationClassificationControlSelection : ModerationQualificationMutation
{
    public required Guid SelectionId { get; init; }
    public required Guid MaterialSourceId { get; init; }
    public required long ExpectedMaterialSourceRevision { get; init; }
    public required long ExpectedMaterialWriterGeneration { get; init; }
    public required Guid MaterialReviewId { get; init; }
    public required long ExpectedMaterialReviewRevision { get; init; }
    public required long ExpectedReviewWriterGeneration { get; init; }
    public required Guid EventGenerationId { get; init; }
    public required long ExpectedEventGenerationRevision { get; init; }
    public required long ExpectedEventWriterGeneration { get; init; }
    public required ModerationPolicyContribution Contribution { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

/// <summary>Requests independent native adoption of one exact classification-control ordinal.</summary>
public sealed record AdoptModerationClassificationEventTarget : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required Guid SelectionId { get; init; }
    public required long ExpectedSelectionRevision { get; init; }
    public required int BindingOrdinal { get; init; }
    public required Guid EventGenerationId { get; init; }
    public required long ExpectedEventGenerationRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

/// <summary>Ends an exact adoption without granting new authority or restoring native material.</summary>
public sealed record RevokeModerationClassificationEventTarget : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required string Grounds { get; init; }
}
