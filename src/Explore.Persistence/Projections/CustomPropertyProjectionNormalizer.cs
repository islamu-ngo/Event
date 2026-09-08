using System.Globalization;
using Explore.Domain.Enums;

namespace Explore.Persistence.Projections;

internal static class CustomPropertyProjectionNormalizer
{
    public static string? Compute(
        PropertyType propertyType,
        string? textValue,
        decimal? numberValue,
        bool? booleanValue,
        DateTimeOffset? dateTimeValue,
        string? optionValue)
    {
        return propertyType switch
        {
            PropertyType.Text => Trim(textValue)?.ToLowerInvariant(),
            PropertyType.Url => Trim(textValue)?.ToLowerInvariant(),
            PropertyType.Number => numberValue?.ToString(CultureInfo.InvariantCulture),
            PropertyType.Boolean => booleanValue switch
            {
                true => "true",
                false => "false",
                _ => null,
            },
            PropertyType.DateTime => dateTimeValue?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            PropertyType.Option => Trim(optionValue)?.ToLowerInvariant(),
            _ => null,
        };
    }

    private static string? Trim(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
