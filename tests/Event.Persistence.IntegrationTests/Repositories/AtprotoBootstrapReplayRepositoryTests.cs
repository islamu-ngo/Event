using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence.Repositories;

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
            await using var context = fixture.CreateDbContext();
            return await new AtprotoBootstrapReplayRepository(context)
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
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var first = new AtprotoBootstrapReplayRepository(firstContext);
        var second = new AtprotoBootstrapReplayRepository(secondContext);

        var firstTenant = await first.TryConsumeAsync(jti, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));
        var secondTenant = await second.TryConsumeAsync(jti, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));
        var expired = await second.TryConsumeAsync(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddSeconds(-1));

        await Assert.That(firstTenant).IsTrue();
        await Assert.That(secondTenant).IsTrue();
        await Assert.That(expired).IsFalse();
    }
}
