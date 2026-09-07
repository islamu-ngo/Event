namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Provides consistent color mappings for event types.
/// Colors are hex codes (without #) used in placeholder images and badges.
/// </summary>
public static class EventColorHelper
{
    /// <summary>
    /// Maps an event type ID to a hex color code.
    /// </summary>
    public static string GetColorByTypeId(int? eventTypeId)
    {
        if (!eventTypeId.HasValue) return DefaultColor;

        return eventTypeId.Value switch
        {
            1 => "2196F3", // Blue - Conference
            2 => "FF9800", // Orange - Workshop
            3 => "4CAF50", // Green - Webinar
            4 => "E91E63", // Pink - Seminar
            5 => "9C27B0", // Purple - Training
            _ => DefaultColor
        };
    }

    /// <summary>
    /// Maps an event type name to a hex color code.
    /// Falls back to default gray if type name is unrecognized.
    /// </summary>
    public static string GetColorByTypeName(string? eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName)) return DefaultColor;

        var lower = eventTypeName.ToLowerInvariant();
        return lower switch
        {
            var s when s.Contains("conference") => "2196F3",
            var s when s.Contains("workshop") => "FF9800",
            var s when s.Contains("webinar") => "4CAF50",
            var s when s.Contains("seminar") => "E91E63",
            var s when s.Contains("training") => "9C27B0",
            _ => DefaultColor
        };
    }

    /// <summary>
    /// Generates a deterministic color from an event title hash.
    /// Used when no event type is available.
    /// </summary>
    public static string GetColorByHash(string title)
    {
        var colors = new[] { "2196F3", "FF9800", "4CAF50", "E91E63", "9C27B0", "607D8B" };
        return colors[Math.Abs(title.GetHashCode()) % colors.Length];
    }

    /// <summary>Default gray color for unknown event types.</summary>
    public const string DefaultColor = "607D8B";
}
