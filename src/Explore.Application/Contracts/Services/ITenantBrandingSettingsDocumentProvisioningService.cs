namespace Explore.Application.Contracts.Services;

using Explore.Domain.Settings.Documents;

public interface ITenantBrandingSettingsDocumentProvisioningService
{
    Task<TenantSettingsDocument> EnsureTenantBrandingDocumentAsync(
        Guid tenantId,
        string? displayName = null,
        CancellationToken cancellationToken = default);
}
