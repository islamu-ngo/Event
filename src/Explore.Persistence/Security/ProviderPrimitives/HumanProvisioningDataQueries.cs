using System.Buffers.Binary;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;
using Native = Explore.Persistence.Security.PostgresManagedProviderProvisioningContract;

namespace Explore.Persistence.Security;

/// <summary>
/// Executes the closed provisioning vocabulary on the captured connector. The source
/// keeps the original request, one-shot admission and terminal commit ownership.
/// </summary>
internal sealed class HumanProvisioningDataQueries(
    ExploreDbContext context, RelationalConnectionIdentity connection, PostgresNativeQueueEnrollment? enrollment)
{
    internal object? CaptureSqliteHandle() =>
        connection.Connection is SqliteConnection sqlite ? sqlite.Handle : null;

    internal bool MatchesSqliteHandle(object? handle) =>
        handle is null || connection.Connection is SqliteConnection sqlite && ReferenceEquals(handle, sqlite.Handle);

    internal bool TerminalChangesMatch(int changes) =>
        connection.Connection is not SqliteConnection sqlite
        || SQLitePCL.raw.sqlite3_total_changes(sqlite.Handle) == changes;

    internal async Task<JsonObject> ReadAsync(Model.Request request, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
            return JsonNode.Parse(await Native.InvokeAsync(context, enrollment!, "read", request, null, token))!.AsObject();
        var sql = new ManagedProviderProvisioningSql(context, false);
        var data = new JsonObject();
        foreach (var relation in Model.Inventory)
        {
            await using var command = connection.Connection.CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = sql.Select(relation);
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@request";
            parameter.Value = Model.SerializeRequest(request);
            command.Parameters.Add(parameter);
            data[relation.Type.Name] = await Model.ReadRowsAsync(context, relation, command, token);
        }
        await using (var capacity = connection.Connection.CreateCommand())
        {
            capacity.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            capacity.CommandText = sql.CapacitySelect;
            await using var reader = await capacity.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("provisioning_capacity_source_missing");
            data["Capacity"] = new JsonObject
            {
                ["DeploymentMode"] = reader.GetInt32(0),
                ["ActiveTenants"] = reader.GetInt32(1),
                ["ReservedTenants"] = reader.GetInt32(2)
            };
        }
        // SQLite has no restricted login or native statement epoch. Retain an
        // internal digest of the remainder of this closed business vocabulary as
        // well: an unrelated row written through a borrowed transaction must not
        // disappear outside the target projection. Stream rows; never retain or
        // publish another tenant's PII image.
        using var remainder = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relation in Model.Inventory)
        {
            await using var command = connection.Connection.CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            var metadata = context.Model.FindEntityType(relation.Type)!;
            command.CommandText = $"SELECT {string.Join(",", relation.Columns.Select(column => "r." + sql.Column(relation.Type, column)))}"
                + $" FROM {sql.Table(relation.Type)} r WHERE NOT COALESCE(({sql.Predicate(relation.Type)}),0) ORDER BY "
                + string.Join(",", metadata.FindPrimaryKey()!.Properties.Select(property => "r." + sql.Column(relation.Type, property.Name)));
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@request";
            parameter.Value = Model.SerializeRequest(request);
            command.Parameters.Add(parameter);
            Append(relation.Type.Name);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var row = new JsonObject();
                for (int index = 0; index < relation.Columns.Length; index++)
                    row[relation.Columns[index]] = Model.Value(metadata.FindProperty(relation.Columns[index])!, reader.GetValue(index));
                Append(row.ToJsonString());
            }
        }
        return new JsonObject { ["Data"] = data, ["Remainder"] = Convert.ToHexStringLower(remainder.GetHashAndReset()) };

        void Append(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            remainder.AppendData(length);
            remainder.AppendData(bytes);
        }
    }

    internal async Task<JsonObject> AcquireAsync(Model.Request request, JsonObject before, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
            return JsonNode.Parse(await Native.InvokeAsync(context, enrollment!, "acquire",
                request, before.ToJsonString(), token))!.AsObject();
        var admitted = await ReadAsync(request, token);
        if (!JsonNode.DeepEquals(before, admitted)) throw new DbUpdateConcurrencyException("provisioning_data_preimage_changed");
        return admitted;
    }

    internal async Task<JsonObject> ApplyAsync(Model.Request request, JsonObject admitted,
        ManagedProviderProvisioningCompiler.Plan plan, DbTransaction transaction, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
        {
            var compiled = JsonNode.Parse(await Native.InvokeAsync(context, enrollment!, "compile",
                request, null, token))!.AsObject();
            if (!JsonNode.DeepEquals(compiled["Data"], plan.Postimage)
                || !JsonNode.DeepEquals(compiled["Rows"], JsonSerializer.SerializeToNode(plan.Rows, Model.Json)))
                throw new InvalidOperationException("provisioning_data_independent_compiler_mismatch");
            return JsonNode.Parse(await Native.InvokeAsync(context, enrollment!, "apply",
                request, null, token))!.AsObject();
        }
        if (!JsonNode.DeepEquals(admitted, await ReadAsync(request, token)))
            throw new DbUpdateConcurrencyException("provisioning_data_compiled_preimage_changed");
        // SQLite's non-deferred Serializable transaction is the native
        // enrollment/writer fence. Anchors and the revision are in the plan.
        foreach (var effect in plan.Effects)
            await ApplySqliteAsync(effect, transaction, token);
        return await ReadAsync(request, token);
    }

    internal void RequireRemainder(JsonObject admitted, JsonObject applied)
    {
        if (context.Database.IsSqlite() && !JsonNode.DeepEquals(admitted["Remainder"], applied["Remainder"]))
            throw new DbUpdateConcurrencyException("provisioning_data_effect_escaped_footprint");
    }

    private async Task ApplySqliteAsync(ManagedProviderProvisioningCompiler.Effect effect,
        DbTransaction transaction, CancellationToken token)
    {
        var sql = new ManagedProviderProvisioningSql(context, false);
        var relation = effect.Relation;
        var metadata = context.Model.FindEntityType(relation.Type)!;
        await using var command = connection.Connection.CreateCommand();
        command.Transaction = transaction;
        string[] columns = effect.Before is null ? relation.Columns : relation.Update;
        if (columns.Length == 0 || effect.Before is null && !relation.Insert)
            throw new InvalidOperationException("provisioning_data_private_plan_not_writable");
        object? Provider(string name, JsonNode? value)
        {
            if (value is null) return DBNull.Value;
            var property = metadata.FindProperty(name)!;
            object? scalar = property.ClrType == typeof(string) && value is JsonObject or JsonArray
                ? value.ToJsonString()
                : value.Deserialize(property.ClrType, Model.Json);
            return property.GetRelationalTypeMapping().Converter?.ConvertToProvider(scalar) ?? scalar;
        }
        for (int index = 0; index < columns.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@v" + index;
            parameter.Value = Provider(columns[index], effect.After[columns[index]]) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        if (effect.Before is null)
            command.CommandText = $"INSERT INTO {sql.Table(relation.Type)} ({string.Join(",", columns.Select(name => sql.Column(relation.Type, name)))})"
                + $" VALUES ({string.Join(",", columns.Select((_, index) => "@v" + index))})";
        else
        {
            var key = command.CreateParameter();
            key.ParameterName = "@key";
            key.Value = Provider(relation.Key, effect.After[relation.Key])!;
            command.Parameters.Add(key);
            command.CommandText = $"UPDATE {sql.Table(relation.Type)} SET "
                + string.Join(",", columns.Select((name, index) => sql.Column(relation.Type, name) + " = @v" + index))
                + $" WHERE {sql.Column(relation.Type, relation.Key)} = @key";
        }
        if (await command.ExecuteNonQueryAsync(token) != 1)
            throw new DbUpdateConcurrencyException("provisioning_data_effect_cardinality");
    }

    internal async Task<int> CompleteAsync(Model.Request request, JsonObject applied, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
        {
            var current = JsonNode.Parse(await Native.InvokeAsync(context, enrollment!, "complete",
                request, null, token));
            if (!JsonNode.DeepEquals(current, applied)) throw new DbUpdateConcurrencyException("provisioning_data_terminal_image_changed");
            return 0;
        }
        if (!JsonNode.DeepEquals(applied, await ReadAsync(request, token)))
            throw new DbUpdateConcurrencyException("provisioning_data_terminal_image_changed");
        return SQLitePCL.raw.sqlite3_total_changes(((SqliteConnection)connection.Connection).Handle);
    }
}
