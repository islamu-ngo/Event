using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
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
        PrimaryDatabaseRole role)
    {
        ExternalIdentityDatabaseDescriptor database =
            IdentityDatabaseConfiguration.BindExternal(configuration, role);
        string migrationsAssembly = PrimaryDatabaseProviderComposition.GetMigrationsAssemblyName(
            database.Provider,
            PrimaryDatabaseMigrationTarget.Application);
        string? schema = database.Provider
            is PrimaryDatabaseProvider.PostgreSql or PrimaryDatabaseProvider.SqlServer
            ? database.Schema
            : null;

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new RelationalNamespaceOptionsExtension(
                database.Schema,
                database.Schema));

        switch (database.Provider)
        {
            case PrimaryDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(database.ConnectionString, provider =>
                {
                    provider.MigrationsAssembly(migrationsAssembly);
                    provider.MigrationsHistoryTable(MigrationsHistoryTable, schema);
                    provider.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                    provider.CommandTimeout(30);
                });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableNpgsqlMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.Sqlite:
                optionsBuilder.UseSqlite(database.ConnectionString, provider =>
                {
                    provider.MigrationsAssembly(migrationsAssembly);
                    provider.MigrationsHistoryTable(
                        RelationalModelNamespace.Prefix + MigrationsHistoryTable);
                });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqliteMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.SqlServer:
                optionsBuilder.UseSqlServer(database.ConnectionString, provider =>
                {
                    provider.MigrationsAssembly(migrationsAssembly);
                    provider.MigrationsHistoryTable(MigrationsHistoryTable, schema);
                });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqlServerMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.MariaDb:
                optionsBuilder.UseMySql(
                    database.ConnectionString,
                    new MariaDbServerVersion(database.ServerVersion!),
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
                    database.ConnectionString,
                    new MySqlServerVersion(database.ServerVersion!),
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
                    $"Unsupported external Identity database provider '{database.Provider}'.");
        }

        optionsBuilder.UseSnakeCaseNamingConvention();
    }
}
