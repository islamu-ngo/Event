namespace Explore.Domain.Common.Localization;

/// <summary>
/// Flat localization bundles are bounded text data, not executable templates.
/// Character budgets include both keys and values before JSON escaping.
/// </summary>
public static class LocalizationBundlePolicy
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const int MaximumEntries = 16_384;
    public const int MaximumKeyCharacters = 512;
    public const int MaximumValueCharacters = 16_384;
    public const int MaximumAggregateCharacters = 524_288;

    public static void Validate(IReadOnlyDictionary<string, string> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);
        if (translations.Count > MaximumEntries)
            throw new ArgumentException("Localization bundle entry limit exceeded.", nameof(translations));

        int characters = 0;
        foreach ((string key, string value) in translations)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaximumKeyCharacters
                || key.Any(char.IsWhiteSpace) || !HasAllowedPrefix(key)
                || key.Split('.').Any(static segment => segment.Length == 0)
                || value is null || value.Length > MaximumValueCharacters)
            {
                throw new ArgumentException("Localization bundle must contain bounded flat translation text.", nameof(translations));
            }

            characters += key.Length + value.Length;
            if (characters > MaximumAggregateCharacters)
                throw new ArgumentException("Localization bundle character limit exceeded.", nameof(translations));
        }
    }

    private static bool HasAllowedPrefix(string key) =>
        key.StartsWith("ui.", StringComparison.Ordinal)
        || key.StartsWith("lookup.", StringComparison.Ordinal)
        || key.StartsWith("payment.", StringComparison.Ordinal);
}
