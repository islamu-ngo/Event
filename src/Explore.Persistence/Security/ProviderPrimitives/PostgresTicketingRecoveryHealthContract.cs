using Explore.Application.Contracts.Recovery;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only recovery fleet observation, enrolled only to the declared Global login.
/// The nonlogin owner has SELECT on predicate columns, never business identities or rows.
/// </summary>
public static class PostgresTicketingRecoveryHealthContract
{
    public const string OwnerRole = "ticketing_recovery_health_owner";
    public const string MigratorRole = "ticketing_recovery_health_migrator";
    public const string ExecutorRole = "ticketing_recovery_health_executor";
    public const string PolicyName = "ticketing_recovery_health_observation";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(TicketingRecoveryCheckpoint))!.GetTableName()! + "_health_snapshot",
            Schema(context));

    internal static Task<TicketingRecoveryAggregateHealth> ObserveAsync(
        ExploreDbContext context, CancellationToken cancellationToken) =>
        context.Database.SqlQueryRaw<TicketingRecoveryAggregateHealth>(
            $"""
            SELECT "RecoveryOnly" AS recovery_only, "Failed" AS failed,
                "PendingReissues" AS pending_reissues, "AmbiguousEffects" AS ambiguous_effects,
                "DeadLetteredEffects" AS dead_lettered_effects, "PoisonEffects" AS poison_effects,
                "OldestDueAt" AS oldest_due_at
            FROM {FunctionSql(context)}()
            """).SingleAsync(cancellationToken);

    /// <summary>Install under migration authority after model-derived forced RLS.</summary>
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
        string signature = function + "()";
        (Type Type, string[] Properties)[] sources =
        [
            (typeof(TicketingRecoveryCheckpoint), [nameof(TicketingRecoveryCheckpoint.Status)]),
            (typeof(TicketingRecoveryReissueIntent), [nameof(TicketingRecoveryReissueIntent.Status)]),
            (typeof(FairReturnOrchestrationEffect), [
                nameof(FairReturnOrchestrationEffect.StatusId),
                nameof(FairReturnOrchestrationEffect.LastFailureCode),
                nameof(FairReturnOrchestrationEffect.NextAttemptAt)])
        ];
        var relations = sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string mappedSchema = context.GetService<IDbContextOptions>()
                .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            if (mappedSchema != schemaName)
                throw new InvalidOperationException("ticketing_recovery_health_sources_require_one_schema");
            return new PostgresPrivilegeGuard.Relation(entity.GetTableName()!, OwnerRole, [],
                source.Properties.Select(property => new PostgresPrivilegeGuard.Column(
                    ColumnName(context, source.Type, property), ["SELECT"])).ToArray());
        }).ToArray();
        var contract = new PostgresPrivilegeGuard.Contract(schemaName,
            [OwnerRole, MigratorRole, ExecutorRole],
            [new(context.Model.FindEntityType(typeof(TicketingRecoveryCheckpoint))!.GetTableName()! + "_health_snapshot",
                "", OwnerRole, [ExecutorRole])],
            relations,
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(ExecutorRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string tables = string.Join(", ", sources.Select(source => Text(Table(context, source.Type)) + "::regclass"));
        // This part is also valid before first grants: no policy or routine may
        // compensate for an unsafe Global session or an unprotected source table.
        string sourceGuard = $"""
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
                    WHERE relation.oid IN ({tables}) AND (
                        NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                        OR pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES'))) THEN
                RAISE EXCEPTION 'ticketing_recovery_health_sources_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        // A restrictive policy could hide work even beside our permissive policy.
        // Check applicability and exact shape before observing even an empty fleet.
        string observationGuard = $"""
            {sourceGuard}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                LEFT JOIN pg_catalog.pg_policy policy
                    ON policy.polrelid = relation.oid AND policy.polname = '{PolicyName}'
                WHERE relation.oid IN ({tables}) AND (
                    policy.oid IS NULL OR policy.polcmd <> 'r' OR NOT policy.polpermissive
                    OR policy.polroles IS DISTINCT FROM ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                    OR pg_catalog.pg_get_expr(policy.polqual, policy.polrelid) IS DISTINCT FROM 'true'
                    OR policy.polwithcheck IS NOT NULL
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other
                        WHERE other.polrelid = relation.oid AND other.polname <> '{PolicyName}'
                            AND other.polcmd IN ('r', '*')
                            AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                                WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE'))
                            AND (NOT other.polpermissive OR other.polname <> 'tenant_isolation'))))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                    WHERE routine.oid = pg_catalog.to_regprocedure({Text(signature)})
                        AND routine.provolatile = 's') THEN
                RAISE EXCEPTION 'ticketing_recovery_health_observation_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        string grants = string.Join("\n", relations.Select(relation =>
        {
            string table = sql.DelimitIdentifier(relation.Name, schemaName);
            return $"""
                GRANT SELECT ({string.Join(", ", relation.Columns.Select(column => sql.DelimitIdentifier(column.Name)))})
                    ON {table} TO {OwnerRole};
                DROP POLICY IF EXISTS "{PolicyName}" ON {table};
                CREATE POLICY "{PolicyName}" ON {table} FOR SELECT TO {OwnerRole} USING (true);
                """;
        }));
        string checkpoints = Table(context, typeof(TicketingRecoveryCheckpoint));
        string reissues = Table(context, typeof(TicketingRecoveryReissueIntent));
        string effects = Table(context, typeof(FairReturnOrchestrationEffect));
        string checkpointStatus = sql.DelimitIdentifier(ColumnName(context, typeof(TicketingRecoveryCheckpoint), nameof(TicketingRecoveryCheckpoint.Status)));
        string reissueStatus = sql.DelimitIdentifier(ColumnName(context, typeof(TicketingRecoveryReissueIntent), nameof(TicketingRecoveryReissueIntent.Status)));
        string effectStatus = sql.DelimitIdentifier(ColumnName(context, typeof(FairReturnOrchestrationEffect), nameof(FairReturnOrchestrationEffect.StatusId)));
        string failure = sql.DelimitIdentifier(ColumnName(context, typeof(FairReturnOrchestrationEffect), nameof(FairReturnOrchestrationEffect.LastFailureCode)));
        string due = sql.DelimitIdentifier(ColumnName(context, typeof(FairReturnOrchestrationEffect), nameof(FairReturnOrchestrationEffect.NextAttemptAt)));
        return $"""
            DO $enrollment$
            DECLARE v_role text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR current_setting('role') NOT IN ('none', {Text(enrollment.MigratorLogin)})
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin) THEN
                    RAISE EXCEPTION 'ticketing_recovery_health_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                {sourceGuard}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{ExecutorRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(schemaName)}
                            AND routine.proname = {Text(contract.Routines[0].Name)})
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                            JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                            WHERE namespace.nspname = {Text(schemaName)} AND policy.polname = '{PolicyName}') THEN
                        RAISE EXCEPTION 'ticketing_recovery_health_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{ExecutorRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {ExecutorRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                    -- Retain fresh creator ADMIN until all declared edges exist.
                    -- Then remove only the fresh owner/executor shortcuts.
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{ExecutorRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members membership
                            JOIN pg_catalog.pg_roles parent ON parent.oid = membership.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = membership.member
                            WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                    IF ({PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}) IS NOT TRUE THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                ELSE
                    -- Existing drift is rejected before grants, never normalized.
                    {guard}
                    {observationGuard}
                END IF;
            END $enrollment$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {ExecutorRole};
            {grants}
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}()
                RETURNS TABLE (
                    "RecoveryOnly" integer, "Failed" integer, "PendingReissues" integer,
                    "AmbiguousEffects" integer, "DeadLetteredEffects" integer, "PoisonEffects" integer,
                    "OldestDueAt" timestamp with time zone)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    {observationGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR current_setting('role') NOT IN ('none', {Text(enrollment.GlobalLogin)})
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'ticketing_recovery_health_unbound_global_login_required' USING ERRCODE = '42501';
                    END IF;
                    RETURN QUERY SELECT
                        (SELECT count(*)::integer FROM {checkpoints} AS checkpoint
                            WHERE checkpoint.{checkpointStatus} = {(int)TicketingRecoveryStatus.RecoveryOnly}),
                        (SELECT count(*)::integer FROM {checkpoints} AS checkpoint
                            WHERE checkpoint.{checkpointStatus} = {(int)TicketingRecoveryStatus.Failed}),
                        (SELECT count(*)::integer FROM {reissues} AS reissue
                            WHERE reissue.{reissueStatus} = {(int)TicketingRecoveryReissueStatus.Pending}),
                        count(*) FILTER (WHERE effect.{effectStatus} = {(int)FairReturnOrchestrationEffectStatus.Unknown})::integer,
                        count(*) FILTER (WHERE effect.{effectStatus} = {(int)FairReturnOrchestrationEffectStatus.DeadLettered})::integer,
                        count(*) FILTER (WHERE left(effect.{failure}, 7) = 'POISON_')::integer,
                        min(effect.{due}) FILTER (WHERE effect.{effectStatus} IN (
                            {(int)FairReturnOrchestrationEffectStatus.Pending}, {(int)FairReturnOrchestrationEffectStatus.Unknown}))
                    FROM {effects} AS effect;
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
        ?? context.Model.FindEntityType(typeof(TicketingRecoveryCheckpoint))!.GetSchema() ?? "public";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
