namespace Explore.Application.DTOs.Onboarding;

public sealed record SystemOnboardingStatusDto
{
    public bool RequiresOnboarding { get; init; }
    public string DeploymentMode { get; init; } = "SingleTenant";
}
