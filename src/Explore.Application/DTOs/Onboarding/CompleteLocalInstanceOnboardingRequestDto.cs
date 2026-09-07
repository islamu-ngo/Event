// ABOUTME: Carries setup-only Local administrator enrollment and existing nonsecret instance settings.
// ABOUTME: Keeps temporary credentials transient and suppresses diagnostic value formatting.

using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Onboarding;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteLocalInstanceOnboardingRequestDto
{
    public Guid OperationId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string TemporaryPassword { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public CompleteInstanceOnboardingRequest Settings { get; init; } = new();

    public override string ToString() => nameof(CompleteLocalInstanceOnboardingRequestDto);
}
