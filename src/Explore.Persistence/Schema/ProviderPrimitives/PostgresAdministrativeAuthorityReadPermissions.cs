using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Schema;

/// <summary>
/// Fixed primitive Global reads for administrative scope enumeration.
/// Subject, role, lifecycle and tenant decisions belong to the application queries.
/// </summary>
public static class PostgresAdministrativeAuthorityReadPermissions
{
    private const string Policy = "administrative_scope_global_read";
    private const string Drift = "administrative_scope_read_permissions_drift";
    private static readonly (Type Type, string[] Properties, bool Tenant)[] Inventory =
    [
        (typeof(TenantUserRoleGrant), ["TenantId", "TenantUserId", "RoleId", "RevokedAt"], true),
        (typeof(OrganizationMember), ["UserId", "TenantId", "RoleId", "OrganizationTenantId", "IsDeleted"], true),
        (typeof(OrganizationTenant), ["Id", "TenantId", "OrganizationId", "IsDeleted"], true),
        (typeof(Organization), ["Id", "IsDeleted"], false),
        (typeof(GroupMember), ["UserId", "TenantId", "RoleId", "GroupTenantId", "IsDeleted"], true),
        (typeof(GroupTenant), ["Id", "TenantId", "GroupId", "IsDeleted"], true),
        (typeof(Group), ["Id", "IsDeleted"], false)
    ];

    internal static bool TryGetGlobalReadColumns(ExploreDbContext context, Type type, out IReadOnlyList<string> columns) =>
        PostgresGlobalReadPermissionsHelper.TryGetGlobalReadColumns(ReadSources(context), type, out columns);

    internal static IReadOnlyList<string> GlobalSourcePolicies(ExploreDbContext context, Type type, string login) =>
        PostgresGlobalReadPermissionsHelper.GlobalSourcePolicies(ReadSources(context), type, Policy, login);

    internal static string GlobalPolicyException(ExploreDbContext context, string login) =>
        "(" + string.Join(" OR ", ReadSources(context).Where(source => source.TenantMetadata is not null)
            .Select(source => $"(session_user={PostgresGlobalReadPermissionsHelper.Text(login)} AND current_user=session_user AND n.nspname={PostgresGlobalReadPermissionsHelper.Text(source.Schema)} AND c.relname={PostgresGlobalReadPermissionsHelper.Text(source.Name)} AND ({PostgresGlobalReadPermissionsHelper.BuildPolicyPredicateSql(source, Policy, login)}))")) + ")";

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) =>
        ReadSources(context).Select(source => source.Table).ToArray();

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"({NoObsoleteInstallation(context)}) AND ({Manifest(context, ReadSources(context), enrollment, true)})";

    internal static string OptionalGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sources = ReadSources(context);
        return $"({NoObsoleteInstallation(context)}) AND ({Manifest(context, sources, enrollment, false)}) AND (({Fresh(context, sources, enrollment)}) OR ({Manifest(context, sources, enrollment, true)}))";
    }

    public static Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return Task.CompletedTask;
        enrollment.Validate();
        var sources = ReadSources(context);
        return PostgresGlobalReadPermissionsHelper.ApplyWithSavepointAsync(
            context,
            enrollment,
            sources,
            Policy,
            "administrative_scope",
            "administrative_scope",
            Drift,
            NoObsoleteInstallation(context),
            complete => Manifest(context, sources, enrollment, complete),
            Fresh(context, sources, enrollment),
            cancellationToken);
    }

    private static string Manifest(ExploreDbContext context, PostgresGlobalReadPermissionsHelper.SourceDescriptor[] sources,
        PostgresNativeQueueEnrollment enrollment, bool complete) =>
        PostgresGlobalReadPermissionsHelper.BuildManifestSql(
            context, sources, enrollment, Policy, complete,
            includeRuntimePrivilegeAudit: false, checkNotPartition: false);

    private static string Fresh(ExploreDbContext context, PostgresGlobalReadPermissionsHelper.SourceDescriptor[] sources,
        PostgresNativeQueueEnrollment enrollment) =>
        PostgresGlobalReadPermissionsHelper.BuildFreshPredicateSql(context, sources, enrollment, Policy);

    private static string NoObsoleteInstallation(ExploreDbContext context)
    {
        var names = new HumanPrincipalSourceSql(context);
        (Type Type, string Suffix)[] retired =
        [
            (typeof(PlatformUserRole), "_is_admin_for_user"), (typeof(TenantUserRoleGrant), "_admin_tenants_for_user"),
            (typeof(OrganizationMember), "_admin_organizations_for_user"), (typeof(GroupMember), "_admin_groups_for_user")
        ];
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                WHERE rolname IN ('event_admin_authority_owner','event_admin_authority_migrator','event_admin_authority_global'))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname={PostgresGlobalReadPermissionsHelper.Text(names.Schema)} AND p.proname IN
                    ({string.Join(',', retired.Select(item => PostgresGlobalReadPermissionsHelper.Text(context.Model.FindEntityType(item.Type)!.GetTableName()! + item.Suffix)))}))
            """;
    }

    private static PostgresGlobalReadPermissionsHelper.SourceDescriptor[] ReadSources(ExploreDbContext context) =>
        PostgresGlobalReadPermissionsHelper.ReadSources(
            context,
            Inventory.Select(item => (item.Type, item.Properties)),
            (type, metadata) =>
            {
                bool tenant = Inventory.Single(item => item.Type == type).Tenant;
                return tenant
                    ? metadata is not null && !metadata.AllowsNullTenant && metadata.ParentJoin is null
                    : metadata is null;
            },
            "administrative_scope");
}
