using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Services.Docking;

namespace Explore.Blazor.Client.Tests.Components.Docking;

public sealed class EventDockPanelsTests
{
    [Test]
    public async Task CustomizeView_UsesWorkspaceEndDockContract()
    {
        var descriptor = EventDockPanels.CustomizeView;

        await Assert.That(descriptor.Id.Value).IsEqualTo("events.customize-view");
        await Assert.That(descriptor.Scope).IsEqualTo(DockScope.Workspace);
        await Assert.That(descriptor.Side).IsEqualTo(DockSide.End);
        await Assert.That(descriptor.DefaultMode).IsEqualTo(DockMode.Docked);
        await Assert.That(descriptor.Title).IsEqualTo("Customize View");
        await Assert.That(descriptor.AriaLabel).IsEqualTo("Customize event list");
        await Assert.That(descriptor.DefaultWidth).IsEqualTo(320);
        await Assert.That(descriptor.MinWidth).IsEqualTo(280);
        await Assert.That(descriptor.MaxWidth).IsEqualTo(480);
        await Assert.That(descriptor.Order).IsEqualTo(10);
        await Assert.That(descriptor.IsResizable).IsTrue();
        await Assert.That(descriptor.CanClose).IsTrue();
        await Assert.That(descriptor.PersistState).IsTrue();
        await Assert.That(descriptor.StackStrategy).IsEqualTo(DockPanelStackStrategy.Split);
        await Assert.That(descriptor.MobilePresentation).IsEqualTo(DockPanelMobilePresentation.TemporaryOverlay);
        await Assert.That(descriptor.ResponsivePriority).IsEqualTo(20);
        await Assert.That(descriptor.CanAutoCloseWhenConstrained).IsFalse();
    }

    [Test]
    public async Task EventPreview_UsesWorkspaceInspectorContractWithoutPersistence()
    {
        var descriptor = EventDockPanels.EventPreview;

        await Assert.That(descriptor.Id.Value).IsEqualTo("events.event-preview");
        await Assert.That(descriptor.Scope).IsEqualTo(DockScope.Workspace);
        await Assert.That(descriptor.Side).IsEqualTo(DockSide.End);
        await Assert.That(descriptor.DefaultMode).IsEqualTo(DockMode.Inspector);
        await Assert.That(descriptor.Title).IsEqualTo("Event Preview");
        await Assert.That(descriptor.AriaLabel).IsEqualTo("Event preview");
        await Assert.That(descriptor.DefaultWidth).IsEqualTo(440);
        await Assert.That(descriptor.MinWidth).IsEqualTo(360);
        await Assert.That(descriptor.MaxWidth).IsEqualTo(560);
        await Assert.That(descriptor.Order).IsEqualTo(20);
        await Assert.That(descriptor.IsResizable).IsFalse();
        await Assert.That(descriptor.CanClose).IsTrue();
        await Assert.That(descriptor.PersistState).IsFalse();
        await Assert.That(descriptor.StackStrategy).IsEqualTo(DockPanelStackStrategy.Split);
        await Assert.That(descriptor.MobilePresentation).IsEqualTo(DockPanelMobilePresentation.TemporaryOverlay);
        await Assert.That(descriptor.ResponsivePriority).IsEqualTo(30);
        await Assert.That(descriptor.CanAutoCloseWhenConstrained).IsFalse();
    }
}
