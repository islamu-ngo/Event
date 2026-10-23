using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanAdministratorCommitAuthorityContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        return new(Schema(context), Roles,
            [
                new(InstanceName, Signature, OwnerRole, [RuntimeRole, GlobalRole, InstanceRole, TenantRole]),
                new(TenantName, Signature, OwnerRole, [RuntimeRole, TenantRole]),
                new(SubjectName, Signature, OwnerRole, [RuntimeRole, GlobalRole, InstanceRole, TenantRole])
            ],
            Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select(property => new PostgresPrivilegeGuard.Column(
                    ColumnName(context, source.Type, property),
                    property == "Id" ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())).ToArray(),
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false),
                new(InstanceRole, enrollment.EmailInstanceMutationLogin, true, false, false),
                new(TenantRole, enrollment.EmailTenantMutationLogin, true, false, false)
            ]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string invalid = string.Join(" OR ", Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string table = Table(context, source.Type);
            string columns = string.Join(" OR ", source.Columns.Select(property =>
            {
                var mapped = entity.FindProperty(property)!;
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = pg_catalog.to_regclass({Text(table)})
                            AND a.attname = {Text(ColumnName(context, source.Type, property))}
                            AND a.attnum > 0 AND NOT a.attisdropped
                            AND a.atttypid = pg_catalog.to_regtype({Text(mapped.GetRelationalTypeMapping().StoreType)})
                            AND a.attnotnull = {(mapped.IsNullable ? "false" : "true")})
                    """;
            }));
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    WHERE t.oid = pg_catalog.to_regclass({Text(table)}) AND t.relkind = 'r'
                        AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR {columns}
                """;
        }));
        // Parent locks exclude FK insertions only while the actual, validated native constraints exist.
        string foreignKeys = string.Join("\n", Sources.SelectMany(source =>
            context.Model.FindEntityType(source.Type)!.GetForeignKeys()
                .Where(key => Sources.Any(parent => parent.Type == key.PrincipalEntityType.ClrType))
                .Select(key =>
                {
                    string Columns(IReadOnlyList<IProperty> properties, Type type) =>
                        "ARRAY[" + string.Join(",", properties.Select(property =>
                            Text(ColumnName(context, type, property.Name)))) + "]::name[]";
                    return $"""
                        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint fk
                            WHERE fk.contype = 'f' AND fk.convalidated AND NOT fk.condeferrable
                                AND fk.conrelid = pg_catalog.to_regclass({Text(Table(context, source.Type))})
                                AND fk.confrelid = pg_catalog.to_regclass({Text(Table(context, key.PrincipalEntityType.ClrType))})
                                AND ARRAY(SELECT a.attname FROM unnest(fk.conkey) WITH ORDINALITY k(id, ord)
                                    JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = k.id
                                    ORDER BY k.ord) = {Columns(key.Properties, source.Type)}
                                AND ARRAY(SELECT a.attname FROM unnest(fk.confkey) WITH ORDINALITY k(id, ord)
                                    JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.confrelid AND a.attnum = k.id
                                    ORDER BY k.ord) = {Columns(key.PrincipalKey.Properties, key.PrincipalEntityType.ClrType)})
                        THEN RAISE EXCEPTION 'human_administrator_fk_drift' USING ERRCODE = '55000'; END IF;
                        """;
                })));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n
                WHERE n.nspname = {Text(Schema(context))}
                    AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR {invalid} THEN
                RAISE EXCEPTION 'human_administrator_source_drift' USING ERRCODE = '55000';
            END IF;
            {foreignKeys}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string functions = string.Join(",", Functions
            .Select(function => $"pg_catalog.to_regprocedure({Text(Function(context, function.Name) + "(" + Signature + ")")})"));
        string routineGuard = string.Join("\n", Functions.Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(Function(context, function.Name) + "(" + Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.bool'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid, 'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc, 'UTF8')), 'hex'))
            THEN RAISE EXCEPTION 'human_administrator_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Sources.Select(source =>
        {
            string table = Table(context, source.Type);
            var tenantTable = tenantTables.SingleOrDefault(value =>
                value.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string tenantGuard = tenantTable is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    WHERE t.oid = pg_catalog.to_regclass({Text(table)}) AND t.relrowsecurity AND t.relforcerowsecurity)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}'
                            AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                            AND pg_catalog.obj_description(p.oid, 'pg_policy') =
                                {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenantTable, Schema(context)))}
                                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'human_administrator_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                {tenantGuard}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{ReadPolicy}'
                        AND p.polcmd = 'r' AND p.polpermissive
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual, p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{UpdatePolicy}'
                            AND p.polcmd = 'w' AND p.polpermissive
                            AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                            AND pg_catalog.pg_get_expr(p.polqual, p.polrelid) = 'true'
                            AND pg_catalog.pg_get_expr(p.polwithcheck, p.polrelid) = 'true')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                            AND p.polname NOT IN ('{ReadPolicy}', '{UpdatePolicy}'
                                {(tenantTable is null ? "" : $", '{PostgresTenantRowLevelSecurityModel.PolicyName}'")}))
                THEN RAISE EXCEPTION 'human_administrator_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            {PostgresPrivilegeGuard.BuildAssertSql(contract)}
            {SourceGuard(context, enrollment)}
            {routineGuard}
            {policies}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid, 'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                        AND pg_catalog.has_schema_privilege(r.oid, n.oid, 'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                        AND n.nspname <> {Text(Schema(context))} AND n.nspname NOT IN ('pg_catalog','information_schema')
                        AND n.nspname NOT LIKE 'pg_toast%' AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid, t.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid, t.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles}) AND t.relkind = 'S'
                        AND pg_catalog.has_sequence_privilege(r.oid, t.oid, 'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                        AND pg_catalog.has_database_privilege(r.oid, d.oid, 'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d
                    WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                        AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        AND d.deptype = 'o' AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                            OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                            OR d.objid NOT IN ({functions})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                    WHERE f.oid NOT IN ({functions})
                        AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                            OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                                WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid, {Text(Schema(context))}, 'USAGE'))
            THEN RAISE EXCEPTION 'human_administrator_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions.Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(Function(context, function.Name) + "(" + Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc, 'UTF8')), 'hex') =
                        {Text(Hash(Body(context, enrollment, function.Tenant, function.SubjectOnly)))})
            THEN RAISE EXCEPTION 'human_administrator_body_drift' USING ERRCODE = '55000'; END IF;
            """));

    /// <summary>
    /// Source-defined enrollment only. Pre-existing role/ACL/body/policy drift is rejected before
    /// replacement; installation never repairs contamination into an apparently valid contract.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string grants = string.Join("\n", Sources.Select(source => $"""
            GRANT SELECT ({string.Join(",", source.Columns.Select(property => Column(context, source.Type, property)))})
                ON {Table(context, source.Type)} TO {OwnerRole};
            GRANT UPDATE ({Column(context, source.Type, "Id")}) ON {Table(context, source.Type)} TO {OwnerRole};
            DROP POLICY IF EXISTS {ReadPolicy} ON {Table(context, source.Type)};
            CREATE POLICY {ReadPolicy} ON {Table(context, source.Type)} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS {UpdatePolicy} ON {Table(context, source.Type)};
            CREATE POLICY {UpdatePolicy} ON {Table(context, source.Type)} FOR UPDATE TO {OwnerRole} USING (true) WITH CHECK (true);
            """));
        string definitions = string.Join("\n", Functions.Select(entry =>
        {
            string function = Function(context, entry.Name);
            string executors = entry.Tenant ? $"{RuntimeRole}, {TenantRole}"
                : $"{RuntimeRole}, {GlobalRole}, {InstanceRole}, {TenantRole}";
            string body = Body(context, enrollment, entry.Tenant, entry.SubjectOnly);
            return $"""
                CREATE OR REPLACE FUNCTION {function}({Arguments}) RETURNS boolean
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
                REVOKE ALL ON FUNCTION {function}({Signature}) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION {function}({Signature}) TO {executors};
                COMMENT ON FUNCTION {function}({Signature}) IS {Text(Stamp + Hash(body))};
                """;
        }));
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE', {Text(edge.Parent)}, {Text(edge.Member)});
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; v_count integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text) THEN
                    RAISE EXCEPTION 'human_administrator_migration_login_mismatch' USING ERRCODE = '42501';
                END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO v_count FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF v_count = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
                        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(Schema(context))} AND p.proname LIKE 'human_administrator_%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                            WHERE p.polname IN ('{ReadPolicy}', '{UpdatePolicy}')) THEN
                        RAISE EXCEPTION 'human_administrator_bootstrap_artifacts' USING ERRCODE = '55000';
                    END IF;
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                        EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L', role_name, '{Stamp}role:' || role_name);
                    END LOOP;
                    {memberships}
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                            WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid
                                AND m.member = pg_catalog.to_regrole(session_user)::oid) THEN
                            EXECUTE pg_catalog.format('REVOKE %I FROM %I', role_name, session_user);
                        END IF;
                    END LOOP;
                ELSE
                    IF v_count <> {Roles.Length} THEN
                        RAISE EXCEPTION 'human_administrator_partial_roles' USING ERRCODE = '55000';
                    END IF;
                    {guard}
                    {exact}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {Identifier(context, Schema(context))} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {Identifier(context, Schema(context))} TO {OwnerRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role', previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {Identifier(context, Schema(context))} FROM {OwnerRole};
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
