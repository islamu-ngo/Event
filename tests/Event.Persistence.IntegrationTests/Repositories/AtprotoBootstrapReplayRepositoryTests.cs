using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence.Repositories;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoBootstrapReplayRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ConcurrentConsumptionAcrossContextsHasExactlyOneWinner()
    {
        await fixture.ResetAsync();
        var tenantId = Guid.NewGuid();
        var jti = Guid.NewGuid().ToString("D");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(2);

        var attempts = Enumerable.Range(0, 24).Select(async _ =>
        {
            await using var context = CreateRuntimeContext(tenantId);
            return await new AtprotoBootstrapReplayRepository(context, enrollment: fixture.NativeQueueEnrollment)
                .TryConsumeAsync(jti, tenantId, expiresAt);
        });
        var results = await Task.WhenAll(attempts);

        await Assert.That(results.Count(consumed => consumed)).IsEqualTo(1);
    }

    [Test]
    public async Task ReplayKeyIsTenantScopedAndExpiredInputIsRejected()
    {
        await fixture.ResetAsync();
        var jti = Guid.NewGuid().ToString("D");
        Guid firstTenantId = Guid.CreateVersion7(), secondTenantId = Guid.CreateVersion7();
        await using var firstContext = CreateRuntimeContext(firstTenantId);
        await using var secondContext = CreateRuntimeContext(secondTenantId);
        var first = new AtprotoBootstrapReplayRepository(firstContext, enrollment: fixture.NativeQueueEnrollment);
        var second = new AtprotoBootstrapReplayRepository(secondContext, enrollment: fixture.NativeQueueEnrollment);

        var firstTenant = await first.TryConsumeAsync(jti, firstTenantId, DateTimeOffset.UtcNow.AddMinutes(2));
        var secondTenant = await second.TryConsumeAsync(jti, secondTenantId, DateTimeOffset.UtcNow.AddMinutes(2));
        var expired = await second.TryConsumeAsync(
            Guid.NewGuid().ToString("D"),
            secondTenantId,
            DateTimeOffset.UtcNow.AddSeconds(-1));

        await Assert.That(firstTenant).IsTrue();
        await Assert.That(secondTenant).IsTrue();
        await Assert.That(expired).IsFalse();
    }

    private ExploreDbContext CreateRuntimeContext(Guid tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        var context = new ExploreDbContext(builder.Options) { TenantContext = new SelectedTenant(tenant) };
        context.BindTenantLease();
        return context;
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
