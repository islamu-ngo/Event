using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Explore.Application.Features.Settings;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Execute-only ordinary Global custody for the original resource-provider command.</summary>
public static partial class PostgresEventResourceProviderContract
{
    public const string OwnerRole = "event_resource_provider_owner";
    public const string MigratorRole = "event_resource_provider_migrator";
    public const string GlobalRole = "event_resource_provider_global";
    public const string SourceTable = "event_resource_provider_source";
    public const string AttemptTable = "event_resource_provider_attempt";
    private const string Prefix = "event_resource_provider_";
    private const string Stamp = "event-resource-provider:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, GlobalRole];
    private static readonly Type[] Sources = [typeof(SystemSetting), typeof(EventResourceProviderActivation), typeof(TenantSetting)];
    private static readonly Type[] WriteSources = [typeof(SystemSetting), typeof(EventResourceProviderActivation)];
    private static readonly (string Name, string Signature, string Arguments, string Result)[] Routines =
    [
        ("read", "", "", "jsonb"),
        ("acquire", "jsonb,jsonb", "p_request jsonb,p_before jsonb", "void"),
        ("apply", "jsonb,jsonb", "p_request jsonb,p_expected jsonb", "void"),
        ("verify", "jsonb,jsonb", "p_request jsonb,p_expected jsonb", "void"),
        ("complete", "jsonb,jsonb", "p_request jsonb,p_expected jsonb", "void"),
        ("changed", "", "", "trigger"), ("row", "", "", "trigger"), ("settlement", "", "", "trigger")
    ];

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("resource_provider_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = Command(context);
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static string SourceTriggerPredicate(ExploreDbContext context, Type sourceType)
    {
        if (!Sources.Contains(sourceType)) return "false";
        return string.Join(" OR ", Routines.Where(routine => routine.Name is "changed" or "row").Select(routine => $"""
            (t.tgrelid=pg_catalog.to_regclass({Text(new HumanPrincipalSourceSql(context).Table(sourceType))})
                AND t.tgname={Text(Prefix + routine.Name)}
                AND t.tgfoid=pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "()")}))
            """));
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        await using var command = Command(context);
        command.CommandText = $"DO $ready$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $ready$;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static Task<string> ObserveAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token) => ReadAsync(context, enrollment, token);

    internal static async Task<string> ReadDataAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token) => JsonNode.Parse(await ReadAsync(context, enrollment, token))!["Data"]!.ToJsonString();

    private static async Task<string> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        RequireGlobalContext(context);
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, "read")}()::text";
        return (string)(await command.ExecuteScalarAsync(token) ?? throw new UnauthorizedAccessException("Original resource-provider data is absent."));
    }

    internal static Task AcquireAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        EventResourceProviderOriginal original, string before, CancellationToken token) =>
        ExecuteAsync(context, enrollment, "acquire", original, "before", before, token);

    internal static Task ApplyOriginalAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        EventResourceProviderOriginal original, string expected, CancellationToken token) =>
        ExecuteAsync(context, enrollment, "apply", original, "expected", expected, token);

    internal static Task VerifyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        EventResourceProviderOriginal original, string expected, CancellationToken token) =>
        ExecuteAsync(context, enrollment, "verify", original, "expected", expected, token);

    internal static Task CompleteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        EventResourceProviderOriginal original, string expected, CancellationToken token) =>
        ExecuteAsync(context, enrollment, "complete", original, "expected", expected, token);

    private static async Task ExecuteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string action, EventResourceProviderOriginal original, string parameter, string image, CancellationToken token)
    {
        RequireGlobalContext(context);
        if (!context.HumanCommitAttemptOwnsTransaction
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("The original non-replaying Serializable owner is required.");
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, action)}(@request,@{parameter})";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = original.Serialize() });
        command.Parameters.Add(new NpgsqlParameter(parameter, NpgsqlDbType.Jsonb) { Value = image });
        await command.ExecuteNonQueryAsync(token);
    }

    private static DbCommand Command(ExploreDbContext context)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("An already-open resource-provider PostgreSQL connection is required.");
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private static void RequireGlobalContext(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Original resource-provider Global connection required.");
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
