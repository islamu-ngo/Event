using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

        await caller.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await caller.Database.BeginTransactionAsync();
            var connection = caller.Database.GetDbConnection();
            await Assert.That(await reader.IsConfiguredAsync()).IsTrue();
            await Assert.That(ReferenceEquals(caller.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(caller.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(caller.ChangeTracker.Entries().Any()).IsFalse();
            await transaction.CommitAsync();
        });
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
        var context = new ExploreDbContext(options.Options)
        { TenantContext = tenant.HasValue ? new SelectedTenant(tenant.Value) : null };
        context.BindTenantLease();
        return context;
    }

    [Test]
    public async Task PrivateColumnGrantIsRejectedWithoutRepairingItsGrant()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var entity = migration.Model.FindEntityType(typeof(SecretBinding))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = migration.GetService<ISqlGenerationHelper>();
        string table = names.DelimitIdentifier(store.Name, store.Schema);
        string column = entity.FindProperty(nameof(SecretBinding.EnvironmentVariableName))!.GetColumnName(store)!;
        string login = fixture.NativeQueueEnrollment.GlobalLogin;
        await migration.Database.ExecuteSqlRawAsync(
            $"GRANT SELECT ({names.DelimitIdentifier(column)}) ON {table} TO {names.DelimitIdentifier(login)}");
        try
        {
            await using var caller = Open(PrimaryDatabaseRole.Global);
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await using var command = migration.Database.GetDbConnection().CreateCommand();
            await migration.Database.OpenConnectionAsync();
            command.CommandText = "SELECT pg_catalog.has_column_privilege(@login,@table,@column,'SELECT')";
            command.Parameters.Add(new NpgsqlParameter("login", login));
            command.Parameters.Add(new NpgsqlParameter("table", table));
            command.Parameters.Add(new NpgsqlParameter("column", column));
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(
                $"REVOKE SELECT ({names.DelimitIdentifier(column)}) ON {table} FROM {names.DelimitIdentifier(login)}");
        }
    }

    [Test]
    public async Task RuntimeReaderCannotChooseAnotherTenant()
    {
        await fixture.ResetAsync();
        Guid selected = Guid.CreateVersion7(), foreign = Guid.CreateVersion7();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        migration.SecretBindings.Add(Binding(SecretScope.Tenant, foreign));
        await migration.SaveChangesAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, selected);
        await caller.Database.OpenConnectionAsync();
        using var services = new ServiceCollection().AddSingleton(caller)
            .AddSingleton(fixture.NativeQueueEnrollment).BuildServiceProvider();
        var reader = ActivatorUtilities.CreateInstance<LocalizationSecretStatusReader>(services);
        await Assert.That(await reader.IsConfiguredAsync()).IsFalse();
        using var missingEnrollment = new ServiceCollection().BuildServiceProvider();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new LocalizationSecretStatusReader(caller, missingEnrollment).IsConfiguredAsync());
        await using var unselected = Open(PrimaryDatabaseRole.Runtime);
        await unselected.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(unselected, fixture.NativeQueueEnrollment));
        await migration.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            caller.TenantContext = new SelectedTenant(foreign);
            return reader.IsConfiguredAsync();
        });
        await Assert.That(caller.TenantFilterTenantId).IsEqualTo(selected);
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
