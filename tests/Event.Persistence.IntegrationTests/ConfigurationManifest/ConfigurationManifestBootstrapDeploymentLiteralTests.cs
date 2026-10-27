using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationManifestBootstrapDeploymentLiteralTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("$body$")]
    [Arguments("$roles$")]
    public async Task BootstrapInstallerAcceptsDollarQuotedDeploymentLogin(string delimiter)
    {
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        await using var migration = new ExploreDbContext(builder.Options);
        migration.ClearTenantFilterBypass();
        migration.BindTenantLease();
        await migration.Database.OpenConnectionAsync();
        var connection = migration.Database.GetDbConnection();
        var originalSession = await ReadSessionAsync(migration);
        string originalLogin = fixture.NativeQueueEnrollment.GlobalLogin;
        string originalCatalog = await BootstrapDeploymentCatalogAsync(migration, originalLogin, options.Schema);
        string renamedLogin = "bootstrap" + delimiter + "'\\" + Guid.CreateVersion7().ToString("N")[..12];
        using var identifiers = new NpgsqlCommandBuilder();
        await using var transaction = await migration.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var physicalTransaction = transaction.GetDbTransaction();
        try
        {
            // PostgreSQL identifiers cannot be parameters; quote both complete role names.
            string renameSql = "ALTER ROLE " + identifiers.QuoteIdentifier(originalLogin)
                + " RENAME TO " + identifiers.QuoteIdentifier(renamedLogin);
            await migration.Database.ExecuteSqlRawAsync(renameSql);

            // The renamed role keeps its fixture-issued credential and original OID.
            // Installation must parse the complete deployment name, not reject it as SQL.
            await PostgresConfigurationManifestBootstrapContract.ApplyAsync(migration, renamedLogin);

            await Assert.That(ReferenceEquals(migration.Database.GetDbConnection(), connection)).IsTrue();
            await Assert.That(ReferenceEquals(migration.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(migration.Database.CurrentTransaction!.GetDbTransaction(), physicalTransaction)).IsTrue();
            await Assert.That(ReferenceEquals(physicalTransaction.Connection, connection)).IsTrue();
            var installedSession = await ReadSessionAsync(migration);
            await Assert.That(installedSession).IsEqualTo(originalSession);
            await Assert.That(installedSession.Login).IsEqualTo(options.Username!);
        }
        finally
        {
            await transaction.RollbackAsync();
            await Assert.That(await BootstrapDeploymentCatalogAsync(migration, originalLogin, options.Schema))
                .IsEqualTo(originalCatalog);
            await Assert.That(await migration.Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {0}) AS \"Value\"", renamedLogin)
                .SingleAsync()).IsFalse();
        }
    }

    private static async Task<(int Backend, string Login)> ReadSessionAsync(ExploreDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_catalog.pg_backend_pid(), CURRENT_USER::text WHERE CURRENT_USER = SESSION_USER";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new UnauthorizedAccessException("The original migration session identity changed.");
        var session = (reader.GetInt32(0), reader.GetString(1));
        if (await reader.ReadAsync())
            throw new InvalidOperationException("The migration session observation was ambiguous.");
        return session;
    }

    private static async Task<string> BootstrapDeploymentCatalogAsync(
        ExploreDbContext context, string globalLogin, string schema)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT pg_catalog.jsonb_build_object(
                'global', (SELECT pg_catalog.jsonb_build_object('oid', r.oid, 'name', r.rolname)
                    FROM pg_catalog.pg_roles r WHERE r.rolname = @login),
                'functions', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'oid', p.oid, 'definition', pg_catalog.pg_get_functiondef(p.oid),
                    'owner', p.proowner, 'acl', p.proacl) ORDER BY p.proname)
                    FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = @schema AND p.proname = ANY(@functions)),
                'policies', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'row', pg_catalog.to_jsonb(p), 'comment', pg_catalog.obj_description(p.oid, 'pg_policy'))
                    ORDER BY p.polrelid, p.polname)
                    FROM pg_catalog.pg_policy p JOIN pg_catalog.pg_class c ON c.oid = p.polrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = @schema AND p.polname LIKE 'manifest_bootstrap%'))::text
            """;
        command.Parameters.Add(new NpgsqlParameter("login", globalLogin));
        command.Parameters.Add(new NpgsqlParameter("schema", schema));
        command.Parameters.Add(new NpgsqlParameter("functions", new[]
        {
            "configuration_manifest_bootstrap_assert_authority", "configuration_manifest_bootstrap_source",
            "configuration_manifest_bootstrap_validate_source", "configuration_manifest_bootstrap_write",
            "configuration_manifest_bootstrap_write_failed"
        }));
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
