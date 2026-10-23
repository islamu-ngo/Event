using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Fixed mapped source vocabulary; no caller-selected key, relation, or predicate.</summary>
internal sealed class HumanAuthorizationRouteSql(ExploreDbContext context)
{
    private readonly HumanPrincipalSourceSql _mapping = new(context);
    internal static readonly (Type Type, string[] Columns)[] Sources =
    [
        (typeof(SystemSetting), ["SettingKey", "Value", "SettingValueTypeId", "IsLocked",
            "Description", "Category", "AllowedValues"]),
        (typeof(TenantSetting), ["SettingKey", "Value", "IsLocked", "TenantId"])
    ];

    internal string Schema => _mapping.Schema;
    internal string Name(string name) => _mapping.Name(name);
    internal string Table(Type type) => _mapping.Table(type);
    internal string Column(Type type, string property) => _mapping.Column(type, property);
    internal string ColumnName(Type type, string property) => _mapping.ColumnName(type, property);
    internal string Quote(string name) => _mapping.Quote(name);

    internal string Query(bool tenant, bool currentRead = false)
    {
        Type type = tenant ? typeof(TenantSetting) : typeof(SystemSetting);
        (string Property, string Alias)[] columns = tenant
            ? [("SettingKey", "Key"), ("Value", "Value"), ("IsLocked", "IsLocked")]
            : [("SettingKey", "Key"), ("Value", "Value"), ("SettingValueTypeId", "ValueType"),
                ("IsLocked", "IsLocked"), ("Description", "Description"),
                ("Category", "Category"), ("AllowedValues", "AllowedValues")];
        string fields = string.Join(",", columns.Select(column =>
            $"{Column(type, column.Property)} AS {Quote(column.Alias)}"));
        string keys = string.Join(",", HumanAuthorizationRouteFacts.Keys.Select((_, index) => $"@key{index}"));
        string hint = currentRead && context.Database.IsSqlServer() ? " WITH (UPDLOCK,HOLDLOCK)" : "";
        string suffix = currentRead && context.Database.ProviderName == RelationalNamedLock.MySqlProvider
            ? " FOR UPDATE" : "";
        return $"SELECT {fields} FROM {Table(type)}{hint} WHERE {Column(type, "SettingKey")} IN ({keys})"
            + (tenant ? $" AND {Column(type, "TenantId")} = @tenant" : "")
            + $" ORDER BY {Column(type, "SettingKey")}{suffix}";
    }
}
