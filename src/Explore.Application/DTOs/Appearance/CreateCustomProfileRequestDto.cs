namespace Explore.Application.DTOs.Appearance;

public sealed record CreateCustomProfileRequestDto
{
    public required string Name { get; init; }
    public string ThemeMode { get; init; } = "system";

    /// <summary>The natural/neutral color (determines surface, background, text, etc.).</summary>
    public required string NaturalColor { get; init; }

    /// <summary>The brand/accent color (determines primary, secondary, appbar, etc.).</summary>
    public required string BrandColor { get; init; }
}
