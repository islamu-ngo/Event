using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationChildAgePolicyCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationChildAgePolicyTerms Terms { get; init; }
    public required ModerationChildAgePolicyOwnership Ownership { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RecordModerationChildAgePolicySource
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
        Ownership = Ownership,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdoptModerationChildAgePolicyCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new AdoptModerationChildAgePolicySource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId,
        ExpectedSourceRevision = ExpectedSourceRevision,
        ExpectedSourceWriterGeneration = ExpectedSourceWriterGeneration,
        AdoptionId = AdoptionId,
        ValidUntil = ValidUntil
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawModerationChildAgePolicyCommand : ModerationQualificationCommand
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationChildAgePolicyAdoption
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        AdoptionId = AdoptionId,
        ExpectedAdoptionRevision = ExpectedAdoptionRevision
    };
}
