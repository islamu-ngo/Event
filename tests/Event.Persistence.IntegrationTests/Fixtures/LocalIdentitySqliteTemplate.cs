using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>
/// Caches only closed SQLite database images for the Identity tests' default-schema,
/// snake-case models. Every fixture receives a private writable database, never a shared connection.
/// </summary>
internal static class LocalIdentitySqliteTemplate
{
    private static readonly Lazy<Task<DatabaseImages>> Images = new(CreateImagesAsync);

    // Shared schema work has its own bounded startup lifetime, before per-fixture operation deadlines.
    internal static Task InitializeAsync() => Images.Value;

    internal static async Task CopyAsync(string applicationPath, string? identityPath,
        bool seedLookups, CancellationToken cancellationToken)
    {
        DatabaseImages images = await Images.Value.WaitAsync(cancellationToken);
        await File.WriteAllBytesAsync(applicationPath,
            seedLookups ? images.SeededApplication : images.EmptyApplication, cancellationToken);
        if (identityPath is not null)
            await File.WriteAllBytesAsync(identityPath, images.ExternalIdentity, cancellationToken);
    }

    internal static async Task CopySeededApplicationToAsync(
        SqliteConnection destination, CancellationToken cancellationToken)
    {
        DatabaseImages images = await Images.Value.WaitAsync(cancellationToken);
        string path = Path.Combine(Path.GetTempPath(), $"identity-memory-copy-{Guid.CreateVersion7():N}.db");
        try
        {
            await File.WriteAllBytesAsync(path, images.SeededApplication, cancellationToken);
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await source.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<DatabaseImages> CreateImagesAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var metadataCache = new MemoryCache(new MemoryCacheOptions());
        CancellationToken token = timeout.Token;
        string applicationPath = Path.Combine(Path.GetTempPath(), $"identity-template-app-{Guid.CreateVersion7():N}.db");
        string identityPath = Path.Combine(Path.GetTempPath(), $"identity-template-external-{Guid.CreateVersion7():N}.db");
        try
        {
            var applicationOptions = TestDbContextOptions.Create<ExploreDbContext>();
            Configure(applicationOptions, applicationPath, metadataCache);
            await using (var application = new ExploreDbContext(applicationOptions.Options))
                await application.Database.EnsureCreatedAsync(token);
            // Pooling is disabled: the final connection close checkpoints WAL before bytes are read.
            byte[] emptyApplication = await File.ReadAllBytesAsync(applicationPath, token);
            await using (var application = new ExploreDbContext(applicationOptions.Options))
            {
                // Lookup setup is immutable, not an operation under test. One commit avoids
                // hundreds of per-table durable commits while retaining every production seed row.
                await using var transaction = await application.Database.BeginTransactionAsync(token);
                await LookupTableSeeder.SeedAsync(application, token);
                await transaction.CommitAsync(token);
            }
            byte[] seededApplication = await File.ReadAllBytesAsync(applicationPath, token);
            var identityOptions = TestDbContextOptions.Create<ExternalIdentityDbContext>();
            Configure(identityOptions, identityPath, metadataCache);
            await using (var identity = new ExternalIdentityDbContext(identityOptions.Options))
                await identity.Database.EnsureCreatedAsync(token);
            byte[] externalIdentity = await File.ReadAllBytesAsync(identityPath, token);
            return new DatabaseImages(emptyApplication, seededApplication, externalIdentity);
        }
        finally
        {
            foreach (string path in new[] { applicationPath, identityPath })
            {
                File.Delete(path);
                File.Delete(path + "-wal");
                File.Delete(path + "-shm");
            }
        }
    }

    private static void Configure(DbContextOptionsBuilder options, string path, IMemoryCache metadataCache) => options
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
        .UseSnakeCaseNamingConvention()
        .UseMemoryCache(metadataCache)
        .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance);

    private sealed record DatabaseImages(byte[] EmptyApplication, byte[] SeededApplication, byte[] ExternalIdentity);
}
