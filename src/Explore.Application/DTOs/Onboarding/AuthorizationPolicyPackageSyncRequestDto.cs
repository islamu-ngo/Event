namespace Explore.Application.DTOs.Onboarding;

public sealed record AuthorizationPolicyPackageSyncRequestDto
{
    public string? AdminUsername { get; init; }

    public string? AdminPassword { get; init; }

    /// <summary>Reads current store metadata without publishing or reloading any policies.</summary>
    public bool InspectOnly { get; init; }

    public override string ToString() => nameof(AuthorizationPolicyPackageSyncRequestDto);
}
