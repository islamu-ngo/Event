namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRealmDoctorResultDto
{
    public string OverallStatus { get; set; } = "blocked";
    public string Message { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string Authority { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string? ApiClientId { get; init; }
    public IReadOnlyList<KeycloakRealmDoctorCheckDto> Checks { get; set; } = [];
}
