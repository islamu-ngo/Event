namespace Explore.Domain;

internal static class EventReportGuards
{
    public static void RequireGuid(Guid value, string parameterName, string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    public static string NormalizeRequired(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return NormalizeLength(value, maxLength, parameterName);
    }

    public static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizeLength(value, maxLength, parameterName);
    }

    public static void RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value is not supported.");
        }
    }

    public static void RequireScore(decimal? score, string parameterName)
    {
        if (score is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(parameterName, score, "Score must be between 0 and 1.");
        }
    }

    private static string NormalizeLength(string value, int maxLength, string parameterName)
    {
        string normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}
