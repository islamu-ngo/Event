using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresPolicySourceSetContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        return new(Schema(context), Roles,
            [
                new(Acquire, "integer,uuid", OwnerRole, [RuntimeRole, GlobalRole]),
                new(Write, "", OwnerRole, [OwnerRole]),
                new(SettingWrite, "", OwnerRole, [OwnerRole])
            ],
            [new(Sentinel, OwnerRole, [],
                [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    private static (Type Type, string Name, string Function, int Flags, string Event)[] Triggers =>
    [
        (typeof(RolePermission), "policy_source_set_write", Write, 62, "INSERT OR UPDATE OR DELETE OR TRUNCATE"),
        (typeof(SystemSetting), "policy_source_set_write", SettingWrite, 29, "INSERT OR UPDATE OR DELETE"),
        (typeof(TenantSetting), "policy_source_set_write", SettingWrite, 29, "INSERT OR UPDATE OR DELETE"),
        (typeof(SystemSetting), "policy_source_set_truncate", Write, 34, "TRUNCATE"),
        (typeof(TenantSetting), "policy_source_set_truncate", Write, 34, "TRUNCATE")
    ];

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string sources = string.Join(" OR ", Sources.Select(type => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                WHERE t.oid = pg_catalog.to_regclass({Text(Table(context, type))}) AND t.relkind = 'r'
                    AND t.relpersistence = 'p'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            """));
        string settingKeys = string.Join(" OR ", new[] { typeof(SystemSetting), typeof(TenantSetting) }.Select(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            var property = entity.FindProperty(nameof(SystemSetting.SettingKey))!;
            string name = property.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = pg_catalog.to_regclass({Text(Table(context, type))})
                        AND a.attname = {Text(name)} AND a.attnum > 0 AND NOT a.attisdropped
                        AND a.attnotnull AND a.attgenerated = '' AND a.attidentity = ''
                        AND a.atttypid = pg_catalog.to_regtype({Text(property.GetRelationalTypeMapping().StoreType)})
                        AND a.attcollation = 'pg_catalog."default"'::regcollation)
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n
                WHERE n.nspname = {Text(Schema(context))}
                    AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR {sources} OR {settingKeys}
            THEN RAISE EXCEPTION 'policy_source_set_source_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string anchor = Text(Name(context, Sentinel));
        string triggerGuard = string.Join("\n", Triggers.Select(trigger => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid = pg_catalog.to_regclass({Text(Table(context, trigger.Type))})
                    AND t.tgname = {Text(trigger.Name)} AND NOT t.tgisinternal
                    AND t.tgfoid = pg_catalog.to_regprocedure({Text(Name(context, trigger.Function) + "()")})
                    AND t.tgtype = {trigger.Flags} AND t.tgenabled = 'A'
                    AND t.tgqual IS NULL AND t.tgnargs = 0 AND t.tgargs = ''::bytea
                    AND t.tgattr = ''::int2vector AND t.tgconstraint = 0
                    AND NOT t.tgdeferrable AND NOT t.tginitdeferred
                    AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION 'policy_source_set_trigger_drift' USING ERRCODE = '55000'; END IF;
            """));
        string routines = string.Join("\n", new[]
        {
            (Acquire, "integer,uuid", "void"), (Write, "", "trigger"), (SettingWrite, "", "trigger")
        }.Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(Name(context, function.Item1) + "(" + function.Item2 + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.{function.Item3}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql'))
            THEN RAISE EXCEPTION 'policy_source_set_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                    AND pg_catalog.shobj_description(r.oid, 'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
            THEN RAISE EXCEPTION 'policy_source_set_role_drift' USING ERRCODE = '55000'; END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                WHERE t.oid = pg_catalog.to_regclass({anchor}) AND t.relkind = 'r'
                    AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = pg_catalog.to_regclass({anchor}) AND a.attnum > 0 AND NOT a.attisdropped) <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = pg_catalog.to_regclass({anchor}) AND a.attname = 'id'
                        AND a.attnum > 0 AND NOT a.attisdropped AND a.attnotnull AND a.atttypid = 'pg_catalog.int2'::regtype)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = pg_catalog.to_regclass({anchor}) AND a.attname = 'revision'
                        AND a.attnum > 0 AND NOT a.attisdropped AND a.attnotnull AND a.atttypid = 'pg_catalog.bool'::regtype)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
                    WHERE c.conrelid = pg_catalog.to_regclass({anchor}) AND c.contype = 'p'
                        AND c.convalidated AND NOT c.condeferrable
                        AND ARRAY(SELECT a.attname FROM unnest(c.conkey) k
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k) = ARRAY['id']::name[])
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
                    WHERE c.conrelid = pg_catalog.to_regclass({anchor}) AND c.contype = 'c'
                        AND c.convalidated AND pg_catalog.pg_get_expr(c.conbin, c.conrelid) = '(id = 1)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({anchor}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r
                    WHERE r.ev_class = pg_catalog.to_regclass({anchor}))
            THEN RAISE EXCEPTION 'policy_source_set_anchor_drift' USING ERRCODE = '55000'; END IF;
            {routines}
            {triggerGuard}
            """;
    }

    /// <summary>
    /// Migration-owned installation, before seeding and before accepting traffic. Existing
    /// ACL/role/body/trigger drift is rejected, never repaired. No old admin role is touched.
    /// The singleton is native infrastructure, not an EF entity or a hand-edited migration.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string schema = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE', {Text(edge.Parent)}, {Text(edge.Member)});
            """));
        string definitions = string.Join("\n", Functions(context, enrollment).Select(function => $"""
            CREATE OR REPLACE FUNCTION {Name(context, function.Name)}({function.Arguments}) RETURNS {function.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {Name(context, function.Name)}({function.Signature}) FROM PUBLIC;
            {(function.Name == Acquire ? $"GRANT EXECUTE ON FUNCTION {Name(context, Acquire)}(integer,uuid) TO {RuntimeRole}, {GlobalRole};" : "")}
            {(function.Name != Acquire ? $"GRANT EXECUTE ON FUNCTION {Name(context, function.Name)}() TO {context.GetService<ISqlGenerationHelper>().DelimitIdentifier(enrollment.MigratorLogin)};" : "")}
            COMMENT ON FUNCTION {Name(context, function.Name)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        string triggers = string.Join("\n", Triggers.Select(trigger => $"""
            DROP TRIGGER IF EXISTS {trigger.Name} ON {Table(context, trigger.Type)};
            CREATE TRIGGER {trigger.Name} {(trigger.Flags == 29 ? "AFTER" : "BEFORE")} {trigger.Event} ON {Table(context, trigger.Type)}
                FOR EACH {(trigger.Flags == 29 ? "ROW" : "STATEMENT")}
                EXECUTE FUNCTION {Name(context, trigger.Function)}();
            ALTER TABLE {Table(context, trigger.Type)} ENABLE ALWAYS TRIGGER {trigger.Name};
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; role_count integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text)
                THEN RAISE EXCEPTION 'policy_source_set_migrator_mismatch' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO role_count FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF role_count = 0 THEN
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
                ELSIF role_count <> {Roles.Length}
                    OR ({PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}) IS NOT TRUE THEN
                    RAISE EXCEPTION 'policy_source_set_role_drift' USING ERRCODE = '55000';
                END IF;
                IF pg_catalog.to_regclass({Text(Name(context, Sentinel))}) IS NOT NULL THEN
                    {guard}
                    {exact}
                    IF NOT EXISTS (SELECT 1 FROM {Name(context, Sentinel)} WHERE id = 1) THEN
                        RAISE EXCEPTION 'policy_source_set_anchor_missing' USING ERRCODE = '55000';
                    END IF;
                ELSIF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
                    JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = {Text(Schema(context))} AND p.proname LIKE 'policy_source_set_%')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                        WHERE t.tgrelid IN ({string.Join(",", Sources.Select(type => Text(Table(context, type)) + "::regclass"))})
                            AND t.tgname IN ('policy_source_set_write','policy_source_set_truncate'))
                THEN RAISE EXCEPTION 'policy_source_set_partial_installation' USING ERRCODE = '55000'; END IF;
            END $bootstrap$;
            CREATE TABLE IF NOT EXISTS {Name(context, Sentinel)}
                (id smallint PRIMARY KEY CHECK (id = 1), revision boolean NOT NULL);
            INSERT INTO {Name(context, Sentinel)} (id, revision) VALUES (1, false) ON CONFLICT (id) DO NOTHING;
            GRANT USAGE ON SCHEMA {schema} TO {string.Join(",", Roles)};
            GRANT SELECT (id, revision), UPDATE (revision) ON {Name(context, Sentinel)} TO {OwnerRole};
            GRANT CREATE ON SCHEMA {schema} TO {OwnerRole};
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role', previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            {triggers}
            DO $close_trigger_install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
                REVOKE EXECUTE ON FUNCTION {Name(context, Write)}(), {Name(context, SettingWrite)}()
                    FROM {context.GetService<ISqlGenerationHelper>().DelimitIdentifier(enrollment.MigratorLogin)};
                PERFORM pg_catalog.set_config('role', previous_role, true);
            END $close_trigger_install$;
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
