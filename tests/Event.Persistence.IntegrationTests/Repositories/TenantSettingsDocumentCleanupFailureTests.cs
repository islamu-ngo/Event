using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class TenantSettingsDocumentCleanupFailureTests
{
    private const string RollbackFailureKey = "TenantSettingsDocument.SavepointRollbackFailure";
    private const string ReleaseFailureKey = "TenantSettingsDocument.SavepointReleaseFailure";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tenant-document-cleanup-{Guid.CreateVersion7():N}.db");

    [After(Test)]
    public void DeleteDatabase()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath }.ConnectionString);
        SqliteConnection.ClearPool(connection);
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    [Test]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, true, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task FailedInsert_PreservesOriginalAndDetachesCandidateDespiteCleanupFailure(
        bool cancelled, bool rollbackFails, bool releaseFails)
    {
        var faults = new CleanupFaults();
        await using var context = await CreateContextAsync(faults);
        var tenant = context.Tenants.Local.Single();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var prior = TenantSettingsDocument.Create(tenant.Id, SettingsDocumentKeys.Tenant.PublicExperience, 1, "test", "{}");
        context.TenantSettingsDocuments.Add(prior);
        await context.SaveChangesAsync();
        tenant.FullName = "Caller pending change";
        using var cancellation = new CancellationTokenSource();
        Exception original = cancelled
            ? new OperationCanceledException("Insert cancelled.", cancellation.Token)
            : new DbUpdateException("Insert failed.", new SqliteException("Synthetic retryable provider failure.", 5));
        faults.Arm(original, rollbackFails, releaseFails);
        if (cancelled) faults.BeforeSave = cancellation.Cancel;
        var candidate = TenantBrandingSettingsDocumentDefaults.Create(tenant.Id, "Not persisted");
        Exception? caught = null;
        try
        {
            await new TenantSettingsDocumentRepository(context).CreateIfMissingAsync(candidate, cancellation.Token);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsSameReferenceAs(original);
        if (cancelled)
        {
            await Assert.That(((OperationCanceledException)caught!).CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        }
        else
        {
            await Assert.That(((SqliteException)caught!.InnerException!).SqliteErrorCode).IsEqualTo(5);
        }
        await AssertCleanupDiagnosticsAsync(original, faults, rollbackFails, releaseFails);
        await Assert.That(context.Entry(candidate).State).IsEqualTo(EntityState.Detached);
        await Assert.That(context.Entry(tenant).State).IsEqualTo(EntityState.Modified);
        await Assert.That(tenant.FullName).IsEqualTo("Caller pending change");
        await Assert.That(context.Entry(prior).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(faults.WinnerReads).IsEqualTo(0);
        faults.Disarm();
        var repository = new TenantSettingsDocumentRepository(context);
        await Assert.That(await repository.GetByTenantAndDocumentKey(tenant.Id, SettingsDocumentKeys.Tenant.Branding)).IsNull();
        await Assert.That(await repository.GetByTenantAndDocumentKey(tenant.Id, SettingsDocumentKeys.Tenant.PublicExperience)).IsNotNull();
        await transaction.RollbackAsync();
        await Assert.That(await repository.GetByTenantAndDocumentKey(tenant.Id, SettingsDocumentKeys.Tenant.PublicExperience)).IsNull();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Arguments(false, true)]
    public async Task ExactUniqueConflict_CannotRecoverAfterFailedRollbackOrHideFailedRelease(bool rollbackFails, bool releaseFails)
    {
        var faults = new CleanupFaults();
        await using var context = await CreateContextAsync(faults);
        Guid tenantId = context.Tenants.Local.Single().Id;
        var repository = new TenantSettingsDocumentRepository(context);
        var winner = await repository.CreateIfMissingAsync(TenantBrandingSettingsDocumentDefaults.Create(tenantId, "Unchanged winner"));
        Guid revision = winner.ConcurrencyStamp;
        string payload = winner.PayloadJson;
        context.Entry(winner).State = EntityState.Detached;
        await using var transaction = await context.Database.BeginTransactionAsync();
        // The real repository's explicit savepoint is the boundary under test, not EF's extra
        // automatic SaveChanges savepoint. The unique failure itself comes from real SQLite.
        context.Database.AutoSavepointsEnabled = false;
        faults.Arm(null, rollbackFails, releaseFails);
        var candidate = TenantBrandingSettingsDocumentDefaults.Create(tenantId, "Losing proposal");
        Exception? caught = null;
        try
        {
            await repository.CreateIfMissingAsync(candidate);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught).IsSameReferenceAs(faults.CapturedSaveFailure);
        await Assert.That(caught).IsTypeOf<DbUpdateException>();
        await Assert.That(((SqliteException)caught!.InnerException!).SqliteExtendedErrorCode).IsEqualTo(2067);
        await AssertCleanupDiagnosticsAsync(caught, faults, rollbackFails, releaseFails);
        await Assert.That(context.Entry(candidate).State).IsEqualTo(EntityState.Detached);
        await Assert.That(faults.WinnerReads).IsEqualTo(rollbackFails ? 0 : 1);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        faults.Disarm();
        var retained = (await repository.GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!;
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(revision);
        await Assert.That(retained.PayloadJson).IsEqualTo(payload);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SuccessfulInsert_ReleaseFailureRemainsAnErrorForTheTransactionOwner()
    {
        var faults = new CleanupFaults();
        await using var context = await CreateContextAsync(faults);
        Guid tenantId = context.Tenants.Local.Single().Id;
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Database.AutoSavepointsEnabled = false;
        faults.Arm(null, rollbackFails: false, releaseFails: true);
        var candidate = TenantBrandingSettingsDocumentDefaults.Create(tenantId, "Uncommitted insert");
        var failure = await Assert.ThrowsAsync<CleanupDbException>(() =>
            new TenantSettingsDocumentRepository(context).CreateIfMissingAsync(candidate));
        await Assert.That(failure).IsSameReferenceAs(faults.ReleaseFailure);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        faults.Disarm();
        await transaction.RollbackAsync();
        await Assert.That(await new TenantSettingsDocumentRepository(context)
            .GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding)).IsNull();
    }

    [Test]
    public async Task BootstrapOwner_StillRecognizesOriginalProviderConflictAndRetriesItsWholeTransaction()
    {
        var faults = new CleanupFaults();
        await using var context = await CreateContextAsync(faults);
        Guid tenantId = context.Tenants.Local.Single().Id;
        var original = new DbUpdateException("Synthetic insert conflict.", new SqliteException("Retryable provider code.", 5));
        int attempts = 0;
        var result = await new EfCoreUnitOfWork(context).ExecuteBootstrapConvergenceAsync(async token =>
        {
            attempts++;
            if (attempts == 1) faults.Arm(original, rollbackFails: true, releaseFails: true);
            else faults.Disarm();
            return await new TenantSettingsDocumentRepository(context).CreateIfMissingAsync(
                TenantBrandingSettingsDocumentDefaults.Create(tenantId, "Owner retry"), token);
        });

        await Assert.That(attempts).IsEqualTo(2);
        await AssertCleanupDiagnosticsAsync(original, faults, rollbackFails: true, releaseFails: true);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        var rows = await new TenantSettingsDocumentRepository(context).GetManyForTenant(tenantId, [SettingsDocumentKeys.Tenant.Branding]);
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].Id).IsEqualTo(result.Id);
    }

    private static async Task AssertCleanupDiagnosticsAsync(Exception original, CleanupFaults faults, bool rollbackFails, bool releaseFails)
    {
        if (rollbackFails) await Assert.That(original.Data[RollbackFailureKey]).IsSameReferenceAs(faults.RollbackFailure);
        if (releaseFails) await Assert.That(original.Data[ReleaseFailureKey]).IsSameReferenceAs(faults.ReleaseFailure);
        await Assert.That(faults.CleanupObservedCancelledToken).IsFalse();
    }

    private async Task<ExploreDbContext> CreateContextAsync(CleanupFaults faults)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Provider = PrimaryDatabaseProvider.Sqlite, Role = PrimaryDatabaseRole.Runtime, Database = _databasePath
        });
        options.UseSnakeCaseNamingConvention().AddInterceptors(new SaveFailure(faults), new TransactionFailure(faults), new ReadObservation(faults));
        var context = new ExploreDbContext(options.Options);
        try
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
            context.Tenants.Add(new Tenant
            {
                Id = Guid.CreateVersion7(), FullName = "Cleanup tenant", Slug = $"cleanup-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
            });
            await context.SaveChangesAsync();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private sealed class CleanupFaults
    {
        public bool Armed { get; private set; }
        public bool RollbackFails { get; private set; }
        public bool ReleaseFails { get; private set; }
        public Exception? SaveException { get; private set; }
        public Exception? CapturedSaveFailure { get; set; }
        public Action? BeforeSave { get; set; }
        public CleanupDbException RollbackFailure { get; } = new("Rollback savepoint unavailable.");
        public CleanupDbException ReleaseFailure { get; } = new("Release savepoint unavailable.");
        public bool CleanupObservedCancelledToken { get; set; }
        public int WinnerReads { get; set; }

        public void Arm(Exception? original, bool rollbackFails, bool releaseFails)
        {
            Armed = true;
            SaveException = original;
            RollbackFails = rollbackFails;
            ReleaseFails = releaseFails;
            WinnerReads = 0;
        }

        public void Disarm()
        {
            Armed = false;
            SaveException = null;
            BeforeSave = null;
        }
    }

    private sealed class CleanupDbException(string message) : DbException(message);

    private sealed class SaveFailure(CleanupFaults faults) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (faults.Armed && faults.SaveException is { } original)
            {
                faults.BeforeSave?.Invoke();
                faults.CapturedSaveFailure = original;
                throw original;
            }
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (faults.Armed) faults.CapturedSaveFailure = eventData.Exception;
            return Task.CompletedTask;
        }
    }

    private sealed class TransactionFailure(CleanupFaults faults) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (faults.Armed)
            {
                faults.CleanupObservedCancelledToken |= cancellationToken.IsCancellationRequested;
                if (faults.RollbackFails) throw faults.RollbackFailure;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> ReleasingSavepointAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (faults.Armed)
            {
                faults.CleanupObservedCancelledToken |= cancellationToken.IsCancellationRequested;
                if (faults.ReleaseFails) throw faults.ReleaseFailure;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReadObservation(CleanupFaults faults) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (faults.Armed && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("tenant_settings_documents", StringComparison.Ordinal)) faults.WinnerReads++;
            return ValueTask.FromResult(result);
        }
    }
}
