namespace Explore.Infrastructure.Secrets;

using Explore.Application.Contracts.Secrets;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Coordinates process-local cache publication and invalidation across scoped resolvers.
/// </summary>
public sealed class SecretResolverCacheState
{
    internal const int MaximumTrackedEntries = 4096;
    private readonly object _gate = new();
    private readonly IMemoryCache _cache;
    private readonly Dictionary<string, string> _cacheKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _invalidations = new(StringComparer.Ordinal);
    private long _generation;
    private long _minimumGeneration;

    public SecretResolverCacheState(IMemoryCache cache) => _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    internal long CaptureGeneration()
    {
        lock (_gate)
            return _generation;
    }

    internal SecretResolutionResult? Read(long generation, string[] prefixes, string cacheKey)
    {
        lock (_gate)
        {
            if (!IsCurrent(generation, prefixes))
                return SecretResolutionResult.Unavailable;

            return _cache.TryGetValue<ResolvedSecret>(cacheKey, out var cached) && cached is not null
                ? SecretResolutionResult.Resolved(cached)
                : null;
        }
    }

    internal SecretResolutionResult Complete(
        long generation, string[] prefixes, string logicalCacheKey, string cacheKey,
        SecretResolutionResult result)
    {
        lock (_gate)
        {
            // Invalidation and publication share this gate, including the final return decision.
            if (!IsCurrent(generation, prefixes))
                return SecretResolutionResult.Unavailable;

            if (result.IsResolved)
            {
                if (_cacheKeys.TryGetValue(logicalCacheKey, out string? previousCacheKey)
                    && !string.Equals(previousCacheKey, cacheKey, StringComparison.Ordinal))
                    _cache.Remove(previousCacheKey);

                if (!_cacheKeys.ContainsKey(logicalCacheKey) && _cacheKeys.Count == MaximumTrackedEntries)
                {
                    string oldestKey = _cacheKeys.Keys.First();
                    _cache.Remove(_cacheKeys[oldestKey]);
                    _cacheKeys.Remove(oldestKey);
                }
                _cacheKeys[logicalCacheKey] = cacheKey;
                _cache.Set(cacheKey, result.Secret!, SecretResolver.CacheTtl);
            }

            return result;
        }
    }

    internal void Invalidate(string prefix)
    {
        lock (_gate)
        {
            if (!_invalidations.ContainsKey(prefix) && _invalidations.Count == MaximumTrackedEntries)
            {
                // Forgetting tombstones must never make an old in-flight read current.
                // Reject all older observations conservatively at the compaction boundary.
                _minimumGeneration = checked(_generation + 1);
                _invalidations.Clear();
            }
            _invalidations[prefix] = checked(++_generation);
            foreach (string logicalCacheKey in _cacheKeys.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                _cache.Remove(_cacheKeys[logicalCacheKey]);
                _cacheKeys.Remove(logicalCacheKey);
            }
        }
    }

    private bool IsCurrent(long generation, string[] prefixes) =>
        generation >= _minimumGeneration
        && prefixes.All(prefix => !_invalidations.TryGetValue(prefix, out long invalidatedAt)
            || invalidatedAt <= generation);
}
