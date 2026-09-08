using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal enum RelationalProvider
{
    PostgreSql,
    Sqlite,
    SqlServer,
    MySql
}

internal static class RelationalProviderClassifier
{
    internal const string PostgreSqlName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    internal const string SqliteName = "Microsoft.EntityFrameworkCore.Sqlite";
    internal const string SqlServerName = "Microsoft.EntityFrameworkCore.SqlServer";
    internal const string MySqlName = "Microting.EntityFrameworkCore.MySql";

    public static RelationalProvider Classify(DatabaseFacade database) =>
        database.ProviderName switch
        {
            PostgreSqlName => RelationalProvider.PostgreSql,
            SqliteName => RelationalProvider.Sqlite,
            SqlServerName => RelationalProvider.SqlServer,
            MySqlName => RelationalProvider.MySql,
            string provider => throw new InvalidOperationException(
                $"Unsupported relational provider '{provider}'."),
            _ => throw new InvalidOperationException(
                "The relational provider is unavailable.")
        };
}
