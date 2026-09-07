namespace Explore.Application.DTOs.Onboarding;

using Explore.Application.Onboarding;

public sealed record KeycloakBootstrapRequestDto
{
    public string KeycloakBaseUrl { get; init; } = string.Empty;
    public string Realm { get; init; } = string.Empty;
    public string BlazorClientId { get; init; } = string.Empty;
    public string BlazorClientSecret { get; init; } = string.Empty;
    public IReadOnlyList<string> BlazorRedirectUris { get; init; } = [];
    public IReadOnlyList<string> BlazorWebOrigins { get; init; } = [];
    public string? ApiClientId { get; init; }
    public string? ApiClientSecret { get; init; }
    public KeycloakBootstrapMode Mode { get; init; } = KeycloakBootstrapMode.PatchExistingRealm;
    public string BootstrapAdminUsername { get; init; } = string.Empty;
    public string BootstrapAdminPassword { get; init; } = string.Empty;
}
