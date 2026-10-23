using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only public response freshness components. No source rows, identifiers,
/// address values, or caller-selected SQL cross this boundary.
/// </summary>
public static partial class PostgresPublicEventResponseClockContract
{
    public const string OwnerRole = "event_public_clock_owner";
    public const string MigratorRole = "event_public_clock_migrator";
    public const string RuntimeRole = "event_public_clock_runtime";
    private const string Routine = "public_event_response_clock";
    private const string Signature = "uuid,text,timestamp with time zone,timestamp with time zone,boolean,boolean";
    private const string Policy = "public_event_response_clock_read";
    private const string Stamp = "public-event-response-clock:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("public_clock_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    /// <summary>Attests rather than repairs the installed clock and its source privilege graph.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("public_clock_original_open_connection_required");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = AdmissionSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        context.ValidateTenantLease();
        var definition = Define(context, enrollment);
        string tenant = context.TenantFilterTenantId is { } selected ? Text(selected.ToString()) + "::uuid" : "NULL::uuid";
        return $"""
            DO $ready$ BEGIN
                {definition.Guard}
                {ExactBody(context, definition.Body)}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR {tenant} IS NULL OR {tenant} = '00000000-0000-0000-0000-000000000000'::uuid
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM {tenant}
                THEN RAISE EXCEPTION 'public_clock_caller_mismatch' USING ERRCODE = '42501'; END IF;
            END $ready$;
            """;
    }

    private static string ExactBody(ExploreDbContext context, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(" + Signature + ")")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND f.prorettype = 'timestamptz'::regtype AND NOT f.proretset
                AND f.proargdefaults IS NULL AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                AND f.proargnames = ARRAY['p_tenant','p_operation','p_now','p_threshold','p_address','p_coordinates']::text[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'public_clock_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
