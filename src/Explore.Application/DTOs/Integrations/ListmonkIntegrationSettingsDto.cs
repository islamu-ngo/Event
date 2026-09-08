namespace Explore.Application.DTOs.Integrations;

public sealed record ListmonkIntegrationSettingsDto
{
    public bool Enabled { get; init; }
    public string? InstanceUrl { get; init; }
    public int DefaultListId { get; init; }
    public bool PreconfirmSubscriptions { get; init; }
    public bool SyncOnRegistration { get; init; }
    public bool ApiUsernameConfigured { get; init; }
    public bool ApiKeyConfigured { get; init; }
    public bool CanEdit { get; init; }
}
