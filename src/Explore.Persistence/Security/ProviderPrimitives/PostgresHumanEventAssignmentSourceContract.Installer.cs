using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanEventAssignmentSourceContract
{
    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = Names(context);
        string sources = string.Join("\n", HumanEventAssignmentSourceSql.Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string table = Text(sql.Table(source.Type));
            string columns = string.Join(" OR ", source.Columns.Select(column =>
            {
                var property = entity.FindProperty(column)!;
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = pg_catalog.to_regclass({table}) AND a.attname = {Text(sql.ColumnName(source.Type, column))}
                            AND a.attnum > 0 AND NOT a.attisdropped
                            AND a.atttypid = pg_catalog.to_regtype({Text(property.GetRelationalTypeMapping().StoreType)})
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                            AND a.attnotnull = {(property.IsNullable ? "false" : "true")}
                            AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                    """;
            }));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                        WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR {columns}
                THEN RAISE EXCEPTION 'human_assignment_source_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'human_assignment_schema_drift' USING ERRCODE = '55000'; END IF;
            {sources}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = Names(context);
        string roles = string.Join(",", Roles.Select(Text));
        (string Name, string Signature, string Result)[] routines =
            [(Observe, Signature, "jsonb"), (Acquire, Signature, "jsonb"), (Write, "", "trigger")];
        // Resolve the source-declared set in an uncorrelated subquery on every
        // assertion, rather than repeating name resolution for every catalog row.
        string routineIds = "SELECT pg_catalog.to_regprocedure(expected.signature) FROM (VALUES "
            + string.Join(",", routines.Select(routine =>
                $"({Text(sql.Name(routine.Name) + "(" + routine.Signature + ")")})"))
            + ") expected(signature)";
        string routineChecks = string.Join("\n", routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'human_assignment_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", HumanEventAssignmentSourceSql.Sources.Select(source =>
        {
            string table = Text(sql.Table(source.Type));
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string tenantGuard = tenant is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relrowsecurity AND t.relforcerowsecurity)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table})
                        AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}'
                        AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'human_assignment_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                {tenantGuard}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({table}) AND p.polname = '{ReadPolicy}'
                        AND p.polpermissive AND p.polcmd = 'r' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({table}) AND p.polname = '{UpdatePolicy}'
                            AND p.polpermissive AND p.polcmd = 'w' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                            AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({table})
                            AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                            AND p.polname NOT IN ('{ReadPolicy}','{UpdatePolicy}'
                                {(tenant is null ? "" : $",'{PostgresTenantRowLevelSecurityModel.PolicyName}'")}))
                THEN RAISE EXCEPTION 'human_assignment_rls_drift' USING ERRCODE = '55000'; END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({table}) AND t.tgname = '{WriterTrigger}'
                        AND t.tgfoid = pg_catalog.to_regprocedure({Text(sql.Name(Write) + "()")})
                        AND t.tgtype = 62 AND t.tgenabled = 'A' AND NOT t.tgisinternal
                        AND t.tgnargs = 0 AND t.tgqual IS NULL AND t.tgattr = ''::int2vector
                        AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
                THEN RAISE EXCEPTION 'human_assignment_writer_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string anchor = Text(sql.Name(Anchor));
        return $"""
            {PostgresManagedProviderProvisioningContract.SharedWriterAssertionSql(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {SourceGuard(context, enrollment)}
            {routineChecks}
            {policies}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({anchor})
                AND t.relkind = 'r' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attname = 'id' AND a.atttypid = 'smallint'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attname = 'revision' AND a.atttypid = 'boolean'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable
                    AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'c' AND c.convalidated AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({anchor}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({anchor}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({anchor}) OR i.inhparent = pg_catalog.to_regclass({anchor}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                        AND n.nspname <> {Text(sql.Schema)} AND n.nspname NOT IN ('pg_catalog','information_schema')
                        AND n.nspname NOT LIKE 'pg_toast%' AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) AND d.deptype = 'o'
                    AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                        OR d.objid NOT IN ({routineIds})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIds})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'human_assignment_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    /// <summary>
    /// Explicit migrator installation before traffic. Existing drift is rejected before grants
    /// or routines are rewritten. This adds dedicated nonlogin roles, not deployment IAM rights.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("human_assignment_native_requires_postgres");
        var sql = Names(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", HumanEventAssignmentSourceSql.Sources.Select(source => $"""
            GRANT SELECT ({string.Join(",", source.Columns.Select(column => sql.Column(source.Type, column)))})
                ON {sql.Table(source.Type)} TO {OwnerRole};
            GRANT UPDATE ({sql.Column(source.Type, source.Columns[0])}) ON {sql.Table(source.Type)} TO {OwnerRole};
            DROP POLICY IF EXISTS {ReadPolicy} ON {sql.Table(source.Type)};
            CREATE POLICY {ReadPolicy} ON {sql.Table(source.Type)} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS {UpdatePolicy} ON {sql.Table(source.Type)};
            CREATE POLICY {UpdatePolicy} ON {sql.Table(source.Type)} FOR UPDATE TO {OwnerRole} USING (true) WITH CHECK (true);
            """));
        string definitions = string.Join("\n", Functions(context, enrollment).Select(function => $"""
            CREATE OR REPLACE FUNCTION {sql.Name(function.Name)}({function.Arguments}) RETURNS {function.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {sql.Name(function.Name)}({function.Signature}) FROM PUBLIC;
            {(function.Result == "jsonb" ? $"GRANT EXECUTE ON FUNCTION {sql.Name(function.Name)}({function.Signature}) TO {RuntimeRole},{GlobalRole};" : "")}
            COMMENT ON FUNCTION {sql.Name(function.Name)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        string triggers = string.Join("\n", HumanEventAssignmentSourceSql.Sources.Select(source => $"""
            DROP TRIGGER IF EXISTS {WriterTrigger} ON {sql.Table(source.Type)};
            CREATE TRIGGER {WriterTrigger} BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {sql.Table(source.Type)}
                FOR EACH STATEMENT EXECUTE FUNCTION {sql.Name(Write)}();
            ALTER TABLE {sql.Table(source.Type)} ENABLE ALWAYS TRIGGER {WriterTrigger};
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; v_count integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'human_assignment_migrator_mismatch' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO v_count FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF v_count = 0 THEN
                    IF pg_catalog.to_regclass({Text(sql.Name(Anchor))}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE 'human_event_assignment_source_%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname IN ('{ReadPolicy}','{UpdatePolicy}'))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgname = '{WriterTrigger}')
                    THEN RAISE EXCEPTION 'human_assignment_bootstrap_artifacts' USING ERRCODE = '55000'; END IF;
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                        EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L',role_name,'{Stamp}role:' || role_name);
                    END LOOP;
                    {memberships}
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                            WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                        THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                    END LOOP;
                    CREATE TABLE {sql.Name(Anchor)} (id smallint NOT NULL PRIMARY KEY CHECK (id = 1), revision boolean NOT NULL);
                    INSERT INTO {sql.Name(Anchor)} VALUES (1,false);
                ELSE
                    IF v_count <> {Roles.Length} THEN RAISE EXCEPTION 'human_assignment_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                END IF;
            END $bootstrap$;
            DO $epoch$
            BEGIN
                IF (SELECT count(*) FROM {sql.Name(Anchor)} WHERE id = 1) <> 1 THEN
                    RAISE EXCEPTION 'human_assignment_anchor_missing' USING ERRCODE = '55000';
                END IF;
            END $epoch$;
            GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
            GRANT SELECT (id,revision), UPDATE (revision) ON {sql.Name(Anchor)} TO {OwnerRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
            GRANT EXECUTE ON FUNCTION {sql.Name(Write)}() TO {sql.Quote(enrollment.MigratorLogin)};
            {triggers}
            REVOKE EXECUTE ON FUNCTION {sql.Name(Write)}() FROM {sql.Quote(enrollment.MigratorLogin)};
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
