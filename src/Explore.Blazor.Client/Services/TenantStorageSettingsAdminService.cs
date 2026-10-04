using Explore.Blazor.Client.Clients;
using System.Globalization;
using System.Text.Json;
using System.Web;

namespace Explore.Blazor.Client.Services;

public interface ITenantStorageSettingsAdminService
{
    Task<HalResourceOfTenantStorageSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PatchPolicyAsync(HalResourceOfTenantStorageSettingsDto settings, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PatchS3Async(HalResourceOfTenantStorageSettingsDto settings, CancellationToken cancellationToken = default);
    Task<InstanceStorageProviderStatusDto> TestProviderAsync(HalResourceOfTenantStorageSettingsDto settings, CancellationToken cancellationToken = default);
    Task<HalCollectionResourceOfStorageObjectListDto> GetFilesAsync(HalLink? page = null, CancellationToken cancellationToken = default);
    Task<HalResourceOfStorageObjectDto> GetFileAsync(HalResourceOfStorageObjectListDto file, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> RetireFileAsync(HalResourceOfStorageObjectDto file, CancellationToken cancellationToken = default);
}

public sealed class TenantStorageSettingsAdminService(
    ITenantStorageSettingsClient api,
    IStorageObjectClient files,
    ILogger<TenantStorageSettingsAdminService> logger) : ITenantStorageSettingsAdminService
{
    public Task<HalCollectionResourceOfStorageObjectListDto> GetFilesAsync(
        HalLink? page = null,
        CancellationToken cancellationToken = default)
    {
        if (page is null)
            return files.GetStorageObjectsAsync(pageNumber: 1, pageSize: 20, cancellationToken: cancellationToken);

        var uri = StorageLink(page, "GET");
        if (!uri.AbsolutePath.Equals("/api/storageobject", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage collection link.");
        var query = HttpUtility.ParseQueryString(uri.Query);
        int pageNumber = int.Parse(query["PageNumber"]!, CultureInfo.InvariantCulture);
        int pageSize = int.Parse(query["PageSize"]!, CultureInfo.InvariantCulture);
        if (pageNumber < 1 || pageSize is < 1 or > 100)
            throw new InvalidOperationException("Invalid storage page bounds.");
        return files.GetStorageObjectsAsync(pageNumber, pageSize, query["api-version"], cancellationToken: cancellationToken);
    }

    public Task<HalResourceOfStorageObjectDto> GetFileAsync(
        HalResourceOfStorageObjectListDto file,
        CancellationToken cancellationToken = default)
    {
        if (file._links is not { } links || !links.TryGetValue("self", out var link))
            throw new InvalidOperationException("Storage metadata is unavailable.");
        var uri = StorageLink(link, "GET");
        var id = StorageTarget(uri, file.Id);
        return files.GetStorageObjectByIdAsync(id, HttpUtility.ParseQueryString(uri.Query)["api-version"],
            cancellationToken: cancellationToken);
    }

    public async Task<BaseCommandResponseOfGuid> RetireFileAsync(
        HalResourceOfStorageObjectDto file,
        CancellationToken cancellationToken = default)
    {
        if (file._links is not { } links || !links.TryGetValue("delete", out var link))
            return new BaseCommandResponseOfGuid { Success = false };

        var uri = StorageLink(link, "DELETE");
        var id = StorageTarget(uri, file.Id);
        try
        {
            return await files.DeleteStorageObjectAsync(id, HttpUtility.ParseQueryString(uri.Query)["api-version"],
                cancellationToken: cancellationToken);
        }
        catch (ApiException<ProblemDetails> exception) when (exception.StatusCode == 409)
        {
            exception.Result.AdditionalProperties.TryGetValue("code", out var value);
            var code = value switch
            {
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
                _ => null
            };
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                FailureCode = code is "storage_object_in_use" or "storage_object_retention_blocked"
                    or "storage_object_invalid_target" ? code : null
            };
        }
    }

    private static Uri StorageLink(HalLink link, string method)
    {
        if (!string.Equals(link.Method, method, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(link.Href))
            throw new InvalidOperationException("Invalid storage affordance.");
        return new Uri(new Uri("https://storage-link.invalid/"), link.Href);
    }

    private static Guid StorageTarget(Uri uri, Guid? expectedId)
    {
        const string prefix = "/api/storageobject/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(uri.AbsolutePath[prefix.Length..], out var id)
            || id != expectedId)
            throw new InvalidOperationException("Invalid storage target.");
        return id;
    }

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
