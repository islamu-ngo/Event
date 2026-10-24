using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Policy;

/// <summary>
/// One terminal consumer owns native acquisition, private representation and actual delivery.
/// No execution strategy can replay its output callback. The original fences survive delivery.
/// </summary>
public sealed class ModerationCurrentPolicyReadProjection : IAsyncDisposable
{
    private IModerationPolicyPublicationRepository? repository;
    private readonly ModerationCurrentPolicyReadRequest request;
    private readonly CancellationToken originCancellation;
    private readonly Guid? prospectivePolicyId;

    internal ModerationCurrentPolicyReadProjection(
        IModerationPolicyPublicationRepository repository, ModerationCurrentPolicyReadRequest request,
        CancellationToken originCancellation, Guid? prospectivePolicyId) =>
        (this.repository, this.request, this.originCancellation, this.prospectivePolicyId) =
            (repository, request, originCancellation, prospectivePolicyId);

    public async Task<ModerationCurrentPolicyReadOutcome> DiscloseAsync<TRepresentation>(
        Func<ModerationCurrentPolicyView, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var owner = Interlocked.Exchange(ref repository, null);
        if (owner is null) return ModerationCurrentPolicyReadOutcome.Unavailable;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(originCancellation, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var acquisition = await owner.AcquireCurrentAuthoringAsync(request, token);
        await using var lease = acquisition.Lease;
        if (acquisition.Outcome != ModerationCurrentPolicyReadOutcome.Allowed) return acquisition.Outcome;
        if (lease is null) return ModerationCurrentPolicyReadOutcome.Unavailable;
        var history = lease.Publication is { } publication
            ? ModerationPolicyPublicationProjection.RestoreHistory(publication) : null;
        if (lease.Publication is not null && history is null)
            return ModerationCurrentPolicyReadOutcome.Unavailable;
        var view = new ModerationCurrentPolicyView(
            lease.Selection?.Id ?? prospectivePolicyId ?? Guid.CreateVersion7(),
            request.Scope == ModerationScopeKind.Tenant ? "tenant" : "instance",
            lease.Selection is null ? "uninitialized" : "adopted",
            lease.Jurisdiction, lease.Host.Id, lease.Epoch.Id, lease.Epoch.Revision,
            ModerationPolicyPublicationProjection.WriterGeneration,
            lease.Selection?.Revision ?? 0, lease.Selection?.LatestPublicationId,
            ModerationReferenceCatalogue.Revision, lease.Inventory.Revision, lease.Inventory.Fingerprint,
            ModerationPolicyPublicationProjection.AuthoringBounds(lease.Jurisdiction),
            history is null ? null : ModerationCurrentPolicyAuthoringState.FromHistory(history))
        {
            NativeAuthoringQualified = true
        };
        var representation = await prepare(view, token);
        token.ThrowIfCancellationRequested();
        var current = await lease.RevalidateAsync(token);
        token.ThrowIfCancellationRequested();
        if (current != ModerationCurrentPolicyReadOutcome.Allowed) return current;
        await deliver(representation, token);
        token.ThrowIfCancellationRequested();
        return ModerationCurrentPolicyReadOutcome.Allowed;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref repository, null);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => nameof(ModerationCurrentPolicyReadProjection);
}
