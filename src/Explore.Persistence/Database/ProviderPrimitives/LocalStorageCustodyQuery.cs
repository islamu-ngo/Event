using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class LocalStorageCustodyQuery
{
    internal static async Task<bool> ExistsAsync(
        ExploreDbContext context, Guid bindingId, string key, CancellationToken ct)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string union = BuildUnion(context, "{0}", "{1}");
        return await context.Database.SqlQueryRaw<int>(
            $"SELECT CASE WHEN EXISTS ({union}) THEN 1 ELSE 0 END AS {sql.DelimitIdentifier("Value")}",
            bindingId, key).SingleAsync(ct) == 1;
    }

    internal static string BuildUnion(ExploreDbContext context, string bindingSql, string keySql)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Select<T>(string binding, string key, string provider)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string C(string name) => sql.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!);
            string normalized = context.Database.IsSqlite() ? $"replace({C(key)}, char(92), '/')"
                : context.Database.ProviderName == RelationalNamedLock.SqlServerProvider ? $"replace({C(key)}, NCHAR(92), '/')"
                : $"replace({C(key)}, chr(92), '/')";
            if (context.Database.ProviderName == RelationalNamedLock.MySqlProvider)
                normalized = $"replace({C(key)}, CHAR(92), '/')";
            string uncertain = "";
            if (!OperatingSystem.IsLinux())
            {
                if (context.Database.IsNpgsql())
                {
                    normalized = $"lower({normalized} COLLATE \"C\")";
                    uncertain = $" OR {C(key)} COLLATE \"C\" ~ '[^ -~]'";
                }
                else if (context.Database.IsSqlite())
                {
                    normalized = $"lower({normalized})";
                    uncertain = $" OR {C(key)} GLOB '*[^ -~]*'";
                }
                else if (context.Database.ProviderName == RelationalNamedLock.SqlServerProvider)
                {
                    normalized = $"lower({normalized} COLLATE Latin1_General_100_BIN2)";
                    uncertain = $" OR {C(key)} COLLATE Latin1_General_100_BIN2 LIKE '%[^ -~]%'";
                }
                else
                {
                    normalized = $"lower({normalized})";
                    uncertain = $" OR {C(key)} REGEXP '[^ -~]'";
                }
                if (OperatingSystem.IsWindows())
                    uncertain += $" OR {normalized} LIKE '%./%' OR {normalized} LIKE '% /%' " +
                        $"OR {normalized} LIKE '%.' OR {normalized} LIKE '% ' OR {normalized} LIKE '%~%'";
            }
            return $"SELECT 1 FROM {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())} " +
                $"WHERE {C(provider)} = '{StorageProviders.Local}' AND {C(binding)} = {bindingSql} " +
                $"AND ({C(key)} = {keySql} OR {normalized} = {keySql}{uncertain})";
        }
        return string.Join(" UNION ALL ",
            Select<StorageObject>(nameof(StorageObject.StorageProviderBindingId), nameof(StorageObject.ObjectKey), nameof(StorageObject.Provider)),
            Select<StorageUploadSession>(nameof(StorageUploadSession.StorageProviderBindingId), nameof(StorageUploadSession.ObjectKey), nameof(StorageUploadSession.Provider)),
            Select<StorageProducerOperation>(nameof(StorageProducerOperation.ProviderBindingId), nameof(StorageProducerOperation.ObjectKey), nameof(StorageProducerOperation.Provider)),
            Select<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderBindingId), nameof(StorageObjectDeletionTombstone.ObjectKey), nameof(StorageObjectDeletionTombstone.Provider)));
    }
}
