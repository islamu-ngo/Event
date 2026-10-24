using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;

namespace Explore.Application.Features.Moderation.Notices;

/// <summary>
/// No public DTO survives the handler. The one terminal consumer acquires current native
/// authority, prepares bytes privately, rechecks authority and delivers under the same fences.
/// </summary>
public sealed class ModerationNoticeReadProjection : IAsyncDisposable
{
    private Func<CancellationToken, Task<ModerationNoticeReadAcquisition>>? _acquire;
    private readonly CancellationToken _originCancellation;
    private readonly Guid _recipientId;
    private readonly IModerationNoticeReadRepository _repository;

    private ModerationNoticeReadProjection(
        Func<CancellationToken, Task<ModerationNoticeReadAcquisition>> acquire,
        IModerationNoticeReadRepository repository, Guid recipientId, CancellationToken originCancellation) =>
        (_acquire, _repository, _recipientId, _originCancellation) = (acquire, repository, recipientId, originCancellation);

    internal static async Task<ModerationNoticeReadResult> PrepareAsync(
        IModerationNoticeReadRepository repository, IAdminContext identity,
        IMachinePrincipalAccessor machine, ITenantContext tenant,
        Guid? id, Guid? afterId, int limit, CancellationToken token)
    {
        if (id == Guid.Empty || afterId == Guid.Empty || limit is < 1 or > 50)
            return new(ModerationNoticeReadOutcome.Invalid);
        if (machine.IsMachineCaller || await identity.ResolveUserIdAsync(token) is not { } recipientId
            || recipientId == Guid.Empty)
            return new(ModerationNoticeReadOutcome.Unauthenticated);
        if (tenant.TenantId == Guid.Empty)
            return new(ModerationNoticeReadOutcome.NotFound);
        var request = new ModerationNoticeReadRequest(tenant.TenantId, recipientId, id, afterId, limit);
        return new(ModerationNoticeReadOutcome.Allowed,
            new(ct => repository.AcquireAsync(request, ct), repository, recipientId, token));
    }

    public async Task<ModerationNoticeReadOutcome> DiscloseAsync<TRepresentation>(
        Func<ModerationNoticePage, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var acquire = Interlocked.Exchange(ref _acquire, null);
        if (acquire is null) return ModerationNoticeReadOutcome.NotFound;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _originCancellation, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var entered = false;
        return await _repository.ExecuteSerializedAsync(async fencedToken =>
        {
            if (entered) return ModerationNoticeReadOutcome.NotFound;
            entered = true;
            var acquisition = await acquire(fencedToken);
            await using var lease = acquisition.Lease;
            if (acquisition.Outcome != ModerationNoticeReadOutcome.Allowed)
                return acquisition.Outcome;
            if (lease is null) return ModerationNoticeReadOutcome.Unavailable;
            var items = lease.Entries.Select(entry =>
            {
                var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(entry.Source.SnapshotJson)!;
                var snapshot = capture.Notice!;
                return new ModerationNoticeDto(entry.Notice.Id, entry.Notice.Audience.ToString(), snapshot.CapturedAt,
                    snapshot.DecisionState.ToString(), snapshot.SanctionState?.ToString(),
                    snapshot.Scope, capture.Decision.StartsAt, capture.Decision.EndsAt, capture.Decision.ReviewAt,
                    snapshot.Rule, snapshot.Reason, snapshot.RuleMeaning.Value, snapshot.ReasonMeaning.Value,
                    snapshot.Facts.Select(fact => fact.Value).ToImmutableArray(),
                    snapshot.RemainingActions.Value, snapshot.Recourse.Value,
                    snapshot.Automation.Value, snapshot.Retention.Value,
                    entry.Source.SnapshotHash, snapshot.ContestationCalendarMonths,
                    entry.Notification?.NotifiedAt, entry.Notification?.RemedyUntil,
                    snapshot.RetainUntil, snapshot.Effects, snapshot.Consequences)
                {
                    QualifiedRecipientId = _recipientId,
                    QualifiedDecisionId = entry.Notice.Audience is Domain.ModerationNoticeAudience.Subject
                        or Domain.ModerationNoticeAudience.ContentRepresentative
                        ? entry.Source.DecisionId : Guid.Empty,
                    QualifiedEffectId = entry.Notice.Audience is Domain.ModerationNoticeAudience.Subject
                        or Domain.ModerationNoticeAudience.ContentRepresentative
                        ? entry.Source.EffectId : Guid.Empty,
                    CanAcknowledge = entry.Notice.Audience is (Domain.ModerationNoticeAudience.Subject
                        or Domain.ModerationNoticeAudience.ContentRepresentative) && entry.Notification is null
                };
            }).ToImmutableArray();
            var representation = await prepare(new(items), fencedToken);
            fencedToken.ThrowIfCancellationRequested();
            var current = await lease.RevalidateAsync(fencedToken);
            fencedToken.ThrowIfCancellationRequested();
            if (current != ModerationNoticeReadOutcome.Allowed) return current;
            await deliver(representation, fencedToken);
            return ModerationNoticeReadOutcome.Allowed;
        }, token);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _acquire, null);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => nameof(ModerationNoticeReadProjection);
}
