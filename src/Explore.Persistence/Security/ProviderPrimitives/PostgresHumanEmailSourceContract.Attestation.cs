using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresHumanEmailSourceContract
{
    private static (Type Type, string[] Columns, string[] Locks)[] Sources() =>
        HumanPrincipalSourceSql.Sources.Concat(HumanPrincipalSourceSql.LocalSources)
            .Concat(HumanSupportAccessSourceSql.Sources)
            .Select(source => (source.Type, source.Columns, Locks: new[] { source.Columns[0] }))
            .Concat(HumanAuthorizationRouteSql.Sources.Select(source =>
                (source.Type, source.Columns, Locks: Array.Empty<string>())))
            .GroupBy(source => source.Type)
            .Select(group => (group.Key, group.SelectMany(source => source.Columns).Distinct(StringComparer.Ordinal).ToArray(),
                group.SelectMany(source => source.Locks).Distinct(StringComparer.Ordinal).ToArray())).ToArray();

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            [new(Observe, Signature, OwnerRole, [InstanceRole, TenantRole]),
                new(Acquire, Signature, OwnerRole, [InstanceRole, TenantRole])],
            [.. Sources().Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, property),
                    source.Locks.Contains(property, StringComparer.Ordinal) ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())),
                .. Anchors.Select(anchor => new PostgresPrivilegeGuard.Relation(anchor, OwnerRole, [],
                    [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])]))],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(InstanceRole, enrollment.EmailInstanceMutationLogin, true, false, false),
                new(TenantRole, enrollment.EmailTenantMutationLogin, true, false, false)]);
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string routineIds = string.Join(",", new[] { Observe, Acquire }.Select(name =>
            $"pg_catalog.to_regprocedure({Text(sql.Name(name) + "(" + Signature + ")")})"));
        string routines = string.Join("\n", new[] { Observe, Acquire }.Select(name => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(name) + "(" + Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.jsonb'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'human_email_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Sources().Select(source =>
        {
            string table = sql.Table(source.Type);
            bool tenant = tenantTables.Any(candidate =>
                candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string locks = source.Locks.Length == 0 ? "" : $"""
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{LockPolicy}'
                        AND p.polpermissive AND p.polcmd = 'w' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                        AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                """;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{ReadPolicy}'
                        AND p.polpermissive AND p.polcmd = 'r' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    {locks}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                            AND p.polname NOT IN ('{ReadPolicy}'{(source.Locks.Length == 0 ? "" : $",'{LockPolicy}'")}
                                {(tenant ? $",'{PostgresTenantRowLevelSecurityModel.PolicyName}'" : "")}))
                THEN RAISE EXCEPTION 'human_email_source_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            {OriginalSources(context, enrollment)}
            {PostgresManagedProviderProvisioningContract.SharedWriterAssertionSql(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {routines}
            {policies}
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
                        OR d.objid NOT IN ({routineIds})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIds})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'human_email_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }
}
