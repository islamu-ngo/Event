using Explore.Domain;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Event.Testing;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.UnitOfWork;

public sealed class HumanCommitFinalAdmissionTests
{
    [Test]
    [Arguments(false, "prepared")]
    [Arguments(true, "prepared")]
    [Arguments(false, "serializable")]
    [Arguments(false, "bootstrap")]
    public async Task TransactionDisposalCannotEraseTheOriginalCommitOutcome(bool loseAcknowledgement, string ownerKind)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var fault = new TransactionCleanupFault(loseAcknowledgement,
            new SqliteException("Transaction cleanup failed after real disposal.", 5));
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention()
            .ReplaceService<IRelationalTransactionFactory, TransactionCleanupFault.Factory>()
            .AddInterceptors(fault).Options);
        await CreateDatabaseAsync(context);
        fault.Arm(context);
        string key = $"commit-{Guid.CreateVersion7():N}";
        int attempts = 0;
        Exception? failure = null;
        try
        {
            async Task<bool> WriteAsync(CancellationToken token)
            {
                attempts++;
                context.Set<SettingValueTypeLookup>().Add(new SettingValueTypeLookup
                {
                    Id = 900_001,
                    MasterCode = key,
                    FullName = "Committed transaction evidence"
                });
                await context.SaveChangesAsync(token);
                return true;
            }
            var owner = new EfCoreUnitOfWork(context);
            _ = await (ownerKind switch
            {
                "prepared" => ((ISerializableAttemptOwner)owner).ExecuteAsync(WriteAsync, default),
                "serializable" => owner.ExecuteSerializableAsync(WriteAsync),
                "bootstrap" => owner.ExecuteBootstrapConvergenceAsync(WriteAsync),
                _ => throw new InvalidOperationException("Unknown test owner.")
            });
        }
        catch (Exception observed) { failure = observed; }
        await Assert.That(fault.PhysicalCommitObserved).IsTrue();
        await using var observer = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        await Assert.That(await observer.Set<SettingValueTypeLookup>().AsNoTracking()
            .Where(row => row.Id == 900_001).Select(row => row.MasterCode).SingleAsync()).IsEqualTo(key);
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure is HumanMutationOutcomeUnknownException).IsEqualTo(loseAcknowledgement);
        await Assert.That(failure is TransactionCommittedCleanupException).IsEqualTo(!loseAcknowledgement);
        await Assert.That(HasCause(failure!, fault.CleanupFailure)).IsTrue();
        await Assert.That(HasCause(failure!, fault.CommitFailure)).IsEqualTo(loseAcknowledgement);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(fault.DisposalFailures).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task CreateDatabaseAsync(ExploreDbContext context)
    {
        await context.Database.EnsureCreatedAsync();
        context.Set<SettingValueTypeLookup>().Add(new SettingValueTypeLookup
        {
            Id = (int)SettingValueType.String,
            MasterCode = "string",
            FullName = "String"
        });
        await context.SaveChangesAsync();
    }

    private static bool HasCause(Exception failure, Exception expected) =>
        ReferenceEquals(failure, expected)
        || (failure is AggregateException aggregate
            ? aggregate.InnerExceptions.Any(inner => HasCause(inner, expected))
            : failure.InnerException is { } inner && HasCause(inner, expected));

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostPhysicalCommitAcknowledgementCannotAuthorizeRollbackReplayOrFailureHistory(bool conflictShapedException)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new LostCommitAcknowledgement(conflictShapedException);
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().AddInterceptors(interceptor).Options);
        await CreateDatabaseAsync(context);
        ISerializableAttemptOwner owner = new EfCoreUnitOfWork(context);
        string key = $"human-lost-ack-{Guid.NewGuid():N}";
        int attempts = 0;
        interceptor.Armed = true;
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => owner.ExecuteAsync(async token =>
        {
            attempts++;
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "committed" });
            await context.SaveChangesAsync(token);
            return true;
        }));
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await using var observer = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key)).Value)
            .IsEqualTo("committed");
    }

    private sealed class LostCommitAcknowledgement(bool conflictShapedException) : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return Task.CompletedTask;
            Armed = false;
            // The real provider has already committed. Even a busy-shaped signal here
            // cannot establish that the original transaction rolled back.
            throw conflictShapedException
                ? new SqliteException("Lost commit acknowledgement.", 5)
                : new InvalidOperationException("Lost commit acknowledgement.");
        }
    }

    [Test]
    public async Task SingleAttemptConflictReportsAcknowledgedRollbackWithoutReplayingCallback()
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"human-attempt-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        await CreateDatabaseAsync(context);
        await using var other = new SqliteConnection(connectionString);
        await other.OpenAsync();
        await using var reader = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(other).UseSnakeCaseNamingConvention().Options);
        await using var readTransaction = other.BeginTransaction(System.Data.IsolationLevel.Serializable, deferred: true);
        await reader.Database.UseTransactionAsync(readTransaction);
        await reader.SystemSettings.AsNoTracking().CountAsync();
        ISerializableAttemptOwner owner = new EfCoreUnitOfWork(context);
        int attempts = 0;
        string key = $"human-conflict-{Guid.NewGuid():N}";
        var conflict = await Assert.ThrowsAsync<HumanMutationAttemptRolledBackException>(() => owner.ExecuteAsync(async token =>
        {
            attempts++;
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "rejected" });
            await context.SaveChangesAsync(token);
            return true;
        }));
        await Assert.That(conflict!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await readTransaction.RollbackAsync();
        await reader.Database.UseTransactionAsync(null);
        await owner.ExecuteAsync(async token =>
        {
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "fresh" });
            await context.SaveChangesAsync(token);
            return true;
        });
        await Assert.That((await context.SystemSettings.AsNoTracking().SingleAsync(setting => setting.SettingKey == key)).Value)
            .IsEqualTo("fresh");
    }

    [Test]
    public async Task FinalAuthorityLossRollsBackPersistedEffectsAndDoesNotLeakIntoNextOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        await CreateDatabaseAsync(context);
        ISerializableAttemptOwner unitOfWork = new EfCoreUnitOfWork(context);
        var key = $"human-final-{Guid.NewGuid():N}";
        var attempts = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unitOfWork.ExecuteAsync(async token =>
        {
            attempts++;
            context.RegisterHumanCommitAdmission(_ => Task.CompletedTask,
                () => throw new UnauthorizedAccessException("Authority expired."));
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "uncommitted" });
            await context.SaveChangesAsync(token);
            return true;
        }));
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(await context.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key)).IsFalse();
        await unitOfWork.ExecuteAsync(async token =>
        {
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "committed" });
            await context.SaveChangesAsync(token);
            return true;
        });
        await Assert.That((await context.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key)).Value)
            .IsEqualTo("committed");
    }

    [Test]
    public async Task EveryBlockingSourceCheckPrecedesAnyFinalLifetimeCheck()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        await CreateDatabaseAsync(context);
        ISerializableAttemptOwner unitOfWork = new EfCoreUnitOfWork(context);
        var sourcesValidated = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unitOfWork.ExecuteAsync(token =>
        {
            context.RegisterHumanCommitAdmission(_ => Task.CompletedTask, () =>
            {
                if (!sourcesValidated)
                    throw new InvalidOperationException("Lifetime was sampled before all blocking checks.");
                throw new UnauthorizedAccessException("Authority expired during final source admission.");
            });
            context.RegisterHumanCommitAdmission(_ =>
            {
                sourcesValidated = true;
                return Task.CompletedTask;
            }, () => { });
            return Task.FromResult(true);
        }));
        await Assert.That(sourcesValidated).IsTrue();
    }
}
