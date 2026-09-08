namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRealmSyncApplyRequestDto
{
    public bool BackupConfirmed { get; init; }

    public string? BootstrapAdminUsername { get; init; }

    public string? BootstrapAdminPassword { get; init; }

    public string? ApiClientId { get; init; } = "islamu-event-api";

    public IReadOnlyList<string> BlazorRedirectUris { get; init; } = [];

    public IReadOnlyList<string> BlazorWebOrigins { get; init; } = [];
}
