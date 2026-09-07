namespace Explore.Application.Features.TenantSettingsDocuments.Handlers.Queries;

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;
using Explore.Application.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using MediatR;

public sealed class GetTenantBrandingSettingsDocumentQueryHandler(
    ITenantContext tenantContext,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
    ITenantBrandingSettingsDocumentProvisioningService provisioningService,
    ITenantBrandingSettingsDocumentLockService lockService)
    : IRequestHandler<GetTenantBrandingSettingsDocumentQuery, TenantBrandingSettingsDocumentDto?>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantBrandingSettingsDocumentDto?> Handle(
        GetTenantBrandingSettingsDocumentQuery request,
        CancellationToken cancellationToken)
    {
        var documentKey = SettingsDocumentKeys.Tenant.Branding;
        var tenantId = tenantContext.TenantId;
        var context = new SettingsResolutionContext(
            tenantId,
            RequestedDocuments: [documentKey]);

        var lockState = await lockService.GetLockStateAsync(cancellationToken);
        var resolved = await typedSettingsDocumentResolver.ResolveTenantDocumentAsync<BrandingSettings>(
            context,
            documentKey,
            cancellationToken);

        if (resolved is not null)
        {
            return MapResolved(resolved, lockState);
        }

        var provisioned = await provisioningService.EnsureTenantBrandingDocumentAsync(tenantId, cancellationToken: cancellationToken);
        return MapProvisioned(provisioned, lockState);
    }

    private static TenantBrandingSettingsDocumentDto MapResolved(
        ResolvedSettingsDocument<BrandingSettings> resolved,
        TenantBrandingSettingsDocumentLockState lockState)
        => new()
        {
            DocumentKey = resolved.DocumentKey,
            SchemaVersion = resolved.SchemaVersion,
            DefaultsVersion = resolved.DefaultsVersion,
            Payload = MapPayload(resolved.Payload),
            Source = resolved.Source.ToString(),
            SourceScopeId = resolved.SourceScopeId,
            ConcurrencyStamp = resolved.ConcurrencyStamp,
            IsLockedByInstance = lockState.IsLockedByInstance,
            CanChangeDisplayName = lockState.CanChangeDisplayName,
            CanChangeLogoUrl = lockState.CanChangeLogoUrl,
            CanChangeFaviconUrl = lockState.CanChangeFaviconUrl,
            CanChangeCustomCssUrl = lockState.CanChangeCustomCssUrl,
            UpdatedAt = resolved.UpdatedAt
        };

    private static TenantBrandingSettingsDocumentDto MapProvisioned(
        TenantSettingsDocument document,
        TenantBrandingSettingsDocumentLockState lockState)
        => new()
        {
            DocumentKey = document.DocumentKey,
            SchemaVersion = document.SchemaVersion,
            DefaultsVersion = document.DefaultsVersion,
            Payload = MapPayload(DeserializePayload(document.PayloadJson)),
            Source = SettingsDocumentSource.Tenant.ToString(),
            SourceScopeId = document.TenantId,
            ConcurrencyStamp = document.ConcurrencyStamp,
            IsLockedByInstance = lockState.IsLockedByInstance,
            CanChangeDisplayName = lockState.CanChangeDisplayName,
            CanChangeLogoUrl = lockState.CanChangeLogoUrl,
            CanChangeFaviconUrl = lockState.CanChangeFaviconUrl,
            CanChangeCustomCssUrl = lockState.CanChangeCustomCssUrl,
            UpdatedAt = document.UpdatedAt
        };

    private static TenantBrandingSettingsPayloadDto MapPayload(BrandingSettings payload)
        => new()
        {
            DisplayName = payload.DisplayName,
            LogoUrl = payload.LogoUrl,
            FaviconUrl = payload.FaviconUrl,
            CustomCssUrl = payload.CustomCssUrl
        };

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
}
