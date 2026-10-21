using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Domain.Settings.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Fixed mixed-batch observation vocabulary shared by its native and SQLite adapters.</summary>
internal static partial class MixedSettingsDataImage
{
    internal static readonly Type[] ReadTypes =
    [
        typeof(SystemSetting), typeof(TenantSetting), typeof(SecretBinding), typeof(Tenant),
        typeof(EmailDispatchProcessorState), typeof(EmailDispatchTenantControl),
        typeof(EventParticipationConfiguration), typeof(EventLocation), typeof(Location), typeof(Explore.Domain.Event)
    ];

    internal static readonly Type[] WriteTypes =
    [
        typeof(SystemSetting), typeof(TenantSetting), typeof(EmailDispatchProcessorState),
        typeof(EmailDispatchTenantControl), typeof(EventLocation), typeof(EventLocationDisclosureAudit),
        typeof(LocationPrivacyCorrectionReceipt), typeof(OutboxMessage)
    ];

    internal static ImmutableArray<string> FixedKeys =>
        EmailDeliverySettingKeys.All
            .Concat(PublicationPolicySettingKeys.All).Concat(EventResourceSettingMutationGuard.Keys)
            .Concat(VisitorAccessCapabilityResolver.AuthoritySettingKeys)
            .Concat(LocationPrivacySettingDefinitions.All.Select(definition => definition.Key))
            .Append(GovernanceSettingKeys.Organizations.TenantCanOmitVerification)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();

    internal static ImmutableArray<string> Keys(UpdateSettingBatchCommand original) =>
        original.Values.Keys.Concat(FixedKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();

    internal static ImmutableArray<string> TypedKeys =>
        TenantPolicySettingService.OriginalReadKeys.Concat(FixedKeys).Concat(HumanAuthorizationRouteFacts.Keys)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();

    internal static string[] Properties(ExploreDbContext context, Type type, bool typed = false) =>
        type == typeof(Explore.Domain.Event) ? ["Id", "TenantId", "IsDeleted"]
        : type == typeof(Tenant) ? typed ? ["Id", "TenantStatusId", "Slug", "FullName"] : ["Id", "TenantStatusId"]
        : type == typeof(EventParticipationConfiguration) ? ["Id", "TenantId", "IsDeleted", "IdentityAccessModeId"]
        : context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();

    internal static string Predicate(ExploreDbContext context, Type type, string tenant, string keys)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Key() => $"r.{sql.Column(type, "SettingKey")} IN ({keys})";
        if (type == typeof(SystemSetting)) return Key();
        if (type == typeof(TenantSetting))
            return $"{Key()} AND ({tenant} IS NULL OR r.{sql.Column(type, "TenantId")} = {tenant})";
        if (type == typeof(SecretBinding))
            return $"{Key()} AND ({tenant} IS NULL OR r.{sql.Column(type, nameof(SecretBinding.ScopeId))} IS NULL"
                + $" OR r.{sql.Column(type, nameof(SecretBinding.ScopeId))} = {tenant})";
        if (type == typeof(Tenant))
            return $"({tenant} IS NULL OR r.{sql.Column(type, "Id")} = {tenant})";
        if (context.Model.FindEntityType(type)!.FindProperty("TenantId") is not null)
            return $"({tenant} IS NULL OR r.{sql.Column(type, "TenantId")} = {tenant})";
        return "1 = 1";
    }

    internal static Task<string> ReadSqliteAsync(ExploreDbContext context,
        UpdateSettingBatchCommand original, Guid? tenant, CancellationToken token) =>
        ReadSqliteAsync(context, Keys(original), tenant, false, token);

    internal static Task<string> ReadTypedSqliteAsync(ExploreDbContext context, TypedSettingsOriginal original, CancellationToken token) =>
        ReadSqliteAsync(context, TypedKeys, original.TenantId, true, token, original);

    private static async Task<string> ReadSqliteAsync(ExploreDbContext context,
        ImmutableArray<string> keys, Guid? tenant, bool typed, CancellationToken token, TypedSettingsOriginal? original = null)
    {
        if (!context.Database.IsSqlite())
            throw new NotSupportedException("mixed_settings_sqlite_observation_required");
        var sql = new HumanPrincipalSourceSql(context);
        var data = new JsonObject();
        foreach (var type in original?.PlanAssignment is not null ? ReadTypes.Concat(PlanReadTypes)
            : original?.Governance is not null ? ReadTypes.Concat(GovernanceReadTypes) : ReadTypes)
        {
            var entity = context.Model.FindEntityType(type)!;
            var properties = Properties(context, type, typed);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            void Parameter(string name, object? value, DbType kind)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.DbType = kind;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            Parameter("tenant", tenant, DbType.Guid);
            if (original?.Governance is not null)
                Parameter("governanceTenant", PlatformDefaults.DefaultTenantId, DbType.Guid);
            if (original?.PlanAssignment is { } plan)
                Parameter("assignment", plan.AssignmentId, DbType.Guid);
            for (int index = 0; index < keys.Length; index++)
                Parameter($"key{index}", keys[index], DbType.String);
            string predicate = GovernanceReadTypes.Contains(type) ? GovernancePredicate(context, type, "@governanceTenant")
                : PlanReadTypes.Contains(type) ? PlanPredicate(context, type, "@assignment")
                : typed && type == typeof(SystemSetting) ? "1 = 1"
                : typed && type == typeof(TenantSetting) ? $"(@tenant IS NULL OR r.{sql.Column(type, "TenantId")} = @tenant)"
                : Predicate(context, type, "@tenant", string.Join(",", keys.Select((_, index) => $"@key{index}")));
            command.CommandText = $"SELECT {string.Join(",", properties.Select(name => $"r.{sql.Column(type, name)} AS {sql.Quote(name)}"))}"
                + $" FROM {sql.Table(type)} r WHERE {predicate} ORDER BY r.{sql.Column(type, "Id")}";
            var rows = new JsonArray();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int index = 0; index < properties.Length; index++)
                {
                    string name = properties[index];
                    object? value = await reader.IsDBNullAsync(index, token) ? null : reader.GetValue(index);
                    Type propertyType = Nullable.GetUnderlyingType(entity.FindProperty(name)!.ClrType)
                        ?? entity.FindProperty(name)!.ClrType;
                    if (value is not null)
                    {
                        if (propertyType == typeof(Guid))
                            value = value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                        else if (propertyType == typeof(bool))
                            value = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                        else if (propertyType == typeof(DateTime))
                            value = DateTime.SpecifyKind(value is DateTime date ? date
                                : DateTime.Parse((string)value, CultureInfo.InvariantCulture), DateTimeKind.Utc)
                                .ToString("O", CultureInfo.InvariantCulture);
                        else if (propertyType == typeof(DateTimeOffset))
                            value = (value is DateTimeOffset offset ? offset
                                : DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
                                .UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                        else if (type == typeof(Explore.Domain.Settings.Documents.TenantSettingsDocument)
                            && name == nameof(Explore.Domain.Settings.Documents.TenantSettingsDocument.PayloadJson)
                            || type == typeof(Explore.Domain.Modules.TenantCapability)
                            && name == nameof(Explore.Domain.Modules.TenantCapability.ConfigurationJson))
                            value = JsonNode.Parse((string)value);
                    }
                    row.Add(name, value);
                }
                rows.Add(JsonSerializer.SerializeToNode(row));
            }
            data[type.Name] = rows;
        }
        return data.ToJsonString();
    }

    internal static bool Same(string left, string right) =>
        JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));

    internal static async Task<JsonObject?> ReadSqliteEffectAsync(
        ExploreDbContext context, Type type, Guid id, CancellationToken token)
    {
        if (!context.Database.IsSqlite() || !WriteTypes.Contains(type))
            throw new UnauthorizedAccessException("Only an exact original SQLite mixed effect can be observed.");
        var sql = new HumanPrincipalSourceSql(context);
        var properties = context.Model.FindEntityType(type)!.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {string.Join(",", properties.Select(property => sql.Column(type, property.Name)))}"
            + $" FROM {sql.Table(type)} WHERE {sql.Column(type, "Id")} = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.DbType = DbType.Guid;
        parameter.Value = id;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var row = new JsonObject();
        for (int index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            if (await reader.IsDBNullAsync(index, token)) { row[property.Name] = null; continue; }
            object value = reader.GetValue(index);
            Type valueType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (valueType == typeof(Guid))
                value = (value is Guid guid ? guid : Guid.Parse((string)value)).ToString("D");
            else if (valueType == typeof(bool))
                value = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            else if (valueType == typeof(DateTime))
                value = DateTime.SpecifyKind(value is DateTime date ? date
                    : DateTime.Parse((string)value, CultureInfo.InvariantCulture), DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
            else if (valueType == typeof(DateTimeOffset))
                value = (value is DateTimeOffset offset ? offset
                    : DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
                    .UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            if (value is string json && property.GetRelationalTypeMapping().StoreType is "json" or "jsonb")
                row[property.Name] = JsonNode.Parse(json);
            else
                row[property.Name] = JsonSerializer.SerializeToNode(value, value.GetType());
        }
        if (await reader.ReadAsync(token))
            throw new UnauthorizedAccessException("Mixed effect identity is not unique.");
        return row;
    }

    internal static ImmutableArray<T> Rows<T>(string data)
    {
        using var document = JsonDocument.Parse(data);
        var rows = document.RootElement.GetProperty(typeof(T).Name);
        if (typeof(T) == typeof(SystemSetting))
            return rows.EnumerateArray().Select(row => (T)(object)new SystemSetting
            {
                Id = row.GetProperty(nameof(SystemSetting.Id)).GetGuid(),
                SettingKey = row.GetProperty(nameof(SystemSetting.SettingKey)).GetString()!,
                Value = row.GetProperty(nameof(SystemSetting.Value)).GetString()!,
                SettingValueTypeId = row.GetProperty(nameof(SystemSetting.SettingValueTypeId)).GetInt32(),
                IsLocked = row.GetProperty(nameof(SystemSetting.IsLocked)).GetBoolean(),
                AllowedValues = row.GetProperty(nameof(SystemSetting.AllowedValues)).ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => row.GetProperty(nameof(SystemSetting.AllowedValues)).GetString(),
                    _ => row.GetProperty(nameof(SystemSetting.AllowedValues)).GetRawText()
                },
                Description = row.GetProperty(nameof(SystemSetting.Description)).GetString(),
                Category = row.GetProperty(nameof(SystemSetting.Category)).GetString(),
                DisplayOrder = row.GetProperty(nameof(SystemSetting.DisplayOrder)).GetInt32(),
                CreatedAt = row.GetProperty(nameof(SystemSetting.CreatedAt)).GetDateTime(),
                CreatedBy = row.GetProperty(nameof(SystemSetting.CreatedBy)).Deserialize<Guid?>(),
                UpdatedAt = row.GetProperty(nameof(SystemSetting.UpdatedAt)).Deserialize<DateTime?>(),
                UpdatedBy = row.GetProperty(nameof(SystemSetting.UpdatedBy)).Deserialize<Guid?>()
            }).ToImmutableArray();
        if (typeof(T) == typeof(TenantSetting))
        {
            // The authoritative image contains every persisted scalar, not navigation
            // objects. Materialize the detached FK-based entity explicitly.
            return rows.EnumerateArray().Select(row => (T)(object)new TenantSetting
            {
                Id = row.GetProperty(nameof(TenantSetting.Id)).GetGuid(),
                TenantId = row.GetProperty(nameof(TenantSetting.TenantId)).GetGuid(),
                Tenant = null!,
                SettingKey = row.GetProperty(nameof(TenantSetting.SettingKey)).GetString()!,
                Value = row.GetProperty(nameof(TenantSetting.Value)).GetString()!,
                IsLocked = row.GetProperty(nameof(TenantSetting.IsLocked)).GetBoolean(),
                CreatedAt = row.GetProperty(nameof(TenantSetting.CreatedAt)).GetDateTime(),
                CreatedBy = row.GetProperty(nameof(TenantSetting.CreatedBy)).Deserialize<Guid?>(),
                UpdatedAt = row.GetProperty(nameof(TenantSetting.UpdatedAt)).Deserialize<DateTime?>(),
                UpdatedBy = row.GetProperty(nameof(TenantSetting.UpdatedBy)).Deserialize<Guid?>()
            }).ToImmutableArray();
        }
        if (typeof(T) == typeof(Explore.Domain.Modules.TenantCapability))
            return rows.EnumerateArray().Select(row => (T)(object)new Explore.Domain.Modules.TenantCapability
            {
                Id = row.GetProperty("Id").GetGuid(),
                TenantId = row.GetProperty("TenantId").GetGuid(),
                Tenant = null!,
                ModuleId = row.GetProperty("ModuleId").GetGuid(),
                Module = null!,
                IsEnabled = row.GetProperty("IsEnabled").GetBoolean(),
                EnabledAt = row.GetProperty("EnabledAt").GetDateTime(),
                EnabledBy = row.GetProperty("EnabledBy").Deserialize<Guid?>(),
                ConfigurationJson = row.GetProperty("ConfigurationJson").ValueKind == JsonValueKind.Null
                    ? null : row.GetProperty("ConfigurationJson").GetRawText(),
                CreatedAt = row.GetProperty("CreatedAt").GetDateTime(),
                CreatedBy = row.GetProperty("CreatedBy").Deserialize<Guid?>(),
                UpdatedAt = row.GetProperty("UpdatedAt").Deserialize<DateTime?>(),
                UpdatedBy = row.GetProperty("UpdatedBy").Deserialize<Guid?>()
            }).ToImmutableArray();
        if (typeof(T) == typeof(Explore.Domain.Settings.Documents.TenantSettingsDocument))
            return rows.EnumerateArray().Select(row => (T)(object)new Explore.Domain.Settings.Documents.TenantSettingsDocument
            {
                Id = row.GetProperty("Id").GetGuid(),
                TenantId = row.GetProperty("TenantId").GetGuid(),
                Tenant = null!,
                DocumentKey = row.GetProperty("DocumentKey").GetString()!,
                SchemaVersion = row.GetProperty("SchemaVersion").GetInt32(),
                DefaultsVersion = row.GetProperty("DefaultsVersion").GetString()!,
                PayloadJson = row.GetProperty("PayloadJson").GetRawText(),
                ConcurrencyStamp = row.GetProperty("ConcurrencyStamp").GetGuid(),
                CreatedAt = row.GetProperty("CreatedAt").GetDateTime(),
                CreatedBy = row.GetProperty("CreatedBy").Deserialize<Guid?>(),
                UpdatedAt = row.GetProperty("UpdatedAt").Deserialize<DateTime?>(),
                UpdatedBy = row.GetProperty("UpdatedBy").Deserialize<Guid?>()
            }).ToImmutableArray();
        return rows.Deserialize<ImmutableArray<T>>();
    }

    internal static ImmutableArray<Guid> TenantIds(string data)
    {
        using var document = JsonDocument.Parse(data);
        return document.RootElement.GetProperty(nameof(Tenant)).EnumerateArray()
            .Select(row => row.GetProperty(nameof(Tenant.Id)).GetGuid()).ToImmutableArray();
    }
}
