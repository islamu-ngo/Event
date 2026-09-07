using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Infrastructure;

public interface IStoragePolicyResolver
{
    Task<ResolvedStoragePolicy> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    Task<ResolvedStoragePolicy> ResolveAsync(
        Guid? tenantId,
        StoragePolicyIntent request,
        CancellationToken cancellationToken = default);

    Task<IFileStorageProvider> ResolveProviderAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    Task<IFileStorageProvider> ResolveProviderAsync(
        Guid? tenantId,
        StoragePolicyIntent request,
        CancellationToken cancellationToken = default);
}
