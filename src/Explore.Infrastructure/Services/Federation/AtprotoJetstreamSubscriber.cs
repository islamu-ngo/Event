using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CarpaNet.Jetstream;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoJetstreamSubscriber : BackgroundService
{
    private static readonly Meter Meter = new("Explore.Atproto.Jetstream", "1.0.0");
    private static readonly Counter<long> EnvelopeCounter = Meter.CreateCounter<long>("atproto.jetstream.envelopes");
    private static readonly Counter<long> RecoveryCounter = Meter.CreateCounter<long>("atproto.pds.recovery");

    /// <summary>
    /// End-to-end producer-to-ingest latency: the wall-clock gap between the timestamp the producing PDS
    /// stamped on the commit and the moment this consumer applied it. This is the primary signal for
    /// whether federation is keeping up.
    /// </summary>
    private static readonly Histogram<double> IngestLatency = Meter.CreateHistogram<double>(
        "atproto.jetstream.ingest_latency",
        unit: "ms",
        description: "Milliseconds between the producer commit timestamp and local application of the envelope.");
    private readonly IAtprotoJetstreamRuntimeStore _store;
    private readonly IAtprotoJetstreamEventSource _eventSource;
    private readonly AtprotoJetstreamOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AtprotoJetstreamSubscriber> _logger;
    private readonly AtprotoJetstreamLiveness _liveness;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.CreateVersion7():N}";
    private readonly Channel<bool> _filterChanges = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly object _filterLock = new();
    private readonly IDisposable? _optionsChangeRegistration;
    private AtprotoDid[] _desiredAllowedDids;
    private string? _lastCompletedRecoveryFingerprint;

    public AtprotoJetstreamSubscriber(
        IAtprotoJetstreamRuntimeStore store,
        IAtprotoJetstreamEventSource eventSource,
        IOptionsMonitor<AtprotoJetstreamOptions> options,
        TimeProvider timeProvider,
        ILogger<AtprotoJetstreamSubscriber> logger,
        AtprotoJetstreamLiveness? liveness = null)
    {
        _store = store;
        _eventSource = eventSource;
        _timeProvider = timeProvider;
        _logger = logger;
        // Registered as a singleton, so dependency injection always supplies the instance the readiness
        // probe reads; the default keeps unit tests that do not assert on liveness unchanged.
        _liveness = liveness ?? new AtprotoJetstreamLiveness();
        AtprotoJetstreamOptions configured = options.CurrentValue;
        _options = new AtprotoJetstreamOptions
        {
            Endpoint = configured.Endpoint,
            MaxMessageSizeBytes = configured.MaxMessageSizeBytes,
            EnableCompression = configured.EnableCompression,
            LeaseDurationSeconds = configured.LeaseDurationSeconds,
            LeaseRenewalSeconds = configured.LeaseRenewalSeconds,
            CapabilityPollMilliseconds = configured.CapabilityPollMilliseconds,
            RetryMinimumMilliseconds = configured.RetryMinimumMilliseconds,
            RetryMaximumMilliseconds = configured.RetryMaximumMilliseconds
        };
        _desiredAllowedDids = NormalizeAllowedDids(configured.AllowedDids);
        _optionsChangeRegistration = options.OnChange(HandleOptionsChanged);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int retryMilliseconds = _options.RetryMinimumMilliseconds;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool consumed = await RunSingleLeaseAsync(stoppingToken);
                retryMilliseconds = consumed
                    ? _options.RetryMinimumMilliseconds
                    : Math.Min(_options.RetryMaximumMilliseconds, retryMilliseconds * 2);
                int delay = consumed ? _options.RetryMinimumMilliseconds : Math.Max(_options.CapabilityPollMilliseconds, retryMilliseconds);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (JetstreamV2Exception exception)
            {
                // ConsumerTooSlow means this consumer is the bottleneck, not the service. Backing off
                // makes the backlog worse, so it reconnects immediately and is surfaced at error level
                // for alerting rather than folded into the generic connection-failure counter.
                bool selfInflicted = string.Equals(
                    exception.ErrorName,
                    JetstreamV2ErrorNames.ConsumerTooSlow,
                    StringComparison.Ordinal);
                if (selfInflicted)
                {
                    _logger.LogError(
                        "ATProto Jetstream dropped this consumer as too slow; local ingestion cannot keep up with the stream.");
                }
                else
                {
                    _logger.LogWarning(
                        "ATProto Jetstream subscription failed with {ErrorName}; reconnecting after bounded backoff.",
                        string.IsNullOrEmpty(exception.ErrorName) ? "unspecified" : exception.ErrorName);
                }

                EnvelopeCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", "connection_failure"),
                    new KeyValuePair<string, object?>("error_name", ErrorNameTag(exception.ErrorName)));
                if (!selfInflicted)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(retryMilliseconds), _timeProvider, stoppingToken);
                    retryMilliseconds = Math.Min(_options.RetryMaximumMilliseconds, retryMilliseconds * 2);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "ATProto Jetstream subscription failed with {FailureType}; reconnecting after bounded backoff.",
                    exception.GetType().Name);
                EnvelopeCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", "connection_failure"),
                    new KeyValuePair<string, object?>("error_name", "transport"));
                await Task.Delay(TimeSpan.FromMilliseconds(retryMilliseconds), _timeProvider, stoppingToken);
                retryMilliseconds = Math.Min(_options.RetryMaximumMilliseconds, retryMilliseconds * 2);
            }
        }
    }

    internal async Task<bool> RunSingleLeaseAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> enabledTenants = await _store.ResolveEnabledTenantIdsAsync(cancellationToken);
        if (enabledTenants.Count == 0)
        {
            return false;
        }

        var endpoint = new Uri(_options.Endpoint, UriKind.Absolute);
        string service = endpoint.GetLeftPart(UriPartial.Authority);
        DateTime claimedAt = _timeProvider.GetUtcNow().UtcDateTime;
        AtprotoJetstreamClaim? claim = await _store.TryClaimAsync(
            service,
            _owner,
            claimedAt,
            TimeSpan.FromSeconds(_options.LeaseDurationSeconds),
            cancellationToken);
        if (claim is null)
        {
            return false;
        }

        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task renewal = RenewLeaseAsync(claim, leaseCancellation);
        Task<RecoveryPumpExit>? recovery = null;
        var state = new LeaseState(claim.Cursor);
        try
        {
            recovery = ProcessRecoveryAsync(claim, leaseCancellation.Token);
            // v2 filters are fixed for the life of a connection, so a DID filter change is served by
            // reconnecting inside the lease rather than by dropping it and waiting for expiry.
            while (true)
            {
                SessionExit exit = await RunSessionAsync(
                    claim,
                    endpoint,
                    state,
                    recovery,
                    leaseCancellation.Token);
                if (exit == SessionExit.Reconnect)
                {
                    continue;
                }

                return exit == SessionExit.Completed && state.AppliedAny;
            }
        }
        finally
        {
            leaseCancellation.Cancel();
            Exception? backgroundFailure = null;
            if (recovery is not null)
            {
                try
                {
                    await recovery;
                }
                catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    backgroundFailure = exception;
                }
            }

            try
            {
                await renewal;
            }
            catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                backgroundFailure ??= exception;
            }

            if (backgroundFailure is not null)
            {
                ExceptionDispatchInfo.Capture(backgroundFailure).Throw();
            }
        }
    }

    private async Task<SessionExit> RunSessionAsync(
        AtprotoJetstreamClaim claim,
        Uri endpoint,
        LeaseState state,
        Task<RecoveryPumpExit> recovery,
        CancellationToken leaseToken)
    {
        AtprotoDid[] connectionDids = ReadDesiredAllowedDids();
        string[] exactConnectionDids = ExactValues(connectionDids);
        var subscription = new AtprotoJetstreamSubscription(
            endpoint,
            AtprotoJetstreamConstants.Collections,
            connectionDids,
            state.ResumeFromTip || state.Cursor == 0 ? null : state.Cursor,
            _options.MaxMessageSizeBytes)
        {
            EnableCompression = _options.EnableCompression,
            ReconnectBackoffMin = TimeSpan.FromMilliseconds(_options.RetryMinimumMilliseconds),
            ReconnectBackoffMax = TimeSpan.FromMilliseconds(_options.RetryMaximumMilliseconds)
        };
        state.ResumeFromTip = false;

        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(leaseToken);
        IAtprotoJetstreamSession? session = null;
        IAsyncEnumerator<JetstreamV2Event>? events = null;
        Task<bool>? nextEvent = null;
        Task? filterChange = null;
        bool readFailureHandled = false;
        try
        {
            session = await _eventSource.OpenSessionAsync(subscription, sessionCancellation.Token);
            _liveness.MarkConnected(_timeProvider.GetUtcNow().UtcDateTime, state.Cursor);
            filterChange = WaitForFilterChangeAsync(connectionDids, sessionCancellation.Token);
            events = session
                .ReadEventsAsync(sessionCancellation.Token)
                .GetAsyncEnumerator(sessionCancellation.Token);
            nextEvent = events.MoveNextAsync().AsTask();
            while (true)
            {
                Task completed = await Task.WhenAny(nextEvent, filterChange, recovery);
                if (completed == recovery)
                {
                    if (await recovery == RecoveryPumpExit.FenceRejected)
                    {
                        return SessionExit.FenceRejected;
                    }

                    throw new InvalidOperationException("The ATProto recovery pump stopped unexpectedly.");
                }

                if (completed == filterChange)
                {
                    await filterChange;
                    EnvelopeCounter.Add(1, new KeyValuePair<string, object?>("outcome", "filter_reconnect"));
                    return SessionExit.Reconnect;
                }

                if (!await nextEvent)
                {
                    return SessionExit.Completed;
                }

                JetstreamV2Event envelope = events.Current;
                // The v2 cursor is inclusive and delivery is at-least-once, so the overlap after a
                // reconnect is expected rather than exceptional.
                if (envelope.Seq <= state.Cursor)
                {
                    EnvelopeCounter.Add(1, new KeyValuePair<string, object?>("outcome", "replay"));
                    nextEvent = events.MoveNextAsync().AsTask();
                    continue;
                }

                IReadOnlyList<Guid> enabledTenants = await _store.ResolveEnabledTenantIdsAsync(sessionCancellation.Token);
                if (enabledTenants.Count == 0)
                {
                    return SessionExit.Completed;
                }

                DateTime observedAt = _timeProvider.GetUtcNow().UtcDateTime;
                AtprotoJetstreamParsedEnvelope parsed = AtprotoJetstreamEnvelopeParser.Parse(
                    envelope,
                    state.Cursor,
                    exactConnectionDids,
                    observedAt);
                if (parsed.Ignored)
                {
                    // Deliberately does not move state.Cursor: that value has to keep mirroring the
                    // persisted cursor or the next apply would fail its fence check.
                    EnvelopeCounter.Add(1, new KeyValuePair<string, object?>("outcome", "ignored_kind"));
                    nextEvent = events.MoveNextAsync().AsTask();
                    continue;
                }

                IReadOnlyList<AtprotoRecordTenantPresentation> presentations =
                    parsed.Record is { TombstonedAt: null }
                        ? enabledTenants.Select(tenantId => new AtprotoRecordTenantPresentation
                        {
                            TenantId = tenantId,
                            IsVisible = true
                        }).ToArray()
                        : [];
                var request = new AtprotoJetstreamApplyRequest(
                    claim,
                    state.Cursor,
                    parsed.Cursor,
                    parsed.Record,
                    presentations,
                    parsed.Quarantine,
                    observedAt,
                    parsed.AdvanceCursor,
                    parsed.EventProjection,
                    parsed.EventProjectionInvalidation)
                {
                    AccountPurge = parsed.AccountPurge
                };
                if (!await _store.TryApplyAndAdvanceAsync(request, sessionCancellation.Token))
                {
                    EnvelopeCounter.Add(1, new KeyValuePair<string, object?>("outcome", "fence_rejected"));
                    return SessionExit.FenceRejected;
                }

                if (parsed.AdvanceCursor)
                {
                    state.Cursor = parsed.Cursor;
                }
                state.AppliedAny = true;
                string collectionTag = parsed.AccountPurge is not null
                    ? "account"
                    : CollectionTag(envelope.Commit?.Collection);
                EnvelopeCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", ApplyOutcomeTag(parsed)),
                    new KeyValuePair<string, object?>("collection", collectionTag));
                RecordIngestLatency(envelope.TimeUs, observedAt, collectionTag);
                nextEvent = events.MoveNextAsync().AsTask();
            }
        }
        catch (JetstreamV2Exception exception) when (string.Equals(
            exception.ErrorName,
            JetstreamV2ErrorNames.CursorTooOld,
            StringComparison.Ordinal))
        {
            // The sealed archive has moved past our seq. Re-enter at the live tip and leave the gap to
            // the governed PDS recovery pump, which honours per-tenant backfill policy.
            _logger.LogWarning(
                "ATProto Jetstream cursor was older than the retained archive; resuming from the live tip.");
            EnvelopeCounter.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "cursor_too_old"),
                new KeyValuePair<string, object?>("error_name", ErrorNameTag(exception.ErrorName)));
            state.ResumeFromTip = true;
            readFailureHandled = true;
            // Jumping to the tip opens a gap that only PDS recovery can close, but the recovery memo is
            // keyed on scope alone and would otherwise short-circuit for the life of the process. Clearing
            // it forces one more reconciliation pass now that the cursor has moved.
            Volatile.Write(ref _lastCompletedRecoveryFingerprint, null);
            return SessionExit.Reconnect;
        }
        finally
        {
            sessionCancellation.Cancel();
            _liveness.MarkDisconnected(_timeProvider.GetUtcNow().UtcDateTime, state.Cursor);
            Exception? sessionFailure = null;
            if (nextEvent is not null)
            {
                try
                {
                    await nextEvent;
                }
                catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception) when (!readFailureHandled)
                {
                    // Rethrowing here would replace the value the catch block just returned.
                    sessionFailure = exception;
                }
                catch
                {
                }
            }

            if (filterChange is not null)
            {
                try
                {
                    await filterChange;
                }
                catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    sessionFailure ??= exception;
                }
            }

            if (events is not null)
            {
                try
                {
                    await events.DisposeAsync();
                }
                catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    sessionFailure ??= exception;
                }
            }

            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch (Exception exception)
                {
                    sessionFailure ??= exception;
                }
            }

            if (sessionFailure is not null)
            {
                ExceptionDispatchInfo.Capture(sessionFailure).Throw();
            }
        }
    }

    public override void Dispose()
    {
        _optionsChangeRegistration?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Completes once the desired DID filter genuinely differs from the one this connection was opened
    /// with, coalescing bursts so a storm of configuration reloads costs a single reconnect.
    /// </summary>
    private async Task WaitForFilterChangeAsync(
        AtprotoDid[] connectionDids,
        CancellationToken cancellationToken)
    {
        while (await _filterChanges.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_filterChanges.Reader.TryRead(out _))
            {
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(_options.CapabilityPollMilliseconds),
                _timeProvider,
                cancellationToken);
            while (_filterChanges.Reader.TryRead(out _))
            {
            }

            if (!ReadDesiredAllowedDids().SequenceEqual(connectionDids))
            {
                return;
            }
        }
    }

    private async Task<RecoveryPumpExit> ProcessRecoveryAsync(
        AtprotoJetstreamClaim claim,
        CancellationToken cancellationToken)
    {
        int initialRetryMilliseconds = Math.Max(
            _options.CapabilityPollMilliseconds,
            _options.RetryMinimumMilliseconds);
        int maximumRetryMilliseconds = Math.Max(
            _options.CapabilityPollMilliseconds,
            _options.RetryMaximumMilliseconds);
        int retryMilliseconds = initialRetryMilliseconds;
        while (true)
        {
            int delayMilliseconds;
            try
            {
                var command = new ReconcileAtprotoPdsSnapshotsCommand(
                    claim,
                    ExactValues(ReadDesiredAllowedDids()),
                    _timeProvider.GetUtcNow().UtcDateTime,
                    Volatile.Read(ref _lastCompletedRecoveryFingerprint));
                AtprotoPdsRecoveryResult result = await _store.ReconcilePdsSnapshotsAsync(
                    command,
                    cancellationToken);
                if (IsCompletedRecoveryOutcome(result.Outcome))
                {
                    Volatile.Write(ref _lastCompletedRecoveryFingerprint, result.Fingerprint);
                }

                RecoveryCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", RecoveryOutcomeTag(result.Outcome)));
                if (result.Outcome == AtprotoPdsRecoveryOutcome.FenceRejected)
                {
                    return RecoveryPumpExit.FenceRejected;
                }

                if (IsCompletedRecoveryOutcome(result.Outcome))
                {
                    retryMilliseconds = initialRetryMilliseconds;
                    delayMilliseconds = _options.CapabilityPollMilliseconds;
                }
                else
                {
                    delayMilliseconds = retryMilliseconds;
                    retryMilliseconds = DoubleBounded(retryMilliseconds, maximumRetryMilliseconds);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "ATProto PDS recovery failed with {FailureType}; retrying with bounded backoff under the current consumer lease.",
                    exception.GetType().Name);
                RecoveryCounter.Add(1, new KeyValuePair<string, object?>("outcome", "recovery_failure"));
                delayMilliseconds = retryMilliseconds;
                retryMilliseconds = DoubleBounded(retryMilliseconds, maximumRetryMilliseconds);
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(delayMilliseconds),
                _timeProvider,
                cancellationToken);
        }
    }

    private void HandleOptionsChanged(AtprotoJetstreamOptions options, string? _)
    {
        AtprotoDid[] desired = NormalizeAllowedDids(options.AllowedDids);
        lock (_filterLock)
        {
            if (desired.SequenceEqual(_desiredAllowedDids))
            {
                return;
            }

            _desiredAllowedDids = desired;
        }

        _filterChanges.Writer.TryWrite(true);
    }

    private AtprotoDid[] ReadDesiredAllowedDids()
    {
        lock (_filterLock)
        {
            return _desiredAllowedDids;
        }
    }

    internal static AtprotoDid[] NormalizeAllowedDids(IEnumerable<string> dids) =>
        dids.Select(did => AtprotoDid.TryParse(did, out AtprotoDid parsedDid)
                ? parsedDid
                : throw new OptionsValidationException(
                    AtprotoJetstreamOptions.SectionName,
                    typeof(AtprotoJetstreamOptions),
                    ["The Jetstream DID filter is invalid."]))
            .Distinct()
            .OrderBy(did => did.Value, StringComparer.Ordinal)
            .ToArray();

    private static string[] ExactValues(IEnumerable<AtprotoDid> dids) =>
        dids.Select(did => did.Value).ToArray();

    private async Task RenewLeaseAsync(
        AtprotoJetstreamClaim claim,
        CancellationTokenSource leaseCancellation)
    {
        try
        {
            while (!leaseCancellation.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.LeaseRenewalSeconds),
                    _timeProvider,
                    leaseCancellation.Token);
                DateTime observedAt = _timeProvider.GetUtcNow().UtcDateTime;
                bool renewed = await _store.TryRenewAsync(
                    claim,
                    observedAt,
                    observedAt.AddSeconds(_options.LeaseDurationSeconds),
                    leaseCancellation.Token);
                if (!renewed)
                {
                    _logger.LogWarning("ATProto Jetstream lease renewal was fenced; the active stream is being cancelled.");
                    EnvelopeCounter.Add(1, new KeyValuePair<string, object?>("outcome", "lease_lost"));
                    leaseCancellation.Cancel();
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !leaseCancellation.IsCancellationRequested)
        {
            _logger.LogWarning(
                "ATProto Jetstream lease renewal failed with {FailureType}; the active stream is being cancelled.",
                exception.GetType().Name);
            EnvelopeCounter.Add(1, new KeyValuePair<string, object?>("outcome", "lease_renewal_failure"));
            leaseCancellation.Cancel();
            throw;
        }
    }

    private static string ApplyOutcomeTag(AtprotoJetstreamParsedEnvelope parsed) => parsed switch
    {
        { AccountPurge: not null } => "account_purged",
        { Quarantine: not null } => "quarantined",
        _ => "materialized"
    };

    private static string CollectionTag(string? collection) => collection switch
    {
        AtprotoJetstreamConstants.EventCollection => "event",
        AtprotoJetstreamConstants.RsvpCollection => "rsvp",
        _ => "unsupported"
    };

    /// <summary>
    /// Records producer-to-ingest latency. Skips envelopes whose <c>time_us</c> is outside DateTime range
    /// or in the future, so a misbehaving producer clock cannot poison the histogram.
    /// </summary>
    private static void RecordIngestLatency(long timeUs, DateTime observedAt, string collectionTag)
    {
        if (timeUs <= 0)
        {
            return;
        }

        DateTime producedAt;
        try
        {
            producedAt = DateTime.UnixEpoch.AddTicks(checked(timeUs * 10));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            return;
        }

        double latency = (observedAt - producedAt).TotalMilliseconds;
        if (latency >= 0)
        {
            IngestLatency.Record(latency, new KeyValuePair<string, object?>("collection", collectionTag));
        }
    }

    /// <summary>Maps to the closed v2 error-name set so the metric stays bounded-cardinality.</summary>
    private static string ErrorNameTag(string? errorName) => errorName switch
    {
        JetstreamV2ErrorNames.CursorTooOld => "cursor_too_old",
        JetstreamV2ErrorNames.ConsumerTooSlow => "consumer_too_slow",
        JetstreamV2ErrorNames.UnknownZstdDictionary => "unknown_zstd_dictionary",
        JetstreamV2ErrorNames.InvalidRequest => "invalid_request",
        JetstreamV2ErrorNames.ServiceUnavailable => "service_unavailable",
        _ => "unspecified"
    };

    private static bool IsCompletedRecoveryOutcome(AtprotoPdsRecoveryOutcome outcome) => outcome is
        AtprotoPdsRecoveryOutcome.Disabled
        or AtprotoPdsRecoveryOutcome.DowntimeOnly
        or AtprotoPdsRecoveryOutcome.Unchanged
        or AtprotoPdsRecoveryOutcome.Completed;

    private static int DoubleBounded(int value, int maximum) =>
        (int)Math.Min(maximum, (long)value * 2);

    private static string RecoveryOutcomeTag(AtprotoPdsRecoveryOutcome outcome) => outcome switch
    {
        AtprotoPdsRecoveryOutcome.Disabled => "recovery_disabled",
        AtprotoPdsRecoveryOutcome.DowntimeOnly => "recovery_cursor_only",
        AtprotoPdsRecoveryOutcome.ScopeRejected => "recovery_scope_rejected",
        AtprotoPdsRecoveryOutcome.Unchanged => "recovery_unchanged",
        AtprotoPdsRecoveryOutcome.Completed => "recovery_completed",
        AtprotoPdsRecoveryOutcome.PartialFailure => "recovery_partial_failure",
        AtprotoPdsRecoveryOutcome.FenceRejected => "recovery_fence_rejected",
        _ => "recovery_unknown"
    };

    /// <summary>
    /// Cursor and progress carried across the successive connections of one lease. <see cref="Cursor"/>
    /// holds the v2 <c>seq</c> and must always mirror the persisted cursor, because it is the fence value
    /// every apply is validated against.
    /// </summary>
    private sealed class LeaseState(long cursor)
    {
        public long Cursor { get; set; } = cursor;
        public bool AppliedAny { get; set; }
        public bool ResumeFromTip { get; set; }
    }

    private enum SessionExit
    {
        Completed,
        Reconnect,
        FenceRejected
    }

    private enum RecoveryPumpExit
    {
        FenceRejected
    }
}
