using Explore.Blazor.Client.Components.Docking;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services.Docking;
using MudBlazor;
using MudBlazor.Services;

namespace Explore.Blazor.Client.Tests.Components.Docking;

public sealed class DockHostTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly DockLayoutState _dockLayoutState = new();

    public DockHostTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.Services.AddSingleton(_dockLayoutState);
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task DockLayoutHost_RendersChildContentAndOpenDockedPanels()
    {
        var panelId = new DockPanelId("shell.workspace-nav");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Shell, DockSide.Start, order: 10), CreateContent("Navigation content"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockLayoutHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Shell)
            .AddChildContent("Main content"));

        await Assert.That(cut.Markup).Contains("Main content");
        await Assert.That(cut.Markup).Contains("Navigation content");
        await Assert.That(cut.Find("[data-testid='dock-layout-host']").GetAttribute("style")).Contains("--dock-layout-start-width: 320px;");
        await Assert.That(cut.Find("[data-dock-panel-id='shell.workspace-nav']").GetAttribute("aria-label")).IsEqualTo("Panel shell.workspace-nav");
    }

    [Test]
    public async Task DockLayoutHost_RendersOnlyPanelsForRequestedScope()
    {
        var shellPanelId = new DockPanelId("shell.ai-assistant");
        var workspacePanelId = new DockPanelId("workspace.customize");
        _dockLayoutState.Register(CreateDescriptor(shellPanelId, DockScope.Shell, DockSide.End, order: 10), CreateContent("Shell AI"));
        _dockLayoutState.Register(CreateDescriptor(workspacePanelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Workspace customization"));
        _dockLayoutState.Open(shellPanelId);
        _dockLayoutState.Open(workspacePanelId);

        var cut = _ctx.Render<DockLayoutHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Shell)
            .AddChildContent("Shell body"));

        await Assert.That(cut.Markup).Contains("Shell AI");
        await Assert.That(cut.Markup).DoesNotContain("Workspace customization");
    }

    [Test]
    public async Task DockLayoutHost_MobileViewport_RoutesDockedPanelsThroughOverlayChrome()
    {
        var dockedPanelId = new DockPanelId("workspace.mobile-docked");
        var scrollManager = Substitute.For<IScrollManager>();
        ConfigureScrollManager(scrollManager);
        _ctx.Services.AddSingleton(scrollManager);
        ConfigureViewport(Breakpoint.Xs);
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Mobile docked panel"));
        var focusService = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();

        var cut = _ctx.Render<DockLayoutHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .AddChildContent("Workspace body"));
        _dockLayoutState.Open(dockedPanelId);

        cut.WaitForAssertion(() =>
        {
            var layoutHost = cut.Find("[data-testid='dock-layout-host']");
            if (layoutHost.GetAttribute("style")?.Contains("--dock-layout-end-width: 0px;", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException("Mobile dock layout did not collapse the end track.");
            }

            if (!cut.Markup.Contains("Mobile docked panel", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Mobile docked panel did not render through the overlay host.");
            }

            if (cut.Find("[data-dock-panel-id='workspace.mobile-docked']").GetAttribute("data-dock-mode") != "temporary")
            {
                throw new InvalidOperationException("Mobile docked panel was not projected as a temporary overlay.");
            }

            if (cut.FindAll("[data-testid='dock-side-host']").Count != 0)
            {
                throw new InvalidOperationException("Mobile docked panel still rendered through the side host.");
            }

            if (cut.FindAll("[data-testid='dock-resize-handle']").Count != 0)
            {
                throw new InvalidOperationException("Mobile docked panel rendered a resize handle.");
            }
        }, timeout: TimeSpan.FromSeconds(5));
        await scrollManager.Received(1).LockScrollAsync("body", "scroll-locked");
        await focusService.Received(1).FocusAsync(
            "[data-testid='dock-overlay-host'][data-dock-scope='workspace'] [data-testid='dock-panel-host']",
            preventScroll: true);
    }

    [Test]
    public async Task DockLayoutHost_SevenHundredSixtyPixelViewport_RoutesDockedPanelsThroughOverlayChrome()
    {
        var dockedPanelId = new DockPanelId("shell.workspace-nav");
        var scrollManager = Substitute.For<IScrollManager>();
        ConfigureScrollManager(scrollManager);
        _ctx.Services.AddSingleton(scrollManager);
        ConfigureViewport(Breakpoint.Sm, width: 760);
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Shell, DockSide.Start, DockMode.Docked, order: 10), CreateContent("Shell nav"));

        var cut = _ctx.Render<DockLayoutHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Shell)
            .AddChildContent("Shell body"));
        _dockLayoutState.Open(dockedPanelId);

        cut.WaitForAssertion(() =>
        {
            var layoutHost = cut.Find("[data-testid='dock-layout-host']");
            if (layoutHost.GetAttribute("style")?.Contains("--dock-layout-start-width: 0px;", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException("Shell dock layout did not collapse the start track at 760px.");
            }

            if (cut.Find("[data-dock-panel-id='shell.workspace-nav']").GetAttribute("data-dock-mode") != "temporary")
            {
                throw new InvalidOperationException("Shell docked panel was not projected as a temporary overlay at 760px.");
            }
        }, timeout: TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DockSideHost_EndSideWithMultiplePanels_RendersSideBySideStack()
    {
        var secondPanelId = new DockPanelId("workspace.second");
        var firstPanelId = new DockPanelId("workspace.first");
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.End, order: 20, stackStrategy: DockPanelStackStrategy.Split), CreateContent("Second panel"));
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.End, order: 10, stackStrategy: DockPanelStackStrategy.Split), CreateContent("First panel"));
        _dockLayoutState.Open(secondPanelId);
        _dockLayoutState.Open(firstPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var panels = cut.FindAll("[data-testid='dock-panel-host']");
        var tabs = cut.FindAll("[data-testid='dock-tab-strip-tab']");

        await Assert.That(tabs.Count).IsEqualTo(0);
        await Assert.That(panels.Count).IsEqualTo(2);
        await Assert.That(panels[0].GetAttribute("data-dock-panel-id")).IsEqualTo("workspace.first");
        await Assert.That(panels[1].GetAttribute("data-dock-panel-id")).IsEqualTo("workspace.second");
        await Assert.That(panels[0].ClassList.Contains("dock-panel-host--stacked")).IsTrue();
        await Assert.That(panels[1].ClassList.Contains("dock-panel-host--stacked")).IsTrue();
        await Assert.That(cut.Find("#dock-panel-body-workspace-first").HasAttribute("role")).IsFalse();
        await Assert.That(cut.Find("#dock-panel-body-workspace-first").HasAttribute("aria-labelledby")).IsFalse();
        await Assert.That(cut.Markup).Contains("First panel");
        await Assert.That(cut.Markup).Contains("Second panel");
    }

    [Test]
    public async Task DockSideHost_StartSideWithMultiplePanels_RendersTabStripAndActivePanelOnly()
    {
        var secondPanelId = new DockPanelId("workspace.start-second");
        var firstPanelId = new DockPanelId("workspace.start-first");
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.Start, order: 20), CreateContent("Second start panel"));
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.Start, order: 10), CreateContent("First start panel"));
        _dockLayoutState.Open(secondPanelId);
        _dockLayoutState.Open(firstPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.Start));
        var panels = cut.FindAll("[data-testid='dock-panel-host']");
        var tabs = cut.FindAll("[data-testid='dock-tab-strip-tab']");

        await Assert.That(tabs.Count).IsEqualTo(2);
        await Assert.That(tabs[0].GetAttribute("data-dock-tab-panel-id")).IsEqualTo("workspace.start-first");
        await Assert.That(tabs[1].GetAttribute("data-dock-tab-panel-id")).IsEqualTo("workspace.start-second");
        await Assert.That(tabs[0].GetAttribute("aria-selected")).IsEqualTo("true");
        await Assert.That(tabs[0].GetAttribute("aria-controls")).IsEqualTo("dock-panel-body-workspace-start-first");
        await Assert.That(tabs[1].HasAttribute("aria-controls")).IsFalse();
        await Assert.That(panels.Count).IsEqualTo(1);
        await Assert.That(panels[0].GetAttribute("data-dock-panel-id")).IsEqualTo("workspace.start-first");
        await Assert.That(cut.Find("#dock-panel-body-workspace-start-first").GetAttribute("role")).IsEqualTo("tabpanel");
        await Assert.That(cut.Find("#dock-panel-body-workspace-start-first").GetAttribute("aria-labelledby")).IsEqualTo("dock-panel-tab-workspace-start-first");
        await Assert.That(cut.Markup).Contains("First start panel");
        await Assert.That(cut.Markup).DoesNotContain("Second start panel");
    }

    [Test]
    public async Task DockSideHost_EndSideWithTabbedStrategy_RendersTabFallback()
    {
        var firstPanelId = new DockPanelId("workspace.first-tabbed-end");
        var secondPanelId = new DockPanelId("workspace.second-tabbed-end");
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("First tabbed end panel"));
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.End, order: 20), CreateContent("Second tabbed end panel"));
        _dockLayoutState.Open(firstPanelId);
        _dockLayoutState.Open(secondPanelId);
        _dockLayoutState.Activate(firstPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));

        await Assert.That(cut.FindAll("[role='tab']").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-testid='dock-panel-host']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("First tabbed end panel");
        await Assert.That(cut.Markup).DoesNotContain("Second tabbed end panel");
    }

    [Test]
    public async Task DockLayoutHost_WithMultipleEndPanels_ReservesCombinedEndWidth()
    {
        var firstPanelId = new DockPanelId("workspace.width-first");
        var secondPanelId = new DockPanelId("workspace.width-second");
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.End, order: 10, stackStrategy: DockPanelStackStrategy.Split), CreateContent("First width panel"));
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.End, order: 20, stackStrategy: DockPanelStackStrategy.Split), CreateContent("Second width panel"));
        _dockLayoutState.Open(firstPanelId);
        _dockLayoutState.Open(secondPanelId);

        var cut = _ctx.Render<DockLayoutHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .AddChildContent("Workspace body"));

        await Assert.That(cut.Find("[data-testid='dock-layout-host']").GetAttribute("style")).Contains("--dock-layout-end-width: 640px;");
    }

    [Test]
    public async Task DockSideHost_Mobile_DoesNotRenderDockedPanels()
    {
        var panelId = new DockPanelId("workspace.mobile-side-hidden");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Mobile side panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End)
            .Add(component => component.IsMobile, true));

        await Assert.That(cut.FindAll("[data-testid='dock-panel-host']").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).DoesNotContain("Mobile side panel");
    }

    [Test]
    public async Task DockSideHost_ConstrainedStartPanel_DoesNotRenderDockedPanel()
    {
        var leftPanelId = new DockPanelId("shell.left-constrained");
        var rightPanelId = new DockPanelId("shell.right-docked");
        _dockLayoutState.Register(CreateDescriptor(leftPanelId, DockScope.Shell, DockSide.Start, DockMode.Docked, order: 10), CreateContent("Constrained left panel"));
        _dockLayoutState.Register(CreateDescriptor(rightPanelId, DockScope.Shell, DockSide.End, DockMode.Docked, order: 10), CreateContent("Docked right panel"));
        _dockLayoutState.UpdateViewport(970, isMobile: false);
        _dockLayoutState.Open(rightPanelId);
        _dockLayoutState.Open(leftPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Shell)
            .Add(component => component.Side, DockSide.Start));

        await Assert.That(cut.FindAll("[data-testid='dock-panel-host']").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).DoesNotContain("Constrained left panel");
    }

    [Test]
    public async Task DockOverlayHost_ConstrainedStartPanel_RendersAsTemporaryOverlayWhileRightPanelStaysDocked()
    {
        var leftPanelId = new DockPanelId("shell.left-overlay");
        var rightPanelId = new DockPanelId("shell.right-stays-docked");
        _dockLayoutState.Register(CreateDescriptor(leftPanelId, DockScope.Shell, DockSide.Start, DockMode.Docked, order: 10), CreateContent("Constrained left overlay"));
        _dockLayoutState.Register(CreateDescriptor(rightPanelId, DockScope.Shell, DockSide.End, DockMode.Docked, order: 10), CreateContent("Right panel remains docked"));
        _dockLayoutState.UpdateViewport(970, isMobile: false);
        _dockLayoutState.Open(rightPanelId);
        _dockLayoutState.Open(leftPanelId);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Shell));

        await Assert.That(_dockLayoutState.GetPanel(rightPanelId)?.State.IsOpen).IsTrue();
        await Assert.That(cut.Markup).Contains("Constrained left overlay");
        await Assert.That(cut.Find("[data-dock-panel-id='shell.left-overlay']").GetAttribute("data-dock-mode")).IsEqualTo("temporary");
        await Assert.That(cut.FindAll("[data-testid='dock-overlay-backdrop']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task DockSideHost_ClosingDockedPanel_RemainsMountedWithClosingClassThenUnmounts()
    {
        var panelId = new DockPanelId("workspace.close-animated");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Closing docked panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));

        _dockLayoutState.Close(panelId);

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("[data-testid='dock-panel-host'][data-dock-panel-id='workspace.close-animated']");
            if (!panel.ClassList.Contains("dock-panel-host--closing"))
            {
                throw new InvalidOperationException("Docked panel did not remain mounted with the closing class.");
            }
        });

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='dock-panel-host'][data-dock-panel-id='workspace.close-animated']").Count != 0)
            {
                throw new InvalidOperationException("Docked panel did not unmount after the close animation delay.");
            }
        }, timeout: TimeSpan.FromSeconds(5));

        await Task.CompletedTask;
    }

    [Test]
    public async Task DockTabStrip_ClickingTab_ActivatesPanelAndRendersItsContent()
    {
        var firstPanelId = new DockPanelId("workspace.stack-first");
        var secondPanelId = new DockPanelId("workspace.stack-second");
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.Start, order: 10), CreateContent("First stack panel"));
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.Start, order: 20), CreateContent("Second stack panel"));
        _dockLayoutState.Open(firstPanelId);
        _dockLayoutState.Open(secondPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.Start));
        var secondTab = cut.Find("[data-dock-tab-panel-id='workspace.stack-second']");

        await secondTab.ClickAsync(new MouseEventArgs());

        await Assert.That(_dockLayoutState.GetPanel(firstPanelId)?.State.IsActive).IsFalse();
        await Assert.That(_dockLayoutState.GetPanel(secondPanelId)?.State.IsActive).IsTrue();
        await Assert.That(cut.Find("[data-testid='dock-panel-host']").GetAttribute("data-dock-panel-id")).IsEqualTo("workspace.stack-second");
        await Assert.That(cut.Markup).Contains("Second stack panel");
        await Assert.That(cut.Markup).DoesNotContain("First stack panel");
    }

    [Test]
    public async Task DockTabStrip_ArrowKey_ActivatesNextPanel()
    {
        var firstPanelId = new DockPanelId("workspace.stack-key-first");
        var secondPanelId = new DockPanelId("workspace.stack-key-second");
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.Start, order: 10), CreateContent("First key panel"));
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.Start, order: 20), CreateContent("Second key panel"));
        _dockLayoutState.Open(firstPanelId);
        _dockLayoutState.Open(secondPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.Start));
        var firstTab = cut.Find("[data-dock-tab-panel-id='workspace.stack-key-first']");

        await firstTab.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowRight" });

        await Assert.That(_dockLayoutState.GetPanel(firstPanelId)?.State.IsActive).IsFalse();
        await Assert.That(_dockLayoutState.GetPanel(secondPanelId)?.State.IsActive).IsTrue();
        await Assert.That(cut.Find("[data-testid='dock-panel-host']").GetAttribute("data-dock-panel-id")).IsEqualTo("workspace.stack-key-second");
    }

    [Test]
    public async Task DockTabStrip_ArrowKey_MovesFocusToActivatedTab()
    {
        var firstPanelId = new DockPanelId("workspace.stack-focus-first");
        var secondPanelId = new DockPanelId("workspace.stack-focus-second");
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.Start, order: 10), CreateContent("First focus panel"));
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.Start, order: 20), CreateContent("Second focus panel"));
        _dockLayoutState.Open(firstPanelId);
        _dockLayoutState.Open(secondPanelId);

        var focusService = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();
        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.Start));
        var firstTab = cut.Find("[data-dock-tab-panel-id='workspace.stack-focus-first']");

        await firstTab.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowRight" });

        await focusService.Received(1).FocusAsync("#dock-panel-tab-workspace-stack-focus-second", preventScroll: true);
        await Assert.That(cut.Find("[data-dock-tab-panel-id='workspace.stack-focus-first']").GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(cut.Find("[data-dock-tab-panel-id='workspace.stack-focus-second']").GetAttribute("tabindex")).IsEqualTo("0");
    }

    [Test]
    public async Task DockOverlayHost_RendersOnlyNonDockedOpenPanels()
    {
        var dockedPanelId = new DockPanelId("workspace.docked");
        var inspectorPanelId = new DockPanelId("workspace.inspector");
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Docked panel"));
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 20), CreateContent("Inspector panel"));
        _dockLayoutState.Open(dockedPanelId);
        _dockLayoutState.Open(inspectorPanelId);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));

        await Assert.That(cut.Markup).Contains("Inspector panel");
        await Assert.That(cut.Markup).DoesNotContain("Docked panel");
        await Assert.That(cut.Find("[data-dock-panel-id='workspace.inspector']").GetAttribute("data-dock-mode")).IsEqualTo("inspector");
    }

    [Test]
    public async Task DockOverlayHost_Mobile_RendersDockedPanelsAsTemporaryOverlays()
    {
        var dockedPanelId = new DockPanelId("workspace.mobile-overlay");
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Mobile overlay panel"));
        _dockLayoutState.UpdateViewport(390, isMobile: true);
        _dockLayoutState.Open(dockedPanelId, DockLayoutChangeReason.UserAction);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.IsMobile, true));

        await Assert.That(cut.Markup).Contains("Mobile overlay panel");
        await Assert.That(cut.Find("[data-dock-panel-id='workspace.mobile-overlay']").GetAttribute("data-dock-mode")).IsEqualTo("temporary");
        await Assert.That(cut.FindAll("[data-testid='dock-resize-handle']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task DockOverlayHost_Mobile_ProjectedDockedPanelDoesNotOpenOverlayByDefault()
    {
        var dockedPanelId = new DockPanelId("workspace.mobile-default-closed");
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Workspace, DockSide.Start, DockMode.Docked, order: 10), CreateContent("Default closed panel"));
        _dockLayoutState.Open(dockedPanelId, DockLayoutChangeReason.Refresh);
        _dockLayoutState.UpdateViewport(390, isMobile: true);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.IsMobile, true));

        await Assert.That(cut.Markup).DoesNotContain("Default closed panel");
    }

    [Test]
    public async Task DockOverlayHost_DesktopResizedToMobile_ProjectedDockedPanelDoesNotOpenOverlay()
    {
        var dockedPanelId = new DockPanelId("workspace.desktop-resize-mobile");
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Workspace, DockSide.Start, DockMode.Docked, order: 10), CreateContent("Resize test panel"));
        _dockLayoutState.UpdateViewport(1440, isMobile: false);
        _dockLayoutState.Open(dockedPanelId, DockLayoutChangeReason.Refresh);

        _dockLayoutState.UpdateViewport(390, isMobile: true);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.IsMobile, true));

        await Assert.That(cut.Markup).DoesNotContain("Resize test panel");
    }

    [Test]
    public async Task DockOverlayHost_OpeningEndPanel_UndocksStartPanelWithoutOpeningOverlayDrawer()
    {
        var startPanelId = new DockPanelId("workspace.tablet-start");
        var endPanelId = new DockPanelId("workspace.tablet-end");

        _dockLayoutState.Register(CreateDescriptor(startPanelId, DockScope.Workspace, DockSide.Start, DockMode.Docked, defaultWidth: 240, order: 10), CreateContent("Tablet start panel"));
        _dockLayoutState.Register(CreateDescriptor(endPanelId, DockScope.Workspace, DockSide.End, DockMode.Docked, defaultWidth: 360, order: 20), CreateContent("Tablet end panel"));

        _dockLayoutState.UpdateViewport(900, isMobile: false, DockScope.Workspace, minimumContentWidth: 375);
        _dockLayoutState.Open(startPanelId, DockLayoutChangeReason.Refresh);

        await Assert.That(_dockLayoutState.ShouldRenderDockedPanelAsOverlay(_dockLayoutState.GetPanel(startPanelId)!)).IsFalse();

        _dockLayoutState.Open(endPanelId, DockLayoutChangeReason.UserAction);

        await Assert.That(_dockLayoutState.ShouldRenderDockedPanelAsOverlay(_dockLayoutState.GetPanel(startPanelId)!)).IsTrue();

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.IsMobile, false));

        await Assert.That(cut.Markup).DoesNotContain("Tablet start panel");
        await Assert.That(_dockLayoutState.GetPanel(startPanelId)!.State.IsActive).IsFalse();
    }

    [Test]
    public async Task DockOverlayHost_Mobile_RendersOnlyActiveOverlaySurfaceWhenMultiplePanelsRemainOpen()
    {
        var customizePanelId = new DockPanelId("workspace.mobile-customize");
        var previewPanelId = new DockPanelId("workspace.mobile-preview");
        _dockLayoutState.Register(CreateDescriptor(customizePanelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Mobile customize panel"));
        _dockLayoutState.Register(CreateDescriptor(previewPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 20), CreateContent("Mobile preview panel"));
        _dockLayoutState.UpdateViewport(390, isMobile: true);
        _dockLayoutState.Open(customizePanelId);
        _dockLayoutState.Open(previewPanelId);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.IsMobile, true));

        await Assert.That(_dockLayoutState.GetPanel(customizePanelId)?.State.IsOpen).IsTrue();
        await Assert.That(_dockLayoutState.GetPanel(previewPanelId)?.State.IsOpen).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("Mobile customize panel");
        await Assert.That(cut.Markup).Contains("Mobile preview panel");
        await Assert.That(cut.FindAll("[aria-modal='true']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task DockOverlayHost_MobileToDesktop_RemovesProjectedDockedOverlayAfterCloseAnimation()
    {
        var dockedPanelId = new DockPanelId("workspace.mobile-to-desktop");
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Mobile projected panel"));
        _dockLayoutState.UpdateViewport(390, isMobile: true);
        _dockLayoutState.Open(dockedPanelId);
        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.IsMobile, true));

        _dockLayoutState.UpdateViewport(1280, isMobile: false);
        cut.Render(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.IsMobile, false));

        await Assert.That(_dockLayoutState.GetPanel(dockedPanelId)?.State.IsOpen).IsTrue();
        await Assert.That(cut.Find("[data-dock-panel-id='workspace.mobile-to-desktop']").GetAttribute("data-dock-mode")).IsEqualTo("temporary");
        await Assert.That(cut.Find(".dock-overlay-host__slot--closing")).IsNotNull();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-dock-panel-id='workspace.mobile-to-desktop']").Count != 0)
            {
                throw new InvalidOperationException("Mobile-projected docked panel stayed mounted after desktop viewport transition.");
            }
        }, timeout: TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DockOverlayHost_OpeningOverlay_SavesFocusLocksScrollAndMovesFocusToPanel()
    {
        var inspectorPanelId = new DockPanelId("workspace.inspector-focus");
        var scrollManager = Substitute.For<IScrollManager>();
        ConfigureScrollManager(scrollManager);
        _ctx.Services.AddSingleton(scrollManager);
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 10), CreateContent("Inspector focus panel"));
        _dockLayoutState.Open(inspectorPanelId);
        var focusService = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();

        _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));

        await focusService.Received(1).SaveFocusAsync();
        await scrollManager.Received(1).LockScrollAsync("body", "scroll-locked");
        await focusService.Received(1).FocusAsync(
            "[data-testid='dock-overlay-host'][data-dock-scope='workspace'] [data-testid='dock-panel-host']",
            preventScroll: true);
    }

    [Test]
    public async Task DockOverlayHost_OpeningOverlay_EnablesFocusTrapForTemporaryChromeOnly()
    {
        var inspectorPanelId = new DockPanelId("workspace.inspector-focus-trap");
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 10), CreateContent("Inspector focus trap panel"));
        _dockLayoutState.Open(inspectorPanelId);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));
        var focusTrap = cut.FindComponent<MudFocusTrap>();

        await Assert.That(focusTrap.Instance.Disabled).IsFalse();
        await Assert.That(focusTrap.Instance.DefaultFocus).IsEqualTo(DefaultFocus.FirstChild);
    }

    [Test]
    public async Task DockOverlayHost_OpeningOverlay_RendersModalDialogSemantics()
    {
        var inspectorPanelId = new DockPanelId("workspace.inspector-modal-semantics");
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 10), CreateContent("Inspector modal semantics panel"));
        _dockLayoutState.Open(inspectorPanelId);

        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));
        var panel = cut.Find("[data-testid='dock-panel-host'][data-dock-panel-id='workspace.inspector-modal-semantics']");

        await Assert.That(panel.GetAttribute("role")).IsEqualTo("dialog");
        await Assert.That(panel.GetAttribute("aria-modal")).IsEqualTo("true");
        await Assert.That(panel.GetAttribute("aria-label")).IsEqualTo("Panel workspace.inspector-modal-semantics");
    }

    [Test]
    public async Task DockSideHost_SplitStack_DoesNotRenderBackdropFocusTrapOrModalSemantics()
    {
        var firstPanelId = new DockPanelId("workspace.split-nonmodal-first");
        var secondPanelId = new DockPanelId("workspace.split-nonmodal-second");
        _dockLayoutState.Register(CreateDescriptor(firstPanelId, DockScope.Workspace, DockSide.End, order: 10, stackStrategy: DockPanelStackStrategy.Split), CreateContent("First non-modal split panel"));
        _dockLayoutState.Register(CreateDescriptor(secondPanelId, DockScope.Workspace, DockSide.End, order: 20, stackStrategy: DockPanelStackStrategy.Split), CreateContent("Second non-modal split panel"));
        _dockLayoutState.Open(firstPanelId);
        _dockLayoutState.Open(secondPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));

        await Assert.That(cut.FindAll("[data-testid='dock-overlay-backdrop']").Count).IsEqualTo(0);
        await Assert.That(cut.FindComponents<MudFocusTrap>().Count).IsEqualTo(0);

        var panels = cut.FindAll("[data-testid='dock-panel-host']");
        await Assert.That(panels.Count).IsEqualTo(2);
        await Assert.That(panels.All(panel => panel.GetAttribute("role") == "complementary")).IsTrue();
        await Assert.That(panels.Any(panel => panel.HasAttribute("aria-modal"))).IsFalse();
    }

    [Test]
    public async Task DockSideHost_DockedPanel_DoesNotRenderFocusTrap()
    {
        var dockedPanelId = new DockPanelId("workspace.docked-no-focus-trap");
        _dockLayoutState.Register(CreateDescriptor(dockedPanelId, DockScope.Workspace, DockSide.End, DockMode.Docked, order: 10), CreateContent("Docked panel without focus trap"));
        _dockLayoutState.Open(dockedPanelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));

        await Assert.That(cut.FindComponents<MudFocusTrap>().Count).IsEqualTo(0);
        var panel = cut.Find("[data-testid='dock-panel-host'][data-dock-panel-id='workspace.docked-no-focus-trap']");

        await Assert.That(panel.GetAttribute("role")).IsEqualTo("complementary");
        await Assert.That(panel.HasAttribute("aria-modal")).IsFalse();
        await Assert.That(cut.Markup).Contains("Docked panel without focus trap");
    }

    [Test]
    public async Task DockOverlayHost_EscapeClosesActiveOverlayAndRestoresFocus()
    {
        var inspectorPanelId = new DockPanelId("workspace.inspector-escape");
        var scrollManager = Substitute.For<IScrollManager>();
        ConfigureScrollManager(scrollManager);
        _ctx.Services.AddSingleton(scrollManager);
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 10), CreateContent("Inspector escape panel"));
        _dockLayoutState.Open(inspectorPanelId);
        var focusService = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();
        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));
        var host = cut.Find("[data-testid='dock-overlay-host']");

        cut.WaitForAssertion(() =>
        {
            scrollManager.Received(1).LockScrollAsync("body", "scroll-locked");
            focusService.Received(1).SaveFocusAsync();
        }, timeout: TimeSpan.FromSeconds(2));

        await host.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Escape" });

        await Assert.That(_dockLayoutState.GetPanel(inspectorPanelId)?.State.IsOpen).IsFalse();
        cut.WaitForAssertion(() =>
        {
            scrollManager.Received(1).UnlockScrollAsync("body", "scroll-locked");
            focusService.Received(1).RestoreFocusAsync("#main-content");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task DockOverlayHost_BackdropClick_KeepsPanelMountedWithClosingClassBeforeUnmount()
    {
        var inspectorPanelId = new DockPanelId("workspace.inspector-closing-animation");
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 10), CreateContent("Inspector closing panel"));
        _dockLayoutState.Open(inspectorPanelId);
        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));

        await cut.Find("[data-testid='dock-overlay-backdrop']").ClickAsync(new MouseEventArgs());

        await Assert.That(_dockLayoutState.GetPanel(inspectorPanelId)?.State.IsOpen).IsFalse();
        await Assert.That(cut.Find("[data-dock-panel-id='workspace.inspector-closing-animation']").TextContent).Contains("Inspector closing panel");
        await Assert.That(cut.Find(".dock-overlay-host__slot--closing")).IsNotNull();
        await Assert.That(cut.Find(".dock-overlay-host__backdrop--closing")).IsNotNull();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-dock-panel-id='workspace.inspector-closing-animation']").Count != 0)
            {
                throw new InvalidOperationException("Closing overlay panel was not unmounted after its reverse animation window.");
            }
        }, timeout: TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DockOverlayHost_BackdropClickClosesActiveOverlay()
    {
        var inspectorPanelId = new DockPanelId("workspace.inspector-backdrop");
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 10), CreateContent("Inspector backdrop panel"));
        _dockLayoutState.Open(inspectorPanelId);
        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));

        await cut.Find("[data-testid='dock-overlay-backdrop']").ClickAsync(new MouseEventArgs());

        await Assert.That(_dockLayoutState.GetPanel(inspectorPanelId)?.State.IsOpen).IsFalse();
    }

    [Test]
    public async Task DockOverlayHost_ClosingOverlay_DisablesFocusTrapDuringExitAnimation()
    {
        var inspectorPanelId = new DockPanelId("workspace.inspector-closing-focus-trap");
        _dockLayoutState.Register(CreateDescriptor(inspectorPanelId, DockScope.Workspace, DockSide.End, DockMode.Inspector, order: 10), CreateContent("Inspector closing focus trap panel"));
        _dockLayoutState.Open(inspectorPanelId);
        var cut = _ctx.Render<DockOverlayHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace));

        await cut.Find("[data-testid='dock-overlay-backdrop']").ClickAsync(new MouseEventArgs());

        await Assert.That(_dockLayoutState.GetPanel(inspectorPanelId)?.State.IsOpen).IsFalse();
        await Assert.That(cut.FindComponent<MudFocusTrap>().Instance.Disabled).IsTrue();
        await Assert.That(cut.Find(".dock-overlay-host__slot--closing")).IsNotNull();
    }

    [Test]
    public async Task DockPanelHost_RendersResizeHandleForResizableDockedInlinePanels()
    {
        var panelId = new DockPanelId("workspace.resize");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await Assert.That(handle.GetAttribute("role")).IsEqualTo("separator");
        await Assert.That(handle.GetAttribute("aria-valuemin")).IsEqualTo("240");
        await Assert.That(handle.GetAttribute("aria-valuemax")).IsEqualTo("520");
        await Assert.That(handle.GetAttribute("aria-valuenow")).IsEqualTo("320");
        await Assert.That(handle.GetAttribute("aria-controls")).IsEqualTo("dock-panel-body-workspace-resize");
        await Assert.That(handle.GetAttribute("data-dock-resize-panel-id")).IsEqualTo("workspace.resize");
        await Assert.That(cut.Find("#dock-panel-body-workspace-resize").TextContent).Contains("Resizable panel");
    }

    [Test]
    public async Task DockResizeHandle_KeyboardResize_UpdatesDockPanelWidth()
    {
        var panelId = new DockPanelId("workspace.resize-keyboard");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowLeft" });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(336);
    }

    [Test]
    public async Task DockResizeHandle_StartSideKeyboardResize_UsesOppositeArrowDirection()
    {
        var panelId = new DockPanelId("workspace.resize-start");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.Start, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.Start));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowLeft" });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(304);
    }

    [Test]
    public async Task DockResizeHandle_ShiftKeyboardResize_ClampsToDescriptorBounds()
    {
        var panelId = new DockPanelId("workspace.resize-clamp");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "End" });
        await handle.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowLeft", ShiftKey = true });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(520);
    }

    [Test]
    public async Task DockResizeHandle_UnsupportedKey_DoesNotRequestResize()
    {
        var panelId = new DockPanelId("workspace.resize-tab");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Tab" });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(320);
    }

    [Test]
    public async Task DockResizeHandle_PointerDrag_UpdatesEndPanelWidth()
    {
        var panelId = new DockPanelId("workspace.resize-pointer-end");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientX = 400, PointerId = 1, IsPrimary = true });
        await handle.TriggerEventAsync("onpointermove", new PointerEventArgs { ClientX = 360, PointerId = 1, IsPrimary = true });
        await handle.TriggerEventAsync("onpointerup", new PointerEventArgs { ClientX = 360, PointerId = 1, IsPrimary = true });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(360);
    }

    [Test]
    public async Task DockResizeHandle_PointerDrag_IgnoresNonPrimaryPointers()
    {
        var panelId = new DockPanelId("workspace.resize-pointer-secondary");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientX = 400, PointerId = 1, IsPrimary = false });
        await handle.TriggerEventAsync("onpointermove", new PointerEventArgs { ClientX = 360, PointerId = 1, IsPrimary = false });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(320);
    }

    [Test]
    public async Task DockResizeHandle_PointerDrag_IgnoresMismatchedPointerId()
    {
        var panelId = new DockPanelId("workspace.resize-pointer-id");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientX = 400, PointerId = 7, IsPrimary = true });
        await handle.TriggerEventAsync("onpointermove", new PointerEventArgs { ClientX = 360, PointerId = 8, IsPrimary = true });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(320);
    }

    [Test]
    public async Task DockResizeHandle_PointerDrag_InvokesPointerCaptureModule()
    {
        var panelId = new DockPanelId("workspace.resize-pointer-capture");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);
        var module = _ctx.JSInterop.SetupModule("/js/dock-resize.js");
        module.SetupVoid("setPointerCapture", _ => true).SetVoidResult();
        module.SetupVoid("releasePointerCapture", _ => true).SetVoidResult();

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientX = 400, PointerId = 17, IsPrimary = true });
        await handle.TriggerEventAsync("onpointermove", new PointerEventArgs { ClientX = 360, PointerId = 17, IsPrimary = true });
        await handle.TriggerEventAsync("onpointerup", new PointerEventArgs { ClientX = 360, PointerId = 17, IsPrimary = true });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(360);
        await Assert.That(_ctx.JSInterop.Invocations.Count(invocation => invocation.Identifier == "import")).IsEqualTo(1);
        await Assert.That(module.Invocations.Count(invocation => invocation.Identifier == "setPointerCapture")).IsEqualTo(1);
        await Assert.That(module.Invocations.Count(invocation => invocation.Identifier == "releasePointerCapture")).IsEqualTo(1);
    }

    [Test]
    public async Task DockResizeHandle_PointerDrag_UpdatesStartPanelWidthWithOppositeDirection()
    {
        var panelId = new DockPanelId("workspace.resize-pointer-start");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.Start, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.Start));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientX = 400, PointerId = 1, IsPrimary = true });
        await handle.TriggerEventAsync("onpointermove", new PointerEventArgs { ClientX = 360, PointerId = 1, IsPrimary = true });
        await handle.TriggerEventAsync("onpointerup", new PointerEventArgs { ClientX = 360, PointerId = 1, IsPrimary = true });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(280);
    }

    [Test]
    public async Task DockResizeHandle_PointerMoveBeforePointerDown_DoesNotResize()
    {
        var panelId = new DockPanelId("workspace.resize-pointer-idle");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onpointermove", new PointerEventArgs { ClientX = 360, PointerId = 1, IsPrimary = true });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(320);
    }

    [Test]
    public async Task DockResizeHandle_PointerCancel_StopsDragResize()
    {
        var panelId = new DockPanelId("workspace.resize-pointer-cancel");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Resizable panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));
        var handle = cut.Find("[data-testid='dock-resize-handle']");

        await handle.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientX = 400, PointerId = 1, IsPrimary = true });
        await handle.TriggerEventAsync("onpointercancel", new PointerEventArgs { ClientX = 400, PointerId = 1, IsPrimary = true });
        await handle.TriggerEventAsync("onpointermove", new PointerEventArgs { ClientX = 360, PointerId = 1, IsPrimary = true });

        await Assert.That(_dockLayoutState.GetPanel(panelId)?.State.Width).IsEqualTo(320);
    }

    [Test]
    public async Task DockPanelHost_DoesNotRenderResizeHandleForNonResizablePanels()
    {
        var panelId = new DockPanelId("workspace.fixed");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10, isResizable: false), CreateContent("Fixed panel"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));

        await Assert.That(cut.FindAll("[data-testid='dock-resize-handle']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task DockPanelHost_DoesNotRenderGenericHeaderChrome()
    {
        var panelId = new DockPanelId("workspace.content-owned-chrome");
        _dockLayoutState.Register(CreateDescriptor(panelId, DockScope.Workspace, DockSide.End, order: 10), CreateContent("Content owned chrome"));
        _dockLayoutState.Open(panelId);

        var cut = _ctx.Render<DockSideHost>(parameters => parameters
            .Add(component => component.Scope, DockScope.Workspace)
            .Add(component => component.Side, DockSide.End));

        await Assert.That(cut.FindAll(".dock-panel-host__header").Count).IsEqualTo(0);
        await Assert.That(cut.Find("[data-testid='dock-panel-host']").HasAttribute("aria-labelledby")).IsFalse();
    }

    private static DockPanelDescriptor CreateDescriptor(
        DockPanelId id,
        DockScope scope,
        DockSide side,
        DockMode mode = DockMode.Docked,
        int order = 10,
        bool isResizable = true,
        DockPanelStackStrategy? stackStrategy = null,
        int defaultWidth = 320)
    {
        return new DockPanelDescriptor(
            id,
            scope,
            side,
            mode,
            $"Panel {id.Value}",
            $"Panel {id.Value}",
            DefaultWidth: defaultWidth,
            MinWidth: 240,
            MaxWidth: 520,
            order,
            IsResizable: isResizable,
            CanClose: true,
            PersistState: true,
            StackStrategy: stackStrategy ?? DockPanelStackStrategy.Tabbed);
    }

    private static RenderFragment CreateContent(string text)
    {
        return builder => builder.AddContent(0, text);
    }

    private static void ConfigureScrollManager(IScrollManager scrollManager)
    {
        scrollManager.LockScrollAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(default(ValueTask));
        scrollManager.UnlockScrollAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(default(ValueTask));
    }

    private void ConfigureViewport(Breakpoint breakpoint, int? width = null)
    {
        var viewportService = Substitute.For<IBrowserViewportService>();
        viewportService.SubscribeAsync(Arg.Any<IBrowserViewportObserver>(), Arg.Any<bool>())
            .Returns(callInfo =>
            {
                var observer = callInfo.Arg<IBrowserViewportObserver>();
                if (observer is null)
                {
                    return Task.CompletedTask;
                }

                return observer.NotifyBrowserViewportChangeAsync(new BrowserViewportEventArgs(
                    Guid.NewGuid(),
                    new BrowserWindowSize { Width = width ?? (breakpoint is Breakpoint.Xs or Breakpoint.Sm ? 390 : 1280), Height = 844 },
                    breakpoint,
                    isImmediate: true));
            });
        viewportService.UnsubscribeAsync(Arg.Any<IBrowserViewportObserver>()).Returns(Task.CompletedTask);
        _ctx.Services.AddSingleton(viewportService);
    }
}
