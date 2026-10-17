using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanOrderedSettingOwnerTests
{
    [Test]
    public async Task CanonicalHumanOrderedLocksDoNotWrapTheOriginalOwnerInAReplayingStrategy()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var subject = selected.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        await using var context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite(fixture.Context.Database.GetDbConnection())
            .ReplaceService<IExecutionStrategyFactory, ConfiguredRetryFactory>().Options);
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, context.TenantFilterTenantId);
        lease.BindHuman(subject, HumanAdministratorCommitPurpose.GlobalInstance, null, fixture.Lifetime);
        var unit = new EfCoreUnitOfWork(context);
        var locks = new RelationalSettingMutationLock(context, unit);
        string key = $"human-lock-owner-{Guid.NewGuid():N}";
        int callbacks = 0;
        await Assert.That(context.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
        await Assert.That(await locks.ExecuteOrderedGroupsAsync([[key]], token =>
            ((ISerializableAttemptOwner)unit).ExecuteAsync(async inner =>
            {
                callbacks++;
                context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "single-attempt" });
                await context.SaveChangesAsync(inner);
                return true;
            }, token))).IsTrue();
        await Assert.That(callbacks).IsEqualTo(1);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking()
            .AnyAsync(row => row.SettingKey == key && row.Value == "single-attempt")).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    public sealed class ConfiguredRetryFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new ConfiguredRetry(dependencies);
    }

    private sealed class ConfiguredRetry(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is SqliteException { SqliteErrorCode: 5 };
    }
}
