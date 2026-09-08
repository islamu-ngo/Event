namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRoleCompositeDesiredStateDto
{
    public string RoleName { get; init; } = string.Empty;
    public IReadOnlyList<string> CompositeRoleNames { get; set; } = [];
}
