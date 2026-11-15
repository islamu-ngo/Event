using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

public static partial class PostgresExternalTenantEnrollmentPermissions
{
    /// <summary>Pure declared columns for intersecting owners; never derives authority from installed ACLs.</summary>
    internal static IReadOnlyList<(string Column, string Privilege)> RuntimeColumns(ExploreDbContext context, Type type) =>
        Sources(context).SingleOrDefault(source => source.Type == type)?.Own
            .Select(grant => (grant.Column, grant.Privilege)).ToArray() ?? [];

    internal static bool Includes(Type type) =>
        ExternalTenantEnrollmentInventory.Sources.Any(source => source.Entity == type);

    /// <summary>A leaf catalogue proof: no executable guard or composed permission owner is called here.</summary>
    internal static string OptionalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"(({Catalogue(context, enrollment, complete: false)}) AND (({Absent(context, enrollment)}) OR ({Catalogue(context, enrollment, complete: true)})))";

    /// <summary>Shares a leaf inside one SQL expression, never an observed result across statements or role changes.</summary>
    internal static string ComposeCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string? catalogueReference, Func<string, string> predicate)
    {
        if (catalogueReference is not null)
            return predicate(catalogueReference);

        return $"""
            (WITH external_enrollment_catalogue AS MATERIALIZED
                (SELECT ({OptionalCataloguePredicate(context, enrollment)}) AS ready)
            SELECT ({predicate("(SELECT ready FROM external_enrollment_catalogue)")}))
            """;
    }

    internal static string RuntimeColumnPredicate(ExploreDbContext context, Type type, string runtimeLogin,
        string roleOid, string relationOid, string columnName, string privilege)
    {
        var source = Sources(context).SingleOrDefault(value => value.Type == type);
        if (source is null || source.Own.Length == 0) return "false";
        string allowed = string.Join(" OR ", source.Own.Select(grant =>
            $"({columnName}={Text(grant.Column)} AND {privilege}={Text(grant.Privilege)})"));
        return $"""
            ({roleOid}={Role(runtimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(source.Table)})
                AND ({allowed})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a_owned
                    JOIN pg_catalog.pg_class t_owned ON t_owned.oid=a_owned.attrelid
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a_owned.attacl) acl_owned
                    WHERE a_owned.attrelid={relationOid} AND a_owned.attname={columnName} AND a_owned.attnum>0
                        AND acl_owned.grantee={roleOid} AND acl_owned.grantor=t_owned.relowner
                        AND acl_owned.privilege_type={privilege} AND NOT acl_owned.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a_public
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a_public.attacl) acl_public
                    WHERE a_public.attrelid={relationOid} AND a_public.attname={columnName}
                        AND acl_public.grantee=0 AND acl_public.privilege_type={privilege}))
            """;
    }

    private static string Absent(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join(" AND ", Sources(context).Select(source =>
        {
            var addition = source.Own.Except(source.Baseline).ToArray();
            if (addition.Length == 0) return "true";
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                        AND acl.grantee={Role(enrollment.RuntimeLogin)}
                        AND ({string.Join(" OR ", addition.Select(grant => $"(a.attname={Text(grant.Column)} AND acl.privilege_type={Text(grant.Privilege)})"))}))
                """;
        }));

    private static string Catalogue(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        enrollment.Validate();
        var sources = Sources(context);
        string expected = string.Join(',', sources.SelectMany(source => source.Own.Concat(source.Baseline).Distinct()
            .Select(grant => $"({Text(source.Table)},{Text(grant.Column)},{Text(grant.Privilege)})")));
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string requiredGrants = string.Join(',', sources.SelectMany(source => source.Own
            .Select(grant => $"({Text(source.Table)},{Text(grant.Column)},{Text(grant.Privilege)})")));
        string required = complete ? $"""
            NOT EXISTS (SELECT 1 FROM required_grants required
                WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass(required.relation) AND a.attname=required.column_name
                        AND acl.grantee={Role(enrollment.RuntimeLogin)} AND acl.grantor={Role(enrollment.MigratorLogin)}
                        AND acl.privilege_type=required.privilege AND NOT acl.is_grantable))
            """ : "true";
        return $"""
            COALESCE((WITH expected(relation,column_name,privilege) AS (VALUES {expected}),
            required_grants(relation,column_name,privilege) AS (VALUES {requiredGrants}),
            members AS (SELECT r.* FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={Role(enrollment.RuntimeLogin)} AND rolcanlogin)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={Role(enrollment.MigratorLogin)} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={Role(enrollment.RuntimeLogin)})
                AND NOT pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},{Role(enrollment.GlobalLogin)},'MEMBER')
                AND NOT pg_catalog.pg_has_role({Role(enrollment.GlobalLogin)},{Role(enrollment.RuntimeLogin)},'MEMBER')
                AND NOT EXISTS (SELECT 1 FROM members r WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb
                    OR r.rolcreaterole OR r.rolreplication
                    OR EXISTS (SELECT 1 FROM unnest({tables}) target(relation)
                        JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(target.relation)
                        WHERE r.oid=t.relowner OR pg_catalog.has_schema_privilege(r.oid,t.relnamespace,'CREATE')
                            OR (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                    AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                        OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                            r.oid={Role(enrollment.RuntimeLogin)} AND a.attnum>0
                                            AND EXISTS (SELECT 1 FROM expected e WHERE e.relation=target.relation
                                                AND e.column_name=a.attname AND e.privilege=permission.privilege)
                                            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.grantee=r.oid AND acl.grantor={Role(enrollment.MigratorLogin)}
                                                    AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.privilege_type=permission.privilege
                                                    AND (acl.grantee=0 OR (acl.grantee=r.oid
                                                        AND acl.grantor<>{Role(enrollment.MigratorLogin)})))))))))
                AND ({required})
                AND ({string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment, complete)))})),false)
            """;
    }

    private static Source[] Sources(ExploreDbContext context) =>
        ExternalTenantEnrollmentInventory.Sources.Select(item =>
        {
            ExternalTenantEnrollmentInventory.RequireModel(context, item);
            var entity = context.Model.FindEntityType(item.Entity)!;
            string name = entity.GetTableName()!;
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
                ?? entity.GetSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(name, entity.GetSchema());
            var columns = entity.GetProperties().ToDictionary(property => property.Name,
                property => new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable),
                StringComparer.Ordinal);
            Grant[] own = [.. item.Read.Select(property => new Grant(columns[property].Name, "SELECT")),
                .. item.Insert.Select(property => new Grant(columns[property].Name, "INSERT"))];
            PostgresTenantMembershipMutationPermissions.TryGetRuntimeColumns(context, item.Entity, out var membershipRead, out var membershipUpdate, out _);
            PostgresActorOwnershipReadPermissions.TryGetRuntimeReadColumns(context, item.Entity, out var ownershipRead);
            Grant[] baseline = [.. membershipRead.Concat(ownershipRead)
                    .Concat(PostgresTenantApiKeyStoragePermissions.RuntimeParentReadColumns(context, item.Entity))
                    .Distinct().Select(column => new Grant(column, "SELECT")),
                .. membershipUpdate.Select(column => new Grant(column, "UPDATE"))];
            return new Source(item.Entity, schema, name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema),
                columns, own, baseline);
        }).ToArray();

    private sealed record Grant(string Column, string Privilege);
    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(Type Type, string Schema, string Name, string Table,
        Dictionary<string, Column> Columns, Grant[] Own, Grant[] Baseline);
}
