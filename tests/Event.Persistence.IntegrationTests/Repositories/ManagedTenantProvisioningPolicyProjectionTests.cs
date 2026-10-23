using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class ManagedTenantProvisioningPolicyProjectionTests
{
    private const string Key = GovernanceSettingKeys.Tenants.WhiteLabelingEnabled;

    [Test]
    public async Task TrackedSourceAndProjectionCommitAndRollBackOnTheSameOwner()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Context())
        {
            context.SystemSettings.Add(Source("true", true));
            await Assert.That(await context.SaveChangesAsync()).IsEqualTo(1);
        }
        await using (var context = database.Context())
        {
            var before = await context.Set<ManagedTenantProvisioningPolicySetting>().SingleAsync(row => row.SettingKey == Key);
            await Assert.That(before.Value).IsEqualTo("true");
            await Assert.That(before.IsLocked).IsTrue();
            long revision = before.Revision;
            await using var transaction = await context.Database.BeginTransactionAsync();
            var setting = await context.SystemSettings.SingleAsync(row => row.SettingKey == Key);
            setting.Value = "false";
            setting.IsLocked = false;
            await context.SaveChangesAsync();
            var changed = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
                .SingleAsync(row => row.SettingKey == Key);
            await Assert.That(changed.Value).IsEqualTo("false");
            await Assert.That(changed.Revision).IsEqualTo(revision + 1);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        await using var observed = database.Context();
        await Assert.That((await observed.SystemSettings.SingleAsync(row => row.SettingKey == Key)).Value).IsEqualTo("true");
        await Assert.That((await observed.Set<ManagedTenantProvisioningPolicySetting>()
            .SingleAsync(row => row.SettingKey == Key)).Value).IsEqualTo("true");
    }

    [Test]
    public async Task MissingProjectionRejectsSourceWriteWithoutCreatingPermissiveCoverage()
    {
        await using var database = await Database.CreateAsync();
        await using (var attack = database.Context())
            await attack.Set<ManagedTenantProvisioningPolicySetting>().Where(row => row.SettingKey == Key).ExecuteDeleteAsync();
        await using (var context = database.Context())
        {
            context.SystemSettings.Add(Source("true", true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
        await using var observed = database.Context();
        await Assert.That(await observed.SystemSettings.AnyAsync(row => row.SettingKey == Key)).IsFalse();
        await Assert.That(await observed.Set<ManagedTenantProvisioningPolicySetting>().AnyAsync(row => row.SettingKey == Key)).IsFalse();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ManagedTenantProvisioningPolicyProjection.InitializeAsync(observed, CancellationToken.None));
    }

    [Test]
    public async Task NativeProducerDeltaCannotEscapeItsOriginalTransaction()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Context())
        {
            context.SystemSettings.Add(Source("true", true));
            await context.SaveChangesAsync();
        }
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            // The same EF bulk path used by original scalar producers bypasses tracking.
            await context.SystemSettings.Where(row => row.SettingKey == Key).ExecuteDeleteAsync();
            await ManagedTenantProvisioningPolicyProjection.RecordAsync(context, Key, null, CancellationToken.None);
            var tombstone = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
                .SingleAsync(row => row.SettingKey == Key);
            await Assert.That(tombstone.IsPresent).IsFalse();
            await Assert.That(tombstone.Value).IsNull();
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        await using var observed = database.Context();
        await Assert.That(await observed.SystemSettings.AnyAsync(row => row.SettingKey == Key)).IsTrue();
        await Assert.That((await observed.Set<ManagedTenantProvisioningPolicySetting>()
            .SingleAsync(row => row.SettingKey == Key)).IsPresent).IsTrue();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ManagedTenantProvisioningPolicyProjection.RecordAsync(observed, Key, null, CancellationToken.None));
    }

    [Test]
    public async Task MissingCoverageRejectsAllTrackedSourcesBeforeWritingOnTheCallerTransaction()
    {
        await using var database = await Database.CreateAsync();
        await using (var attack = database.Context())
            await attack.Set<ManagedTenantProvisioningPolicySetting>().Where(row => row.SettingKey == Key).ExecuteDeleteAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync();
        const string earlierKey = GovernanceSettingKeys.Domains.AllowTenantCustomDomain;
        var earlier = Source("true", true);
        earlier.SettingKey = earlierKey;
        var later = Source("false", true);
        context.SystemSettings.AddRange(earlier, later);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(context.Entry(earlier).State).IsEqualTo(EntityState.Added);
        await Assert.That(context.Entry(later).State).IsEqualTo(EntityState.Added);
        await Assert.That(await context.SystemSettings.AsNoTracking()
            .AnyAsync(row => row.SettingKey == earlierKey || row.SettingKey == Key)).IsFalse();
        var retained = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .SingleAsync(row => row.SettingKey == earlierKey);
        await Assert.That(retained.IsPresent).IsFalse();
        await Assert.That(retained.Value).IsNull();
        await Assert.That(retained.Revision).IsEqualTo(0);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SecondProjectionWriteFailureRollsBackSourcesAndFirstProjectionOnTheOriginalTransaction()
    {
        await using var database = await Database.CreateAsync();
        var failure = new FailSecondProjectionUpdate();
        await using var context = database.Context(failure);
        failure.Target(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        const string earlierKey = GovernanceSettingKeys.Domains.AllowTenantCustomDomain;
        var earlier = Source("true", true);
        earlier.SettingKey = earlierKey;
        var later = Source("false", true);
        context.SystemSettings.AddRange(earlier, later);

        await Assert.ThrowsAsync<ProjectionWriteFailure>(() => context.SaveChangesAsync());

        await Assert.That(failure.Attempts).IsEqualTo(2);
        await Assert.That(failure.CompletedRows).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(context.Entry(earlier).State).IsEqualTo(EntityState.Added);
        await Assert.That(context.Entry(later).State).IsEqualTo(EntityState.Added);
        await Assert.That(await context.SystemSettings.AsNoTracking()
            .AnyAsync(row => row.SettingKey == earlierKey || row.SettingKey == Key)).IsFalse();
        var retained = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .Where(row => row.SettingKey == earlierKey || row.SettingKey == Key).ToArrayAsync();
        await Assert.That(retained.Length).IsEqualTo(2);
        foreach (var row in retained)
        {
            await Assert.That(row.IsPresent).IsFalse();
            await Assert.That(row.IsLocked).IsFalse();
            await Assert.That(row.Value).IsNull();
            await Assert.That(row.Revision).IsEqualTo(0);
        }
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task TrackedProjectionForgeryCannotJoinAnOtherwiseValidSourceWrite()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Context())
        {
            var image = await context.Set<ManagedTenantProvisioningPolicySetting>()
                .SingleAsync(row => row.SettingKey == Key);
            image.Project(Source("false", false));
            context.SystemSettings.Add(Source("true", true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
        await using var observed = database.Context();
        await Assert.That(await observed.SystemSettings.AnyAsync(row => row.SettingKey == Key)).IsFalse();
        var retained = await observed.Set<ManagedTenantProvisioningPolicySetting>().SingleAsync(row => row.SettingKey == Key);
        await Assert.That(retained.IsPresent).IsFalse();
        await Assert.That(retained.Revision).IsEqualTo(0);
    }

    [Test]
    public async Task UnboundNullTenantCannotUpdateProjectionWhileBoundNullOwnerCan()
    {
        await using var database = await Database.CreateAsync();
        await using (var control = database.Context())
        {
            await using var transaction = await control.Database.BeginTransactionAsync();
            await ManagedTenantProvisioningPolicyProjection.RecordAsync(control, Key, Source("true", true), CancellationToken.None);
            var changed = await control.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
                .SingleAsync(row => row.SettingKey == Key);
            await Assert.That(changed.IsPresent).IsTrue();
            await Assert.That(changed.Value).IsEqualTo("true");
            await Assert.That(changed.Revision).IsEqualTo(1);
            await transaction.RollbackAsync();
        }

        await using var unbound = database.UnboundContext();
        await using var originalTransaction = await unbound.Database.BeginTransactionAsync();
        await Assert.That(unbound.TenantFilterTenantId).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ManagedTenantProvisioningPolicyProjection.RecordAsync(unbound, Key, Source("true", true), CancellationToken.None));
        await Assert.That(unbound.Database.CurrentTransaction).IsSameReferenceAs(originalTransaction);
        var retained = await unbound.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .SingleAsync(row => row.SettingKey == Key);
        await Assert.That(retained.IsPresent).IsFalse();
        await Assert.That(retained.IsLocked).IsFalse();
        await Assert.That(retained.Value).IsNull();
        await Assert.That(retained.Revision).IsEqualTo(0);
        await originalTransaction.RollbackAsync();
    }

    [Test]
    public async Task StaleTrackedMetadataWriteCannotUndoAnotherOwnersPolicyLock()
    {
        await using var database = await Database.CreateAsync();
        await using (var seed = database.Context())
        {
            seed.SystemSettings.Add(Source("true", false));
            await seed.SaveChangesAsync();
        }
        await using var staleOwner = database.Context();
        var stale = await staleOwner.SystemSettings.SingleAsync(row => row.SettingKey == Key);
        await Assert.That(stale.IsLocked).IsFalse();
        long lockedRevision;
        await using (var lockingOwner = database.Context())
        {
            var source = await lockingOwner.SystemSettings.SingleAsync(row => row.SettingKey == Key);
            source.IsLocked = true;
            await lockingOwner.SaveChangesAsync();
            var locked = await lockingOwner.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
                .SingleAsync(row => row.SettingKey == Key);
            await Assert.That(locked.IsLocked).IsTrue();
            lockedRevision = locked.Revision;
        }

        stale.Description = "Unrelated metadata update from the earlier source image.";
        await Assert.ThrowsAsync<InvalidOperationException>(() => staleOwner.SaveChangesAsync());

        await using var observed = database.Context();
        var retainedSource = await observed.SystemSettings.SingleAsync(row => row.SettingKey == Key);
        var retainedImage = await observed.Set<ManagedTenantProvisioningPolicySetting>()
            .SingleAsync(row => row.SettingKey == Key);
        await Assert.That(retainedSource.IsLocked).IsTrue();
        await Assert.That(retainedSource.Description).IsNull();
        await Assert.That(retainedImage.IsLocked).IsTrue();
        await Assert.That(retainedImage.Value).IsEqualTo("true");
        await Assert.That(retainedImage.Revision).IsEqualTo(lockedRevision);
    }

    private static SystemSetting Source(string value, bool locked) => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = Key,
        Value = value,
        ValueType = SettingValueType.Boolean,
        IsLocked = locked
    };

    private sealed class ProjectionWriteFailure : Exception { }

    private sealed class FailSecondProjectionUpdate : DbCommandInterceptor
    {
        private string? updatePrefix;
        internal int Attempts { get; private set; }
        internal int CompletedRows { get; private set; }

        internal void Target(ExploreDbContext context)
        {
            var entity = context.Model.FindEntityType(typeof(ManagedTenantProvisioningPolicySetting))!;
            updatePrefix = "UPDATE " + context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema()) + " ";
        }

        private bool IsProjectionUpdate(DbCommand command, CommandEventData eventData) =>
            eventData.CommandSource == CommandSource.ExecuteUpdate
            && updatePrefix is not null
            && command.CommandText.TrimStart().StartsWith(updatePrefix, StringComparison.Ordinal);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsProjectionUpdate(command, eventData) && ++Attempts == 2)
                throw new ProjectionWriteFailure();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (IsProjectionUpdate(command, eventData))
                CompletedRows += result;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Database(SqliteConnection connection) : IAsyncDisposable
    {
        internal ExploreDbContext Context(params IInterceptor[] interceptors)
        {
            var context = UnboundContext(interceptors);
            context.BindTenantLease();
            return context;
        }

        internal ExploreDbContext UnboundContext(params IInterceptor[] interceptors) => new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().AddInterceptors(interceptors).Options);

        internal static async Task<Database> CreateAsync()
        {
            var database = new Database(await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync());
            try
            {
                await using var context = database.Context();
                await context.Database.EnsureCreatedAsync();
                context.Add(new SettingValueTypeLookup
                {
                    Id = (int)SettingValueType.Boolean,
                    MasterCode = "boolean",
                    FullName = "Boolean"
                });
                await context.SaveChangesAsync();
                await ManagedTenantProvisioningPolicyProjection.InitializeAsync(context, CancellationToken.None);
                return database;
            }
            catch { await database.DisposeAsync(); throw; }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
