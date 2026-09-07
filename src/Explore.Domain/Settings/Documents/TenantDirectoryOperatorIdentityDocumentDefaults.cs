namespace Explore.Domain.Settings.Documents;

using System.Text.Json;
using Explore.Domain.Settings.Documents.Payloads;

public static class TenantDirectoryOperatorIdentityDocumentDefaults
{
    public const int SchemaVersion = 1;
    public const string DefaultsVersion = "2026-08-28";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static TenantSettingsDocument Create(Guid tenantId, string? publicName = null)
    {
        var payload = new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = Normalize(publicName)
        };

        return Create(tenantId, payload);
    }

    public static TenantSettingsDocument Create(
        Guid tenantId,
        TenantDirectoryOperatorIdentitySettings payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return TenantSettingsDocument.Create(
            tenantId,
            SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
            SchemaVersion,
            DefaultsVersion,
            JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
