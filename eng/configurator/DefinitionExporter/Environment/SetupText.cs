namespace ISLAMU.Event.DefinitionExporter;

internal static class SetupText
{
    internal static string Identifier(string value, string parameterName) =>
        Normalize(value, parameterName, allowDot: false);

    internal static string SectionKey(string value, string parameterName) =>
        Normalize(value, parameterName, allowDot: true);

    private static string Normalize(string value, string parameterName, bool allowDot)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length is < 1 or > 128
            || normalized.Any(character => !IsAllowed(character, allowDot)))
        {
            throw new ArgumentException("Identifier format is invalid.", parameterName);
        }

        return normalized;
    }

    private static bool IsAllowed(char character, bool allowDot) =>
        character is >= 'a' and <= 'z'
        || character is >= '0' and <= '9'
        || character is '-' or '_'
        || allowDot && character == '.';
}
