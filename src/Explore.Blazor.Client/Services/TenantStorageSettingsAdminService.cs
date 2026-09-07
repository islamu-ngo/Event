using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public interface ITenantStorageSettingsAdminService
{
    Task<HalResourceOfTenantStorageSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PatchPolicyAsync(HalResourceOfTenantStorageSettingsDto settings, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PatchS3Async(HalResourceOfTenantStorageSettingsDto settings, CancellationToken cancellationToken = default);
    Task<InstanceStorageProviderStatusDto> TestProviderAsync(HalResourceOfTenantStorageSettingsDto settings, CancellationToken cancellationToken = default);
}

public sealed class TenantStorageSettingsAdminService(
    ITenantStorageSettingsClient api,
    ILogger<TenantStorageSettingsAdminService> logger) : ITenantStorageSettingsAdminService
{
    public async Task<HalResourceOfTenantStorageSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await api.GetTenantStorageSettingsAsync(cancellationToken: cancellationToken);
            return response.InitializeForEditing();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tenant storage settings.");
            return new HalResourceOfTenantStorageSettingsDto().InitializeForEditing();
        }
    }

    public Task<BaseCommandResponseOfGuid> PatchPolicyAsync(
        HalResourceOfTenantStorageSettingsDto settings,
        CancellationToken cancellationToken = default) =>
        PatchAsync(settings, settings.ToPolicyPatchRequest(), "policy", cancellationToken);

    public Task<BaseCommandResponseOfGuid> PatchS3Async(
        HalResourceOfTenantStorageSettingsDto settings,
        CancellationToken cancellationToken = default) =>
        PatchAsync(settings, settings.ToS3PatchRequest(), "S3", cancellationToken);

    public async Task<InstanceStorageProviderStatusDto> TestProviderAsync(
        HalResourceOfTenantStorageSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.HasLink("provider-test"))
        {
            return new InstanceStorageProviderStatusDto
            {
                Provider = settings.Provider,
                IsAvailable = false,
                FailureCode = "provider_test_not_allowed",
                Message = "The API did not expose a tenant storage test affordance."
            };
        }

        try
        {
            return await api.TestTenantStorageConnectionAsync(cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Tenant storage provider test failed. FailureType={FailureType}",
                exception.GetType().Name);
            return new InstanceStorageProviderStatusDto
            {
                Provider = settings.Provider,
                IsAvailable = false,
                FailureCode = "provider_test_failed",
                Message = "Tenant storage provider test failed."
            };
        }
    }

    private async Task<BaseCommandResponseOfGuid> PatchAsync(
        HalResourceOfTenantStorageSettingsDto settings,
        PatchTenantStorageSettingsDto request,
        string group,
        CancellationToken cancellationToken)
    {
        if (!settings.IsEditable())
        {
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "The API did not expose a tenant storage edit affordance."
            };
        }

        try
        {
            return await api.PatchTenantStorageSettingsAsync(
                request,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to patch tenant storage {Group} settings.", group);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Tenant storage settings save failed.",
                Errors = [ex.Message]
            };
        }
    }
}
