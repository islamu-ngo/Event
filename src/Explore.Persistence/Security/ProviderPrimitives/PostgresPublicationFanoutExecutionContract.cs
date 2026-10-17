using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Publication fanout persistence on the admitted Runtime connection. Every operation retains
/// the original outbox tuple; recipient operations resolve current subscription authority in SQL.
/// </summary>
public static partial class PostgresPublicationFanoutExecutionContract
{
    public const string OwnerRole = "event_publication_fanout_execution_owner";
    public const string MigratorRole = "event_publication_fanout_execution_migrator";
    public const string RuntimeRole = "event_publication_fanout_execution_runtime";
    private const string Routine = "publication_fanout_execution";
    private const string Signature = "jsonb,text,jsonb";
    private const string Policy = "publication_fanout_execution";
    private const string PolicyStamp = "publication-fanout-execution:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    internal static bool Applies(ExploreDbContext context) =>
        context.Database.IsNpgsql() && context.NotificationOutboxExecutionClaim?.EventType
            == EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType;

    internal static async Task<T?> ReadAsync<T>(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        string operation, object request, CancellationToken token) where T : class
    {
        JsonElement result = await ExecuteAsync(context, enrollment, operation, request, token);
        return result.ValueKind == JsonValueKind.Null ? null
            : PublicEventDiscoveryProviderOperations.Materialize<T>(context, result);
    }

    internal static async Task<List<T>> ListAsync<T>(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        string operation, object request, CancellationToken token) where T : class =>
        (await ExecuteAsync(context, enrollment, operation, request, token)).EnumerateArray()
            .Select(row => PublicEventDiscoveryProviderOperations.Materialize<T>(context, row)).ToList();

    internal sealed record PreferenceRows(
        IReadOnlyList<NotificationPreferenceCategory> Categories,
        IReadOnlyList<NotificationPreferenceChannel> Channels,
        IReadOnlyList<NotificationChannelPreference> Preferences,
        IReadOnlyList<NotificationPreferenceProfile> Profiles);

    internal static async Task<PreferenceRows> ReadPreferencesAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        Guid tenantId, Guid userId, CancellationToken token)
    {
        JsonElement result = await ExecuteAsync(context, enrollment, "preferences", new { TenantId = tenantId, UserId = userId }, token);
        List<T> Rows<T>(string name) where T : class => result.GetProperty(name).EnumerateArray()
            .Select(row => PublicEventDiscoveryProviderOperations.Materialize<T>(context, row)).ToList();
        return new(Rows<NotificationPreferenceCategory>("Categories"), Rows<NotificationPreferenceChannel>("Channels"),
            Rows<NotificationChannelPreference>("Preferences"), Rows<NotificationPreferenceProfile>("Profiles"));
    }

    internal static async Task<JsonElement> ExecuteAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        string operation, object request, CancellationToken token)
    {
        if (enrollment is null)
            throw new UnauthorizedAccessException("publication_fanout_enrollment_required");
        context.ValidateTenantLease();
        var claim = context.NotificationOutboxExecutionClaim;
        if (claim is null || claim.EventType != EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType
            || claim.AggregateType != nameof(Event) || claim.AggregateId != claim.SourceId
            || context.IsTenantFilterBypassed || context.BindTenantLease() != claim.TenantId)
            throw new UnauthorizedAccessException("publication_fanout_original_scope_required");

        var definition = Define(context, enrollment);
        bool openedHere = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (openedHere) await context.Database.OpenConnectionAsync(token);
        try
        {
            var connection = context.Database.GetDbConnection();
            var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
            command.CommandText = $"""
                DO $ready$ BEGIN {definition.Guard} {ExactBody(definition)} END $ready$;
                SELECT {definition.Sql.Names.Name(Routine)}(@claim,@operation,@request)::text;
                """;
            command.Parameters.Add(new NpgsqlParameter("claim", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(claim) });
            command.Parameters.Add(new NpgsqlParameter("operation", NpgsqlDbType.Text) { Value = operation });
            command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request) });
            string result = await command.ExecuteScalarAsync(token) as string
                ?? throw new UnauthorizedAccessException("publication_fanout_original_source_unavailable");
            context.ValidateTenantLease();
            if (!ReferenceEquals(claim, context.NotificationOutboxExecutionClaim)
                || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction()))
                throw new UnauthorizedAccessException("publication_fanout_original_scope_replaced");
            using var document = JsonDocument.Parse(result);
            return document.RootElement.Clone();
        }
        finally
        {
            if (openedHere) await context.Database.CloseConnectionAsync();
        }
    }

    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(token);
        if (owned is not null) await owned.CommitAsync(token);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Boolean(bool value) => value ? "true" : "false";
}
