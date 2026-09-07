using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.References;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class ExternalBindingRepository : GenericRepository<ExternalBinding, Guid>, IExternalBindingRepository
{
    private readonly ExploreDbContext _dbContext;

    public ExternalBindingRepository(ExploreDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task<ExternalBinding> Create(ExternalBinding entity)
    {
        EnsureRegisteredReference(entity);
        return await base.Create(entity);
    }

    public override async Task Update(ExternalBinding entity)
    {
        EnsureRegisteredReference(entity);
        await base.Update(entity);
    }

    public Task<ExternalBinding?> GetByExternalKeyAsync(
        string providerKey,
        string externalSystem,
        string externalType,
        string externalId,
        Guid? scopeTenantId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.ExternalBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                binding => binding.ProviderKey == providerKey
                    && binding.ExternalSystem == externalSystem
                    && binding.ExternalType == externalType
                    && binding.ExternalId == externalId
                    && binding.ScopeTenantId == scopeTenantId,
                cancellationToken);
    }

    public Task<ExternalBinding?> GetByInternalReferenceAsync(
        string providerKey,
        string externalSystem,
        string internalType,
        Guid internalId,
        Guid? scopeTenantId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.ExternalBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                binding => binding.ProviderKey == providerKey
                    && binding.ExternalSystem == externalSystem
                    && binding.InternalType == internalType
                    && binding.InternalId == internalId
                    && binding.ScopeTenantId == scopeTenantId,
                cancellationToken);
    }

    private static void EnsureRegisteredReference(ExternalBinding binding)
    {
        var errors = ReferenceTypeRegistry.ValidateExternalBinding(binding);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', errors));
        }
    }
}
