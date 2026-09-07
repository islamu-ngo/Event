namespace Explore.Application.DTOs.Appearance;

public sealed record AppearanceCapabilitiesDto
{
    public bool CanEditProfile { get; init; }
    public bool CanCreateCustomProfile { get; init; }
    public bool CanClonePreset { get; init; }
    public bool CanDeleteProfile { get; init; }
}
