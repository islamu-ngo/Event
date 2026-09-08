using Explore.Blazor.Client.Services.Docking;

namespace Explore.Blazor.Client.Tests.Services.Docking;

public sealed class DockLayoutStateTests
{
    private static readonly DockPanelId ShellNavId = new("shell.workspace-nav");
    private static readonly DockPanelId ShellAiId = new("shell.ai-assistant");

    [Test]
    public async Task Register_AddsClosedPanelWithDescriptorDefaults()
    {
        var state = new DockLayoutState();
        var descriptor = CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, order: 20, defaultWidth: 280);

        state.Register(descriptor, _ => { });

        var panels = state.GetPanels(DockScope.Shell, DockSide.Start);

        await Assert.That(panels.Count).IsEqualTo(1);
        await Assert.That(panels[0].Descriptor).IsEqualTo(descriptor);
        await Assert.That(panels[0].State.IsOpen).IsFalse();
        await Assert.That(panels[0].State.Width).IsEqualTo(280);
        await Assert.That(panels[0].State.Mode).IsEqualTo(DockMode.Docked);
    }

    [Test]
    public async Task Register_DuplicateId_ThrowsInvalidOperationException()
    {
        var state = new DockLayoutState();
        var descriptor = CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start);
        state.Register(descriptor, _ => { });

        var thrown = false;
        try
        {
            state.Register(descriptor, _ => { });
        }
        catch (InvalidOperationException)
        {
            thrown = true;
        }

        await Assert.That(thrown).IsTrue();
    }

    [Test]
    public async Task OpenCloseToggle_UpdateOpenAndActiveState()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start), _ => { });

        state.Open(ShellNavId);

        var opened = state.GetPanel(ShellNavId)?.State;
        await Assert.That(opened?.IsOpen).IsTrue();
        await Assert.That(opened?.IsActive).IsTrue();

        state.Toggle(ShellNavId);

        var toggledClosed = state.GetPanel(ShellNavId)?.State;
        await Assert.That(toggledClosed?.IsOpen).IsFalse();
        await Assert.That(toggledClosed?.IsActive).IsFalse();

        state.Toggle(ShellNavId);

        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsTrue();

        state.Close(ShellNavId);

        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsFalse();
    }

    [Test]
    public async Task Resize_ClampsToDescriptorBounds()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360, minWidth: 280, maxWidth: 520), _ => { });

        state.Resize(ShellAiId, 120);
        await Assert.That(state.GetPanel(ShellAiId)?.State.Width).IsEqualTo(280);

        state.Resize(ShellAiId, 900);
        await Assert.That(state.GetPanel(ShellAiId)?.State.Width).IsEqualTo(520);
    }

    [Test]
    public async Task LastChangeReason_ClassifiesPersistentAndNonPersistentChanges()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        await Assert.That(state.LastChangeReason).IsEqualTo(DockLayoutChangeReason.Registration);

        state.Open(ShellNavId);
        await Assert.That(state.LastChangeReason).IsEqualTo(DockLayoutChangeReason.UserAction);

        state.UpdateViewport(390, isMobile: true);
        await Assert.That(state.LastChangeReason).IsEqualTo(DockLayoutChangeReason.ViewportPolicy);

        state.Open(ShellNavId);
        state.ResetToDefaults();
        await Assert.That(state.LastChangeReason).IsEqualTo(DockLayoutChangeReason.Reset);

        state.RestoreSnapshot(new DockLayoutSnapshot(
            "shell",
            [new DockPanelState(ShellNavId, true, DockMode.Docked, Width: 300, Order: 10, IsActive: true)],
            TestTime.UtcNow), "shell", DockScope.Shell);
        await Assert.That(state.LastChangeReason).IsEqualTo(DockLayoutChangeReason.SnapshotRestore);
    }

    [Test]
    public async Task Resize_NonResizablePanel_ThrowsAndPreservesWidth()
    {
        var state = new DockLayoutState();
        var descriptor = CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, isResizable: false);
        state.Register(descriptor, _ => { });

        var thrown = false;
        try
        {
            state.Resize(ShellAiId, 420);
        }
        catch (InvalidOperationException)
        {
            thrown = true;
        }

        await Assert.That(thrown).IsTrue();
        await Assert.That(state.GetPanel(ShellAiId)?.State.Width).IsEqualTo(descriptor.DefaultWidth);
    }

    [Test]
    public async Task Close_NonClosablePanel_ThrowsAndPreservesOpenState()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, canClose: false), _ => { });
        state.Open(ShellNavId);

        var thrown = false;
        try
        {
            state.Close(ShellNavId);
        }
        catch (InvalidOperationException)
        {
            thrown = true;
        }

        await Assert.That(thrown).IsTrue();
        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellNavId)?.State.IsActive).IsTrue();
    }

    [Test]
    public async Task Activate_DeactivatesOtherPanelsOnSameScopeAndSideOnly()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("workspace.events.customize");
        var workspaceInspectorId = new DockPanelId("workspace.events.inspector");
        var invalidNotifications = 0;

        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, order: 20), _ => { });
        state.Register(CreateDescriptor(workspaceInspectorId, DockScope.Workspace, DockSide.End, order: 10), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End), _ => { });
        state.Changed += () =>
        {
            var activeWorkspaceEndPanels = state.GetPanels(DockScope.Workspace, DockSide.End)
                .Count(panel => panel.State.IsActive);

            if (activeWorkspaceEndPanels > 1)
            {
                invalidNotifications++;
            }
        };

        state.Open(workspaceCustomizeId);
        state.Open(ShellAiId);
        state.Open(workspaceInspectorId);

        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsActive).IsFalse();
        await Assert.That(state.GetPanel(workspaceInspectorId)?.State.IsActive).IsTrue();
        await Assert.That(state.GetPanel(ShellAiId)?.State.IsActive).IsTrue();
        await Assert.That(invalidNotifications).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateViewport_PreservesOpenStartPanelsWhenRightPanelsConstrainContent()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });
        state.UpdateViewport(970, isMobile: false);
        state.Open(ShellNavId);

        state.Open(workspaceCustomizeId);

        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
    }

    [Test]
    public async Task UpdateViewport_AllowsStartAndEndPanelsUntilContentShrinksBelowMobileWidth()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });
        state.UpdateViewport(1000, isMobile: false);

        state.Open(ShellNavId);
        state.Open(workspaceCustomizeId);

        var nav = state.GetPanel(ShellNavId)!;
        await Assert.That(nav.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(nav)).IsFalse();
    }

    [Test]
    public async Task UpdateViewport_ContentFloorProjectsStartPanelWithoutMutatingSnapshot()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.Open(ShellNavId);
        state.Open(ShellAiId);
        var before = state.CreateSnapshot("shell", DockScope.Shell);

        state.UpdateViewport(1100, isMobile: false, DockScope.Shell, minimumContentWidth: 520);

        var nav = state.GetPanel(ShellNavId)!;
        var after = state.CreateSnapshot("shell", DockScope.Shell);
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(nav)).IsTrue();
        await Assert.That(after.Panels.Select(panel => panel with { IsActive = false }))
            .IsEquivalentTo(before.Panels.Select(panel => panel with { IsActive = false }));
        await Assert.That(nav.State.IsActive).IsFalse();
        await Assert.That(state.LastChangeReason).IsEqualTo(DockLayoutChangeReason.ViewportPolicy);
    }

    [Test]
    public async Task UpdateViewport_ContentFloorsRemainIndependentPerScope()
    {
        var state = new DockLayoutState();
        var workspaceStartId = new DockPanelId("workspace.events.filters");
        var workspaceEndId = new DockPanelId("workspace.events.preview");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.Register(CreateDescriptor(workspaceStartId, DockScope.Workspace, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(workspaceEndId, DockScope.Workspace, DockSide.End, defaultWidth: 360), _ => { });
        state.Open(ShellNavId);
        state.Open(ShellAiId);
        state.Open(workspaceStartId);
        state.Open(workspaceEndId);

        state.UpdateViewport(1100, isMobile: false, DockScope.Shell, minimumContentWidth: 520);
        state.UpdateViewport(1100, isMobile: false, DockScope.Workspace, minimumContentWidth: 375);

        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(state.GetPanel(ShellNavId)!)).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(state.GetPanel(workspaceStartId)!)).IsFalse();
    }

    [Test]
    public async Task ResponsivePolicy_DesktopGeometryEvidence_AllowsLeftAndSingleRightAtDocumentedWidths()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });

        state.UpdateViewport(1280, isMobile: false);
        state.Open(ShellNavId);
        state.Open(workspaceCustomizeId);

        var navAt1280 = state.GetPanel(ShellNavId)!;
        await Assert.That(navAt1280.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(navAt1280)).IsFalse();

        state.UpdateViewport(1000, isMobile: false);

        var navAt1000 = state.GetPanel(ShellNavId)!;
        await Assert.That(navAt1000.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(navAt1000)).IsFalse();
    }

    [Test]
    public async Task ResponsivePolicy_WideDesktopGeometryEvidence_AllowsLeftAiAndCustomizeDocked()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });
        state.UpdateViewport(1760, isMobile: false);

        state.Open(ShellNavId);
        state.Open(ShellAiId);
        state.Open(workspaceCustomizeId);

        var nav = state.GetPanel(ShellNavId)!;
        await Assert.That(nav.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellAiId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(nav)).IsFalse();
    }

    [Test]
    public async Task Open_StartPanelProjectionUsesPanelsFromTheSameScope()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });
        state.UpdateViewport(970, isMobile: false);
        state.Open(workspaceCustomizeId);

        state.Open(ShellNavId);

        var nav = state.GetPanel(ShellNavId)!;
        await Assert.That(nav.State.IsOpen).IsTrue();
        await Assert.That(nav.State.Mode).IsEqualTo(DockMode.Docked);
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(nav)).IsFalse();
    }

    [Test]
    public async Task UpdateViewport_PreservesEndPanelsAcrossScopesOnMobile()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });
        state.UpdateViewport(390, isMobile: true);
        state.Open(ShellAiId);

        state.Open(workspaceCustomizeId);

        await Assert.That(state.GetPanel(ShellAiId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsActive).IsTrue();
    }

    [Test]
    public async Task UpdateViewport_MobilePreservesExplicitStartPanelWhenEndPanelOpens()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });
        state.UpdateViewport(390, isMobile: true);

        state.Open(ShellNavId);

        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellNavId)?.State.IsActive).IsTrue();

        state.Open(ShellAiId);

        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellAiId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsFalse();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(state.GetPanel(ShellNavId)!)).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(state.GetPanel(ShellAiId)!)).IsTrue();
        await Assert.That(state.GetPanel(ShellNavId)?.State.IsActive).IsFalse();
        await Assert.That(state.GetPanel(ShellAiId)?.State.IsActive).IsTrue();
    }

    [Test]
    public async Task Open_MobileActivatesOnePanelPerScopeWithoutClosingOtherSides()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.UpdateViewport(390, isMobile: true);

        state.Open(ShellAiId);
        state.Open(ShellNavId);

        await Assert.That(state.GetPanel(ShellAiId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellAiId)?.State.IsActive).IsFalse();
        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellNavId)?.State.IsActive).IsTrue();
    }

    [Test]
    public async Task UpdateViewport_PreservesMultipleOpenEndPanelsWhenNarrow()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("events.customize-view");
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End, defaultWidth: 320), _ => { });
        state.UpdateViewport(970, isMobile: false);
        state.Open(ShellNavId);
        state.Open(ShellAiId);

        state.Open(workspaceCustomizeId);

        await Assert.That(state.GetPanel(ShellNavId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellAiId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.IsOpen).IsTrue();
    }

    [Test]
    public async Task Open_StartPanelProjectsToOverlayWhenSameScopeDockedRightPanelLeavesLessThanMobileContentWidth()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, defaultWidth: 360), _ => { });
        state.UpdateViewport(970, isMobile: false);
        state.Open(ShellAiId);

        state.Open(ShellNavId);

        var nav = state.GetPanel(ShellNavId)!;
        await Assert.That(nav.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(ShellAiId)?.State.IsOpen).IsTrue();
        await Assert.That(state.ShouldRenderDockedPanelAsOverlay(nav)).IsTrue();
    }

    [Test]
    public async Task SetMode_UpdatesOnlyTargetPanel()
    {
        var state = new DockLayoutState();
        var workspaceCustomizeId = new DockPanelId("workspace.events.customize");
        var workspaceInspectorId = new DockPanelId("workspace.events.inspector");

        state.Register(CreateDescriptor(workspaceCustomizeId, DockScope.Workspace, DockSide.End), _ => { });
        state.Register(CreateDescriptor(workspaceInspectorId, DockScope.Workspace, DockSide.End), _ => { });

        state.SetMode(workspaceCustomizeId, DockMode.Overlay);

        await Assert.That(state.GetPanel(workspaceCustomizeId)?.State.Mode).IsEqualTo(DockMode.Overlay);
        await Assert.That(state.GetPanel(workspaceInspectorId)?.State.Mode).IsEqualTo(DockMode.Docked);
    }

    [Test]
    public async Task GetPanels_ReturnsPanelsOrderedByRuntimeOrder()
    {
        var state = new DockLayoutState();
        var firstId = new DockPanelId("workspace.events.first");
        var secondId = new DockPanelId("workspace.events.second");

        state.Register(CreateDescriptor(firstId, DockScope.Workspace, DockSide.End, order: 20), _ => { });
        state.Register(CreateDescriptor(secondId, DockScope.Workspace, DockSide.End, order: 10), _ => { });

        var panels = state.GetPanels(DockScope.Workspace, DockSide.End);

        await Assert.That(panels[0].Descriptor.Id).IsEqualTo(secondId);
        await Assert.That(panels[1].Descriptor.Id).IsEqualTo(firstId);
    }

    [Test]
    public async Task Snapshot_RestoresPersistentPanelStateOnly()
    {
        var state = new DockLayoutState();
        var persistentId = new DockPanelId("workspace.events.customize");
        var transientId = new DockPanelId("workspace.events.preview");

        state.Register(CreateDescriptor(persistentId, DockScope.Workspace, DockSide.End, persistState: true), _ => { });
        state.Register(CreateDescriptor(transientId, DockScope.Workspace, DockSide.End, persistState: false), _ => { });
        state.Open(persistentId);
        state.Resize(persistentId, 410);
        state.Open(transientId);

        var snapshot = state.CreateSnapshot("events", DockScope.Workspace);
        state.Close(persistentId);
        state.Resize(persistentId, 280);
        state.Close(transientId);
        state.RestoreSnapshot(snapshot, "events", DockScope.Workspace);

        await Assert.That(snapshot.LayoutKey).IsEqualTo("events");
        await Assert.That(snapshot.Panels.Count).IsEqualTo(1);
        await Assert.That(state.GetPanel(persistentId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(persistentId)?.State.Width).IsEqualTo(410);
        await Assert.That(state.GetPanel(transientId)?.State.IsOpen).IsFalse();
    }

    [Test]
    public async Task Snapshot_CreatesScopeOwnedPanelStateOnly()
    {
        var state = new DockLayoutState();
        var shellId = new DockPanelId("shell.workspace-nav");
        var workspaceId = new DockPanelId("events.customize-view");

        state.Register(CreateDescriptor(shellId, DockScope.Shell, DockSide.Start, persistState: true), _ => { });
        state.Register(CreateDescriptor(workspaceId, DockScope.Workspace, DockSide.End, persistState: true), _ => { });
        state.Open(shellId);
        state.Open(workspaceId);

        var shellSnapshot = state.CreateSnapshot("shell", DockScope.Shell);
        var workspaceSnapshot = state.CreateSnapshot("events", DockScope.Workspace);

        await Assert.That(shellSnapshot.Panels).HasSingleItem();
        await Assert.That(shellSnapshot.Panels[0].Id).IsEqualTo(shellId);
        await Assert.That(workspaceSnapshot.Panels).HasSingleItem();
        await Assert.That(workspaceSnapshot.Panels[0].Id).IsEqualTo(workspaceId);
    }

    [Test]
    public async Task RestoreSnapshot_IgnoresWrongLayoutKeyAndForeignScopePanels()
    {
        var state = new DockLayoutState();
        var shellId = new DockPanelId("shell.workspace-nav");
        var workspaceId = new DockPanelId("events.customize-view");

        state.Register(CreateDescriptor(shellId, DockScope.Shell, DockSide.Start, persistState: true), _ => { });
        state.Register(CreateDescriptor(workspaceId, DockScope.Workspace, DockSide.End, persistState: true), _ => { });

        var wrongKeySnapshot = new DockLayoutSnapshot(
            "shell",
            [new DockPanelState(workspaceId, IsOpen: true, Mode: DockMode.Docked, Width: 360, Order: 10, IsActive: true)],
            TestTime.UtcNow);
        state.RestoreSnapshot(wrongKeySnapshot, "events", DockScope.Workspace);

        await Assert.That(state.GetPanel(workspaceId)?.State.IsOpen).IsFalse();

        var mixedScopeSnapshot = new DockLayoutSnapshot(
            "events",
            [
                new DockPanelState(shellId, IsOpen: true, Mode: DockMode.Docked, Width: 300, Order: 10, IsActive: true),
                new DockPanelState(workspaceId, IsOpen: true, Mode: DockMode.Docked, Width: 360, Order: 10, IsActive: true)
            ],
            TestTime.UtcNow);
        state.RestoreSnapshot(mixedScopeSnapshot, "events", DockScope.Workspace);

        await Assert.That(state.GetPanel(shellId)?.State.IsOpen).IsFalse();
        await Assert.That(state.GetPanel(workspaceId)?.State.IsOpen).IsTrue();
    }

    [Test]
    public async Task RestoreSnapshot_IgnoresLegacyLeftNavSnapshotWhenOnlyWorkspaceNavRegistered()
    {
        var state = new DockLayoutState();
        var workspaceNavId = new DockPanelId("shell.workspace-nav");
        var legacyLeftNavId = new DockPanelId("shell.left-nav");
        state.Register(CreateDescriptor(workspaceNavId, DockScope.Shell, DockSide.Start, persistState: true), _ => { });

        var staleSnapshot = new DockLayoutSnapshot(
            "shell",
            [new DockPanelState(legacyLeftNavId, IsOpen: true, Mode: DockMode.Docked, Width: 300, Order: 10, IsActive: true)],
            TestTime.UtcNow);
        state.RestoreSnapshot(staleSnapshot, "shell", DockScope.Shell);

        await Assert.That(state.GetPanel(legacyLeftNavId)).IsNull();
        await Assert.That(state.GetPanel(workspaceNavId)?.State.IsOpen).IsFalse();
    }

    [Test]
    public async Task RestoreSnapshot_NormalizesActivePanelsPerScopeAndSide()
    {
        var state = new DockLayoutState();
        var firstId = new DockPanelId("workspace.events.first");
        var secondId = new DockPanelId("workspace.events.second");
        state.Register(CreateDescriptor(firstId, DockScope.Workspace, DockSide.End, order: 20), _ => { });
        state.Register(CreateDescriptor(secondId, DockScope.Workspace, DockSide.End, order: 10), _ => { });

        var snapshot = new DockLayoutSnapshot(
            "events",
            [
                new DockPanelState(firstId, IsOpen: true, Mode: DockMode.Docked, Width: 320, Order: 20, IsActive: true),
                new DockPanelState(secondId, IsOpen: true, Mode: DockMode.Docked, Width: 320, Order: 10, IsActive: true)
            ],
            TestTime.UtcNow);

        state.RestoreSnapshot(snapshot, "events", DockScope.Workspace);

        var activePanels = state.GetPanels(DockScope.Workspace, DockSide.End)
            .Count(panel => panel.State.IsActive);

        await Assert.That(activePanels).IsEqualTo(1);
        await Assert.That(state.GetPanel(firstId)?.State.IsActive).IsFalse();
        await Assert.That(state.GetPanel(secondId)?.State.IsActive).IsTrue();
    }

    [Test]
    public async Task RestoreSnapshot_OnMobileNormalizesActivePanelsPerScope()
    {
        var state = new DockLayoutState();
        var startId = new DockPanelId("workspace.events.filters");
        var endId = new DockPanelId("workspace.events.preview");
        state.Register(CreateDescriptor(startId, DockScope.Workspace, DockSide.Start, order: 10), _ => { });
        state.Register(CreateDescriptor(endId, DockScope.Workspace, DockSide.End, order: 20), _ => { });
        state.UpdateViewport(390, isMobile: true);

        var snapshot = new DockLayoutSnapshot(
            "events",
            [
                new DockPanelState(startId, IsOpen: true, Mode: DockMode.Docked, Width: 320, Order: 10, IsActive: true),
                new DockPanelState(endId, IsOpen: true, Mode: DockMode.Docked, Width: 320, Order: 20, IsActive: true)
            ],
            TestTime.UtcNow);

        state.RestoreSnapshot(snapshot, "events", DockScope.Workspace);

        var activePanels = state.GetPanels(DockScope.Workspace, DockSide.Start)
            .Concat(state.GetPanels(DockScope.Workspace, DockSide.End))
            .Count(panel => panel.State.IsActive);

        await Assert.That(activePanels).IsEqualTo(1);
        await Assert.That(state.GetPanel(startId)?.State.IsActive).IsTrue();
        await Assert.That(state.GetPanel(endId)?.State.IsActive).IsFalse();
    }

    [Test]
    public async Task RestoreSnapshot_PromotesFirstOpenPanelWhenSnapshotHasNoActivePanel()
    {
        var state = new DockLayoutState();
        var firstId = new DockPanelId("workspace.events.first");
        var secondId = new DockPanelId("workspace.events.second");
        state.Register(CreateDescriptor(firstId, DockScope.Workspace, DockSide.End, order: 20), _ => { });
        state.Register(CreateDescriptor(secondId, DockScope.Workspace, DockSide.End, order: 10), _ => { });

        var snapshot = new DockLayoutSnapshot(
            "events",
            [
                new DockPanelState(firstId, IsOpen: true, Mode: DockMode.Docked, Width: 320, Order: 20, IsActive: false),
                new DockPanelState(secondId, IsOpen: true, Mode: DockMode.Docked, Width: 320, Order: 10, IsActive: false)
            ],
            TestTime.UtcNow);

        state.RestoreSnapshot(snapshot, "events", DockScope.Workspace);

        var activePanels = state.GetPanels(DockScope.Workspace, DockSide.End)
            .Count(panel => panel.State.IsActive);

        await Assert.That(activePanels).IsEqualTo(1);
        await Assert.That(state.GetPanel(firstId)?.State.IsActive).IsFalse();
        await Assert.That(state.GetPanel(secondId)?.State.IsActive).IsTrue();
    }

    [Test]
    public async Task RestoreSnapshot_PreservesDescriptorCapabilities()
    {
        var state = new DockLayoutState();
        var pinnedId = new DockPanelId("workspace.events.pinned");
        state.Register(CreateDescriptor(
            pinnedId,
            DockScope.Workspace,
            DockSide.End,
            defaultWidth: 320,
            minWidth: 240,
            maxWidth: 640,
            isResizable: false,
            canClose: false), _ => { });
        state.Open(pinnedId);

        var snapshot = new DockLayoutSnapshot(
            "events",
            [new DockPanelState(pinnedId, IsOpen: false, Mode: DockMode.Docked, Width: 640, Order: 0, IsActive: false)],
            TestTime.UtcNow);

        state.RestoreSnapshot(snapshot, "events", DockScope.Workspace);

        var restored = state.GetPanel(pinnedId)?.State;
        await Assert.That(restored?.IsOpen).IsTrue();
        await Assert.That(restored?.IsActive).IsTrue();
        await Assert.That(restored?.Width).IsEqualTo(320);
    }

    [Test]
    public async Task RestoreSnapshot_ClampsResizableWidthAndIgnoresUnknownPanels()
    {
        var state = new DockLayoutState();
        var knownId = new DockPanelId("workspace.events.known");
        var unknownId = new DockPanelId("workspace.events.unknown");
        state.Register(CreateDescriptor(knownId, DockScope.Workspace, DockSide.End, defaultWidth: 320, minWidth: 280, maxWidth: 520), _ => { });

        var snapshot = new DockLayoutSnapshot(
            "events",
            [
                new DockPanelState(unknownId, IsOpen: true, Mode: DockMode.Docked, Width: 999, Order: 0, IsActive: true),
                new DockPanelState(knownId, IsOpen: true, Mode: DockMode.Docked, Width: 900, Order: 0, IsActive: true)
            ],
            TestTime.UtcNow);

        state.RestoreSnapshot(snapshot, "events", DockScope.Workspace);

        await Assert.That(state.GetPanel(unknownId)).IsNull();
        await Assert.That(state.GetPanel(knownId)?.State.IsOpen).IsTrue();
        await Assert.That(state.GetPanel(knownId)?.State.Width).IsEqualTo(520);
        await Assert.That(state.GetPanel(knownId)?.State.IsActive).IsTrue();
    }

    [Test]
    public async Task ResetToDefaults_RestoresRegisteredDescriptorDefaults()
    {
        var state = new DockLayoutState();
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start, order: 20, defaultWidth: 280), _ => { });
        state.Register(CreateDescriptor(ShellAiId, DockScope.Shell, DockSide.End, order: 10, defaultWidth: 360, minWidth: 300, maxWidth: 560), _ => { });
        state.Open(ShellNavId);
        state.Open(ShellAiId);
        state.Resize(ShellAiId, 520);
        state.SetMode(ShellAiId, DockMode.Overlay);

        state.ResetToDefaults();

        var nav = state.GetPanel(ShellNavId)?.State;
        var ai = state.GetPanel(ShellAiId)?.State;
        await Assert.That(nav?.IsOpen).IsFalse();
        await Assert.That(nav?.IsActive).IsFalse();
        await Assert.That(nav?.Width).IsEqualTo(280);
        await Assert.That(nav?.Order).IsEqualTo(20);
        await Assert.That(ai?.IsOpen).IsFalse();
        await Assert.That(ai?.IsActive).IsFalse();
        await Assert.That(ai?.Width).IsEqualTo(360);
        await Assert.That(ai?.Mode).IsEqualTo(DockMode.Docked);
        await Assert.That(ai?.Order).IsEqualTo(10);
    }

    [Test]
    public async Task ResetToDefaults_DoesNotNotifyWhenAlreadyAtDefaults()
    {
        var state = new DockLayoutState();
        var notifications = 0;
        state.Register(CreateDescriptor(ShellNavId, DockScope.Shell, DockSide.Start), _ => { });
        state.Changed += () => notifications++;

        state.ResetToDefaults();

        await Assert.That(notifications).IsEqualTo(0);
    }

    private static DockPanelDescriptor CreateDescriptor(
        DockPanelId id,
        DockScope scope,
        DockSide side,
        int order = 0,
        int defaultWidth = 320,
        int minWidth = 240,
        int maxWidth = 640,
        bool persistState = true,
        bool isResizable = true,
        bool canClose = true)
    {
        return new DockPanelDescriptor(
            id,
            scope,
            side,
            DockMode.Docked,
            $"Panel {id.Value}",
            $"Panel {id.Value}",
            defaultWidth,
            minWidth,
            maxWidth,
            order,
            isResizable,
            canClose,
            persistState);
    }
}
