using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresDiscoveryTraversalContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("discovery_traversal_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Name,
                routine.Signature, OwnerRole, [RuntimeRole])).ToArray(),
            Sources.Select(type => new PostgresPrivilegeGuard.Relation(context.Model.FindEntityType(type)!.GetTableName()!,
                OwnerRole,
                type == typeof(EventDiscoverySnapshot) || type == typeof(EventDiscoverySnapshotItem) ? ["SELECT", "INSERT", "DELETE"]
                    : type == typeof(EventDiscoveryRevision) || type == typeof(EventDiscoverySnapshotReservation) ? ["SELECT", "INSERT", "UPDATE"]
                    : type == typeof(Tenant) ? [] : ["SELECT"],
                type == typeof(Tenant) ? [new(sql.ColumnName(type, "Id"), ["SELECT", "UPDATE"])] : [])).ToArray(),
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)
            ]);
    }

    /// <summary>Fixed migration SQL, generated from mapped identifiers and explicit deployment enrollment only.</summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", contract.Relations.Select(relation =>
            (relation.Privileges.Count == 0 ? "" : $"GRANT {string.Join(",", relation.Privileges)} ON {sql.Name(relation.Name)} TO {OwnerRole};")
            + "\n" + string.Join("\n", relation.Columns.Select(column =>
                $"GRANT {string.Join(",", column.Privileges.Select(privilege => privilege + " (" + sql.Quote(column.Name) + ")"))} ON {sql.Name(relation.Name)} TO {OwnerRole};"))));
        string definitions = string.Join("\n", Routines.Select(routine =>
        {
            string body = Body(context, enrollment, routine.Name);
            string result = routine.Entity is null ? routine.Scalar : "SETOF " + sql.Table(routine.Entity);
            return $"""
                CREATE OR REPLACE FUNCTION {FunctionSql(context, routine.Name)}({routine.Arguments}) RETURNS {result}
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
                REVOKE ALL ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) TO {RuntimeRole};
                COMMENT ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) IS {Text(Stamp + Hash(body))};
                """;
        }));
        string policies = string.Join("\n", Sources.Select(type => $"""
            DROP POLICY IF EXISTS {Policy} ON {sql.Table(type)};
            CREATE POLICY {Policy} ON {sql.Table(type)} FOR ALL TO {OwnerRole}
                USING (true) WITH CHECK (true);
            COMMENT ON POLICY {Policy} ON {sql.Table(type)} IS '{Stamp}policy';
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; installed integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'discovery_traversal_original_migrator' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO installed FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF installed = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                        WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname = '{Policy}'
                            AND p.polrelid IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))}))
                    THEN RAISE EXCEPTION 'discovery_traversal_partial_installation' USING ERRCODE = '55000'; END IF;
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
                ELSE
                    IF installed <> {Roles.Length} THEN RAISE EXCEPTION 'discovery_traversal_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
            {grants}
            GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
            {policies}
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string tables = string.Join("\n", Sources.Select(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
            var properties = type == typeof(Tenant)
                ? new[] { entity.FindProperty("Id")! } : entity.GetProperties().ToArray();
            string table = Text(sql.Table(type));
            string fields = string.Join(" OR ", properties.Select(property => $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attname = {Text(sql.ColumnName(type, property.Name))} AND a.attnum > 0 AND NOT a.attisdropped
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                    AND a.attnotnull = {(!property.IsNullable).ToString().ToLowerInvariant()} AND a.attgenerated = '' AND a.attidentity = ''
                    AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                """));
            string Names(IReadOnlyList<IProperty> values, Type owner) =>
                $"ARRAY[{string.Join(",", values.Select(property => Text(sql.ColumnName(owner, property.Name))))}]::name[]";
            var indexes = entity.GetKeys().Select(key => (key.Properties, Unique: true, Filter: (string?)null))
                .Concat(entity.GetIndexes().Select(index => (index.Properties, Unique: index.IsUnique, Filter: index.GetFilter())));
            string keys = string.Join("\n", indexes.Where(index => type != typeof(Tenant) || index.Properties.All(property => property.Name == "Id"))
                .Select(index => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                        AND i.indisunique = {index.Unique.ToString().ToLowerInvariant()} AND i.indisvalid AND i.indisready
                        AND i.indimmediate AND i.indexprs IS NULL AND i.indnkeyatts = {index.Properties.Count} AND i.indnatts = {index.Properties.Count}
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indoption::smallint[]) flag WHERE flag <> 0)
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                            JOIN pg_catalog.pg_opclass o ON o.oid = operator_id JOIN pg_catalog.pg_am am ON am.oid = o.opcmethod
                            WHERE NOT o.opcdefault OR am.amname <> 'btree')
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                            JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum WHERE col.value <> a.attcollation)
                        AND {(index.Filter is null ? "i.indpred IS NULL" : $"pg_catalog.pg_get_expr(i.indpred,i.indrelid) = {Text("(" + index.Filter + ")")}")}
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {Names(index.Properties, type)})
                    THEN RAISE EXCEPTION 'discovery_traversal_source_key_drift' USING ERRCODE = '55000'; END IF;
                    """));
            string foreignKeys = type == typeof(Tenant) ? "" : string.Join("\n", entity.GetForeignKeys().Select(key => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                    AND c.contype = 'f' AND c.convalidated AND NOT c.condeferrable AND NOT c.condeferred
                    AND c.confrelid = {Text(sql.Table(key.PrincipalEntityType.ClrType))}::regclass
                    AND c.confmatchtype = 's' AND c.confupdtype = 'a'
                    AND c.confdeltype = '{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c" : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                    AND ARRAY(SELECT a.attname FROM unnest(c.conkey) WITH ORDINALITY k(id,ord)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.id ORDER BY k.ord) = {Names(key.Properties, type)}
                    AND ARRAY(SELECT a.attname FROM unnest(c.confkey) WITH ORDINALITY k(id,ord)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = k.id ORDER BY k.ord)
                        = {Names(key.PrincipalKey.Properties, key.PrincipalEntityType.ClrType)}
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint = c.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                        WHERE t.tgconstraint = c.oid AND t.tgenabled NOT IN ('O','A')))
                THEN RAISE EXCEPTION 'discovery_traversal_source_fk_drift' USING ERRCODE = '55000'; END IF;
                """));
            string policy = tenant is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table})
                    AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}' AND p.polcmd = '*'
                    AND p.polroles = ARRAY[0::oid] AND p.polpermissive AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy') =
                        {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'discovery_traversal_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND t.relrowsecurity = {(tenant is not null).ToString().ToLowerInvariant()}
                    AND t.relforcerowsecurity = {(tenant is not null).ToString().ToLowerInvariant()})
                    OR {fields}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = pg_catalog.to_regclass({table})
                        OR i.inhparent = pg_catalog.to_regclass({table}))
                    {(type == typeof(Tenant) ? "" : $"""
                        OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                            AND a.attnum > 0 AND NOT a.attisdropped) <> {properties.Length}
                        OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table}))
                            <> {entity.GetKeys().Count() + entity.GetIndexes().Count()}
                        OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                            AND c.contype = 'f') <> {entity.GetForeignKeys().Count()}
                        """)}
                THEN RAISE EXCEPTION 'discovery_traversal_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {keys}
                {foreignKeys}
                {policy}
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'discovery_traversal_namespace_owner' USING ERRCODE = '55000'; END IF;
            {tables}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string ids = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})"));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND f.proretset = {(routine.Entity is not null).ToString().ToLowerInvariant()}
                    AND f.prorettype = {Text(routine.Entity is null ? routine.Scalar switch
        {
            "integer" => "pg_catalog.int4",
            "uuid" => "pg_catalog.uuid",
            "void" => "pg_catalog.void",
            _ => throw new InvalidOperationException("Unsupported traversal scalar result type.")
        } : sql.Table(routine.Entity))}::regtype
                    AND f.proargdefaults IS NULL AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                    AND f.proargnames = ARRAY[{string.Join(",", routine.Arguments.Split(',').Select(argument => Text(argument.Split(' ')[0])))}]::text[]
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'discovery_traversal_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string policies = string.Join("\n", Sources.Select(type => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(type))}::regclass
                AND p.polname = '{Policy}' AND p.polcmd = '*' AND p.polpermissive
                AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true'
                AND pg_catalog.obj_description(p.oid,'pg_policy') = '{Stamp}policy')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(type))}::regclass
                    AND p.polname NOT IN ('{Policy}','{PostgresTenantRowLevelSecurityModel.PolicyName}')
                    AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) r
                        WHERE pg_catalog.pg_has_role('{OwnerRole}',r,'USAGE'))))
            THEN RAISE EXCEPTION 'discovery_traversal_owner_policy_drift' USING ERRCODE = '55000'; END IF;
            IF {(type == typeof(EventDiscoveryRevision) ? $"NOT ({PostgresDiscoveryMetadataPermissions.RevisionAuthorityPredicate(context, enrollment.RuntimeLogin)})" : $"""
                pg_catalog.has_table_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Table(type))}::regclass,
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR pg_catalog.has_any_column_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Table(type))}::regclass,
                    'SELECT,INSERT,UPDATE,REFERENCES')
                """)}
            THEN RAISE EXCEPTION 'discovery_traversal_runtime_table_access' USING ERRCODE = '55000'; END IF;
            """));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(contract)}
            {routines}
            {policies}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles}) AND n.nspname <> {Text(sql.Schema)}
                        AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                        AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) AND d.deptype = 'o'
                    AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication))
                OR pg_catalog.has_schema_privilege({Text(enrollment.RuntimeLogin)},{Text(sql.Schema)},'CREATE')
                OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},'{PostgresDiscoveryDisclosureContract.RuntimeRole}','MEMBER')
            THEN RAISE EXCEPTION 'discovery_traversal_closed_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex')
                        = {Text(Hash(Body(context, enrollment, routine.Name)))})
            THEN RAISE EXCEPTION 'discovery_traversal_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
