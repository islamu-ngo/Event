using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionTemplateRepository : IGenericRepository<EventSessionTemplate, Guid>
{
    Task<EventSessionTemplate?> GetSessionTemplateWithDetails(Guid id);
    Task<EventSessionTemplate?> GetTrackedSessionTemplateWithDefinitions(Guid id, CancellationToken cancellationToken);

    Task<(List<EventSessionTemplate> Items, int TotalCount)> GetSessionTemplatesPaged(
        Guid eventTemplateId,
        int pageNumber,
        int pageSize);

    Task<bool> ExistsSessionTemplateKey(Guid eventTemplateId, string sessionTemplateKey, int version, Guid? excludeSessionTemplateId = null);
    Task<EventSessionTemplate?> GetLatestPublishedSessionTemplate(Guid eventTemplateId, string sessionTemplateKey);
    Task<EventSessionTemplate?> GetPublishedSessionTemplateVersion(Guid eventTemplateId, string sessionTemplateKey, int version, CancellationToken cancellationToken);

    Task<EventSessionTemplate> CreateWithDefinitions(
        EventSessionTemplate sessionTemplate,
        IReadOnlyCollection<SessionTemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken);

    Task<EventSessionTemplate> UpdateWithDefinitions(
        EventSessionTemplate sessionTemplate,
        IReadOnlyCollection<SessionTemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken);

    Task<bool> DeleteSessionTemplate(Guid id, CancellationToken cancellationToken);
}

public sealed record SessionTemplateDefinitionWithOptions(
    EventSessionTemplateCustomPropertyDefinition Definition,
    IReadOnlyCollection<EventSessionTemplateCustomPropertyOption> Options,
    Guid? DefaultOptionId);
