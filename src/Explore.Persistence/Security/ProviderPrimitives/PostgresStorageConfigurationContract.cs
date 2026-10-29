using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Configuration-only data capability. Enrollment never grants human or administrator authority.</summary>
public static partial class PostgresStorageConfigurationContract
{
    public const string OwnerRole = "event_storage_configuration_owner";
    public const string MigratorRole = "event_storage_configuration_migrator";
    public const string GlobalRole = "event_storage_configuration_global";
    public const string RuntimeRole = "event_storage_configuration_runtime";
    public const string SourceTable = "storage_configuration_source";
    private const string Prefix = "storage_configuration_";
    private const string Stamp = "storage-configuration:v1:";
    private const string TenantPolicy = "storage_configuration_owner";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, GlobalRole, RuntimeRole];
    private static readonly Type[] Sources = [typeof(SystemSetting), typeof(TenantSetting), typeof(SecretBinding)];
    private static readonly string[] Fields =
    [
        "TenantId", "Key", "Id", "Value", "ValueType", "IsLocked", "AllowedValues",
        "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly (string Name, string Signature, string Result)[] Routines =
    [
        ("read", "uuid", "jsonb"), ("acquire", "uuid,bigint", "int8"),
        ("write", "uuid,uuid,jsonb,jsonb,timestamp with time zone", "int8"), ("changed", "", "trigger")
    ];

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    internal static string SourceTriggerPredicate(ExploreDbContext context, Type sourceType)
    {
        if (!Sources.Contains(sourceType)) return "false";
        return $"""
            (t.tgrelid=pg_catalog.to_regclass({Text(new HumanPrincipalSourceSql(context).Table(sourceType))})
                AND t.tgname={Text(Prefix + "changed")}
                AND t.tgfoid=pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")}))
            """;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("storage_configuration_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid ambientTenant, CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Configuration admission cannot replace its selected connection.");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"""
            DO $ready$ BEGIN
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
                IF session_user = {Text(enrollment.MigratorLogin)} AND current_user = session_user
                    AND pg_catalog.current_setting('role') IN ('none',session_user::text) THEN
                    IF (SELECT count(*) FROM {new HumanPrincipalSourceSql(context).Name(SourceTable)} WHERE id = 1) <> 1
                    THEN RAISE EXCEPTION 'storage_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                ELSE
                    PERFORM {FunctionSql(context, "read")}({Text(ambientTenant.ToString("D"))}::uuid);
                END IF;
            END $ready$;
            """;
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<StorageConfigurationState> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Guid ambientTenant, CancellationToken token)
    {
        RequireContext(context);
        await RequireReadyAsync(context, enrollment, ambientTenant, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "read")}(@ambient)::text";
        command.Parameters.Add(new NpgsqlParameter("ambient", NpgsqlDbType.Uuid) { Value = ambientTenant });
        var result = await command.ExecuteScalarAsync(token);
        var state = result is string json ? JsonSerializer.Deserialize<StorageConfigurationState>(json) : null;
        if (state is null) throw new UnauthorizedAccessException("Configuration source is absent.");
        state.RequireShape(ambientTenant);
        RequireContext(context);
        return state;
    }

    internal static async Task<long> WriteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        StorageSettingsMutation mutation, CancellationToken token)
    {
        RequireContext(context);
        mutation.RequireActive();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction is null || transaction.IsolationLevel != IsolationLevel.Serializable
            || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())
            || mutation.TenantId != context.TenantFilterTenantId || mutation.OccurredAt.Kind != DateTimeKind.Utc
            || mutation.OccurredAt.Ticks % 10 != 0)
            throw new UnauthorizedAccessException("Configuration requires its original represented Serializable attempt.");
        await RequireReadyAsync(context, enrollment, mutation.Source.AmbientTenant, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {FunctionSql(context, "write")}(@ambient,@actor,@before,@effects,@occurred)";
        command.Parameters.Add(new NpgsqlParameter("ambient", NpgsqlDbType.Uuid) { Value = mutation.Source.AmbientTenant });
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = mutation.Actor });
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(mutation.Before) });
        command.Parameters.Add(new NpgsqlParameter("effects", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(mutation.Effects) });
        command.Parameters.Add(new NpgsqlParameter("occurred", NpgsqlDbType.TimestampTz) { Value = mutation.OccurredAt });
        var result = await command.ExecuteScalarAsync(token);
        RequireContext(context);
        if (!ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction()) || result is not long revision)
            throw new UnauthorizedAccessException("The original native configuration attempt changed.");
        return revision;
    }

    private static void RequireContext(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Configuration requires its retained ordinary native owner.");
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
