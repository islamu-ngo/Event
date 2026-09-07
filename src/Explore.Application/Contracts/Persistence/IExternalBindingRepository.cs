using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IExternalBindingRepository : IGenericRepository<ExternalBinding, Guid>
{
    Task<ExternalBinding?> GetByExternalKeyAsync(
        string providerKey,
        string externalSystem,
        string externalType,
        string externalId,
        Guid? scopeTenantId,
        CancellationToken cancellationToken = default);

    Task<ExternalBinding?> GetByInternalReferenceAsync(
        string providerKey,
        string externalSystem,
        string internalType,
        Guid internalId,
        Guid? scopeTenantId,
        CancellationToken cancellationToken = default);
}
