using Explore.Application.Contracts.Notifications;

namespace Explore.Application.Features.Moderation.Notices;

/// <summary>Prepares privately, checks the original lease again, and writes before releasing its fences.</summary>
public sealed class ModerationNoticeStatusProjection : IAsyncDisposable
{
    private IModerationNoticeStatusRepository? _repository;
    private readonly ModerationNoticeStatusRequest _request;
    private readonly CancellationToken _originCancellation;

    internal ModerationNoticeStatusProjection(IModerationNoticeStatusRepository repository,
        ModerationNoticeStatusRequest request, CancellationToken originCancellation) =>
        (_repository, _request, _originCancellation) = (repository, request, originCancellation);

    public async Task<ModerationNoticeReadOutcome> DiscloseAsync<TRepresentation>(
        Func<ModerationNoticeStatusDto, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var repository = Interlocked.Exchange(ref _repository, null);
        if (repository is null) return ModerationNoticeReadOutcome.NotFound;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_originCancellation, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var entered = false;
        return await repository.ExecuteSerializedAsync(async fencedToken =>
        {
            if (entered) return ModerationNoticeReadOutcome.NotFound;
            entered = true;
            var acquisition = await repository.AcquireAsync(_request, fencedToken);
            await using var lease = acquisition.Lease;
            if (acquisition.Outcome != ModerationNoticeReadOutcome.Allowed) return acquisition.Outcome;
            if (lease is null) return ModerationNoticeReadOutcome.Unavailable;
            var entry = lease.Entry;
            var dto = new ModerationNoticeStatusDto(entry.Effect.DecisionId, entry.Effect.Id, entry.Proof.ToString(),
                entry.Notification?.NotifiedAt, entry.Notification?.RemedyUntil,
                entry.OriginalContestationCalendarMonths, entry.OriginalRetainUntil)
            { QualifiedRecipientId = _request.SubjectId };
            var representation = await prepare(dto, fencedToken);
            fencedToken.ThrowIfCancellationRequested();
            var outcome = await lease.RevalidateAsync(fencedToken);
            fencedToken.ThrowIfCancellationRequested();
            if (outcome != ModerationNoticeReadOutcome.Allowed) return outcome;
            await deliver(representation, fencedToken);
            return ModerationNoticeReadOutcome.Allowed;
        }, token);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _repository, null);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => nameof(ModerationNoticeStatusProjection);
}
