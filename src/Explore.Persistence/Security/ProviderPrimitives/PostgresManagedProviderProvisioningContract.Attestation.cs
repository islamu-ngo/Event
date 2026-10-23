using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

public static partial class PostgresManagedProviderProvisioningContract
{
    private static string OriginalSources(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresPolicySourceSetContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanPrincipalSourceContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanAuthorizationRouteContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanSupportAccessSourceContract.OriginalSourceAssertionSql(context, enrollment);

    private static string SourceCatalog(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        var writers = ProvisioningSharedWriterAssertions.Inventory(context, enrollment);
        string assertions = string.Join("\n", writers.Select(writer => writer.PrecedesProvisioning
            ? writer.AssertInstalled(context) : writer.AssertInstalledOrMigrationAbsent(context, enrollment)));
        return assertions + "\n" + string.Join("\n", Sources.Select(relation =>
        {
            var entity = context.Model.FindEntityType(relation.Type)!;
            string table = Text(sql.Table(relation.Type));
            bool tenant = tenantTables.Any(item => item.TableName == entity.GetTableName());
            string columns = string.Join("\n", relation.Columns.Select(name =>
            {
                var property = entity.FindProperty(name)!;
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
                return $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = pg_catalog.to_regclass({table}) AND a.attname = {Text(sql.ColumnName(relation.Type, name))}
                            AND a.attnum > 0 AND NOT a.attisdropped AND a.attgenerated = '' AND a.attidentity = ''
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                            AND a.attnotnull = {(!property.IsNullable).ToString().ToLowerInvariant()}
                            AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))})))
                    THEN RAISE EXCEPTION 'human_provisioning_column_drift' USING ERRCODE = '55000'; END IF;
                    """;
            }));
            string KeyColumns(Type type, IEnumerable<IProperty> properties) => "ARRAY[" +
                string.Join(",", properties.Select(property => Text(sql.ColumnName(type, property.Name)))) + "]::name[]";
            string keys = string.Join("\n", entity.GetKeys().Select(key =>
                    (key.Properties, Filter: (string?)null, Descending: (IReadOnlyList<bool>?)null))
                .Concat(context.GetService<IDesignTimeModel>().Model.FindEntityType(relation.Type)!
                    .GetIndexes().Where(index => index.IsUnique).Select(index =>
                    (index.Properties, Filter: index.GetFilter(), Descending: index.IsDescending)))
                .Select(key => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i
                        WHERE i.indrelid = pg_catalog.to_regclass({table}) AND i.indisunique AND i.indisvalid
                            AND i.indisready AND i.indimmediate AND i.indexprs IS NULL
                            AND {(key.Filter is null ? "i.indpred IS NULL" : "pg_catalog.pg_get_expr(i.indpred,i.indrelid) = " + Text("(" + key.Filter.Replace("\"", "", StringComparison.Ordinal) + ")"))}
                            AND i.indnkeyatts = {key.Properties.Count} AND i.indnatts = {key.Properties.Count}
                            AND ARRAY(SELECT option.flag FROM unnest(i.indoption::smallint[])
                                WITH ORDINALITY option(flag,ord) ORDER BY option.ord)
                                = ARRAY[{string.Join(",", key.Properties.Select((_, position) =>
                                    key.Descending is { Count: 0 } || key.Descending?[position] == true ? "3" : "0"))}]::smallint[]
                            AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                                JOIN pg_catalog.pg_opclass o ON o.oid = operator_id JOIN pg_catalog.pg_am a ON a.oid = o.opcmethod
                                WHERE NOT o.opcdefault OR a.amname <> 'btree')
                            AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                                JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord) ON k.ord = col.ord
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id WHERE col.value <> a.attcollation)
                            AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {KeyColumns(relation.Type, key.Properties)})
                    THEN RAISE EXCEPTION 'human_provisioning_key_drift' USING ERRCODE = '55000',
                        HINT = {Text(relation.Type.Name + ":" + string.Join(",", key.Properties.Select(property => property.Name)) + ":" + key.Filter)};
                    END IF;
                    """));
            string foreignKeys = string.Join("\n", entity.GetForeignKeys().Select(key => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint f
                    WHERE f.conrelid = pg_catalog.to_regclass({table}) AND f.contype = 'f'
                        AND f.confrelid = pg_catalog.to_regclass({Text(sql.Name(key.PrincipalEntityType.GetTableName()!))})
                        AND f.convalidated AND NOT f.condeferrable AND NOT f.condeferred
                        AND f.confmatchtype = 's' AND f.confupdtype = 'a'
                        AND f.confdeltype = '{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c"
                            : key.DeleteBehavior == DeleteBehavior.SetNull ? "n" : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                        AND ARRAY(SELECT a.attname FROM unnest(f.conkey) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = f.conrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {KeyColumns(relation.Type, key.Properties)}
                        AND ARRAY(SELECT a.attname FROM unnest(f.confkey) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = f.confrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {KeyColumns(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)}
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint = f.oid AND t.tgenabled NOT IN ('O','A')))
                THEN RAISE EXCEPTION 'human_provisioning_foreign_key_drift' USING ERRCODE = '55000'; END IF;
                """));
            // A writer is admitted only on the exact source-defined intersection,
            // after its complete trigger/body/privilege assertion above.
            string epochs = string.Join(" OR ", writers.Where(writer => writer.Targets.Any(target => target.Type == relation.Type))
                .Select(writer => $"(t.tgname = {Text(writer.Name)} AND t.tgfoid = pg_catalog.to_regprocedure({Text(sql.Name(writer.Function) + "()")}))"));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    WHERE t.oid = pg_catalog.to_regclass({table}) AND t.relkind = 'r' AND t.relpersistence = 'p'
                        AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                        AND t.relrowsecurity = {tenant.ToString().ToLowerInvariant()} AND t.relforcerowsecurity = {tenant.ToString().ToLowerInvariant()})
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = pg_catalog.to_regclass({table})
                        OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {entity.GetProperties().Count(property => sql.ColumnName(relation.Type, property.Name) != "xmin")}
                    OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table}) AND i.indisunique)
                        <> {entity.GetKeys().Count() + entity.GetIndexes().Count(index => index.IsUnique)}
                    OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table}) AND c.contype = 'f')
                        <> {entity.GetForeignKeys().Count()}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({table})
                        AND NOT t.tgisinternal AND ({epochs}) IS NOT TRUE)
                THEN RAISE EXCEPTION 'human_provisioning_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {columns}
                {keys}
                {foreignKeys}
                """;
        }));
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        bool creatorIntersection = Sources.Any(relation =>
            PostgresTenantCreationPermissions.TryGetGlobalColumns(context, relation.Type, out _, out _));
        string creatorProof = creatorIntersection
            ? PostgresTenantCreationPermissions.OptionalGlobalCataloguePredicate(context, enrollment) : "true";
        string policies = string.Join("\n", Sources.Select(relation =>
        {
            var table = tenantTables.SingleOrDefault(item => item.TableName == context.Model.FindEntityType(relation.Type)!.GetTableName());
            string name = Text(sql.Table(relation.Type));
            bool creatorInserts = PostgresTenantCreationPermissions.TryGetGlobalColumns(context, relation.Type, out _, out var insert)
                && insert.Count != 0;
            string columnWrites = creatorInserts ? $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN (VALUES ('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                    WHERE a.attrelid={name}::regclass AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege({Text(enrollment.GlobalLogin)},a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                            OR (pg_catalog.has_column_privilege({Text(enrollment.GlobalLogin)},a.attrelid,a.attnum,permission.privilege)
                                AND NOT (a.attnum>0 AND permission.privilege='INSERT'
                                    AND a.attname=ANY(ARRAY[{string.Join(',', insert.Select(Text))}]::text[])))))
                """ : $"pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{name}::regclass,'INSERT,UPDATE,REFERENCES')";
            if (PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, relation.Type, out _, out var workerInsert, out var workerUpdate)
                && (workerInsert.Count != 0 || workerUpdate.Count != 0))
                columnWrites = $"({columnWrites}) AND ({PostgresManagedTenantWorkerCreationPermissions.CompleteGlobalCataloguePredicate(context, enrollment)}) IS NOT TRUE";
            return (table is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {name}::regclass
                    AND p.polname = '{Policy}' AND p.polcmd = '*' AND p.polpermissive
                    AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                    AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                    AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {name}::regclass
                        AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}' AND p.polpermissive
                        AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(table, sql.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'human_provisioning_rls_drift' USING ERRCODE = '55000'; END IF;
                """) + $"""
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {name}::regclass
                    {(table is null ? "" : $"AND p.polname NOT IN ('{Policy}','{PostgresTenantRowLevelSecurityModel.PolicyName}')")}
                    AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) r
                        WHERE pg_catalog.pg_has_role('{OwnerRole}',r,'USAGE'))))
                THEN RAISE EXCEPTION 'human_provisioning_rls_widened' USING ERRCODE = '55000'; END IF;
                IF {(relation.Type == typeof(EventDiscoveryRevision) ? $"NOT ({PostgresDiscoveryMetadataPermissions.RevisionAuthorityPredicate(context, enrollment.GlobalLogin)})" : $"""
                    pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},{name}::regclass,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR {columnWrites}
                    """)}
                THEN RAISE EXCEPTION 'human_provisioning_ambient_write_authority' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'human_provisioning_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string triggers = string.Join("\n", Sources.Select(relation => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid = {Text(sql.Table(relation.Type))}::regclass AND t.tgname = '{Prefix}changed'
                    AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                    AND t.tgtype = 62 AND t.tgenabled = 'A' AND NOT t.tgisinternal AND t.tgqual IS NULL
                    AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector AND t.tgconstraint = 0)
            THEN RAISE EXCEPTION 'human_provisioning_writer_drift' USING ERRCODE = '55000'; END IF;
            """));
        return $"""
            {SourceCatalog(context, enrollment)}
            {OriginalSources(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            IF NOT ({creatorProof})
            THEN RAISE EXCEPTION 'human_provisioning_ambient_write_authority' USING ERRCODE = '55000'; END IF;
            {routines}
            {policies}
            {triggers}
            {PrivateCatalog(context, enrollment)}
            {AuthorityCatalog(context, enrollment)}
            {ExactBody(context, "program", "jsonb,jsonb", ProgramBody(context))}
            """;
    }

    private static string AuthorityCatalog(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string routines = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})"));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles}) AND n.nspname <> {Text(sql.Schema)}
                    AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                    AND t.relkind IN ('r','p','v','m','f') AND
                        (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) AND d.deptype = 'o'
                    AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) OR d.objid NOT IN ({routines})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routines}) AND
                    (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a WHERE a.grantee IN
                            (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'human_provisioning_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string PrivateCatalog(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var fields = new[] { (SourceTable, "id", "int2"), (SourceTable, "revision", "int8"),
            (AttemptTable, "attempt", "int8"), (AttemptTable, "backend", "int4"), (AttemptTable, "request", "jsonb"),
            (AttemptTable, "preimage", "jsonb"), (AttemptTable, "plan", "jsonb"), (AttemptTable, "expected", "jsonb"),
            (AttemptTable, "applied", "bool"), (AttemptTable, "completed", "bool") };
        return string.Join("\n", new[] { SourceTable, AttemptTable }.Select(table => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({Text(sql.Name(table))})
                AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(sql.Name(table))})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> {fields.Count(field => field.Item1 == table)}
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({Text(sql.Name(table))}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({Text(sql.Name(table))})
                    AND NOT t.tgisinternal AND t.tgname <> '{Prefix}settlement')
            THEN RAISE EXCEPTION 'human_provisioning_private_catalog_drift' USING ERRCODE = '55000'; END IF;
            """)) + string.Join("\n", fields.Select(field => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {Text(sql.Name(field.Item1))}::regclass
                AND a.attname = {Text(field.Item2)} AND a.atttypid = 'pg_catalog.{field.Item3}'::regtype
                AND a.attnotnull = {(field.Item2 is not ("plan" or "expected")).ToString().ToLowerInvariant()}
                AND a.attgenerated = '' AND a.attidentity = '' AND NOT a.atthasdef)
            THEN RAISE EXCEPTION 'human_provisioning_private_column_drift' USING ERRCODE = '55000'; END IF;
            """)) + $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(AttemptTable))}::regclass
                AND c.contype = 'p' AND NOT c.condeferrable AND c.convalidated
                AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (attempt, backend)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(SourceTable))}::regclass
                    AND c.contype = 'p' AND NOT c.condeferrable AND c.convalidated AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = {Text(sql.Name(AttemptTable))}::regclass
                    AND t.tgname = '{Prefix}settlement' AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "settlement") + "()")})
                    AND t.tgtype = 5 AND t.tgenabled = 'A' AND NOT t.tgisinternal AND t.tgqual IS NULL
                    AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector
                    AND t.tgconstraint <> 0 AND t.tgdeferrable AND t.tginitdeferred)
            THEN RAISE EXCEPTION 'human_provisioning_settlement_seal_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBody(ExploreDbContext context, string name, string signature, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE
            f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, name) + "(" + signature + ")")})
            AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'human_provisioning_exact_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => ExactBody(context, function.Name, function.Signature, function.Body)));
}
