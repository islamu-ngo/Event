using Explore.Persistence.Services;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Onboarding;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class InstanceBootstrapStartupNativeReadTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task StartupReadsExplicitAbsenceAndCurrentCommittedCompletionWithoutRawTableAccess()
    {
        await using var migration = fixture.CreateDbContext();
        await migration.InstanceBootstrapStates.ExecuteDeleteAsync();
        await PostgresProviderConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        string directory = Directory.CreateTempSubdirectory("bootstrap-native-read-").FullName;
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["SETUP_SECRET_FILE"] = Path.Combine(directory, "setup-secret") }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IGlobalExploreDbContextFactory>(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
            services.AddSingleton(fixture.NativeQueueEnrollment);
            services.AddScoped(_ => OpenRuntime());
            services.AddScoped<IInstanceBootstrapStateRepository, InstanceBootstrapStateRepository>();
            services.AddScoped<IInstanceBootstrapStateReader, InstanceBootstrapStateReader>();
            await using var provider = services.BuildServiceProvider();
            using var setup = new SetupSecretProvider(configuration, provider.GetRequiredService<IServiceScopeFactory>());
            await setup.InitializeAsync();
            await Assert.That(setup.IsSetupModeActive).IsTrue();
            await Assert.That(await setup.IsSetupModeActiveAsync()).IsTrue();
            using var scope = provider.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IInstanceBootstrapStateReader>();
            await Assert.That(await reader.GetCurrent()).IsNull();

            var runtimeDenied = await Assert.ThrowsAsync<PostgresException>(() =>
                scope.ServiceProvider.GetRequiredService<IInstanceBootstrapStateRepository>().GetCurrent());
            await Assert.That(runtimeDenied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await using (var global = await provider.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync())
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                    global.InstanceBootstrapStates.AsNoTracking().ToArrayAsync());
                await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }

            var created = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
            const string fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var current = InstanceBootstrapState.CreateConfiguredAdministratorPending(
                Guid.CreateVersion7(), AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant,
                12, fingerprint, fingerprint, created);
            migration.InstanceBootstrapStates.AddRange(current,
                InstanceBootstrapState.CreateConfiguredAdministratorPending(
                    Guid.CreateVersion7(), AuthenticationProviderKind.Keycloak, DeploymentMode.SingleTenant,
                    11, fingerprint, fingerprint, created.AddMinutes(1)));
            await migration.SaveChangesAsync();
            var pending = await reader.GetCurrent();
            await Assert.That(pending!.Id).IsEqualTo(current.Id);
            await Assert.That(pending.Generation).IsEqualTo(12L);
            await Assert.That(pending.CreatedAt).IsEqualTo(created);
            await Assert.That(pending.CreatedAt.Kind).IsEqualTo(DateTimeKind.Utc);
            await Assert.That(pending.DeploymentMode).IsEqualTo(DeploymentMode.MultiTenant);
            await Assert.That(pending.Mode).IsEqualTo(InstanceBootstrapMode.ConfiguredAdministrator);
            await Assert.That(pending.ProviderKind).IsEqualTo(AuthenticationProviderKind.Keycloak);
            await Assert.That(pending.ConfigurationFingerprint).IsEqualTo(fingerprint);
            await Assert.That(pending.SelectorFingerprint).IsEqualTo(fingerprint);
            await Assert.That(pending.CompletedAt).IsNull();
            await Assert.That(pending.SupersededAt).IsNull();
            await Assert.That(pending.Status).IsEqualTo(InstanceBootstrapStatus.Pending);

            Guid userId = Guid.CreateVersion7();
            migration.Users.Add(new User
            {
                Id = userId,
                Pii = new UserPii
                {
                    Email = $"bootstrap-{userId:N}@example.test",
                    FirstName = "Bootstrap",
                    LastName = "Administrator"
                },
                EmailVerified = true,
                ConcurrencyStamp = Guid.CreateVersion7(),
                CreatedAt = created
            });
            current.CompleteConfiguredAdministrator(
                AuthenticationProviderKind.Keycloak, 12, fingerprint, userId, created.AddMinutes(2));
            await migration.SaveChangesAsync();
            var completed = await reader.GetCurrent();
            await Assert.That(completed!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
            await Assert.That(completed.CompletedByUserId).IsEqualTo(userId);
            await Assert.That(completed.CompletedIdentityFingerprint).IsEqualTo(fingerprint);
            await Assert.That(completed.CompletedAt).IsEqualTo(created.AddMinutes(2));
            await Assert.That(completed.CompletedAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
            await Assert.That(pending.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
            await Assert.That(await setup.IsSetupModeActiveAsync()).IsFalse();
            await Assert.That(File.Exists(setup.GeneratedSecretFilePath)).IsFalse();
            using var restarted = new SetupSecretProvider(configuration, provider.GetRequiredService<IServiceScopeFactory>());
            await restarted.InitializeAsync();
            await Assert.That(restarted.IsSetupModeActive).IsFalse();
            await Assert.That(File.Exists(restarted.GeneratedSecretFilePath)).IsFalse();
            await migration.InstanceBootstrapStates.ExecuteDeleteAsync();
            await Assert.That(await reader.GetCurrent()).IsNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task StartupReaderRejectsSubstitutedNativeLogin(PrimaryDatabaseRole role)
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresProviderConfigurationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var services = new ServiceCollection();
        services.AddSingleton(fixture.NativeQueueEnrollment);
        await using var provider = services.BuildServiceProvider();
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(role) with { Role = PrimaryDatabaseRole.Global });
        var reader = new InstanceBootstrapStateReader(factory, provider);
        if (role == PrimaryDatabaseRole.Migrator)
        {
            var readinessDenied = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetCurrent());
            await Assert.That(readinessDenied!.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
            return;
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() => reader.GetCurrent());
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private ExploreDbContext OpenRuntime()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        return new ExploreDbContext(options.Options);
    }
}
