namespace Explore.Application.DTOs.UiShell;

public sealed record UiShellContextDto
{
    public Guid TenantId { get; init; }
    public string DeploymentMode { get; init; } = "SingleTenant";
    public WorkspaceAvailabilityDto Workspaces { get; init; } = new();
    public IReadOnlyList<ManagedActorDto> ManagedActors { get; init; } = [];
    public IReadOnlyList<SettingsScopeDto> SettingsScopes { get; init; } = [];
    public Guid? PinnedActorId { get; init; }
    public UiShellNavigationDefaultsDto NavigationDefaults { get; init; } = new();
}

public sealed record WorkspaceAvailabilityDto
{
    public bool Events { get; init; } = true;
    public bool Studio { get; init; }
    public bool Ai { get; init; }
    public bool Settings { get; init; } = true;
}

public sealed record ManagedActorDto
{
    public Guid ActorId { get; init; }
    public Guid ScopeId { get; init; }
    public string ActorType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record SettingsScopeDto
{
    public string Scope { get; init; } = string.Empty;
    public Guid? ScopeId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record UiShellNavigationDefaultsDto
{
    public string Events { get; init; } = "Docked";
    public string Studio { get; init; } = "Docked";
    public string Ai { get; init; } = "Docked";
    public bool AllowUserOverride { get; init; } = true;
    public string OrganizerDefaultWorkspace { get; init; } = "Events";
}
