using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerTestSession)]
public sealed class PostgresNamedLockTenantLeaseTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SessionLockInitializesOriginalTenantBeforeProtectedWork()
    {
        Guid tenantId = Guid.CreateVersion7();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        await using var context = new ExploreDbContext(options.Options)
        {
            TenantContext = new SelectedTenant(tenantId)
        };
        var connection = context.Database.GetDbConnection();

        await using (await RelationalNamedLock.AcquireSessionAsync(
            context, $"tenant-session-{Guid.CreateVersion7():N}", CancellationToken.None))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(tenantId.ToString());
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(tenantId);
        }

        await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
