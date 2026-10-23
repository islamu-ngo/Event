using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionCustomPropertyRepository : IGenericRepository<EventSessionCustomPropertyDefinition, Guid>
{
    /// <summary>Holds the visible session and event in the caller's exact-tenant Serializable definition transaction.</summary>
    Task<EventSession?> GetParentForMutationAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<EventSessionCustomPropertyDefinition?> GetDefinitionWithDetails(Guid id, CancellationToken cancellationToken = default);
    Task<EventSessionCustomPropertyDefinition?> GetTrackedDefinitionWithOptions(Guid id, CancellationToken cancellationToken);

    Task<(List<EventSessionCustomPropertyDefinition> Items, int TotalCount)> GetDefinitionsForSessionPaged(
        Guid eventSessionId,
        int pageNumber,
        int pageSize);

    Task<List<EventSessionCustomPropertyDefinition>> GetAllDefinitionsForSession(Guid eventSessionId);
    Task<List<EventSessionCustomPropertyDefinition>> GetTrackedDefinitionsForSession(Guid eventSessionId, CancellationToken cancellationToken);

    Task<int> CountDefinitionsForSession(Guid eventSessionId, CancellationToken cancellationToken);

    Task<bool> ExistsDefinitionKey(Guid eventSessionId, string namespaceValue, string key, Guid? excludeDefinitionId = null);

    Task<EventSessionCustomPropertyDefinition> CreateWithOptions(
        EventSessionCustomPropertyDefinition definition,
        IReadOnlyCollection<EventSessionCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken);

    Task<EventSessionCustomPropertyDefinition> UpdateWithOptions(
        EventSessionCustomPropertyDefinition definition,
        IReadOnlyCollection<EventSessionCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken);

    Task<bool> DeleteDefinition(Guid id, CancellationToken cancellationToken);
    Task<bool> FenceDefinitionForPurgeAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomPropertyPurgeDependencySummary?> GetPurgeDependencies(Guid id, CancellationToken cancellationToken);
    Task<bool> PurgeDefinition(Guid id, CancellationToken cancellationToken);

    Task<List<EventSessionCustomPropertyValue>> GetValuesForSession(Guid eventSessionId);
    Task<List<EventSessionCustomPropertyValue>> GetValuesForDefinition(Guid definitionId);
    Task<EventSessionCustomPropertyValue> SetValue(EventSessionCustomPropertyValue value, CancellationToken cancellationToken);
    Task<EventSessionCustomPropertyOption> CreateOption(EventSessionCustomPropertyOption option, CancellationToken cancellationToken);
    Task UpdateOption(EventSessionCustomPropertyOption option, CancellationToken cancellationToken);

    Task SetMultiValues(
        Guid definitionId,
        Guid eventSessionId,
        IReadOnlyCollection<EventSessionCustomPropertyValue> values,
        CancellationToken cancellationToken);
}
