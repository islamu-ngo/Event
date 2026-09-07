using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ISyncStateRepository : IGenericRepository<SyncState, int>
{
    Task<List<SyncState>> GetAllSyncStates();
    Task<SyncState?> GetSyncStateByService(string service);
    Task<bool> ExistsByService(string service);
}
