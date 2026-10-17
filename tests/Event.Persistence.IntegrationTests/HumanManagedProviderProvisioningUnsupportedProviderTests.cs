using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed class HumanManagedProviderProvisioningUnsupportedProviderTests
{
    [Test]
    public async Task UnsupportedSqlServerIsRejectedBeforeConnectionOrOwnerResolution()
    {
        // Provider selection only: no connection string, database or synthetic
        // human capability exists for this deliberately unsupported construction.
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>().UseSqlServer().Options);
        await Assert.ThrowsAsync<NotSupportedException>(() => Task.FromResult(new HumanManagedProviderProvisioningDataSource(
            context, new ExploreDbContextExecutionLease(), TimeProvider.System, null)));
    }
}
