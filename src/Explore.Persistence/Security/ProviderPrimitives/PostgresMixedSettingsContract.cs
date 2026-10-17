using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Independent mixed data capability on Global, not Email/provider/storage permission.
/// Every entry attests its original catalog, source exclusions, body and effective grants.
/// </summary>
public static partial class PostgresMixedSettingsContract
{
    public const string OwnerRole = "event_mixed_settings_owner";
    public const string MigratorRole = "event_mixed_settings_migrator";
    public const string GlobalRole = "event_mixed_settings_global";
    public const string SourceTable = "mixed_settings_source";
    private const string AttemptTable = "mixed_settings_attempt";
    private const string Prefix = "mixed_settings_";
    private const string Stamp = "mixed-settings:v1:";
    private const string Policy = "mixed_settings_source_read";
    private const string HumanSignature = "uuid,integer,text,uuid,text,uuid,uuid,jsonb";
    private const string HumanArguments = "p_actor uuid,p_provider integer,p_account text,p_tenant uuid,"
        + "p_stamp text,p_session uuid,p_resolved uuid,p_request jsonb";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, GlobalRole];
    private static readonly (string Name, string Signature, string Result)[] Routines =
    [
        ("ready", "uuid", "void"),
        ("data", "uuid,jsonb", "jsonb"),
        ("read", HumanSignature, "jsonb"),
        ("acquire", HumanSignature + ",jsonb", "jsonb"),
        ("compile", "uuid,jsonb,jsonb", "timestamptz"),
        ("email_consume", EmailConsumeSignature, "void"),
        ("write", "uuid,text,jsonb", "void"),
        ("complete", "uuid,jsonb", "void"),
        ("typed_data", "uuid,jsonb", "jsonb"),
        ("typed_read", HumanSignature, "jsonb"),
        ("typed_acquire", HumanSignature + ",jsonb", "jsonb"),
        ("typed_compile", "uuid,jsonb,jsonb", "timestamptz"),
        ("typed_write", "uuid,jsonb", "void"),
        ("typed_complete", "uuid,jsonb", "void"),
        ("plan_write", "uuid,jsonb", "void"),
        ("governance_write", "uuid,jsonb", "void"),
        ("changed", "", "trigger"),
        ("settlement", "", "trigger")
    ];

    internal static string FunctionSql(ExploreDbContext context, string name) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + name);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("mixed_settings_native_provider_not_supported");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Mixed readiness requires its original open PostgreSQL connection.");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        // Bind outside the anonymous block: PostgreSQL DO bodies cannot receive command parameters.
        command.CommandText = $"""
            SELECT {FunctionSql(context, "ready")}(@tenant)
                WHERE session_user <> {Text(enrollment.MigratorLogin)};
            DO $ready$ BEGIN
                IF session_user = {Text(enrollment.MigratorLogin)} THEN
                    {Guard(context, enrollment)}
                END IF;
                {ExactBodies(context, enrollment)}
            END $ready$;
            """;
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
        { Value = (object?)context.TenantFilterTenantId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<string> ReadHumanAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        UpdateSettingBatchCommand original, HumanCommitSubject subject, HumanAuthenticationLifetime lifetime,
        HumanSupportForwardingObservation forwarding, Guid? tenant, string? before, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, before is null ? "read" : "acquire")}"
            + $"(@actor,@provider,@account,@tenant,@stamp,@session,@resolved,@request{(before is null ? "" : ",@before")})::text";
        void Add(string name, NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        Add("actor", NpgsqlDbType.Uuid, subject.PrincipalId);
        Add("provider", NpgsqlDbType.Integer, (int)subject.AccountKey!.ProviderKind);
        Add("account", NpgsqlDbType.Text, subject.AccountKey.Value);
        Add("tenant", NpgsqlDbType.Uuid, tenant);
        Add("stamp", NpgsqlDbType.Text, lifetime.LocalSession?.SecurityStamp);
        Add("session", NpgsqlDbType.Uuid, forwarding.SessionId);
        Add("resolved", NpgsqlDbType.Uuid, forwarding.ResolvedTenantId);
        Add("request", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(original));
        if (before is not null) Add("before", NpgsqlDbType.Jsonb, before);
        return (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The original native mixed image is absent."));
    }

    internal static async Task<string> ReadDataAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, UpdateSettingBatchCommand original,
        Guid? tenant, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "data")}(@tenant,@request)::text";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(original) });
        return (string)(await command.ExecuteScalarAsync(token) ?? throw new UnauthorizedAccessException("Mixed data is absent."));
    }

    internal static async Task<DateTime> CompileAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid? tenant, UpdateSettingBatchCommand original, IReadOnlyDictionary<string, string> accepted, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "compile")}(@tenant,@request,@accepted)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(original) });
        command.Parameters.Add(new NpgsqlParameter("accepted", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(accepted) });
        return (DateTime)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The native mixed compilation time is absent."));
    }

    internal static async Task WriteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid? tenant, string slice, string changes, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "write")}(@tenant,@slice,@changes)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("slice", NpgsqlDbType.Text) { Value = slice });
        command.Parameters.Add(new NpgsqlParameter("changes", NpgsqlDbType.Jsonb) { Value = changes });
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task CompleteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid? tenant, string expected, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "complete")}(@tenant,@expected)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("expected", NpgsqlDbType.Jsonb) { Value = expected });
        await command.ExecuteNonQueryAsync(token);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Native(string value) => value.Replace("@actor", "p_actor", StringComparison.Ordinal)
        .Replace("@provider", "p_provider", StringComparison.Ordinal).Replace("@account", "p_account", StringComparison.Ordinal)
        .Replace("@tenant", "p_tenant", StringComparison.Ordinal).Replace("@stamp", "p_stamp", StringComparison.Ordinal)
        .Replace("@session", "p_session", StringComparison.Ordinal).Replace("@resolved", "p_resolved", StringComparison.Ordinal);
}
