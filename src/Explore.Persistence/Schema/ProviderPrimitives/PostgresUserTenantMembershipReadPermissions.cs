using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Schema;

/// <summary>
/// Fixed Global membership graph reads. Subject selection and membership status decisions belong to C#;
/// these shared credential permissions are not human authorization.
/// </summary>
public static class PostgresUserTenantMembershipReadPermissions
{
    private const string Policy = "membership_directory_global_read";
    private const string Drift = "membership_read_permissions_drift";
    private static readonly (Type Type, string[] Properties)[] Inventory =
    [
        (typeof(TenantUser), ["Id", "TenantId", "UserId", "ActorId", "StatusId", "JoinedAt", "SuspendedAt",
            "SuspendedBy", "BanExpiresAt", "RemovedAt", "RemovedBy", "ModerationNote", "CreatedAt", "CreatedBy",
            "UpdatedAt", "UpdatedBy", "IsDeleted", "DeletedAt", "DeletedBy"]),
        (typeof(Tenant), ["Id", "FullName", "Slug", "Description", "TenantStatusId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"]),
        (typeof(TenantStatus), ["Id", "MasterCode", "FullName", "Description", "IsActiveState"])
    ];

    /// <summary>Pure source-owned descriptors for intersecting permission manifests; never database-derived authority.</summary>
    internal static bool TryGetGlobalReadColumns(ExploreDbContext context, Type type, out IReadOnlyList<string> columns) =>
        PostgresGlobalReadPermissionsHelper.TryGetGlobalReadColumns(ReadSources(context), type, out columns);

    internal static IReadOnlyList<string> GlobalSourcePolicies(ExploreDbContext context, Type type, string login) =>
        PostgresGlobalReadPermissionsHelper.GlobalSourcePolicies(ReadSources(context), type, Policy, login);

    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        var source = ReadSources(context).Single(source => source.Type == typeof(TenantUser));
        return $"(session_user={PostgresGlobalReadPermissionsHelper.Text(login)} AND current_user=session_user AND n.nspname={PostgresGlobalReadPermissionsHelper.Text(source.Schema)} AND c.relname={PostgresGlobalReadPermissionsHelper.Text(source.Name)} AND ({PostgresGlobalReadPermissionsHelper.BuildPolicyPredicateSql(source, Policy, login)}))";
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
            "membership_permissions",
            "membership",
            Drift,
            NoObsoleteInstallation(sources),
            complete => Manifest(context, sources, enrollment, complete),
            Fresh(context, sources, enrollment),
            cancellationToken);
    }

    public static Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default) =>
        PostgresGlobalReadPermissionsHelper.RequireReadyWithCustodyAsync(
            context,
            enrollment,
            "membership_enrollment_required",
            "membership_original_owner_changed",
            "membership_read_permissions_not_ready",
            () => NoObsoleteInstallation(ReadSources(context)),
            () => Manifest(context, ReadSources(context), enrollment!, complete: true),
            cancellationToken);

    private static string Manifest(ExploreDbContext context, PostgresGlobalReadPermissionsHelper.SourceDescriptor[] sources,
        PostgresNativeQueueEnrollment enrollment, bool complete) =>
        PostgresGlobalReadPermissionsHelper.BuildManifestSql(
            context, sources, enrollment, Policy, complete,
            includeRuntimePrivilegeAudit: true, checkNotPartition: true);

    private static string Fresh(ExploreDbContext context, PostgresGlobalReadPermissionsHelper.SourceDescriptor[] sources,
        PostgresNativeQueueEnrollment enrollment) =>
        PostgresGlobalReadPermissionsHelper.BuildFreshPredicateSql(
            context, sources, enrollment, Policy, excludeOwnSelectFromBaseline: false);

    private static string NoObsoleteInstallation(PostgresGlobalReadPermissionsHelper.SourceDescriptor[] sources) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
            ('event_user_membership_owner','event_user_membership_global','event_user_membership_migrator'))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname={PostgresGlobalReadPermissionsHelper.Text(sources[0].Schema)} AND p.proname={PostgresGlobalReadPermissionsHelper.Text(sources[0].Name + "_active_for_user")})
        """;

    private static PostgresGlobalReadPermissionsHelper.SourceDescriptor[] ReadSources(ExploreDbContext context) =>
        PostgresGlobalReadPermissionsHelper.ReadSources(
            context,
            Inventory,
            (type, metadata) => type == typeof(TenantUser)
                ? metadata is not null && !metadata.AllowsNullTenant && metadata.ParentJoin is null
                : metadata is null,
            "membership");
}
