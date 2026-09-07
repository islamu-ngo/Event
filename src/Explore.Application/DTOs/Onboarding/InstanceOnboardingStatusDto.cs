namespace Explore.Application.DTOs.Onboarding;

public sealed record InstanceOnboardingStatusDto
{
    public bool IsCompleted { get; init; }
    public string State { get; init; } = "Invalid";
    public string Mode { get; init; } = "Interactive";
    public string? Provider { get; init; }
    public long Generation { get; init; }
    public bool IsAuthenticated { get; init; }
    public bool IsCurrentUserInstanceAdmin { get; set; }
    public string? SelectedDeploymentMode { get; init; }
    public bool IsSetupModeActive { get; init; }
    public bool SetupSecretFromEnvironment { get; init; }
    public bool SetupTimedOut { get; init; }
    public DateTime? InstanceStartedAt { get; init; }
    public string SetupSecretState { get; set; } = "Unavailable";
    public string SetupSecretGuidance { get; set; } = "Setup access is not currently available.";
}
