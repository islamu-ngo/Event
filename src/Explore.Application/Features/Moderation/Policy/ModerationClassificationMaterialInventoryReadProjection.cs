using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;

namespace Explore.Application.Features.Moderation.Policy;

/// <summary>Serializes privately before rechecking original complete native inventory and delivering under its retained fences.</summary>
public sealed class ModerationClassificationMaterialInventoryReadProjection : IAsyncDisposable
{
    private IModerationClassificationMaterialInventoryReadRepository? repository;
    private readonly ModerationClassificationMaterialInventoryReadRequest request;
    private readonly CancellationToken originCancellation;

    internal ModerationClassificationMaterialInventoryReadProjection(
        IModerationClassificationMaterialInventoryReadRepository repository,
        ModerationClassificationMaterialInventoryReadRequest request, CancellationToken originCancellation) =>
        (this.repository, this.request, this.originCancellation) = (repository, request, originCancellation);

    public async Task<ModerationCurrentPolicyReadOutcome> DiscloseAsync<TRepresentation>(
        Func<ModerationClassificationMaterialInventoryView, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var owner = Interlocked.Exchange(ref repository, null);
        if (owner is null) return ModerationCurrentPolicyReadOutcome.Unavailable;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(originCancellation, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var acquisition = await owner.AcquireInventoryReadAsync(request, token);
        await using var lease = acquisition.Lease;
        if (acquisition.Outcome != ModerationCurrentPolicyReadOutcome.Allowed) return acquisition.Outcome;
        if (lease is null) return ModerationCurrentPolicyReadOutcome.Unavailable;
        var observed = lease.Observation;
        var taxonomy = observed.Taxonomy.Source;
        var view = new ModerationClassificationMaterialInventoryView(
            lease.Jurisdiction, lease.Epoch.Revision, lease.MaterialWriterGeneration,
            observed.Generation.Id, taxonomy.PolicyId, taxonomy.PolicyRevision, taxonomy.PublicationId,
            request.ProfileId, request.Mode, taxonomy.TaxonomyId, taxonomy.Id, observed.Taxonomy.Adoption.Id,
            observed.Materials, observed.Components, observed.Taxonomy.Categories.Select(category =>
                new ModerationClassificationMaterialCategoryView(category.Id, category.CategoryId, category.Revision,
                    category.Code, category.Kind, category.MinimumAge, category.Meaning, category.ReviewRationale)).ToImmutableArray(),
            observed.ValidUntil)
        { NativeOwnerQualified = true };
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

    public override string ToString() => nameof(ModerationClassificationMaterialInventoryReadProjection);
}
