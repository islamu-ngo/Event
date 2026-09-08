using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Projections;

internal static class ProjectionInfrastructure
{
    internal static Task<bool> TryAcquireAdvisoryLockAsync(
        ExploreDbContext dbContext,
        int projectionLockKey,
        Guid tenantId,
        bool exclusive,
        CancellationToken cancellationToken) =>
        RelationalProjectionLock.TryAcquireAsync(
            dbContext,
            projectionLockKey,
            tenantId,
            exclusive,
            cancellationToken);

    internal static int ComputeStableKey(string value)
    {
        unchecked
        {
            const int fnvOffsetBasis = unchecked((int)2166136261);
            const int fnvPrime = 16777619;
            var hash = fnvOffsetBasis;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= fnvPrime;
            }

            return hash;
        }
    }

    internal static IEnumerable<IReadOnlyList<T>> Chunk<T>(
        IReadOnlyList<T> source,
        int size)
    {
        if (size <= 0)
        {
            yield return source;
            yield break;
        }

        for (var index = 0; index < source.Count; index += size)
        {
            int end = Math.Min(index + size, source.Count);
            var chunk = new List<T>(end - index);
            for (int itemIndex = index; itemIndex < end; itemIndex++)
            {
                chunk.Add(source[itemIndex]);
            }

            yield return chunk;
        }
    }
}
