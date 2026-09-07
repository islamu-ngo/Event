using Explore.Blazor.Client.Services.Docking;

namespace Explore.Blazor.Client.Tests.Services.Docking;

public sealed class DockPanelDescriptorTests
{
    [Test]
    public async Task Validate_UsesSafePolicyDefaultsWhenDescriptorOmitsPolicyMetadata()
    {
        var descriptor = new DockPanelDescriptor(
            new DockPanelId("test.default-policy"),
            DockScope.Workspace,
            DockSide.End,
            DockMode.Docked,
            Title: "Default Policy",
            AriaLabel: "Default policy panel",
            DefaultWidth: 320,
            MinWidth: 240,
            MaxWidth: 520,
            Order: 10,
            IsResizable: true,
            CanClose: true,
            PersistState: true).Validate();

        await Assert.That(descriptor.StackStrategy).IsEqualTo(DockPanelStackStrategy.Tabbed);
        await Assert.That(descriptor.MobilePresentation).IsEqualTo(DockPanelMobilePresentation.TemporaryOverlay);
        await Assert.That(descriptor.ResponsivePriority).IsEqualTo(0);
        await Assert.That(descriptor.CanAutoCloseWhenConstrained).IsFalse();
    }

    [Test]
    public async Task Validate_RejectsNegativeResponsivePriority()
    {
        var descriptor = new DockPanelDescriptor(
            new DockPanelId("test.invalid-priority"),
            DockScope.Workspace,
            DockSide.End,
            DockMode.Docked,
            Title: "Invalid Priority",
            AriaLabel: "Invalid priority panel",
            DefaultWidth: 320,
            MinWidth: 240,
            MaxWidth: 520,
            Order: 10,
            IsResizable: true,
            CanClose: true,
            PersistState: true,
            ResponsivePriority: -1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.Validate());

        await Assert.That(exception.ParamName).IsEqualTo("ResponsivePriority");
    }
}
