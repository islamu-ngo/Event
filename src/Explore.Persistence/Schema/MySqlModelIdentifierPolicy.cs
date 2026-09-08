using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Schema;

internal static class MySqlModelIdentifierPolicy
{
    private const int MaxIdentifierLength = 64;
    private const int HashLength = 8;

    public static void Apply(ModelBuilder modelBuilder, string? providerName)
    {
        if (providerName != "Microting.EntityFrameworkCore.MySql")
        {
            return;
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (string.IsNullOrEmpty(table))
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());

            foreach (var key in entityType.GetKeys())
            {
                var name = key.GetName();
                var prefix = key.IsPrimaryKey() ? "pk" : "ak";
                var conventionName = $"{prefix}_{table}_{GetColumns(key.Properties, storeObject)}";
                if (RequiresShortening(conventionName) || RequiresShortening(name))
                {
                    key.SetName(Shorten(RequiresShortening(conventionName) ? conventionName : name!));
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var name = index.GetDatabaseName();
                var conventionName = $"ix_{table}_{GetColumns(index.Properties, storeObject)}";
                if (RequiresShortening(conventionName) || RequiresShortening(name))
                {
                    index.SetDatabaseName(Shorten(RequiresShortening(conventionName) ? conventionName : name!));
                }
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var name = foreignKey.GetConstraintName();
                var principalTable = foreignKey.PrincipalEntityType.GetTableName()!;
                var conventionName = $"fk_{table}_{principalTable}_{GetColumns(foreignKey.Properties, storeObject)}";
                if (RequiresShortening(conventionName) || RequiresShortening(name))
                {
                    foreignKey.SetConstraintName(Shorten(RequiresShortening(conventionName) ? conventionName : name!));
                }
            }
        }
    }

    private static bool RequiresShortening(string? name) =>
        name?.Length > MaxIdentifierLength || name?.EndsWith('~') == true;

    private static string GetColumns(IEnumerable<IMutableProperty> properties, StoreObjectIdentifier storeObject) =>
        string.Join('_', properties.Select(property => property.GetColumnName(storeObject)));

    private static string Shorten(string name)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..HashLength]
            .ToLowerInvariant();
        return name[..(MaxIdentifierLength - HashLength - 1)] + "_" + hash;
    }
}
