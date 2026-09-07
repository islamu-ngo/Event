using System.Globalization;
using System.Text.Encodings.Web;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.VisualQa;

public sealed class ConfigurationManifestVisualQaCaptureTests
{
    [Test]
    public async Task Capture_WhenEvidenceDirectoryIsConfiguredWritesLtrAndRtlHtml()
    {
        string? outputDirectory =
            Environment.GetEnvironmentVariable("CONFIGURATION_MANIFEST_VISUAL_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        using var context = new BlazorTestContext();
        var service = Substitute.For<IConfigurationManifestExportService>();
        service.GetCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfControlPlaneOverviewDto
            {
                _links = new Dictionary<string, HalLink>
                {
                    [ControlPlaneLinkRelations.ExportConfigurationOverrides] =
                        new HalLink { Href = "/ignored", Method = "GET" },
                    [ControlPlaneLinkRelations.ExportConfigurationPortable] =
                        new HalLink { Href = "/ignored", Method = "GET" }
                }
            });
        context.Services.AddSingleton(service);

        MudTheme theme = new AppearanceThemeService(
            Substitute.For<IUserAppearanceClient>(),
            Substitute.For<IUiThemeAdminClient>(),
            NullLogger<AppearanceThemeService>.Instance)
            .CreateTheme("64px");
        var themeProvider = context.Render<MudThemeProvider>(parameters => parameters
            .Add(provider => provider.Theme, theme));
        var cut = context.Render<ConfigurationManifestExportSection>();
        cut.WaitForAssertion(() => cut.Find("#configuration-manifest-export-heading"));
        string themedMarkup = string.Concat(themeProvider.Markup, cut.Markup);

        string repositoryRoot = FindRepositoryRoot();
        string mudCss = await File.ReadAllTextAsync(MudBlazorStylePath());
        string tokenCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor",
            "wwwroot",
            "css",
            "tokens.css"));
        string componentCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Pages",
            "Admin",
            "Instance",
            "Components",
            "ConfigurationManifestExportSection.razor.css"));
        string actionButtonCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Components",
            "ControlPlane",
            "ControlPlaneActionButton.razor.css"));
        componentCss = string.Concat(
                componentCss,
                Environment.NewLine,
                actionButtonCss)
            .Replace("::deep ", string.Empty, StringComparison.Ordinal);

        Directory.CreateDirectory(outputDirectory);
        await WriteCaptureAsync(
            Path.Combine(outputDirectory, "configuration-manifest-ltr.html"),
            "ltr",
            themedMarkup,
            mudCss,
            tokenCss,
            componentCss);
        await WriteCaptureAsync(
            Path.Combine(outputDirectory, "configuration-manifest-rtl.html"),
            "rtl",
            themedMarkup,
            mudCss,
            tokenCss,
            componentCss);

        await Assert.That(File.Exists(Path.Combine(
            outputDirectory,
            "configuration-manifest-ltr.html"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(
            outputDirectory,
            "configuration-manifest-rtl.html"))).IsTrue();
        string ltrHtml = await File.ReadAllTextAsync(Path.Combine(
            outputDirectory,
            "configuration-manifest-ltr.html"));
        await Assert.That(ltrHtml).Contains("--mud-palette-primary:");
        await Assert.That(ltrHtml).Contains("--mud-palette-lines-default:");
        await Assert.That(ltrHtml).Contains(
            ".control-plane-action-button .mud-button-root");
        await Assert.That(ltrHtml).DoesNotContain("::deep");
    }

    private static async Task WriteCaptureAsync(
        string path,
        string direction,
        string componentMarkup,
        string mudCss,
        string tokenCss,
        string componentCss)
    {
        string encodedDirection = HtmlEncoder.Default.Encode(direction);
        string html = $$"""
            <!doctype html>
            <html lang="en" dir="{{encodedDirection}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Configuration manifest visual QA</title>
              <style>{{mudCss}}</style>
              <style>{{tokenCss}}</style>
              <style>{{componentCss}}</style>
              <style>
                body {
                  margin: 0;
                  min-block-size: 100vh;
                  background: #f6f7f9;
                  color: #1f2937;
                  font-family: Inter, system-ui, sans-serif;
                }
                main {
                  box-sizing: border-box;
                  inline-size: min(52rem, calc(100% - 2rem));
                  margin-block: 3rem;
                  margin-inline: auto;
                }
                h1 {
                  margin-block: 0 1.5rem;
                  font-size: 1.75rem;
                }
              </style>
            </head>
            <body>
              <main id="main-content">
                <h1>Instance settings</h1>
                {{componentMarkup}}
              </main>
            </body>
            </html>
            """;

        await File.WriteAllTextAsync(path, html);
    }

    private static string MudBlazorStylePath()
    {
        Version version = typeof(MudButton).Assembly.GetName().Version
            ?? throw new InvalidOperationException("MudBlazor assembly version is unavailable.");
        string packageVersion = string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{version.Build}");
        string packagesRoot =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        return Path.Combine(
            packagesRoot,
            "mudblazor",
            packageVersion,
            "staticwebassets",
            "MudBlazor.min.css");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
