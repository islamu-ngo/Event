namespace Explore.Blazor.Client.Services.Docking;

public sealed record DockPanelStackStrategy
{
    public static readonly DockPanelStackStrategy Tabbed = new("Tabbed");
    public static readonly DockPanelStackStrategy Split = new("Split");

    private DockPanelStackStrategy(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;
}
