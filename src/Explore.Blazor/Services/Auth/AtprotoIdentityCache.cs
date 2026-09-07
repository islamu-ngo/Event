using CarpaNet.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace Explore.Blazor.Services.Auth;

public sealed class AtprotoIdentityCache : IIdentityCache, IDisposable
{
    public const int MaximumEntriesPerKind = 1000;
    public static readonly TimeSpan HandleTtl = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DidDocumentTtl = TimeSpan.FromMinutes(5);

    private readonly MemoryCache _handles;
    private readonly MemoryCache _didDocuments;

    public AtprotoIdentityCache(TimeProvider? timeProvider = null)
    {
        var clock = new TimeProviderClock(timeProvider ?? TimeProvider.System);
        _handles = CreateCache(clock);
        _didDocuments = CreateCache(clock);
    }

    public Task<DidDocument?> GetDidDocumentAsync(
        string did,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(did))
        {
            return Task.FromResult<DidDocument?>(null);
        }

        return Task.FromResult(
            _didDocuments.TryGetValue<DidDocument>(NormalizeKey(did), out var document)
                ? document
                : null);
    }

    public Task SetDidDocumentAsync(
        string did,
        DidDocument document,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(did) && document is not null)
        {
            _didDocuments.Set(NormalizeKey(did), document, CreateEntryOptions(DidDocumentTtl));
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetHandleDidAsync(
        string handle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(handle))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(
            _handles.TryGetValue<string>(NormalizeKey(handle), out var did)
                ? did
                : null);
    }

    public Task SetHandleDidAsync(
        string handle,
        string did,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(handle) && !string.IsNullOrEmpty(did))
        {
            _handles.Set(NormalizeKey(handle), did, CreateEntryOptions(HandleTtl));
        }

        return Task.CompletedTask;
    }

    public void RemoveDidDocument(string did)
    {
        if (!string.IsNullOrEmpty(did))
        {
            _didDocuments.Remove(NormalizeKey(did));
        }
    }

    public void RemoveHandle(string handle)
    {
        if (!string.IsNullOrEmpty(handle))
        {
            _handles.Remove(NormalizeKey(handle));
        }
    }

    public void Clear()
    {
        _didDocuments.Clear();
        _handles.Clear();
    }

    public void Dispose()
    {
        _didDocuments.Dispose();
        _handles.Dispose();
    }

    private static MemoryCache CreateCache(ISystemClock clock) => new(new MemoryCacheOptions
    {
        Clock = clock,
        SizeLimit = MaximumEntriesPerKind
    });

    private static MemoryCacheEntryOptions CreateEntryOptions(TimeSpan timeToLive) => new()
    {
        AbsoluteExpirationRelativeToNow = timeToLive,
        Size = 1
    };

    private static string NormalizeKey(string key) => key.ToLowerInvariant().Trim();

    private sealed class TimeProviderClock(TimeProvider timeProvider) : ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }
}
