using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Ordinary Settings data custody for the enrolled Standard Runtime/Global service.
/// The managed original issuer remains the API human admission boundary.
/// </summary>
public static partial class PostgresOrdinarySettingsContract
{
    public const string OwnerRole = "event_ordinary_settings_owner";
    public const string MigratorRole = "event_ordinary_settings_migrator";
    public const string RuntimeRole = "event_ordinary_settings_runtime";
    public const string GlobalRole = "event_ordinary_settings_global";
    public const string SourceTable = "ordinary_settings_source";
    public const string AttemptTable = "ordinary_settings_attempt";
    private const string Prefix = "ordinary_settings_";
    private const string Policy = "ordinary_settings_owner";
    private const string Stamp = "ordinary-settings:v1:";
    private static readonly JsonSerializerOptions Json = new() { Converters = { new NativeInstantConverter() } };
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];
    private static readonly Type[] Sources = [typeof(SystemSetting), typeof(TenantSetting), typeof(UserPreference)];
    private static readonly (string Name, string Signature, string Arguments, string Result)[] Routines =
    [
        ("read", "uuid,uuid,jsonb", "p_tenant uuid,p_user uuid,p_keys jsonb", "jsonb"),
        ("acquire", "uuid,uuid,jsonb,jsonb", "p_tenant uuid,p_user uuid,p_request jsonb,p_before jsonb", "void"),
        ("write", "text,text,integer,jsonb,jsonb", "p_group text,p_key text,p_operation integer,p_before jsonb,p_after jsonb", "jsonb"),
        ("verify", "jsonb", "p_expected jsonb", "void"),
        ("complete", "jsonb", "p_expected jsonb", "void"),
        ("changed", "", "", "trigger"),
        ("row", "", "", "trigger"),
        ("tenant_changed", "", "", "trigger"),
        ("tenant_truncated", "", "", "trigger"),
        ("settlement", "", "", "trigger")
    ];

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("ordinary_settings_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = Command(context);
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Ordinary readiness requires the original open PostgreSQL backend.");
        await using var command = Command(context);
        string tenant = context.TenantFilterTenantId is { } selected ? Text(selected.ToString()) + "::uuid" : "NULL::uuid";
        command.CommandText = $"""
            DO $ready$ BEGIN
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
                IF session_user IN ({Text(enrollment.RuntimeLogin)},{Text(enrollment.GlobalLogin)}) THEN
                    PERFORM {FunctionSql(context, "read")}({tenant},NULL::uuid,'[]'::jsonb);
                ELSIF session_user = {Text(enrollment.MigratorLogin)} AND current_user = session_user
                    AND pg_catalog.current_setting('role') IN ('none',session_user::text) THEN
                    IF NOT EXISTS (SELECT 1 FROM {new HumanPrincipalSourceSql(context).Name(SourceTable)}
                        WHERE id = 1 AND revision >= 0 AND tenant_revision >= 0)
                    THEN RAISE EXCEPTION 'ordinary_settings_anchor_missing' USING ERRCODE = '55000'; END IF;
                ELSE RAISE EXCEPTION 'ordinary_settings_readiness_caller' USING ERRCODE = '42501'; END IF;
            END $ready$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal interface IObservation : IDisposable
    {
        OrdinarySettingsDataImage Image { get; }
        ImmutableDictionary<string, Guid> NewIds { get; }
        DateTime OccurredAt { get; }
    }

    internal interface IAttempt;

    internal sealed record RowEffect<T>(T? Before, T? After) where T : class;

    internal static async Task<IObservation> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        ExploreDbContextExecutionLease selection, OrdinarySettingsMutation mutation, HumanCommitSubject subject,
        TimeProvider clock, CancellationToken token)
    {
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("The original ordinary observation must precede BEGIN.");
        var source = new Source(context, enrollment, selection, mutation, subject, clock);
        try
        {
            source.Require(false);
            await RequireReadyAsync(context, enrollment, token);
            source.Require(false);
            await using var command = Command(context);
            command.CommandText = $"SELECT {FunctionSql(context, "read")}(@tenant,@user,@keys)::text";
            Add(command, "tenant", NpgsqlDbType.Uuid, source.Tenant);
            Add(command, "user", NpgsqlDbType.Uuid, source.User);
            Add(command, "keys", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(source.Keys, Json));
            source.NativeBefore = (string)(await command.ExecuteScalarAsync(token)
                ?? throw new UnauthorizedAccessException("The complete original ordinary source is missing."));
            using var document = JsonDocument.Parse(source.NativeBefore);
            source.Image = document.RootElement.GetProperty("Image").Deserialize<OrdinarySettingsDataImage>()
                ?? throw new UnauthorizedAccessException("The complete original ordinary image is missing.");
            source.Require(false);
            return source;
        }
        catch { source.Dispose(); throw; }
    }

    internal static async Task<IAttempt> AcquireAsync(IObservation observation, CancellationToken token)
    {
        var source = observation as Source ?? throw new UnauthorizedAccessException("A native observation must be genuine.");
        try
        {
            if (source.Acquired) throw new UnauthorizedAccessException("An ordinary observation belongs to one attempt.");
            source.Require(true);
            source.Transaction = source.Context.Database.CurrentTransaction!.GetDbTransaction();
            await RequireReadyAsync(source.Context, source.Enrollment, token);
            source.Require(true);
            await using var command = Command(source.Context);
            command.CommandText = $"SELECT {FunctionSql(source.Context, "acquire")}(@tenant,@user,@request,@before)";
            Add(command, "tenant", NpgsqlDbType.Uuid, source.Tenant);
            Add(command, "user", NpgsqlDbType.Uuid, source.User);
            Add(command, "request", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new
            {
                source.Keys,
                Effects = source.Mutation.Effects.Where(effect => !AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key)),
                source.NewIds,
                Actor = source.Subject.PrincipalId,
                source.OccurredAt,
                source.Mutation.RequiresTenantVerificationCeiling
            }, Json));
            Add(command, "before", NpgsqlDbType.Jsonb, source.NativeBefore);
            await command.ExecuteNonQueryAsync(token);
            source.Acquired = true;
            source.Require(true);
            return source;
        }
        catch { source.Dispose(); throw; }
    }

    internal static Task<RowEffect<OrdinarySystemSettingImage>> WriteSystemAsync(IAttempt attempt, string key,
        HumanSettingsMutationKind kind, OrdinarySystemSettingImage? before, OrdinarySystemSettingImage? after,
        CancellationToken token) => WriteAsync(attempt, "System", key, (int)kind, before, after, token);

    internal static Task<RowEffect<OrdinaryTenantSettingImage>> WriteTenantAsync(IAttempt attempt, string key,
        HumanSettingsMutationKind kind, OrdinaryTenantSettingImage? before, OrdinaryTenantSettingImage? after,
        CancellationToken token) => WriteAsync(attempt, "Tenant", key, (int)kind, before, after, token);

    internal static Task<RowEffect<OrdinaryUserPreferenceImage>> WriteUserAsync(IAttempt attempt, string key,
        OrdinaryUserPreferenceWrite operation, OrdinaryUserPreferenceImage? before, OrdinaryUserPreferenceImage? after,
        CancellationToken token) => WriteAsync(attempt, "User", key, (int)operation, before, after, token);

    private static async Task<RowEffect<T>> WriteAsync<T>(IAttempt attempt, string group, string key, int operation,
        T? before, T? after, CancellationToken token) where T : class
    {
        var source = RequireAttempt(attempt);
        try
        {
            await RequireReadyAsync(source.Context, source.Enrollment, token);
            source.Require(true);
            await using var command = Command(source.Context);
            command.CommandText = $"SELECT {FunctionSql(source.Context, "write")}(@group,@key,@operation,@before,@after)::text";
            Add(command, "group", NpgsqlDbType.Text, group);
            Add(command, "key", NpgsqlDbType.Text, key);
            Add(command, "operation", NpgsqlDbType.Integer, operation);
            Add(command, "before", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(before, Json));
            Add(command, "after", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(after, Json));
            string result = (string)(await command.ExecuteScalarAsync(token)
                ?? throw new UnauthorizedAccessException("Native ordinary completion is absent."));
            var actual = JsonSerializer.Deserialize<RowEffect<T>>(result)
                ?? throw new UnauthorizedAccessException("The actual native transition is absent.");
            if (actual != new RowEffect<T>(before, after))
                throw new UnauthorizedAccessException("The actual native transition differs from the canonical effect.");
            source.Require(true);
            return actual;
        }
        catch { source.Dispose(); throw; }
    }

    internal static Task VerifyAsync(IAttempt attempt, OrdinarySettingsDataImage expectedImage, CancellationToken token) =>
        CheckImageAsync(attempt, expectedImage, false, token);

    internal static Task RequireCompleteAsync(IAttempt attempt, OrdinarySettingsDataImage expectedImage, CancellationToken token) =>
        CheckImageAsync(attempt, expectedImage, true, token);

    private static async Task CheckImageAsync(IAttempt attempt, OrdinarySettingsDataImage expectedImage,
        bool terminal, CancellationToken token)
    {
        var source = RequireAttempt(attempt);
        try
        {
            await RequireReadyAsync(source.Context, source.Enrollment, token);
            source.Require(true);
            await using var command = Command(source.Context);
            command.CommandText = $"SELECT {FunctionSql(source.Context, terminal ? "complete" : "verify")}(@expected)";
            Add(command, "expected", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(expectedImage, Json));
            await command.ExecuteNonQueryAsync(token);
            source.Require(true);
        }
        catch { source.Dispose(); throw; }
    }

    private static Source RequireAttempt(IAttempt attempt)
    {
        var source = attempt as Source ?? throw new UnauthorizedAccessException("The ordinary native attempt is foreign.");
        if (!source.Acquired) throw new UnauthorizedAccessException("The original native attempt has not been acquired.");
        source.Require(true);
        return source;
    }

    private sealed class Source : IObservation, IAttempt
    {
        private readonly ExploreDbContextExecutionLease selection;
        private readonly DbContextId contextId;
        private readonly NpgsqlConnection connection;
        private readonly int backend;
        private readonly string schema;
        private bool expired;
        internal Source(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
            ExploreDbContextExecutionLease selection, OrdinarySettingsMutation mutation, HumanCommitSubject subject,
            TimeProvider clock)
        {
            Context = context; Enrollment = enrollment; this.selection = selection; Mutation = mutation; Subject = subject;
            contextId = context.ContextId;
            connection = context.Database.GetDbConnection() as NpgsqlConnection
                ?? throw new UnauthorizedAccessException("The original ordinary backend is not PostgreSQL.");
            if (connection.State != ConnectionState.Open)
                throw new UnauthorizedAccessException("The original ordinary connection must already be open.");
            backend = connection.ProcessID;
            schema = new HumanPrincipalSourceSql(context).Schema;
            Tenant = selection.SelectedTenantId;
            User = mutation.Effects.Any(effect => effect.Scope == SettingScope.User) ? subject.PrincipalId : null;
            Keys = OrdinarySettingsDataImage.Keys(mutation);
            NewIds = mutation.Effects.Where(effect => !AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key))
                .ToImmutableDictionary(effect => effect.Key, _ => Guid.CreateVersion7(), StringComparer.Ordinal);
            var instant = clock.GetUtcNow().UtcDateTime;
            OccurredAt = instant.AddTicks(-(instant.Ticks % 10));
            connection.StateChange += Changed;
        }
        internal ExploreDbContext Context { get; }
        internal PostgresNativeQueueEnrollment Enrollment { get; }
        internal OrdinarySettingsMutation Mutation { get; }
        internal HumanCommitSubject Subject { get; }
        internal Guid? Tenant { get; }
        internal Guid? User { get; }
        internal string[] Keys { get; }
        internal string NativeBefore { get; set; } = "";
        internal DbTransaction? Transaction { get; set; }
        internal bool Acquired { get; set; }
        public OrdinarySettingsDataImage Image { get; internal set; } = null!;
        public ImmutableDictionary<string, Guid> NewIds { get; }
        public DateTime OccurredAt { get; }
        internal void Require(bool transaction)
        {
            try { RequireSelection(transaction); }
            catch { Dispose(); throw; }
        }
        private void RequireSelection(bool transaction)
        {
            selection.RequireOriginalOwner(Context);
            if (expired || Context.ContextId != contextId || connection.State != ConnectionState.Open
                || connection.ProcessID != backend || !ReferenceEquals(Context.Database.GetDbConnection(), connection)
                || new HumanPrincipalSourceSql(Context).Schema != schema || Context.TenantFilterTenantId != Tenant
                || Context.IsTenantFilterBypassed || Context.IsConfigurationInstanceImportOwner
                || !Context.HasHumanCommitContextLease || !ReferenceEquals(Context.HumanCommitAuditSubject, Subject)
                || !ReferenceEquals(selection.HumanScope?.Subject, Subject)
                || (selection.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
                || selection.HumanScope?.Purpose != (Tenant.HasValue
                    ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance)
                || Mutation.Effects.Any(effect => effect.TenantId != Tenant)
                || System.Transactions.Transaction.Current is not null)
                throw new UnauthorizedAccessException("The original ordinary selection changed.");
            var current = Context.Database.CurrentTransaction?.GetDbTransaction();
            if (transaction
                ? current is null || !Context.HumanCommitAttemptOwnsTransaction
                    || current.IsolationLevel != IsolationLevel.Serializable || !ReferenceEquals(current.Connection, connection)
                    || Transaction is not null && !ReferenceEquals(current, Transaction)
                : current is not null)
                throw new UnauthorizedAccessException("The original ordinary physical owner changed.");
        }
        private void Changed(object? sender, StateChangeEventArgs args)
        {
            if (args.CurrentState is ConnectionState.Closed or ConnectionState.Broken) expired = true;
        }
        public void Dispose() { expired = true; connection.StateChange -= Changed; }
    }

    private static DbCommand Command(ExploreDbContext context)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }
    private static void Add(DbCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class NativeInstantConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTime();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            if (value.Kind != DateTimeKind.Utc || value.Ticks % 10 != 0)
                throw new UnauthorizedAccessException("Ordinary native instants must already be representable UTC microseconds.");
            writer.WriteStringValue(value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
