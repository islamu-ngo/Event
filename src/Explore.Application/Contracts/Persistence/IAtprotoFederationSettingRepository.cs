using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads dedicated nonsecret federation settings through the caller's selected persistence owner.
/// </summary>
public interface IAtprotoFederationSettingRepository
{
    Task<IReadOnlyList<AtprotoFederationInstanceSetting>> GetInstanceAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);

    /// <summary>Reads overrides only for the exact tenant already selected by the caller.</summary>
    Task<IReadOnlyList<AtprotoFederationTenantSetting>> GetForTenantAsync(
        Guid tenantId,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);
}
