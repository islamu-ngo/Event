using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Services;

namespace Explore.Application.Services;

public sealed partial class EventResourceAuthorityOrchestrator(
    IUnitOfWork unitOfWork,
    IEventResourceAuthoritySnapshotReader reader,
    IEventResourceProviderSnapshotReader routes,
    IEventResourceAuthorizationProvider provider,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Owns A and a new B transaction; callers must not supply an ambient transaction.
    /// Preparation remains private until CompleteHeadersAsync succeeds. A failing preparation factory
    /// owns cleanup of anything it has not returned. Content requires an explicit deadline; native
    /// metadata and mutation operations retain caller cancellation without inventing a delivery TTL.
    /// </summary>
    public async Task<EventResourceAuthorityResult> AuthorizeAsync(EventResourceAuthorityRequest request,
        Func<EventResourceAuthorizationFacts, CancellationToken, Task<IEventResourcePrivatePreparation>> prepare,
        CancellationToken cancellationToken = default)
    {
        var initialTime = timeProvider.GetUtcNow();
        if (request.DeadlineUtc is { } expiredAt && expiredAt <= initialTime
            || request.DeadlineUtc is null && request.Action is "access" or "download")
            return new(EventResourceAuthorityOutcome.Expired);
        if (request.TenantId == Guid.Empty || request.ResourceId == Guid.Empty || request.SubjectUserId == Guid.Empty)
            return new(EventResourceAuthorityOutcome.NotFound);
        using var deadline = request.DeadlineUtc is { } deadlineUtc
            ? new CancellationTokenSource(deadlineUtc - initialTime, timeProvider) : null;
        using var linked = deadline is null
            ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var ct = linked?.Token ?? cancellationToken;
        bool concealFailure = false;
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var aTime = attempt == 0 ? initialTime : timeProvider.GetUtcNow();
                if (aTime >= request.DeadlineUtc) return new(EventResourceAuthorityOutcome.Expired);
                var a = await unitOfWork.ExecuteSerializableAsync(token => ReadAsync(request, aTime, token), ct);
                concealFailure = ConcealsFailure(request, a);
                var denial = Denial(request, a);
                if (denial.HasValue) return new(denial.Value == EventResourceAuthorityOutcome.Unavailable && concealFailure
                    ? EventResourceAuthorityOutcome.NotFound : denial.Value);
                if (a!.Evaluation.ProviderInput is { } input)
                {
                    var decision = await provider.CheckAsync(input, ct);
                    if (decision != EventResourceProviderDecision.Allow)
                        return new(concealFailure ? EventResourceAuthorityOutcome.NotFound
                            : decision == EventResourceProviderDecision.Deny
                            ? EventResourceAuthorityOutcome.Forbidden : EventResourceAuthorityOutcome.Unavailable);
                }
                ct.ThrowIfCancellationRequested();
                IEventResourcePrivatePreparation? preparation = null;
                try
                {
                    preparation = await prepare(a.Facts, ct);
                    ct.ThrowIfCancellationRequested();
                    if (preparation is null || preparation.AttachmentGeneration != a.Facts.AttachmentGeneration)
                        return new(EventResourceAuthorityOutcome.Unavailable);
                    var bTime = timeProvider.GetUtcNow();
                    if (bTime >= request.DeadlineUtc) return new(EventResourceAuthorityOutcome.Expired);
                    var b = await unitOfWork.ExecuteSerializableAsync(token => ReadAsync(request, bTime, token), ct);
                    ct.ThrowIfCancellationRequested();
                    denial = Denial(request, b);
                    if (denial.HasValue) return new(denial.Value == EventResourceAuthorityOutcome.Unavailable && concealFailure
                        ? EventResourceAuthorityOutcome.NotFound : denial.Value);
                    if (!SameAuthority(a, b!)) continue;
                    var acceptedPreparation = preparation;
                    preparation = null;
                    return new(request, b!, acceptedPreparation, cancellationToken);
                }
                finally
                {
                    if (preparation is not null) await preparation.DisposeAsync();
                }
            }
            return new(EventResourceAuthorityOutcome.Unavailable);
        }
        catch (OperationCanceledException)
        {
            return new(cancellationToken.IsCancellationRequested
                ? EventResourceAuthorityOutcome.Cancelled
                : request.DeadlineUtc.HasValue ? EventResourceAuthorityOutcome.Expired : EventResourceAuthorityOutcome.Unavailable);
        }
        catch (Exception)
        {
            // External persistence/provider/preparation failures are a bounded outcome, never a response body.
            return new(concealFailure ? EventResourceAuthorityOutcome.NotFound : EventResourceAuthorityOutcome.Unavailable);
        }
    }

    /// <summary>Single-use ownership transfer immediately before headers; emits neither bytes nor Location.</summary>
    public async Task<EventResourceHeaderResult> CompleteHeadersAsync(EventResourceAuthorityLease lease,
        CancellationToken cancellationToken = default)
    {
        var preparation = lease.TakePreparation();
        if (preparation is null) return new(EventResourceAuthorityOutcome.Forbidden);
        try
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                lease.CallerCancellation.ThrowIfCancellationRequested();
                var now = timeProvider.GetUtcNow();
                if (now >= lease.Request.DeadlineUtc) return new(EventResourceAuthorityOutcome.Expired);
                var current = lease.Snapshot.Facts.Evaluate(lease.Request, lease.Snapshot.Route, now);
                if (!current.Allowed || current.ProviderInput != lease.Snapshot.Evaluation.ProviderInput
                    || current.Disclosure != lease.Snapshot.Evaluation.Disclosure)
                    return new(EventResourceAuthorityOutcome.Forbidden);
                var released = preparation;
                preparation = null;
                return new(EventResourceAuthorityOutcome.Allowed, current.Disclosure, released);
            }
            finally
            {
                if (preparation is not null) await preparation.DisposeAsync();
            }
        }
        catch (OperationCanceledException) { return new(EventResourceAuthorityOutcome.Cancelled); }
        catch (Exception) { return new(EventResourceAuthorityOutcome.Unavailable); }
    }

    /// <summary>
    /// Called inside the handler-owned short mutation transaction, including every execution-strategy replay.
    /// Each invocation captures fresh authority time, including execution-strategy replays. Mutation
    /// values may remain stable across retries, but an expired grant must never reuse their timestamp.
    /// The handler disposes the lease outside the transaction. This performs no PDP or preparation I/O.
    /// </summary>
    public async Task<EventResourceAuthorityOutcome> RecheckMutationAsync(EventResourceAuthorityLease lease,
        Guid expectedVersion, CancellationToken cancellationToken = default)
    {
        if (lease.CallerCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            return EventResourceAuthorityOutcome.Cancelled;
        if (!lease.IsPending || lease.Request.Action is not ("create" or "update" or "publish" or "unpublish"
            or "archive" or "delete" or "moderate")) return EventResourceAuthorityOutcome.Forbidden;
        var evaluationUtc = timeProvider.GetUtcNow();
        if (evaluationUtc >= lease.Request.DeadlineUtc) return EventResourceAuthorityOutcome.Expired;
        var current = await ReadAsync(lease.Request, evaluationUtc, cancellationToken);
        var denial = Denial(lease.Request, current);
        if (denial.HasValue) return denial.Value;
        if (current!.Facts.ResourceVersion != expectedVersion) return EventResourceAuthorityOutcome.VersionConflict;
        if (!SameAuthority(lease.Snapshot, current)) return EventResourceAuthorityOutcome.Forbidden;
        if (lease.CallerCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            return EventResourceAuthorityOutcome.Cancelled;
        var finalTime = timeProvider.GetUtcNow();
        if (finalTime >= lease.Request.DeadlineUtc) return EventResourceAuthorityOutcome.Expired;
        var final = current.Facts.Evaluate(lease.Request, current.Route, finalTime);
        return final.Allowed && final.ProviderInput == current.Evaluation.ProviderInput
            && final.Disclosure == current.Evaluation.Disclosure
                ? EventResourceAuthorityOutcome.Allowed : EventResourceAuthorityOutcome.Forbidden;
    }

    private async Task<EventResourceAuthoritySnapshot?> ReadAsync(EventResourceAuthorityRequest request,
        DateTimeOffset evaluationUtc, CancellationToken ct)
    {
        var facts = await reader.ReadAsync(request, evaluationUtc, ct);
        if (facts is null) return null;
        var route = request.SubjectUserId.HasValue && !request.IsMachineCaller
            ? await routes.ReadAsync(request.TenantId, ct) : null;
        return new(facts, route, facts.Evaluate(request, route, evaluationUtc));
    }

    private static EventResourceAuthorityOutcome? Denial(EventResourceAuthorityRequest request,
        EventResourceAuthoritySnapshot? snapshot)
    {
        if (snapshot is null) return EventResourceAuthorityOutcome.NotFound;
        if (!snapshot.Evaluation.Allowed)
            return !snapshot.Evaluation.Disclosure.DiscloseMetadata ? EventResourceAuthorityOutcome.NotFound
                : request.SubjectUserId is null ? EventResourceAuthorityOutcome.AuthenticationRequired
                : EventResourceAuthorityOutcome.Forbidden;
        if (request.SubjectUserId.HasValue && !request.IsMachineCaller
            && (snapshot.Route?.IsUsable != true || snapshot.Evaluation.ProviderInput is null))
            return EventResourceAuthorityOutcome.Unavailable;
        return null;
    }

    private static bool SameAuthority(EventResourceAuthoritySnapshot a, EventResourceAuthoritySnapshot b) =>
        a.Evaluation.ProviderInput == b.Evaluation.ProviderInput && a.Route == b.Route
        && a.Evaluation.Disclosure == b.Evaluation.Disclosure
        && Equals(a.Facts.Access.GovernancePolicy, b.Facts.Access.GovernancePolicy)
        && a.Facts.ResourceVersion == b.Facts.ResourceVersion
        && a.Facts.StorageObjectId == b.Facts.StorageObjectId
        && a.Facts.AttachmentGeneration == b.Facts.AttachmentGeneration;
}

public interface IEventResourcePrivatePreparation : IAsyncDisposable
{
    string AttachmentGeneration { get; }
}

public enum EventResourceAuthorityOutcome
{
    Allowed, NotFound, AuthenticationRequired, Forbidden, Unavailable, VersionConflict, Expired, Cancelled
}

public sealed class EventResourceAuthorityResult : IAsyncDisposable
{
    public EventResourceAuthorityOutcome Outcome { get; }
    public EventResourceAuthorityLease? Lease { get; }

    internal EventResourceAuthorityResult(EventResourceAuthorityOutcome outcome) => Outcome = outcome;

    internal EventResourceAuthorityResult(EventResourceAuthorityRequest request,
        EventResourceAuthoritySnapshot snapshot, IEventResourcePrivatePreparation preparation, CancellationToken callerCancellation)
    {
        Outcome = EventResourceAuthorityOutcome.Allowed;
        Lease = new(request, snapshot, preparation, callerCancellation);
    }

    public ValueTask DisposeAsync() => Lease?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public sealed record EventResourceHeaderResult(EventResourceAuthorityOutcome Outcome,
    EventResourceAccessDecision? Disclosure = null, IEventResourcePrivatePreparation? Preparation = null);

internal sealed record EventResourceAuthoritySnapshot(EventResourceAuthorizationFacts Facts,
    EventResourceProviderSnapshot? Route, EventResourceEvaluation Evaluation);

/// <summary>Owns one private preparation, not a reusable grant. Dispose unless ownership was transferred at the header gate.</summary>
public sealed class EventResourceAuthorityLease : IAsyncDisposable
{
    private IEventResourcePrivatePreparation? _preparation;
    internal EventResourceAuthorityRequest Request { get; }
    internal EventResourceAuthoritySnapshot Snapshot { get; }
    internal bool IsPending => Volatile.Read(ref _preparation) is not null;
    internal CancellationToken CallerCancellation { get; }
    internal EventResourceAuthorityLease(EventResourceAuthorityRequest request,
        EventResourceAuthoritySnapshot snapshot, IEventResourcePrivatePreparation preparation, CancellationToken callerCancellation)
    {
        Request = request;
        Snapshot = snapshot;
        _preparation = preparation;
        CallerCancellation = callerCancellation;
    }
    internal IEventResourcePrivatePreparation? TakePreparation() => Interlocked.Exchange(ref _preparation, null);
    public async ValueTask DisposeAsync()
    {
        var preparation = TakePreparation();
        if (preparation is not null) await preparation.DisposeAsync();
    }
}
