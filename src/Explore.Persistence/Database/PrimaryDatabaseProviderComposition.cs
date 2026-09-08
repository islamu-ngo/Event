using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Explore.Persistence.Database;

public enum PrimaryDatabaseMigrationTarget
{
    Application,
    DataProtection,
    CoLocatedPrivacyErasureAuthority,
}

public static class PrimaryDatabaseProviderComposition
{
    public const string UnsupportedCoLocatedPrivacyErasureAuthorityMessage =
        "PrivacyErasure:Authority:Topology=CoLocated supports only primary PostgreSql or Sqlite databases. " +
        "Choose EmbeddedSqlite with any primary provider, or choose ExternalDatabase with a separate PostgreSql database.";
    public const string PostgreSqlApplicationMigrationsAssembly = "Explore.Persistence";
    public const string PostgreSqlDataProtectionMigrationsAssembly = "Explore.Persistence";
    internal const string ApplicationMigrationsHistoryTable = "__EFMigrationsHistory";
    internal const string DataProtectionMigrationsHistoryTable = "__EFDataProtectionMigrationsHistory";
    internal const string CoLocatedPrivacyErasureAuthorityMigrationsHistoryTable =
        "__EFPrivacyErasureAuthorityMigrationsHistory";

    public static PrimaryDatabaseConnectionResult ConfigureApplication(
        DbContextOptionsBuilder optionsBuilder,
        PrimaryDatabaseConnectionOptions options)
    {
        PrimaryDatabaseConnectionResult database = Configure(
            optionsBuilder,
            options,
            PrimaryDatabaseMigrationTarget.Application);
        switch (options.Provider)
        {
            case PrimaryDatabaseProvider.MariaDb:
            case PrimaryDatabaseProvider.MySql:
                optionsBuilder.AddInterceptors(
                    MySqlNamedLockTransactionInterceptor.Instance);
                break;
            case PrimaryDatabaseProvider.Sqlite:
                optionsBuilder.AddInterceptors(
                    SqliteNamedLockTransactionInterceptor.Instance,
                    SqliteProjectionLockTransactionInterceptor.Instance);
                break;
        }

        return database;
    }

    public static PrimaryDatabaseConnectionResult ConfigureDataProtection(
        DbContextOptionsBuilder optionsBuilder,
        PrimaryDatabaseConnectionOptions options)
        => Configure(optionsBuilder, options, PrimaryDatabaseMigrationTarget.DataProtection);

    public static PrimaryDatabaseConnectionResult ConfigureCoLocatedPrivacyErasureAuthority(
        DbContextOptionsBuilder optionsBuilder,
        PrimaryDatabaseConnectionOptions options)
    {
        if (options.Provider != PrimaryDatabaseProvider.PostgreSql)
        {
            throw new InvalidOperationException(UnsupportedCoLocatedPrivacyErasureAuthorityMessage);
        }

        return Configure(
            optionsBuilder,
            options,
            PrimaryDatabaseMigrationTarget.CoLocatedPrivacyErasureAuthority);
    }

    public static string GetMigrationsAssemblyName(
        PrimaryDatabaseProvider provider,
        PrimaryDatabaseMigrationTarget target)
    {
        if (provider == PrimaryDatabaseProvider.PostgreSql)
        {
            return target switch
            {
                PrimaryDatabaseMigrationTarget.Application => PostgreSqlApplicationMigrationsAssembly,
                PrimaryDatabaseMigrationTarget.DataProtection => PostgreSqlDataProtectionMigrationsAssembly,
                PrimaryDatabaseMigrationTarget.CoLocatedPrivacyErasureAuthority =>
                    PostgreSqlApplicationMigrationsAssembly,
                _ => throw new ArgumentOutOfRangeException(nameof(target)),
            };
        }

        var providerName = provider == PrimaryDatabaseProvider.MariaDb
            ? PrimaryDatabaseProvider.MySql.ToString()
            : provider.ToString();
        return target switch
        {
            PrimaryDatabaseMigrationTarget.Application => $"Explore.Persistence.Migrations.{providerName}",
            PrimaryDatabaseMigrationTarget.DataProtection =>
                $"Explore.Persistence.DataProtection.Migrations.{providerName}",
            _ => throw new InvalidOperationException(
                "Co-located authority migrations currently support PostgreSql only."),
        };
    }

    private static PrimaryDatabaseConnectionResult Configure(
        DbContextOptionsBuilder optionsBuilder,
        PrimaryDatabaseConnectionOptions options,
        PrimaryDatabaseMigrationTarget target)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(options);

        var database = PrimaryDatabaseConfiguration.BuildConnectionString(options);
        var migrationsAssembly = GetMigrationsAssemblyName(options.Provider, target);
        var migrationsHistory = GetMigrationsHistoryTable(options.Provider, target, options.Schema);
        var modelSchema = options.Role == PrimaryDatabaseRole.Migrator
            ? PrimaryDatabaseConnectionOptions.DefaultSchema
            : options.Schema;
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new RelationalNamespaceOptionsExtension(modelSchema, options.Schema));

        switch (options.Provider)
        {
            case PrimaryDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(database.ConnectionString, providerOptions =>
                {
                    providerOptions.MigrationsAssembly(migrationsAssembly);
                    providerOptions.MigrationsHistoryTable(migrationsHistory.Table, migrationsHistory.Schema);
                    if (target != PrimaryDatabaseMigrationTarget.CoLocatedPrivacyErasureAuthority)
                    {
                        providerOptions.EnableRetryOnFailure(
                            maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(5),
                            errorCodesToAdd: null);
                    }
                    providerOptions.CommandTimeout(30);
                    if (target == PrimaryDatabaseMigrationTarget.Application)
                    {
                        providerOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    }
                });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableNpgsqlMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.Sqlite:
                optionsBuilder.UseSqlite(
                    database.ConnectionString,
                    providerOptions =>
                    {
                        providerOptions.MigrationsAssembly(migrationsAssembly);
                        providerOptions.MigrationsHistoryTable(migrationsHistory.Table);
                    });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqliteMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.SqlServer:
                optionsBuilder.UseSqlServer(
                    database.ConnectionString,
                    providerOptions =>
                    {
                        providerOptions.MigrationsAssembly(migrationsAssembly);
                        providerOptions.MigrationsHistoryTable(migrationsHistory.Table, migrationsHistory.Schema);
                    });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqlServerMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.MariaDb:
                optionsBuilder.UseMySql(
                    database.ConnectionString,
                    new MariaDbServerVersion(options.ServerVersion!),
                    providerOptions =>
                    {
                        providerOptions.MigrationsAssembly(migrationsAssembly);
                        providerOptions.MigrationsHistoryTable(migrationsHistory.Table);
                    });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableMySqlMigrationsSqlGenerator>();
                break;

            case PrimaryDatabaseProvider.MySql:
                optionsBuilder.UseMySql(
                    database.ConnectionString,
                    new MySqlServerVersion(options.ServerVersion!),
                    providerOptions =>
                    {
                        providerOptions.MigrationsAssembly(migrationsAssembly);
                        providerOptions.MigrationsHistoryTable(migrationsHistory.Table);
                    });
                optionsBuilder.ReplaceService<IMigrationsSqlGenerator, ConfigurableMySqlMigrationsSqlGenerator>();
                break;

            default:
                throw new InvalidOperationException($"Unsupported primary database provider '{options.Provider}'.");
        }

        optionsBuilder.UseSnakeCaseNamingConvention();
        return database;
    }

    internal static (string Table, string? Schema) GetMigrationsHistoryTable(
        PrimaryDatabaseProvider provider,
        PrimaryDatabaseMigrationTarget target,
        string schema = PrimaryDatabaseConnectionOptions.DefaultSchema)
    {
        var table = target switch
        {
            PrimaryDatabaseMigrationTarget.Application => ApplicationMigrationsHistoryTable,
            PrimaryDatabaseMigrationTarget.DataProtection => DataProtectionMigrationsHistoryTable,
            PrimaryDatabaseMigrationTarget.CoLocatedPrivacyErasureAuthority =>
                CoLocatedPrivacyErasureAuthorityMigrationsHistoryTable,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        return provider is PrimaryDatabaseProvider.PostgreSql or PrimaryDatabaseProvider.SqlServer
            ? (table, schema)
            : (RelationalModelNamespace.Prefix + table, null);
    }
}
