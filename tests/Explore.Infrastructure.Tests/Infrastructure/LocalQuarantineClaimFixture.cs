using Explore.Persistence.Services;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Infrastructure.Tests.Infrastructure;

internal sealed class LocalQuarantineClaimFixture : IAsyncDisposable, IGlobalExploreDbContextFactory
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private DbContextOptions<ExploreDbContext> options = null!;
    internal StorageReconciliationOwner Owner { get; private set; } = null!;
    internal FileStorageQuarantineInput Input { get; private set; } = null!;

    internal static async Task<LocalQuarantineClaimFixture> CreateAsync(
        string root, string key, long size, DateTime lastModified)
    {
        var fixture = new LocalQuarantineClaimFixture();
        try
        {
            await fixture.connection.OpenAsync();
            fixture.options = new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite(fixture.connection).UseSnakeCaseNamingConvention().Options;
            await using var seed = new ExploreDbContext(fixture.options);
            await seed.Database.EnsureCreatedAsync();
            var binding = StorageProviderBinding.Local(root);
            seed.Add(binding);
            await seed.SaveChangesAsync();
            fixture.Owner = new StorageReconciliationOwner(fixture);
            var work = await fixture.Owner.AdmitLocalQuarantineAsync(binding.Id, key, size,
                lastModified, DateTime.UtcNow, "test-orphan", default)
                ?? throw new InvalidOperationException("Original quarantine admission failed.");
            var claim = (await fixture.Owner.ClaimLocalQuarantineAsync(1, TimeSpan.FromMinutes(5), default))
                .Single(row => row.Id == work.Id);
            fixture.Input = new(claim.ObjectKey, claim.Id, claim.ObservedSize, claim.ObservedLastModifiedUtc,
                claim.ProviderBindingId, claim.ConcurrencyStamp, claim.LeaseExpiresAtUtc!.Value,
                claim.CanonicalPhysicalPath, claim.PhysicalPathHash);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ExploreDbContext(options));
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
