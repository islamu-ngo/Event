namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents;

internal static class ConfigurationManifestExportBrandingPolicy
{
    public static void EnsureSafeForExport(ConfigurationManifestV1Alpha2 manifest)
    {
        EnsureSafeSettings(manifest.Spec.Instance.Settings);
        foreach (ConfigurationManifestTenantV1Alpha2 tenant in manifest.Spec.Tenants)
        {
            EnsureSafeSettings(tenant.Spec.Settings);
            if (tenant.Spec.Documents.TryGetValue(
                    SettingsDocumentKeys.Tenant.Branding,
                    out ConfigurationManifestDocumentV1Alpha2? document)
                && document is not null)
            {
                EnsureSafeForExport(document.Payload);
            }
        }
    }

    public static void EnsureSafeForExport(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return;

        EnsureSafeProperty(payload, "logoUrl");
        EnsureSafeProperty(payload, "faviconUrl");
        EnsureSafeProperty(payload, "customCssUrl");
    }

    private static void EnsureSafeSettings(IReadOnlyDictionary<string, JsonElement> settings)
    {
        EnsureSafeSetting(settings, BrandingSettingDefinitions.LogoUrl.Key);
        EnsureSafeSetting(settings, BrandingSettingDefinitions.FaviconUrl.Key);
        EnsureSafeSetting(settings, BrandingSettingDefinitions.CustomCssUrl.Key);
    }

    private static void EnsureSafeSetting(
        IReadOnlyDictionary<string, JsonElement> settings,
        string key)
    {
        if (!settings.TryGetValue(key, out JsonElement value))
            return;

        if (value.ValueKind != JsonValueKind.String)
            throw UnsafeBrandingUrl();

        EnsureSafeUrl(value.GetString());
    }

    private static void EnsureSafeProperty(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (property.ValueKind != JsonValueKind.String)
            throw UnsafeBrandingUrl();

        EnsureSafeUrl(property.GetString());
    }

    private static void EnsureSafeUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (value.Length > 2048
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw UnsafeBrandingUrl();
        }
    }

    private static InvalidOperationException UnsafeBrandingUrl() =>
        new("Current branding configuration contains a URL that is unsafe to export.");
}
