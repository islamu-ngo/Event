namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRealmDesiredStateBuildRequestDto
{
    public string Realm { get; init; } = string.Empty;
    public string BlazorClientId { get; init; } = string.Empty;
    public string? ApiClientId { get; init; }
    public IReadOnlyList<string> BlazorRedirectUris { get; init; } = [];
    public IReadOnlyList<string> BlazorWebOrigins { get; init; } = [];
}
