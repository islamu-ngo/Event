using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public class EventSessionTemplateInstantiationService : IEventSessionTemplateInstantiationService
{
    public SessionInstantiationResult InstantiateFromSessionTemplate(
        Guid eventSessionId,
        Guid tenantId,
        EventSessionTemplate sessionTemplate,
        string userId)
    {
        var now = DateTime.UtcNow;
        var instantiatedAt = DateTimeOffset.UtcNow;
        var definitions = new List<SessionRuntimeDefinitionWithOptions>();

        foreach (var templateDef in sessionTemplate.Definitions)
        {
            var defId = Guid.CreateVersion7();
            var optionIdMap = new Dictionary<Guid, Guid>();

            var options = new List<EventSessionCustomPropertyOption>();
            foreach (var templateOpt in templateDef.Options)
            {
                var newOptId = Guid.CreateVersion7();
                optionIdMap[templateOpt.Id] = newOptId;

                options.Add(new EventSessionCustomPropertyOption
                {
                    Id = newOptId,
                    EventSessionCustomPropertyDefinitionId = defId,
                    Namespace = CustomPropertyIdentity.NormalizeNamespace(templateOpt.Namespace),
                    Key = CustomPropertyIdentity.NormalizeKey(templateOpt.Key),
                    DisplayName = templateOpt.DisplayName,
                    Description = templateOpt.Description,
                    Value = templateOpt.Value,
                    IsDefault = templateOpt.IsDefault,
                    IsActive = templateOpt.IsActive,
                    SortOrder = templateOpt.SortOrder,
                    ParentOptionId = templateOpt.ParentOptionId.HasValue && optionIdMap.TryGetValue(templateOpt.ParentOptionId.Value, out var mappedParent)
                        ? mappedParent
                        : null,
                    SourceTemplateOptionId = templateOpt.Id,
                    SourceTemplateVersion = sessionTemplate.Version,
                    CreatedAt = now,
                    CreatedBy = Guid.TryParse(userId, out var createdById) ? createdById : null,
                    UpdatedBy = Guid.TryParse(userId, out var updatedById) ? updatedById : null
                });
            }

            Guid? defaultOptionId = templateDef.DefaultOptionId.HasValue
                && optionIdMap.TryGetValue(templateDef.DefaultOptionId.Value, out var mappedDefault)
                    ? mappedDefault
                    : null;

            var definition = new EventSessionCustomPropertyDefinition
            {
                Id = defId,
                EventSessionId = eventSessionId,
                TenantId = tenantId,
                Namespace = CustomPropertyIdentity.NormalizeNamespace(templateDef.Namespace),
                Key = CustomPropertyIdentity.NormalizeKey(templateDef.Key),
                DisplayName = templateDef.DisplayName,
                Description = templateDef.Description,
                PropertyType = templateDef.PropertyType,
                IsRequired = templateDef.IsRequired,
                IsMulti = templateDef.IsMulti,
                IsActive = templateDef.IsActive,
                SortOrder = templateDef.SortOrder,
                ExposureLevel = templateDef.ExposureLevel,
                IsSearchable = templateDef.IsSearchable,
                IsFilterable = templateDef.IsFilterable,
                IsExportable = templateDef.IsExportable,
                IsModerationRelevant = templateDef.IsModerationRelevant,
                IsAnalyticsRelevant = templateDef.IsAnalyticsRelevant,
                IsSystemOwned = templateDef.IsSystemOwned,
                DefaultTextValue = templateDef.DefaultTextValue,
                DefaultNumberValue = templateDef.DefaultNumberValue,
                DefaultBooleanValue = templateDef.DefaultBooleanValue,
                DefaultDateTimeValue = templateDef.DefaultDateTimeValue,
                DefaultOptionId = defaultOptionId,
                MinLength = templateDef.MinLength,
                MaxLength = templateDef.MaxLength,
                RegexPattern = templateDef.RegexPattern,
                MinNumber = templateDef.MinNumber,
                MaxNumber = templateDef.MaxNumber,
                MinDateTime = templateDef.MinDateTime,
                MaxDateTime = templateDef.MaxDateTime,
                AllowedUrlSchemes = templateDef.AllowedUrlSchemes,
                SourceTemplateId = sessionTemplate.Id,
                SourceTemplateKey = sessionTemplate.SessionTemplateKey,
                SourceTemplateVersion = sessionTemplate.Version,
                SourceTemplateDefinitionId = templateDef.Id,
                InstantiatedAt = instantiatedAt,
                CreatedAt = now,
                CreatedBy = Guid.TryParse(userId, out var defCreatedById) ? defCreatedById : null,
                UpdatedBy = Guid.TryParse(userId, out var defUpdatedById) ? defUpdatedById : null
            };

            EventSessionCustomPropertyValue? defaultValue = CreateDefaultValue(
                definition, eventSessionId, tenantId, defaultOptionId, userId, now);

            definitions.Add(new SessionRuntimeDefinitionWithOptions(definition, options, defaultOptionId, defaultValue));
        }

        return new SessionInstantiationResult(definitions);
    }

    public IReadOnlyList<SessionProvenanceMatch> MatchByProvenance(
        IReadOnlyCollection<EventSessionCustomPropertyDefinition> existingDefinitions,
        IReadOnlyCollection<EventSessionTemplateCustomPropertyDefinition> templateDefinitions)
    {
        var matches = new List<SessionProvenanceMatch>();
        var matchedExistingIds = new HashSet<Guid>();
        var matchedTemplateIds = new HashSet<Guid>();

        foreach (var existing in existingDefinitions)
        {
            if (!existing.SourceTemplateDefinitionId.HasValue)
                continue;

            var templateDef = templateDefinitions
                .FirstOrDefault(t => t.Id == existing.SourceTemplateDefinitionId.Value);

            if (templateDef != null && !matchedTemplateIds.Contains(templateDef.Id))
            {
                matches.Add(new SessionProvenanceMatch(existing, templateDef, ProvenanceMatchType.SourceId));
                matchedExistingIds.Add(existing.Id);
                matchedTemplateIds.Add(templateDef.Id);
            }
        }

        foreach (var existing in existingDefinitions)
        {
            if (matchedExistingIds.Contains(existing.Id))
                continue;

            var normalizedNs = CustomPropertyIdentity.NormalizeNamespace(existing.Namespace);
            var normalizedKey = CustomPropertyIdentity.NormalizeKey(existing.Key);

            var templateDef = templateDefinitions
                .FirstOrDefault(t => !matchedTemplateIds.Contains(t.Id)
                    && CustomPropertyIdentity.NormalizeNamespace(t.Namespace) == normalizedNs
                    && CustomPropertyIdentity.NormalizeKey(t.Key) == normalizedKey);

            if (templateDef != null)
            {
                matches.Add(new SessionProvenanceMatch(existing, templateDef, ProvenanceMatchType.NamespaceKey));
                matchedExistingIds.Add(existing.Id);
                matchedTemplateIds.Add(templateDef.Id);
            }
        }

        return matches;
    }

    private static EventSessionCustomPropertyValue? CreateDefaultValue(
        EventSessionCustomPropertyDefinition definition,
        Guid eventSessionId,
        Guid tenantId,
        Guid? defaultOptionId,
        string userId,
        DateTime now)
    {
        bool hasDefault = definition.DefaultTextValue != null
            || definition.DefaultNumberValue.HasValue
            || definition.DefaultBooleanValue.HasValue
            || definition.DefaultDateTimeValue.HasValue
            || defaultOptionId.HasValue;

        if (!hasDefault)
            return null;

        return new EventSessionCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            EventSessionCustomPropertyDefinitionId = definition.Id,
            EventSessionId = eventSessionId,
            TenantId = tenantId,
            Ordinal = 0,
            TextValue = definition.DefaultTextValue,
            NumberValue = definition.DefaultNumberValue,
            BooleanValue = definition.DefaultBooleanValue,
            DateTimeValue = definition.DefaultDateTimeValue,
            OptionId = defaultOptionId,
            CreatedAt = now,
            CreatedBy = Guid.TryParse(userId, out var valCreatedById) ? valCreatedById : null,
            UpdatedBy = Guid.TryParse(userId, out var valUpdatedById) ? valUpdatedById : null
        };
    }
}
