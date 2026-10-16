using System.Data;
using System.Text.Json;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

public static partial class PostgresOrdinarySettingsContract
{
    internal static async Task<(IReadOnlyList<SystemSetting> System, IReadOnlyList<TenantSetting> Tenant)?>
        TryReadHierarchyAsync(ExploreDbContext context, Func<PostgresNativeQueueEnrollment> enrollment,
            Guid? tenant, IReadOnlyCollection<string> keys, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? await ReadHierarchyAsync(context, enrollment(), tenant, keys, token)
            : null;

    internal static async Task<(IReadOnlyList<SystemSetting> System, IReadOnlyList<TenantSetting> Tenant)>
        ReadHierarchyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
            Guid? tenant, IReadOnlyCollection<string> keys, CancellationToken token)
    {
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Settings reads must retain their selected ordinary scope.");

        bool openedHere = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (openedHere)
            await context.Database.OpenConnectionAsync(token);
        try
        {
            await RequireReadyAsync(context, enrollment, token);
            await using var command = Command(context);
            command.CommandText = $"SELECT {FunctionSql(context, "read")}(@tenant,NULL::uuid,@keys)::text";
            Add(command, "tenant", NpgsqlDbType.Uuid, tenant);
            Add(command, "keys", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(keys.Distinct(StringComparer.Ordinal).ToArray()));
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(token)
                ?? throw new UnauthorizedAccessException("The current settings image is absent.")));
            var image = document.RootElement.GetProperty("Image");
            return (Materialize<SystemSetting>(image.GetProperty("System")),
                Materialize<TenantSetting>(image.GetProperty("Tenant")));
        }
        finally
        {
            // An enclosing physical transaction already owns its connection.
            if (openedHere)
                await context.Database.CloseConnectionAsync();
        }

        IReadOnlyList<T> Materialize<T>(JsonElement rows) where T : class
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var properties = entity.GetProperties().ToArray();
            var fields = Fields(typeof(T)).ToDictionary(field => field.Property, field => field.Field);
            var materializer = context.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(entity);
            List<T> result = [];
            foreach (var entry in rows.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Null)
                    continue;
                object?[] values = new object?[properties.Max(property => property.GetIndex()) + 1];
                foreach (var property in properties)
                    values[property.GetIndex()] = entry.Value.GetProperty(fields[property.Name])
                        .Deserialize(property.ClrType);
                result.Add((T)materializer(new MaterializationContext(new ValueBuffer(values), context)));
            }
            return result;
        }
    }
}
