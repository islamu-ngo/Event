using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Onboarding;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.None)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresBootstrapStartupStorageTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EnrolledGlobalOwnerConvergesInteractiveAndConfiguredBootstrapWithoutDeletion()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var migrationOptions = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(migrationOptions,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var database = await factory.CreateDbContextAsync(token);
        await using var gate = new EventDiscoveryEnrollmentGate(database);
        var repository = new InstanceBootstrapStateRepository(database);
        var transactions = new EfCoreUnitOfWork(database);

        Task PrepareAsync(IConfiguration configuration) =>
            new ConfiguredAdministratorBootstrapStartupRunner(
                new ConfiguredAdministratorBootstrapProvider(configuration,
                    Options.Create(new InstanceOperatorIdentityOptions()), repository),
                repository, transactions, TimeProvider.System, gate).PrepareAsync(token);

        var interactive = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = "Interactive",
            ["Deployment:Mode"] = "SingleTenant"
        }).Build();
        await PrepareAsync(interactive);
        await Assert.That(await repository.GetCurrent(token)).IsNull();

        IConfiguration configured = ConfiguredAdministratorBootstrapStartupConcurrencyTests.CreateConfiguration();
        await PrepareAsync(configured);
        var first = await repository.GetCurrent(token);
        await Assert.That(first!.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(first.Generation).IsEqualTo(1L);
        await PrepareAsync(configured);
        await Assert.That((await repository.GetCurrent(token))!.Id).IsEqualTo(first.Id);

        var replacement = new ConfigurationBuilder().AddConfiguration(configured)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "2"
            }).Build();
        await PrepareAsync(replacement);
        var current = await repository.GetCurrent(token);
        await Assert.That(current!.Generation).IsEqualTo(2L);
        await Assert.That(current.Id).IsNotEqualTo(first.Id);
        var prior = await database.InstanceBootstrapStates.AsNoTracking()
            .SingleAsync(state => state.Id == first.Id, token);
        await Assert.That(prior.Status).IsEqualTo(InstanceBootstrapStatus.Superseded);
        await Assert.That(await database.InstanceBootstrapStates.CountAsync(token)).IsEqualTo(2);

        await database.Database.OpenConnectionAsync(token);
        await PostgresOperatorIdentityEditorContract.RequireReadyAsync(
            database, fixture.NativeQueueEnrollment, token);
        await using (var migrator = new ExploreDbContext(migrationOptions.Options))
        {
            migrator.BindTenantLease();
            await migrator.Database.OpenConnectionAsync(token);
            await ManagedTenantWorkerCreationPermissionsNativeTests.EnsurePolicyProjectionAsync(migrator);
            await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
            await PostgresTenantCreationPermissions.ApplyAsync(
                migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            database, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(
            database, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        await PostgresOperatorIdentityEditorContract.RequireReadyAsync(
            database, fixture.NativeQueueEnrollment, token);

        var denial = await Assert.ThrowsAsync<PostgresException>(() =>
            database.InstanceBootstrapStates.Where(state => state.Id == first.Id).ExecuteDeleteAsync(token));
        await Assert.That(denial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }
}
