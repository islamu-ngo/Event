using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventCustomPropertyRepository : IGenericRepository<EventCustomPropertyDefinition, Guid>
{
    Task<EventCustomPropertyDefinition?> GetDefinitionWithDetails(Guid id);
    Task<EventCustomPropertyDefinition?> GetTrackedDefinitionWithOptions(Guid id, CancellationToken cancellationToken);

    Task<(List<EventCustomPropertyDefinition> Items, int TotalCount)> GetDefinitionsForEventPaged(
        Guid eventId,
        int pageNumber,
        int pageSize);

    Task<List<EventCustomPropertyDefinition>> GetAllDefinitionsForEvent(Guid eventId);
    Task<List<EventCustomPropertyDefinition>> GetTrackedDefinitionsForEvent(Guid eventId, CancellationToken cancellationToken);

    Task<int> CountDefinitionsForEvent(Guid eventId, CancellationToken cancellationToken);

    Task<bool> ExistsDefinitionKey(Guid eventId, string namespaceValue, string key, Guid? excludeDefinitionId = null);

    Task<EventCustomPropertyDefinition> CreateWithOptions(
        EventCustomPropertyDefinition definition,
        IReadOnlyCollection<EventCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken);

    Task<EventCustomPropertyDefinition> UpdateWithOptions(
        EventCustomPropertyDefinition definition,
        IReadOnlyCollection<EventCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken);

    Task<bool> DeleteDefinition(Guid id, CancellationToken cancellationToken);
    Task<CustomPropertyPurgeDependencySummary?> GetPurgeDependencies(Guid id, CancellationToken cancellationToken);
    Task<bool> PurgeDefinition(Guid id, CancellationToken cancellationToken);

    Task<List<EventCustomPropertyValue>> GetValuesForEvent(Guid eventId);
    Task<List<EventCustomPropertyValue>> GetValuesForDefinition(Guid definitionId);
    Task<EventCustomPropertyValue> SetValue(EventCustomPropertyValue value, CancellationToken cancellationToken);
    Task<EventCustomPropertyOption> CreateOption(EventCustomPropertyOption option, CancellationToken cancellationToken);
    Task UpdateOption(EventCustomPropertyOption option, CancellationToken cancellationToken);

    Task SetMultiValues(
        Guid definitionId,
        Guid eventId,
        IReadOnlyCollection<EventCustomPropertyValue> values,
        CancellationToken cancellationToken);
}
