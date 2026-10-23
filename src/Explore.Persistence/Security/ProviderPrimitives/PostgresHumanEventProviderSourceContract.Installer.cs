using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanEventProviderSourceContract
{
    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanEventProviderSourceSql(context);
        string relations = string.Join("\n", HumanEventProviderSourceSql.Sources.Select(source =>
        {
            string table = Text(sql.Names.Table(source.Type));
            string columns = string.Join(" OR ", sql.Properties(source.Type).Select(property => $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attname = {Text(sql.Names.ColumnName(source.Type, property.Name))}
                        AND a.attnum > 0 AND NOT a.attisdropped
                        AND a.atttypid = pg_catalog.to_regtype({Text(property.GetRelationalTypeMapping().StoreType)})
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(property.IsNullable ? "false" : "true")}
                        AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                """));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                        WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR {columns}
                THEN RAISE EXCEPTION 'human_event_provider_source_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'human_event_provider_schema_drift' USING ERRCODE = '55000'; END IF;
            {relations}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanEventProviderSourceSql(context);
        var names = sql.Names;
        string roles = string.Join(",", Roles.Select(Text));
        (string Name, string Signature, string Result)[] routines =
            [(Observe, Signature, "jsonb"), (Acquire, Signature, "jsonb"), (Validate, Signature, "jsonb"), (Write, "", "trigger")];
        string ids = string.Join(",", routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(names.Name(routine.Name) + "(" + routine.Signature + ")")})"));
        string routineChecks = string.Join("\n", routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(names.Name(routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'human_event_provider_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", HumanEventProviderSourceSql.Sources.Select(source =>
        {
            string table = Text(names.Table(source.Type));
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string tenantGuard = tenant is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relrowsecurity AND t.relforcerowsecurity)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table})
                        AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}' AND p.polpermissive
                        AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, names.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'human_event_provider_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                {tenantGuard}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({table}) AND p.polname = '{ReadPolicy}'
                        AND p.polpermissive AND p.polcmd = 'r'
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table})
                        AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                        AND p.polname NOT IN ('{ReadPolicy}'{(tenant is null ? "" : $",'{PostgresTenantRowLevelSecurityModel.PolicyName}'")}))
                THEN RAISE EXCEPTION 'human_event_provider_rls_drift' USING ERRCODE = '55000'; END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({table}) AND t.tgname = '{WriterTrigger}'
                        AND t.tgfoid = pg_catalog.to_regprocedure({Text(names.Name(Write) + "()")})
                        AND t.tgtype = 31 AND t.tgenabled = 'A' AND NOT t.tgisinternal
                        AND t.tgnargs = 0 AND t.tgqual IS NULL AND t.tgattr = ''::int2vector
                        AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                        WHERE t.tgrelid = pg_catalog.to_regclass({table}) AND t.tgname = '{TruncateTrigger}'
                            AND t.tgfoid = pg_catalog.to_regprocedure({Text(names.Name(Write) + "()")})
                            AND t.tgtype = 34 AND t.tgenabled = 'A' AND NOT t.tgisinternal
                            AND t.tgnargs = 0 AND t.tgqual IS NULL AND t.tgattr = ''::int2vector
                            AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
                THEN RAISE EXCEPTION 'human_event_provider_writer_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string epoch = Text(names.Name(Epoch));
        return $"""
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {SourceGuard(context, enrollment)}
            {routineChecks}
            {policies}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({epoch})
                AND t.relkind = 'r' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({epoch})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> 10
                OR EXISTS (SELECT 1 FROM (VALUES ('tenant_id','uuid'),('kind','text'),('source_id','uuid'),('revision','boolean')) e(name,type)
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({epoch})
                        AND a.attname = e.name AND a.atttypid = pg_catalog.to_regtype(e.type) AND a.attnotnull
                        AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation)))
                OR EXISTS (SELECT 1 FROM (VALUES ('reader_tx','xid8'),('reader_signature','text'),('reader_hash','text'),
                    ('writer_tx','xid8'),('before_hash','text'),('after_hash','text')) e(name,type)
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({epoch})
                        AND a.attname = e.name AND a.atttypid = pg_catalog.to_regtype(e.type) AND NOT a.attnotnull
                        AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation)))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({epoch})
                    AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable
                    AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (tenant_id, kind, source_id)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({epoch}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d WHERE d.adrelid = pg_catalog.to_regclass({epoch}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({epoch}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({epoch}) OR i.inhparent = pg_catalog.to_regclass({epoch}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(names.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                        AND n.nspname <> {Text(names.Schema)} AND n.nspname NOT IN ('pg_catalog','information_schema')
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
                        OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'human_event_provider_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    /// <summary>Stopped-traffic, explicitly enrolled migrator only. Existing drift is not repaired.</summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("human_event_provider_native_requires_postgres");
        var sql = new HumanEventProviderSourceSql(context);
        var names = sql.Names;
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", HumanEventProviderSourceSql.Sources.Select(source => $"""
            GRANT SELECT ({string.Join(",", sql.Properties(source.Type).Select(property => names.Column(source.Type, property.Name)))})
                ON {names.Table(source.Type)} TO {OwnerRole};
            DROP POLICY IF EXISTS {ReadPolicy} ON {names.Table(source.Type)};
            CREATE POLICY {ReadPolicy} ON {names.Table(source.Type)} FOR SELECT TO {OwnerRole} USING (true);
            """));
        string definitions = string.Join("\n", Functions(context, enrollment).Select(function => $"""
            CREATE OR REPLACE FUNCTION {names.Name(function.Name)}({function.Arguments}) RETURNS {function.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {names.Name(function.Name)}({function.Signature}) FROM PUBLIC;
            {(function.Result == "jsonb" ? $"GRANT EXECUTE ON FUNCTION {names.Name(function.Name)}({function.Signature}) TO {RuntimeRole};" : "")}
            COMMENT ON FUNCTION {names.Name(function.Name)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        string triggers = string.Join("\n", HumanEventProviderSourceSql.Sources.Select(source => $"""
            CREATE TRIGGER {WriterTrigger} BEFORE INSERT OR UPDATE OR DELETE ON {names.Table(source.Type)}
                FOR EACH ROW EXECUTE FUNCTION {names.Name(Write)}();
            ALTER TABLE {names.Table(source.Type)} ENABLE ALWAYS TRIGGER {WriterTrigger};
            CREATE TRIGGER {TruncateTrigger} BEFORE TRUNCATE ON {names.Table(source.Type)}
                FOR EACH STATEMENT EXECUTE FUNCTION {names.Name(Write)}();
            ALTER TABLE {names.Table(source.Type)} ENABLE ALWAYS TRIGGER {TruncateTrigger};
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; v_count integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'human_event_provider_migrator_mismatch' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO v_count FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF v_count = 0 THEN
                    IF pg_catalog.to_regclass({Text(names.Name(Epoch))}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(names.Schema)} AND f.proname LIKE 'human_event_provider_source_%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname = '{ReadPolicy}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgname IN ('{WriterTrigger}','{TruncateTrigger}'))
                    THEN RAISE EXCEPTION 'human_event_provider_bootstrap_artifacts' USING ERRCODE = '55000'; END IF;
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
                    CREATE TABLE {names.Name(Epoch)} (tenant_id uuid NOT NULL,kind text NOT NULL,source_id uuid NOT NULL,
                        revision boolean NOT NULL,reader_tx xid8 NULL,reader_signature text NULL,reader_hash text NULL,
                        writer_tx xid8 NULL,before_hash text NULL,after_hash text NULL,PRIMARY KEY(tenant_id,kind,source_id));
                ELSE
                    IF v_count <> {Roles.Length} THEN RAISE EXCEPTION 'human_event_provider_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {names.Quote(names.Schema)} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {OwnerRole};
            GRANT SELECT (tenant_id,kind,source_id,revision), INSERT (tenant_id,kind,source_id,revision), UPDATE (revision)
                ON {names.Name(Epoch)} TO {OwnerRole};
            GRANT SELECT (reader_tx,reader_signature,reader_hash,writer_tx,before_hash,after_hash),
                UPDATE (reader_tx,reader_signature,reader_hash,writer_tx,before_hash,after_hash)
                ON {names.Name(Epoch)} TO {OwnerRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {OwnerRole};
            GRANT EXECUTE ON FUNCTION {names.Name(Write)}() TO {names.Quote(enrollment.MigratorLogin)};
            {string.Join("\n", HumanEventProviderSourceSql.Sources.Select(source => $"""
                DROP TRIGGER IF EXISTS {WriterTrigger} ON {names.Table(source.Type)};
                DROP TRIGGER IF EXISTS {TruncateTrigger} ON {names.Table(source.Type)};
                """))}
            {triggers}
            REVOKE EXECUTE ON FUNCTION {names.Name(Write)}() FROM {names.Quote(enrollment.MigratorLogin)};
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
