namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRealmDoctorCheckDto
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Remediation { get; init; }
}
