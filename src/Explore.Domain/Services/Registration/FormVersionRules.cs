using System.Globalization;
using Explore.Domain.Common.Localization;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public static class FormVersionRules
{
    public static string NormalizeNamespace(string value)
    {
        string normalized = CustomPropertyIdentity.NormalizeNamespace(value);
        return normalized.Length == 0
            ? throw new ArgumentException("Namespace must contain letters or digits.", nameof(value))
            : normalized;
    }

    public static string NormalizeKey(string value)
    {
        string normalized = CustomPropertyIdentity.NormalizeKey(value);
        return normalized.Length == 0
            ? throw new ArgumentException("Key must contain letters or digits.", nameof(value))
            : normalized;
    }

    public static string NormalizeLanguageTag(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(value.Trim());
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException("Language tag must be a valid supported BCP-47 tag.", nameof(value), exception);
        }

        string baseLanguage = culture.TwoLetterISOLanguageName;
        if (!CultureRegistry.Contains(baseLanguage))
        {
            throw new ArgumentException("Language tag is not supported by the platform culture registry.", nameof(value));
        }

        return culture.Name;
    }

    public static void ValidateGovernance(
        RegistrationFieldTypeEnum fieldType,
        int retentionPolicyId,
        RegistrationOrganizerVisibilityEnum organizerVisibility,
        bool requiresExplicitConsent,
        bool isProviderTransferAllowed,
        bool isExportable = false,
        string? exportPurposeCode = null,
        bool isAnalyticsRelevant = false,
        bool isOperationallyFilterable = false)
    {
        if (!Enum.IsDefined(fieldType))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldType));
        }

        if (retentionPolicyId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionPolicyId));
        }

        if (!Enum.IsDefined(organizerVisibility))
        {
            throw new ArgumentOutOfRangeException(nameof(organizerVisibility));
        }

        if (fieldType == RegistrationFieldTypeEnum.Consent && !requiresExplicitConsent)
        {
            throw new ArgumentException("Consent fields require explicit consent evidence.", nameof(requiresExplicitConsent));
        }

        if (organizerVisibility == RegistrationOrganizerVisibilityEnum.Hidden && isProviderTransferAllowed)
        {
            throw new ArgumentException("Organizer-hidden fields cannot be transferred to a provider.", nameof(isProviderTransferAllowed));
        }

        if (isExportable != !string.IsNullOrWhiteSpace(exportPurposeCode))
        {
            throw new ArgumentException("Exportable fields require one bounded export purpose code and non-exportable fields must not carry one.", nameof(exportPurposeCode));
        }

        if (exportPurposeCode is { Length: > 100 } || exportPurposeCode?.Any(char.IsControl) == true)
        {
            throw new ArgumentOutOfRangeException(nameof(exportPurposeCode));
        }

        if ((isAnalyticsRelevant || isOperationallyFilterable) && !IsAggregatableFieldType(fieldType))
        {
            throw new ArgumentException("Analytics and operational filters require categorical, boolean, numeric, or date fields.");
        }

        if ((isAnalyticsRelevant || isOperationallyFilterable) && requiresExplicitConsent)
        {
            throw new ArgumentException("Explicit-consent fields cannot be used for analytics or operational filters.");
        }
    }

    public static bool IsAggregatableFieldType(RegistrationFieldTypeEnum fieldType) => fieldType is
        RegistrationFieldTypeEnum.Integer or
        RegistrationFieldTypeEnum.Decimal or
        RegistrationFieldTypeEnum.Boolean or
        RegistrationFieldTypeEnum.Date or
        RegistrationFieldTypeEnum.SingleChoice or
        RegistrationFieldTypeEnum.MultipleChoice or
        RegistrationFieldTypeEnum.Rating;

    public static void ValidateConstraints(
        int? minLength,
        int? maxLength,
        decimal? minNumber,
        decimal? maxNumber,
        DateTimeOffset? minDateTime,
        DateTimeOffset? maxDateTime)
    {
        if (minLength is < 0 || maxLength is < 0 ||
            minLength is not null && maxLength is not null && minLength > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(minLength), "Length bounds must be non-negative and ordered.");
        }

        if (minNumber is not null && maxNumber is not null && minNumber > maxNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(minNumber), "Number bounds must be ordered.");
        }

        if (minDateTime is not null && maxDateTime is not null && minDateTime > maxDateTime)
        {
            throw new ArgumentOutOfRangeException(nameof(minDateTime), "Date/time bounds must be ordered.");
        }
    }

    public static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }
}
