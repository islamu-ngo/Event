using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Prepares only the original TenantSetting/update question for tenant.branding, outside
/// governance locks and transactions. This is not the Tenant/update administration action.
/// </summary>
public interface IHumanTenantBrandingMutationPreparation
{
    Task<IPreparedHumanTenantBrandingMutation> PrepareAsync(
        PatchTenantBrandingSettingsDocumentCommand command, CancellationToken cancellationToken = default);
}

public interface IPreparedHumanTenantBrandingMutation
{
    /// <summary>Consumes the exact original command once on its selected physical owner.</summary>
    Task AcquireAsync(PatchTenantBrandingSettingsDocumentCommand command,
        CancellationToken cancellationToken = default);
}
