using System.Text;

namespace Explore.Domain.Constants;

public static class CustomPropertyIdentity
{
    public static string NormalizeNamespace(string value)
    {
        return NormalizeSegments(value, '.');
    }

    public static string NormalizeKey(string value)
    {
        return NormalizeSegments(value, '_');
    }

    private static string NormalizeSegments(string value, char separator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if ((character == '.' || character == '-' || character == '_' || char.IsWhiteSpace(character)) && !previousWasSeparator)
            {
                normalized.Append(separator);
                previousWasSeparator = true;
            }
        }

        return normalized.ToString().Trim(separator);
    }
}
