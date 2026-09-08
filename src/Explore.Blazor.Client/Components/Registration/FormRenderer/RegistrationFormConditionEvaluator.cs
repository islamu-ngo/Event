using System.Globalization;
using System.Text.Json;

namespace Explore.Blazor.Client.Components.Registration.FormRenderer;

internal static class RegistrationFormConditionEvaluator
{
    public static bool Evaluate(
        object condition,
        IReadOnlyDictionary<string, object?> answers)
    {
        JsonElement node = JsonSerializer.SerializeToElement(condition);
        string operation = Property(node, "operator").GetString() ?? string.Empty;
        object? answer = FieldAnswer(node, answers);
        return operation switch
        {
            "equals" => EqualsScalar(answer, Property(node, "value")),
            "notEquals" => answer is not null && !EqualsScalar(answer, Property(node, "value")),
            "in" => Property(node, "values") is { ValueKind: JsonValueKind.Array } values && values.EnumerateArray().Any(value => EqualsScalar(answer, value)),
            "contains" => answer is IEnumerable<string> selected && Property(node, "value") is { ValueKind: JsonValueKind.Object } value &&
                          selected.Any(item => EqualsScalar(item, value)),
            "exists" => RegistrationFormValue.IsAnswered(answer),
            "compare" => Compare(answer, Property(node, "value"), Property(node, "comparison").GetString()),
            "all" => Property(node, "conditions") is { ValueKind: JsonValueKind.Array } all && all.EnumerateArray().All(child => Evaluate(child, answers)),
            "any" => Property(node, "conditions") is { ValueKind: JsonValueKind.Array } any && any.EnumerateArray().Any(child => Evaluate(child, answers)),
            "not" => Property(node, "condition") is { ValueKind: JsonValueKind.Object } child && !Evaluate(child, answers),
            _ => false
        };
    }

    private static object? FieldAnswer(
        JsonElement condition,
        IReadOnlyDictionary<string, object?> answers)
    {
        string? fieldNamespace = Property(condition, "fieldNamespace").GetString();
        string? fieldKey = Property(condition, "fieldKey").GetString();
        return fieldNamespace is null || fieldKey is null ? null : answers.GetValueOrDefault($"{fieldNamespace}.{fieldKey}");
    }

    private static bool EqualsScalar(object? answer, JsonElement expected)
    {
        if (expected.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return answer is null;
        return Property(expected, "type").GetString() switch
        {
            "null" => answer is null,
            "text" => string.Equals(Convert.ToString(answer, CultureInfo.InvariantCulture), Property(expected, "textValue").GetString(), StringComparison.Ordinal),
            "boolean" => TryBoolean(answer, out bool boolean) && Property(expected, "booleanValue").GetBoolean() == boolean,
            "number" => TryDecimal(answer, out decimal number) && Property(expected, "numberValue").GetDecimal() == number,
            "date" => TryDate(answer, out DateOnly date) && DateOnly.TryParse(Property(expected, "dateValue").GetString(), out DateOnly expectedDate) && date == expectedDate,
            _ => false
        };
    }

    private static bool Compare(object? answer, JsonElement expected, string? comparison)
    {
        int? result = Property(expected, "type").GetString() switch
        {
            "number" when TryDecimal(answer, out decimal number) && Property(expected, "numberValue").TryGetDecimal(out decimal expectedNumber) => number.CompareTo(expectedNumber),
            "date" when TryDate(answer, out DateOnly date) && DateOnly.TryParse(Property(expected, "dateValue").GetString(), out DateOnly expectedDate) => date.CompareTo(expectedDate),
            _ => null
        };
        return result is { } value && comparison switch
        {
            "lessThan" => value < 0,
            "lessThanOrEqual" => value <= 0,
            "greaterThan" => value > 0,
            "greaterThanOrEqual" => value >= 0,
            "equal" => value == 0,
            "notEqual" => value != 0,
            _ => false
        };
    }

    private static bool TryBoolean(object? value, out bool result) =>
        value is bool boolean ? (result = boolean) == boolean : bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result);

    private static bool TryDecimal(object? value, out decimal result) =>
        decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static bool TryDate(object? value, out DateOnly result) =>
        DateOnly.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    private static JsonElement Property(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out JsonElement value) ? value : default;
}
