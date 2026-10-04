using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class StorageProviderBindingRepository(ExploreDbContext dbContext) : IStorageProviderBindingRepository
{
    public Task<StorageProviderBinding?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<StorageProviderBinding>().AsNoTracking()
            .SingleOrDefaultAsync(binding => binding.Id == id, cancellationToken);

    public async Task AddAsync(StorageProviderBinding binding, CancellationToken cancellationToken) =>
        await dbContext.Set<StorageProviderBinding>().AddAsync(binding, cancellationToken);

    public async Task<IReadOnlyList<StorageProviderBinding>> ListLocalAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<StorageProviderBinding>().AsNoTracking()
            .Where(binding => binding.Provider == StorageProviders.Local)
            .OrderBy(binding => binding.Id).ToArrayAsync(cancellationToken);
}
