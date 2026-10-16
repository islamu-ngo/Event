using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads current committed bootstrap evidence without selecting a mutation owner.</summary>
public interface IInstanceBootstrapStateReader
{
    Task<InstanceBootstrapState?> GetCurrent(CancellationToken cancellationToken = default);
}
