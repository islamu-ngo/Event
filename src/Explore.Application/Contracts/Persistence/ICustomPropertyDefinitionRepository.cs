using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface ICustomPropertyDefinitionRepository : IGenericRepository<CustomPropertyDefinition, Guid>
{
    Task<CustomPropertyDefinition?> GetDefinitionWithDetails(Guid id);
    Task<CustomPropertyDefinition?> GetTrackedDefinitionWithOptions(Guid id, CancellationToken cancellationToken);
    Task<(List<CustomPropertyDefinition> Items, int TotalCount)> GetDefinitionsWithDetailsPaged(
        EntityTypeName entityTypeName,
        int pageNumber,
        int pageSize);
    Task<int> CountDefinitionsForScope(Guid tenantId, EntityTypeName entityTypeName, CancellationToken cancellationToken);
    Task<bool> ExistsScopedMachineKey(Guid tenantId, EntityTypeName entityTypeName, string namespaceValue, string key, Guid? excludeDefinitionId = null);
    Task<CustomPropertyDefinition> CreateWithOptions(
        CustomPropertyDefinition definition,
        IReadOnlyCollection<CustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken);
    Task<CustomPropertyDefinition> UpdateWithOptions(
        CustomPropertyDefinition definition,
        IReadOnlyCollection<CustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken);
    Task<bool> DeleteDefinition(Guid id, CancellationToken cancellationToken);
    Task<CustomPropertyPurgeDependencySummary?> GetPurgeDependencies(Guid id, CancellationToken cancellationToken);
    Task<bool> PurgeDefinition(Guid id, CancellationToken cancellationToken);
}
