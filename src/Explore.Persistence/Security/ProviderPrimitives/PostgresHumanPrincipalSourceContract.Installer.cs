using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanPrincipalSourceContract
{
    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string tables = string.Join("\n", Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string table = sql.Table(source.Type);
            string columns = string.Join(" OR ", source.Columns.Select(property =>
            {
                var mapped = entity.FindProperty(property)!;
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(table)})
                        AND a.attname = {Text(sql.ColumnName(source.Type, property))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND a.atttypid = pg_catalog.to_regtype({Text(mapped.GetRelationalTypeMapping().StoreType)})
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(mapped.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(mapped.IsNullable ? "false" : "true")}
                        AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                    """;
            }));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    WHERE t.oid = pg_catalog.to_regclass({Text(table)}) AND t.relkind = 'r'
                        AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({Text(table)}))
                    OR {columns}
                THEN RAISE EXCEPTION 'human_principal_source_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string ForeignKeyColumns(IReadOnlyList<IProperty> properties, Type type) =>
            "ARRAY[" + string.Join(",", properties.Select(property => Text(sql.ColumnName(type, property.Name)))) + "]::name[]";
        string constraints = string.Join("\n", Sources.SelectMany(source =>
            context.Model.FindEntityType(source.Type)!.GetForeignKeys()
                .Where(key => Sources.Any(parent => parent.Type == key.PrincipalEntityType.ClrType))
                .Select(key => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint fk
                        WHERE fk.contype = 'f' AND fk.convalidated AND NOT fk.condeferrable
                            AND fk.confmatchtype = 's' AND fk.confupdtype = 'a'
                            AND fk.confdeltype = '{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c"
                                : key.DeleteBehavior == DeleteBehavior.SetNull ? "n"
                                : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                            AND fk.conrelid = pg_catalog.to_regclass({Text(sql.Table(source.Type))})
                            AND fk.confrelid = pg_catalog.to_regclass({Text(sql.Table(key.PrincipalEntityType.ClrType))})
                            AND ARRAY(SELECT a.attname FROM unnest(fk.conkey) WITH ORDINALITY k(id, ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {ForeignKeyColumns(key.Properties, source.Type)}
                            AND ARRAY(SELECT a.attname FROM unnest(fk.confkey) WITH ORDINALITY k(id, ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.confrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {ForeignKeyColumns(key.PrincipalKey.Properties, key.PrincipalEntityType.ClrType)}
                            AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint = fk.oid)
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                                WHERE t.tgconstraint = fk.oid AND t.tgenabled NOT IN ('O','A')))
                    THEN RAISE EXCEPTION 'human_principal_fk_drift' USING ERRCODE = '55000'; END IF;
                    """)));
        // Local FirstOrDefault owner queries are single-valued only under the real membership
        // unique indexes and placement alternate keys. Reject losing those guarantees.
        string unique = string.Join("\n", Sources.SelectMany(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            var keys = entity.GetKeys().Select(key => key.Properties)
                .Concat(entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null)
                    .Select(index => index.Properties));
            return keys.Select(properties => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i
                    WHERE i.indrelid = pg_catalog.to_regclass({Text(sql.Table(source.Type))})
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indpred IS NULL AND i.indexprs IS NULL
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id, ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id
                            WHERE k.ord <= i.indnkeyatts ORDER BY k.ord) = {ForeignKeyColumns(properties, source.Type)})
                THEN RAISE EXCEPTION 'human_principal_unique_drift' USING ERRCODE = '55000'; END IF;
                """);
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'human_principal_schema_drift' USING ERRCODE = '55000'; END IF;
            {tables}
            {constraints}
            {unique}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        (string Name, string Signature, string Result)[] routines =
            [(Observe, Signature, "jsonb"), (Acquire, Signature, "jsonb"),
                (Session, SessionSignature, "jsonb"), (Write, "", "trigger"), (SettingWrite, "", "trigger")];
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
            THEN RAISE EXCEPTION 'human_principal_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Sources.Select(source =>
        {
            string table = sql.Table(source.Type);
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string tenantGuard = tenant is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({Text(table)})
                    AND t.relrowsecurity AND t.relforcerowsecurity)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}'
                            AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                            AND pg_catalog.obj_description(p.oid,'pg_policy') =
                                {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'human_principal_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                {tenantGuard}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{ReadPolicy}'
                        AND p.polpermissive AND p.polcmd = 'r' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{UpdatePolicy}'
                            AND p.polpermissive AND p.polcmd = 'w' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                            AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                            AND p.polname NOT IN ('{ReadPolicy}','{UpdatePolicy}'
                                {(tenant is null ? "" : $",'{PostgresTenantRowLevelSecurityModel.PolicyName}'")}))
                THEN RAISE EXCEPTION 'human_principal_rls_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string triggerChecks = string.Join("\n", Sources.Where(source => source.Type != typeof(RolePermission)).Select(source =>
        {
            string table = sql.Table(source.Type);
            bool setting = source.Type == typeof(SystemSetting);
            string Check(string name, string function, int kind) => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({Text(table)}) AND t.tgname = '{name}'
                        AND t.tgfoid = pg_catalog.to_regprocedure({Text(sql.Name(function) + "()")})
                        AND t.tgtype = {kind} AND t.tgenabled = 'A' AND NOT t.tgisinternal
                        AND t.tgnargs = 0 AND t.tgqual IS NULL AND t.tgattr = ''::int2vector
                        AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
                THEN RAISE EXCEPTION 'human_principal_writer_drift' USING ERRCODE = '55000'; END IF;
                """;
            return setting ? Check(SettingWrite, SettingWrite, 29) + Check(Write, Write, 34)
                : Check(Write, Write, 62);
        }));
        string anchor = sql.Name(Anchor);
        return $"""
            {PostgresManagedProviderProvisioningContract.SharedWriterAssertionSql(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {SourceGuard(context, enrollment)}
            {routineChecks}
            {policies}
            {triggerChecks}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                WHERE t.oid = pg_catalog.to_regclass({Text(anchor)}) AND t.relkind = 'r'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND a.attname = 'id' AND a.atttypid = 'smallint'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND a.attname = 'revision' AND a.atttypid = 'boolean'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable
                    AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({Text(anchor)})
                    AND c.contype = 'c' AND c.convalidated AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({Text(anchor)}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({Text(anchor)}))
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
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND d.deptype = 'o' AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                        OR d.objid NOT IN ({routineIds})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIds})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'human_principal_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    /// <summary>Install through the declared migrator, before traffic; never repairs an existing drifted contract.</summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE', {Text(edge.Parent)}, {Text(edge.Member)});
            """));
        string grants = string.Join("\n", Sources.Select(source => $"""
            GRANT SELECT ({string.Join(",", source.Columns.Select(property => sql.Column(source.Type, property)))})
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
            {(function.Result == "jsonb" ? $"GRANT EXECUTE ON FUNCTION {sql.Name(function.Name)}({function.Signature}) TO {RuntimeRole}{(function.Name == Session ? "" : "," + GlobalRole)};" : "")}
            COMMENT ON FUNCTION {sql.Name(function.Name)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        string triggers = string.Join("\n", Sources.Where(source => source.Type != typeof(RolePermission)).Select(source =>
        {
            string table = sql.Table(source.Type);
            string regular = $"""
                DROP TRIGGER IF EXISTS {Write} ON {table};
                CREATE TRIGGER {Write} BEFORE {(source.Type == typeof(SystemSetting) ? "TRUNCATE" : "INSERT OR UPDATE OR DELETE OR TRUNCATE")}
                    ON {table} FOR EACH STATEMENT EXECUTE FUNCTION {sql.Name(Write)}();
                ALTER TABLE {table} ENABLE ALWAYS TRIGGER {Write};
                """;
            return source.Type != typeof(SystemSetting) ? regular : regular + $"""

                DROP TRIGGER IF EXISTS {SettingWrite} ON {table};
                CREATE TRIGGER {SettingWrite} AFTER INSERT OR UPDATE OR DELETE
                    ON {table} FOR EACH ROW EXECUTE FUNCTION {sql.Name(SettingWrite)}();
                ALTER TABLE {table} ENABLE ALWAYS TRIGGER {SettingWrite};
                """;
        }));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; v_count integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'human_principal_migrator_mismatch' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO v_count FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF v_count = 0 THEN
                    IF pg_catalog.to_regclass({Text(sql.Name(Anchor))}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE 'human_principal_source_%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname IN ('{ReadPolicy}','{UpdatePolicy}'))
                    THEN RAISE EXCEPTION 'human_principal_bootstrap_artifacts' USING ERRCODE = '55000'; END IF;
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
                    IF v_count <> {Roles.Length} THEN RAISE EXCEPTION 'human_principal_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                END IF;
            END $bootstrap$;
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
            GRANT EXECUTE ON FUNCTION {sql.Name(Write)}(), {sql.Name(SettingWrite)}() TO {sql.Quote(enrollment.MigratorLogin)};
            {triggers}
            REVOKE EXECUTE ON FUNCTION {sql.Name(Write)}(), {sql.Name(SettingWrite)}() FROM {sql.Quote(enrollment.MigratorLogin)};
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
