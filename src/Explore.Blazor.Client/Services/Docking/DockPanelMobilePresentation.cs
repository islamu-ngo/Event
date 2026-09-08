namespace Explore.Blazor.Client.Services.Docking;

public sealed record DockPanelMobilePresentation
{
    public static readonly DockPanelMobilePresentation TemporaryOverlay = new("TemporaryOverlay");
    public static readonly DockPanelMobilePresentation FullscreenOverlay = new("FullscreenOverlay");

    private DockPanelMobilePresentation(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;
}
