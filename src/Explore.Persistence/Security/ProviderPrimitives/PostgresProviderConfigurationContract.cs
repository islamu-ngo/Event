using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Provider-only data capability. Native enrollment is neither human nor setup authority.</summary>
public static partial class PostgresProviderConfigurationContract
{
    public const string OwnerRole = "event_provider_configuration_owner";
    public const string MigratorRole = "event_provider_configuration_migrator";
    public const string GlobalRole = "event_provider_configuration_global";
    public const string SourceTable = "provider_configuration_source";
    private const string Prefix = "provider_configuration_";
    private const string Stamp = "provider-configuration:v1:";
    private const string Policy = "provider_configuration_sources";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, GlobalRole];
    private static readonly Type[] Sources =
    [
        typeof(SystemSetting), typeof(TenantSetting), typeof(SecretBinding), typeof(InstanceBootstrapState),
        typeof(User), typeof(UserExternalLogin), typeof(EventParticipationConfiguration), typeof(Explore.Domain.Event)
    ];
    private static readonly string[] RowColumns =
    [
        "Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked", "AllowedValues", "Description",
        "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly string[] Updates =
        ["Value", "SettingValueTypeId", "IsLocked", "AllowedValues", "Description", "Category", "DisplayOrder", "UpdatedAt", "UpdatedBy"];
    private static readonly (string Name, string Signature, string Result)[] Routines =
    [
        ("read", "uuid", "jsonb"), ("acquire", "uuid,jsonb", "int8"), ("changed", "", "trigger"),
        ("authentication_human", "uuid,jsonb,jsonb,timestamp with time zone", "void"),
        ("authentication_setup", "jsonb,jsonb,timestamp with time zone", "void"),
        ("authorization_human", "uuid,jsonb,jsonb,timestamp with time zone", "void"),
        ("authorization_setup", "jsonb,jsonb,timestamp with time zone", "void")
    ];

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);

    private static string[] Properties(ExploreDbContext context, Type type) => type == typeof(User)
        ? ["Id", "IsDeleted"] : type == typeof(Explore.Domain.Event)
        ? ["Id", "TenantId", "IsDeleted"] : type == typeof(EventParticipationConfiguration)
        ? ["Id", "TenantId", "IsDeleted", "IdentityAccessModeId"]
        : context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("provider_configuration_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(token);
        if (owned is not null) await owned.CommitAsync(token);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Provider admission requires its original open native connection.");
        var sql = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"""
            DO $admission$ BEGIN
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
                IF session_user = {Text(enrollment.GlobalLogin)} THEN
                    PERFORM {FunctionSql(context, "read")}(NULL::uuid);
                ELSIF session_user = {Text(enrollment.MigratorLogin)} AND current_user = session_user
                    AND pg_catalog.current_setting('role') IN ('none',session_user::text) THEN
                    IF NOT EXISTS (SELECT 1 FROM {sql.Name(SourceTable)} WHERE id = 1)
                    THEN RAISE EXCEPTION 'provider_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                ELSE RAISE EXCEPTION 'provider_configuration_caller' USING ERRCODE = '42501'; END IF;
            END $admission$;
            """;
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<ProviderConfigurationState> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Guid? actor, CancellationToken token)
    {
        RequireContext(context);
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "read")}(@actor)::text";
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = (object?)actor ?? DBNull.Value });
        string json = (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The original provider source is absent."));
        var state = JsonSerializer.Deserialize<ProviderConfigurationState>(json)
            ?? throw new UnauthorizedAccessException("The complete provider source is absent.");
        state.RequireShape();
        RequireContext(context);
        return state with { NativeImage = json };
    }

    internal static async Task AcquireAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid? actor, ProviderConfigurationState before, CancellationToken token)
    {
        RequireAttempt(context);
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "acquire")}(@actor,@before)";
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = (object?)actor ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Jsonb)
        { Value = before.NativeImage ?? throw new UnauthorizedAccessException("Native provider observation is absent.") });
        await command.ExecuteScalarAsync(token);
        RequireAttempt(context);
    }

    internal static async Task WriteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        ProviderConfigurationMutation mutation, CancellationToken token)
    {
        mutation.RequireActive();
        RequireAttempt(context);
        if (mutation.OccurredAt.Kind != DateTimeKind.Utc || mutation.OccurredAt.Ticks % 10 != 0
            || mutation.Effects.Any(row => row.UpdatedAt != mutation.OccurredAt))
            throw new UnauthorizedAccessException("Provider data time must be represented before freezing.");
        await RequireReadyAsync(context, enrollment, token);
        string operation = (mutation.IsAuthentication ? "authentication_" : "authorization_")
            + (mutation.Source is IProviderConfigurationSetupSource ? "setup" : "human");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, operation)}({(mutation.IsSetup ? "" : "@actor,")}@before,@effects,@occurred)";
        if (!mutation.IsSetup)
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = mutation.Actor!.Value });
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Jsonb)
        { Value = mutation.Before.NativeImage ?? throw new UnauthorizedAccessException() });
        command.Parameters.Add(new NpgsqlParameter("effects", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(mutation.Effects) });
        command.Parameters.Add(new NpgsqlParameter("occurred", NpgsqlDbType.TimestampTz) { Value = mutation.OccurredAt });
        await command.ExecuteNonQueryAsync(token);
        RequireAttempt(context);
    }

    private static void RequireAttempt(ExploreDbContext context)
    {
        RequireContext(context);
        if (context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !ReferenceEquals(transaction.Connection, context.Database.GetDbConnection()))
            throw new UnauthorizedAccessException("Provider effects require their original Serializable attempt.");
    }

    private static void RequireContext(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open
            || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Provider custody requires ordinary Global selection.");
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
