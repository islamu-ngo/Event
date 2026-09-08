namespace Event.Architecture.Tests;

using System.Text.RegularExpressions;

public class DockLayoutArchitectureTests
{
    private static readonly string BlazorClientRoot = ResolveBlazorClientRoot();

    [Test]
    public async Task DockPanelIds_MustNotBeModeledAsCentralEnums()
    {
        var enumPattern = new Regex(@"\benum\s+(?<name>\w+)", RegexOptions.CultureInvariant);
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(BlazorClientRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file))
                continue;

            var content = await File.ReadAllTextAsync(file);
            foreach (Match match in enumPattern.Matches(content))
            {
                var enumName = match.Groups["name"].Value;
                if (!enumName.Contains("Panel", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                violations.Add($"{GetRelativePath(file)}:{lineNumber} — enum {enumName}");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("dock panels must use component-owned DockPanelId constants/descriptors, not a central panel enum. " +
                     $"Violations: {string.Join(", ", violations)}");
    }

    [Test]
    public async Task PageCss_MustNotCompensateForShellDockPanels()
    {
        var bannedShellCompensationPatterns = new[]
        {
            new Regex(@"--isl-dock-shell-", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex(@"main-layout__main--ai-open", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex(@"\bai-rail\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex(@"\bshell-ai\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex(@"width\s*:\s*calc\(\s*100%\s*\+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
        };

        var knownLegacyCompensation = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pages/Events/EventList.razor.css"
        };

        var violations = new List<string>();
        var pagesRoot = Path.Combine(BlazorClientRoot, "Pages");
        foreach (var cssFile in Directory.EnumerateFiles(pagesRoot, "*.razor.css", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(cssFile);
            if (knownLegacyCompensation.Contains(relative))
                continue;

            var content = await File.ReadAllTextAsync(cssFile);
            foreach (var pattern in bannedShellCompensationPatterns)
            {
                foreach (Match match in pattern.Matches(content))
                {
                    var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                    violations.Add($"{relative}:{lineNumber} — {match.Value.Trim()}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("page-scoped CSS must not compensate for global shell dock panels. " +
                     "Use DockLayoutHost grid tracks and dock descriptors instead. " +
                     $"Violations: {string.Join(", ", violations)}");
    }

    [Test]
    public async Task DockCss_MustLayerWorkspaceInspectorAboveAppChrome()
    {
        var clientDockOverlayCss = await File.ReadAllTextAsync(Path.Combine(
            BlazorClientRoot,
            "Components/Docking/DockOverlayHost.razor.css"));
        var globalTokensCss = await File.ReadAllTextAsync(Path.Combine(
            ResolveRepoRoot(),
            "src", "Explore.Blazor/wwwroot/css/tokens.css"));

        await Assert.That(clientDockOverlayCss).Contains("z-index: var(--isl-dock-z-inspector, 1400);")
            .Because("workspace inspectors must paint above page content, shell chrome, and the footer.");
        await Assert.That(clientDockOverlayCss).Contains(".dock-overlay-host--workspace .dock-overlay-host__backdrop")
            .Because("workspace inspector backdrops must dim the whole app chrome below the inspector panel.");
        await Assert.That(clientDockOverlayCss).Contains("z-index: var(--isl-dock-z-workspace-backdrop, 1100);")
            .Because("workspace backdrop must stay below the inspector panel inside the overlay stacking context.");
        await Assert.That(globalTokensCss).Contains("--isl-dock-z-shell: 1200;");
        await Assert.That(globalTokensCss).Contains("--isl-dock-z-workspace-backdrop: 1100;");
    }

    [Test]
    public async Task DockCss_MustKeepShellSidePanelsFixedBelowHeader()
    {
        var clientDockSideCss = await File.ReadAllTextAsync(Path.Combine(
            BlazorClientRoot,
            "Components/Docking/DockSideHost.razor.css"));
        var mainLayoutCss = await File.ReadAllTextAsync(Path.Combine(
            BlazorClientRoot,
            "Layout/MainLayout.razor.css"));
        var globalComponentsCss = await File.ReadAllTextAsync(Path.Combine(
            ResolveRepoRoot(),
            "src", "Explore.Blazor/wwwroot/css/components.css"));

        await Assert.That(mainLayoutCss).Contains("position: fixed;")
            .Because("the shell header must remain visible while event-list content scrolls.");
        await Assert.That(mainLayoutCss).Contains("margin-block-start: var(--mud-appbar-height, 4rem);")
            .Because("fixed shell chrome must reserve its height before the dock host content starts.");
        await Assert.That(clientDockSideCss).Contains("position: fixed;")
            .Because("shell side panels must not drift while the page scrolls.");
        await Assert.That(globalComponentsCss).Contains("position: fixed !important;")
            .Because("the global cross-component shell dock rule must match the isolated component rule.");
        await Assert.That(globalComponentsCss).Contains("inset-inline-start: var(--isl-shell-start-inset, 0px) !important;")
            .Because("the fixed Start dock must honor permanent inline-start shell chrome such as the workspace rail.");
        await Assert.That(globalComponentsCss).DoesNotContain("position: sticky !important;")
            .Because("sticky shell docks move during scroll boundary transitions.");
    }

    [Test]
    public async Task AppWorkspaceRailIsSanctionedShellChromeNotDockPanel()
    {
        var railRazorPath = Path.Combine(BlazorClientRoot, "Components", "Shell", "AppWorkspaceRail.razor");
        var railCssPath = Path.Combine(BlazorClientRoot, "Components", "Shell", "AppWorkspaceRail.razor.css");
        var railCodePath = Path.Combine(BlazorClientRoot, "Components", "Shell", "AppWorkspaceRail.razor.cs");

        await Assert.That(File.Exists(railRazorPath)).IsTrue()
            .Because("AppWorkspaceRail is sanctioned permanent shell chrome per ADR-019 D1.");
        await Assert.That(File.Exists(railCssPath)).IsTrue()
            .Because("the rail must use BEM isolated CSS.");
        await Assert.That(File.Exists(railCodePath)).IsTrue()
            .Because("the rail code-behind must exist for DI and lifecycle management.");

        var railRazor = await File.ReadAllTextAsync(railRazorPath);
        var railCss = await File.ReadAllTextAsync(railCssPath);
        await Assert.That(railRazor).Contains("aria-label=\"Application workspaces\"")
            .Because("the rail must be a named navigation landmark.");
        await Assert.That(railRazor).Contains("data-testid=\"app-workspace-rail\"")
            .Because("the rail must expose a stable test hook.");
        await Assert.That(railCss).Contains("@media (max-width: 37.5em)")
            .Because("the desktop rail must project to bottom navigation at the Xs breakpoint.");
        await Assert.That(railCss).Contains("inset-block-end: 0;")
            .Because("the Xs projection must anchor to the logical block end.");
        await Assert.That(railCss).Contains("flex-direction: row;")
            .Because("the Xs projection must present workspace links horizontally.");

        var railCode = await File.ReadAllTextAsync(railCodePath);
        await Assert.That(railCode).DoesNotContain("DockPanelDescriptor")
            .Because("the rail is permanent chrome, not a dock panel (ADR-019 D1).");
        await Assert.That(railCode).DoesNotContain("DockLayoutState")
            .Because("the rail must not register with the dock layout state.");
        await Assert.That(railCode).Contains("IWorkspaceRegistry")
            .Because("the rail must consume the compile-time workspace registry.");
        await Assert.That(railCode).Contains("UiShellState")
            .Because("the rail must derive the active workspace from route-derived shell state.");

        var mainLayoutRazor = await File.ReadAllTextAsync(Path.Combine(
            BlazorClientRoot, "Layout", "MainLayout.razor"));
        await Assert.That(mainLayoutRazor).Contains("<AppWorkspaceRail />")
            .Because("MainLayout must render the rail as permanent chrome.");
        await Assert.That(mainLayoutRazor).Contains("main-layout-root--has-rail")
            .Because("MainLayout must flag the root for rail offset adjustments.");

        var mainLayoutCss = await File.ReadAllTextAsync(Path.Combine(
            BlazorClientRoot, "Layout", "MainLayout.razor.css"));
        await Assert.That(mainLayoutCss).Contains("--isl-shell-start-inset: var(--isl-workspace-rail-width);")
            .Because("desktop Start docks must begin after the permanent rail.");
        await Assert.That(mainLayoutCss).Contains("--isl-shell-start-inset: 0px;")
            .Because("mobile dock projection must not retain a desktop inline-start offset.");
    }

    private static string ResolveBlazorClientRoot()
    {
        var repoRoot = ResolveRepoRoot();
        var clientRoot = Path.Combine(repoRoot, "src", "Explore.Blazor.Client");

        if (!Directory.Exists(clientRoot))
            throw new DirectoryNotFoundException($"Explore.Blazor.Client not found at '{clientRoot}'. Test binary dir: '{AppContext.BaseDirectory}'");

        return clientRoot;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root containing Explore.slnx.");
    }

    private static bool IsGenerated(string filePath) =>
        filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
        filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string GetRelativePath(string fullPath) =>
        Path.GetRelativePath(BlazorClientRoot, fullPath).Replace('\\', '/');
}
