using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed resolver data capability on the original Global connection. Neither enrollment
/// nor possession of a native executor is human request or administrator authority.
/// </summary>
public static partial class PostgresResolverConfigurationContract
{
    public const string OwnerRole = "event_resolver_configuration_owner";
    public const string MigratorRole = "event_resolver_configuration_migrator";
    public const string GlobalRole = "event_resolver_configuration_global";
    private const string Prefix = "resolver_configuration_";
    private const string Sentinel = Prefix + "source";
    private const string Stamp = "resolver-configuration:v1:";
    private const string WriteSignature = "uuid,jsonb,jsonb,timestamp with time zone";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, GlobalRole];
    private static readonly string[] Columns =
    [
        "Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked", "AllowedValues",
        "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly string[] Updates =
        ["Value", "SettingValueTypeId", "IsLocked", "AllowedValues", "Description", "Category", "DisplayOrder", "UpdatedAt", "UpdatedBy"];
    private static readonly (string Operation, string Signature, string Result)[] Routines =
        [("read", "", "jsonb"), ("write", WriteSignature, "void"), ("changed", "", "trigger")];

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    /// <summary>Read-only catalog and source-anchor admission; never enrolls, grants or repairs.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new InvalidOperationException("Resolver native custody requires PostgreSQL.");
        if (context.Database.GetDbConnection().State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"""
            DO $admission$ BEGIN
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
                IF session_user = {Text(enrollment.GlobalLogin)} THEN
                    -- The already-attested definer checks the anchor without granting its
                    -- underlying SELECT capability to Global. It also enforces caller scope.
                    PERFORM {FunctionSql(context, "read")}();
                ELSIF session_user = {Text(enrollment.MigratorLogin)} AND current_user = session_user
                    AND pg_catalog.current_setting('role') IN ('none',session_user::text) THEN
                    IF NOT EXISTS (SELECT 1 FROM {new HumanPrincipalSourceSql(context).Name(Sentinel)} WHERE id = 1)
                    THEN RAISE EXCEPTION 'resolver_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                ELSE
                    RAISE EXCEPTION 'resolver_configuration_caller' USING ERRCODE = '42501';
                END IF;
            END $admission$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>> ReadAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        RequireContext(context);
        // Test/native callers explicitly select their own connection; the writer retains
        // the already-open backend and rejects reconnects before and after this method.
        if (context.Database.GetDbConnection().State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(token);
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"SELECT {FunctionSql(context, "read")}()::pg_catalog.text";
        var result = await command.ExecuteScalarAsync(token);
        var rows = result is string json
            ? JsonSerializer.Deserialize<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>>(json) : null;
        if (rows is null || rows.Count != ResolverConfigurationMutation.Keys.Length
            || ResolverConfigurationMutation.Keys.Any(key => !rows.ContainsKey(key) || rows[key] is { } row && row.Key != key))
            throw new UnauthorizedAccessException("The complete native resolver source is missing.");
        RequireContext(context);
        return rows.WithComparers(StringComparer.Ordinal);
    }

    internal static async Task WriteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        ResolverConfigurationMutation mutation, CancellationToken token)
    {
        mutation.RequireActive();
        RequireContext(context);
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction is null || !context.HumanCommitAttemptOwnsTransaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Resolver effects require the original Serializable attempt.");
        if (mutation.Effects.IsEmpty || mutation.Effects.Any(effect =>
                effect.UpdatedAt is not { Kind: DateTimeKind.Utc } instant || instant.Ticks % 10 != 0))
            throw new UnauthorizedAccessException("Resolver effects must be represented before compilation.");
        await RequireReadyAsync(context, enrollment, token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"SELECT {FunctionSql(context, "write")}(@actor,@before,@effects,@occurred)";
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = mutation.Actor });
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(mutation.Before) });
        command.Parameters.Add(new NpgsqlParameter("effects", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(mutation.Effects) });
        command.Parameters.Add(new NpgsqlParameter("occurred", NpgsqlDbType.TimestampTz) { Value = mutation.Effects[0].UpdatedAt!.Value });
        await command.ExecuteNonQueryAsync(token);
        RequireContext(context);
        if (!ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
            throw new UnauthorizedAccessException("The original resolver native transaction changed.");
    }

    private static void RequireContext(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || context.TenantFilterTenantId is not null
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Resolver native custody requires ordinary tenant-null Global selection.");
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
