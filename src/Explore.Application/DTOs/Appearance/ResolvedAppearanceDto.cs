namespace Explore.Application.DTOs.Appearance;

public sealed record ResolvedAppearanceDto
{
    public Guid? ActiveProfileId { get; init; }
    public Guid? SourcePresetId { get; init; }
    public string? SourcePresetKey { get; init; }

    /// <summary>
    /// How the resolved appearance was determined. Maps to AppearanceResolutionSource enum values.
    /// </summary>
    public string ResolutionSource { get; init; } = default!;

    public string ThemeMode { get; init; } = "system";

    /// <summary>
    /// Nullable because the server cannot fully resolve System mode —
    /// the Blazor client is the runtime authority for the final dark/light decision.
    /// </summary>
    public bool? ServerEffectiveDarkMode { get; init; }

    public string Direction { get; init; } = "auto";
    public string Language { get; init; } = "en";

    public ResolvedThemeDto Theme { get; init; } = default!;

    public AppearanceCapabilitiesDto Capabilities { get; init; } = default!;
}
