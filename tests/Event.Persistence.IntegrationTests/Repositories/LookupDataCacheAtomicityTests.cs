using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Caching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class LookupDataCacheAtomicityTests
{
    [Test]
    public async Task FailedLateCatalogueLoadDoesNotPublishPartialInitialization()
    {
        await using var database = await CatalogueDatabase.CreateAsync();
        await database.MakeOwnerTypesUnavailableAsync();

        await Assert.ThrowsAsync<SqliteException>(() => database.Cache.InitializeAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            _ = database.Cache.Get<EventType>();
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            _ = database.Cache.Get<OwnerType>();
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task FailedLateRefreshPreservesThePreviousCompleteCatalogue()
    {
        await using var database = await CatalogueDatabase.CreateAsync();
        await database.Cache.InitializeAsync();
        var originalTypes = database.Cache.Get<EventType>();
        var originalOwners = database.Cache.Get<OwnerType>();
        await database.ChangeEventTypeAsync();
        await database.MakeOwnerTypesUnavailableAsync();

        await Assert.ThrowsAsync<SqliteException>(() => database.Cache.RefreshAsync());

        await Assert.That(database.Cache.Get<EventType>()).IsSameReferenceAs(originalTypes);
        await Assert.That(database.Cache.Get<OwnerType>()).IsSameReferenceAs(originalOwners);
        await Assert.That(database.Cache.GetById<EventType>(101)!.FullName).IsEqualTo("Original catalogue type");
    }

    [Test]
    public async Task CancellationDuringLateRefreshPropagatesAndPreservesThePreviousCatalogue()
    {
        await using var database = await CatalogueDatabase.CreateAsync();
        await database.Cache.InitializeAsync();
        var originalTypes = database.Cache.Get<EventType>();
        var originalOwners = database.Cache.Get<OwnerType>();
        await database.ChangeEventTypeAsync();
        using var cancellation = new CancellationTokenSource();
        database.CancelOwnerRead.Cancellation = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            database.Cache.RefreshAsync(cancellation.Token));

        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(database.Cache.Get<EventType>()).IsSameReferenceAs(originalTypes);
        await Assert.That(database.Cache.Get<OwnerType>()).IsSameReferenceAs(originalOwners);
        await Assert.That(database.Cache.GetById<EventType>(101)!.FullName).IsEqualTo("Original catalogue type");
    }

    [Test]
    public async Task CancelledQueuedRefreshCannotPublishOrInterruptTheActiveReplacement()
    {
        await using var database = await CatalogueDatabase.CreateAsync();
        await database.Cache.InitializeAsync();
        var originalTypes = database.Cache.Get<EventType>();
        var originalOwners = database.Cache.Get<OwnerType>();
        await database.ChangeEventTypeAsync();
        database.CancelOwnerRead.Pause = true;
        Task active = database.Cache.RefreshAsync();
        try
        {
            await database.CancelOwnerRead.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            Task queued = database.Cache.RefreshAsync(cancellation.Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => queued);

            await Assert.That(database.Cache.Get<EventType>()).IsSameReferenceAs(originalTypes);
            await Assert.That(database.Cache.Get<OwnerType>()).IsSameReferenceAs(originalOwners);
        }
        finally
        {
            database.CancelOwnerRead.Release.TrySetResult(true);
            await active.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await Assert.That(database.Cache.GetById<EventType>(101)!.FullName).IsEqualTo("Unpublished replacement type");
        await Assert.That(database.Cache.GetById<OwnerType>(101)!.FullName).IsEqualTo("Original catalogue owner");
    }

    private sealed class CatalogueDatabase(SqliteConnection connection) : IDbContextFactory<ExploreDbContext>, IAsyncDisposable
    {
        private ServiceProvider _services = null!;
        internal LookupDataCache Cache { get; private set; } = null!;
        internal CancelAtOwnerRead CancelOwnerRead { get; } = new();

        internal static async Task<CatalogueDatabase> CreateAsync()
        {
            var database = new CatalogueDatabase(await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync());
            try
            {
                await using var context = database.CreateDbContext();
                await context.Database.EnsureCreatedAsync();
                context.AddRange(
                    new EventType { Id = 101, MasterCode = "ATOMIC_TYPE", FullName = "Original catalogue type" },
                    new OwnerType { Id = 101, MasterCode = "ATOMIC_OWNER", FullName = "Original catalogue owner" });
                await context.SaveChangesAsync();
                database.CancelOwnerRead.Table = context.GetService<ISqlGenerationHelper>()
                    .DelimitIdentifier(context.Model.FindEntityType(typeof(OwnerType))!.GetTableName()!);
                var services = new ServiceCollection();
                services.AddSingleton<IDbContextFactory<ExploreDbContext>>(database);
                database._services = services.BuildServiceProvider();
                database.Cache = new LookupDataCache(database._services.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<LookupDataCache>.Instance);
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public ExploreDbContext CreateDbContext() => new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().AddInterceptors(CancelOwnerRead).Options);

        internal async Task ChangeEventTypeAsync()
        {
            await using var context = CreateDbContext();
            var type = await context.EventTypes.SingleAsync(value => value.Id == 101);
            type.FullName = "Unpublished replacement type";
            await context.SaveChangesAsync();
        }

        internal async Task MakeOwnerTypesUnavailableAsync()
        {
            await using var context = CreateDbContext();
            string replacement = context.GetService<ISqlGenerationHelper>().DelimitIdentifier("unavailable_owner_types");
            await context.Database.ExecuteSqlRawAsync($"ALTER TABLE {CancelOwnerRead.Table} RENAME TO {replacement}");
        }

        public async ValueTask DisposeAsync()
        {
            Cache?.Dispose();
            if (_services is not null) await _services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class CancelAtOwnerRead : DbCommandInterceptor
    {
        internal string Table { get; set; } = string.Empty;
        internal CancellationTokenSource? Cancellation { get; set; }
        internal bool Pause { get; set; }
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            // Cancel after SQLite executes the late query, before EF consumes its rows.
            if (command.CommandText.Contains(Table, StringComparison.Ordinal))
            {
                if (Cancellation is not null) await Cancellation.CancelAsync();
                if (Pause)
                {
                    Entered.TrySetResult(true);
                    await Release.Task.WaitAsync(cancellationToken);
                }
            }
            return result;
        }
    }
}
