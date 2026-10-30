using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only refund fleet observation. The nonlogin owner reads only status
/// and time columns; neither it nor its executor can read private refund identities.
/// Installation consumes immutable deployment enrollment, never catalog-derived enrollment.
/// </summary>
public static class PostgresRefundReconciliationHealthContract
{
    public const string OwnerRole = "refund_health_owner";
    public const string MigratorRole = "refund_health_migrator";
    public const string ExecutorRole = "refund_health_executor";
    public const string PolicyName = "refund_health_observation";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(RefundAttempt))!.GetTableName()! + "_health_snapshot", Schema(context));

    internal static Task<RefundReconciliationHealth> ObserveAsync(
        ExploreDbContext context, DateTime observedAt, CancellationToken cancellationToken) =>
        context.Database.SqlQueryRaw<RefundReconciliationHealth>(
            $$"""
            SELECT "Pending" AS pending, "Unknown" AS unknown,
                "RequiresAction" AS requires_action, "Failed" AS failed,
                "CampaignsRequiringOperator" AS campaigns_requiring_operator,
                "DisputesDueSoon" AS disputes_due_soon,
                "DisputesDueWithin72Hours" AS disputes_due_within72hours,
                "DisputesOverdue" AS disputes_overdue,
                "OldestNonTerminalAt" AS oldest_non_terminal_at
            FROM {{FunctionSql(context)}}({0})
            """, observedAt).SingleAsync(cancellationToken);

    /// <summary>Called by migration authority after the model's forced-RLS installation.</summary>
    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = context.GetService<ISqlGenerationHelper>();
        string schemaName = Schema(context);
        string schema = sql.DelimitIdentifier(schemaName);
        string function = FunctionSql(context);
        string signature = function + "(timestamp with time zone)";
        (Type Type, string[] Properties)[] sources =
        [
            (typeof(RefundAttempt), [nameof(RefundAttempt.Status), nameof(RefundAttempt.LastObservedAt), nameof(RefundAttempt.UpdatedAt)]),
            (typeof(RefundCampaign), [nameof(RefundCampaign.Status)]),
            (typeof(PaymentDispute), [nameof(PaymentDispute.Status), nameof(PaymentDispute.ResponseDueAt)])
        ];
        var relations = sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string mappedSchema = context.GetService<IDbContextOptions>()
                .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            if (mappedSchema != schemaName)
                throw new InvalidOperationException("refund_health_sources_require_one_schema");
            return new PostgresPrivilegeGuard.Relation(entity.GetTableName()!, OwnerRole, [],
                source.Properties.Select(property => new PostgresPrivilegeGuard.Column(
                    ColumnName(context, source.Type, property), ["SELECT"])).ToArray());
        }).ToArray();
        var contract = new PostgresPrivilegeGuard.Contract(schemaName,
            [OwnerRole, MigratorRole, ExecutorRole],
            [new(context.Model.FindEntityType(typeof(RefundAttempt))!.GetTableName()! + "_health_snapshot",
                "timestamp with time zone", OwnerRole, [ExecutorRole])],
            relations,
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(ExecutorRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string tables = string.Join(", ", sources.Select(source => Text(Table(context, source.Type)) + "::regclass"));
        // A permissive true policy supplies fleet coverage, but an applicable
        // restrictive policy can silently hide work. Both shape and applicability
        // must therefore be checked before aggregates, even on an empty fleet.
        string observationGuard = $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                WHERE role.rolname = {Text(enrollment.GlobalLogin)} AND role.rolcanlogin
                    AND NOT role.rolsuper AND NOT role.rolbypassrls AND NOT role.rolcreatedb
                    AND NOT role.rolcreaterole AND NOT role.rolreplication)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                    WHERE (pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'SET'))
                        AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                            OR role.rolcreaterole OR role.rolreplication))
                OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)}, {Text(schemaName)}, 'CREATE')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                    LEFT JOIN pg_catalog.pg_policy policy
                        ON policy.polrelid = relation.oid AND policy.polname = '{PolicyName}'
                    WHERE relation.oid IN ({tables}) AND (
                        NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                        OR policy.oid IS NULL OR policy.polcmd <> 'r' OR NOT policy.polpermissive
                        OR policy.polroles IS DISTINCT FROM ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        OR pg_catalog.pg_get_expr(policy.polqual, policy.polrelid) IS DISTINCT FROM 'true'
                        OR policy.polwithcheck IS NOT NULL
                        OR (pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql(Text(enrollment.GlobalLogin), "relation.oid", false)})
                        OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other
                            WHERE other.polrelid = relation.oid AND other.polname <> '{PolicyName}'
                                AND other.polcmd IN ('r', '*')
                                AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                                    WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE'))
                                AND (NOT other.polpermissive OR other.polname <> 'tenant_isolation'))))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                    WHERE routine.oid = pg_catalog.to_regprocedure({Text(signature)})
                        AND routine.provolatile = 's') THEN
                RAISE EXCEPTION 'refund_health_observation_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        var installation = new FleetHealthInstallationSql(
            contract, enrollment, OwnerRole, MigratorRole, ExecutorRole, PolicyName, "refund_health");
        string grants = string.Join("\n", relations.Select(relation => FleetHealthInstallationSql.ReadPolicy(
            sql.DelimitIdentifier(relation.Name, schemaName),
            relation.Columns.Select(column => sql.DelimitIdentifier(column.Name)), OwnerRole, PolicyName)));
        string attempts = Table(context, typeof(RefundAttempt));
        string campaigns = Table(context, typeof(RefundCampaign));
        string disputes = Table(context, typeof(PaymentDispute));
        string status = sql.DelimitIdentifier(ColumnName(context, typeof(RefundAttempt), nameof(RefundAttempt.Status)));
        string lastObserved = sql.DelimitIdentifier(ColumnName(context, typeof(RefundAttempt), nameof(RefundAttempt.LastObservedAt)));
        string updated = sql.DelimitIdentifier(ColumnName(context, typeof(RefundAttempt), nameof(RefundAttempt.UpdatedAt)));
        string campaignStatus = sql.DelimitIdentifier(ColumnName(context, typeof(RefundCampaign), nameof(RefundCampaign.Status)));
        string disputeStatus = sql.DelimitIdentifier(ColumnName(context, typeof(PaymentDispute), nameof(PaymentDispute.Status)));
        string due = sql.DelimitIdentifier(ColumnName(context, typeof(PaymentDispute), nameof(PaymentDispute.ResponseDueAt)));
        string pending = $"{(int)RefundAttemptStatusEnum.Requested}, {(int)RefundAttemptStatusEnum.DispatchPending}, {(int)RefundAttemptStatusEnum.Pending}";
        return $"""
            DO $enrollment$
            DECLARE v_role text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR current_setting('role') NOT IN ('none', {Text(enrollment.MigratorLogin)})
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.GlobalLogin)} AND rolcanlogin
                            AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                            AND NOT rolcreaterole AND NOT rolreplication) THEN
                    RAISE EXCEPTION 'refund_health_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                {installation.Enroll(guard, observationGuard)}
            END $enrollment$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {ExecutorRole};
            {grants}
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_observed timestamp with time zone)
                RETURNS TABLE (
                    "Pending" integer, "Unknown" integer, "RequiresAction" integer, "Failed" integer,
                    "CampaignsRequiringOperator" integer, "DisputesDueSoon" integer,
                    "DisputesDueWithin72Hours" integer, "DisputesOverdue" integer,
                    "OldestNonTerminalAt" timestamp with time zone)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    {observationGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'refund_health_unbound_global_login_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_observed IS NULL OR NOT isfinite(p_observed) THEN
                        RAISE EXCEPTION 'invalid refund health observation' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT
                        count(*) FILTER (WHERE attempt.{status} IN ({pending}))::integer,
                        count(*) FILTER (WHERE attempt.{status} = {(int)RefundAttemptStatusEnum.Unknown})::integer,
                        count(*) FILTER (WHERE attempt.{status} = {(int)RefundAttemptStatusEnum.RequiresAction})::integer,
                        count(*) FILTER (WHERE attempt.{status} = {(int)RefundAttemptStatusEnum.Failed}
                            AND COALESCE(attempt.{updated}, attempt.{lastObserved}) >= p_observed - interval '24 hours')::integer,
                        (SELECT count(*)::integer FROM {campaigns} AS campaign
                            WHERE campaign.{campaignStatus} = {(int)RefundCampaignStatus.RequiresOperator}),
                        (SELECT count(*)::integer FROM {disputes} AS dispute
                            WHERE dispute.{disputeStatus} = {(int)PaymentDisputeStatus.Open}
                                AND dispute.{due} >= p_observed AND dispute.{due} <= p_observed + interval '24 hours'),
                        (SELECT count(*)::integer FROM {disputes} AS dispute
                            WHERE dispute.{disputeStatus} = {(int)PaymentDisputeStatus.Open}
                                AND dispute.{due} > p_observed + interval '24 hours' AND dispute.{due} <= p_observed + interval '72 hours'),
                        (SELECT count(*)::integer FROM {disputes} AS dispute
                            WHERE dispute.{disputeStatus} = {(int)PaymentDisputeStatus.Open} AND dispute.{due} < p_observed),
                        min(attempt.{lastObserved}) FILTER (WHERE attempt.{status} IN (
                            {pending}, {(int)RefundAttemptStatusEnum.RequiresAction}, {(int)RefundAttemptStatusEnum.Unknown}))
                    FROM {attempts} AS attempt;
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {ExecutorRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {ExecutorRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $verify$
            BEGIN
                {guard}
                {observationGuard}
            END $verify$;
            """;
    }

    private static string ColumnName(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Model.FindEntityType(type)!.GetTableName()!, Schema(context));

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(RefundAttempt))!.GetSchema() ?? "public";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
