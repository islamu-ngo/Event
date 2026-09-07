using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio.RegistrationForms;

public sealed class RegistrationFormFieldEditModel
{
    public static readonly IReadOnlyList<RegistrationFormFieldTypeChoice> FieldTypes =
    [
        new(1, "SHORT_TEXT", "Short text"), new(2, "LONG_TEXT", "Long text"),
        new(3, "INTEGER", "Integer"), new(4, "DECIMAL", "Decimal"),
        new(5, "BOOLEAN", "Boolean"), new(6, "DATE", "Date"), new(7, "TIME", "Time"),
        new(8, "INSTANT", "Instant"), new(9, "EMAIL", "Email"), new(10, "PHONE", "Phone"),
        new(11, "URL", "URL"), new(12, "COUNTRY_CODE", "Country"),
        new(13, "LANGUAGE_TAG", "Language"), new(14, "SINGLE_CHOICE", "Single choice"),
        new(15, "MULTIPLE_CHOICE", "Multiple choice"), new(16, "RATING", "Rating"),
        new(17, "CONSENT", "Consent"), new(18, "FILE", "File"),
        new(19, "OPAQUE_EXTERNAL", "External provider value")
    ];

    public RegistrationFormFieldDto? Source { get; private init; }
    public int Ordinal { get; set; }
    public string Namespace { get; set; } = "event";
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int FieldTypeId { get; set; } = 1;
    public int RetentionPolicyId { get; set; } = 1;
    public int OrganizerVisibilityId { get; set; } = 2;
    public bool RequiresExplicitConsent { get; set; }
    public bool IsProviderTransferAllowed { get; set; }
    public bool IsAnalyticsRelevant { get; set; }
    public bool IsOperationallyFilterable { get; set; }
    public string? ConsentPurposeCode { get; set; }
    public string? ConsentTextVersion { get; set; }
    public string? ConsentText { get; set; }
    public bool IsRequired { get; set; }
    public bool IsMulti { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public double? MinNumber { get; set; }
    public double? MaxNumber { get; set; }
    public DateTime? MinDateTime { get; set; }
    public DateTime? MaxDateTime { get; set; }
    public string? RegexPattern { get; set; }
    public string? AllowedUrlSchemes { get; set; }

    public string FieldTypeCode => FieldTypes.First(item => item.Id == FieldTypeId).Code;
    public bool IsTextLike => FieldTypeId is 1 or 2 or 9 or 10 or 11;
    public bool IsNumeric => FieldTypeId is 3 or 4 or 16;
    public bool IsTemporal => FieldTypeId is 6 or 7 or 8;
    public bool IsConsent => FieldTypeId == 17;
    public bool CanUseForAnalytics => (FieldTypeId is 3 or 4 or 5 or 6 or 14 or 15 or 16) && !RequiresExplicitConsent;
    public bool IsValid => Ordinal > 0 && !string.IsNullOrWhiteSpace(Namespace) && !string.IsNullOrWhiteSpace(Key)
        && !string.IsNullOrWhiteSpace(Label) && RetentionPolicyId > 0 && OrganizerVisibilityId is 1 or 2
        && (MinLength is null || MaxLength is null || MinLength <= MaxLength)
        && (MinNumber is null || MaxNumber is null || MinNumber <= MaxNumber)
        && (MinDateTime is null || MaxDateTime is null || MinDateTime <= MaxDateTime)
        && (!IsConsent || RequiresExplicitConsent && !string.IsNullOrWhiteSpace(ConsentPurposeCode)
            && !string.IsNullOrWhiteSpace(ConsentTextVersion) && !string.IsNullOrWhiteSpace(ConsentText));

    public static RegistrationFormFieldEditModel Create(int ordinal) => new() { Ordinal = ordinal };

    public static RegistrationFormFieldEditModel From(RegistrationFormFieldDto field) => new()
    {
        Source = field,
        Ordinal = field.Ordinal,
        Namespace = field.Namespace,
        Key = field.Key,
        Label = field.Label,
        FieldTypeId = field.FieldTypeId,
        RetentionPolicyId = field.RetentionPolicyId,
        OrganizerVisibilityId = field.OrganizerVisibilityId,
        RequiresExplicitConsent = field.RequiresExplicitConsent,
        IsProviderTransferAllowed = field.IsProviderTransferAllowed,
        IsAnalyticsRelevant = Bool(field.AdditionalProperties, "isAnalyticsRelevant"),
        IsOperationallyFilterable = Bool(field.AdditionalProperties, "isOperationallyFilterable"),
        ConsentPurposeCode = field.ConsentPurposeCode,
        ConsentTextVersion = field.ConsentTextVersion,
        ConsentText = field.ConsentText,
        IsRequired = field.IsRequired,
        IsMulti = field.IsMulti,
        MinLength = field.MinLength,
        MaxLength = field.MaxLength,
        MinNumber = field.MinNumber,
        MaxNumber = field.MaxNumber,
        MinDateTime = field.MinDateTime?.UtcDateTime,
        MaxDateTime = field.MaxDateTime?.UtcDateTime,
        RegexPattern = field.RegexPattern,
        AllowedUrlSchemes = field.AllowedUrlSchemes
    };

    public void SetFieldType(int value)
    {
        FieldTypeId = value;
        if (IsConsent)
        {
            RequiresExplicitConsent = true;
        }

        if (!CanUseForAnalytics)
        {
            IsAnalyticsRelevant = false;
            IsOperationallyFilterable = false;
        }
    }

    public RegistrationFormFieldCreateInput ToCreateInput()
    {
        RegistrationFormFieldCreateInput input = new()
        {
            Ordinal = Ordinal,
            Namespace = Namespace.Trim(),
            Key = Key.Trim(),
            Label = Label.Trim(),
            FieldTypeId = FieldTypeId,
            RetentionPolicyId = RetentionPolicyId,
            OrganizerVisibilityId = OrganizerVisibilityId,
            RequiresExplicitConsent = RequiresExplicitConsent,
            IsProviderTransferAllowed = IsProviderTransferAllowed,
            ConsentPurposeCode = Clean(ConsentPurposeCode),
            ConsentTextVersion = Clean(ConsentTextVersion),
            ConsentText = Clean(ConsentText)
        };
        AddAnalyticsGovernance(input.AdditionalProperties);
        return input;
    }

    public RegistrationFormFieldUpdateInput ToUpdateInput()
    {
        RegistrationFormFieldUpdateInput input = new()
        {
            Ordinal = Ordinal,
            Label = Label.Trim(),
            RetentionPolicyId = RetentionPolicyId,
            OrganizerVisibilityId = OrganizerVisibilityId,
            RequiresExplicitConsent = RequiresExplicitConsent,
            IsProviderTransferAllowed = IsProviderTransferAllowed,
            ConsentPurposeCode = Clean(ConsentPurposeCode),
            ConsentTextVersion = Clean(ConsentTextVersion),
            ConsentText = Clean(ConsentText),
            IsRequired = IsRequired,
            IsMulti = IsMulti,
            MinLength = MinLength,
            MaxLength = MaxLength,
            MinNumber = MinNumber,
            MaxNumber = MaxNumber,
            MinDateTime = Offset(MinDateTime),
            MaxDateTime = Offset(MaxDateTime),
            RegexPattern = Clean(RegexPattern),
            AllowedUrlSchemes = Clean(AllowedUrlSchemes)
        };
        AddAnalyticsGovernance(input.AdditionalProperties);
        return input;
    }

    private void AddAnalyticsGovernance(IDictionary<string, object> properties)
    {
        properties["isAnalyticsRelevant"] = IsAnalyticsRelevant;
        properties["isOperationallyFilterable"] = IsOperationallyFilterable;
    }

    private static DateTimeOffset? Offset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private static bool Bool(IDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out object? value) && (value is true
            || value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.True });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record RegistrationFormFieldTypeChoice(int Id, string Code, string Name);
