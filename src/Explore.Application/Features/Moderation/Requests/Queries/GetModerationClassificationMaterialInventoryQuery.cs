using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Queries;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GetModerationClassificationMaterialInventoryQuery : IQuery<ModerationClassificationMaterialInventoryReadResult>
{
    public required Guid EventGenerationId { get; init; }
    public required Guid TaxonomyId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required ModerationClassificationMaterialMandate Mandate { get; init; }
}

/// <summary>Current metadata to author exact source intent, never audience eligibility or accepted classification.</summary>
public sealed record ModerationClassificationMaterialInventoryView(
    AuthorityScope Target, long ExpectedQualificationRevision, long ExpectedMaterialGeneration,
    Guid EventGenerationId, Guid PolicyId, long ExpectedPolicyRevision, Guid PublicationId,
    Guid ProfileId, ModerationNativeMode Mode, Guid TaxonomyId,
    Guid ExpectedTaxonomySourceId, Guid ExpectedTaxonomyAdoptionId,
    ImmutableArray<ModerationClassificationMaterial> Materials,
    ImmutableArray<ModerationClassificationMaterialComponentVersion> Components,
    ImmutableArray<ModerationClassificationMaterialCategoryView> Categories, DateTimeOffset ValidUntil)
{
    [JsonIgnore] public bool NativeOwnerQualified { get; internal init; }
    public override string ToString() => nameof(ModerationClassificationMaterialInventoryView);
}

public sealed record ModerationClassificationMaterialCategoryView(
    Guid VersionId, int CategoryId, long Revision, string Code,
    EventContentClassificationVersion.CategoryKind Kind, int MinimumAge, string Meaning, string ReviewRationale);

/// <summary>Acquisition is deferred to one terminal consumer that keeps native fences through actual delivery.</summary>
public sealed class ModerationClassificationMaterialInventoryReadResult : IAsyncDisposable
{
    public ModerationClassificationMaterialInventoryReadResult(ModerationCurrentPolicyReadOutcome outcome) => Outcome = outcome;

    internal ModerationClassificationMaterialInventoryReadResult(
        IModerationClassificationMaterialInventoryReadRepository repository,
        ModerationClassificationMaterialInventoryReadRequest request, CancellationToken originCancellation)
    {
        Outcome = ModerationCurrentPolicyReadOutcome.Allowed;
        Projection = new ModerationClassificationMaterialInventoryReadProjection(repository, request, originCancellation);
    }

    public ModerationCurrentPolicyReadOutcome Outcome { get; }
    [JsonIgnore] public ModerationClassificationMaterialInventoryReadProjection? Projection { get; }
    public ValueTask DisposeAsync() => Projection?.DisposeAsync() ?? ValueTask.CompletedTask;
    public override string ToString() => nameof(ModerationClassificationMaterialInventoryReadResult);

    public static BaseCommandResponse<Guid> Failure(ModerationCurrentPolicyReadOutcome outcome) => outcome switch
    {
        ModerationCurrentPolicyReadOutcome.Invalid => BaseCommandResponse.Validation<Guid>(["Invalid material inventory intent."]),
        ModerationCurrentPolicyReadOutcome.Unauthenticated => BaseCommandResponse.Authentication<Guid>(),
        ModerationCurrentPolicyReadOutcome.Denied => BaseCommandResponse.Authorization<Guid>(),
        _ => BaseCommandResponse.Failure<Guid>("ModerationMaterialInventoryUnavailable")
    };
}
