using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Explore.Persistence.Schema;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresPrivateEventManagementContract
{
    private static readonly bool[] PolicyModes = [false, true];

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var definition = Define(context, enrollment);
        var sql = definition.Sql;
        string roles = string.Join(",", Roles.Select(Text));
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(sql.Names.Table(type)) + "::regclass"));
        string memberships = string.Join("\n", definition.Privileges.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {Boolean(edge.Inherit)}, SET {Boolean(edge.Set)}, ADMIN FALSE',
                {Text(edge.Parent)},{Text(edge.Member)});
            """));
        string policies = string.Join("\n", definition.Policies.SelectMany(pair => PolicyModes.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                CREATE POLICY {name} ON {sql.Names.Table(pair.Key)} AS {(permissive ? "PERMISSIVE" : "RESTRICTIVE")}
                    FOR SELECT TO {OwnerRole} USING ({pair.Value});
                EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',p.polname,
                    {Text(sql.Names.Table(pair.Key))},{Text(Stamp + Hash(pair.Value) + ":")} || pg_catalog.md5(p.polqual::text))
                    FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = '{name}');
                """;
        })));
        string grants = string.Join("\n", sql.Sources.Select(source =>
            $"GRANT SELECT ({string.Join(",", source.Value.Order(StringComparer.Ordinal).Select(property => sql.Names.Column(source.Key, property)))}) ON {sql.Names.Table(source.Key)} TO {OwnerRole};"));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'private_management_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, sql, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(sql.Names.Schema)} AND p.proname = '{Routine}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid IN ({sources})
                            AND p.polname IN ('{Policy}','{Policy}_allow'))
                    THEN RAISE EXCEPTION 'private_management_partial_installation' USING ERRCODE = '55000'; END IF;
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    END LOOP;
                    {memberships}
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                            WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                        THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                    END LOOP;
                    {policies}
                ELSE
                    IF existing <> 3 THEN RAISE EXCEPTION 'private_management_partial_roles' USING ERRCODE = '55000'; END IF;
                    {definition.Guard}
                    {ExactBody(context, definition.Body)}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {sql.Names.Quote(sql.Names.Schema)} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {sql.Names.Quote(sql.Names.Schema)} TO {OwnerRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {FunctionSql(context)}(
                        p_tenant uuid,p_principal uuid,p_kind integer,p_event uuid,p_session uuid,p_page integer,p_size integer,p_type integer)
                        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                        AS $body${definition.Body}$body$;
                    REVOKE ALL ON FUNCTION {FunctionSql(context)}({Signature}) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {FunctionSql(context)}({Signature}) TO {RuntimeRole};
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Names.Quote(sql.Names.Schema)} FROM {OwnerRole};
            DO $installed$ BEGIN {definition.Guard} {ExactBody(context, definition.Body)} END $installed$;
            """;
    }

    private static IReadOnlyDictionary<Type, string> Policies(ExploreDbContext context, GraphSql sql)
    {
        var result = new Dictionary<Type, string>();
        var tables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        while (sql.Sources.Keys.FirstOrDefault(type => !result.ContainsKey(type)) is { } type)
        {
            var entity = context.Model.FindEntityType(type)!;
            var table = tables.SingleOrDefault(t => t.TableName == entity.GetTableName());
            result.Add(type, table is null ? "true"
                : table.ParentJoin is { } parent ? ParentPolicy(parent, sql.Names.Table(type), 0)
                : Tenant(table.TenantIdColumn!, table.AllowsNullTenant));
        }
        return result;

        string Tenant(string column, bool nullable)
        {
            string value = sql.Names.Quote(column);
            string match = value + " = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
            return nullable ? $"({value} IS NULL OR {match})" : match;
        }
        string ParentPolicy(TenantParentJoinMetadata parent, string child, int depth)
        {
            string alias = "private_parent_" + depth;
            var entity = context.Model.GetEntityTypes().Single(e => e.GetTableName() == parent.ParentTableName);
            string Register(string column)
            {
                var property = entity.GetProperties().Single(p => sql.Names.ColumnName(entity.ClrType, p.Name) == column);
                return sql.Column(entity.ClrType, alias, property.Name);
            }
            string key = Register(parent.ParentKeyColumn);
            string tenantColumn = parent.ParentTenantIdColumn is { } column ? Register(column) : "";
            string predicate = parent.ParentJoin is { } next ? ParentPolicy(next, alias, depth + 1)
                : parent.ParentAllowsNullTenant
                    ? $"({tenantColumn} IS NULL OR {tenantColumn} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid)"
                    : tenantColumn + " = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
            return $"EXISTS (SELECT 1 FROM {sql.Names.Name(parent.ParentTableName)} {alias} WHERE {key} = {child}.{sql.Names.Quote(parent.ForeignKeyColumn)} AND {predicate})";
        }
    }

    private static string PolicyGuard(GraphSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.SelectMany(pair => PolicyModes.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = {Text(name)}
                        AND p.polcmd = 'r' AND p.polpermissive = {Boolean(permissive)}
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND p.polqual IS NOT NULL AND p.polwithcheck IS NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(Stamp + Hash(pair.Value) + ":")} || pg_catalog.md5(p.polqual::text))
                THEN RAISE EXCEPTION 'private_management_policy_drift' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass
                        AND p.polname NOT IN ('tenant_isolation','{Policy}','{Policy}_allow')
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE')))
                THEN RAISE EXCEPTION 'private_management_unexpected_policy' USING ERRCODE = '55000'; END IF;
                """;
        })));

    private static string CallerPrivileges(GraphSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(sql.Names.Table(type)) + "::regclass"));
        return $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                    OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(sql.Names.Schema)}),'CREATE')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r WHERE t.oid IN ({sources})
                    AND (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.oid = t.relowner
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
            THEN RAISE EXCEPTION 'private_management_runtime_source_privilege' USING ERRCODE = '42501'; END IF;
            """;
    }

    private static string SourceGuard(ExploreDbContext context, GraphSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        var tables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'private_management_namespace_drift' USING ERRCODE = '55000'; END IF;
            """ + string.Join("\n", sql.Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Key)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Key)!;
            var tenant = tables.SingleOrDefault(t => t.TableName == entity.GetTableName());
            string table = Text(sql.Names.Table(source.Key));
            string columns = string.Join(" OR ", source.Value.Select(name =>
            {
                var property = entity.FindProperty(name)!;
                var declaredProperty = declared.FindProperty(name)!;
                string? computed = declaredProperty.GetComputedColumnSql();
                string generated = computed is null ? "" : declaredProperty.GetIsStored() is true ? "s" : "v";
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declaredProperty) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                    || property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attname = {Text(sql.Names.ColumnName(source.Key, name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {Boolean(!property.IsNullable)} AND a.attgenerated = {Text(generated)}
                        AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Names.Quote(collation))}))
                        {(computed is null ? "" : $"""
                            AND EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d WHERE d.adrelid = a.attrelid AND d.adnum = a.attnum
                                AND pg_catalog.pg_get_expr(d.adbin,d.adrelid) = {Text(computed)})
                            """)})
                    """;
            }));
            string primary = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(p => Text(sql.Names.ColumnName(source.Key, p.Name))));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND t.relrowsecurity = {Boolean(tenant is not null)} AND t.relforcerowsecurity = {Boolean(tenant is not null)})
                    OR {columns}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = {table}::regclass)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = {table}::regclass OR i.inhparent = {table}::regclass)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = {table}::regclass
                        AND i.indisprimary AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = ARRAY[{primary}]::name[])
                THEN RAISE EXCEPTION 'private_management_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {(tenant is null ? "" : $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                        AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Names.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    THEN RAISE EXCEPTION 'private_management_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                    """)}
                """;
        }));
    }
}
