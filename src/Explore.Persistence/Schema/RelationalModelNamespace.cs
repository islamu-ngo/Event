using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Schema;

internal static class RelationalModelNamespace
{
    internal const string DefaultSchema = "islamu_event";
    internal const string Name = DefaultSchema;
    internal const string Prefix = "ie_";

    public static void Apply(ModelBuilder modelBuilder, string? providerName, string schema)
    {
        if (SupportsSchemas(providerName))
        {
            modelBuilder.HasDefaultSchema(schema);
            return;
        }

        if (providerName is not ("Microsoft.EntityFrameworkCore.Sqlite" or "Microting.EntityFrameworkCore.MySql"))
        {
            return;
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (!string.IsNullOrEmpty(tableName) && !tableName.StartsWith(Prefix, StringComparison.Ordinal))
            {
                entityType.SetTableName(Prefix + tableName);
            }
        }
    }

    private static bool SupportsSchemas(string? providerName) =>
        providerName is "Npgsql.EntityFrameworkCore.PostgreSQL" or "Microsoft.EntityFrameworkCore.SqlServer";
}
