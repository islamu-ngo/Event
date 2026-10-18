using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class LocalizationSecretStatusReaderTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SelectedTenantStatusExcludesForeignAndQualifiedBindingsAndFallsBackToInstance()
    {
        await fixture.ResetAsync();
        var tenant = Guid.CreateVersion7();
        var foreign = Guid.CreateVersion7();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        migration.Tenants.AddRange(Tenant(tenant), Tenant(foreign));
        migration.SecretBindings.AddRange(Binding(SecretScope.Tenant, foreign),
            Binding(SecretScope.Tenant, tenant, "other"));
        await migration.SaveChangesAsync();

        await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
        using var services = new ServiceCollection()
            .AddSingleton(caller)
            .AddSingleton<ITenantContext>(new SelectedTenant(tenant))
            .AddSingleton(fixture.NativeQueueEnrollment)
            .AddSingleton<ISecretBindingRepository>(new SecretBindingRepository(caller))
            .BuildServiceProvider();
        var reader = ActivatorUtilities.CreateInstance<LocalizationSecretStatusReader>(services);
        await Assert.That(await reader.IsConfiguredAsync()).IsFalse();

        var own = Binding(SecretScope.Tenant, tenant);
        migration.SecretBindings.Add(own);
        await migration.SaveChangesAsync();
        await Assert.That(await reader.IsConfiguredAsync()).IsTrue();
        migration.SecretBindings.Remove(own);
        await migration.SaveChangesAsync();
        await Assert.That(await reader.IsConfiguredAsync()).IsFalse();
        migration.SecretBindings.Add(Binding(SecretScope.Instance, null));
        await migration.SaveChangesAsync();

        await using var transaction = await caller.Database.BeginTransactionAsync();
        var connection = caller.Database.GetDbConnection();
        await Assert.That(await reader.IsConfiguredAsync()).IsTrue();
        await Assert.That(ReferenceEquals(caller.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(ReferenceEquals(caller.Database.GetDbConnection(), connection)).IsTrue();
        await Assert.That(caller.ChangeTracker.Entries().Any()).IsFalse();
        await transaction.CommitAsync();
        var denied = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            caller.SecretBindings.AsNoTracking().ToListAsync());
        await Assert.That(denied!.SqlState).IsEqualTo(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenant = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime)
            options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new(options.Options) { TenantContext = tenant.HasValue ? new SelectedTenant(tenant.Value) : null };
    }

    [Test]
    public async Task UndeclaredExecutorIsRejectedWithoutRepairingItsGrant()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var executor = $"status_executor_{Guid.CreateVersion7():N}";
        var function = PostgresLocalizationSecretStatusContract.FunctionSql(migration);
        await migration.Database.ExecuteSqlRawAsync(
            $"CREATE ROLE \"{executor}\" NOLOGIN; GRANT EXECUTE ON FUNCTION {function}(uuid) TO \"{executor}\"");
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                PostgresLocalizationSecretStatusContract.ReadAsync(caller, fixture.NativeQueueEnrollment, default));
            await using var command = migration.Database.GetDbConnection().CreateCommand();
            await migration.Database.OpenConnectionAsync();
            command.CommandText = $"SELECT pg_catalog.has_function_privilege('{executor}', '{function}(uuid)', 'EXECUTE')";
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(
                $"REVOKE EXECUTE ON FUNCTION {function}(uuid) FROM \"{executor}\"; DROP ROLE \"{executor}\"");
        }
    }

    [Test]
    public async Task RuntimeSessionCannotChooseAnotherTenant()
    {
        await using var caller = Open(PrimaryDatabaseRole.Runtime, Guid.CreateVersion7());
        await caller.Database.OpenConnectionAsync();
        await using var command = caller.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {PostgresLocalizationSecretStatusContract.FunctionSql(caller)}(@tenant)";
        command.Parameters.Add(new Npgsql.NpgsqlParameter("tenant", Guid.CreateVersion7()));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => command.ExecuteScalarAsync());
    }

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = "Localization status",
        Slug = $"status-{id:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static SecretBinding Binding(SecretScope scope, Guid? tenant, string qualifier = "") => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
        Scope = scope,
        ScopeId = tenant,
        Qualifier = qualifier,
        SourceType = SecretSourceType.EnvironmentVariable,
        EnvironmentVariableName = "LOCALIZATION_TMS_API_KEY",
        LastValidationResult = SecretValidationResult.NotValidated
    };

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
