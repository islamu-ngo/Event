using System.Text.RegularExpressions;

namespace Event.Architecture.Tests;

public sealed class EventControlPlaneClientArchitectureTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly string ExploreBlazorClientRoot = Path.Combine(RepoRoot, "src", "Explore.Blazor.Client");

    [Test]
    public async Task ControlPlaneRoutes_MustStayUnderAdminPortalRoots()
    {
        var routesPath = Path.Combine(ExploreBlazorClientRoot, "Routing", "ControlPlane", "ControlPlaneRoutes.cs");
        await Assert.That(File.Exists(routesPath)).IsTrue()
            .Because("Control-plane route constants must be centralized.");

        var source = await File.ReadAllTextAsync(routesPath);
        await Assert.That(source.Contains("public const string Root = \"/admin/instance\";", StringComparison.Ordinal)).IsTrue()
            .Because("Control-plane routes must live under the instance administration root.");
        await Assert.That(source.Contains("public const string TenantRoot = \"/tenant/{TenantSlug}\";", StringComparison.Ordinal)).IsTrue()
            .Because("Tenant-console routes must live under the dedicated tenant administration root.");

        var directRouteLiterals = Regex.Matches(source, "public const string (?<name>\\w+) = \"(?<path>/[^\"]+)\";")
            .Select(match => match.Groups["path"].Value)
            .ToArray();

        var directRouteViolations = directRouteLiterals
            .Where(route => !route.StartsWith("/admin/instance", StringComparison.Ordinal)
                && !route.StartsWith("/tenant/{TenantSlug}", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(directRouteViolations).IsEmpty()
            .Because($"AdminPortal routes must not introduce public paths outside the instance or tenant administration roots. Violations: {string.Join(", ", directRouteViolations)}");

        var nonRootConstants = Regex.Matches(source, "public const string (?<name>\\w+) = (?<expression>[^;]+);")
            .Select(match => new
            {
                Name = match.Groups["name"].Value,
                Expression = match.Groups["expression"].Value.Trim()
            })
            .Where(route => route.Name is not "Root" and not "TenantRoot")
            .ToArray();

        var compositionViolations = nonRootConstants
            .Where(route => route.Expression != "Root"
                && route.Expression != "TenantRoot"
                && !route.Expression.StartsWith("Root +", StringComparison.Ordinal)
                && !route.Expression.StartsWith("TenantRoot +", StringComparison.Ordinal))
            .Select(route => $"{route.Name} = {route.Expression}")
            .ToArray();

        await Assert.That(compositionViolations).IsEmpty()
            .Because($"AdminPortal child routes must compose from Root or TenantRoot. Violations: {string.Join(", ", compositionViolations)}");
    }

    [Test]
    public async Task PublicAdminHost_MustUseExplicitControlPlaneRoutesWithoutLegacyProjectReference()
    {
        var clientProject = await File.ReadAllTextAsync(Path.Combine(ExploreBlazorClientRoot, "Explore.Blazor.Client.csproj"));
        var hostProject = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.Blazor", "Explore.Blazor.csproj"));
        var router = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.Blazor", "Components", "ControlPlane", "EmbeddedControlPlaneRoutes.razor"));
        var page = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.Blazor", "Components", "ControlPlane", "EmbeddedControlPlanePage.razor"));

        await Assert.That(clientProject).DoesNotContain("Event.ControlPlane.Client.csproj");
        await Assert.That(hostProject).DoesNotContain("Event.ControlPlane.Client.csproj");
        await Assert.That(router).DoesNotContain("AdditionalAssemblies");
        await Assert.That(router).DoesNotContain("ControlPlaneClientAssembly");
        await Assert.That(Regex.Matches(page, "(?m)^@page ").Count).IsEqualTo(7);
        await Assert.That(page).Contains("@attribute [Authorize]");
        await Assert.That(page).Contains("<ControlPlaneOverviewPage />");
        await Assert.That(page).Contains("<InstanceTenants />");
        await Assert.That(page).Contains("<InstanceTenantConfiguration TenantId=\"@tenantId\" />");
        await Assert.That(page).Contains("<InstancePlans />");
        await Assert.That(page).Contains("<InstancePlanDetail Key=\"@Key\" />");
        await Assert.That(page).Contains("<ControlPlaneDomainsPage />");
        await Assert.That(page).Contains("<ControlPlaneOperationsPage />");
    }

    [Test]
    public async Task ExploreBlazorClient_MustExposeControlPlaneServiceRegistration()
    {
        var extensionPath = Path.Combine(ExploreBlazorClientRoot, "Extensions", "ControlPlaneServiceCollectionExtensions.cs");
        var sharedExtensionPath = Path.Combine(ExploreBlazorClientRoot, "Extensions", "ServiceCollectionExtensions.cs");
        var adapterPath = Path.Combine(ExploreBlazorClientRoot, "Services", "ControlPlane", "ControlPlaneApiAdapter.cs");
        await Assert.That(File.Exists(extensionPath)).IsTrue()
            .Because("Blazor hosts need one shared registration entry point for control-plane client services.");

        var source = await File.ReadAllTextAsync(extensionPath);
        var sharedSource = await File.ReadAllTextAsync(sharedExtensionPath);
        var adapterSource = await File.ReadAllTextAsync(adapterPath);
        await Assert.That(source.Contains("AddEventControlPlaneClient", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("IControlPlaneRouteCatalog", StringComparison.Ordinal)).IsTrue()
            .Because("The embedded shell needs one shared route catalog.");

        foreach (var service in new[]
        {
            "IControlPlaneOverviewService",
            "IControlPlaneTenantService",
            "IControlPlaneDomainService",
            "IControlPlaneOperationsService",
            "IControlPlanePlanCatalogService",
            "IControlPlaneTenantConfigurationService"
        })
        {
            await Assert.That(sharedSource).Contains(service)
                .Because("Control-plane pages use thin UI services around the generated client.");
        }

        await Assert.That(adapterSource).Contains("IControlPlaneClient controlPlaneClient")
            .Because("The generated API client must be the only backend transport boundary.");
        await Assert.That(adapterSource).DoesNotContain("Explore.Application");
        await Assert.That(adapterSource).DoesNotContain("Explore.Domain");
    }

    [Test]
    public async Task ExploreBlazorClient_MustUseGeneratedHalAndCommandContracts()
    {
        var requiredFiles = new[]
        {
            Path.Combine(ExploreBlazorClientRoot, "Contracts", "ControlPlane", "ControlPlaneLinkRelations.cs"),
            Path.Combine(ExploreBlazorClientRoot, "Contracts", "ControlPlane", "ControlPlaneHal.cs"),
            Path.Combine(ExploreBlazorClientRoot, "Contracts", "Services", "ControlPlane", "IControlPlaneOverviewService.cs"),
            Path.Combine(ExploreBlazorClientRoot, "Contracts", "Services", "ControlPlane", "IControlPlaneTenantService.cs"),
            Path.Combine(ExploreBlazorClientRoot, "Contracts", "Services", "ControlPlane", "IControlPlaneDomainService.cs")
        };

        var missing = requiredFiles
            .Where(file => !File.Exists(file))
            .Select(file => Path.GetRelativePath(RepoRoot, file).Replace('\\', '/'))
            .ToArray();

        await Assert.That(missing).IsEmpty()
            .Because($"Embedded control-plane components need generated HAL-aware service contracts. Missing: {string.Join(", ", missing)}");

        var forbiddenMirrors = new[]
        {
            "ControlPlaneHalLink.cs",
            "IControlPlaneHalResource.cs",
            "ControlPlaneResult.cs",
            "ControlPlaneCommandResult.cs"
        };
        foreach (var fileName in forbiddenMirrors)
        {
            await Assert.That(File.Exists(Path.Combine(ExploreBlazorClientRoot, "Contracts", "ControlPlane", fileName))).IsFalse()
                .Because("HAL, result, and command contracts must come from EventApiTagClients.g.cs.");
        }

        foreach (var file in requiredFiles.Where(file => file.Contains($"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var contractSource = await File.ReadAllTextAsync(file);
            await Assert.That(contractSource).Contains("using Explore.Blazor.Client.Clients;");
            await Assert.That(Regex.IsMatch(
                contractSource,
                @"\b(?:HalResource|HalCollectionResource|BaseCommandResponse)",
                RegexOptions.CultureInvariant)).IsTrue()
                .Because("Control-plane UI services must expose generated HAL or command-response contracts.");
        }

        var halSource = await File.ReadAllTextAsync(Path.Combine(ExploreBlazorClientRoot, "Contracts", "ControlPlane", "ControlPlaneHal.cs"));
        await Assert.That(halSource.Contains("HasLink", StringComparison.Ordinal)).IsTrue()
            .Because("Control-plane components must gate action affordances from HAL link presence.");
    }

    [Test]
    public async Task PublicTenantConfigurationPage_MustNotDependOnPlanAssignmentService()
    {
        var pagePath = Path.Combine(
            ExploreBlazorClientRoot,
            "Pages",
            "Admin",
            "Instance",
            "InstanceTenantConfiguration.razor");
        var source = await File.ReadAllTextAsync(pagePath);

        await Assert.That(source.Contains("IControlPlaneTenantConfigurationService", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("IControlPlanePlanService", StringComparison.Ordinal)).IsFalse();
        await Assert.That(source.Contains("ControlPlaneHal.HasLink(setting", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task PublicPlanCatalog_MustUseGeneratedContractHalAffordancesAndGuardedRoutes()
    {
        var catalogPath = Path.Combine(ExploreBlazorClientRoot, "Contracts", "Services", "ControlPlane", "IControlPlanePlanCatalogService.cs");
        var planServicePath = Path.Combine(ExploreBlazorClientRoot, "Contracts", "Services", "ControlPlane", "IControlPlanePlanService.cs");
        var listPagePath = Path.Combine(ExploreBlazorClientRoot, "Pages", "Admin", "Instance", "InstancePlans.razor");
        var detailPagePath = Path.Combine(ExploreBlazorClientRoot, "Pages", "Admin", "Instance", "InstancePlanDetail.razor");
        var routesPath = Path.Combine(ExploreBlazorClientRoot, "Routes.razor");

        await Assert.That(File.Exists(catalogPath)).IsTrue()
            .Because("public plan pages need one generated HAL and command-response boundary");
        await Assert.That(File.Exists(listPagePath)).IsTrue();
        await Assert.That(File.Exists(listPagePath + ".css")).IsTrue();
        await Assert.That(File.Exists(detailPagePath)).IsTrue();
        await Assert.That(File.Exists(detailPagePath + ".css")).IsTrue();

        var catalog = await File.ReadAllTextAsync(catalogPath);
        var listPage = await File.ReadAllTextAsync(listPagePath);
        var listCss = await File.ReadAllTextAsync(listPagePath + ".css");
        var detailPage = await File.ReadAllTextAsync(detailPagePath);
        var detailCss = await File.ReadAllTextAsync(detailPagePath + ".css");
        var routes = await File.ReadAllTextAsync(routesPath);

        await Assert.That(catalog).Contains("GetPlansAsync");
        await Assert.That(catalog).Contains("GetPlanAsync");
        await Assert.That(catalog).Contains("CreatePlanDraftAsync");
        await Assert.That(catalog).Contains("CreateVersionDraftAsync");
        await Assert.That(catalog).Contains("UpdateVersionDraftAsync");
        await Assert.That(catalog).Contains("PublishVersionAsync");
        await Assert.That(catalog).Contains("ArchiveVersionAsync");
        await Assert.That(catalog).Contains("ClonePlanAsync");
        await Assert.That(catalog).Contains("ValidateDraftAsync");
        await Assert.That(catalog).Contains("PreviewDiffAsync");
        await Assert.That(File.Exists(planServicePath)).IsFalse()
            .Because("The Blazor client must not retain a local plan mutation contract.");

        foreach (var page in new[] { listPage, detailPage })
        {
            await Assert.That(page).Contains("IControlPlanePlanCatalogService");
            await Assert.That(page).DoesNotContain("IControlPlanePlanService");
        }

        await Assert.That(listPage).Contains("ControlPlaneHal.HasLink(plan._links, ControlPlaneLinkRelations.Self)")
            .Because("plan detail navigation must come only from each item's HAL self link");
        await Assert.That(listPage).Contains("ControlPlaneHal.HasLink(_catalog?._links, ControlPlaneLinkRelations.Create)");
        await Assert.That(listPage).Contains("ControlPlaneHal.HasLink(_catalog?._links, ControlPlaneLinkRelations.Validate)");
        await Assert.That(detailPage).Contains("ControlPlaneHal.HasLink(_plan?._links, ControlPlaneLinkRelations.CreateVersionDraft)");
        await Assert.That(detailPage).Contains("ControlPlaneHal.HasLink(_plan?._links, ControlPlaneLinkRelations.Validate)");
        await Assert.That(detailPage).Contains("HasVersionLink(ControlPlaneLinkRelations.UpdateVersionDraft, version)");
        await Assert.That(detailPage).Contains("HasVersionLink(ControlPlaneLinkRelations.Publish, version)");
        await Assert.That(detailPage).Contains("HasVersionLink(ControlPlaneLinkRelations.Archive, version)");
        await Assert.That(detailPage).Contains("HasVersionLink(ControlPlaneLinkRelations.Clone, version)");
        await Assert.That(detailPage).Contains("role=\"region\"");
        await Assert.That(detailPage).Contains("tabindex=\"0\"");
        await Assert.That(listCss).Contains("min-inline-size: 0");
        await Assert.That(detailCss).Contains("overflow-x: auto");
        await Assert.That(detailCss).Contains("min-inline-size:");
        await Assert.That(routes).Contains("Path = ControlPlaneRoutes.Plans, Component = typeof(InstancePlans), Transition = RouteTransition.Fade, Guards = RequireMultiTenantAdmin()");
        await Assert.That(routes).Contains("Path = \"/admin/instance/plans/:Key\", Component = typeof(InstancePlanDetail), Transition = RouteTransition.Fade, Guards = RequireMultiTenantAdmin()");
        await Assert.That(routes).DoesNotContain("Component = typeof(ControlPlanePlansPage)");
        await Assert.That(routes).DoesNotContain("Component = typeof(ControlPlanePlanDetailPage)");
    }

    [Test]
    public async Task ExploreBlazorClient_ControlPlaneDesignSystem_MustUseLocalPrimitives()
    {
        var importsPath = Path.Combine(ExploreBlazorClientRoot, "Pages", "Admin", "Instance", "ControlPlane", "_Imports.razor");
        var requiredFiles = new[]
        {
            Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlaneActionButton.razor"),
            Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlaneActionButton.razor.css"),
            Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlanePageHeader.razor"),
            Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlanePageHeader.razor.css"),
            Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlanePanel.razor"),
            Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlanePanel.razor.css")
        };

        var missing = requiredFiles
            .Where(file => !File.Exists(file))
            .Select(file => Path.GetRelativePath(RepoRoot, file).Replace('\\', '/'))
            .ToArray();

        await Assert.That(missing).IsEmpty()
            .Because($"The control-plane RCL needs local design-system primitives before pages are added. Missing: {string.Join(", ", missing)}");

        var imports = await File.ReadAllTextAsync(importsPath);
        await Assert.That(imports.Contains("@using MudBlazor", StringComparison.Ordinal)).IsTrue()
            .Because("Shared control-plane primitives should have a single MudBlazor import point.");
        await Assert.That(imports.Contains("@using Explore.Blazor.Client.Components.ControlPlane", StringComparison.Ordinal)).IsTrue()
            .Because("Shared pages should consume local ControlPlane* primitives without per-page using churn.");

        var componentNameViolations = new List<string>();
        foreach (var file in EnumerateSourceFiles(Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane")))
        {
            var relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            var fileName = Path.GetFileNameWithoutExtension(file);
            var content = await File.ReadAllTextAsync(file);

            if (fileName.StartsWith("App", StringComparison.Ordinal))
            {
                componentNameViolations.Add($"{relative} uses public-app wrapper naming");
            }

            if (content.Contains("Explore.Blazor.Client.Components.Common", StringComparison.Ordinal)
                || content.Contains("<AppButton", StringComparison.Ordinal)
                || content.Contains("<AppCard", StringComparison.Ordinal)
                || content.Contains("<AppIconButton", StringComparison.Ordinal)
                || content.Contains("<AppTextField", StringComparison.Ordinal)
                || content.Contains("<AppDialogShell", StringComparison.Ordinal))
            {
                componentNameViolations.Add($"{relative} couples to public-app wrapper components");
            }

            var cssPair = Path.ChangeExtension(file, ".razor.css");
            if (!File.Exists(cssPair))
            {
                componentNameViolations.Add($"{relative} is missing a colocated CSS isolation file");
            }
        }

        await Assert.That(componentNameViolations).IsEmpty()
            .Because(string.Join('\n', componentNameViolations));

        var cssViolations = new List<string>();
        foreach (var file in EnumerateCssFiles(Path.Combine(ExploreBlazorClientRoot, "Components", "ControlPlane")))
        {
            var content = await File.ReadAllTextAsync(file);
            var relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');

            if (!content.Contains(".control-plane-", StringComparison.Ordinal))
            {
                cssViolations.Add($"{relative} must use the control-plane BEM namespace");
            }

            if (Regex.IsMatch(content, @"(?m)^\s*\.mud-", RegexOptions.CultureInvariant))
            {
                cssViolations.Add($"{relative} contains a bare MudBlazor selector instead of scoped ::deep usage");
            }

            var physicalDirectionTokens = new[]
            {
                "margin-left",
                "margin-right",
                "padding-left",
                "padding-right",
                "border-left",
                "border-right",
                "left:",
                "right:",
                "text-align: left",
                "text-align: right"
            };

            foreach (var token in physicalDirectionTokens)
            {
                if (content.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    cssViolations.Add($"{relative} contains physical direction token '{token}'");
                }
            }
        }

        await Assert.That(cssViolations).IsEmpty()
            .Because(string.Join('\n', cssViolations));
    }

    [Test]
    public async Task TenantLifecycleVisuals_MustUseFlatRowsAdaptiveBordersAndMeaningfulHoverStates()
    {
        var panelCss = await File.ReadAllTextAsync(Path.Combine(
            ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlanePanel.razor.css"));
        var rclButtonCss = await File.ReadAllTextAsync(Path.Combine(
            ExploreBlazorClientRoot, "Components", "ControlPlane", "ControlPlaneActionButton.razor.css"));
        var publicButtonCss = await File.ReadAllTextAsync(Path.Combine(
            ExploreBlazorClientRoot, "Components", "Common", "AppButton.razor.css"));
        var publicTenantCss = await File.ReadAllTextAsync(Path.Combine(
            ExploreBlazorClientRoot, "Pages", "Admin", "Instance", "Components", "InstanceTenantsSection.razor.css"));

        await Assert.That(panelCss).DoesNotContain(".control-plane-panel:hover")
            .Because("non-interactive structural panels must not imply clickability through hover elevation");
        await Assert.That(publicTenantCss).Contains("border: 1px solid var(--isl-color-text-secondary)")
            .Because("public table and form controls need an adaptive border with at least 3:1 contrast");
        await Assert.That(publicTenantCss).Contains("text-align: start")
            .Because("mobile action labels must follow the document writing direction");

        foreach (var buttonCss in new[] { rclButtonCss, publicButtonCss })
        {
            await Assert.That(buttonCss).Contains("border: 1px solid var(--isl-color-text-secondary)")
                .Because("outlined controls need an adaptive border with at least 3:1 contrast");
            await Assert.That(buttonCss).Contains(".mud-button-outlined-error:hover")
                .Because("outlined destructive actions need an explicit high-contrast hover state");
            await Assert.That(buttonCss).Contains(".mud-button-outlined-error:active")
                .Because("outlined destructive actions need an explicit high-contrast pressed state");
            await Assert.That(buttonCss).Contains(".mud-button-filled-error:hover")
                .Because("filled destructive actions need an explicit high-contrast hover state");
            await Assert.That(buttonCss).Contains(".mud-button-filled-error:active")
                .Because("filled destructive actions need an explicit high-contrast pressed state");
            await Assert.That(buttonCss).Contains("background-color: var(--mud-palette-error-darken)")
                .Because("destructive hover backgrounds must use the theme's darker error state");
            await Assert.That(buttonCss).Contains("color: var(--isl-color-background)")
                .Because("destructive hover needs a light foreground in light mode and dark foreground in dark mode");
            await Assert.That(buttonCss).Contains("--mud-ripple-color: transparent")
                .Because("destructive ripple overlays must not alter the verified foreground/background contrast pair");
        }
    }

    [Test]
    public async Task PublicTenantConfiguration_MustOwnResponsiveOverflowAdaptiveRowsAndPostRenderFocus()
    {
        var pagePath = Path.Combine(
            ExploreBlazorClientRoot,
            "Pages", "Admin", "Instance", "InstanceTenantConfiguration.razor");
        var cssPath = pagePath + ".css";
        var publicTokensPath = Path.Combine(RepoRoot, "src", "Explore.Blazor", "wwwroot", "css", "tokens.css");
        var rclButtonCssPath = Path.Combine(
            ExploreBlazorClientRoot,
            "Components",
            "ControlPlane",
            "ControlPlaneActionButton.razor.css");
        var publicButtonCssPath = Path.Combine(
            ExploreBlazorClientRoot,
            "Components",
            "Common",
            "AppButton.razor.css");
        var source = await File.ReadAllTextAsync(pagePath);
        var css = await File.ReadAllTextAsync(cssPath);
        var publicTokens = await File.ReadAllTextAsync(publicTokensPath);
        var rclButtonCss = await File.ReadAllTextAsync(rclButtonCssPath);
        var publicButtonCss = await File.ReadAllTextAsync(publicButtonCssPath);

        await Assert.That(source).Contains("@key=\"setting.Key\"");
        await Assert.That(source).Contains("OnAfterRenderAsync");
        await Assert.That(source).Contains("FocusService.RestoreFocusAsync");
        await Assert.That(source).Contains("ControlPlaneHal.HasLink(setting");
        await Assert.That(source).DoesNotContain("data-focus-generation");
        await Assert.That(css).Contains("overflow-x: auto");
        await Assert.That(css).Contains("border-block-end: 1px solid var(--isl-color-border)");
        await Assert.That(css).Contains(".instance-tenant-config__actions:focus,")
            .Because("the public host also restores focus programmatically to its action group");
        await Assert.That(css).Contains("text-wrap: pretty")
            .Because("public human descriptions and feedback must avoid orphaned RTL/CJK fragments");
        await Assert.That(source).Contains("dir=\"auto\"");
        await Assert.That(source).Contains("dir=\"ltr\"");
        await Assert.That(css).Contains("color: var(--isl-color-warning-strong)")
            .Because("public warning text is small and needs at least 4.5:1 contrast");
        await Assert.That(css).Contains("color: var(--isl-color-success-strong)")
            .Because("public success feedback is small and needs at least 4.5:1 contrast");
        await Assert.That(publicTokens).Contains("--isl-color-warning-strong: color-mix(in srgb, var(--isl-color-warning) 55%, var(--isl-color-text))");
        await Assert.That(publicTokens).Contains("--isl-color-success-strong: color-mix(in srgb, var(--isl-color-success) 55%, var(--isl-color-text))");

        foreach (var buttonCss in new[] { rclButtonCss, publicButtonCss })
        {
            await Assert.That(buttonCss).Contains(".mud-button-filled-warning")
                .Because("filled lock controls need an explicit verified foreground/background pair");
            await Assert.That(buttonCss).Contains("background-color: var(--isl-color-warning-strong)");
            await Assert.That(buttonCss).Contains("color: var(--isl-color-background)");
            await Assert.That(buttonCss).Contains(".mud-button-outlined-warning")
                .Because("public outlined lock controls also need strong warning text");
        }

        await Assert.That(css).Contains("background: var(--isl-color-surface)");
        await Assert.That(css).Contains("border: 1px solid var(--isl-color-text-secondary)");
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(file => !IsGeneratedOrBuildOutput(file));

    private static IEnumerable<string> EnumerateCssFiles(string root) =>
        Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedOrBuildOutput(file));

    private static bool IsGeneratedOrBuildOutput(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx"))
                && (Directory.Exists(Path.Combine(current.FullName, "Event.Web.BffHosting")) ||
                    Directory.Exists(Path.Combine(current.FullName, "src", "Event.Web.BffHosting"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Explore.slnx and Event.Web.BffHosting.");
    }
}
