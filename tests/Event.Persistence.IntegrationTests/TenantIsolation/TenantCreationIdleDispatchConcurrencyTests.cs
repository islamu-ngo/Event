using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[NotInParallel]
[ClassDataSource<ManagedWorkerPostgreSqlFixture>(Shared = SharedType.PerClass)]
public sealed class TenantCreationIdleDispatchConcurrencyTests(ManagedWorkerPostgreSqlFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IdleDispatchDoesNotInvalidateTheOriginalCreatorUserFence(bool pollAfterSnapshot)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using (var installer = fixture.CreateDbContext())
            await ExploreDatabaseMigrator.ApplyGlobalOutboxAdmissionMembershipsAsync(
                installer, fixture.NativeQueueEnrollment, token);

        Guid userId = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.BindTenantLease();
            seed.Users.Add(new User
            {
                Id = userId,
                ConcurrencyStamp = Guid.CreateVersion7(),
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii
                {
                    UserId = userId,
                    Email = $"creator-{userId:N}@example.test",
                    FirstName = "Creator",
                    LastName = "Concurrency"
                }
            });
            await seed.SaveChangesAsync(token);
        }

        await using var creator = OpenGlobal();
        await using var dispatcher = OpenGlobal();
        await creator.Database.OpenConnectionAsync(token);
        await dispatcher.Database.OpenConnectionAsync(token);
        int originalBackend = ((NpgsqlConnection)creator.Database.GetDbConnection()).ProcessID;
        await Assert.That(((NpgsqlConnection)dispatcher.Database.GetDbConnection()).ProcessID)
            .IsNotEqualTo(originalBackend);

        await PollEmptyAsync(dispatcher, token);
        await using var observation = fixture.CreateDbContext();
        observation.BindTenantLease();
        DateTime originalStateUpdate = await observation.EmailDispatchProcessorStates.AsNoTracking()
            .Where(row => row.ProcessorCode == "smtp").Select(row => row.UpdatedAt).SingleAsync(token);

        if (!pollAfterSnapshot)
            await PollEmptyAsync(dispatcher, token);

        await using var originalTransaction = await creator.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, token);
        Guid originalOwner = originalTransaction.TransactionId;
        await Assert.That(await creator.Users.AsNoTracking().Where(row => row.Id == userId)
            .Select(row => row.Id).SingleAsync(token)).IsEqualTo(userId);

        if (pollAfterSnapshot)
            await PollEmptyAsync(dispatcher, token);

        int fenced = await creator.Users.Where(row => row.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Id, row => row.Id), token);

        await Assert.That(fenced).IsEqualTo(1);
        await Assert.That(creator.Database.CurrentTransaction?.TransactionId).IsEqualTo(originalOwner);
        await Assert.That(((NpgsqlConnection)creator.Database.GetDbConnection()).ProcessID)
            .IsEqualTo(originalBackend);
        await originalTransaction.CommitAsync(token);
        await Assert.That(await observation.EmailDispatchProcessorStates.AsNoTracking()
            .Where(row => row.ProcessorCode == "smtp").Select(row => row.UpdatedAt).SingleAsync(token))
            .IsEqualTo(originalStateUpdate);
    }

    [Test]
    public async Task EmptyPollPersistsARealLowWatermarkDeferralTransition()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using (var installer = fixture.CreateDbContext())
            await ExploreDatabaseMigrator.ApplyGlobalOutboxAdmissionMembershipsAsync(
                installer, fixture.NativeQueueEnrollment, token);
        await using var dispatcher = OpenGlobal();
        await PollEmptyAsync(dispatcher, token);
        DateTime previousUpdate = DateTime.UtcNow.AddMinutes(-1);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.BindTenantLease();
            await seed.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == "smtp")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.OptionalRemindersDeferred, true)
                    .SetProperty(row => row.UpdatedAt, previousUpdate), token);
        }

        await PollEmptyAsync(dispatcher, token);

        await using var observation = fixture.CreateDbContext();
        observation.BindTenantLease();
        var state = await observation.EmailDispatchProcessorStates.AsNoTracking()
            .SingleAsync(row => row.ProcessorCode == "smtp", token);
        await Assert.That(state.OptionalRemindersDeferred).IsFalse();
        await Assert.That(state.UpdatedAt).IsGreaterThan(previousUpdate);
    }

    private static async Task PollEmptyAsync(ExploreDbContext dispatcher, CancellationToken token)
    {
        var claimed = await PostgresEmailDispatchAdmissionContract.ClaimAsync(
            dispatcher, new EmailDispatchBatchClaimRequest(
                Guid.CreateVersion7(), 1, 1, 8, 2, 1000, 500, DateTime.UtcNow),
            null, null, token);
        await Assert.That(claimed).IsEmpty();
    }

    private ExploreDbContext OpenGlobal()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        options.UseNpgsql(provider => provider.ExecutionStrategy(
            dependencies => new NonRetryingExecutionStrategy(dependencies)));
        var context = new ExploreDbContext(options.Options) { TenantContext = null };
        context.BindTenantLease();
        return context;
    }
}
