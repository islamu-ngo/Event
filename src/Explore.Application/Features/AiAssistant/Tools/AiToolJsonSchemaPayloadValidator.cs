using System.Globalization;
using System.Text.Json;

namespace Explore.Application.Features.AiAssistant.Tools;

internal static class AiToolJsonSchemaPayloadValidator
{
    public static AiToolValidationResult Validate(JsonElement payload, string schemaJson)
    {
        try
        {
            using var schemaDocument = JsonDocument.Parse(schemaJson);
            var schema = schemaDocument.RootElement;
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return Failure("invalid_action_schema", "AI tool schema must be a JSON object.");
            }

            var requiredResult = ValidateRequiredFields(payload, schema);
            if (!requiredResult.Succeeded)
            {
                return requiredResult;
            }

            if (!schema.TryGetProperty("properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                return AiToolValidationResult.Success();
            }

            foreach (var property in payload.EnumerateObject())
            {
                if (!properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    continue;
                }

                var result = ValidateValue(property.Value, propertySchema);
                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return AiToolValidationResult.Success();
        }
        catch (JsonException)
        {
            return Failure("invalid_action_schema", "AI tool schema must be valid JSON.");
        }
    }

    private static AiToolValidationResult ValidateRequiredFields(JsonElement payload, JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var requiredElement)
            || requiredElement.ValueKind != JsonValueKind.Array)
        {
            return AiToolValidationResult.Success();
        }

        var presentFields = payload.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var required in requiredElement.EnumerateArray())
        {
            if (required.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var requiredName = required.GetString();
            if (!string.IsNullOrWhiteSpace(requiredName) && !presentFields.Contains(requiredName))
            {
                return AiToolValidationResult.ClarificationFailure(
                    "missing_tool_argument",
                    "AI tool payload is missing a required field.",
                    "Please provide the required event draft details before this action can be proposed.",
                    AiToolCorrectionMessages.SchemaExactRetry);
            }
        }

        return AiToolValidationResult.Success();
    }

    private static AiToolValidationResult ValidateValue(JsonElement value, JsonElement schema)
    {
        if (GetBooleanProperty(schema, "x-islamu-hiddenRuntimeContext") == true)
        {
            return Failure("forbidden_tool_argument", "AI tool payload contains a field that is not allowed.");
        }

        var allowedTypes = GetAllowedTypes(schema);
        if (allowedTypes.Count == 0)
        {
            return AiToolValidationResult.Success();
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return allowedTypes.Contains("null", StringComparer.OrdinalIgnoreCase)
                ? AiToolValidationResult.Success()
                : Failure("invalid_tool_argument_type", "AI tool payload contains a value with the wrong type.");
        }

        var type = allowedTypes.FirstOrDefault(candidate => !string.Equals(candidate, "null", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(type))
        {
            return AiToolValidationResult.Success();
        }

        var enumResult = ValidateEnumValue(value, schema);
        if (!enumResult.Succeeded)
        {
            return enumResult;
        }

        return type switch
        {
            "string" => ValidateStringValue(value, schema),
            "integer" => ValidateIntegerValue(value),
            "number" => ValidateNumberValue(value, schema),
            "boolean" => ValidateBooleanValue(value),
            "array" => ValidateArrayValue(value, schema),
            "object" => ValidateObjectValue(value, schema),
            _ => AiToolValidationResult.Success()
        };
    }

    private static AiToolValidationResult ValidateStringValue(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return Failure("invalid_tool_argument_type", "AI tool payload contains a value with the wrong type.");
        }

        var text = value.GetString() ?? string.Empty;
        if (TryGetInt32Property(schema, "maxLength", out var maxLength) && text.Length > maxLength)
        {
            return Failure("invalid_tool_argument_value", "AI tool payload contains a value outside the allowed bounds.");
        }

        var format = GetStringProperty(schema, "format");
        if (!ValidateStringFormat(text, format))
        {
            return Failure("invalid_tool_argument_format", "AI tool payload contains a value with an invalid format.");
        }

        return AiToolValidationResult.Success();
    }

    private static AiToolValidationResult ValidateObjectValue(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return Failure("invalid_tool_argument_type", "AI tool payload contains a value with the wrong type.");
        }

        var requiredResult = ValidateRequiredFields(value, schema);
        if (!requiredResult.Succeeded)
        {
            return requiredResult;
        }

        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return AiToolValidationResult.Success();
        }

        var disallowAdditionalProperties = schema.TryGetProperty("additionalProperties", out var additionalProperties)
            && additionalProperties.ValueKind is JsonValueKind.False;

        foreach (var property in value.EnumerateObject())
        {
            if (!properties.TryGetProperty(property.Name, out var propertySchema))
            {
                if (disallowAdditionalProperties)
                {
                    return Failure("unsupported_tool_argument", "AI tool payload contains an unsupported field.");
                }

                continue;
            }

            var result = ValidateValue(property.Value, propertySchema);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return AiToolValidationResult.Success();
    }

    private static AiToolValidationResult ValidateIntegerValue(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _)
            ? AiToolValidationResult.Success()
            : Failure("invalid_tool_argument_type", "AI tool payload contains a value with the wrong type.");

    private static AiToolValidationResult ValidateNumberValue(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            return Failure("invalid_tool_argument_type", "AI tool payload contains a value with the wrong type.");
        }

        if (TryGetDecimalProperty(schema, "minimum", out var minimum) && number < minimum)
        {
            return Failure("invalid_tool_argument_value", "AI tool payload contains a value outside the allowed bounds.");
        }

        return AiToolValidationResult.Success();
    }

    private static AiToolValidationResult ValidateEnumValue(JsonElement value, JsonElement schema)
    {
        if (!schema.TryGetProperty("enum", out var enumElement) || enumElement.ValueKind != JsonValueKind.Array)
        {
            return AiToolValidationResult.Success();
        }

        foreach (var allowed in enumElement.EnumerateArray())
        {
            if (JsonElementsEqual(value, allowed))
            {
                return AiToolValidationResult.Success();
            }
        }

        return Failure("invalid_tool_argument_value", "AI tool payload contains a value outside the allowed bounds.");
    }

    private static bool JsonElementsEqual(JsonElement first, JsonElement second)
    {
        if (first.ValueKind != second.ValueKind)
        {
            return false;
        }

        return first.ValueKind switch
        {
            JsonValueKind.String => string.Equals(first.GetString(), second.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => first.TryGetDecimal(out var firstNumber)
                && second.TryGetDecimal(out var secondNumber)
                && firstNumber == secondNumber,
            JsonValueKind.True or JsonValueKind.False => first.GetBoolean() == second.GetBoolean(),
            JsonValueKind.Null => true,
            _ => string.Equals(first.GetRawText(), second.GetRawText(), StringComparison.Ordinal)
        };
    }

    private static IReadOnlyList<string> GetAllowedTypes(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeElement))
        {
            return [];
        }

        if (typeElement.ValueKind == JsonValueKind.String)
        {
            var type = typeElement.GetString();
            return string.IsNullOrWhiteSpace(type) ? [] : [type];
        }

        if (typeElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return typeElement
            .EnumerateArray()
            .Where(type => type.ValueKind == JsonValueKind.String)
            .Select(type => type.GetString())
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type!)
            .ToList();
    }

    private static bool ValidateStringFormat(string text, string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return true;
        }

        return format.ToLowerInvariant() switch
        {
            "uuid" => Guid.TryParse(text, out _),
            "date" => DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "time" => TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            _ => true
        };
    }

    private static AiToolValidationResult ValidateBooleanValue(JsonElement value)
        => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? AiToolValidationResult.Success()
            : Failure("invalid_tool_argument_type", "AI tool payload contains a value with the wrong type.");

    private static AiToolValidationResult ValidateArrayValue(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Failure("invalid_tool_argument_type", "AI tool payload contains a value with the wrong type.");
        }

        if (!schema.TryGetProperty("items", out var itemSchema) || itemSchema.ValueKind != JsonValueKind.Object)
        {
            return AiToolValidationResult.Success();
        }

        foreach (var item in value.EnumerateArray())
        {
            var result = ValidateValue(item, itemSchema);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return AiToolValidationResult.Success();
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? GetBooleanProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static bool TryGetInt32Property(JsonElement element, string propertyName, out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryGetDecimalProperty(JsonElement element, string propertyName, out decimal value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out value);
    }

    private static AiToolValidationResult Failure(string failureCode, string failureMessage)
        => AiToolValidationResult.Failure(failureCode, failureMessage, AiToolCorrectionMessages.SchemaExactRetry);
}
