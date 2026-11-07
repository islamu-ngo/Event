using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

public static partial class PostgresSetupLiveStoragePermissions
{
    // These are storage rights of the original Global setup owner. Completion
    // authority and identity/application crash recovery stay in the application.
    private static readonly (Type Type, string[] Properties, bool Insert, bool Delete)[] GlobalCompletionStorage =
    [
        (typeof(PrivacyErasureSaga), ["SubjectId"], false, false),
        (typeof(UserIdentityEmailClaim), ["Id", "UserId", "NormalizedEmail"], true, true),
        (typeof(UserIdentityEmailEvidence),
            ["Id", "UserId", "ClaimId", "ExternalLoginId", "ObservedAt", "IsActive"], true, true),
        (typeof(PlatformUserRole), ["Id", "UserId", "RoleId", "GrantedAt", "GrantedBy"], true, false),
        (typeof(Role), ["Id", "MasterCode", "FullName", "Description", "RoleScopeId", "IsSystem"], false, false)
    ];

    internal static (string Column, string Privilege)[] GlobalCompletionColumns(ExploreDbContext context, Type type)
    {
        var item = GlobalCompletionStorage.SingleOrDefault(item => item.Type == type);
        if (item.Type is null) return [];
        var entity = context.Model.FindEntityType(type)
            ?? throw new InvalidOperationException("global_setup_completion_model_missing");
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.GetProperties().Any(property => property.IsShadowProperty()))
            throw new InvalidOperationException("global_setup_completion_model_shape_drift");
        if (type != typeof(PrivacyErasureSaga)
            && !entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(item.Properties.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("global_setup_completion_model_drift");
        if (entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded)
            || entity.GetSkipNavigations().Any(navigation => navigation.IsEagerLoaded))
            throw new InvalidOperationException("global_setup_completion_auto_include_drift");
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return item.Properties.SelectMany(property =>
        {
            string column = entity.FindProperty(property)?.GetColumnName(store)
                ?? throw new InvalidOperationException("global_setup_completion_property_missing");
            return (item.Insert ? new[] { "SELECT", "INSERT" } : ["SELECT"])
                .Select(privilege => (column, privilege));
        }).ToArray();
    }

    internal static string[] GlobalCompletionRelations(ExploreDbContext context) =>
        GlobalCompletionStorage.Select(item => GlobalCompletionTable(context, item.Type)).ToArray();

    // Before setup completion is installed, only the existing creator/worker
    // Role SELECT baseline is allowed. Any addition requires the entire profile.
    internal static string OptionalGlobalCompletionCataloguePredicate(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string global = SettingText(enrollment.GlobalLogin);
        string absent = string.Join(" AND ", GlobalCompletionStorage.Select(item =>
        {
            string table = SettingText(GlobalCompletionTable(context, item.Type));
            var entity = context.Model.FindEntityType(item.Type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string baseline = item.Type != typeof(Role) ? "false" : string.Join(" OR ",
                new[] { "Id", "MasterCode", "RoleScopeId" }.Select(property =>
                    $"a.attname={SettingText(entity.FindProperty(property)!.GetColumnName(store)!)}"));
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN LATERAL pg_catalog.aclexplode(t.relacl) acl
                    WHERE t.oid=pg_catalog.to_regclass({table}) AND acl.grantee=pg_catalog.to_regrole({global})::oid)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({table}) AND acl.grantee=pg_catalog.to_regrole({global})::oid
                        AND NOT (acl.privilege_type='SELECT' AND ({baseline})))
                """;
        }));
        return $"((({GlobalCompletionCataloguePredicate(context, enrollment, complete: false)}) AND ({absent})) OR ({GlobalCompletionCataloguePredicate(context, enrollment)}))";
    }

    private static string GlobalCompletionTable(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        string schema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, schema);
    }

    internal static string GlobalCompletionCataloguePredicate(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool complete = true)
    {
        string global = SettingText(enrollment.GlobalLogin);
        string migrator = SettingText(enrollment.MigratorLogin);
        return string.Join(" AND ", GlobalCompletionStorage.Select(item =>
        {
            string table = SettingText(GlobalCompletionTable(context, item.Type));
            var columns = GlobalCompletionColumns(context, item.Type);
            var entity = context.Model.FindEntityType(item.Type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string shape = string.Join(" AND ", item.Properties.Select(name =>
            {
                var property = entity.FindProperty(name)!;
                return $"""
                    EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid
                        AND a.attname={SettingText(property.GetColumnName(store)!)} AND a.attnum>0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod)={SettingText(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull={(property.IsNullable ? "false" : "true")} AND a.attgenerated='' AND a.attidentity='')
                    """;
            }));
            string expected = "VALUES " + string.Join(',', columns.Select(column =>
                $"({SettingText(column.Column)},{SettingText(column.Privilege)})"));
            string required = !complete ? "" : $"""
                AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=t.oid AND a.attname=e.column_name AND acl.grantee=pg_catalog.to_regrole({global})::oid
                        AND acl.grantor=t.relowner AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
                {(item.Delete ? $"AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=pg_catalog.to_regrole({global})::oid AND acl.grantor=t.relowner AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)" : "")}
                """;
            // Native readers retain dormant policies. They cannot affect this
            // profile while both RLS flags below remain disabled.
            return $"""
                (WITH expected(column_name,privilege) AS ({expected}),
                members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE r.rolname={global}
                    OR pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
                SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid=pg_catalog.to_regclass({table})
                    AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                    AND t.relowner=pg_catalog.to_regrole({migrator})::oid
                    AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                    AND ({shape})
                    {(item.Type == typeof(PrivacyErasureSaga) ? "" : $"AND (SELECT count(*)={item.Properties.Length} FROM pg_catalog.pg_attribute WHERE attrelid=t.oid AND attnum>0 AND NOT attisdropped)")}
                    AND NOT EXISTS (SELECT 1 FROM members r WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb
                        OR r.rolcreaterole OR r.rolreplication OR r.oid=t.relowner
                        OR pg_catalog.has_schema_privilege(r.oid,t.relnamespace,'CREATE')
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER')
                        OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)}
                        OR (pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE') AND NOT (
                            {(item.Delete ? "true" : "false")} AND r.rolname={global}
                            AND NOT pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE WITH GRANT OPTION')
                            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl
                                WHERE acl.grantee=r.oid AND acl.grantor=t.relowner
                                    AND acl.privilege_type='DELETE' AND NOT acl.is_grantable))))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl
                        WHERE acl.grantee=0 OR (acl.grantee IN (SELECT oid FROM members)
                            AND NOT ({(item.Delete ? "true" : "false")} AND acl.grantee=pg_catalog.to_regrole({global})::oid
                                AND acl.grantor=t.relowner AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl WHERE a.attrelid=t.oid
                            AND (acl.grantee=0 OR acl.grantee IN (SELECT oid FROM members))
                            AND NOT (a.attnum>0 AND NOT a.attisdropped AND acl.grantee=pg_catalog.to_regrole({global})::oid
                                AND acl.grantor=t.relowner AND NOT acl.is_grantable
                                AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=acl.privilege_type)))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN members r
                        CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                        WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege)
                                    AND NOT (a.attnum>0 AND r.rolname={global}
                                        AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)))))
                    {required}))
                """;
        }));
    }

    // Called only inside ApplyAsync's original Migrator transaction
    // and savepoint. No separate owner, binding stage or transaction is introduced.
    private static async Task ApplyGlobalCompletionAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Action requireOriginal, CancellationToken token)
    {
        requireOriginal();
        if (await SettingsScalar(context, $"SELECT {OptionalGlobalCompletionCataloguePredicate(context, enrollment)}", token) is not true)
            throw new UnauthorizedAccessException("global_setup_completion_storage_drift");
        requireOriginal();
        var names = context.GetService<ISqlGenerationHelper>();
        foreach (var item in GlobalCompletionStorage)
        {
            var grants = GlobalCompletionColumns(context, item.Type).GroupBy(column => column.Privilege)
                .Select(group => $"{group.Key} ({string.Join(',', group.Select(column => names.DelimitIdentifier(column.Column)))})")
                .Concat(item.Delete ? new[] { "DELETE" } : []);
            requireOriginal();
            await context.Database.ExecuteSqlRawAsync(
                $"GRANT {string.Join(',', grants)} ON {GlobalCompletionTable(context, item.Type)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)}", token);
            requireOriginal();
        }
        if (await SettingsScalar(context, $"SELECT {GlobalCompletionCataloguePredicate(context, enrollment)}", token) is not true)
            throw new UnauthorizedAccessException("global_setup_completion_storage_not_ready");
        requireOriginal();
    }
}
