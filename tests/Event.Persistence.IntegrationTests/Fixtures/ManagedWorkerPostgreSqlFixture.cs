using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Installs the complete worker package once; each test still checks current database authority.</summary>
public sealed class ManagedWorkerPostgreSqlFixture : PostgreSqlContainerFixture, IAsyncInitializer
{
    async Task IAsyncInitializer.InitializeAsync()
    {
        await base.InitializeAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        options.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        await using var migrator = new ExploreDbContext(options.Options);
        await PostgresTenantCreationPermissions.ApplyAsync(
            migrator, NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, NativeQueueEnrollment);
        await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
            migrator, NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
    }
}
