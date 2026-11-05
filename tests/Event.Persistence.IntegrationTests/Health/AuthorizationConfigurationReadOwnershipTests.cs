using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AuthorizationConfigurationReadOwnershipTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task InstanceReadinessObservesConfigurationWithoutPromotingItsRuntimeCaller()
    {
        await fixture.ResetAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        await using var runtime = new ExploreDbContext(options.Options);
        runtime.BindTenantLease();
        var mutationScopes = Substitute.For<IPolicyMutationExecutionScopeFactory>();
        mutationScopes.CreateAsync(Arg.Any<PolicyMutationScope>(), Arg.Any<CancellationToken>())
            .Returns<IPolicyMutationExecutionScope>(_ =>
                throw new InvalidOperationException("Readiness cannot acquire a mutation capability."));
        var service = new AuthorizationProviderConfigurationService(
            CreateReader(),
            new ConfigurationBuilder().Build(),
            new CerbosAdminEndpointValidator(),
            Substitute.For<IAuthorizationProviderModeCacheInvalidator>(),
            Substitute.For<ICerbosConfigResolver>(),
            Substitute.For<IPolicyPackageService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "local" }),
            new AuthorizationProviderBootstrapState(),
            NullLogger<AuthorizationProviderConfigurationService>.Instance,
            mutationScopes,
            Substitute.For<ICerbosEndpointHealthProbe>());

        var observed = await service.ReadConfigurationAsync();

        await Assert.That(observed.Provider).IsEqualTo("local");
        await Assert.That(observed.AuthorizationProviderConfigured).IsTrue();
        await Assert.That(runtime.TenantFilterTenantId).IsNull();
        await Assert.That(runtime.ChangeTracker.Entries()).IsEmpty();
        string table = new HumanPrincipalSourceSql(runtime).Table(typeof(SystemSetting));
        var denial = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Database.ExecuteSqlRawAsync($"SELECT value FROM {table}"));
        await Assert.That(denial?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task InstanceReadReturnsOnlyRequestedDetachedStoredRowsAndPreservesAbsence()
    {
        await fixture.ResetAsync();
        await using var migration = fixture.CreateDbContext();
        migration.BindTenantLease();
        string endpointKey = GovernanceSettingKeys.Cerbos.GrpcEndpoint;
        string providerKey = GovernanceSettingKeys.Security.AuthorizationProvider;
        await Assert.That(await migration.SystemSettings.AnyAsync(
            row => row.SettingKey == endpointKey || row.SettingKey == providerKey)).IsFalse();
        var endpoint = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = endpointKey,
            Value = "\"https://pdp.example.test:443\"",
            ValueType = SettingValueType.String,
            IsLocked = true,
            Description = "Stored endpoint",
            CreatedAt = DateTime.UtcNow
        };
        migration.SystemSettings.AddRange(endpoint, new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = "instance.reader.unrequested",
            Value = "\"not-requested\"",
            ValueType = SettingValueType.String,
            CreatedAt = DateTime.UtcNow
        });
        await migration.SaveChangesAsync();
        var reader = CreateReader();

        var first = await reader.ReadAsync([endpointKey, providerKey]);

        await Assert.That(first.Count).IsEqualTo(1);
        await Assert.That(first[0].Id).IsEqualTo(endpoint.Id);
        await Assert.That(first[0].Value).IsEqualTo(endpoint.Value);
        await Assert.That(first[0].IsLocked).IsTrue();
        first[0].Value = "\"forged-copy\"";
        var current = await reader.ReadAsync([endpointKey, providerKey]);
        await Assert.That(current.Count).IsEqualTo(1);
        await Assert.That(current[0].Value).IsEqualTo(endpoint.Value);
        await Assert.That(current[0].SettingKey).IsEqualTo(endpointKey);
    }

    private InstanceSystemSettingsReader CreateReader() =>
        new(new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)),
            fixture.NativeQueueEnrollment);
}
