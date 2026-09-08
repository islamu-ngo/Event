namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakProtocolMapperDesiredStateDto
{
    public string Name { get; init; } = string.Empty;
    public string MapperType { get; init; } = string.Empty;
    public string? IncludedClientAudience { get; init; }
    public bool AddToAccessToken { get; init; } = true;
    public bool AddToIdToken { get; init; }
}
