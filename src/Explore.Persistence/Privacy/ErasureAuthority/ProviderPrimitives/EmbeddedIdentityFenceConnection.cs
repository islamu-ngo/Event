using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Privacy.ErasureAuthority.ProviderPrimitives;

internal static class EmbeddedIdentityFenceConnection
{
    internal static bool TryShareApplicationConnection(
        ExploreDbContext? applicationContext,
        EmbeddedPrivacyErasureAuthorityDbContext authorityContext)
    {
        if (applicationContext?.Database.IsSqlite() != true)
            return false;

        var connection = applicationContext.Database.GetDbConnection();
        var left = new SqliteConnectionStringBuilder(connection.ConnectionString);
        var right = new SqliteConnectionStringBuilder(authorityContext.Database.GetDbConnection().ConnectionString);
        if (string.IsNullOrEmpty(left.DataSource) || left.DataSource == ":memory:"
            || Path.GetFullPath(left.DataSource) != Path.GetFullPath(right.DataSource))
            return false;

        if (applicationContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The identity gate must precede the application transaction.");
        authorityContext.Database.SetDbConnection(connection, contextOwnsConnection: false);
        return true;
    }
}
