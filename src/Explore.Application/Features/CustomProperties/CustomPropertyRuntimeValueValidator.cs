using System.Text.RegularExpressions;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.CustomProperties;

internal static class CustomPropertyRuntimeValueValidator
{
    public static List<string> ValidateSingle(EventCustomPropertyDefinition definition, SetEventCustomPropertyValueDto value)
    {
        var errors = ValidateDefinitionState(definition.IsActive);
        errors.AddRange(ValidateOrdinal(definition.IsMulti, value.Ordinal));
        errors.AddRange(ValidateValueShape(
            definition.PropertyType,
            definition.IsRequired,
            definition.MinLength,
            definition.MaxLength,
            definition.RegexPattern,
            definition.MinNumber,
            definition.MaxNumber,
            definition.MinDateTime,
            definition.MaxDateTime,
            definition.AllowedUrlSchemes,
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateTimeValue,
            value.OptionId));
        errors.AddRange(ValidateOption(definition.PropertyType, definition.Options, value.OptionId));
        errors.AddRange(ValidateDuplicateExistingValues(definition.IsMulti, definition.Values, value.Ordinal, CustomPropertyValueNormalization.CreateKey(value)));
        return errors;
    }

    public static List<string> ValidateMany(EventCustomPropertyDefinition definition, IReadOnlyCollection<SetEventCustomPropertyValueDto> values)
    {
        var errors = ValidateDefinitionState(definition.IsActive);
        errors.AddRange(ValidateCollection(definition.IsRequired, definition.IsMulti, values.Count));

        var index = 0;
        foreach (var value in values)
        {
            foreach (var error in ValidateValueShape(
                definition.PropertyType,
                definition.IsRequired,
                definition.MinLength,
                definition.MaxLength,
                definition.RegexPattern,
                definition.MinNumber,
                definition.MaxNumber,
                definition.MinDateTime,
                definition.MaxDateTime,
                definition.AllowedUrlSchemes,
                value.TextValue,
                value.NumberValue,
                value.BooleanValue,
                value.DateTimeValue,
                value.OptionId))
            {
                errors.Add($"Value[{index}]: {error}");
            }

            foreach (var error in ValidateOption(definition.PropertyType, definition.Options, value.OptionId))
            {
                errors.Add($"Value[{index}]: {error}");
            }

            index++;
        }

        errors.AddRange(ValidateDuplicateIncomingValues(values.Select(CustomPropertyValueNormalization.CreateKey)));
        return errors;
    }

    public static List<string> ValidateSingle(EventSessionCustomPropertyDefinition definition, SetEventSessionCustomPropertyValueDto value)
    {
        var errors = ValidateDefinitionState(definition.IsActive);
        errors.AddRange(ValidateOrdinal(definition.IsMulti, value.Ordinal));
        errors.AddRange(ValidateValueShape(
            definition.PropertyType,
            definition.IsRequired,
            definition.MinLength,
            definition.MaxLength,
            definition.RegexPattern,
            definition.MinNumber,
            definition.MaxNumber,
            definition.MinDateTime,
            definition.MaxDateTime,
            definition.AllowedUrlSchemes,
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateTimeValue,
            value.OptionId));
        errors.AddRange(ValidateOption(definition.PropertyType, definition.Options, value.OptionId));
        errors.AddRange(ValidateDuplicateExistingValues(definition.IsMulti, definition.Values, value.Ordinal, CustomPropertyValueNormalization.CreateKey(value)));
        return errors;
    }

    public static List<string> ValidateMany(EventSessionCustomPropertyDefinition definition, IReadOnlyCollection<SetEventSessionCustomPropertyValueDto> values)
    {
        var errors = ValidateDefinitionState(definition.IsActive);
        errors.AddRange(ValidateCollection(definition.IsRequired, definition.IsMulti, values.Count));

        var index = 0;
        foreach (var value in values)
        {
            foreach (var error in ValidateValueShape(
                definition.PropertyType,
                definition.IsRequired,
                definition.MinLength,
                definition.MaxLength,
                definition.RegexPattern,
                definition.MinNumber,
                definition.MaxNumber,
                definition.MinDateTime,
                definition.MaxDateTime,
                definition.AllowedUrlSchemes,
                value.TextValue,
                value.NumberValue,
                value.BooleanValue,
                value.DateTimeValue,
                value.OptionId))
            {
                errors.Add($"Value[{index}]: {error}");
            }

            foreach (var error in ValidateOption(definition.PropertyType, definition.Options, value.OptionId))
            {
                errors.Add($"Value[{index}]: {error}");
            }

            index++;
        }

        errors.AddRange(ValidateDuplicateIncomingValues(values.Select(CustomPropertyValueNormalization.CreateKey)));
        return errors;
    }

    private static List<string> ValidateDefinitionState(bool isActive)
    {
        return isActive ? [] : ["Custom property definition is not active."];
    }

    private static List<string> ValidateOrdinal(bool isMulti, int ordinal)
    {
        return !isMulti && ordinal > 0
            ? ["Single-value custom property definitions only accept ordinal 0."]
            : [];
    }

    private static List<string> ValidateCollection(bool isRequired, bool isMulti, int count)
    {
        var errors = new List<string>();

        if (isRequired && count == 0)
        {
            errors.Add("Required custom property definitions must include at least one value.");
        }

        if (!isMulti && count > 1)
        {
            errors.Add("Single-value custom property definitions cannot accept more than one value.");
        }

        return errors;
    }

    private static List<string> ValidateValueShape(
        PropertyType propertyType,
        bool isRequired,
        int? minLength,
        int? maxLength,
        string? regexPattern,
        decimal? minNumber,
        decimal? maxNumber,
        DateTimeOffset? minDateTime,
        DateTimeOffset? maxDateTime,
        string? allowedUrlSchemes,
        string? textValue,
        decimal? numberValue,
        bool? booleanValue,
        DateTimeOffset? dateTimeValue,
        Guid? optionId)
    {
        var errors = new List<string>();
        var hasText = !string.IsNullOrWhiteSpace(textValue);
        var populatedCount = (hasText ? 1 : 0)
            + (numberValue.HasValue ? 1 : 0)
            + (booleanValue.HasValue ? 1 : 0)
            + (dateTimeValue.HasValue ? 1 : 0)
            + (optionId.HasValue ? 1 : 0);

        if (populatedCount == 0)
        {
            errors.Add(isRequired
                ? "Required custom property definitions must include a value."
                : "Custom property values must include exactly one typed value.");
            return errors;
        }

        if (populatedCount > 1)
        {
            errors.Add("Custom property values must include exactly one typed value.");
            return errors;
        }

        switch (propertyType)
        {
            case PropertyType.Text:
                errors.AddRange(ValidateTextValue("TextValue", textValue, minLength, maxLength, regexPattern));
                break;
            case PropertyType.Url:
                errors.AddRange(ValidateTextValue("TextValue", textValue, minLength, maxLength, regexPattern));
                errors.AddRange(ValidateUrlValue(textValue, allowedUrlSchemes));
                break;
            case PropertyType.Number:
                if (!numberValue.HasValue)
                {
                    errors.Add("Number custom properties must use NumberValue.");
                    break;
                }

                if (minNumber.HasValue && numberValue.Value < minNumber.Value)
                {
                    errors.Add($"NumberValue must be greater than or equal to {minNumber.Value}.");
                }

                if (maxNumber.HasValue && numberValue.Value > maxNumber.Value)
                {
                    errors.Add($"NumberValue must be less than or equal to {maxNumber.Value}.");
                }

                break;
            case PropertyType.Boolean:
                if (!booleanValue.HasValue)
                {
                    errors.Add("Boolean custom properties must use BooleanValue.");
                }
                break;
            case PropertyType.DateTime:
                if (!dateTimeValue.HasValue)
                {
                    errors.Add("DateTime custom properties must use DateTimeValue.");
                    break;
                }

                if (minDateTime.HasValue && dateTimeValue.Value < minDateTime.Value)
                {
                    errors.Add($"DateTimeValue must be greater than or equal to {minDateTime.Value:O}.");
                }

                if (maxDateTime.HasValue && dateTimeValue.Value > maxDateTime.Value)
                {
                    errors.Add($"DateTimeValue must be less than or equal to {maxDateTime.Value:O}.");
                }
                break;
            case PropertyType.Option:
                if (!optionId.HasValue)
                {
                    errors.Add("Option custom properties must use OptionId.");
                }
                break;
            default:
                errors.Add("Unsupported custom property type.");
                break;
        }

        return errors;
    }

    private static List<string> ValidateTextValue(string fieldName, string? value, int? minLength, int? maxLength, string? regexPattern)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [$"Text and URL custom properties must use {fieldName}."];
        }

        var trimmedValue = value.Trim();
        var errors = new List<string>();

        if (minLength.HasValue && trimmedValue.Length < minLength.Value)
        {
            errors.Add($"{fieldName} must be at least {minLength.Value} characters long.");
        }

        if (maxLength.HasValue && trimmedValue.Length > maxLength.Value)
        {
            errors.Add($"{fieldName} must be {maxLength.Value} characters or fewer.");
        }

        if (!string.IsNullOrWhiteSpace(regexPattern) && !Regex.IsMatch(trimmedValue, regexPattern))
        {
            errors.Add($"{fieldName} does not match the required pattern.");
        }

        return errors;
    }

    private static List<string> ValidateUrlValue(string? value, string? allowedUrlSchemes)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return ["TextValue must be an absolute URL."];
        }

        var schemes = SplitAllowedUrlSchemes(allowedUrlSchemes);
        return schemes.Count == 0 || schemes.Contains(uri.Scheme)
            ? []
            : [$"TextValue URL scheme '{uri.Scheme}' is not allowed."];
    }

    private static HashSet<string> SplitAllowedUrlSchemes(string? allowedUrlSchemes)
    {
        return string.IsNullOrWhiteSpace(allowedUrlSchemes)
            ? []
            : allowedUrlSchemes
                .Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(scheme => scheme.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ValidateOption(PropertyType propertyType, IReadOnlyCollection<EventCustomPropertyOption> options, Guid? optionId)
    {
        if (propertyType != PropertyType.Option || !optionId.HasValue)
        {
            return [];
        }

        var option = options.FirstOrDefault(candidate => candidate.Id == optionId.Value);
        return option is null || !option.IsActive || option.IsDeleted
            ? ["OptionId must reference an active option on the custom property definition."]
            : [];
    }

    private static List<string> ValidateOption(PropertyType propertyType, IReadOnlyCollection<EventSessionCustomPropertyOption> options, Guid? optionId)
    {
        if (propertyType != PropertyType.Option || !optionId.HasValue)
        {
            return [];
        }

        var option = options.FirstOrDefault(candidate => candidate.Id == optionId.Value);
        return option is null || !option.IsActive || option.IsDeleted
            ? ["OptionId must reference an active option on the custom property definition."]
            : [];
    }

    private static List<string> ValidateDuplicateExistingValues(bool isMulti, IReadOnlyCollection<EventCustomPropertyValue> values, int ordinal, string incomingKey)
    {
        return isMulti && values.Where(value => value.Ordinal != ordinal).Any(value => CustomPropertyValueNormalization.CreateKey(value) == incomingKey)
            ? ["Duplicate normalized values are not allowed for the same definition and event."]
            : [];
    }

    private static List<string> ValidateDuplicateExistingValues(bool isMulti, IReadOnlyCollection<EventSessionCustomPropertyValue> values, int ordinal, string incomingKey)
    {
        return isMulti && values.Where(value => value.Ordinal != ordinal).Any(value => CustomPropertyValueNormalization.CreateKey(value) == incomingKey)
            ? ["Duplicate normalized values are not allowed for the same definition and session."]
            : [];
    }

    private static List<string> ValidateDuplicateIncomingValues(IEnumerable<string> keys)
    {
        return keys.GroupBy(key => key, StringComparer.Ordinal).Any(group => group.Count() > 1)
            ? ["Duplicate normalized values are not allowed for the same definition."]
            : [];
    }
}
