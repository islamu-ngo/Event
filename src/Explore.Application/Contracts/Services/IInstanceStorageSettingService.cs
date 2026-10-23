using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Settings;

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
    Task<StorageSettingsMutation> ObserveConfigurationAsync(UpdateInstanceStorageSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task ApplySettingsAsync(StorageSettingsMutation observation, CancellationToken cancellationToken = default);

    Task<InstanceStorageProviderStatusDto> TestProviderAsync(
        TestInstanceStorageProviderQuery query, CancellationToken cancellationToken = default);

    Task<InstanceStorageProviderStatusDto> TestProviderAsync(
        TestInstanceStorageProviderDuringSetupQuery query, CancellationToken cancellationToken = default);

    Task<InstanceStorageUsageDto> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageCommand command, CancellationToken cancellationToken = default);

    Task<InstanceStorageUsageDto> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageDuringSetupCommand command, CancellationToken cancellationToken = default);
}
