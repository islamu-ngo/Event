namespace Explore.Application.DTOs.Onboarding;

public sealed record AuthorizationPolicyPackageSyncRequestDto
{
    public string? AdminUsername { get; init; }

    public string? AdminPassword { get; init; }
}
