using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresResolverConfigurationContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }
            .Any(login => Roles.Contains(login, StringComparer.Ordinal)))
            throw new InvalidOperationException("resolver_configuration_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Operation,
                routine.Signature, OwnerRole, routine.Result == "trigger" ? [OwnerRole] : [GlobalRole])).ToArray(),
            [
                new(context.Model.FindEntityType(typeof(SystemSetting))!.GetTableName()!, OwnerRole, [],
                    Columns.Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(typeof(SystemSetting), property),
                        ["SELECT", "INSERT", .. Updates.Contains(property, StringComparer.Ordinal) ? new[] { "UPDATE" } : []])).ToArray()),
                new(Sentinel, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])
            ],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var entity = context.Model.FindEntityType(typeof(SystemSetting))!;
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(Columns.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("resolver_configuration_mapping_changed");
        string table = sql.Table(typeof(SystemSetting));
        string columns = string.Join(" OR ", Columns.Select(property =>
        {
            var mapped = entity.FindProperty(property)!;
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = pg_catalog.to_regclass({Text(table)})
                        AND a.attname = {Text(sql.ColumnName(typeof(SystemSetting), property))}
                        AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(mapped.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(mapped.IsNullable ? "false" : "true")}
                        AND a.attgenerated = '' AND a.attidentity = ''
                        AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                """;
        }));
        string keys = string.Join("\n", entity.GetKeys().Select(key => key.Properties)
            .Concat(entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null)
                .Select(index => index.Properties)).Select(properties => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i
                    WHERE i.indrelid = pg_catalog.to_regclass({Text(table)})
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indpred IS NULL AND i.indexprs IS NULL
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id
                            WHERE k.ord <= i.indnkeyatts ORDER BY k.ord)
                            = ARRAY[{string.Join(",", properties.Select(property => Text(sql.ColumnName(typeof(SystemSetting), property.Name))))}]::name[])
                THEN RAISE EXCEPTION 'resolver_configuration_source_key_drift' USING ERRCODE = '55000'; END IF;
                """));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class r WHERE r.oid = pg_catalog.to_regclass({Text(table)})
                    AND r.relkind = 'r' AND r.relpersistence = 'p'
                    AND r.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND NOT r.relrowsecurity AND NOT r.relforcerowsecurity)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite w WHERE w.ev_class = pg_catalog.to_regclass({Text(table)}))
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(table)})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> 13
                OR {columns}
            THEN RAISE EXCEPTION 'resolver_configuration_source_drift' USING ERRCODE = '55000'; END IF;
            {keys}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string table = Text(sql.Table(typeof(SystemSetting)));
        string anchor = Text(sql.Name(Sentinel));
        string ids = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Operation) + "(" + routine.Signature + ")")})"));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Operation) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'resolver_configuration_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string triggers = string.Join("\n", new[] { ("changed", 29), ("truncate", 34) }.Select(trigger => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid = pg_catalog.to_regclass({table}) AND t.tgname = {Text(Prefix + trigger.Item1)}
                    AND NOT t.tgisinternal AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                    AND t.tgtype = {trigger.Item2} AND t.tgenabled = 'A'
                    AND t.tgqual IS NULL AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector
                    AND t.tgconstraint = 0 AND NOT t.tgdeferrable AND NOT t.tginitdeferred
                    AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION 'resolver_configuration_trigger_drift' USING ERRCODE = '55000'; END IF;
            """));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {routines}
            {triggers}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({anchor})
                AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attname = 'id' AND a.attnum > 0 AND NOT a.attisdropped AND a.attnotnull
                    AND a.atttypid = 'pg_catalog.int2'::regtype AND a.atttypmod = -1 AND a.attgenerated = ''
                    AND a.attidentity = '' AND NOT a.atthasdef AND a.attcollation = 0)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attname = 'revision' AND a.attnum > 0 AND NOT a.attisdropped AND a.attnotnull
                    AND a.atttypid = 'pg_catalog.bool'::regtype AND a.atttypmod = -1 AND a.attgenerated = ''
                    AND a.attidentity = '' AND NOT a.atthasdef AND a.attcollation = 0)
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype <> 'n') <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable AND NOT c.condeferred
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = c.conindid
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate)
                    AND ARRAY(SELECT a.attname FROM unnest(c.conkey) k JOIN pg_catalog.pg_attribute a
                        ON a.attrelid = c.conrelid AND a.attnum = k) = ARRAY['id']::name[])
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit
                    AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({anchor}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite w WHERE w.ev_class = pg_catalog.to_regclass({anchor}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid IN (pg_catalog.to_regclass({anchor}),pg_catalog.to_regclass({table}))
                        OR i.inhparent IN (pg_catalog.to_regclass({anchor}),pg_catalog.to_regclass({table})))
            THEN RAISE EXCEPTION 'resolver_configuration_anchor_drift' USING ERRCODE = '55000'; END IF;
            IF pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Schema)},'CREATE')
                OR pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},pg_catalog.to_regclass({table}),
                    'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},pg_catalog.to_regclass({table}),'INSERT,UPDATE,REFERENCES')
                OR pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},pg_catalog.to_regclass({anchor}),
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},pg_catalog.to_regclass({anchor}),'SELECT,INSERT,UPDATE,REFERENCES')
            THEN RAISE EXCEPTION 'resolver_configuration_ambient_authority' USING ERRCODE = '55000'; END IF;
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
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
                        OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                        AND (t.tgrelid <> pg_catalog.to_regclass({table})
                            OR t.tgname NOT IN ('{Prefix}changed','{Prefix}truncate')))
            THEN RAISE EXCEPTION 'resolver_configuration_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Operation) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(function.Body))})
            THEN RAISE EXCEPTION 'resolver_configuration_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
