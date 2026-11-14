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

    [Test]
    public async Task ReusedAttestationRejectsNewWritePrivilegesOnOriginalBackendAndRecovers()
    {
        await fixture.ResetAsync();
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var original = await factory.CreateDbContextAsync();
        await original.Database.OpenConnectionAsync();
        await PostgresOrdinarySettingsContract.RequireReadyAsync(original, fixture.NativeQueueEnrollment);
        await using var migration = fixture.CreateDbContext();
        migration.BindTenantLease();
        var names = migration.GetService<ISqlGenerationHelper>();
        var source = new HumanPrincipalSourceSql(migration);
        string table = source.Table(typeof(SystemSetting));
        string column = source.Column(typeof(SystemSetting), nameof(SystemSetting.Value));
        string global = names.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);

        await migration.Database.ExecuteSqlRawAsync($"GRANT UPDATE ({column}) ON {table} TO {global}");
        try
        {
            var denial = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresOrdinarySettingsContract.RequireReadyAsync(original, fixture.NativeQueueEnrollment));
            await Assert.That(denial.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await Assert.That(denial.MessageText).IsEqualTo("ordinary_settings_global_ambient_write");
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"REVOKE UPDATE ({column}) ON {table} FROM {global}");
        }
        await PostgresOrdinarySettingsContract.RequireReadyAsync(original, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task ReusedAttestationCannotBorrowAnotherDeclaredGlobalEnrollment()
    {
        await fixture.ResetAsync();
        string key = GovernanceSettingKeys.Security.AuthorizationProvider;
        await Assert.That(await CreateReader().ReadAsync([key])).IsEmpty();
        var different = fixture.NativeQueueEnrollment with
        {
            GlobalLogin = "missing_global_" + Guid.CreateVersion7().ToString("N")
        };
        var reader = new InstanceSystemSettingsReader(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)), different);

        await Assert.ThrowsAsync<PostgresException>(async () => await reader.ReadAsync([key]));

        await Assert.That(await CreateReader().ReadAsync([key])).IsEmpty();
    }

    [Test]
    public async Task ConcurrentInstanceObservationsReturnStoredPolicyWithoutSharingTrackedState()
    {
        await fixture.ResetAsync();
        string key = "instance.concurrent.observation." + Guid.CreateVersion7().ToString("N");
        await using (var migration = fixture.CreateDbContext())
        {
            migration.BindTenantLease();
            migration.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = "\"https://pdp.example.test:443\"",
                ValueType = SettingValueType.String,
                CreatedAt = DateTime.UtcNow
            });
            await migration.SaveChangesAsync();
        }

        var reader = CreateReader();
        var results = await Task.WhenAll(
            reader.ReadAsync([key], TestContext.Current!.Execution.CancellationToken),
            reader.ReadAsync([key], TestContext.Current.Execution.CancellationToken));

        await Assert.That(results.All(rows => rows.Count == 1 && rows[0].SettingKey == key
            && rows[0].Value == "\"https://pdp.example.test:443\"")).IsTrue();
        await Assert.That(ReferenceEquals(results[0][0], results[1][0])).IsFalse();
        results[0][0].Value = "\"changed-observation\"";
        await Assert.That(results[1][0].Value).IsEqualTo("\"https://pdp.example.test:443\"");
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        readiness.CancelAfter(TimeSpan.FromSeconds(5));
        var warmed = await Task.WhenAll(Enumerable.Range(3, 4)
            .Select(_ => reader.ReadAsync([key], readiness.Token)));
        await Assert.That(warmed.All(rows => rows.Count == 1
            && rows[0].Value == "\"https://pdp.example.test:443\"")).IsTrue();
    }
}
