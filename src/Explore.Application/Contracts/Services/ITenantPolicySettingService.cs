using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.Notifications;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Service for reading and applying tenant policy settings with instance-level governance constraints.
/// </summary>
public interface ITenantPolicySettingService
{
    /// <summary>
    /// Reads effective tenant policy settings by merging tenant overrides with instance defaults.
    /// Includes read-only CanOverride* flags derived from instance governance locks.
    /// </summary>
    Task<TenantPolicySettingsDto> ReadEffectiveTenantSettingsAsync(Guid tenantId);

    /// <summary>
    /// Applies tenant policy setting overrides while enforcing instance-level delegation constraints.
    /// Only writable fields from UpdateTenantPolicyRequest are persisted.
    /// </summary>
    Task<IReadOnlyList<SettingChangedNotification>> ApplyTenantSettingsAsync(
        Guid tenantId,
        Guid? actorUserId,
        UpdateTenantPolicyRequest settings,
        CancellationToken cancellationToken = default);
}
