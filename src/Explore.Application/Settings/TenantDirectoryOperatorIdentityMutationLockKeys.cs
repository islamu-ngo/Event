using Explore.Domain.Settings.Documents;

namespace Explore.Application.Settings;

public static class TenantDirectoryOperatorIdentityMutationLockKeys
{
    public static string ForTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant identity lock requires a tenant id.", nameof(tenantId));

        return $"{SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity}:{tenantId:D}";
    }
}
