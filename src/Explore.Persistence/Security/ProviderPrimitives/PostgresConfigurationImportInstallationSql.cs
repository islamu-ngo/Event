namespace Explore.Persistence.Security;

/// <summary>
/// Emits the common installation mechanics for one configuration-import owner.
/// Callers retain their source inventories, membership ceilings, routine ACLs,
/// policy predicates and complete admission bodies. This type executes no SQL,
/// enrolls no runtime login and never derives authority from the live catalog.
/// </summary>
internal sealed record PostgresConfigurationImportInstallationSql(
    string OwnerRole,
    string MigratorRole,
    string GlobalRole,
    string FingerprintPrefix,
    string MigratorPlaceholder)
{
    private string Roles => $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";

    internal string MissingMemberships() => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
            WHERE member.rolname = '{MigratorRole}' AND parent.rolname = '{OwnerRole}'
                AND edge.set_option AND NOT edge.inherit_option AND NOT edge.admin_option)
        OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
            WHERE member.rolname = {MigratorPlaceholder} AND parent.rolname = '{MigratorRole}'
                AND edge.set_option AND NOT edge.inherit_option AND NOT edge.admin_option)
        """;

    // Follow actual edges. A superuser's implicit membership is not enrollment.
    private string MigrationInGlobal() => $"""
        EXISTS (WITH RECURSIVE memberships(roleid) AS (
            SELECT roleid FROM pg_catalog.pg_auth_members
                WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
            UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                JOIN memberships parent ON parent.roleid = edge.member)
            SELECT 1 FROM memberships JOIN pg_catalog.pg_roles role ON role.oid = memberships.roleid
                WHERE role.rolname = '{GlobalRole}')
        """;

    internal string NonRoutineOwnership() => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend dependency
            WHERE dependency.refclassid = 'pg_catalog.pg_authid'::regclass
                AND dependency.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({Roles}))
                AND dependency.deptype = 'o' AND (dependency.classid <> 'pg_catalog.pg_proc'::regclass
                    OR dependency.dbid <> (SELECT oid FROM pg_catalog.pg_database
                        WHERE datname = pg_catalog.current_database())))
        """;

    internal string ColumnPrivilegeDrift(string expectedColumns) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_attribute attribute
            JOIN pg_catalog.pg_class relation ON relation.oid = attribute.attrelid
            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) access(privilege)
            WHERE attribute.attnum > 0 AND NOT attribute.attisdropped
                AND namespace.nspname NOT IN ('pg_catalog','information_schema')
                AND namespace.nspname NOT LIKE 'pg_toast%'
                AND pg_catalog.has_column_privilege('{OwnerRole}', relation.oid, attribute.attnum, access.privilege)
                AND NOT EXISTS (SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
                    WHERE expected.relation_id = relation.oid AND expected.column_name = attribute.attname
                        AND expected.privilege = access.privilege))
        OR EXISTS (SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
            WHERE NOT pg_catalog.has_column_privilege('{OwnerRole}', expected.relation_id,
                expected.column_name, expected.privilege))
        """;

    internal string ExtraneousGrants() => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class sequence
            CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({Roles})) role
            WHERE sequence.relkind = 'S'
                AND pg_catalog.has_sequence_privilege(role.oid, sequence.oid, 'USAGE,SELECT,UPDATE'))
        OR EXISTS (SELECT 1 FROM pg_catalog.pg_database database
            CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({Roles})) role
            WHERE pg_catalog.has_database_privilege(role.oid, database.oid, 'CREATE'))
        OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
            CROSS JOIN LATERAL pg_catalog.aclexplode(relation.relacl) permission
            WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({Roles}))
                AND permission.is_grantable)
        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute attribute
            CROSS JOIN LATERAL pg_catalog.aclexplode(attribute.attacl) permission
            WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({Roles}))
                AND permission.is_grantable)
        """;

    /// <summary>
    /// Installs only the caller-supplied functions and grants, between its original
    /// pre-install and closed-state proofs. The migration login placeholder is
    /// bound inside the same installation session, never discovered or replaced
    /// with a shared login.
    /// </summary>
    internal string Build(
        string schema,
        string schemaGrants,
        string grants,
        string policies,
        string ddl,
        string acl,
        string installationInvalid,
        string closedInvalid,
        string diagnosticPrefix) => $"""
        DO $roles$
        DECLARE role_name text;
        BEGIN
            IF CURRENT_USER <> SESSION_USER OR CURRENT_USER IN ({Roles}) OR {MigrationInGlobal()} THEN
                RAISE EXCEPTION '{diagnosticPrefix}_migration_scope' USING ERRCODE = '42501';
            END IF;
            FOREACH role_name IN ARRAY ARRAY[{Roles}] LOOP
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                    EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L', role_name,
                        '{FingerprintPrefix}role:' || role_name);
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                    AND ((rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit)
                        OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                        '{FingerprintPrefix}role:' || role_name)) THEN
                    RAISE EXCEPTION '{diagnosticPrefix}_role_invalid' USING ERRCODE = '42501';
                END IF;
            END LOOP;
            IF {installationInvalid} THEN
                RAISE EXCEPTION '{diagnosticPrefix}_migration_authority_invalid' USING ERRCODE = '42501';
            END IF;
        END; $roles$;
        GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
        GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
        {schemaGrants}
        GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
        GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
        {grants}
        {policies}
        DO $install$
        DECLARE previous_role text := pg_catalog.current_setting('role');
            migrator_login text := CURRENT_USER;
        BEGIN
            PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
            EXECUTE pg_catalog.replace($ddl${ddl}$ddl$, '{MigratorPlaceholder}', pg_catalog.quote_literal(migrator_login));
            EXECUTE $acl${acl}$acl$;
            PERFORM pg_catalog.set_config('role', previous_role, true);
        END; $install$;
        REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
        DO $closed$
        BEGIN
            IF {closedInvalid} THEN
                RAISE EXCEPTION '{diagnosticPrefix}_authority_not_ready' USING ERRCODE = '42501';
            END IF;
        END; $closed$;
        """;
}
