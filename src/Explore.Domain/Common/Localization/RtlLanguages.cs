namespace Explore.Domain.Common.Localization;

/// <summary>
/// Answers the one question: "is this code RTL?". Delegates to <see cref="CultureRegistry"/>
/// so RTL membership stays aligned with the allowlist.
/// </summary>
public static class RtlLanguages
{
    /// <summary>
    /// Returns true when <paramref name="code"/> resolves to a registered culture with <c>IsRtl = true</c>.
    /// Unknown or unregistered codes always return false — callers should treat unknown as LTR.
    /// </summary>
    public static bool IsRtl(string? code) =>
        CultureRegistry.TryGetEntry(code, out var entry) && entry.IsRtl;
}
