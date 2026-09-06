// ABOUTME: Proves admission order fences contend on every supported external relational engine.
// ABOUTME: Executes production-mapped schema and prefix SQL against real SQL Server, MariaDB, and MySQL.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AdmissionAuthorityProviderDb")]
public sealed class AdmissionAuthorityRowFenceProviderTests(
    AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task OrderFenceRejectsConcurrentNowaitWriterAndReleasesAfterCommit(
        PrimaryDatabaseProvider provider)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await using ExploreDbContext setup = CreateContext(provider);
        await PrepareMinimalOrderTableAsync(setup, provider, tenantId, orderId);

        await using ExploreDbContext first = CreateContext(provider);
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await RelationalEntityRowFence.AcquireAsync<RegistrationOrder>(
            first,
            tenantId,
            order => order.Id,
            orderId,
            CancellationToken.None);

        await using ExploreDbContext contender = CreateContext(provider);
        await using var contenderTransaction =
            await contender.Database.BeginTransactionAsync();
        Exception? contention = await CaptureAsync(() =>
            contender.Database.ExecuteSqlRawAsync(
                BuildNowaitCommand(provider),
                [tenantId, orderId],
                CancellationToken.None));

        await Assert.That(contention).IsNotNull();
        await Assert.That(contention!.GetBaseException()).IsAssignableTo<DbException>();
        await contenderTransaction.RollbackAsync();
        await firstTransaction.CommitAsync();

        await using ExploreDbContext released = CreateContext(provider);
        await using var releasedTransaction = await released.Database.BeginTransactionAsync();
        await RelationalEntityRowFence.AcquireAsync<RegistrationOrder>(
            released,
            tenantId,
            order => order.Id,
            orderId,
            CancellationToken.None);
        await releasedTransaction.CommitAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task NamedLockLifecycleCoversContentionCommitRollbackCancellationAndDisposal(
        PrimaryDatabaseProvider provider)
    {
        string resource = $"named-lock-lifecycle:{provider}:{Guid.CreateVersion7():N}";
        await using ExploreDbContext owner = CreateContext(provider);
        await using var ownerTransaction = await owner.Database.BeginTransactionAsync();
        await using IAsyncDisposable transactionLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                owner,
                resource,
                CancellationToken.None);

        await using (ExploreDbContext contender = CreateContext(provider))
        await using (var contenderTransaction = await contender.Database.BeginTransactionAsync())
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            Exception? blocked = await CaptureAsync(async () =>
            {
                await using IAsyncDisposable lease =
                    await RelationalNamedLock.AcquireTransactionAsync(
                        contender,
                        resource,
                        cancellation.Token);
                return 0;
            });
            await Assert.That(blocked).IsNotNull();
        }

        await ownerTransaction.CommitAsync();

        await using (ExploreDbContext rollbackOwner = CreateContext(provider))
        await using (var rollbackTransaction = await rollbackOwner.Database.BeginTransactionAsync())
        {
            await using IAsyncDisposable lease =
                await RelationalNamedLock.AcquireTransactionAsync(
                    rollbackOwner,
                    resource,
                    CancellationToken.None);
            await rollbackTransaction.RollbackAsync();
        }

        await using ExploreDbContext sessionOwner = CreateContext(provider);
        IAsyncDisposable sessionLease = await RelationalNamedLock.AcquireSessionAsync(
            sessionOwner,
            resource,
            CancellationToken.None);
        await sessionLease.DisposeAsync();

        await using ExploreDbContext verifier = CreateContext(provider);
        await using var verificationTransaction = await verifier.Database.BeginTransactionAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncDisposable verificationLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                verifier,
                resource,
                timeout.Token);
        await verificationTransaction.CommitAsync(timeout.Token);
    }

    private ExploreDbContext CreateContext(PrimaryDatabaseProvider provider)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options,
            fixture.CreateOptions(provider));
        return new ExploreDbContext(options.Options);
    }

    private static async Task PrepareMinimalOrderTableAsync(
        ExploreDbContext context,
        PrimaryDatabaseProvider provider,
        Guid tenantId,
        Guid orderId)
    {
        string prepare = provider == PrimaryDatabaseProvider.SqlServer
            ? """
              IF SCHEMA_ID(N'islamu_event') IS NULL EXEC(N'CREATE SCHEMA [islamu_event]');
              IF OBJECT_ID(N'islamu_event.registration_orders', N'U') IS NULL
              CREATE TABLE [islamu_event].[registration_orders] (
                  [tenant_id] uniqueidentifier NOT NULL,
                  [id] uniqueidentifier NOT NULL,
                  CONSTRAINT [PK_admission_authority_orders] PRIMARY KEY ([tenant_id], [id])
              );
              DELETE FROM [islamu_event].[registration_orders];
              """
            : """
              CREATE TABLE IF NOT EXISTS `ie_registration_orders` (
                  `tenant_id` char(36) NOT NULL,
                  `id` char(36) NOT NULL,
                  PRIMARY KEY (`tenant_id`, `id`)
              );
              DELETE FROM `ie_registration_orders`;
              """;
        await context.Database.ExecuteSqlRawAsync(prepare);
        string insert = provider == PrimaryDatabaseProvider.SqlServer
            ? "INSERT INTO [islamu_event].[registration_orders] ([tenant_id], [id]) VALUES ({0}, {1})"
            : "INSERT INTO `ie_registration_orders` (`tenant_id`, `id`) VALUES ({0}, {1})";
        await context.Database.ExecuteSqlRawAsync(insert, tenantId, orderId);
    }

    private static string BuildNowaitCommand(PrimaryDatabaseProvider provider)
    {
        return provider == PrimaryDatabaseProvider.SqlServer
            ? """
              SELECT [id] FROM [islamu_event].[registration_orders]
              WITH (UPDLOCK, HOLDLOCK, NOWAIT)
              WHERE [tenant_id] = {0} AND [id] = {1}
              """
            : """
              SELECT `id` FROM `ie_registration_orders`
              WHERE `tenant_id` = {0} AND `id` = {1}
              FOR UPDATE NOWAIT
              """;
    }

    private static async Task<Exception?> CaptureAsync(Func<Task<int>> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
