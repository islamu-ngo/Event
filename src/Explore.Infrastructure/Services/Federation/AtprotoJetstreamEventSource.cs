using System.Runtime.CompilerServices;
using CarpaNet.Jetstream;
using Explore.Domain.ValueObjects;

namespace Explore.Infrastructure.Services.Federation;

public sealed record AtprotoJetstreamSubscription(
    Uri Endpoint,
    IReadOnlyList<string> Collections,
    IReadOnlyList<AtprotoDid> Dids,
    long? LiveCursor,
    int MaxMessageSizeBytes)
{
    public bool EnableCompression { get; init; }
    public TimeSpan ReconnectBackoffMin { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ReconnectBackoffMax { get; init; } = TimeSpan.FromSeconds(30);
}

public interface IAtprotoJetstreamSession : IAsyncDisposable
{
    IAsyncEnumerable<JetstreamV2Event> ReadEventsAsync(CancellationToken cancellationToken);
}

public interface IAtprotoJetstreamEventSource
{
    Task<IAtprotoJetstreamSession> OpenSessionAsync(
        AtprotoJetstreamSubscription subscription,
        CancellationToken cancellationToken);
}

internal sealed class CarpaNetJetstreamEventSource : IAtprotoJetstreamEventSource
{
    public Task<IAtprotoJetstreamSession> OpenSessionAsync(
        AtprotoJetstreamSubscription subscription,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = new JetstreamV2Client(
            subscription.Endpoint,
            new JetstreamV2ClientOptions
            {
                EnableCompression = subscription.EnableCompression,
                ReadLimitBytes = subscription.MaxMessageSizeBytes,
                ReconnectBackoffMin = subscription.ReconnectBackoffMin,
                ReconnectBackoffMax = subscription.ReconnectBackoffMax
            });
        try
        {
            return Task.FromResult<IAtprotoJetstreamSession>(
                new CarpaNetJetstreamSession(client, CreateSubscribeOptions(subscription), cancellationToken));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // v2 carries the filter set in the subscribe request itself; there is no options_update frame to
    // renegotiate it later, so a filter change has to be expressed as a fresh subscription.
    // Account is requested alongside Commit for the deletion purge signal. It is bounded: the DID filter
    // does constrain account events, and unfiltered account traffic is only around twenty events a minute
    // network-wide. Identity and Sync stay unrequested — neither changes what we present.
    internal static JetstreamV2SubscribeOptions CreateSubscribeOptions(AtprotoJetstreamSubscription subscription) =>
        new()
        {
            Kinds = [JetstreamV2EventKind.Commit, JetstreamV2EventKind.Account],
            Collections =
            [
                AtprotoJetstreamConstants.EventCollection,
                AtprotoJetstreamConstants.RsvpCollection
            ],
            Dids = [.. subscription.Dids.Select(did => did.Value)],
            LiveCursor = subscription.LiveCursor,
            MaxMessageSizeBytes = subscription.MaxMessageSizeBytes
        };

    private sealed class CarpaNetJetstreamSession : IAtprotoJetstreamSession
    {
        private readonly JetstreamV2Client _client;
        private readonly JetstreamV2SubscribeOptions _options;
        private readonly CancellationTokenSource _lifetimeCancellation;
        private int _readStarted;
        private int _disposed;

        public CarpaNetJetstreamSession(
            JetstreamV2Client client,
            JetstreamV2SubscribeOptions options,
            CancellationToken cancellationToken)
        {
            _client = client;
            _options = options;
            _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public IAsyncEnumerable<JetstreamV2Event> ReadEventsAsync(CancellationToken cancellationToken)
        {
            // Guard eagerly rather than from inside the iterator, so a second reader fails at the call
            // site instead of on its first MoveNext.
            if (Interlocked.Exchange(ref _readStarted, 1) != 0)
            {
                throw new InvalidOperationException("The Jetstream session event reader has already been started.");
            }

            return ReadCoreAsync(cancellationToken);
        }

        private async IAsyncEnumerable<JetstreamV2Event> ReadCoreAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken);
            await foreach (JetstreamV2Event value in _client
                .SubscribeAsync(_options, readCancellation.Token)
                .ConfigureAwait(false))
            {
                yield return value;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            _lifetimeCancellation.Cancel();
            _client.Dispose();
            _lifetimeCancellation.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
