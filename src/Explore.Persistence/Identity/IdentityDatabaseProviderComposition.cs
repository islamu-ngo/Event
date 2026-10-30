using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Identity;

internal static class IdentityDatabaseProviderComposition
{
    internal const string MigrationsHistoryTable = "__EFIdentityMigrationsHistory";

    internal static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        IConfiguration configuration,
        PrimaryDatabaseRole role) =>
        Configure(optionsBuilder, IdentityDatabaseConfiguration.BindExternal(configuration, role));

    internal static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        ExternalIdentityDatabaseDescriptor database)
    {
        ArgumentNullException.ThrowIfNull(database);
        ConfigureProvider(optionsBuilder, database.Provider, database.Schema, database.ServerVersion,
            database.ConnectionString);
    }

    /// <summary>Builds the same provider model without granting connection authority.</summary>
    internal static void ConfigureModel(DbContextOptionsBuilder optionsBuilder,
        PrimaryDatabaseProvider provider, string schema, Version? serverVersion) =>
        ConfigureProvider(optionsBuilder, provider, schema, serverVersion, connectionString: null);

    private static void ConfigureProvider(DbContextOptionsBuilder optionsBuilder,
        PrimaryDatabaseProvider databaseProvider, string databaseSchema, Version? serverVersion,
        string? connectionString)
    {
        string migrationsAssembly = PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
            databaseProvider,
            PrimaryDatabaseMigrationTarget.Application);
        string? schema = databaseProvider
            is PrimaryDatabaseProvider.PostgreSql or PrimaryDatabaseProvider.SqlServer
            ? databaseSchema
            : null;

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new RelationalNamespaceOptionsExtension(
                databaseSchema,
                databaseSchema));

        switch (databaseProvider)
        {
            case PrimaryDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(connectionString, provider =>
                {
                    provider.MigrationsAssembly(migrationsAssembly);
                    provider.MigrationsHistoryTable(MigrationsHistoryTable, schema);
                    provider.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                    provider.CommandTimeout(30);
                });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableNpgsqlMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.Sqlite:
                optionsBuilder.UseSqlite(connectionString, provider =>
                {
                    provider.MigrationsAssembly(migrationsAssembly);
                    provider.MigrationsHistoryTable(
                        RelationalModelNamespace.Prefix + MigrationsHistoryTable);
                });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqliteMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.SqlServer:
                optionsBuilder.UseSqlServer(connectionString, provider =>
                {
                    provider.MigrationsAssembly(migrationsAssembly);
                    provider.MigrationsHistoryTable(MigrationsHistoryTable, schema);
                });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqlServerMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.MariaDb:
                optionsBuilder.UseMySql(
                    connectionString,
                    new MariaDbServerVersion(serverVersion!),
                    provider =>
                    {
                        provider.MigrationsAssembly(migrationsAssembly);
                        provider.MigrationsHistoryTable(
                            RelationalModelNamespace.Prefix + MigrationsHistoryTable);
                    });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableMySqlMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.MySql:
                optionsBuilder.UseMySql(
                    connectionString,
                    new MySqlServerVersion(serverVersion!),
                    provider =>
                    {
                        provider.MigrationsAssembly(migrationsAssembly);
                        provider.MigrationsHistoryTable(
                            RelationalModelNamespace.Prefix + MigrationsHistoryTable);
                    });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableMySqlMigrationsSqlGenerator>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported external Identity database provider '{databaseProvider}'.");
        }

        optionsBuilder.UseSnakeCaseNamingConvention();
    }
}
