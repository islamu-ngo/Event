using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads dedicated nonsecret federation policy through independently owned Global read scopes.
/// </summary>
public interface IAtprotoFederationGlobalReadRepository
{
    Task<IReadOnlyList<AtprotoFederationInstanceSetting>> GetInstanceAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);

    /// <summary>Observes the requested keys across all tenant states in one override query.</summary>
    Task<IReadOnlyList<AtprotoFederationTenantSetting>> GetOverridesAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);
}
