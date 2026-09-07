namespace Explore.Blazor.Services;

public static class BffCultureRegistry
{
    private static readonly string[] SupportedCultureCodes = ["en", "fr", "ar"];

    public static IReadOnlyList<string> GetSupportedCultureCodes() => SupportedCultureCodes;

    public static bool TryNormalize(string? code, out string normalized)
    {
        normalized = code?.Trim().ToLowerInvariant() ?? string.Empty;
        return SupportedCultureCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsRtl(string? code) =>
        string.Equals(code, "ar", StringComparison.OrdinalIgnoreCase);
}
