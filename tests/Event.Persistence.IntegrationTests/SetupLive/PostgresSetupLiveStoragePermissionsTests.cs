using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain.Secrets;
using Explore.Domain.SetupLive;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.SetupLive;

[ClassDataSource<PostgresSetupStorageFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresSetupLiveStoragePermissionsTests(PostgresSetupStorageFixture fixture)
{
    [Test]
    public async Task DeclaredMigratorInstallsOnlyRequiredRuntimeStorageRights()
    {
        await using var migrator = CreateMigrator();
        await migrator.Database.OpenConnectionAsync();
        string migratorLogin = migrator.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(fixture.NativeQueueEnrollment.MigratorLogin);
        // Bootstrap needs elevated DDL rights; exercise this installer as the ordinary schema owner.
        await migrator.Database.ExecuteSqlRawAsync(
            $"ALTER ROLE {migratorLogin} NOSUPERUSER NOCREATEDB NOCREATEROLE");
        await using (var identity = migrator.Database.GetDbConnection().CreateCommand())
        {
            identity.CommandText = """
                SELECT session_user = @login AND current_user = session_user
                    AND NOT (SELECT rolsuper FROM pg_roles WHERE rolname = session_user)
                """;
            var login = identity.CreateParameter();
            login.ParameterName = "login";
            login.Value = fixture.NativeQueueEnrollment.MigratorLogin;
            identity.Parameters.Add(login);
            await Assert.That(await identity.ExecuteScalarAsync()).IsEqualTo(true);
        }

        await PostgresSetupLiveStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        await PostgresSetupLiveStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);

        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        await runtime.Database.OpenConnectionAsync();
        Type[] storage =
        [
            typeof(SetupTargetEnrollment), typeof(SetupEnrollmentIssuanceClaim),
            typeof(SetupSecretBindingOperation)
        ];
        foreach (Type type in storage)
            await AssertStorageRightsAsync(runtime, type, writable: true);
        await AssertStorageRightsAsync(runtime, typeof(SecretBinding), writable: false);
    }

    [Test]
    public async Task RuntimeCannotInvokeTheMigratorStorageInstaller()
    {
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        var exception = await Assert.That(async () =>
                await PostgresSetupLiveStoragePermissions.ApplyAsync(runtime, fixture.NativeQueueEnrollment))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(exception!.Message).IsEqualTo("setup_live_storage_installer_wrong_identity");
    }

    private ExploreDbContext CreateMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        return new ExploreDbContext(options.Options);
    }

    private async Task AssertStorageRightsAsync(ExploreDbContext runtime, Type type, bool writable)
    {
        var entity = runtime.Model.GetEntityTypes().Single(candidate => candidate.ClrType == type);
        string relation = runtime.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        await using var command = runtime.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT session_user = @login AND current_user = session_user
                AND NOT (SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = session_user)
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'SELECT')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'INSERT')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'UPDATE')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'DELETE')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'TRUNCATE')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'REFERENCES')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'TRIGGER')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'SELECT WITH GRANT OPTION')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'INSERT WITH GRANT OPTION')
                AND NOT has_table_privilege(current_user, CAST(@relation AS regclass), 'UPDATE WITH GRANT OPTION')
            """;
        foreach (var parameter in new (string Name, object Value)[]
                 {
                     ("login", fixture.NativeQueueEnrollment.RuntimeLogin),
                     ("relation", relation)
                 })
        {
            var value = command.CreateParameter();
            value.ParameterName = parameter.Name;
            value.Value = parameter.Value;
            command.Parameters.Add(value);
        }
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);

        string[] updates = type == typeof(SetupTargetEnrollment)
            ? [nameof(SetupTargetEnrollment.CapabilityDigest), nameof(SetupTargetEnrollment.Generation),
                nameof(SetupTargetEnrollment.State), nameof(SetupTargetEnrollment.ExpiresAt),
                nameof(SetupTargetEnrollment.RevokedAt), nameof(SetupTargetEnrollment.ExpiredAt),
                nameof(SetupTargetEnrollment.ConcurrencyStamp), nameof(SetupTargetEnrollment.UpdatedAt),
                nameof(SetupTargetEnrollment.UpdatedBy)]
            : type == typeof(SetupSecretBindingOperation)
                ? [nameof(SetupSecretBindingOperation.State), nameof(SetupSecretBindingOperation.Outcome),
                    nameof(SetupSecretBindingOperation.MutationEvidence), nameof(SetupSecretBindingOperation.SettledAt),
                    nameof(SetupSecretBindingOperation.ConcurrencyStamp), nameof(SetupSecretBindingOperation.UpdatedAt),
                    nameof(SetupSecretBindingOperation.UpdatedBy)]
                : [];
        string[] bindingIdentity =
        [
            nameof(SecretBinding.Id), nameof(SecretBinding.SettingKey), nameof(SecretBinding.Qualifier),
            nameof(SecretBinding.SettingScopeId), nameof(SecretBinding.ScopeId)
        ];
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        foreach (var property in entity.GetProperties())
        {
            await using var column = runtime.Database.GetDbConnection().CreateCommand();
            column.CommandText = """
                SELECT has_column_privilege(current_user, CAST(@relation AS regclass), @column, 'SELECT') = @read
                    AND has_column_privilege(current_user, CAST(@relation AS regclass), @column, 'INSERT') = @insert
                    AND has_column_privilege(current_user, CAST(@relation AS regclass), @column, 'UPDATE') = @update
                    AND NOT has_column_privilege(current_user, CAST(@relation AS regclass), @column, 'REFERENCES')
                    AND NOT has_column_privilege(current_user, CAST(@relation AS regclass), @column, 'SELECT WITH GRANT OPTION')
                    AND NOT has_column_privilege(current_user, CAST(@relation AS regclass), @column, 'INSERT WITH GRANT OPTION')
                    AND NOT has_column_privilege(current_user, CAST(@relation AS regclass), @column, 'UPDATE WITH GRANT OPTION')
                """;
            foreach (var parameter in new (string Name, object Value)[]
                     {
                         ("relation", relation), ("column", property.GetColumnName(store)!),
                         ("read", writable || bindingIdentity.Contains(property.Name)),
                         ("insert", writable), ("update", updates.Contains(property.Name))
                     })
            {
                var value = column.CreateParameter();
                value.ParameterName = parameter.Name;
                value.Value = parameter.Value;
                column.Parameters.Add(value);
            }
            await Assert.That(await column.ExecuteScalarAsync()).IsEqualTo(true);
        }
    }
}

public sealed class PostgresSetupStorageFixture()
    : PostgreSqlContainerFixture(IdentityDatabaseTopology.Colocated, distinctMigrator: true);
