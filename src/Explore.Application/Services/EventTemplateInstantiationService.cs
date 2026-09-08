using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public class EventTemplateInstantiationService : IEventTemplateInstantiationService
{
    public InstantiationResult InstantiateFromTemplate(
        Guid eventId,
        Guid tenantId,
        EventTemplate template,
        string userId)
    {
        var now = DateTime.UtcNow;
        var instantiatedAt = DateTimeOffset.UtcNow;
        var definitions = new List<RuntimeDefinitionWithOptions>();

        foreach (var templateDef in template.Definitions)
        {
            var defId = Guid.CreateVersion7();
            var optionIdMap = new Dictionary<Guid, Guid>();

            var options = new List<EventCustomPropertyOption>();
            foreach (var templateOpt in templateDef.Options)
            {
                var newOptId = Guid.CreateVersion7();
                optionIdMap[templateOpt.Id] = newOptId;

                options.Add(new EventCustomPropertyOption
                {
                    Id = newOptId,
                    EventCustomPropertyDefinitionId = defId,
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
                    SourceTemplateVersion = template.Version,
                    CreatedAt = now,
                    CreatedBy = Guid.TryParse(userId, out var createdById) ? createdById : null,
                    UpdatedBy = Guid.TryParse(userId, out var updatedById) ? updatedById : null
                });
            }

            Guid? defaultOptionId = templateDef.DefaultOptionId.HasValue
                && optionIdMap.TryGetValue(templateDef.DefaultOptionId.Value, out var mappedDefault)
                    ? mappedDefault
                    : null;

            var definition = new EventCustomPropertyDefinition
            {
                Id = defId,
                EventId = eventId,
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
                SourceTemplateId = template.Id,
                SourceTemplateKey = template.TemplateKey,
                SourceTemplateVersion = template.Version,
                SourceTemplateDefinitionId = templateDef.Id,
                InstantiatedAt = instantiatedAt,
                CreatedAt = now,
                CreatedBy = Guid.TryParse(userId, out var defCreatedById) ? defCreatedById : null,
                UpdatedBy = Guid.TryParse(userId, out var defUpdatedById) ? defUpdatedById : null
            };

            EventCustomPropertyValue? defaultValue = CreateDefaultValue(
                definition, eventId, tenantId, defaultOptionId, userId, now);

            definitions.Add(new RuntimeDefinitionWithOptions(definition, options, defaultOptionId, defaultValue));
        }

        return new InstantiationResult(definitions);
    }

    public IReadOnlyList<ProvenanceMatch> MatchByProvenance(
        IReadOnlyCollection<EventCustomPropertyDefinition> existingDefinitions,
        IReadOnlyCollection<EventTemplateCustomPropertyDefinition> templateDefinitions)
    {
        var matches = new List<ProvenanceMatch>();
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
                matches.Add(new ProvenanceMatch(existing, templateDef, ProvenanceMatchType.SourceId));
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
                matches.Add(new ProvenanceMatch(existing, templateDef, ProvenanceMatchType.NamespaceKey));
                matchedExistingIds.Add(existing.Id);
                matchedTemplateIds.Add(templateDef.Id);
            }
        }

        return matches;
    }

    private static EventCustomPropertyValue? CreateDefaultValue(
        EventCustomPropertyDefinition definition,
        Guid eventId,
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

        return new EventCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = definition.Id,
            EventId = eventId,
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
