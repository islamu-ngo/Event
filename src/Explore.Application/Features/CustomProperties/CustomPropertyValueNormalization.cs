using System.Globalization;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Domain;

namespace Explore.Application.Features.CustomProperties;

internal static class CustomPropertyValueNormalization
{
    public static string CreateKey(SetEventCustomPropertyValueDto value)
    {
        return CreateKey(value.OptionId, value.TextValue, value.NumberValue, value.BooleanValue, value.DateTimeValue);
    }

    public static string CreateKey(SetEventSessionCustomPropertyValueDto value)
    {
        return CreateKey(value.OptionId, value.TextValue, value.NumberValue, value.BooleanValue, value.DateTimeValue);
    }

    public static string CreateKey(EventCustomPropertyValue value)
    {
        return CreateKey(value.OptionId, value.TextValue, value.NumberValue, value.BooleanValue, value.DateTimeValue);
    }

    public static string CreateKey(EventSessionCustomPropertyValue value)
    {
        return CreateKey(value.OptionId, value.TextValue, value.NumberValue, value.BooleanValue, value.DateTimeValue);
    }

    private static string CreateKey(Guid? optionId, string? textValue, decimal? numberValue, bool? booleanValue, DateTimeOffset? dateTimeValue)
    {
        if (optionId.HasValue)
        {
            return $"option:{optionId.Value:D}";
        }

        if (textValue is not null)
        {
            return $"text:{textValue.Trim().ToUpperInvariant()}";
        }

        if (numberValue.HasValue)
        {
            return $"number:{numberValue.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        if (booleanValue.HasValue)
        {
            return $"bool:{booleanValue.Value}";
        }

        if (dateTimeValue.HasValue)
        {
            return $"datetime:{dateTimeValue.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}";
        }

        return "empty:";
    }
}
