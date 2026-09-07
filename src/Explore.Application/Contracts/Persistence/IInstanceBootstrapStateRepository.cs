using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IInstanceBootstrapStateRepository : IGenericRepository<InstanceBootstrapState, Guid>
{
    Task<InstanceBootstrapState?> GetCurrent(CancellationToken cancellationToken = default);
    Task<InstanceBootstrapState?> GetCurrentForUpdate(CancellationToken cancellationToken = default);
}
