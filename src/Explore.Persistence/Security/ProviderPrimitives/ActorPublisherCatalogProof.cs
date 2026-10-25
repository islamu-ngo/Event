using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// Catalog proof mechanics for actor reads, publisher reads, and conversation owner fences.
/// Each owner supplies only its own source inventory; grants, policies, and function contracts stay local.
/// </summary>
internal static class ActorPublisherCatalogProof
{
    internal static string CallerPrivilegeGuard(ExploreDbContext context, HumanPrincipalSourceSql names, IReadOnlyCollection<Type> sources,
        PostgresNativeQueueEnrollment enrollment, string diagnosticPrefix, bool allowRuntimeCatalogueRoleRead = false)
    {
        if (allowRuntimeCatalogueRoleRead && sources.Contains(typeof(Role))
            || sources.Any(type => PostgresTenantCreationPermissions.TryGetGlobalColumns(context, type, out _, out _)
                || PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, type, out _, out _, out _)
                || PostgresTenantMembershipMutationPermissions.TryGetRuntimeColumns(context, type, out _, out _, out _)
                || PostgresActorOwnershipReadPermissions.TryGetRuntimeReadColumns(context, type, out _)))
            return CatalogueAwareCallerPrivilegeGuard(context, names, sources, enrollment, diagnosticPrefix, allowRuntimeCatalogueRoleRead);

        string relations = string.Join(",", sources.Select(type => Text(names.Table(type)) + "::regclass"));
        return string.Join("\n", new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin }.Select(login => $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role({Text(login)},r.oid,'USAGE') OR pg_catalog.pg_has_role({Text(login)},r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE t.oid IN ({relations})
                        AND (pg_catalog.pg_has_role({Text(login)},r.oid,'USAGE') OR pg_catalog.pg_has_role({Text(login)},r.oid,'SET'))
                        AND (r.oid = t.relowner
                            OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({Text(login)},r.oid,'USAGE') OR pg_catalog.pg_has_role({Text(login)},r.oid,'SET'))
                        AND pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(names.Schema)}),'CREATE'))
            THEN RAISE EXCEPTION '{diagnosticPrefix}_private_source_privilege' USING ERRCODE = '42501'; END IF;
            """));
    }

    private static string CatalogueAwareCallerPrivilegeGuard(ExploreDbContext context, HumanPrincipalSourceSql names,
        IReadOnlyCollection<Type> sources, PostgresNativeQueueEnrollment enrollment, string diagnosticPrefix,
        bool allowRuntimeCatalogueRoleRead)
    {
        string relations = string.Join(",", sources.Select(type => Text(names.Table(type)) + "::regclass"));
        string runtime = Text(enrollment.RuntimeLogin);
        string role = Text(names.Table(typeof(Role))) + "::regclass";
        string columns = "ARRAY[" + string.Join(",", PostgresLookupCatalogueReadPermissions.RuntimeRoleReadProperties
            .Select(property => Text(names.ColumnName(typeof(Role), property)))) + "]::text[]";
        string[] creatorGrants = sources.SelectMany(type =>
        {
            if (!PostgresTenantCreationPermissions.TryGetGlobalColumns(context, type, out var read, out var insert))
                return Array.Empty<string>();
            return read.Select(column => (column, Privilege: "SELECT"))
                .Concat(insert.Select(column => (column, Privilege: "INSERT")))
                .Select(grant => $"(t.oid={Text(names.Table(type))}::regclass AND a.attname={Text(grant.column)} AND permission.privilege={Text(grant.Privilege)})");
        }).ToArray();
        string[] workerGrants = sources.SelectMany(type =>
        {
            if (!PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, type, out var read, out var insert, out var update))
                return Array.Empty<string>();
            return read.Select(column => (column, Privilege: "SELECT"))
                .Concat(insert.Select(column => (column, Privilege: "INSERT")))
                .Concat(update.Select(column => (column, Privilege: "UPDATE")))
                .Select(grant => $"(t.oid={Text(names.Table(type))}::regclass AND a.attname={Text(grant.column)} AND permission.privilege={Text(grant.Privilege)})");
        }).ToArray();
        string[] membershipReads = sources.SelectMany(type =>
        {
            PostgresUserTenantMembershipReadPermissions.TryGetGlobalReadColumns(context, type, out var read);
            return read.Select(column => $"(t.oid={Text(names.Table(type))}::regclass AND a.attname={Text(column)})");
        }).ToArray();
        string[] runtimeMutations = sources.SelectMany(type =>
        {
            PostgresTenantMembershipMutationPermissions.TryGetRuntimeColumns(context, type, out var read, out var update, out _);
            return read.Select(column => (column, Privilege: "SELECT"))
                .Concat(update.Select(column => (column, Privilege: "UPDATE")))
                .Select(grant => $"(t.oid={Text(names.Table(type))}::regclass AND a.attname={Text(grant.column)} AND permission.privilege={Text(grant.Privilege)})");
        }).ToArray();
        string[] globalMutations = sources.SelectMany(type => PostgresTenantMembershipMutationPermissions.GlobalUpdateColumns(context, type)
            .Select(column => $"(t.oid={Text(names.Table(type))}::regclass AND a.attname={Text(column)} AND permission.privilege='UPDATE')")).ToArray();
        return string.Join("\n", new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin }.Select(login =>
        {
            string[] mutationGrants = login == enrollment.RuntimeLogin ? runtimeMutations : globalMutations;
            string mutationGrant = mutationGrants.Length == 0 ? "false" : $"""
                r.rolname={Text(login)} AND a.attnum>0 AND ({string.Join(" OR ", mutationGrants)})
                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                    WHERE acl.grantee=r.oid AND acl.grantor=t.relowner
                        AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                    WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege)
                """;
            string mutationProof = mutationGrants.Length == 0 ? "true"
                : PostgresTenantMembershipMutationPermissions.OptionalCataloguePredicate(context, enrollment);
            string delete = login == enrollment.RuntimeLogin && sources.Contains(typeof(TenantUserProfile))
                ? PostgresTenantMembershipMutationPermissions.RuntimeDeletePredicate(context, enrollment, "r.oid", "t.oid")
                : "false";
            string referenceRead = login == enrollment.RuntimeLogin && allowRuntimeCatalogueRoleRead && sources.Contains(typeof(Role)) ? $"""
                t.oid = {role} AND r.rolname = {runtime} AND a.attnum > 0 AND a.attname = ANY({columns})
                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                    WHERE acl.grantee = pg_catalog.to_regrole({runtime})::oid
                        AND acl.privilege_type = 'SELECT' AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                    WHERE acl.grantee = 0 AND acl.privilege_type = 'SELECT')
                """ : login == enrollment.GlobalLogin && sources.Contains(typeof(Tenant))
                    ? PostgresDiscoveryMetadataPermissions.GlobalTenantIdReadPredicate(names, enrollment.GlobalLogin) : "false";
            if (login == enrollment.RuntimeLogin && sources.Any(type =>
                    PostgresTenantApiKeyStoragePermissions.RuntimeParentReadColumns(context, type).Count != 0))
                referenceRead = $"({referenceRead}) OR ({PostgresTenantApiKeyStoragePermissions.RuntimeParentReadPredicate(context, enrollment, "r.oid", "t.oid", "a.attname", "permission.privilege")})";
            if (login == enrollment.RuntimeLogin)
            {
                string ownershipReads = string.Join(" OR ", sources.Select(type =>
                    PostgresActorOwnershipReadPermissions.RuntimeReadPredicate(context, type, login,
                        "r.oid", "t.oid", "a.attname", "permission.privilege")));
                if (ownershipReads.Length != 0) referenceRead = $"({referenceRead}) OR ({ownershipReads})";
            }
            if (login == enrollment.GlobalLogin && sources.Contains(typeof(User)))
                referenceRead = $"""
                    ({referenceRead}) OR (t.oid={Text(names.Table(typeof(User)))}::regclass
                        AND r.rolname={Text(enrollment.GlobalLogin)} AND a.attnum>0
                        AND ({PostgresUserTenantNavigationReadPermissions.AllowedGlobalReadPredicate(context, enrollment)})
                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                            WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)})
                                AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                            WHERE acl.grantee=0 AND acl.privilege_type='SELECT'))
                    """;
            bool creatorIntersection = login == enrollment.GlobalLogin && creatorGrants.Length != 0;
            string creatorGrant = creatorIntersection
                ? $"r.rolname={Text(enrollment.GlobalLogin)} AND a.attnum>0 AND ({string.Join(" OR ", creatorGrants)})"
                : "false";
            string creatorProof = creatorIntersection
                ? PostgresTenantCreationPermissions.OptionalGlobalCataloguePredicate(context, enrollment) : "true";
            bool workerIntersection = login == enrollment.GlobalLogin && workerGrants.Length != 0;
            string workerGrant = workerIntersection
                ? $"r.rolname={Text(enrollment.GlobalLogin)} AND ({string.Join(" OR ", workerGrants)})"
                : "false";
            string workerProof = workerIntersection
                ? PostgresManagedTenantWorkerCreationPermissions.OptionalGlobalCataloguePredicate(context, enrollment) : "true";
            string membershipRead = login == enrollment.GlobalLogin && membershipReads.Length != 0 ? $"""
                r.rolname={Text(enrollment.GlobalLogin)} AND a.attnum>0 AND permission.privilege='SELECT'
                AND ({string.Join(" OR ", membershipReads)})
                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                    WHERE acl.grantee=r.oid AND acl.grantor=t.relowner
                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                    WHERE acl.grantee=0 AND acl.privilege_type='SELECT')
                """ : "false";
            return $"""
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE pg_catalog.pg_has_role({Text(login)},r.oid,'MEMBER')
                        AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                        WHERE t.oid IN ({relations}) AND pg_catalog.pg_has_role({Text(login)},r.oid,'MEMBER')
                            AND (r.oid = t.relowner
                                OR (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER')
                                    OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                                OR (pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE') AND NOT ({delete}))
                                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                    WHERE a.attrelid = t.oid AND a.attnum <> 0 AND NOT a.attisdropped
                                        AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                            OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege)
                                                AND NOT ((permission.privilege='SELECT' AND ({referenceRead})) OR ({creatorGrant}) OR ({workerGrant}) OR ({membershipRead}) OR ({mutationGrant})))))))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                        WHERE pg_catalog.pg_has_role({Text(login)},r.oid,'MEMBER')
                            AND pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(names.Schema)}),'CREATE'))
                    OR NOT ({creatorProof})
                    OR NOT ({workerProof})
                    OR NOT ({mutationProof})
                THEN RAISE EXCEPTION '{diagnosticPrefix}_private_source_privilege' USING ERRCODE = '42501'; END IF;
                """;
        }));
    }

    internal static string SourceGuard(ExploreDbContext context, HumanPrincipalSourceSql names,
        IReadOnlyCollection<Type> sources, PostgresNativeQueueEnrollment enrollment, string diagnosticPrefix)
    {
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION '{diagnosticPrefix}_namespace_drift' USING ERRCODE = '55000'; END IF;
            """ + string.Join("\n", sources.Select(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(type)!;
            var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
            string table = Text(names.Table(type));
            string columns = string.Join(" OR ", entity.GetProperties().Select(property =>
            {
                var declaredProperty = declared.FindProperty(property.Name)!;
                string collation = declaredProperty.GetCollation()
                    ?? (property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                        || property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true ? "C" : "default");
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declaredProperty) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attname = {Text(names.ColumnName(type, property.Name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {Boolean(!property.IsNullable)} AND a.attgenerated = '' AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + names.Quote(collation))})))
                    """;
            }));
            string primary = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(property =>
                Text(names.ColumnName(type, property.Name))));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relispartition
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND t.relrowsecurity = {Boolean(tenant is not null)} AND t.relforcerowsecurity = {Boolean(tenant is not null)})
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {entity.GetProperties().Count()}
                    OR {columns}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = {table}::regclass)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = {table}::regclass OR i.inhparent = {table}::regclass)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = {table}::regclass
                        AND i.indisprimary AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = ARRAY[{primary}]::name[])
                THEN RAISE EXCEPTION '{diagnosticPrefix}_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {(tenant is null ? "" : $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                        AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, names.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    THEN RAISE EXCEPTION '{diagnosticPrefix}_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                    """)}
                """;
        })) + RelationalKeysGuard(context, names, sources, diagnosticPrefix);
    }

    private static string RelationalKeysGuard(ExploreDbContext context, HumanPrincipalSourceSql names,
        IReadOnlyCollection<Type> sources, string diagnosticPrefix)
    {
        string Fields(Type type, IEnumerable<IProperty> properties) =>
            "ARRAY[" + string.Join(",", properties.Select(property => Text(names.ColumnName(type, property.Name)))) + "]::name[]";
        return string.Join("\n", sources.SelectMany(type =>
        {
            var entity = context.Model.FindEntityType(type)!;
            string table = Text(names.Table(type));
            var keys = entity.GetKeys().Select(key => key.Properties)
                .Concat(entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null)
                    .Select(index => index.Properties));
            return keys.Select(properties => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class idx ON idx.oid = i.indexrelid
                    JOIN pg_catalog.pg_am method ON method.oid = idx.relam
                    WHERE i.indrelid = {table}::regclass AND i.indisunique AND i.indisvalid AND i.indisready
                        AND i.indimmediate AND NOT i.indisexclusion AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND method.amname = 'btree' AND i.indnkeyatts = {properties.Count} AND i.indnatts = {properties.Count}
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                            JOIN pg_catalog.pg_opclass op ON op.oid = operator_id
                            WHERE NOT op.opcdefault OR op.opcmethod <> idx.relam)
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                            JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                            WHERE col.value <> a.attcollation)
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {Fields(type, properties)})
                THEN RAISE EXCEPTION '{diagnosticPrefix}_unique_drift' USING ERRCODE = '55000'; END IF;
                """).Concat(entity.GetForeignKeys().Where(key => sources.Contains(key.PrincipalEntityType.ClrType))
                .Select(key => $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint fk
                        WHERE fk.contype = 'f' AND fk.convalidated AND NOT fk.condeferrable AND NOT fk.condeferred
                            AND fk.confmatchtype = 's' AND fk.confupdtype = 'a'
                            AND fk.confdeltype = '{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c"
                                : key.DeleteBehavior == DeleteBehavior.SetNull ? "n"
                                : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                            AND fk.conrelid = {table}::regclass
                            AND fk.confrelid = {Text(names.Table(key.PrincipalEntityType.ClrType))}::regclass
                            AND ARRAY(SELECT a.attname FROM unnest(fk.conkey) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {Fields(type, key.Properties)}
                            AND ARRAY(SELECT a.attname FROM unnest(fk.confkey) WITH ORDINALITY k(id,ord)
                                JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.confrelid AND a.attnum = k.id ORDER BY k.ord)
                                = {Fields(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)}
                            AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint = fk.oid)
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                                WHERE t.tgconstraint = fk.oid AND t.tgenabled NOT IN ('O','A')))
                    THEN RAISE EXCEPTION '{diagnosticPrefix}_foreign_key_drift' USING ERRCODE = '55000'; END IF;
                    """));
        }));
    }

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
