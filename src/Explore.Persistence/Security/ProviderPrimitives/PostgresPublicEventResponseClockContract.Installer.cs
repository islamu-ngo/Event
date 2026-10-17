using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventResponseClockContract
{
    private sealed record Definition(SourceSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("public_clock_enrollment_collision");
        var sql = new SourceSql(context);
        string queries = Queries(sql);
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
                THEN RAISE EXCEPTION 'public_clock_caller_mismatch' USING ERRCODE = '42501'; END IF;
                IF p_operation IS NULL OR p_now IS NULL OR p_threshold IS NULL
                    OR p_address IS NULL OR p_coordinates IS NULL
                THEN RAISE EXCEPTION 'public_clock_arguments_required' USING ERRCODE = '22023'; END IF;
                {queries}
            END;
            """;
        return new(sql, privileges, policies, guard, body);
    }

    /// <summary>Migration-only installation after source tables, lookups and tenant RLS.</summary>
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
                THEN RAISE EXCEPTION 'public_clock_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, sql, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(names.Schema)} AND p.proname = '{Routine}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid IN ({sources})
                            AND p.polname IN ('{Policy}','{Policy}_allow'))
                    THEN RAISE EXCEPTION 'public_clock_partial_installation' USING ERRCODE = '55000'; END IF;
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
                    IF existing <> 3 THEN RAISE EXCEPTION 'public_clock_partial_roles' USING ERRCODE = '55000'; END IF;
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
                    CREATE OR REPLACE FUNCTION {FunctionSql(context)}(
                        p_tenant uuid,p_operation text,p_now timestamptz,p_threshold timestamptz,p_address boolean,p_coordinates boolean)
                        RETURNS timestamptz LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
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

    private sealed class SourceSql(ExploreDbContext context)
    {
        internal HumanPrincipalSourceSql Names { get; } = new(context);
        internal Dictionary<Type, HashSet<string>> Sources { get; } = [];
        internal string Table<T>() => Names.Table(typeof(T));
        internal string Column<T>(string alias, string property) => Column(typeof(T), alias, property);
        private string Column(Type type, string alias, string property)
        {
            if (context.Model.FindEntityType(type)?.FindProperty(property) is null)
                throw new InvalidOperationException($"public_clock_unmapped_source:{type.Name}.{property}");
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
            string alias = "clock_parent_" + depth;
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
