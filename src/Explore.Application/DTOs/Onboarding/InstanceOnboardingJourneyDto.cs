using Explore.Application.DTOs.Instance;

namespace Explore.Application.DTOs.Onboarding;

public sealed record InstanceOnboardingJourneyDto
{
    public string State { get; init; } = "Failed";
    public string ReasonCode { get; init; } = "source_unavailable";
    public string? Generation { get; init; }
    public InstanceOnboardingStatusDto? Bootstrap { get; init; }
    public OnboardingProviderReadinessDto Authentication { get; init; } = new();
    public OnboardingProviderReadinessDto Authorization { get; init; } = new();
    public SelfHostOnboardingProfileDto? Profile { get; init; }
    public OnboardingPreflightDto? Preflight { get; init; }
    public InstanceOperatorIdentityDocumentDto? OperatorIdentity { get; init; }
    public override string ToString() => nameof(InstanceOnboardingJourneyDto);
}

public sealed record OnboardingProviderReadinessDto
{
    public string? Provider { get; init; }
    public string State { get; init; } = "Unavailable";
    public string RemediationAuthority { get; init; } = "Deployment";
    public string ReasonCode { get; init; } = "source_unavailable";
    public bool RestartRequired { get; init; }
    public string? ActionRelation { get; init; }
}
