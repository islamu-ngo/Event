namespace Explore.Domain.Settings.Documents;

using System.Text.Json;
using Explore.Domain.Settings.Documents.Payloads;

public static class TenantBrandingSettingsDocumentDefaults
{
    public const int SchemaVersion = 1;
    public const string DefaultsVersion = "2026-05-14";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static TenantSettingsDocument Create(Guid tenantId, string? displayName = null)
    {
        var payload = new BrandingSettings
        {
            DisplayName = Normalize(displayName),
            LogoUrl = null,
            FaviconUrl = null,
            CustomCssUrl = null
        };

        return TenantSettingsDocument.Create(
            tenantId,
            SettingsDocumentKeys.Tenant.Branding,
            SchemaVersion,
            DefaultsVersion,
            JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
