using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public class EventSessionTemplateDiffService : IEventSessionTemplateDiffService
{
    private readonly IEventSessionTemplateRepository _eventSessionTemplateRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventSessionCustomPropertyRepository _eventSessionCustomPropertyRepository;

    public EventSessionTemplateDiffService(
        IEventSessionTemplateRepository eventSessionTemplateRepository,
        IEventSessionRepository eventSessionRepository,
        IEventSessionCustomPropertyRepository eventSessionCustomPropertyRepository)
    {
        _eventSessionTemplateRepository = eventSessionTemplateRepository;
        _eventSessionRepository = eventSessionRepository;
        _eventSessionCustomPropertyRepository = eventSessionCustomPropertyRepository;
    }

    public async Task<TemplateDiffDto> ComputeDiffAsync(
        Guid eventSessionId,
        int targetTemplateVersion,
        CancellationToken cancellationToken)
    {
        var eventSession = await _eventSessionRepository.GetById(eventSessionId)
            ?? throw new NotFoundException(nameof(EventSession), eventSessionId);

        var runtimeDefinitions = await _eventSessionCustomPropertyRepository.GetAllDefinitionsForSession(eventSessionId);
        var templateKey = ResolveTemplateKey(eventSession, runtimeDefinitions);

        if (string.IsNullOrWhiteSpace(templateKey))
        {
            return new TemplateDiffDto(
                targetTemplateVersion,
                eventSession.SourceTemplateVersion ?? 0,
                [], [], [], [], [], [], []);
        }

        if (!eventSession.SourceTemplateId.HasValue)
            throw new NotFoundException(nameof(EventSessionTemplate), templateKey);

        var targetTemplate = await _eventSessionTemplateRepository.GetPublishedSessionTemplateVersion(
            eventSession.SourceTemplateId.Value,
            templateKey,
            targetTemplateVersion,
            cancellationToken)
            ?? throw new NotFoundException(nameof(EventSessionTemplate), $"{templateKey}:{targetTemplateVersion}");

        return BuildDiff(runtimeDefinitions, targetTemplate, eventSession.SourceTemplateVersion ?? 0);
    }

    private static TemplateDiffDto BuildDiff(
        IReadOnlyList<EventSessionCustomPropertyDefinition> runtimeDefinitions,
        EventSessionTemplate targetTemplate,
        int baseProvenanceVersion)
    {
        var templateDefinitions = targetTemplate.Definitions.OrderBy(x => x.SortOrder).ToList();
        var matchedRuntimeDefinitionIds = new HashSet<Guid>();
        var matchedTemplateDefinitionIds = new HashSet<Guid>();
        var definitionPairs = new List<(EventSessionCustomPropertyDefinition Runtime, EventSessionTemplateCustomPropertyDefinition Template)>();

        foreach (var runtimeDefinition in runtimeDefinitions)
        {
            if (!runtimeDefinition.SourceTemplateDefinitionId.HasValue)
                continue;

            var templateDefinition = templateDefinitions.FirstOrDefault(x => x.Id == runtimeDefinition.SourceTemplateDefinitionId.Value);
            if (templateDefinition is null || matchedTemplateDefinitionIds.Contains(templateDefinition.Id))
                continue;

            matchedRuntimeDefinitionIds.Add(runtimeDefinition.Id);
            matchedTemplateDefinitionIds.Add(templateDefinition.Id);
            definitionPairs.Add((runtimeDefinition, templateDefinition));
        }

        foreach (var runtimeDefinition in runtimeDefinitions)
        {
            if (matchedRuntimeDefinitionIds.Contains(runtimeDefinition.Id))
                continue;

            var normalizedNamespace = NormalizeCompositePart(runtimeDefinition.Namespace);
            var normalizedKey = NormalizeCompositePart(runtimeDefinition.Key);
            var templateDefinition = templateDefinitions.FirstOrDefault(x =>
                !matchedTemplateDefinitionIds.Contains(x.Id)
                && NormalizeCompositePart(x.Namespace) == normalizedNamespace
                && NormalizeCompositePart(x.Key) == normalizedKey);

            if (templateDefinition is null)
                continue;

            matchedRuntimeDefinitionIds.Add(runtimeDefinition.Id);
            matchedTemplateDefinitionIds.Add(templateDefinition.Id);
            definitionPairs.Add((runtimeDefinition, templateDefinition));
        }

        var addedDefinitions = templateDefinitions
            .Where(x => !matchedTemplateDefinitionIds.Contains(x.Id))
            .Select(MapAddedDefinition)
            .ToList();

        var modifiedDefinitions = new List<ModifiedDefinitionDto>();
        var addedOptions = new List<AddedOptionDto>();
        var modifiedOptions = new List<ModifiedOptionDto>();
        var retiredOptions = new List<RetiredOptionDto>();

        foreach (var pair in definitionPairs)
        {
            var definitionFieldChanges = BuildDefinitionFieldChanges(pair.Runtime, pair.Template);
            if (definitionFieldChanges.Count > 0)
            {
                modifiedDefinitions.Add(new ModifiedDefinitionDto(
                    pair.Runtime.Namespace,
                    pair.Runtime.Key,
                    pair.Runtime.ConcurrencyStamp,
                    definitionFieldChanges));
            }

            BuildOptionDiff(pair.Runtime, pair.Template, addedOptions, modifiedOptions, retiredOptions);
        }

        var retiredDefinitions = runtimeDefinitions
            .Where(x => !matchedRuntimeDefinitionIds.Contains(x.Id))
            .Where(x => x.SourceTemplateDefinitionId.HasValue || x.SourceTemplateId.HasValue)
            .Select(x => new RetiredDefinitionDto(x.Namespace, x.Key, x.ConcurrencyStamp))
            .ToList();

        var untouchedLocalDefinitions = runtimeDefinitions
            .Where(x => !matchedRuntimeDefinitionIds.Contains(x.Id))
            .Where(x => !x.SourceTemplateDefinitionId.HasValue && !x.SourceTemplateId.HasValue)
            .Select(x => new UntouchedLocalDefinitionDto(
                x.Namespace,
                x.Key,
                x.IsActive ? "LocallyAdded" : "LocallyRetired"))
            .ToList();

        return new TemplateDiffDto(
            targetTemplate.Version,
            baseProvenanceVersion,
            addedDefinitions,
            modifiedDefinitions,
            retiredDefinitions,
            addedOptions,
            modifiedOptions,
            retiredOptions,
            untouchedLocalDefinitions);
    }

    private static void BuildOptionDiff(
        EventSessionCustomPropertyDefinition runtimeDefinition,
        EventSessionTemplateCustomPropertyDefinition templateDefinition,
        List<AddedOptionDto> addedOptions,
        List<ModifiedOptionDto> modifiedOptions,
        List<RetiredOptionDto> retiredOptions)
    {
        var runtimeOptions = runtimeDefinition.Options.OrderBy(x => x.SortOrder).ToList();
        var templateOptions = templateDefinition.Options.OrderBy(x => x.SortOrder).ToList();
        var matchedRuntimeOptionIds = new HashSet<Guid>();
        var matchedTemplateOptionIds = new HashSet<Guid>();
        var optionPairs = new List<(EventSessionCustomPropertyOption Runtime, EventSessionTemplateCustomPropertyOption Template)>();

        foreach (var runtimeOption in runtimeOptions)
        {
            if (!runtimeOption.SourceTemplateOptionId.HasValue)
                continue;

            var templateOption = templateOptions.FirstOrDefault(x => x.Id == runtimeOption.SourceTemplateOptionId.Value);
            if (templateOption is null || matchedTemplateOptionIds.Contains(templateOption.Id))
                continue;

            matchedRuntimeOptionIds.Add(runtimeOption.Id);
            matchedTemplateOptionIds.Add(templateOption.Id);
            optionPairs.Add((runtimeOption, templateOption));
        }

        foreach (var runtimeOption in runtimeOptions)
        {
            if (matchedRuntimeOptionIds.Contains(runtimeOption.Id))
                continue;

            var normalizedNamespace = NormalizeCompositePart(runtimeOption.Namespace);
            var normalizedKey = NormalizeCompositePart(runtimeOption.Key);
            var templateOption = templateOptions.FirstOrDefault(x =>
                !matchedTemplateOptionIds.Contains(x.Id)
                && NormalizeCompositePart(x.Namespace) == normalizedNamespace
                && NormalizeCompositePart(x.Key) == normalizedKey);

            if (templateOption is null)
                continue;

            matchedRuntimeOptionIds.Add(runtimeOption.Id);
            matchedTemplateOptionIds.Add(templateOption.Id);
            optionPairs.Add((runtimeOption, templateOption));
        }

        addedOptions.AddRange(templateOptions
            .Where(x => !matchedTemplateOptionIds.Contains(x.Id))
            .Select(MapAddedOption));

        foreach (var pair in optionPairs)
        {
            var fieldChanges = BuildOptionFieldChanges(pair.Runtime, pair.Template);
            if (fieldChanges.Count == 0)
                continue;

            modifiedOptions.Add(new ModifiedOptionDto(
                pair.Runtime.Namespace,
                pair.Runtime.Key,
                pair.Runtime.ConcurrencyStamp,
                fieldChanges));
        }

        retiredOptions.AddRange(runtimeOptions
            .Where(x => !matchedRuntimeOptionIds.Contains(x.Id))
            .Where(x => x.SourceTemplateOptionId.HasValue)
            .Select(x => new RetiredOptionDto(x.Namespace, x.Key, x.ConcurrencyStamp)));
    }

    private static AddedDefinitionDto MapAddedDefinition(EventSessionTemplateCustomPropertyDefinition definition)
        => new(
            definition.Namespace,
            definition.Key,
            definition.DisplayName,
            definition.Description,
            definition.PropertyType.ToString(),
            definition.IsRequired,
            definition.IsMulti,
            SerializeDefaultValue(
                definition.DefaultTextValue,
                definition.DefaultNumberValue,
                definition.DefaultBooleanValue,
                definition.DefaultDateTimeValue,
                definition.DefaultOption?.Value),
            definition.ExposureLevel.ToString(),
            definition.IsSearchable,
            definition.IsFilterable,
            definition.IsExportable,
            definition.IsModerationRelevant,
            definition.IsAnalyticsRelevant,
            definition.IsSystemOwned,
            definition.MinLength,
            definition.MaxLength,
            definition.RegexPattern,
            definition.MinNumber,
            definition.MaxNumber,
            definition.MinDateTime,
            definition.MaxDateTime,
            definition.AllowedUrlSchemes,
            definition.Options.OrderBy(x => x.SortOrder).Select(MapAddedOption).ToList());

    private static AddedOptionDto MapAddedOption(EventSessionTemplateCustomPropertyOption option)
        => new(
            option.Namespace,
            option.Key,
            option.DisplayName,
            option.Description,
            option.Value,
            option.IsDefault,
            option.IsActive,
            option.SortOrder,
            option.ParentOptionId?.ToString());

    private static List<FieldChangeDto> BuildDefinitionFieldChanges(
        EventSessionCustomPropertyDefinition runtimeDefinition,
        EventSessionTemplateCustomPropertyDefinition templateDefinition)
    {
        var changes = new List<FieldChangeDto>();

        if (!string.Equals(runtimeDefinition.DisplayName, templateDefinition.DisplayName, StringComparison.Ordinal))
            changes.Add(CreateChange("DisplayName", runtimeDefinition.DisplayName, templateDefinition.DisplayName, "string"));
        if (!string.Equals(runtimeDefinition.Description, templateDefinition.Description, StringComparison.Ordinal))
            changes.Add(CreateChange("Description", runtimeDefinition.Description, templateDefinition.Description, "string"));
        if (runtimeDefinition.PropertyType != templateDefinition.PropertyType)
            changes.Add(CreateChange("PropertyType", runtimeDefinition.PropertyType.ToString(), templateDefinition.PropertyType.ToString(), "enum"));
        if (runtimeDefinition.IsRequired != templateDefinition.IsRequired)
            changes.Add(CreateChange("IsRequired", runtimeDefinition.IsRequired.ToString(), templateDefinition.IsRequired.ToString(), "bool"));
        if (runtimeDefinition.IsMulti != templateDefinition.IsMulti)
            changes.Add(CreateChange("IsMulti", runtimeDefinition.IsMulti.ToString(), templateDefinition.IsMulti.ToString(), "bool"));
        if (runtimeDefinition.IsActive != templateDefinition.IsActive)
            changes.Add(CreateChange("IsActive", runtimeDefinition.IsActive.ToString(), templateDefinition.IsActive.ToString(), "bool"));
        if (runtimeDefinition.SortOrder != templateDefinition.SortOrder)
            changes.Add(CreateChange("SortOrder", runtimeDefinition.SortOrder.ToString(), templateDefinition.SortOrder.ToString(), "int"));
        if (runtimeDefinition.ExposureLevel != templateDefinition.ExposureLevel)
            changes.Add(CreateChange("ExposureLevel", runtimeDefinition.ExposureLevel.ToString(), templateDefinition.ExposureLevel.ToString(), "enum"));
        if (runtimeDefinition.IsSearchable != templateDefinition.IsSearchable)
            changes.Add(CreateChange("IsSearchable", runtimeDefinition.IsSearchable.ToString(), templateDefinition.IsSearchable.ToString(), "bool"));
        if (runtimeDefinition.IsFilterable != templateDefinition.IsFilterable)
            changes.Add(CreateChange("IsFilterable", runtimeDefinition.IsFilterable.ToString(), templateDefinition.IsFilterable.ToString(), "bool"));
        if (runtimeDefinition.IsExportable != templateDefinition.IsExportable)
            changes.Add(CreateChange("IsExportable", runtimeDefinition.IsExportable.ToString(), templateDefinition.IsExportable.ToString(), "bool"));
        if (runtimeDefinition.IsModerationRelevant != templateDefinition.IsModerationRelevant)
            changes.Add(CreateChange("IsModerationRelevant", runtimeDefinition.IsModerationRelevant.ToString(), templateDefinition.IsModerationRelevant.ToString(), "bool"));
        if (runtimeDefinition.IsAnalyticsRelevant != templateDefinition.IsAnalyticsRelevant)
            changes.Add(CreateChange("IsAnalyticsRelevant", runtimeDefinition.IsAnalyticsRelevant.ToString(), templateDefinition.IsAnalyticsRelevant.ToString(), "bool"));
        if (runtimeDefinition.IsSystemOwned != templateDefinition.IsSystemOwned)
            changes.Add(CreateChange("IsSystemOwned", runtimeDefinition.IsSystemOwned.ToString(), templateDefinition.IsSystemOwned.ToString(), "bool"));
        if (!string.Equals(runtimeDefinition.DefaultTextValue, templateDefinition.DefaultTextValue, StringComparison.Ordinal))
            changes.Add(CreateChange("DefaultTextValue", runtimeDefinition.DefaultTextValue, templateDefinition.DefaultTextValue, "string"));
        if (runtimeDefinition.DefaultNumberValue != templateDefinition.DefaultNumberValue)
            changes.Add(CreateChange("DefaultNumberValue", SerializeDecimal(runtimeDefinition.DefaultNumberValue), SerializeDecimal(templateDefinition.DefaultNumberValue), "decimal"));
        if (runtimeDefinition.DefaultBooleanValue != templateDefinition.DefaultBooleanValue)
            changes.Add(CreateChange("DefaultBooleanValue", SerializeBoolean(runtimeDefinition.DefaultBooleanValue), SerializeBoolean(templateDefinition.DefaultBooleanValue), "bool"));
        if (runtimeDefinition.DefaultDateTimeValue != templateDefinition.DefaultDateTimeValue)
            changes.Add(CreateChange("DefaultDateTimeValue", SerializeDateTime(runtimeDefinition.DefaultDateTimeValue), SerializeDateTime(templateDefinition.DefaultDateTimeValue), "datetimeoffset"));
        if (!string.Equals(runtimeDefinition.DefaultOption?.Value, templateDefinition.DefaultOption?.Value, StringComparison.Ordinal))
            changes.Add(CreateChange("DefaultOptionValue", runtimeDefinition.DefaultOption?.Value, templateDefinition.DefaultOption?.Value, "string"));
        if (runtimeDefinition.MinLength != templateDefinition.MinLength)
            changes.Add(CreateChange("MinLength", SerializeInt(runtimeDefinition.MinLength), SerializeInt(templateDefinition.MinLength), "int"));
        if (runtimeDefinition.MaxLength != templateDefinition.MaxLength)
            changes.Add(CreateChange("MaxLength", SerializeInt(runtimeDefinition.MaxLength), SerializeInt(templateDefinition.MaxLength), "int"));
        if (!string.Equals(runtimeDefinition.RegexPattern, templateDefinition.RegexPattern, StringComparison.Ordinal))
            changes.Add(CreateChange("RegexPattern", runtimeDefinition.RegexPattern, templateDefinition.RegexPattern, "string"));
        if (runtimeDefinition.MinNumber != templateDefinition.MinNumber)
            changes.Add(CreateChange("MinNumber", SerializeDecimal(runtimeDefinition.MinNumber), SerializeDecimal(templateDefinition.MinNumber), "decimal"));
        if (runtimeDefinition.MaxNumber != templateDefinition.MaxNumber)
            changes.Add(CreateChange("MaxNumber", SerializeDecimal(runtimeDefinition.MaxNumber), SerializeDecimal(templateDefinition.MaxNumber), "decimal"));
        if (runtimeDefinition.MinDateTime != templateDefinition.MinDateTime)
            changes.Add(CreateChange("MinDateTime", SerializeDateTime(runtimeDefinition.MinDateTime), SerializeDateTime(templateDefinition.MinDateTime), "datetimeoffset"));
        if (runtimeDefinition.MaxDateTime != templateDefinition.MaxDateTime)
            changes.Add(CreateChange("MaxDateTime", SerializeDateTime(runtimeDefinition.MaxDateTime), SerializeDateTime(templateDefinition.MaxDateTime), "datetimeoffset"));
        if (!string.Equals(runtimeDefinition.AllowedUrlSchemes, templateDefinition.AllowedUrlSchemes, StringComparison.Ordinal))
            changes.Add(CreateChange("AllowedUrlSchemes", runtimeDefinition.AllowedUrlSchemes, templateDefinition.AllowedUrlSchemes, "string"));

        return changes;
    }

    private static List<FieldChangeDto> BuildOptionFieldChanges(
        EventSessionCustomPropertyOption runtimeOption,
        EventSessionTemplateCustomPropertyOption templateOption)
    {
        var changes = new List<FieldChangeDto>();

        if (!string.Equals(runtimeOption.DisplayName, templateOption.DisplayName, StringComparison.Ordinal))
            changes.Add(CreateChange("DisplayName", runtimeOption.DisplayName, templateOption.DisplayName, "string"));
        if (!string.Equals(runtimeOption.Description, templateOption.Description, StringComparison.Ordinal))
            changes.Add(CreateChange("Description", runtimeOption.Description, templateOption.Description, "string"));
        if (!string.Equals(runtimeOption.Value, templateOption.Value, StringComparison.Ordinal))
            changes.Add(CreateChange("Value", runtimeOption.Value, templateOption.Value, "string"));
        if (runtimeOption.IsDefault != templateOption.IsDefault)
            changes.Add(CreateChange("IsDefault", runtimeOption.IsDefault.ToString(), templateOption.IsDefault.ToString(), "bool"));
        if (runtimeOption.IsActive != templateOption.IsActive)
            changes.Add(CreateChange("IsActive", runtimeOption.IsActive.ToString(), templateOption.IsActive.ToString(), "bool"));
        if (runtimeOption.SortOrder != templateOption.SortOrder)
            changes.Add(CreateChange("SortOrder", runtimeOption.SortOrder.ToString(), templateOption.SortOrder.ToString(), "int"));
        if (runtimeOption.ParentOptionId != templateOption.ParentOptionId)
            changes.Add(CreateChange("ParentOptionId", runtimeOption.ParentOptionId?.ToString(), templateOption.ParentOptionId?.ToString(), "guid"));

        return changes;
    }

    private static FieldChangeDto CreateChange(string fieldName, string? oldValue, string? newValue, string valueType)
        => new(fieldName, oldValue, newValue, valueType);

    private static string NormalizeCompositePart(string value)
        => CustomPropertyIdentity.NormalizeKey(value);

    private static string? ResolveTemplateKey(EventSession eventSession, IReadOnlyList<EventSessionCustomPropertyDefinition> runtimeDefinitions)
        => eventSession.SourceTemplateKey
           ?? runtimeDefinitions.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.SourceTemplateKey))?.SourceTemplateKey;

    private static string? SerializeDefaultValue(
        string? defaultTextValue,
        decimal? defaultNumberValue,
        bool? defaultBooleanValue,
        DateTimeOffset? defaultDateTimeValue,
        string? defaultOptionValue)
        => defaultTextValue
           ?? SerializeDecimal(defaultNumberValue)
           ?? SerializeBoolean(defaultBooleanValue)
           ?? SerializeDateTime(defaultDateTimeValue)
           ?? defaultOptionValue;

    private static string? SerializeDecimal(decimal? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? SerializeBoolean(bool? value)
        => value?.ToString();

    private static string? SerializeDateTime(DateTimeOffset? value)
        => value?.ToString("O");

    private static string? SerializeInt(int? value)
        => value?.ToString();
}
