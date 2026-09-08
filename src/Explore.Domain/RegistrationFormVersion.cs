using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class RegistrationFormVersion : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationFormSection> _sections = [];
    private readonly List<RegistrationFormRule> _rules = [];

    private RegistrationFormVersion()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public int Version { get; private set; }
    public int StatusId { get; private set; }
    public RegistrationFormStatus? Status { get; private set; }
    public int SourceKindId { get; private set; }
    public RegistrationFormVersionSourceKind? SourceKind { get; private set; }
    public string LanguageTag { get; private set; } = string.Empty;
    public string? SchemaHash { get; private set; }
    public string? DataSchemaArtifact { get; private set; }
    public string? UiSchemaArtifact { get; private set; }
    public string? LogicSchemaArtifact { get; private set; }
    public string? MappingArtifact { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public DateTime? RetiredAt { get; private set; }
    public Guid? SourceTemplateFormId { get; private set; }
    public Guid? SourceTemplateVersionId { get; private set; }
    public Guid? ExternalRegistrationProviderConnectionId { get; private set; }
    public Guid? ExternalRegistrationProviderSchemaRevisionId { get; private set; }
    public string? ExternalProviderSurveyId { get; private set; }
    public string? ExternalProviderSurveyRevisionId { get; private set; }
    public string? ExternalImportMappingRevisionHash { get; private set; }
    public IReadOnlyCollection<RegistrationFormSection> Sections => _sections.AsReadOnly();
    public IReadOnlyCollection<RegistrationFormRule> Rules => _rules.AsReadOnly();
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationFormVersion Create(
        RegistrationForm form,
        int version,
        string languageTag,
        Guid? sourceTemplateFormId,
        Guid? sourceTemplateVersionId,
        DateTime createdAt) => Create(
            Guid.CreateVersion7(), form, version, languageTag, sourceTemplateFormId, sourceTemplateVersionId, createdAt);

    public static RegistrationFormVersion Create(
        Guid id,
        RegistrationForm form,
        int version,
        string languageTag,
        Guid? sourceTemplateFormId,
        Guid? sourceTemplateVersionId,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Version identifier is required.", nameof(id));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if ((sourceTemplateFormId is null) != (sourceTemplateVersionId is null) ||
            sourceTemplateFormId == Guid.Empty || sourceTemplateVersionId == Guid.Empty)
        {
            throw new ArgumentException("Template form and version provenance must be supplied together.");
        }

        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));
        return new RegistrationFormVersion
        {
            Id = id,
            TenantId = form.TenantId,
            EventId = form.EventId,
            RegistrationFormId = form.Id,
            Version = version,
            StatusId = (int)RegistrationFormStatusEnum.Draft,
            SourceKindId = sourceTemplateFormId is null
                ? (int)RegistrationFormVersionSourceKindEnum.Authored
                : (int)RegistrationFormVersionSourceKindEnum.TemplateClone,
            LanguageTag = FormVersionRules.NormalizeLanguageTag(languageTag),
            SourceTemplateFormId = sourceTemplateFormId,
            SourceTemplateVersionId = sourceTemplateVersionId,
            CreatedAt = createdAt
        };
    }

    public static RegistrationFormVersion CreateExternalImported(
        RegistrationForm form,
        int version,
        string languageTag,
        Guid connectionId,
        Guid schemaRevisionId,
        string providerSurveyId,
        string? providerSurveyRevisionId,
        string mappingRevisionHash,
        DateTime createdAt)
    {
        RegistrationFormVersion imported = Create(Guid.CreateVersion7(), form, version, languageTag, null, null, createdAt);
        imported.SourceKindId = (int)RegistrationFormVersionSourceKindEnum.ExternalImported;
        imported.ExternalRegistrationProviderConnectionId = connectionId != Guid.Empty
            ? connectionId
            : throw new ArgumentException("External provider connection is required.", nameof(connectionId));
        imported.ExternalRegistrationProviderSchemaRevisionId = schemaRevisionId != Guid.Empty
            ? schemaRevisionId
            : throw new ArgumentException("External schema revision is required.", nameof(schemaRevisionId));
        imported.ExternalProviderSurveyId = NormalizeExternalProviderId(providerSurveyId, nameof(providerSurveyId));
        imported.ExternalProviderSurveyRevisionId = string.IsNullOrWhiteSpace(providerSurveyRevisionId)
            ? null
            : NormalizeExternalProviderId(providerSurveyRevisionId, nameof(providerSurveyRevisionId));
        imported.ExternalImportMappingRevisionHash = RegistrationSha256Hash.Normalize(mappingRevisionHash, nameof(mappingRevisionHash), "External import mapping revision hash");
        return imported;
    }

    public void AddSection(RegistrationFormSection section)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(section);
        if (section.RegistrationFormVersionId != Id || section.TenantId != TenantId || section.EventId != EventId)
        {
            throw new ArgumentException("Section must belong to this version, event, and tenant.", nameof(section));
        }

        if (_sections.Any(existing => !existing.IsDeleted &&
                (existing.Id == section.Id || existing.Ordinal == section.Ordinal)))
        {
            throw new ArgumentException("Section identity and ordinal must be unique within the version.", nameof(section));
        }

        _sections.Add(section);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RenameSection(RegistrationFormSection section, string title)
    {
        EnsureDraft();
        EnsureContains(section);
        section.Rename(title);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateSection(RegistrationFormSection section, int ordinal, string title)
    {
        EnsureDraft();
        EnsureContains(section);
        if (_sections.Any(existing => existing != section && !existing.IsDeleted && existing.Ordinal == ordinal))
        {
            throw new ArgumentException("Section ordinal must be unique within the version.", nameof(ordinal));
        }

        int previousOrdinal = section.Ordinal;
        string previousTitle = section.Title;
        section.Update(ordinal, title);
        try
        {
            ValidateAllRuleReferences();
        }
        catch
        {
            section.Update(previousOrdinal, previousTitle);
            throw;
        }

        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void ReorderSections(IReadOnlyList<Guid> sectionIds)
    {
        EnsureDraft();
        RegistrationFormSection[] activeSections = ValidateCompleteOrder(
            sectionIds,
            _sections.Where(section => !section.IsDeleted).ToArray(),
            section => section.Id,
            nameof(sectionIds));
        int[] previousOrdinals = [.. activeSections.Select(section => section.Ordinal)];
        ApplyOrder(sectionIds, activeSections, section => section.Id, (section, ordinal) => section.Reorder(ordinal));
        try
        {
            ValidateAllRuleReferences();
        }
        catch
        {
            for (int index = 0; index < activeSections.Length; index++)
            {
                activeSections[index].Reorder(previousOrdinals[index]);
            }

            throw;
        }

        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RemoveSection(RegistrationFormSection section, DateTime removedAt)
    {
        EnsureDraft();
        EnsureContains(section);
        if (section.Fields.Any(field => !field.IsDeleted))
        {
            throw new InvalidOperationException("A section must be empty before it can be removed.");
        }

        section.Remove(removedAt);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void AddField(RegistrationFormSection section, RegistrationFormField field)
    {
        EnsureDraft();
        EnsureContains(section);
        ArgumentNullException.ThrowIfNull(field);
        if (_sections.SelectMany(existingSection => existingSection.Fields)
            .Any(existingField => !existingField.IsDeleted &&
                existingField.Namespace == field.Namespace && existingField.Key == field.Key))
        {
            throw new ArgumentException(
                "Field machine identity must be unique within the version.", nameof(field));
        }

        section.AddField(field);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateFieldGovernance(
        RegistrationFormField field,
        int retentionPolicyId,
        RegistrationOrganizerVisibilityEnum organizerVisibility,
        bool requiresExplicitConsent,
        bool isProviderTransferAllowed,
        string? consentPurposeCode = null,
        string? consentTextVersion = null,
        string? consentText = null) => UpdateFieldGovernance(
            field, retentionPolicyId, organizerVisibility, requiresExplicitConsent, isProviderTransferAllowed,
            false, null, false, false, consentPurposeCode, consentTextVersion, consentText);

    public void UpdateFieldGovernance(
        RegistrationFormField field,
        int retentionPolicyId,
        RegistrationOrganizerVisibilityEnum organizerVisibility,
        bool requiresExplicitConsent,
        bool isProviderTransferAllowed,
        bool isExportable,
        string? exportPurposeCode,
        bool isAnalyticsRelevant,
        bool isOperationallyFilterable,
        string? consentPurposeCode = null,
        string? consentTextVersion = null,
        string? consentText = null)
    {
        EnsureDraft();
        EnsureContains(field);
        field.UpdateGovernance(retentionPolicyId, organizerVisibility, requiresExplicitConsent, isProviderTransferAllowed,
            isExportable, exportPurposeCode, isAnalyticsRelevant, isOperationallyFilterable,
            consentPurposeCode, consentTextVersion, consentText);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateFieldDetails(RegistrationFormField field, int ordinal, string label)
    {
        EnsureDraft();
        EnsureContains(field);
        RegistrationFormSection section = _sections.Single(candidate => candidate.Fields.Contains(field));
        if (section.Fields.Any(existing => existing != field && !existing.IsDeleted && existing.Ordinal == ordinal))
        {
            throw new ArgumentException("Field ordinal must be unique within the section.", nameof(ordinal));
        }

        int previousOrdinal = field.Ordinal;
        string previousLabel = field.Label;
        field.UpdateDetails(ordinal, label);
        try
        {
            ValidateAllRuleReferences();
        }
        catch
        {
            field.UpdateDetails(previousOrdinal, previousLabel);
            throw;
        }

        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void ReorderFields(RegistrationFormSection section, IReadOnlyList<Guid> fieldIds)
    {
        EnsureDraft();
        EnsureContains(section);
        RegistrationFormField[] activeFields = ValidateCompleteOrder(
            fieldIds,
            section.Fields.Where(field => !field.IsDeleted).ToArray(),
            field => field.Id,
            nameof(fieldIds));
        int[] previousOrdinals = [.. activeFields.Select(field => field.Ordinal)];
        ApplyOrder(fieldIds, activeFields, field => field.Id, (field, ordinal) => field.Reorder(ordinal));
        try
        {
            ValidateAllRuleReferences();
        }
        catch
        {
            for (int index = 0; index < activeFields.Length; index++)
            {
                activeFields[index].Reorder(previousOrdinals[index]);
            }

            throw;
        }

        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RemoveField(RegistrationFormField field, DateTime removedAt)
    {
        EnsureDraft();
        EnsureContains(field);
        FormFieldReference reference = new(field.Namespace, field.Key);
        if (_rules.Any(rule => !rule.IsDeleted &&
                (rule.Target == reference || FormConditionEvaluator.References(rule.Condition).Contains(reference))))
        {
            throw new InvalidOperationException("A field referenced by an active rule cannot be removed.");
        }

        field.Remove(removedAt);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateFieldValidation(
        RegistrationFormField field,
        bool isRequired,
        bool isMulti,
        int? minLength,
        int? maxLength,
        string? regexPattern,
        decimal? minNumber,
        decimal? maxNumber,
        DateTimeOffset? minDateTime,
        DateTimeOffset? maxDateTime,
        string? allowedUrlSchemes)
    {
        EnsureDraft();
        EnsureContains(field);
        field.UpdateValidation(isRequired, isMulti, minLength, maxLength, regexPattern, minNumber, maxNumber,
            minDateTime, maxDateTime, allowedUrlSchemes);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void AddOption(RegistrationFormField field, RegistrationFormFieldOption option)
    {
        EnsureDraft();
        EnsureContains(field);
        field.AddOption(option);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateFieldOption(
        RegistrationFormField field,
        RegistrationFormFieldOption option,
        int ordinal,
        string key,
        string label)
    {
        EnsureDraft();
        EnsureContains(field);
        field.UpdateOption(option, ordinal, key, label);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RetireOption(RegistrationFormField field, RegistrationFormFieldOption option, DateTime retiredAt)
    {
        EnsureDraft();
        EnsureContains(field);
        field.RetireOption(option, retiredAt);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void AddRule(RegistrationFormRule rule)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.RegistrationFormVersionId != Id || rule.RegistrationFormId != RegistrationFormId ||
            rule.EventId != EventId || rule.TenantId != TenantId)
        {
            throw new ArgumentException("Rule must belong to this version, form, event, and tenant.", nameof(rule));
        }

        if (_rules.Any(existing => !existing.IsDeleted &&
                (existing.Id == rule.Id || existing.Ordinal == rule.Ordinal)))
        {
            throw new ArgumentException("Rule identity and ordinal must be unique within the version.", nameof(rule));
        }

        ValidateRuleReferences(rule);
        _rules.Add(rule);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RemoveRule(RegistrationFormRule rule, DateTime removedAt)
    {
        EnsureDraft();
        EnsureContains(rule);
        rule.Remove(removedAt);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateRule(
        RegistrationFormRule rule,
        int ordinal,
        FormFieldReference target,
        RegistrationFormRuleEffect effect,
        FormCondition condition)
    {
        EnsureDraft();
        EnsureContains(rule);
        if (_rules.Any(existing => existing != rule && !existing.IsDeleted && existing.Ordinal == ordinal))
        {
            throw new ArgumentException("Rule ordinal must be unique within the version.", nameof(ordinal));
        }

        ValidateRuleReferences(target, condition);
        rule.Update(ordinal, target, effect, condition);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void ReorderRules(IReadOnlyList<Guid> ruleIds)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(ruleIds);
        RegistrationFormRule[] activeRules = [.. _rules.Where(rule => !rule.IsDeleted)];
        if (ruleIds.Count != activeRules.Length || ruleIds.Distinct().Count() != ruleIds.Count ||
            ruleIds.Any(id => activeRules.All(rule => rule.Id != id)))
        {
            throw new ArgumentException("Reorder must contain every active rule exactly once.", nameof(ruleIds));
        }

        for (int index = 0; index < ruleIds.Count; index++)
        {
            activeRules.Single(rule => rule.Id == ruleIds[index]).Reorder(index + 1);
        }

        ConcurrencyStamp = Guid.CreateVersion7();
    }

    internal void PinGeneratedSchemaBundle(string canonicalSchemaBundle, DateTime publishedAt)
    {
        EnsureDraft();
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSchemaBundle);
        FormVersionRules.RequireUtc(publishedAt, nameof(publishedAt));
        if (_sections.Count == 0 || _sections.SelectMany(section => section.Fields).Any(field =>
                field.FieldTypeId == (int)RegistrationFieldTypeEnum.OpaqueExternal && field.IsRequired))
        {
            throw new InvalidOperationException("Published forms require content and cannot require opaque external fields.");
        }

        foreach (RegistrationFormRule rule in _rules.Where(rule => !rule.IsDeleted))
        {
            ValidateRuleReferences(rule);
        }

        using JsonDocument bundle = JsonDocument.Parse(canonicalSchemaBundle);
        JsonElement root = bundle.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("$schema", out JsonElement schema) ||
            schema.GetString() != "https://json-schema.org/draft/2020-12/schema" ||
            !root.TryGetProperty("versionId", out JsonElement versionId) ||
            versionId.GetString() != Id.ToString("D", CultureInfo.InvariantCulture) ||
            !root.TryGetProperty("version", out JsonElement versionNumber) ||
            !versionNumber.TryGetInt32(out int bundleVersion) || bundleVersion != Version ||
            !root.TryGetProperty("languageTag", out JsonElement languageTag) || languageTag.GetString() != LanguageTag)
        {
            throw new ArgumentException("Schema bundle identity must match this form version.", nameof(canonicalSchemaBundle));
        }

        string dataSchemaArtifact = RequiredArtifact(root, "data");
        string uiSchemaArtifact = RequiredArtifact(root, "ui");
        string logicSchemaArtifact = RequiredArtifact(root, "logic");
        string mappingArtifact = RequiredArtifact(root, "mapping");
        string schemaHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSchemaBundle)));

        DataSchemaArtifact = dataSchemaArtifact;
        UiSchemaArtifact = uiSchemaArtifact;
        LogicSchemaArtifact = logicSchemaArtifact;
        MappingArtifact = mappingArtifact;
        SchemaHash = schemaHash;
        PublishedAt = publishedAt;
        StatusId = (int)RegistrationFormStatusEnum.Published;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Retire(DateTime retiredAt)
    {
        if (StatusId != (int)RegistrationFormStatusEnum.Published)
        {
            throw new InvalidOperationException("Only a published form version can be retired.");
        }

        FormVersionRules.RequireUtc(retiredAt, nameof(retiredAt));
        RetiredAt = retiredAt;
        StatusId = (int)RegistrationFormStatusEnum.Retired;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public RegistrationFormVersion CloneToDraft(int version, DateTime createdAt)
    {
        if (StatusId != (int)RegistrationFormStatusEnum.Published)
        {
            throw new InvalidOperationException("Only a published form version can be cloned into a draft.");
        }

        RegistrationFormVersion clone = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            EventId = EventId,
            RegistrationFormId = RegistrationFormId,
            Version = version,
            StatusId = (int)RegistrationFormStatusEnum.Draft,
            SourceKindId = (int)RegistrationFormVersionSourceKindEnum.Authored,
            LanguageTag = LanguageTag,
            SourceTemplateFormId = SourceTemplateFormId,
            SourceTemplateVersionId = SourceTemplateVersionId,
            CreatedAt = createdAt
        };
        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));
        if (version <= Version)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A cloned draft must have a later version number.");
        }

        foreach (RegistrationFormSection section in _sections.Where(section => !section.IsDeleted))
        {
            clone._sections.Add(section.CloneTo(clone.Id));
        }

        foreach (RegistrationFormRule rule in _rules.Where(rule => !rule.IsDeleted))
        {
            clone._rules.Add(rule.CloneTo(clone.Id));
        }

        return clone;
    }

    public RegistrationFormVersion CloneToTemplateInstance(
        RegistrationForm targetForm,
        DateTime createdAt,
        Guid sourceTemplateFormId,
        Guid sourceTemplateVersionId)
    {
        ArgumentNullException.ThrowIfNull(targetForm);
        if (StatusId != (int)RegistrationFormStatusEnum.Published)
        {
            throw new InvalidOperationException("Only a published form version can be instantiated from a template.");
        }

        RegistrationFormVersion clone = Create(
            Guid.CreateVersion7(), targetForm, 1, LanguageTag, sourceTemplateFormId, sourceTemplateVersionId, createdAt);
        foreach (RegistrationFormSection section in _sections.Where(section => !section.IsDeleted))
        {
            clone._sections.Add(section.CloneTo(clone));
        }

        foreach (RegistrationFormRule rule in _rules.Where(rule => !rule.IsDeleted))
        {
            clone._rules.Add(rule.CloneTo(clone));
        }

        return clone;
    }

    private static string NormalizeExternalProviderId(string value, string parameterName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 200 && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException("External provider id must be non-blank and at most 200 characters.", parameterName);
    }

    private void EnsureDraft()
    {
        if (StatusId != (int)RegistrationFormStatusEnum.Draft)
        {
            throw new InvalidOperationException("Published or retired form versions are immutable.");
        }
    }

    private void EnsureContains(RegistrationFormSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (!_sections.Contains(section) || section.IsDeleted)
        {
            throw new ArgumentException("Section does not belong to this version.", nameof(section));
        }
    }

    private void EnsureContains(RegistrationFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.IsDeleted || !_sections.Any(section => !section.IsDeleted && section.Fields.Contains(field)))
        {
            throw new ArgumentException("Field does not belong to this version.", nameof(field));
        }
    }

    private void EnsureContains(RegistrationFormRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!_rules.Contains(rule) || rule.IsDeleted)
        {
            throw new ArgumentException("Rule does not belong to this version.", nameof(rule));
        }
    }

    private void ValidateRuleReferences(RegistrationFormRule rule)
    {
        RegistrationFormField[] orderedFields =
        [
            .. _sections.Where(section => !section.IsDeleted).OrderBy(section => section.Ordinal)
                .SelectMany(section => section.Fields.Where(field => !field.IsDeleted).OrderBy(field => field.Ordinal))
        ];
        ValidateRuleReferences(rule.Target, rule.Condition, orderedFields);
    }

    private void ValidateAllRuleReferences()
    {
        foreach (RegistrationFormRule rule in _rules.Where(rule => !rule.IsDeleted))
        {
            ValidateRuleReferences(rule);
        }
    }

    private void ValidateRuleReferences(FormFieldReference target, FormCondition condition)
    {
        RegistrationFormField[] orderedFields =
        [
            .. _sections.Where(section => !section.IsDeleted).OrderBy(section => section.Ordinal)
                .SelectMany(section => section.Fields.Where(field => !field.IsDeleted).OrderBy(field => field.Ordinal))
        ];
        ValidateRuleReferences(target, condition, orderedFields);
    }

    private static void ValidateRuleReferences(
        FormFieldReference target,
        FormCondition condition,
        RegistrationFormField[] orderedFields)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(condition);
        int targetIndex = Array.FindIndex(orderedFields,
            field => new FormFieldReference(field.Namespace, field.Key) == target);
        if (targetIndex < 0)
        {
            throw new ArgumentException("Rule target must exist in this form version.", nameof(target));
        }

        foreach (FormFieldReference reference in FormConditionEvaluator.References(condition))
        {
            int referenceIndex = Array.FindIndex(orderedFields,
                field => new FormFieldReference(field.Namespace, field.Key) == reference);
            if (referenceIndex < 0 || referenceIndex >= targetIndex)
            {
                throw new ArgumentException("Rule conditions may reference only earlier fields in this form version.",
                    nameof(condition));
            }
        }
    }

    private static string RequiredArtifact(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement artifact) || artifact.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Schema bundle requires an object artifact named '{name}'.", "canonicalSchemaBundle");
        }

        return artifact.GetRawText();
    }

    private static T[] ValidateCompleteOrder<T>(
        IReadOnlyList<Guid> orderedIds,
        T[] activeItems,
        Func<T, Guid> idSelector,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(orderedIds, parameterName);
        if (orderedIds.Count is <= 0 or > 200 ||
            orderedIds.Count != activeItems.Length ||
            orderedIds.Distinct().Count() != orderedIds.Count ||
            orderedIds.Any(id => id == Guid.Empty || activeItems.All(item => idSelector(item) != id)))
        {
            throw new ArgumentException(
                "Reorder must contain every active item exactly once and no more than 200 items.",
                parameterName);
        }

        return activeItems;
    }

    private static void ApplyOrder<T>(
        IReadOnlyList<Guid> orderedIds,
        IReadOnlyCollection<T> activeItems,
        Func<T, Guid> idSelector,
        Action<T, int> reorder)
    {
        for (int index = 0; index < orderedIds.Count; index++)
        {
            reorder(activeItems.Single(item => idSelector(item) == orderedIds[index]), index + 1);
        }
    }
}
