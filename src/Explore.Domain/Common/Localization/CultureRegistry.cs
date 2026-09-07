using System.Collections.Frozen;

namespace Explore.Domain.Common.Localization;

/// <summary>
/// Static, compile-time registry of every culture the codebase knows how to handle.
/// <para>
/// This is the <b>allowlist</b>. A culture that is not listed here cannot be served by the app — period.
/// Instance admins narrow this set further via the <c>localization.enabled_languages</c> governance key,
/// but they can never add a culture that is not in the registry.
/// </para>
/// <para>
/// Display metadata (native name, flag, RTL flag) lives here too so all layers (Blazor, API, middleware)
/// see the same values without duplicating dictionaries. Placed in <c>Explore.Domain</c> so the Blazor
/// WASM client can depend on it without pulling the Application layer's heavyweight dependencies.
/// </para>
/// </summary>
public static class CultureRegistry
{
    private static readonly FrozenDictionary<string, CultureEntry> Entries =
        new CultureEntry[]
        {
            new(Code: "en", DisplayName: "English", NativeName: "English",  Flag: "🇺🇸", IsRtl: false),
            new(Code: "fr", DisplayName: "French",  NativeName: "Français", Flag: "🇫🇷", IsRtl: false),
            new(Code: "ar", DisplayName: "Arabic",  NativeName: "العربية",  Flag: "🇸🇦", IsRtl: true),
        }.ToFrozenDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns every registered culture in a stable order (en, fr, ar).
    /// </summary>
    public static IReadOnlyList<CultureEntry> GetAll() => Entries.Values.ToArray();

    /// <summary>
    /// Attempts to resolve a culture entry by code. Input is normalised (trimmed + lowercased).
    /// </summary>
    public static bool TryGetEntry(string? code, out CultureEntry entry)
    {
        var normalised = Normalize(code);
        if (normalised.Length == 0)
        {
            entry = null!;
            return false;
        }

        return Entries.TryGetValue(normalised, out entry!);
    }

    /// <summary>
    /// Returns true when the code maps to a registered culture.
    /// </summary>
    public static bool Contains(string? code) => TryGetEntry(code, out _);

    /// <summary>
    /// Normalises an input code: trims whitespace, lowercases, returns empty when obviously invalid.
    /// Does NOT validate against the registry — use <see cref="TryGetEntry"/> or <see cref="Contains"/> for that.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var trimmed = code.Trim().ToLowerInvariant();

        // Defensive: reject anything that could not possibly be a bare ISO 639-1 code.
        // We accept exactly 2-letter codes for v1; "en-us" etc. are rejected here so they can be
        // surfaced as governance/seed errors rather than silently succeeding.
        if (trimmed.Length != 2)
            return string.Empty;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c < 'a' || c > 'z')
                return string.Empty;
        }

        return trimmed;
    }
}
