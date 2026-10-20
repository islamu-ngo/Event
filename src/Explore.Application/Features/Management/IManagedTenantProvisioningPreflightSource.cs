using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Modules;

namespace Explore.Application.Features.Management;

/// <summary>
/// Read-only source for the common provisioning policy. Implementations retain
/// their own original observation/transaction; this interface grants no authority.
/// </summary>
public interface IManagedTenantProvisioningPreflightSource
{
    Task<bool> TenantSlugExistsAsync(string slug, CancellationToken cancellationToken);
    Task<TenantPlanVersion?> GetPlanVersionAsync(Guid versionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModuleDefinition>> GetActiveModulesAsync(IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken);
    Task<bool> DomainHostExistsAsync(string host, CancellationToken cancellationToken);
    Task<SystemSetting?> ReadPolicySettingAsync(string key, CancellationToken cancellationToken);
    Task<bool> IsPolicyLockedAsync(string key, CancellationToken cancellationToken);
    Task<LocalIdentityBinding?> ReadLocalAdministratorAsync(Guid subject, CancellationToken cancellationToken);
    Task<string?> ValidateStorageQuotasAsync(IEnumerable<TenantPlanVersionQuota> quotas,
        CancellationToken cancellationToken);
    Task<TenantBrandingSettingsDocumentLockState> ReadBrandingLocksAsync(CancellationToken cancellationToken);
}
