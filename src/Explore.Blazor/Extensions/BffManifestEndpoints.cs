namespace Explore.Blazor.Extensions;

using Explore.Blazor.Client.Clients;

public static class BffManifestEndpoints
{
    private const string ManifestContentType = "application/manifest+json";
    private const string FallbackName = "Event Platform";
    private const string FallbackShortName = "Events";
    private const string FallbackDescription = "Discover and register for events.";
    private const string ThemeColor = "#2563eb";
    private const string BackgroundColor = "#ffffff";

    public static WebApplication MapManifestEndpoints(this WebApplication app)
    {
        app.MapGet("/manifest.webmanifest", HandleManifestAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> HandleManifestAsync(
        IPublicExperienceClient apiClient,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Explore.Blazor.Manifest");
        var brand = await ResolveBrandAsync(apiClient, logger, cancellationToken);
        return Results.Json(BuildManifest(brand), contentType: ManifestContentType);
    }

    private static async Task<ManifestBrand> ResolveBrandAsync(
        IPublicExperienceClient apiClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var shell = await apiClient.GetPublicExperienceShellAsync(cancellationToken: cancellationToken);
            var home = shell.Home;

            return new ManifestBrand(
                FirstNonBlank(home?.BrandDisplayName, FallbackName)!,
                FirstNonBlank(home?.BrandLogoUrl),
                FirstNonBlank(home?.BrandFaviconUrl));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve public branding for web manifest; using generic fallback.");
            return new ManifestBrand(FallbackName, null, null);
        }
    }

    private static Dictionary<string, object?> BuildManifest(ManifestBrand brand)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = brand.DisplayName,
            ["short_name"] = BuildShortName(brand.DisplayName),
            ["description"] = FallbackDescription,
            ["start_url"] = "/",
            ["scope"] = "/",
            ["display"] = "standalone",
            ["background_color"] = BackgroundColor,
            ["theme_color"] = ThemeColor,
            ["icons"] = BuildIcons(brand)
        };
    }

    private static List<Dictionary<string, string>> BuildIcons(ManifestBrand brand)
    {
        var icons = new List<Dictionary<string, string>>();
        AddIcon(icons, brand.FaviconUrl, "any");
        AddIcon(icons, brand.LogoUrl, "any");

        if (icons.Count == 0)
        {
            icons.Add(new Dictionary<string, string>
            {
                ["src"] = "/favicon.ico",
                ["sizes"] = "16x16 32x32 48x48",
                ["type"] = "image/x-icon"
            });
        }

        return icons;
    }

    private static void AddIcon(List<Dictionary<string, string>> icons, string? source, string sizes)
    {
        if (string.IsNullOrWhiteSpace(source) || icons.Any(icon => icon["src"] == source))
        {
            return;
        }

        icons.Add(new Dictionary<string, string>
        {
            ["src"] = source,
            ["sizes"] = sizes,
            ["type"] = InferImageType(source),
            ["purpose"] = "any"
        });
    }

    private static string InferImageType(string source)
    {
        var path = source.Split('?', '#')[0];
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ico" => "image/x-icon",
            _ => "image/png"
        };
    }

    private static string BuildShortName(string displayName)
    {
        var name = FirstNonBlank(displayName, FallbackShortName)!;
        var firstWord = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var candidate = firstWord is { Length: > 0 and <= 12 } ? firstWord : name;
        return candidate.Length <= 12 ? candidate : candidate[..12].Trim();
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private sealed record ManifestBrand(string DisplayName, string? LogoUrl, string? FaviconUrl);
}
