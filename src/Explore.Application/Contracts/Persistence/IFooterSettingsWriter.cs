using Explore.Application.Features.Footer.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Footer-only persistence requiring the actual producer's original active admission.</summary>
public interface IFooterSettingsWriter
{
    Task WriteGovernanceAsync(UpdateFooterGovernanceSettingsCommand original, CancellationToken cancellationToken);
    Task WriteTenantAsync(PatchTenantFooterSettingsCommand original,
        IReadOnlyList<TenantSettingOverrideUpsert> writes, CancellationToken cancellationToken);
}
