namespace Explore.Application.DTOs.Onboarding;

using Explore.Application.Onboarding;

public sealed record KeycloakBootstrapResultDto
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FailureCode { get; init; }
    public KeycloakBootstrapMode Mode { get; init; }
    public string Realm { get; init; } = string.Empty;
    public string BlazorClientId { get; init; } = string.Empty;
    public string? ApiClientId { get; init; }
    public bool RealmCreated { get; init; }
    public bool BlazorClientUpdated { get; init; }
    public bool ApiClientUpdated { get; init; }
}
