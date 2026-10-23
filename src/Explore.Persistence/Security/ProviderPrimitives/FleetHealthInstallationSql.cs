namespace Explore.Persistence.Security;

/// <summary>
/// Emits installation and catalog checks for one execute-only fleet-health owner.
/// Inputs are owner-declared, never combined across owners or inferred from live privileges.
/// Business queries, signatures, result shapes and pre-enrollment checks remain owner-local.
/// </summary>
internal sealed class FleetHealthInstallationSql(
    PostgresPrivilegeGuard.Contract contract,
    PostgresNativeQueueEnrollment enrollment,
    string ownerRole,
    string migratorRole,
    string executorRole,
    string policyName,
    string diagnosticPrefix)
{
    // Valid before first grants: this check does not depend on the routine or dedicated roles.
    internal string SourceGuard(string tables) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
            WHERE role.rolname = {Text(enrollment.GlobalLogin)} AND role.rolcanlogin
                AND NOT role.rolsuper AND NOT role.rolbypassrls AND NOT role.rolcreatedb
                AND NOT role.rolcreaterole AND NOT role.rolreplication)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                WHERE (pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'USAGE')
                    OR pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'SET'))
                    AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                        OR role.rolcreaterole OR role.rolreplication))
            OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)}, {Text(contract.Schema)}, 'CREATE')
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                WHERE relation.oid IN ({tables}) AND (
                    NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                    OR pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                        'SELECT,INSERT,UPDATE,REFERENCES'))) THEN
            RAISE EXCEPTION '{diagnosticPrefix}_sources_not_ready' USING ERRCODE = '55000';
        END IF;
        """;

    // Restrictive or undeclared applicable policies must not hide an unhealthy fleet.
    internal string ObservationGuard(string sourceGuard, string tables, string signature) => $"""
        {sourceGuard}
        IF EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
            LEFT JOIN pg_catalog.pg_policy policy
                ON policy.polrelid = relation.oid AND policy.polname = '{policyName}'
            WHERE relation.oid IN ({tables}) AND (
                policy.oid IS NULL OR policy.polcmd <> 'r' OR NOT policy.polpermissive
                OR policy.polroles IS DISTINCT FROM ARRAY[pg_catalog.to_regrole('{ownerRole}')::oid]
                OR pg_catalog.pg_get_expr(policy.polqual, policy.polrelid) IS DISTINCT FROM 'true'
                OR policy.polwithcheck IS NOT NULL
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other
                    WHERE other.polrelid = relation.oid AND other.polname <> '{policyName}'
                        AND other.polcmd IN ('r', '*')
                        AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{ownerRole}', role_id, 'USAGE'))
                        AND (NOT other.polpermissive OR other.polname <> 'tenant_isolation'))))
            OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                WHERE routine.oid = pg_catalog.to_regprocedure({Text(signature)})
                    AND routine.provolatile = 's') THEN
            RAISE EXCEPTION '{diagnosticPrefix}_observation_not_ready' USING ERRCODE = '55000';
        END IF;
        """;

    /// <summary>
    /// Requires the owner's pre-enrollment checks and enclosing v_role declaration.
    /// Existing drift is rejected before any grants; only fresh creator shortcuts are removed.
    /// </summary>
    internal string Enroll(string guard, string observationGuard) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
            WHERE rolname IN ('{ownerRole}', '{migratorRole}', '{executorRole}')) THEN
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                WHERE namespace.nspname = {Text(contract.Schema)}
                    AND routine.proname = {Text(contract.Routines[0].Name)})
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                    JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                    JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                    WHERE namespace.nspname = {Text(contract.Schema)} AND policy.polname = '{policyName}') THEN
                RAISE EXCEPTION '{diagnosticPrefix}_partial_bootstrap' USING ERRCODE = '55000';
            END IF;
            FOREACH v_role IN ARRAY ARRAY['{ownerRole}', '{migratorRole}', '{executorRole}'] LOOP
                EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
            END LOOP;
            GRANT {ownerRole} TO {migratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            EXECUTE format('GRANT {executorRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
            EXECUTE format('GRANT {migratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
            -- Retain fresh creator ADMIN until all declared edges exist.
            -- Then remove only the fresh owner/executor shortcuts.
            FOREACH v_role IN ARRAY ARRAY['{ownerRole}', '{executorRole}'] LOOP
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
            {guard}
            {observationGuard}
        END IF;
        """;

    internal static string ReadPolicy(
        string table, IEnumerable<string> columns, string ownerRole, string policyName) => $"""
        GRANT SELECT ({string.Join(", ", columns)})
            ON {table} TO {ownerRole};
        DROP POLICY IF EXISTS "{policyName}" ON {table};
        CREATE POLICY "{policyName}" ON {table} FOR SELECT TO {ownerRole} USING (true);
        """;

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
