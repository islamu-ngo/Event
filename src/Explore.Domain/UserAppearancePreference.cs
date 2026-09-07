namespace Explore.Domain;

using Explore.Domain.Enums;

public class UserAppearancePreference
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// Null means this is the user's global preference;
    /// non-null means this is a tenant-specific preference.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// The currently active appearance profile for this user/scope.
    /// </summary>
    public Guid ActiveProfileId { get; set; }

    /// <summary>
    /// Navigation to the active profile.
    /// </summary>
    public UserAppearanceProfile ActiveProfile { get; set; } = default!;

    public AppearanceThemeMode ThemeMode { get; set; } = AppearanceThemeMode.System;

    public string Direction { get; set; } = "auto";

    public string Language { get; set; } = "en";
}
