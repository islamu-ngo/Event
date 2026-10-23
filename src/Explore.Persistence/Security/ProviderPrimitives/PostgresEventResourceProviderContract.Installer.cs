using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresEventResourceProviderContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("resource_provider_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Name, routine.Signature,
                OwnerRole, routine.Result == "trigger" ? [OwnerRole] : [GlobalRole])).ToArray(),
            [
                .. Sources.Select(type => new PostgresPrivilegeGuard.Relation(context.Model.FindEntityType(type)!.GetTableName()!,
                    OwnerRole, type == typeof(TenantSetting) ? [] : ["UPDATE"], context.Model.FindEntityType(type)!.GetProperties()
                        .Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(type, property.Name),
                            type == typeof(TenantSetting) ? ["SELECT"] : ["SELECT", "INSERT"])).ToArray())),
                new(SourceTable, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])]),
                new(AttemptTable, OwnerRole, ["SELECT", "INSERT", "UPDATE"], [])
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string definitions = string.Join("\n", Routines.Select(routine => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, routine.Name)}({routine.Arguments}) RETURNS {routine.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${Body(context, enrollment, routine.Name)}$body$;
            REVOKE ALL ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature})
                TO {(routine.Result == "trigger" ? sql.Quote(enrollment.MigratorLogin) : GlobalRole)};
            COMMENT ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) IS {Text(Stamp + Hash(Body(context, enrollment, routine.Name)))};
            """));
        string grants = string.Join("\n", contract.Relations.Select(relation =>
            (relation.Privileges.Count == 0 ? "" : $"GRANT {string.Join(",", relation.Privileges)} ON {sql.Name(relation.Name)} TO {relation.Role};")
            + "\n" + string.Join("\n", new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                var columns = relation.Columns.Where(column => column.Privileges.Contains(privilege, StringComparer.Ordinal))
                    .Select(column => sql.Quote(column.Name)).ToArray();
                return columns.Length == 0 ? "" : $"GRANT {privilege} ({string.Join(",", columns)}) ON {sql.Name(relation.Name)} TO {relation.Role};";
            }))));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; installed integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'resource_provider_original_migrator' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO installed FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF installed = 0 THEN
                    IF pg_catalog.to_regclass({Text(sql.Name(SourceTable))}) IS NOT NULL
                        OR pg_catalog.to_regclass({Text(sql.Name(AttemptTable))}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgname IN ('{Prefix}changed','{Prefix}row','{Prefix}settlement')
                            AND t.tgrelid IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))}))
                    THEN RAISE EXCEPTION 'resource_provider_partial_installation' USING ERRCODE = '55000'; END IF;
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                        EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L',role_name,'{Stamp}role:' || role_name);
                    END LOOP;
                    {string.Join("\n", contract.Memberships.Select(edge => $"""
                        EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                            SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
                        """))}
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                            WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                        THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                    END LOOP;
                    CREATE TABLE {sql.Name(SourceTable)} (id smallint PRIMARY KEY CHECK (id = 1),revision bigint NOT NULL);
                    INSERT INTO {sql.Name(SourceTable)} (id,revision) VALUES (1,0);
                    CREATE TABLE {sql.Name(AttemptTable)} (attempt bigint NOT NULL,backend integer NOT NULL,request jsonb NOT NULL,
                        image jsonb NOT NULL,applied boolean NOT NULL,finished boolean NOT NULL,pending jsonb,observed boolean NOT NULL,
                        PRIMARY KEY (attempt,backend));
                    REVOKE ALL ON {sql.Name(SourceTable)},{sql.Name(AttemptTable)} FROM PUBLIC;
                ELSE
                    IF installed <> {Roles.Length} THEN RAISE EXCEPTION 'resource_provider_partial_roles' USING ERRCODE = '55000'; END IF;
                    {Guard(context, enrollment)}
                    {ExactBodies(context, enrollment)}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
            {grants}
            GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
            DO $install$ DECLARE previous_role text := pg_catalog.current_setting('role'); BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
            DROP POLICY IF EXISTS {Prefix}read ON {sql.Table(typeof(TenantSetting))};
            CREATE POLICY {Prefix}read ON {sql.Table(typeof(TenantSetting))} FOR SELECT TO {OwnerRole} USING (true);
            {string.Join("\n", Sources.Select(type => $"""
                DROP TRIGGER IF EXISTS {Prefix}changed ON {sql.Table(type)};
                CREATE TRIGGER {Prefix}changed BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {sql.Table(type)}
                    FOR EACH STATEMENT EXECUTE FUNCTION {FunctionSql(context, "changed")}();
                ALTER TABLE {sql.Table(type)} ENABLE ALWAYS TRIGGER {Prefix}changed;
                DROP TRIGGER IF EXISTS {Prefix}row ON {sql.Table(type)};
                CREATE TRIGGER {Prefix}row AFTER INSERT OR UPDATE OR DELETE ON {sql.Table(type)}
                    FOR EACH ROW EXECUTE FUNCTION {FunctionSql(context, "row")}();
                ALTER TABLE {sql.Table(type)} ENABLE ALWAYS TRIGGER {Prefix}row;
                """))}
            DROP TRIGGER IF EXISTS {Prefix}settlement ON {sql.Name(AttemptTable)};
            CREATE CONSTRAINT TRIGGER {Prefix}settlement AFTER INSERT OR UPDATE ON {sql.Name(AttemptTable)}
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {FunctionSql(context, "settlement")}();
            ALTER TABLE {sql.Name(AttemptTable)} ENABLE ALWAYS TRIGGER {Prefix}settlement;
            DO $close$ DECLARE previous_role text := pg_catalog.current_setting('role'); BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                REVOKE EXECUTE ON FUNCTION {FunctionSql(context, "changed")}(),{FunctionSql(context, "row")}(),
                    {FunctionSql(context, "settlement")}() FROM {sql.Quote(enrollment.MigratorLogin)};
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $close$;
            DO $installed$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $installed$;
            """;
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'resource_provider_namespace_owner' USING ERRCODE = '55000'; END IF;
            {string.Join("\n", Sources.Select(type =>
            {
                var entity = context.Model.FindEntityType(type)!;
                string table = Text(sql.Table(type));
                string tenantPolicy = "";
                if (type == typeof(TenantSetting))
                {
                    var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                        .Single(value => value.TableName == entity.GetTableName());
                    string fingerprint = PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, sql.Schema);
                    tenantPolicy = $"""
                        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                            AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}' AND p.polcmd = '*'
                            AND p.polroles = ARRAY[0::oid] AND p.polpermissive AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                            AND pg_catalog.obj_description(p.oid,'pg_policy') =
                                {Text(fingerprint)} || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                        THEN RAISE EXCEPTION 'resource_provider_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                        """;
                }
                return $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                        AND t.relkind = 'r' AND t.relpersistence = 'p'
                        AND ({(type == typeof(SystemSetting)
                            ? PostgresLocalIdentitySystemSettingPolicies.Compatible(OwnerRole, "t")
                            : $"t.relrowsecurity = {(type == typeof(TenantSetting)).ToString().ToLowerInvariant()} AND t.relforcerowsecurity = {(type == typeof(TenantSetting)).ToString().ToLowerInvariant()}")})
                        AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                        OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                            AND a.attnum > 0 AND NOT a.attisdropped) <> {entity.GetProperties().Count()}
                        OR {string.Join(" OR ", entity.GetProperties().Select(property => $"""
                            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                                AND a.attname = {Text(sql.ColumnName(type, property.Name))} AND a.attnum > 0 AND NOT a.attisdropped
                                AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                                AND a.attnotnull = {(!property.IsNullable).ToString().ToLowerInvariant()} AND a.attgenerated = '' AND a.attidentity = ''
                                AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation(
                                    {Text("pg_catalog." + sql.Quote(property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true ? "C" : "default"))})))
                            """))}
                    THEN RAISE EXCEPTION 'resource_provider_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                    {tenantPolicy}
                    {string.Join("\n", entity.GetKeys().Select(key => key.Properties)
                        .Concat(entity.GetIndexes().Where(index => index.IsUnique).Select(index => index.Properties)).Select(properties => $"""
                        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                            AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate AND i.indpred IS NULL AND i.indexprs IS NULL
                            AND i.indnkeyatts = {properties.Count} AND i.indnatts = {properties.Count}
                            AND NOT EXISTS (SELECT 1 FROM unnest(i.indoption::smallint[]) option WHERE option <> 0)
                            AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                                JOIN pg_catalog.pg_opclass o ON o.oid = operator_id JOIN pg_catalog.pg_am am ON am.oid = o.opcmethod
                                WHERE NOT o.opcdefault OR am.amname <> 'btree')
                            AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                                JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                                WHERE col.value <> a.attcollation)
                            AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum ORDER BY k.ord)
                                = ARRAY[{string.Join(",", properties.Select(property => Text(sql.ColumnName(type, property.Name))))}]::name[])
                        THEN RAISE EXCEPTION 'resource_provider_source_key_drift' USING ERRCODE = '55000'; END IF;
                        """))}
                    """;
            }))}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string ids = string.Join(",", Routines.Select(routine => $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})"));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {string.Join("\n", Routines.Select(routine => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                    WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                        AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                        AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                        AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                        AND f.proargnames IS NOT DISTINCT FROM {(routine.Arguments.Length == 0 ? "NULL::text[]"
                            : "ARRAY[" + string.Join(",", routine.Arguments.Split(',').Select(argument => Text(argument.Split(' ')[0]))) + "]::text[]")}
                        AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                        AND f.proconfig = ARRAY['search_path=pg_catalog']::text[]
                        AND pg_catalog.obj_description(f.oid,'pg_proc') = '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
                THEN RAISE EXCEPTION 'resource_provider_routine_drift' USING ERRCODE = '55000'; END IF;
                """))}
            {InternalGuard(context, enrollment)}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(typeof(TenantSetting)))}::regclass
                AND p.polname = '{Prefix}read' AND p.polcmd = 'r' AND p.polpermissive
                AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(typeof(TenantSetting)))}::regclass
                    AND p.polname NOT IN ('{Prefix}read','{PostgresTenantRowLevelSecurityModel.PolicyName}')
                    AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) r WHERE pg_catalog.pg_has_role('{OwnerRole}',r,'USAGE'))))
            THEN RAISE EXCEPTION 'resource_provider_source_policy_drift' USING ERRCODE = '55000'; END IF;
            {string.Join("\n", Sources.SelectMany(type => new[] { ("changed", 62), ("row", 29) }
                .Select(trigger => TriggerGuard(context, sql.Table(type), trigger.Item1, trigger.Item2, false))))}
            {TriggerGuard(context, sql.Name(AttemptTable), "settlement", 21, true)}
            {string.Join("\n", Sources.Select(type => $"""
                IF pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Table(type))}::regclass,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Table(type))}::regclass,'INSERT,UPDATE,REFERENCES')
                THEN RAISE EXCEPTION 'resource_provider_ambient_write' USING ERRCODE = '55000'; END IF;
                """))}
            {string.Join("\n", new[] { enrollment.GlobalLogin, enrollment.RuntimeLogin }.SelectMany(login => new[] { SourceTable, AttemptTable }.Select(table => $"""
                IF pg_catalog.has_table_privilege({Text(login)},{Text(sql.Name(table))}::regclass,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege({Text(login)},{Text(sql.Name(table))}::regclass,'SELECT,INSERT,UPDATE,REFERENCES')
                THEN RAISE EXCEPTION 'resource_provider_private_service_access' USING ERRCODE = '55000'; END IF;
                """)))}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Schema)},'CREATE')
                OR pg_catalog.has_schema_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Schema)},'CREATE')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                        AND n.nspname <> {Text(sql.Schema)} AND n.nspname NOT IN ('pg_catalog','information_schema')
                        AND n.nspname NOT LIKE 'pg_toast%' AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))}) AND t.relkind = 'S'
                        AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))}))
                    AND d.deptype = 'o' AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))})))))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgfoid IN ({ids}) AND NOT
                    (t.tgrelid = {Text(sql.Name(AttemptTable))}::regclass AND t.tgname = '{Prefix}settlement'
                        AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "settlement") + "()")})
                    OR t.tgrelid IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))})
                        AND (t.tgname = '{Prefix}changed' AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                            OR t.tgname = '{Prefix}row' AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "row") + "()")}))))
            THEN RAISE EXCEPTION 'resource_provider_closed_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string InternalGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return string.Join("\n", new[] { SourceTable, AttemptTable }.Select(name =>
        {
            (string Name, string Type, bool Nullable)[] columns = name == SourceTable
                ? [("id", "smallint", false), ("revision", "bigint", false)]
                : [("attempt", "bigint", false), ("backend", "integer", false), ("request", "jsonb", false), ("image", "jsonb", false),
                    ("applied", "boolean", false), ("finished", "boolean", false), ("pending", "jsonb", true), ("observed", "boolean", false)];
            string table = Text(sql.Name(name));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {columns.Length}
                    OR {string.Join(" OR ", columns.Select(column => $"""
                        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                            AND a.attname = {Text(column.Name)} AND a.attnum > 0 AND NOT a.attisdropped
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(column.Type)}
                            AND a.attnotnull = {(!column.Nullable).ToString().ToLowerInvariant()}
                            AND a.attgenerated = '' AND a.attidentity = '' AND NOT a.atthasdef AND a.attcollation = 0)
                        """))}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({table})
                        AND NOT t.tgisinternal {(name == AttemptTable ? $"AND t.tgname <> '{Prefix}settlement'" : "")})
                    OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table}) AND c.contype <> 'n')
                        <> 2
                    OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})) <> 1
                    {(name == SourceTable ? $"""
                        OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                            AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit
                            AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                        """ : "")}
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                        AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable AND NOT c.condeferred
                        AND ARRAY(SELECT a.attname FROM unnest(c.conkey) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.id ORDER BY k.ord)
                            = ARRAY[{(name == SourceTable ? "'id'" : "'attempt','backend'")}]::name[])
                THEN RAISE EXCEPTION 'resource_provider_private_catalog_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
    }

    private static string TriggerGuard(ExploreDbContext context, string table, string operation, int type, bool deferred) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = {Text(table)}::regclass
            AND t.tgname = '{Prefix}{operation}' AND NOT t.tgisinternal AND t.tgtype = {type} AND t.tgenabled = 'A'
            AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, operation) + "()")})
            AND t.tgqual IS NULL AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector
            AND t.tgconstrrelid = 0 AND t.tgconstrindid = 0 AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL
            AND t.tgdeferrable = {deferred.ToString().ToLowerInvariant()} AND t.tginitdeferred = {deferred.ToString().ToLowerInvariant()}
            AND {(deferred ? "t.tgconstraint <> 0" : "t.tgconstraint = 0")})
        THEN RAISE EXCEPTION 'resource_provider_trigger_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(Body(context, enrollment, routine.Name)))})
            THEN RAISE EXCEPTION 'resource_provider_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
