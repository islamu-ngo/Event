namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Provides consistent string manipulation utilities.
/// </summary>
public static class StringHelper
{
    /// <summary>
    /// Truncates text to a maximum length, appending "..." if truncated.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxLength">Maximum character length before truncation.</param>
    /// <returns>The original text if within limit, or truncated text with ellipsis.</returns>
    public static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? string.Empty;

        return text[..maxLength] + "...";
    }

    /// <summary>
    /// Truncates a description by taking the first two sentences,
    /// then truncating to maxLength if still too long.
    /// </summary>
    /// <param name="description">The description to truncate.</param>
    /// <param name="maxLength">Maximum character length. Defaults to 150.</param>
    /// <param name="fallback">Text returned when description is empty. Defaults to "No description available."</param>
    public static string TruncateDescription(string? description, int maxLength = 150, string fallback = "No description available.")
    {
        if (string.IsNullOrWhiteSpace(description)) return fallback;

        var sentences = System.Text.RegularExpressions.Regex.Split(description, @"(?<=[.!?])\s+");
        var result = string.Join(" ", sentences.Take(2));
        if (sentences.Length > 2) result += "...";
        if (result.Length > maxLength) result = result[..(maxLength - 3)] + "...";
        return result;
    }
}
