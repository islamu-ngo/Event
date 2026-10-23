using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Settings;

namespace Explore.Application.Contracts.Services;

public interface ITenantStorageSettingService
{
    Task<TenantStorageSettingsDto> ReadSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<StorageSettingsMutation> ObserveConfigurationAsync(
        PatchTenantStorageSettingsCommand command,
        CancellationToken cancellationToken = default);
    Task ApplyPatchAsync(StorageSettingsMutation observation, CancellationToken cancellationToken = default);

    Task<InstanceStorageProviderStatusDto> TestProviderAsync(
        TestTenantStorageProviderCommand command,
        CancellationToken cancellationToken = default);
}
