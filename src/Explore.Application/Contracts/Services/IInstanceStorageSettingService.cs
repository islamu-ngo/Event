using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Service for reading and applying instance storage settings.
/// </summary>
public interface IInstanceStorageSettingService
{
    /// <summary>
    /// Reads current storage settings from SystemSetting records.
    /// </summary>
    /// <returns>Current storage settings DTO.</returns>
    Task<InstanceStorageSettingsDto> ReadSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies storage settings to SystemSetting records.
    /// </summary>
    /// <param name="settings">The storage settings to apply.</param>
    Task ApplySettingsAsync(InstanceStorageSettingsDto settings, PatchInstanceStorageSettingsDto patch);

    Task<InstanceStorageProviderStatusDto> TestProviderAsync(CancellationToken cancellationToken = default);

    Task<InstanceStorageUsageDto> RecalculateUsageAsync(CancellationToken cancellationToken = default);
}
