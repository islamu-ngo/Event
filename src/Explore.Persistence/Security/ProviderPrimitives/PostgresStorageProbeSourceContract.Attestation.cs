using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresStorageProbeSourceContract
{
    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }
            .Any(login => Roles.Contains(login, StringComparer.Ordinal)))
            throw new InvalidOperationException("storage_probe_source_enrollment_collision");
        return new(new HumanPrincipalSourceSql(context).Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Operation,
                routine.Signature, OwnerRole, routine.Result == "trigger" ? [OwnerRole] : [GlobalRole, RuntimeRole])).ToArray(),
            [new(SourceTable, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string sources = string.Join("\n", Sources.Select(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            string table = Text(sql.Table(type));
            var properties = entity.GetProperties().ToArray();
            string columns = string.Join(" OR ", properties.Select(property =>
            {
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                    ? "C" : "default";
                return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attname = {Text(sql.ColumnName(type, property.Name))} AND a.attnum > 0 AND NOT a.attisdropped
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                    AND a.attnotnull = {(property.IsNullable ? "false" : "true")} AND a.attgenerated = '' AND a.attidentity = ''
                    AND (a.attcollation = 0 OR a.attcollation =
                        pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))})))
                """;
            }));
            string Names(IReadOnlyList<IProperty> fields) =>
                $"ARRAY[{string.Join(",", fields.Select(property => Text(sql.ColumnName(type, property.Name))))}]::name[]";
            var indexes = entity.GetKeys().Select(key => (key.Properties, Filter: (string?)null))
                .Concat(entity.GetIndexes().Where(index => index.IsUnique).Select(index => (index.Properties, Filter: index.GetFilter())));
            string uniqueness = string.Join("\n", indexes.Select(index => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                    AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate AND i.indexprs IS NULL
                    AND {(index.Filter is null ? "i.indpred IS NULL" : $"pg_catalog.pg_get_expr(i.indpred,i.indrelid) = {Text("(" + index.Filter + ")")}")}
                    AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id
                        WHERE k.ord <= i.indnkeyatts ORDER BY k.ord) = {Names(index.Properties)})
                THEN RAISE EXCEPTION 'storage_probe_source_key_drift' USING ERRCODE = '55000'; END IF;
                """));
            var tenant = tenantTables.SingleOrDefault(candidate => candidate.TableName == entity.GetTableName());
            string rls = tenant is null ? $"""
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND (t.relrowsecurity OR t.relforcerowsecurity))
                THEN RAISE EXCEPTION 'storage_probe_source_rls_drift' USING ERRCODE = '55000'; END IF;
                """ : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relrowsecurity AND t.relforcerowsecurity)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table})
                        AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}'
                        AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = pg_catalog.to_regclass({table})
                        AND p.polname <> '{PostgresTenantRowLevelSecurityModel.PolicyName}'
                        AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},role_id,'USAGE')
                                OR pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)},role_id,'USAGE'))))
                THEN RAISE EXCEPTION 'storage_probe_source_rls_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                        WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {properties.Length}
                    OR {columns}
                THEN RAISE EXCEPTION 'storage_probe_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {uniqueness}
                {rls}
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'storage_probe_source_schema_drift' USING ERRCODE = '55000'; END IF;
            {sources}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string anchor = Text(sql.Name(SourceTable));
        string sources = string.Join(",", Sources.Select(type => $"pg_catalog.to_regclass({Text(sql.Table(type))})"));
        string ids = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Operation) + "(" + routine.Signature + ")")})"));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Operation) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'storage_probe_source_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string triggers = string.Join("\n", Sources.Select(type => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid = pg_catalog.to_regclass({Text(sql.Table(type))}) AND t.tgname = '{Prefix}changed'
                    AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                    AND t.tgtype = 62 AND t.tgenabled = 'A' AND NOT t.tgisinternal
                    AND t.tgqual IS NULL AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector
                    AND t.tgconstraint = 0 AND NOT t.tgdeferrable AND NOT t.tginitdeferred
                    AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION 'storage_probe_source_exclusion_drift' USING ERRCODE = '55000'; END IF;
            """));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresManagedProviderProvisioningContract.SharedWriterAssertionSql(context, enrollment)}
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
                    AND a.atttypid = 'pg_catalog.int8'::regtype AND a.atttypmod = -1 AND a.attgenerated = ''
                    AND a.attidentity = '' AND NOT a.atthasdef AND a.attcollation = 0)
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype <> 'n') <> 3
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'p' AND c.convalidated AND NOT c.condeferrable AND NOT c.condeferred
                    AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)'
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = c.conindid
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit
                    AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({anchor})
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit
                    AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(revision >= 0)')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({anchor}) AND NOT t.tgisinternal)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({anchor}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({anchor}) OR i.inhparent = pg_catalog.to_regclass({anchor}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                        AND (t.tgrelid NOT IN ({sources}) OR t.tgname <> '{Prefix}changed'))
            THEN RAISE EXCEPTION 'storage_probe_source_anchor_drift' USING ERRCODE = '55000'; END IF;
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
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({Text(enrollment.RuntimeLogin)},{Text(enrollment.GlobalLogin)})
                    AND (pg_catalog.has_table_privilege(r.oid,pg_catalog.to_regclass({anchor}),'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege(r.oid,pg_catalog.to_regclass({anchor}),'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND d.deptype = 'o' AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) OR d.objid NOT IN ({ids})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({ids})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'storage_probe_source_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }
}
