using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Schema;

/// <summary>Fixed membership column inventory shared by intersecting storage owners.</summary>
public static partial class PostgresTenantMembershipMutationPermissions
{
    private static readonly (Type Type, string[] Read, string[] Update, bool Delete)[] Runtime =
    [
        (typeof(TenantUser), ["Id", "TenantId", "UserId", "IsDeleted", "StatusId"],
            ["StatusId", "RemovedAt", "RemovedBy", "IsDeleted", "DeletedAt", "DeletedBy", "UpdatedAt", "UpdatedBy"], false),
        (typeof(TenantUserProfile), ["TenantId", "TenantUserId"], [], true),
        (typeof(TenantUserRoleGrant), ["TenantId", "TenantUserId", "RevokedAt"],
            ["RevokedAt", "RevokedBy", "UpdatedAt", "UpdatedBy"], false)
    ];
    private static readonly string[] GlobalUpdates = ["TenantStatusId", "UpdatedAt", "UpdatedBy"];

    internal static bool TryGetRuntimeColumns(ExploreDbContext context, Type type,
        out IReadOnlyList<string> read, out IReadOnlyList<string> update, out bool delete)
    {
        var item = Runtime.SingleOrDefault(item => item.Type == type);
        read = item.Type is null ? [] : Array.AsReadOnly(item.Read.Select(property => Column(context, type, property)).ToArray());
        update = item.Type is null ? [] : Array.AsReadOnly(item.Update.Select(property => Column(context, type, property)).ToArray());
        delete = item.Delete;
        return item.Type is not null;
    }

    internal static IReadOnlyList<string> GlobalUpdateColumns(ExploreDbContext context, Type type) =>
        type == typeof(Tenant) ? Array.AsReadOnly(GlobalUpdates.Select(property => Column(context, type, property)).ToArray()) : [];

    private static string Column(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }
}
