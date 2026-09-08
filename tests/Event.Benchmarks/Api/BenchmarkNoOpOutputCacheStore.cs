using Microsoft.AspNetCore.OutputCaching;

namespace Event.Benchmarks.Api;

internal sealed class BenchmarkNoOpOutputCacheStore : IOutputCacheStore
{
    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<byte[]?>(null);
    }

    public ValueTask SetAsync(
        string key,
        byte[] value,
        string[]? tags,
        TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
