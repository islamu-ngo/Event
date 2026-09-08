using System.Net;

namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Provides consistent local fallback image generation without external network dependencies.
/// </summary>
public static class ImageHelper
{
    private const string DefaultTextColor = "ffffff";
    private const string DefaultOrganizationColor = "2196F3";
    private const int MaxOrganizationNameLength = 15;

    /// <summary>
    /// Returns the event's featured image URL or a title-derived local gradient fallback.
    /// </summary>
    /// <param name="featuredImageUri">The actual image URI, if available.</param>
    /// <param name="title">The event title used to derive a stable gradient.</param>
    /// <param name="colorHex">Retained for caller compatibility; gradient colors are derived from the title.</param>
    /// <param name="width">Fallback width. Defaults to 600.</param>
    /// <param name="height">Fallback height. Defaults to 400.</param>
    public static string GetEventImageUrl(
        string? featuredImageUri,
        string title,
        string? colorHex = null,
        int width = 600,
        int height = 400)
    {
        if (!string.IsNullOrEmpty(featuredImageUri))
            return featuredImageUri;

        _ = colorHex;
        return CreateEventGradientSvgDataUri(width, height, title);
    }

    /// <summary>
    /// Returns an organization image URI or a local SVG fallback with the organization name.
    /// </summary>
    public static string GetOrganizationPlaceholder(
        string? profileImageUri,
        string name,
        int size = 240)
    {
        if (!string.IsNullOrEmpty(profileImageUri))
            return profileImageUri;

        var truncatedName = name.Length > MaxOrganizationNameLength
            ? name[..MaxOrganizationNameLength] + "..."
            : name;

        return CreateSvgDataUri(size, size, DefaultOrganizationColor, truncatedName, DefaultTextColor);
    }

    private static string CreateSvgDataUri(int width, int height, string backgroundColor, string text, string textColor)
    {
        var safeWidth = Math.Max(1, width);
        var safeHeight = Math.Max(1, height);
        var fontSize = Math.Clamp(Math.Min(safeWidth, safeHeight) / 7, 18, 34);
        var encodedText = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(text) ? "Event" : text.Trim());

        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{safeWidth}\" height=\"{safeHeight}\" viewBox=\"0 0 {safeWidth} {safeHeight}\" role=\"img\" aria-label=\"{encodedText}\"><rect width=\"100%\" height=\"100%\" fill=\"#{backgroundColor}\"/><foreignObject width=\"100%\" height=\"100%\"><div xmlns=\"http://www.w3.org/1999/xhtml\" style=\"width:100%;height:100%;display:flex;align-items:center;justify-content:center;text-align:center;color:#{textColor};font-family:Inter,Segoe UI,Arial,sans-serif;font-size:{fontSize}px;font-weight:600;padding:24px;box-sizing:border-box;overflow:hidden;word-break:break-word;line-height:1.3;\">{encodedText}</div></foreignObject></svg>";
        return $"data:image/svg+xml;utf8,{Uri.EscapeDataString(svg)}";
    }

    private static string CreateEventGradientSvgDataUri(int width, int height, string title)
    {
        var safeWidth = Math.Max(1, width);
        var safeHeight = Math.Max(1, height);
        var hash = GetStableTitleHash(title);
        var firstHue = hash % 360;
        var secondHue = (firstHue + 48 + ((hash >> 8) % 120)) % 360;
        var meshHue = (secondHue + 72 + ((hash >> 16) % 90)) % 360;
        var meshX = 20 + ((hash >> 5) % 61);
        var meshY = 15 + ((hash >> 13) % 71);

        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{safeWidth}\" height=\"{safeHeight}\" viewBox=\"0 0 {safeWidth} {safeHeight}\" aria-hidden=\"true\" focusable=\"false\"><style data-palettes=\"event-gradient-light event-gradient-dark\">.base-start{{stop-color:hsl({firstHue},58%,70%)}}.base-end{{stop-color:hsl({secondHue},55%,60%)}}.mesh-a-start{{stop-color:hsl({meshHue},64%,76%)}}.mesh-a-end{{stop-color:hsl({meshHue},58%,64%)}}.mesh-b-start{{stop-color:hsl({secondHue},65%,68%)}}.mesh-b-end{{stop-color:hsl({secondHue},55%,58%)}}@media (prefers-color-scheme: dark){{.base-start{{stop-color:hsl({firstHue},78%,42%)}}.base-end{{stop-color:hsl({secondHue},72%,34%)}}.mesh-a-start{{stop-color:hsl({meshHue},88%,66%)}}.mesh-a-end{{stop-color:hsl({meshHue},88%,44%)}}.mesh-b-start{{stop-color:hsl({secondHue},90%,58%)}}.mesh-b-end{{stop-color:hsl({secondHue},80%,38%)}}}}</style><defs><linearGradient id=\"base\" x1=\"0%\" y1=\"0%\" x2=\"100%\" y2=\"100%\"><stop class=\"base-start\" offset=\"0%\"/><stop class=\"base-end\" offset=\"100%\"/></linearGradient><radialGradient id=\"mesh-a\" cx=\"{meshX}%\" cy=\"{meshY}%\" r=\"72%\"><stop class=\"mesh-a-start\" offset=\"0%\" stop-opacity=\"0.72\"/><stop class=\"mesh-a-end\" offset=\"100%\" stop-opacity=\"0\"/></radialGradient><radialGradient id=\"mesh-b\" cx=\"92%\" cy=\"88%\" r=\"68%\"><stop class=\"mesh-b-start\" offset=\"0%\" stop-opacity=\"0.58\"/><stop class=\"mesh-b-end\" offset=\"100%\" stop-opacity=\"0\"/></radialGradient></defs><rect width=\"100%\" height=\"100%\" fill=\"url(#base)\"/><rect width=\"100%\" height=\"100%\" fill=\"url(#mesh-a)\"/><rect width=\"100%\" height=\"100%\" fill=\"url(#mesh-b)\"/></svg>";
        return $"data:image/svg+xml;utf8,{Uri.EscapeDataString(svg)}";
    }

    private static uint GetStableTitleHash(string title)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;

        foreach (var character in string.IsNullOrWhiteSpace(title) ? "event" : title.Trim())
        {
            hash ^= character;
            hash = unchecked(hash * prime);
        }

        return hash;
    }
}
