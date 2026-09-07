using System.Collections.Frozen;

namespace Explore.Blazor.Client.Localization;

public sealed record CultureEntry(
    string Code,
    string DisplayName,
    string NativeName,
    string Flag,
    bool IsRtl);

public static class CultureRegistry
{
    private static readonly CultureEntry[] Entries =
    [
        new(Code: "en", DisplayName: "English", NativeName: "English", Flag: "🇺🇸", IsRtl: false),
        new(Code: "fr", DisplayName: "French", NativeName: "Français", Flag: "🇫🇷", IsRtl: false),
        new(Code: "ar", DisplayName: "Arabic", NativeName: "العربية", Flag: "🇸🇦", IsRtl: true),
    ];

    private static readonly FrozenDictionary<string, CultureEntry> EntriesByCode =
        Entries.ToFrozenDictionary(entry => entry.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CultureEntry> GetAll() => Entries.ToArray();

    public static bool TryGetEntry(string? code, out CultureEntry entry)
    {
        var normalized = Normalize(code);
        if (normalized.Length == 0)
        {
            entry = null!;
            return false;
        }

        return EntriesByCode.TryGetValue(normalized, out entry!);
    }

    public static bool Contains(string? code) => TryGetEntry(code, out _);

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length != 2)
            return string.Empty;

        return normalized.All(character => character is >= 'a' and <= 'z')
            ? normalized
            : string.Empty;
    }
}
