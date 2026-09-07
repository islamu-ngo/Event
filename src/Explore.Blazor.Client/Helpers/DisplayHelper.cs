namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Provides display-related utility methods for generating initials from names.
/// </summary>
public static class DisplayHelper
{
    /// <summary>
    /// Extracts up to two initials from a display name.
    /// Handles single-word names, multi-word names, and null/empty input.
    /// </summary>
    /// <param name="name">The display name to extract initials from.</param>
    /// <param name="fallback">Fallback character when name is null/empty. Defaults to "?".</param>
    /// <returns>One or two uppercase initials, or the fallback character.</returns>
    public static string GetInitials(string? name, string fallback = "?")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => fallback,
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{words[0][0]}{words[1][0]}".ToUpperInvariant()
        };
    }

    /// <summary>
    /// Extracts initials from separate first and last name fields.
    /// Falls back to username if both names are empty.
    /// </summary>
    public static string GetInitials(string? firstName, string? lastName, string? username, string fallback = "?")
    {
        var firstInitial = !string.IsNullOrEmpty(firstName)
            ? firstName[0].ToString().ToUpperInvariant()
            : string.Empty;

        var lastInitial = !string.IsNullOrEmpty(lastName)
            ? lastName[0].ToString().ToUpperInvariant()
            : string.Empty;

        var initials = $"{firstInitial}{lastInitial}";

        if (!string.IsNullOrEmpty(initials))
            return initials;

        return !string.IsNullOrEmpty(username)
            ? username[0].ToString().ToUpperInvariant()
            : fallback;
    }
}
