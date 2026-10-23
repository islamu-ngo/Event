using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record RecordModerationConstitutionCommand : ModerationQualificationCommand
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationConstitutionTerms Terms { get; init; }
    public required ImmutableArray<ModerationConstitutionReviewerTerms> Reviewers { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RecordModerationConstitutionSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        PolicyId = PolicyId,
        ExpectedPolicyRevision = ExpectedPolicyRevision,
        PublicationId = PublicationId,
        ProfileId = ProfileId,
        Mode = Mode,
        Terms = Terms,
        Reviewers = Reviewers,
        ValidUntil = ValidUntil,
        OriginalEffect = OriginalEffect
    };
}

public sealed record AdoptModerationConstitutionCommand : ModerationQualificationCommand
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationConstitutionSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceWriterGeneration = ExpectedSourceWriterGeneration,
        AdoptionId = AdoptionId,
        ValidUntil = ValidUntil,
        OriginalEffect = OriginalEffect
    };
}

public sealed record WithdrawModerationConstitutionCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationConstitutionAdoption
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        ExpectedAdoptionRevision = ExpectedAdoptionRevision
    };
}

public sealed record RecordModerationConstitutionAvailabilityCommand : ModerationQualificationCommand
{
    public required Guid AvailabilityId { get; init; }
    public required int MaximumCaseload { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RecordModerationConstitutionAvailability
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AvailabilityId = AvailabilityId,
        MaximumCaseload = MaximumCaseload,
        ValidUntil = ValidUntil
    };
}

public sealed record WithdrawModerationConstitutionAvailabilityCommand : ModerationQualificationCommand
{
    public required Guid AvailabilityId { get; init; }
    public required long ExpectedAvailabilityRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationConstitutionAvailability
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AvailabilityId = AvailabilityId,
        ExpectedAvailabilityRevision = ExpectedAvailabilityRevision
    };
}
