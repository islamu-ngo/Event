using Explore.Domain;

namespace Explore.Application.Contracts.Services;

public interface IEventSessionTemplateInstantiationService
{
    SessionInstantiationResult InstantiateFromSessionTemplate(
        Guid eventSessionId,
        Guid tenantId,
        EventSessionTemplate sessionTemplate,
        string userId);

    IReadOnlyList<SessionProvenanceMatch> MatchByProvenance(
        IReadOnlyCollection<EventSessionCustomPropertyDefinition> existingDefinitions,
        IReadOnlyCollection<EventSessionTemplateCustomPropertyDefinition> templateDefinitions);
}

public sealed record SessionInstantiationResult(
    IReadOnlyList<SessionRuntimeDefinitionWithOptions> Definitions);

public sealed record SessionRuntimeDefinitionWithOptions(
    EventSessionCustomPropertyDefinition Definition,
    IReadOnlyList<EventSessionCustomPropertyOption> Options,
    Guid? DefaultOptionId,
    EventSessionCustomPropertyValue? DefaultValue);

public sealed record SessionProvenanceMatch(
    EventSessionCustomPropertyDefinition ExistingDefinition,
    EventSessionTemplateCustomPropertyDefinition TemplateDefinition,
    ProvenanceMatchType MatchType);
