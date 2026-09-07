namespace Explore.Application.Contracts.Services;

using Explore.Application.DTOs.Appearance;

public interface IAppearanceResolutionService
{
    /// <summary>
    /// Resolves the full appearance state for the current user in the current tenant context.
    /// Walks the fallback chain and returns a fully populated ResolvedAppearanceDto.
    /// </summary>
    Task<ResolvedAppearanceDto> ResolveForCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available theme presets (platform + tenant catalogs) for the current tenant.
    /// </summary>
    Task<IReadOnlyList<AvailablePresetDto>> GetAvailablePresetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user's appearance profiles for the current tenant scope.
    /// </summary>
    Task<IReadOnlyList<UserAppearanceProfileDto>> GetUserProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones a preset into a user-owned profile and optionally activates it.
    /// Avoids duplicate clones by checking for an existing profile from the same preset.
    /// </summary>
    Task<UserAppearanceProfileDto> ClonePresetAsync(Guid presetId, string? name, bool activate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a fully custom user appearance profile from natural + brand color inputs.
    /// </summary>
    Task<UserAppearanceProfileDto> CreateCustomProfileAsync(CreateCustomProfileRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active profile for the current user/scope.
    /// </summary>
    Task SetActiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the theme mode (light/dark/system/lighthighcontrast/darkhighcontrast/custom) without changing the active profile.
    /// </summary>
    Task SetThemeModeAsync(string mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user-owned profile's palette or metadata.
    /// </summary>
    Task<UserAppearanceProfileDto> UpdateProfileAsync(Guid profileId, UpdateAppearanceProfileRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a complete palette from natural + brand color inputs.
    /// </summary>
    UiThemePaletteDto GeneratePalette(string naturalColor, string brandColor, bool isDark);

    /// <summary>
    /// Archives a user-owned profile, hiding it from the quick switcher without deletion.
    /// </summary>
    Task ArchiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Duplicates a user-owned profile with an optional name override.
    /// </summary>
    Task<UserAppearanceProfileDto> DuplicateProfileAsync(Guid profileId, string? name, CancellationToken cancellationToken = default);
}
