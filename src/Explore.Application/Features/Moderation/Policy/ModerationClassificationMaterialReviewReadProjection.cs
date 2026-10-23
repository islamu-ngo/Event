using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;

namespace Explore.Application.Features.Moderation.Policy;

/// <summary>Prepares exact proposal metadata privately, then rechecks its original nominated-reviewer lease through actual output.</summary>
public sealed class ModerationClassificationMaterialReviewReadProjection : IAsyncDisposable
{
    private IModerationClassificationMaterialReviewReadRepository? repository;
    private readonly ModerationClassificationMaterialReviewReadRequest request;
    private readonly CancellationToken originCancellation;

    internal ModerationClassificationMaterialReviewReadProjection(
        IModerationClassificationMaterialReviewReadRepository repository,
        ModerationClassificationMaterialReviewReadRequest request, CancellationToken originCancellation) =>
        (this.repository, this.request, this.originCancellation) = (repository, request, originCancellation);

    public async Task<ModerationCurrentPolicyReadOutcome> DiscloseAsync<TRepresentation>(
        Func<ModerationClassificationMaterialReviewView, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var owner = Interlocked.Exchange(ref repository, null);
        if (owner is null) return ModerationCurrentPolicyReadOutcome.Unavailable;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(originCancellation, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var acquired = await owner.AcquireReviewReadAsync(request, token);
        await using var lease = acquired.Lease;
        if (acquired.Outcome != ModerationCurrentPolicyReadOutcome.Allowed) return acquired.Outcome;
        if (lease is null) return ModerationCurrentPolicyReadOutcome.Unavailable;
        var source = lease.Source;
        var view = new ModerationClassificationMaterialReviewView(
            lease.Jurisdiction, lease.Epoch.Revision, source.Id, source.Revision, source.WriterGeneration,
            source.EventGenerationId, source.TaxonomyId, source.TaxonomySourceId, source.TaxonomyAdoptionId,
            source.Mandate, source.Materials.Select(row => row.Selection).ToImmutableArray(),
            source.Components.Select(row => row.Version).ToImmutableArray(),
            lease.Observation.Taxonomy.Categories.Select(category => new ModerationClassificationMaterialCategoryView(
                category.Id, category.CategoryId, category.Revision, category.Code, category.Kind,
                category.MinimumAge, category.Meaning, category.ReviewRationale)).ToImmutableArray(),
            source.ValidUntil)
        { NativeReviewerQualified = true };
        var representation = await prepare(view, token);
        token.ThrowIfCancellationRequested();
        var current = await lease.RevalidateAsync(token);
        token.ThrowIfCancellationRequested();
        if (current != ModerationCurrentPolicyReadOutcome.Allowed) return current;
        await deliver(representation, token);
        return ModerationCurrentPolicyReadOutcome.Allowed;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref repository, null);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => nameof(ModerationClassificationMaterialReviewReadProjection);
}
