using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Global exports bounded UUIDs through column-only observation; Runtime owns all redaction.</summary>
public static class PostgresEmailRetentionDiscoveryContract
{
    public const string OwnerRole = "email_retention_discovery_owner";
    public const string MigratorRole = "email_retention_discovery_migrator";
    public const string ExecutorRole = "email_retention_discovery_executor";
    public const string PolicyName = "email_retention_identity_discovery";
    private const string FunctionName = "email_retention_tenant_ids";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(FunctionName, Schema(context));

    /// <summary>Called only by the migration owner after forced RLS installation, never during acquisition.</summary>
    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("email_retention_discovery_requires_postgresql");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("email_retention_discovery_requires_postgresql");
        var sql = context.GetService<ISqlGenerationHelper>();
        string schemaName = Schema(context);
        string schema = sql.DelimitIdentifier(schemaName);
        string table = Table(context, typeof(EmailDispatchOutbox));
        string function = FunctionSql(context);
        string signature = function + "(timestamp with time zone,integer)";
        string[] properties =
        [
            nameof(EmailDispatchOutbox.TenantId), nameof(EmailDispatchOutbox.CreatedAt),
            nameof(EmailDispatchOutbox.Status), nameof(EmailDispatchOutbox.SentAt),
            nameof(EmailDispatchOutbox.LastFailureAt), nameof(EmailDispatchOutbox.ContentRedactedAt),
            nameof(EmailDispatchOutbox.IsDeleted)
        ];
        var entity = context.Model.FindEntityType(typeof(EmailDispatchOutbox))!;
        var relation = new PostgresPrivilegeGuard.Relation(entity.GetTableName()!, OwnerRole, [],
            properties.Select(property => new PostgresPrivilegeGuard.Column(Column(context, property), ["SELECT"])).ToArray());
        var contract = new PostgresPrivilegeGuard.Contract(schemaName,
            [OwnerRole, MigratorRole, ExecutorRole],
            [new(FunctionName, "timestamp with time zone,integer", OwnerRole, [ExecutorRole])],
            [relation],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(ExecutorRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        Type[] privateSources = [typeof(EmailDispatchOutbox), typeof(EmailDispatchAttempt),
            typeof(EmailDispatchReceipt), typeof(NotificationDelivery)];
        string privateTables = string.Join(", ", privateSources.Select(type => Text(Table(context, type)) + "::regclass"));
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
                    WHERE relation.oid IN ({privateTables}) AND (
                        NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                        OR pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES')))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                    WHERE policy.polrelid = {Text(table)}::regclass AND policy.polname = '{PolicyName}'
                        AND policy.polcmd = 'r' AND policy.polpermissive
                        AND policy.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(policy.polqual, policy.polrelid) = 'true'
                        AND policy.polwithcheck IS NULL)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other
                    WHERE other.polrelid = {Text(table)}::regclass AND other.polname <> '{PolicyName}'
                        AND other.polcmd IN ('r', '*')
                        AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE'))
                        AND (NOT other.polpermissive OR other.polname <> 'tenant_isolation'))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                    WHERE routine.oid = pg_catalog.to_regprocedure({Text(signature)})
                        AND routine.provolatile = 's') THEN
                RAISE EXCEPTION 'email_retention_discovery_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        string C(string property) => sql.DelimitIdentifier(Column(context, property));
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
                    RAISE EXCEPTION 'email_retention_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{ExecutorRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(schemaName)} AND routine.proname = '{FunctionName}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                            WHERE policy.polrelid = {Text(table)}::regclass AND policy.polname = '{PolicyName}') THEN
                        RAISE EXCEPTION 'email_retention_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{ExecutorRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {ExecutorRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{ExecutorRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                            WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                    IF ({PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}) IS NOT TRUE THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                ELSE
                    {guard}
                    {sourceGuard}
                END IF;
            END $enrollment$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {ExecutorRole};
            GRANT SELECT ({string.Join(", ", relation.Columns.Select(column => sql.DelimitIdentifier(column.Name)))})
                ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS "{PolicyName}" ON {table};
            CREATE POLICY "{PolicyName}" ON {table} FOR SELECT TO {OwnerRole} USING (true);
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_cutoff timestamp with time zone, p_take integer)
                RETURNS TABLE (tenant_id uuid)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    {sourceGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'email_retention_unbound_global_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_cutoff IS NULL OR NOT isfinite(p_cutoff) OR p_take IS NULL OR p_take < 1 THEN
                        RAISE EXCEPTION 'invalid email retention discovery bounds' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT outbox.{C(nameof(EmailDispatchOutbox.TenantId))}
                        FROM {table} outbox
                        WHERE NOT outbox.{C(nameof(EmailDispatchOutbox.IsDeleted))}
                            AND outbox.{C(nameof(EmailDispatchOutbox.ContentRedactedAt))} IS NULL
                            AND ((outbox.{C(nameof(EmailDispatchOutbox.Status))} = {(int)EmailDispatchStatus.Sent}
                                AND outbox.{C(nameof(EmailDispatchOutbox.SentAt))} <= p_cutoff)
                                OR (outbox.{C(nameof(EmailDispatchOutbox.Status))} = {(int)EmailDispatchStatus.Skipped}
                                AND outbox.{C(nameof(EmailDispatchOutbox.LastFailureAt))} <= p_cutoff))
                        GROUP BY outbox.{C(nameof(EmailDispatchOutbox.TenantId))}
                        ORDER BY min(outbox.{C(nameof(EmailDispatchOutbox.CreatedAt))}),
                            outbox.{C(nameof(EmailDispatchOutbox.TenantId))}
                        LIMIT p_take;
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {ExecutorRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {ExecutorRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $exit$ BEGIN
                {guard}
                {sourceGuard}
            END $exit$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetSchema() ?? "public";

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        string mappedSchema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
        if (mappedSchema != Schema(context))
            throw new InvalidOperationException("email_retention_sources_require_one_schema");
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, mappedSchema);
    }

    private static string Column(ExploreDbContext context, string property)
    {
        var entity = context.Model.FindEntityType(typeof(EmailDispatchOutbox))!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
