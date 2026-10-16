using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresGlobalLookupCatalogueContract
{
    private sealed record Definition(HumanPrincipalSourceSql Names, PostgresPrivilegeGuard.Contract Privileges,
        string Predicate, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("global_lookup_enrollment_collision");
        foreach (var lookup in Lookups) Resolve(context, lookup.Type);
        var names = new HumanPrincipalSourceSql(context);
        string predicate = names.Column(typeof(EventType), nameof(EventType.TenantId)) + " IS NULL";
        var privileges = new PostgresPrivilegeGuard.Contract(names.Schema, Roles,
            [new(Routine, "integer", OwnerRole, [RuntimeRole])],
            Lookups.Select(lookup => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(lookup.Type)!.GetTableName()!, OwnerRole, [],
                lookup.Properties.Select(property => new PostgresPrivilegeGuard.Column(
                    names.ColumnName(lookup.Type, property), ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(privileges)
            + SourceGuard(context, names, enrollment) + PolicyGuard(names, predicate)
            + CallerPrivilegeGuard(names, enrollment);
        string branches = string.Join("\n", Lookups.Select(lookup =>
        {
            string columns = string.Join(",", ResultColumns.Select(column => lookup.Properties.Contains(column.Name)
                ? $"source.{names.Column(lookup.Type, column.Name)}::{column.StoreType}"
                : $"NULL::{column.StoreType}"));
            string where = lookup.Type == typeof(EventType) ? "source." + predicate : "true";
            return $"""
                WHEN {lookup.Selector} THEN
                    RETURN QUERY SELECT {columns} FROM {names.Table(lookup.Type)} AS source WHERE {where};
                """;
        }));
        string body = $"""
            BEGIN
                {guard}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.RuntimeLogin)}
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
                THEN RAISE EXCEPTION 'global_lookup_caller_mismatch' USING ERRCODE = '42501'; END IF;
                CASE p_type
                    {branches}
                    ELSE RAISE EXCEPTION 'global_lookup_type_not_declared' USING ERRCODE = '22023';
                END CASE;
                RETURN;
            END;
            """;
        return new(names, privileges, predicate, guard, body);
    }

    /// <summary>Migration-only installation; an existing inconsistent installation is never repaired.</summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var definition = Define(context, enrollment);
        var names = definition.Names;
        string roles = string.Join(",", Roles.Select(Text));
        string table = names.Table(typeof(EventType));
        string memberships = string.Join("\n", definition.Privileges.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {Boolean(edge.Inherit)}, SET {Boolean(edge.Set)}, ADMIN FALSE',
                {Text(edge.Parent)},{Text(edge.Member)});
            """));
        string policies = string.Join("\n", new[] { false, true }.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                CREATE POLICY {name} ON {table} AS {(permissive ? "PERMISSIVE" : "RESTRICTIVE")}
                    FOR SELECT TO {OwnerRole} USING ({definition.Predicate});
                EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',p.polname,
                    {Text(table)},{Text(Stamp + Hash(definition.Predicate) + ":")} || pg_catalog.md5(p.polqual::text))
                    FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(table)}::regclass AND p.polname = '{name}');
                """;
        }));
        string grants = string.Join("\n", Lookups.Select(lookup =>
            $"GRANT SELECT ({string.Join(",", lookup.Properties.Select(property => names.Column(lookup.Type, property)))}) ON {names.Table(lookup.Type)} TO {OwnerRole};"));
        string result = string.Join(",", ResultColumns.Select(column => names.Quote(column.Name) + " " + column.StoreType));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'global_lookup_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, names, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(names.Schema)} AND p.proname = '{Routine}')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(table)}::regclass
                            AND p.polname IN ('{Policy}','{Policy}_allow'))
                    THEN RAISE EXCEPTION 'global_lookup_partial_installation' USING ERRCODE = '55000'; END IF;
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
                    IF existing <> 3 THEN RAISE EXCEPTION 'global_lookup_partial_roles' USING ERRCODE = '55000'; END IF;
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
                    CREATE OR REPLACE FUNCTION {FunctionSql(context)}(p_type integer)
                        RETURNS TABLE ({result}) LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                        AS $body${definition.Body}$body$;
                    REVOKE ALL ON FUNCTION {FunctionSql(context)}(integer) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {FunctionSql(context)}(integer) TO {RuntimeRole};
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {OwnerRole};
            DO $installed$ BEGIN {definition.Guard} {ExactBody(context, definition.Body)} END $installed$;
            """;
    }
}
