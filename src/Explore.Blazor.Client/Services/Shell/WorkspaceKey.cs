namespace Explore.Blazor.Client.Services.Shell;

public sealed record WorkspaceKey
{
    public static WorkspaceKey Events { get; } = new("events");
    public static WorkspaceKey Studio { get; } = new("studio");
    public static WorkspaceKey Ai { get; } = new("ai");
    public static WorkspaceKey Settings { get; } = new("settings");

    public WorkspaceKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
