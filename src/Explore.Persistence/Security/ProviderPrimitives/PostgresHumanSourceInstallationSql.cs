using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>
/// Catalog and installation mechanics for fixed Human original-source contracts.
/// Callers retain their source inventories, role authority, policies and routine bodies.
/// </summary>
internal static class PostgresHumanSourceInstallationSql
{
    internal static string SourceGuard(ExploreDbContext context, string migratorLogin,
        (Type Type, string[] Columns)[] sources, string errorPrefix)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string checks = string.Join("\n", sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string table = Text(sql.Table(source.Type));
            string columns = string.Join(" OR ", source.Columns.Select(column =>
            {
                var property = entity.FindProperty(column)!;
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = pg_catalog.to_regclass({table}) AND a.attname = {Text(sql.ColumnName(source.Type, column))}
                            AND a.attnum > 0 AND NOT a.attisdropped
                            AND a.atttypid = pg_catalog.to_regtype({Text(property.GetRelationalTypeMapping().StoreType)})
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                            AND a.attnotnull = {(property.IsNullable ? "false" : "true")}
                            AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                    """;
            }));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relowner = pg_catalog.to_regrole({Text(migratorLogin)})::oid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                        WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR {columns}
                THEN RAISE EXCEPTION '{errorPrefix}_source_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(migratorLogin)})::oid)
            THEN RAISE EXCEPTION '{errorPrefix}_schema_drift' USING ERRCODE = '55000'; END IF;
            {checks}
            """;
    }

    internal static string RoutineGuard(HumanPrincipalSourceSql sql,
        (string Name, string Signature, string Result)[] routines, string stamp, string error) =>
        string.Join("\n", routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION '{error}' USING ERRCODE = '55000'; END IF;
            """));

    internal static string AnchorDriftPredicate(HumanPrincipalSourceSql sql, string anchorName, string migratorLogin)
    {
        string anchor = Text(sql.Name(anchorName));
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({anchor})
                AND t.relkind = 'r' AND t.relowner = pg_catalog.to_regrole({Text(migratorLogin)})::oid
                AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attname = 'id' AND a.atttypid = 'smallint'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({anchor})
                    AND a.attname = 'revision' AND a.atttypid = 'boolean'::regtype AND a.attnotnull)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable
                    AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'c' AND c.convalidated AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({anchor}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({anchor}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({anchor}) OR i.inhparent = pg_catalog.to_regclass({anchor}))
            """;
    }

    internal static string WriterTriggers(HumanPrincipalSourceSql sql,
        (Type Type, string[] Columns)[] sources, string writerTrigger, string write) =>
        string.Join("\n", sources.Select(source => $"""
            DROP TRIGGER IF EXISTS {writerTrigger} ON {sql.Table(source.Type)};
            CREATE TRIGGER {writerTrigger} BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {sql.Table(source.Type)}
                FOR EACH STATEMENT EXECUTE FUNCTION {sql.Name(write)}();
            ALTER TABLE {sql.Table(source.Type)} ENABLE ALWAYS TRIGGER {writerTrigger};
            """));

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
