namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoSessionRefreshLock
{
    Task<IAsyncDisposable> AcquireAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default);
}
