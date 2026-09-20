using Explore.Domain.Enums;

namespace Explore.Domain.ValueObjects;

/// <summary>Pure lifecycle boundary; administrative authority is supplied by the trusted application.</summary>
public static class TenantLifecycleAccessPolicy
{
    public static bool AllowsPublic(int? statusId) => statusId == (int)TenantStatusEnum.Active;

    public static bool AllowsManagement(Guid? tenantId, int? statusId, Guid? authorizedTenantId, bool isInstanceAdministrator) =>
        tenantId is { } id && id != Guid.Empty
        && statusId is (int)TenantStatusEnum.Provisioning or (int)TenantStatusEnum.Active
            or (int)TenantStatusEnum.Suspended or (int)TenantStatusEnum.Archived
        && (isInstanceAdministrator || authorizedTenantId == id);
}
