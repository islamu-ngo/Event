namespace Explore.Application.Localization;

public static class TranslationKeys
{
    private const char SegmentDelimiter = '.';

    public static string Lookup(string entityType, string masterCode, string field)
    {
        string normalizedEntityType = NormalizeSegment(entityType, nameof(entityType), lowercase: true);
        string normalizedMasterCode = NormalizeSegment(masterCode, nameof(masterCode), lowercase: false);
        string normalizedField = NormalizeSegment(field, nameof(field), lowercase: true);

        return $"lookup.{normalizedEntityType}.{normalizedMasterCode}.{normalizedField}";
    }

    private static string NormalizeSegment(string value, string parameterName, bool lowercase)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Translation key segments must not be blank.", parameterName);
        }

        string normalized = value.Trim();

        if (normalized.Contains(SegmentDelimiter) || normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Translation key segments must not contain dots or whitespace.", parameterName);
        }

        return lowercase ? normalized.ToLowerInvariant() : normalized;
    }
}
