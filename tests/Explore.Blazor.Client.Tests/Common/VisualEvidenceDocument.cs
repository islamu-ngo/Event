using System.Text.RegularExpressions;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Common;

internal static partial class VisualEvidenceDocument
{
    public static async Task<string> WriteAsync(
        string relativePath,
        string title,
        string renderedMarkup)
    {
        string repositoryRoot = RepositoryRoot();
        string mudVersion =
            typeof(MudButton).Assembly.GetName().Version?.ToString(3)
            ?? throw new InvalidOperationException("MudBlazor version is unavailable.");
        string mudCssPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "mudblazor",
            mudVersion,
            "staticwebassets",
            "MudBlazor.min.css");
        string tokenCssPath = Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor",
            "wwwroot",
            "css",
            "tokens.css");
        string scopedCssPath = Directory
            .GetFiles(
                Path.Combine(
                    repositoryRoot,
                    "src",
                    "Explore.Blazor.Client",
                    "obj",
                    "Release"),
                "Explore.Blazor.Client.bundle.scp.css",
                SearchOption.AllDirectories)
            .Single();

        string document = $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{title}}</title>
                <style>
                {{await File.ReadAllTextAsync(mudCssPath)}}
                {{await File.ReadAllTextAsync(tokenCssPath)}}
                {{await File.ReadAllTextAsync(scopedCssPath)}}
                :root {
                    --mud-palette-primary: #2563eb;
                    --mud-palette-surface: #ffffff;
                    --mud-palette-background: #f8fafc;
                    --mud-palette-text-primary: #0f172a;
                    --mud-palette-text-secondary: #64748b;
                    --mud-palette-lines-inputs: #94a3b8;
                    --mud-palette-lines-default: #cbd5e1;
                    --mud-palette-action-default: rgb(15 23 42 / 0.08);
                    --mud-palette-action-default-hover: rgb(37 99 235 / 0.12);
                    --mud-palette-error: #dc2626;
                }
                body {
                    min-block-size: 100vh;
                    margin: 0;
                    background: var(--mud-palette-background);
                    color: var(--mud-palette-text-primary);
                    font-family: var(--isl-font-family-primary);
                }
                </style>
            </head>
            <body>
                {{Sanitize(renderedMarkup)}}
            </body>
            </html>
            """;
        string path = Path.Combine(repositoryRoot, relativePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Evidence path has no directory."));
        await File.WriteAllTextAsync(path, document);
        return path;
    }

    private static string Sanitize(string markup) =>
        GuidPattern().Replace(markup, "[guid]");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !Directory.Exists(Path.Combine(directory.FullName, ".git"))
               && !File.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    [GeneratedRegex(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}
