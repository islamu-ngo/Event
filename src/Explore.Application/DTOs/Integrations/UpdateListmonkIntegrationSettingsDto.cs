namespace Explore.Application.DTOs.Integrations;

public sealed record UpdateListmonkIntegrationSettingsDto
{
    public ListmonkConnectionUpdateDto? Connection { get; init; }
    public ListmonkBehaviorUpdateDto? Behavior { get; init; }
}

public sealed record ListmonkConnectionUpdateDto
{
    public string? InstanceUrl { get; init; }
    public int DefaultListId { get; init; }
}

public sealed record ListmonkBehaviorUpdateDto
{
    public bool Enabled { get; init; }
    public bool PreconfirmSubscriptions { get; init; }
    public bool SyncOnRegistration { get; init; }
}
