using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class InstanceBootstrapStateReaderTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GlobalReaderReturnsLatestDetachedBootstrapWithoutRuntimeTableAccess()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var generation = (await migration.InstanceBootstrapStates
            .Select(row => (long?)row.Generation).MaxAsync() ?? 0) + 1;
        var evidence = InstanceBootstrapState.CreateConfiguredAdministratorPending(
            Guid.CreateVersion7(), AuthenticationProviderKind.Local, DeploymentMode.SingleTenant,
            generation, new string('a', 64), new string('b', 64), DateTime.UtcNow);
        migration.InstanceBootstrapStates.Add(evidence);
        await migration.SaveChangesAsync();
        try
        {
            await using var global = Open(PrimaryDatabaseRole.Global);
            var projected = await InstanceBootstrapStateQuery.ReadAsync(
                global, () => fixture.NativeQueueEnrollment, default);
            await Assert.That(projected!.Id).IsEqualTo(evidence.Id);
            await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
            var factory = Substitute.For<IGlobalExploreDbContextFactory>();
            factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(global);
            using var services = new ServiceCollection()
                .AddSingleton(fixture.NativeQueueEnrollment).BuildServiceProvider();
            var reader = new InstanceBootstrapStateReader(factory, services);

            var actual = await reader.GetCurrent();

            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.Id).IsEqualTo(evidence.Id);
            await Assert.That(actual.Generation).IsEqualTo(generation);
            await Assert.That(actual.ConfigurationFingerprint).IsEqualTo(evidence.ConfigurationFingerprint);
            await Assert.That(actual.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
            await using var runtime = Open(PrimaryDatabaseRole.Runtime);
            var denied = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                runtime.InstanceBootstrapStates.AsNoTracking().ToListAsync());
            await Assert.That(denied!.SqlState).IsEqualTo(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            await migration.InstanceBootstrapStates.Where(row => row.Id == evidence.Id).ExecuteDeleteAsync();
        }
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        return new(options.Options);
    }
}
