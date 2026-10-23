using System.Data;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Dedicated source exclusion for the actual storage probe. No source-table read or write
/// grants are added: original readers, subjects and selected scopes remain authoritative.
/// </summary>
public static partial class PostgresStorageProbeSourceContract
{
    public const string OwnerRole = "event_storage_probe_source_owner";
    public const string MigratorRole = "event_storage_probe_source_migrator";
    public const string RuntimeRole = "event_storage_probe_source_runtime";
    public const string GlobalRole = "event_storage_probe_source_global";
    public const string SourceTable = "storage_probe_source";
    private const string Prefix = "storage_probe_source_";
    private const string Stamp = "storage-probe-source:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];
    // Deliberately whole-relation exclusion: every winner/absence, metadata field, rename,
    // replacement and bootstrap absence participates, including future storage keys.
    private static readonly Type[] Sources =
        [typeof(SystemSetting), typeof(TenantSetting), typeof(SecretBinding), typeof(InstanceBootstrapState)];
    private static readonly (string Operation, string Signature, string Result)[] Routines =
        [("observe", "uuid", "int8"), ("acquire", "uuid", "int8"), ("changed", "", "trigger")];

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("storage_probe_native_contract_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    /// <summary>Independent expected catalog/body admission. This never installs or repairs.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Storage probe admission requires an already-open native connection.");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"""
            DO $storage_probe_ready$ BEGIN
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
                IF session_user = {Text(enrollment.MigratorLogin)} AND current_user = session_user
                    AND pg_catalog.current_setting('role') IN ('none',session_user::text) THEN
                    {AnchorRow(context)}
                ELSE
                    PERFORM {FunctionSql(context, "observe")}(
                        NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid);
                END IF;
            END $storage_probe_ready$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
