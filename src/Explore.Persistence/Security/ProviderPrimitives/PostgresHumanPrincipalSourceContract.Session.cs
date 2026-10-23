using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanPrincipalSourceContract
{
    private const string Session = "human_principal_source_session";
    private const string SessionSignature = "uuid,text,uuid";
    private const string SessionArguments = "p_actor uuid,p_stamp text,p_tenant uuid";

    /// <summary>
    /// Revocation facts for an already signature-validated Local session, on its selected
    /// physical Runtime owner. No human subject, lifetime, proof or Identity entity is issued.
    /// A null tenant is retained for instance authentication, never converted to a bypass.
    /// </summary>
    internal static async Task<JsonElement> ReadSessionAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, LocalSessionAuthority authority, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(authority);
        enrollment.Validate();
        if (!context.Database.IsNpgsql() || context.IsTenantFilterBypassed
            || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("local_session_requires_original_runtime_owner");
        var tenant = context.BindTenantLease();
        var lease = context.ContextId;
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        void RequireOwner()
        {
            context.ValidateTenantLease();
            if (context.ContextId != lease || context.IsTenantFilterBypassed
                || context.TenantFilterTenantId != tenant
                || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !ReferenceEquals(transaction.Connection, connection))
                throw new InvalidOperationException("local_session_original_owner_changed");
        }
        RequireOwner();
        bool opened = connection.State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
            command.CommandText = AdmissionSql(context, enrollment);
            await command.ExecuteNonQueryAsync(token);
            RequireOwner();
            command.CommandText = $"SELECT {new HumanPrincipalSourceSql(context).Name(Session)}(@actor,@stamp,@tenant)";
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = authority.LocalSubjectId });
            command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlDbType.Text) { Value = authority.SecurityStamp });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
            using var image = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(token)
                ?? throw new InvalidOperationException("local_session_native_result_missing")));
            command.Parameters.Clear();
            command.CommandText = AdmissionSql(context, enrollment);
            await command.ExecuteNonQueryAsync(token);
            RequireOwner();
            token.ThrowIfCancellationRequested();
            return image.RootElement.Clone();
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }

    private static string SessionBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Aggregate(string query) =>
            $"(SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(facts_row)), '[]'::jsonb) FROM ({Native(query)}) facts_row)";
        string caller = $"""
            {PostgresPrivilegeGuard.RestrictedCallerSql}
            IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.RuntimeLogin)}
                OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
                OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                OR p_stamp IS NULL OR length(p_stamp) = 0 OR length(p_stamp) > 256
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                        AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
            THEN RAISE EXCEPTION 'local_session_caller_mismatch' USING ERRCODE = '42501'; END IF;
            """;
        string local = string.Join("\n", sql.LocalQueries.Select((query, index) =>
            $"result := result || pg_catalog.jsonb_build_object('l{index}', {Aggregate(query)});"));
        return $"""
            DECLARE result jsonb := pg_catalog.jsonb_build_object();
                p_provider integer := {(int)AuthenticationProviderKind.Local}; p_account text := p_actor::text;
            BEGIN
                {Guard(context, enrollment)}
                {caller}
                result := pg_catalog.jsonb_build_object(
                    'q0', {Aggregate(sql.Queries[0])}, 'q1', {Aggregate(sql.Queries[1])});
                {local}
                {caller}
                {Guard(context, enrollment)}
                RETURN result;
            END;
            """;
    }
}
