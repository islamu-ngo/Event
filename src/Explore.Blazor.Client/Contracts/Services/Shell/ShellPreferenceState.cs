namespace Explore.Blazor.Client.Contracts.Services.Shell;

public sealed record ShellPreferenceState(
    string LastWorkspace,
    Guid? LastActorId,
    string LastSettingsScopeHref);
