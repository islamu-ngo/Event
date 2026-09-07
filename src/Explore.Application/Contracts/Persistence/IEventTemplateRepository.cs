using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventTemplateRepository : IGenericRepository<EventTemplate, Guid>
{
    Task<EventTemplate?> GetTemplateWithDetails(Guid id);
    Task<EventTemplate?> GetTrackedTemplateWithDefinitions(Guid id, CancellationToken cancellationToken);

    Task<(List<EventTemplate> Items, int TotalCount)> GetTemplatesPaged(
        Guid tenantId,
        int? eventTypeId,
        int pageNumber,
        int pageSize);

    Task<bool> ExistsTemplateKey(Guid tenantId, string templateKey, int version, Guid? excludeTemplateId = null);
    Task<EventTemplate?> GetLatestPublishedTemplate(Guid tenantId, string templateKey);
    Task<EventTemplate?> GetPublishedTemplateVersion(Guid tenantId, string templateKey, int version, CancellationToken cancellationToken);

    Task<EventTemplate> CreateWithDefinitions(
        EventTemplate template,
        IReadOnlyCollection<TemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken);

    Task<EventTemplate> UpdateWithDefinitions(
        EventTemplate template,
        IReadOnlyCollection<TemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken);

    Task<bool> DeleteTemplate(Guid id, CancellationToken cancellationToken);
}

public sealed record TemplateDefinitionWithOptions(
    EventTemplateCustomPropertyDefinition Definition,
    IReadOnlyCollection<EventTemplateCustomPropertyOption> Options,
    Guid? DefaultOptionId);
