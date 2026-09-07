using System;
using System.Text.RegularExpressions;

namespace Explore.Application.Services;

public static class SlugGenerator
{
    public static string FromTitle(string title, string fallbackPrefix = "item")
    {
        if (string.IsNullOrWhiteSpace(title))
            return $"{fallbackPrefix}-{Guid.CreateVersion7().ToString("N")[..8]}";

        var slug = title.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "")
            .Replace(".", "")
            .Replace(",", "");

        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

        if (slug.Length > 50)
            slug = slug[..50];

        return slug;
    }
}
