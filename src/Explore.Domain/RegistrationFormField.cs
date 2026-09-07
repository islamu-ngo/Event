using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class RegistrationFormField : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationFormFieldOption> _options = [];

    private RegistrationFormField()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public Guid RegistrationFormSectionId { get; private set; }
    public int Ordinal { get; private set; }
    public string Namespace { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public int FieldTypeId { get; private set; }
    public RegistrationFieldType? FieldType { get; private set; }
    public int RetentionPolicyId { get; private set; }
    public RegistrationRetentionPolicy? RetentionPolicy { get; private set; }
    public bool IsExportable { get; private set; }
    public string? ExportPurposeCode { get; private set; }
    public int OrganizerVisibilityId { get; private set; }
    public RegistrationOrganizerVisibility? OrganizerVisibility { get; private set; }
    public bool RequiresExplicitConsent { get; private set; }
    public string? ConsentPurposeCode { get; private set; }
    public string? ConsentText { get; private set; }
    public string? ConsentTextVersion { get; private set; }
    public bool IsProviderTransferAllowed { get; private set; }
    public bool IsAnalyticsRelevant { get; private set; }
    public bool IsOperationallyFilterable { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsMulti { get; private set; }
    public int? MinLength { get; private set; }
    public int? MaxLength { get; private set; }
    public string? RegexPattern { get; private set; }
    public decimal? MinNumber { get; private set; }
    public decimal? MaxNumber { get; private set; }
    public DateTimeOffset? MinDateTime { get; private set; }
    public DateTimeOffset? MaxDateTime { get; private set; }
    public string? AllowedUrlSchemes { get; private set; }
    public IReadOnlyCollection<RegistrationFormFieldOption> Options => _options.AsReadOnly();
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationFormField Create(
        Guid id,
        RegistrationFormSection section,
        int ordinal,
        string @namespace,
        string key,
        string label,
        RegistrationFieldTypeEnum fieldType,
        int retentionPolicyId,
        RegistrationOrganizerVisibilityEnum organizerVisibility,
        bool requiresExplicitConsent,
        bool isProviderTransferAllowed,
        DateTime createdAt,
        string? consentPurposeCode = null,
        string? consentTextVersion = null,
        string? consentText = null) => Create(
            id, section, ordinal, @namespace, key, label, fieldType, retentionPolicyId, organizerVisibility,
            requiresExplicitConsent, isProviderTransferAllowed, false, null, false, false, createdAt, consentPurposeCode,
            consentTextVersion, consentText);

    public static RegistrationFormField Create(
        Guid id,
        RegistrationFormSection section,
        int ordinal,
        string @namespace,
        string key,
        string label,
        RegistrationFieldTypeEnum fieldType,
        int retentionPolicyId,
        RegistrationOrganizerVisibilityEnum organizerVisibility,
        bool requiresExplicitConsent,
        bool isProviderTransferAllowed,
        bool isExportable,
        string? exportPurposeCode,
        bool isAnalyticsRelevant,
        bool isOperationallyFilterable,
        DateTime createdAt,
        string? consentPurposeCode = null,
        string? consentTextVersion = null,
        string? consentText = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Field identifier is required.", nameof(id));
        }

        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        FormVersionRules.ValidateGovernance(fieldType, retentionPolicyId, organizerVisibility,
            requiresExplicitConsent, isProviderTransferAllowed, isExportable, exportPurposeCode,
            isAnalyticsRelevant, isOperationallyFilterable);
        (string? normalizedPurposeCode, string? normalizedConsentText, string? normalizedTextVersion) =
            NormalizeConsentMetadata(requiresExplicitConsent, consentPurposeCode, consentTextVersion, consentText);
        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));
        return new RegistrationFormField
        {
            Id = id,
            TenantId = section.TenantId,
            EventId = section.EventId,
            RegistrationFormId = section.RegistrationFormId,
            RegistrationFormVersionId = section.RegistrationFormVersionId,
            RegistrationFormSectionId = section.Id,
            Ordinal = ordinal,
            Namespace = FormVersionRules.NormalizeNamespace(@namespace),
            Key = FormVersionRules.NormalizeKey(key),
            Label = label.Trim(),
            FieldTypeId = (int)fieldType,
            RetentionPolicyId = retentionPolicyId,
            OrganizerVisibilityId = (int)organizerVisibility,
            RequiresExplicitConsent = requiresExplicitConsent,
            ConsentPurposeCode = normalizedPurposeCode,
            ConsentText = normalizedConsentText,
            ConsentTextVersion = normalizedTextVersion,
            IsProviderTransferAllowed = isProviderTransferAllowed,
            IsExportable = isExportable,
            ExportPurposeCode = NormalizePurposeCode(exportPurposeCode),
            IsAnalyticsRelevant = isAnalyticsRelevant,
            IsOperationallyFilterable = isOperationallyFilterable,
            CreatedAt = createdAt
        };
    }

    internal void UpdateGovernance(
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
        FormVersionRules.ValidateGovernance((RegistrationFieldTypeEnum)FieldTypeId, retentionPolicyId,
            organizerVisibility, requiresExplicitConsent, isProviderTransferAllowed, isExportable, exportPurposeCode,
            isAnalyticsRelevant, isOperationallyFilterable);
        (string? normalizedPurposeCode, string? normalizedConsentText, string? normalizedTextVersion) =
            NormalizeConsentMetadata(requiresExplicitConsent, consentPurposeCode, consentTextVersion, consentText);
        RetentionPolicyId = retentionPolicyId;
        OrganizerVisibilityId = (int)organizerVisibility;
        RequiresExplicitConsent = requiresExplicitConsent;
        ConsentPurposeCode = normalizedPurposeCode;
        ConsentText = normalizedConsentText;
        ConsentTextVersion = normalizedTextVersion;
        IsProviderTransferAllowed = isProviderTransferAllowed;
        IsExportable = isExportable;
        ExportPurposeCode = NormalizePurposeCode(exportPurposeCode);
        IsAnalyticsRelevant = isAnalyticsRelevant;
        IsOperationallyFilterable = isOperationallyFilterable;
    }

    internal void UpdateDetails(int ordinal, string label)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Ordinal = ordinal;
        Label = label.Trim();
    }

    internal void Reorder(int ordinal)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        Ordinal = ordinal;
    }

    internal void UpdateValidation(
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
        FormVersionRules.ValidateConstraints(minLength, maxLength, minNumber, maxNumber, minDateTime, maxDateTime);
        if (FieldTypeId == (int)RegistrationFieldTypeEnum.OpaqueExternal && isRequired)
        {
            throw new ArgumentException("Opaque external fields cannot be required.", nameof(isRequired));
        }

        IsRequired = isRequired;
        IsMulti = isMulti;
        MinLength = minLength;
        MaxLength = maxLength;
        RegexPattern = string.IsNullOrWhiteSpace(regexPattern) ? null : regexPattern.Trim();
        MinNumber = minNumber;
        MaxNumber = maxNumber;
        MinDateTime = minDateTime;
        MaxDateTime = maxDateTime;
        AllowedUrlSchemes = string.IsNullOrWhiteSpace(allowedUrlSchemes) ? null : allowedUrlSchemes.Trim();
    }

    internal void AddOption(RegistrationFormFieldOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.RegistrationFormFieldId != Id || option.RegistrationFormVersionId != RegistrationFormVersionId ||
            option.TenantId != TenantId || option.EventId != EventId)
        {
            throw new ArgumentException("Option must belong to this field, version, event, and tenant.", nameof(option));
        }

        if (_options.Any(existing => existing.Id == option.Id || existing.Ordinal == option.Ordinal || existing.Key == option.Key))
        {
            throw new ArgumentException("Option identity, key, and ordinal must be unique within the field.", nameof(option));
        }

        _options.Add(option);
    }

    internal void UpdateOption(RegistrationFormFieldOption option, int ordinal, string key, string label)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!_options.Contains(option) || option.IsDeleted)
        {
            throw new ArgumentException("Option does not belong to this field.", nameof(option));
        }

        string normalizedKey = FormVersionRules.NormalizeKey(key);
        if (_options.Any(existing => existing != option && !existing.IsDeleted &&
                (existing.Ordinal == ordinal || existing.Key == normalizedKey)))
        {
            throw new ArgumentException("Option key and ordinal must be unique within the field.", nameof(option));
        }

        option.Update(ordinal, normalizedKey, label);
    }

    internal void RetireOption(RegistrationFormFieldOption option, DateTime retiredAt)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!_options.Contains(option))
        {
            throw new ArgumentException("Option does not belong to this field.", nameof(option));
        }

        option.Retire(retiredAt);
    }

    internal void Remove(DateTime removedAt)
    {
        FormVersionRules.RequireUtc(removedAt, nameof(removedAt));
        IsDeleted = true;
        DeletedAt ??= removedAt;
    }

    internal RegistrationFormField CloneTo(Guid versionId, Guid sectionId)
    {
        RegistrationFormField clone = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            EventId = EventId,
            RegistrationFormId = RegistrationFormId,
            RegistrationFormVersionId = versionId,
            RegistrationFormSectionId = sectionId,
            Ordinal = Ordinal,
            Namespace = Namespace,
            Key = Key,
            Label = Label,
            FieldTypeId = FieldTypeId,
            RetentionPolicyId = RetentionPolicyId,
            OrganizerVisibilityId = OrganizerVisibilityId,
            RequiresExplicitConsent = RequiresExplicitConsent,
            ConsentPurposeCode = ConsentPurposeCode,
            ConsentText = ConsentText,
            ConsentTextVersion = ConsentTextVersion,
            IsProviderTransferAllowed = IsProviderTransferAllowed,
            IsExportable = IsExportable,
            ExportPurposeCode = ExportPurposeCode,
            IsAnalyticsRelevant = IsAnalyticsRelevant,
            IsOperationallyFilterable = IsOperationallyFilterable,
            IsRequired = IsRequired,
            IsMulti = IsMulti,
            MinLength = MinLength,
            MaxLength = MaxLength,
            RegexPattern = RegexPattern,
            MinNumber = MinNumber,
            MaxNumber = MaxNumber,
            MinDateTime = MinDateTime,
            MaxDateTime = MaxDateTime,
            AllowedUrlSchemes = AllowedUrlSchemes,
            CreatedAt = CreatedAt
        };
        foreach (RegistrationFormFieldOption option in _options.Where(option => !option.IsDeleted))
        {
            clone._options.Add(option.CloneTo(versionId, sectionId, clone.Id));
        }

        return clone;
    }

    internal RegistrationFormField CloneTo(RegistrationFormVersion version, Guid sectionId)
    {
        RegistrationFormField clone = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = version.TenantId,
            EventId = version.EventId,
            RegistrationFormId = version.RegistrationFormId,
            RegistrationFormVersionId = version.Id,
            RegistrationFormSectionId = sectionId,
            Ordinal = Ordinal,
            Namespace = Namespace,
            Key = Key,
            Label = Label,
            FieldTypeId = FieldTypeId,
            RetentionPolicyId = RetentionPolicyId,
            OrganizerVisibilityId = OrganizerVisibilityId,
            RequiresExplicitConsent = RequiresExplicitConsent,
            IsProviderTransferAllowed = IsProviderTransferAllowed,
            IsExportable = IsExportable,
            ExportPurposeCode = ExportPurposeCode,
            IsAnalyticsRelevant = IsAnalyticsRelevant,
            IsOperationallyFilterable = IsOperationallyFilterable,
            ConsentPurposeCode = ConsentPurposeCode,
            ConsentTextVersion = ConsentTextVersion,
            ConsentText = ConsentText,
            IsRequired = IsRequired,
            IsMulti = IsMulti,
            MinLength = MinLength,
            MaxLength = MaxLength,
            RegexPattern = RegexPattern,
            MinNumber = MinNumber,
            MaxNumber = MaxNumber,
            MinDateTime = MinDateTime,
            MaxDateTime = MaxDateTime,
            AllowedUrlSchemes = AllowedUrlSchemes,
            CreatedAt = CreatedAt
        };
        foreach (RegistrationFormFieldOption option in _options.Where(option => !option.IsDeleted))
        {
            clone._options.Add(option.CloneTo(version, sectionId, clone.Id));
        }

        return clone;
    }

    private static string? NormalizePurposeCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static (string? PurposeCode, string? Text, string? TextVersion) NormalizeConsentMetadata(
        bool requiresExplicitConsent,
        string? consentPurposeCode,
        string? consentTextVersion,
        string? consentText)
    {
        string? purposeCode = string.IsNullOrWhiteSpace(consentPurposeCode)
            ? null
            : consentPurposeCode.Trim().ToUpperInvariant();
        string? text = string.IsNullOrWhiteSpace(consentText) ? null : consentText.Trim();
        string? textVersion = string.IsNullOrWhiteSpace(consentTextVersion) ? null : consentTextVersion.Trim();
        if (purposeCode?.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(consentPurposeCode));
        }

        if (textVersion?.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(consentTextVersion));
        }

        if (text?.Length > 4000)
        {
            throw new ArgumentOutOfRangeException(nameof(consentText));
        }

        if (requiresExplicitConsent
                ? purposeCode is null || text is null || textVersion is null
                : purposeCode is not null || text is not null || textVersion is not null)
        {
            throw new ArgumentException(
                "Explicit-consent fields require a purpose code, text, and text version; other fields require none.");
        }

        return (purposeCode, text, textVersion);
    }
}
