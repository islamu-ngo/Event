namespace Explore.Application.Settings;

using System.Globalization;
using System.Text.Json;
using Explore.Domain;

/// <summary>
/// Centralizes all setting value serialization/deserialization logic.
/// All setting values are stored as JSON-serialized strings in the database.
/// </summary>
public static class SettingValueSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Deserializes a JSON-serialized setting value to the target type.
    /// Returns <paramref name="defaultValue"/> when the raw value is null, empty, or malformed.
    /// </summary>
    public static T Deserialize<T>(string? rawValue, T defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return defaultValue;

        try
        {
            var result = JsonSerializer.Deserialize<T>(rawValue, Options);
            return result ?? defaultValue;
        }
        catch
        {
            return TryParseFallback(rawValue, defaultValue);
        }
    }

    /// <summary>
    /// Serializes a value to its JSON string representation for storage.
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static string ToDisplayValue(
        string? rawValue,
        SettingValueType valueType,
        string defaultRawValue)
    {
        return TryToDisplayValue(rawValue, valueType, out string? displayValue)
            ? displayValue
            : TryToDisplayValue(defaultRawValue, valueType, out displayValue)
                ? displayValue
                : SafeDisplayDefault(valueType);
    }

    /// <summary>
    /// Deserializes a string setting value, trimming JSON quotes if present.
    /// </summary>
    public static string DeserializeString(string? rawValue, string defaultValue = "")
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return defaultValue;

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue, Options);
            return string.IsNullOrWhiteSpace(deserialized) ? defaultValue : deserialized;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }

    /// <summary>
    /// Deserializes an integer setting value with fallback parsing.
    /// </summary>
    public static int DeserializeInt(string? rawValue, int defaultValue = 0)
        => Deserialize(rawValue, defaultValue);

    /// <summary>
    /// Deserializes a long integer setting value with fallback parsing.
    /// </summary>
    public static long DeserializeLong(string? rawValue, long defaultValue = 0)
        => Deserialize(rawValue, defaultValue);

    /// <summary>
    /// Deserializes a boolean setting value with fallback parsing.
    /// </summary>
    public static bool DeserializeBool(string? rawValue, bool defaultValue = false)
        => Deserialize(rawValue, defaultValue);

    /// <summary>
    /// Deserializes a decimal setting value with fallback parsing.
    /// </summary>
    public static decimal DeserializeDecimal(string? rawValue, decimal defaultValue = 0m)
        => Deserialize(rawValue, defaultValue);

    private static bool TryToDisplayValue(
        string? rawValue,
        SettingValueType valueType,
        out string displayValue)
    {
        displayValue = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        string candidate = rawValue.Trim();
        switch (valueType)
        {
            case SettingValueType.String:
                try
                {
                    string? value = JsonSerializer.Deserialize<string>(candidate, Options);
                    if (value is not null)
                    {
                        displayValue = value;
                        return true;
                    }
                }
                catch (JsonException)
                {
                    if (candidate.StartsWith('"') || candidate.EndsWith('"'))
                    {
                        return false;
                    }
                }

                displayValue = candidate;
                return true;

            case SettingValueType.DateTime:
                if (TryParseDateTime(candidate, out DateTime dateTime))
                {
                    displayValue = dateTime.ToString("O", CultureInfo.InvariantCulture);
                    return true;
                }

                return false;

            case SettingValueType.Boolean:
                if (TryParseJsonScalar(candidate, out bool booleanValue))
                {
                    displayValue = booleanValue ? "true" : "false";
                    return true;
                }

                return false;

            case SettingValueType.Integer:
                if (TryParseJsonScalar(candidate, out int integerValue))
                {
                    displayValue = integerValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                return false;

            case SettingValueType.Long:
                if (TryParseJsonScalar(candidate, out long longValue))
                {
                    displayValue = longValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                return false;

            case SettingValueType.Decimal:
                if (TryParseJsonScalar(candidate, out decimal decimalValue))
                {
                    displayValue = decimalValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                return false;

            case SettingValueType.Json:
                try
                {
                    using JsonDocument document = JsonDocument.Parse(candidate);
                    displayValue = candidate;
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }

            default:
                return false;
        }
    }

    private static bool TryParseDateTime(string candidate, out DateTime value)
    {
        try
        {
            value = JsonSerializer.Deserialize<DateTime>(candidate, Options);
            return true;
        }
        catch (JsonException)
        {
            return DateTime.TryParse(
                candidate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out value);
        }
    }

    private static bool TryParseJsonScalar<T>(string candidate, out T value)
        where T : struct
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(candidate, Options);
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static string SafeDisplayDefault(SettingValueType valueType) => valueType switch
    {
        SettingValueType.Boolean => "false",
        SettingValueType.Integer or SettingValueType.Long or SettingValueType.Decimal => "0",
        SettingValueType.Json => "{}",
        _ => string.Empty
    };

    private static T TryParseFallback<T>(string rawValue, T defaultValue)
    {
        if (typeof(T) == typeof(int) && int.TryParse(rawValue, out var intVal))
            return (T)(object)intVal;

        if (typeof(T) == typeof(bool) && bool.TryParse(rawValue, out var boolVal))
            return (T)(object)boolVal;

        if (typeof(T) == typeof(decimal) && decimal.TryParse(rawValue, out var decVal))
            return (T)(object)decVal;

        if (typeof(T) == typeof(string))
            return (T)(object)rawValue.Trim('"');

        if (typeof(T) == typeof(DateTime) && DateTime.TryParse(rawValue, out var dtVal))
            return (T)(object)dtVal;

        return defaultValue;
    }
}
