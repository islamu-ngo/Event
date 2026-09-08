using System.Net;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public interface ITenantBrandingSettingsAdminService
{
    Task<TenantBrandingSettingsAdminModel> GetAsync(CancellationToken cancellationToken = default);
    Task<TenantBrandingSettingsSaveResult> PatchDisplayNameAsync(TenantBrandingSettingsAdminModel model, CancellationToken cancellationToken = default);
    Task<TenantBrandingSettingsSaveResult> PatchLogoUrlAsync(TenantBrandingSettingsAdminModel model, CancellationToken cancellationToken = default);
    Task<TenantBrandingSettingsSaveResult> PatchFaviconUrlAsync(TenantBrandingSettingsAdminModel model, CancellationToken cancellationToken = default);
    Task<TenantBrandingSettingsSaveResult> PatchCustomCssUrlAsync(TenantBrandingSettingsAdminModel model, CancellationToken cancellationToken = default);
}

public sealed class TenantBrandingSettingsAdminService(
    ITenantSettingsDocumentsClient api,
    ILogger<TenantBrandingSettingsAdminService> logger) : ITenantBrandingSettingsAdminService
{
    private const string EditLinkRel = "edit";

    public async Task<TenantBrandingSettingsAdminModel> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await api.GetTenantBrandingSettingsDocumentAsync(cancellationToken: cancellationToken);
            return TenantBrandingSettingsAdminModel.FromDocument(
                document,
                document._links?.ContainsKey(EditLinkRel) == true);
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return TenantBrandingSettingsAdminModel.Missing();
        }
        catch (Exception ex) when (IsNotFoundException(ex))
        {
            return TenantBrandingSettingsAdminModel.Missing();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tenant branding settings document.");
            return TenantBrandingSettingsAdminModel.Failed("Unable to load tenant branding settings.");
        }
    }

    public Task<TenantBrandingSettingsSaveResult> PatchDisplayNameAsync(
        TenantBrandingSettingsAdminModel model,
        CancellationToken cancellationToken = default) =>
        PatchAsync(
            model,
            model.CanChangeDisplayName,
            "display name",
            new PatchTenantBrandingSettingsDocumentDto
            {
                ExpectedConcurrencyStamp = model.ConcurrencyStamp,
                DisplayName = new PatchTenantBrandingDisplayNameDto
                {
                    Value = StringUpdate(model.DisplayName)
                }
            },
            cancellationToken);

    public Task<TenantBrandingSettingsSaveResult> PatchLogoUrlAsync(
        TenantBrandingSettingsAdminModel model,
        CancellationToken cancellationToken = default) =>
        PatchAssetAsync(model, model.CanChangeLogoUrl, "logo URL", logoUrl: StringUpdate(model.LogoUrl), cancellationToken: cancellationToken);

    public Task<TenantBrandingSettingsSaveResult> PatchFaviconUrlAsync(
        TenantBrandingSettingsAdminModel model,
        CancellationToken cancellationToken = default) =>
        PatchAssetAsync(model, model.CanChangeFaviconUrl, "favicon URL", faviconUrl: StringUpdate(model.FaviconUrl), cancellationToken: cancellationToken);

    public Task<TenantBrandingSettingsSaveResult> PatchCustomCssUrlAsync(
        TenantBrandingSettingsAdminModel model,
        CancellationToken cancellationToken = default) =>
        PatchAssetAsync(model, model.CanChangeCustomCssUrl, "custom CSS URL", customCssUrl: StringUpdate(model.CustomCssUrl), cancellationToken: cancellationToken);

    private Task<TenantBrandingSettingsSaveResult> PatchAssetAsync(
        TenantBrandingSettingsAdminModel model,
        bool canChange,
        string fieldName,
        OptionalUpdateOfstring? logoUrl = null,
        OptionalUpdateOfstring? faviconUrl = null,
        OptionalUpdateOfstring? customCssUrl = null,
        CancellationToken cancellationToken = default) =>
        PatchAsync(
            model,
            canChange,
            fieldName,
            new PatchTenantBrandingSettingsDocumentDto
            {
                ExpectedConcurrencyStamp = model.ConcurrencyStamp,
                Assets = new PatchTenantBrandingAssetsDto
                {
                    LogoUrl = logoUrl,
                    FaviconUrl = faviconUrl,
                    CustomCssUrl = customCssUrl
                }
            },
            cancellationToken);

    private async Task<TenantBrandingSettingsSaveResult> PatchAsync(
        TenantBrandingSettingsAdminModel model,
        bool canChange,
        string fieldName,
        PatchTenantBrandingSettingsDocumentDto request,
        CancellationToken cancellationToken)
    {
        if (!model.Exists)
        {
            return TenantBrandingSettingsSaveResult.Failed("Tenant branding settings have not been initialized.");
        }

        if (!model.CanReplace)
        {
            return TenantBrandingSettingsSaveResult.Failed("The API did not expose a tenant branding edit affordance.");
        }

        if (!canChange)
        {
            return TenantBrandingSettingsSaveResult.Failed($"The tenant branding {fieldName} is locked.");
        }

        try
        {
            var document = await api.PatchTenantBrandingSettingsDocumentAsync(
                request,
                cancellationToken: cancellationToken);

            TenantBrandingSettingsAdminModel updated = TenantBrandingSettingsAdminModel.FromDocument(
                document,
                document._links?.ContainsKey(EditLinkRel) == true);

            return TenantBrandingSettingsSaveResult.Successful(updated);
        }
        catch (Exception ex) when (IsStatusException(ex, HttpStatusCode.Conflict))
        {
            logger.LogWarning(ex, "Tenant branding settings PATCH encountered a concurrency conflict.");
            return TenantBrandingSettingsSaveResult.Conflict();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to patch tenant branding settings document.");
            return TenantBrandingSettingsSaveResult.Failed("Failed to save tenant branding settings.");
        }
    }


    private static bool IsNotFoundException(Exception exception) =>
        IsStatusException(exception, HttpStatusCode.NotFound);

    private static bool IsStatusException(Exception exception, HttpStatusCode status) =>
        exception is ApiException apiException && apiException.StatusCode == (int)status
        || exception.InnerException is not null && IsStatusException(exception.InnerException, status)
        || exception.Message.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OptionalUpdateOfstring StringUpdate(string? value) => new()
    {
        HasValue = true,
        Value = Normalize(value)
    };

}

public sealed class TenantBrandingSettingsAdminModel
{
    public bool Exists { get; set; }
    public bool CanReplace { get; set; }
    public bool CanChangeDisplayName { get; set; }
    public bool CanChangeLogoUrl { get; set; }
    public bool CanChangeFaviconUrl { get; set; }
    public bool CanChangeCustomCssUrl { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public string? DisplayName { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? CustomCssUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public static TenantBrandingSettingsAdminModel Missing() => new()
    {
        Exists = false,
        ErrorMessage = "Tenant branding settings have not been initialized."
    };

    public static TenantBrandingSettingsAdminModel Failed(string message) => new()
    {
        Exists = false,
        ErrorMessage = message
    };

    public static TenantBrandingSettingsAdminModel FromDocument(
        HalResourceOfTenantBrandingSettingsDocumentDto document,
        bool canReplace) => new()
        {
            Exists = true,
            CanReplace = canReplace,
            CanChangeDisplayName = document.CanChangeDisplayName == true,
            CanChangeLogoUrl = document.CanChangeLogoUrl == true,
            CanChangeFaviconUrl = document.CanChangeFaviconUrl == true,
            CanChangeCustomCssUrl = document.CanChangeCustomCssUrl == true,
            ConcurrencyStamp = document.ConcurrencyStamp,
            DisplayName = document.Payload?.DisplayName,
            LogoUrl = document.Payload?.LogoUrl,
            FaviconUrl = document.Payload?.FaviconUrl,
            CustomCssUrl = document.Payload?.CustomCssUrl
        };
}

public sealed record TenantBrandingSettingsSaveResult
{
    public bool Success { get; init; }
    public bool IsConcurrencyConflict { get; init; }
    public string Message { get; init; } = string.Empty;
    public TenantBrandingSettingsAdminModel? Model { get; init; }

    public static TenantBrandingSettingsSaveResult Successful(TenantBrandingSettingsAdminModel model) => new()
    {
        Success = true,
        Message = "Tenant branding settings saved.",
        Model = model
    };

    public static TenantBrandingSettingsSaveResult Failed(string message) => new()
    {
        Success = false,
        Message = message
    };

    public static TenantBrandingSettingsSaveResult Conflict() => new()
    {
        Success = false,
        IsConcurrencyConflict = true,
        Message = "Tenant branding settings changed elsewhere. The latest values were reloaded."
    };
}
