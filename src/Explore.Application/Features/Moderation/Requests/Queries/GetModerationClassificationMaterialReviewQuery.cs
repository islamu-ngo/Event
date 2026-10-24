using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Responses;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Queries;

public sealed record GetModerationClassificationMaterialReviewQuery(Guid SourceId) : IQuery<ModerationClassificationMaterialReviewReadResult>;

/// <summary>Original unaccepted proposal metadata for the nominated reviewer, not content disclosure or acceptance authority.</summary>
public sealed record ModerationClassificationMaterialReviewView(
    AuthorityScope Target, long ExpectedQualificationRevision, Guid SourceId, long ExpectedSourceRevision,
    long ExpectedSourceWriterGeneration, Guid EventGenerationId, Guid TaxonomyId,
    Guid ExpectedTaxonomySourceId, Guid ExpectedTaxonomyAdoptionId, ModerationClassificationMaterialMandate Mandate,
    ImmutableArray<ModerationClassificationMaterialSelection> Materials,
    ImmutableArray<ModerationClassificationMaterialComponentVersion> Components,
    ImmutableArray<ModerationClassificationMaterialCategoryView> Categories, DateTimeOffset ValidUntil)
{
    [JsonIgnore] public bool NativeReviewerQualified { get; internal init; }
    public override string ToString() => nameof(ModerationClassificationMaterialReviewView);
}

public sealed class ModerationClassificationMaterialReviewReadResult : IAsyncDisposable
{
    public ModerationClassificationMaterialReviewReadResult(ModerationCurrentPolicyReadOutcome outcome) => Outcome = outcome;

    internal ModerationClassificationMaterialReviewReadResult(
        IModerationClassificationMaterialReviewReadRepository repository,
        ModerationClassificationMaterialReviewReadRequest request, CancellationToken originCancellation)
    {
        Outcome = ModerationCurrentPolicyReadOutcome.Allowed;
        Projection = new ModerationClassificationMaterialReviewReadProjection(repository, request, originCancellation);
    }

    public ModerationCurrentPolicyReadOutcome Outcome { get; }
    [JsonIgnore] public ModerationClassificationMaterialReviewReadProjection? Projection { get; }
    public ValueTask DisposeAsync() => Projection?.DisposeAsync() ?? ValueTask.CompletedTask;
    public override string ToString() => nameof(ModerationClassificationMaterialReviewReadResult);

    public static BaseCommandResponse<Guid> Failure(ModerationCurrentPolicyReadOutcome outcome) => outcome switch
    {
        ModerationCurrentPolicyReadOutcome.Invalid => BaseCommandResponse.Validation<Guid>(["Invalid material proposal identity."]),
        ModerationCurrentPolicyReadOutcome.Unauthenticated => BaseCommandResponse.Authentication<Guid>(),
        ModerationCurrentPolicyReadOutcome.Denied => BaseCommandResponse.Authorization<Guid>(),
        _ => BaseCommandResponse.Failure<Guid>("ModerationMaterialReviewUnavailable")
    };
}
