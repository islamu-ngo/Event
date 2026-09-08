namespace Explore.Application.Services;

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;

public sealed class TenantBrandingSettingsDocumentProvisioningService(
    ITenantRepository tenantRepository,
    ITenantSettingsDocumentRepository tenantSettingsDocumentRepository,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver)
    : ITenantBrandingSettingsDocumentProvisioningService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantSettingsDocument> EnsureTenantBrandingDocumentAsync(
        Guid tenantId,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await tenantSettingsDocumentRepository.GetTrackedByTenantAndDocumentKey(
            tenantId,
            SettingsDocumentKeys.Tenant.Branding,
            cancellationToken);

        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                var currentPayload = DeserializePayload(existing.PayloadJson);
                var normalizedDisplayName = Normalize(displayName);

                if (!string.Equals(currentPayload.DisplayName, normalizedDisplayName, StringComparison.Ordinal))
                {
                    var updatedPayload = currentPayload with { DisplayName = normalizedDisplayName };
                    existing.UpdatePayload(
                        existing.SchemaVersion,
                        existing.DefaultsVersion,
                        JsonSerializer.Serialize(updatedPayload, SerializerOptions));
                    await tenantSettingsDocumentRepository.Update(existing);
                    typedSettingsDocumentResolver.InvalidateTenantDocumentCache(tenantId, SettingsDocumentKeys.Tenant.Branding);
                }
            }

            return existing;
        }

        var fallbackDisplayName = displayName;
        if (string.IsNullOrWhiteSpace(fallbackDisplayName))
        {
            var tenant = await tenantRepository.GetById(tenantId);
            fallbackDisplayName = tenant?.FullName;
        }

        var document = TenantBrandingSettingsDocumentDefaults.Create(tenantId, fallbackDisplayName);
        var created = await tenantSettingsDocumentRepository.Create(document);
        typedSettingsDocumentResolver.InvalidateTenantDocumentCache(tenantId, SettingsDocumentKeys.Tenant.Branding);
        return created;
    }

    private static BrandingSettings DeserializePayload(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BrandingSettings>(payloadJson, SerializerOptions) ?? new BrandingSettings();
        }
        catch (JsonException)
        {
            return new BrandingSettings();
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
