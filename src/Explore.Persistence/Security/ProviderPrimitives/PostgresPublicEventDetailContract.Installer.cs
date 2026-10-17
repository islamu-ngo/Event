using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventDetailContract
{
    private sealed record Definition(SourceSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("public_detail_enrollment_collision");
        var sql = new SourceSql(context);
        string query = $"""
            SELECT {Projection(sql)} FROM {sql.Table<Event>()} e WHERE {Eligible(sql)}
                AND (p_id IS NOT NULL AND {sql.Column<Event>("e", "Id")} = p_id
                    OR p_code IS NOT NULL AND {sql.Column<Event>("e", "PublicCode")} = p_code)
            """;
        var policies = sql.Policies();
        var privileges = new PostgresPrivilegeGuard.Contract(sql.Names.Schema, Roles,
            [new(Routine, Signature, OwnerRole, [RuntimeRole])],
            sql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Key)!.GetTableName()!, OwnerRole, [],
                source.Value.Order(StringComparer.Ordinal).Select(property => new PostgresPrivilegeGuard.Column(
                    sql.Names.ColumnName(source.Key, property), ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(privileges)
            + SourceGuard(context, sql, enrollment) + PolicyGuard(sql, policies) + CallerPrivilegeGuard(sql, enrollment);
        string body = $"""
            BEGIN
                {guard}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.RuntimeLogin)}
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    OR p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
                THEN RAISE EXCEPTION 'public_detail_caller_mismatch' USING ERRCODE = '42501'; END IF;
                IF (p_id IS NULL) = (p_code IS NULL) OR p_id = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_code IS NOT NULL AND (length(p_code) = 0 OR length(p_code) > 128)
                THEN RAISE EXCEPTION 'public_detail_selector_invalid' USING ERRCODE = '22023'; END IF;
                RETURN ({query});
            END;
            """;
        return new(sql, privileges, policies, guard, body);
    }

    /// <summary>Install after the model, lookup seed and tenant RLS, under declared Migrator only.</summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var definition = Define(context, enrollment);
        var sql = definition.Sql;
        var names = sql.Names;
        string roles = string.Join(",", Roles.Select(Text));
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(names.Table(type)) + "::regclass"));
        string memberships = string.Join("\n", definition.Privileges.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {Boolean(edge.Inherit)}, SET {Boolean(edge.Set)}, ADMIN FALSE',
                {Text(edge.Parent)},{Text(edge.Member)});
            """));
        string policies = string.Join("\n", definition.Policies.SelectMany(pair => new[] { false, true }.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                CREATE POLICY {name} ON {names.Table(pair.Key)} AS {(permissive ? "PERMISSIVE" : "RESTRICTIVE")}
                    FOR SELECT TO {OwnerRole} USING ({pair.Value});
                EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',p.polname,
                    {Text(names.Table(pair.Key))},{Text(Stamp + Hash(pair.Value) + ":")} || pg_catalog.md5(p.polqual::text))
                    FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(names.Table(pair.Key))}::regclass AND p.polname = '{name}');
                """;
        })));
        string grants = string.Join("\n", sql.Sources.Select(source =>
            $"GRANT SELECT ({string.Join(",", source.Value.Order(StringComparer.Ordinal).Select(property => names.Column(source.Key, property)))}) ON {names.Table(source.Key)} TO {OwnerRole};"));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'public_detail_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, sql, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(names.Schema)} AND p.proname = '{Routine}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid IN ({sources})
                            AND p.polname IN ('{Policy}','{Policy}_allow'))
                    THEN RAISE EXCEPTION 'public_detail_partial_installation' USING ERRCODE = '55000'; END IF;
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
                    IF existing <> 3 THEN RAISE EXCEPTION 'public_detail_partial_roles' USING ERRCODE = '55000'; END IF;
                    {definition.Guard}
                    {ExactBody(context, definition.Body)}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {names.Quote(names.Schema)} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {OwnerRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {FunctionSql(context)}(p_tenant uuid,p_id uuid,p_code text)
                        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                        AS $body${definition.Body}$body$;
                    REVOKE ALL ON FUNCTION {FunctionSql(context)}({Signature}) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {FunctionSql(context)}({Signature}) TO {RuntimeRole};
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {OwnerRole};
            DO $installed$ BEGIN {definition.Guard} {ExactBody(context, definition.Body)} END $installed$;
            """;
    }

    private static string CallerPrivilegeGuard(SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(sql.Names.Table(type)) + "::regclass"));
        return $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                    OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r WHERE t.oid IN ({sources})
                    AND (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.oid = t.relowner
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                        AND pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(sql.Names.Schema)}),'CREATE'))
            THEN RAISE EXCEPTION 'public_detail_runtime_source_privilege' USING ERRCODE = '42501'; END IF;
            """;
    }

    private static string PolicyGuard(SourceSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.SelectMany(pair => new[] { false, true }.Select(permissive =>
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
                THEN RAISE EXCEPTION 'public_detail_policy_drift' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass
                        AND p.polname NOT IN ('tenant_isolation','{Policy}','{Policy}_allow')
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE')))
                THEN RAISE EXCEPTION 'public_detail_unexpected_policy' USING ERRCODE = '55000'; END IF;
                """;
        })));

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'public_detail_namespace_drift' USING ERRCODE = '55000'; END IF;
            """ + string.Join("\n", sql.Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Key)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Key)!;
            var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
            string table = Text(sql.Names.Table(source.Key));
            string columns = string.Join(" OR ", source.Value.Select(name =>
            {
                var property = entity.FindProperty(name)!;
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                    || property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declared.FindProperty(name)!) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attname = {Text(sql.Names.ColumnName(source.Key, name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {Boolean(!property.IsNullable)} AND a.attgenerated = '' AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Names.Quote(collation))})))
                    """;
            }));
            string primary = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(property =>
                Text(sql.Names.ColumnName(source.Key, property.Name))));
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
                THEN RAISE EXCEPTION 'public_detail_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {(tenant is null ? "" : $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                        AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Names.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    THEN RAISE EXCEPTION 'public_detail_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                    """)}
                """;
        }));
    }

    private sealed class SourceSql(ExploreDbContext context)
    {
        internal HumanPrincipalSourceSql Names { get; } = new(context);
        internal Dictionary<Type, HashSet<string>> Sources { get; } = [];
        internal string Table<T>() => Names.Table(typeof(T));
        internal string Column<T>(string alias, string property) => Column(typeof(T), alias, property);
        private string Column(Type type, string alias, string property)
        {
            if (context.Model.FindEntityType(type)?.FindProperty(property) is null)
                throw new InvalidOperationException($"public_detail_unmapped_source:{type.Name}.{property}");
            if (!Sources.TryGetValue(type, out var fields)) Sources.Add(type, fields = []);
            fields.Add(property);
            return Names.Quote(alias) + "." + Names.Column(type, property);
        }
        internal string Live<T>(string alias)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var clauses = new List<string>();
            if (entity.FindProperty("TenantId") is not null)
            {
                var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                    .SingleOrDefault(table => table.TableName == entity.GetTableName());
                string column = Column<T>(alias, "TenantId");
                clauses.Add(tenant?.AllowsNullTenant is true
                    ? $"({column} IS NULL OR {column} = p_tenant)" : column + " = p_tenant");
            }
            if (entity.FindProperty("IsDeleted") is not null) clauses.Add("NOT " + Column<T>(alias, "IsDeleted"));
            return clauses.Count == 0 ? "true" : string.Join(" AND ", clauses);
        }
        internal string Projection<T>(string alias, params string[] properties) =>
            "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                Text(property) + "," + Column<T>(alias, property))) + ")";

        internal IReadOnlyDictionary<Type, string> Policies()
        {
            var result = new Dictionary<Type, string>();
            var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
            while (Sources.Keys.FirstOrDefault(type => !result.ContainsKey(type)) is { } type)
            {
                var entity = context.Model.FindEntityType(type)!;
                var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
                string predicate = tenant is null ? "true"
                    : tenant.ParentJoin is { } parent ? ParentPolicy(parent, Names.Table(type), 0)
                    : tenant.AllowsNullTenant
                        ? $"({Names.Quote(tenant.TenantIdColumn!)} IS NULL OR {Names.Quote(tenant.TenantIdColumn!)} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid)"
                        : $"{Names.Quote(tenant.TenantIdColumn!)} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
                if (type == typeof(Event))
                    predicate += $" AND NOT {Names.Column(type, "IsDeleted")} AND {Names.Column(type, "VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public}";
                result.Add(type, predicate);
            }
            return result;
        }

        private string ParentPolicy(TenantParentJoinMetadata parent, string child, int depth)
        {
            string alias = "detail_parent_" + depth;
            var entity = context.Model.GetEntityTypes().Single(entity => entity.GetTableName() == parent.ParentTableName);
            string Register(string name)
            {
                var property = entity.GetProperties().Single(property => Names.ColumnName(entity.ClrType, property.Name) == name);
                return Column(entity.ClrType, alias, property.Name);
            }
            string key = Register(parent.ParentKeyColumn);
            string tenantColumn = parent.ParentTenantIdColumn is { } column ? Register(column) : "";
            string tenant = parent.ParentJoin is { } next ? ParentPolicy(next, alias, depth + 1)
                : parent.ParentAllowsNullTenant
                    ? $"({tenantColumn} IS NULL OR {tenantColumn} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid)"
                    : tenantColumn + " = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
            return $"EXISTS (SELECT 1 FROM {Names.Name(parent.ParentTableName)} {alias} WHERE {key} = {child}.{Names.Quote(parent.ForeignKeyColumn)} AND {tenant})";
        }
    }
}
