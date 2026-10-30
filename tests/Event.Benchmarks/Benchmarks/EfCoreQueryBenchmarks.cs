using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Configuration;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainEvent = Explore.Domain.Event;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
[BenchmarkCategory("QueryConstruction", "DeferredExecution", "NoSqlExecution", "NpgsqlModel")]
public class EfCoreQueryBenchmarks : IDisposable
{
    private static readonly Func<ExploreDbContext, Guid, IAsyncEnumerable<DomainEvent>> CompiledEventQuery =
        EF.CompileAsyncQuery(
            (ExploreDbContext context, Guid tenantId) => context.Events
                .AsNoTracking()
                .Where(e => e.TenantId == tenantId)
                .OrderByDescending(e => e.CreatedAt)
                .Take(25));

    private ExploreDbContext _dbContext = null!;
    private Guid _tenantId;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql()
            .Options;

        _dbContext = new ExploreDbContext(options);
        _tenantId = Guid.NewGuid();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _dbContext.Dispose();

    [Benchmark(Baseline = true, Description = "Construct tracked query (no SQL)")]
    public void QueryConstruction_WithTracking()
    {
        GC.KeepAlive(_dbContext.Events
            .Include(e => e.EventType)
            .Include(e => e.AudienceGender)
            .Include(e => e.Actor)
            .Include(e => e.EventStatus)
            .Include(e => e.EventFormat)
            .AsSplitQuery()
            .Where(e => e.TenantId == _tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(50));
    }

    [Benchmark(Description = "Construct untracked query (no SQL)")]
    public void QueryConstruction_WithNoTracking()
    {
        GC.KeepAlive(_dbContext.Events
            .AsNoTracking()
            .Include(e => e.EventType)
            .Include(e => e.AudienceGender)
            .Include(e => e.Actor)
            .Include(e => e.EventStatus)
            .Include(e => e.EventFormat)
            .AsSplitQuery()
            .Where(e => e.TenantId == _tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(50));
    }

    [Benchmark(Description = "Invoke compiled query without enumeration (no SQL)")]
    public void CompiledQuery_Invocation()
    {
        // Preserve deferred invocation without enumerating the result or executing SQL.
        GC.KeepAlive(CompiledEventQuery(_dbContext, _tenantId));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
