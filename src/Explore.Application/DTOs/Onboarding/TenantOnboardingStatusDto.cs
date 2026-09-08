namespace Explore.Application.DTOs.Onboarding;

public sealed record TenantOnboardingStatusDto
{
    private IReadOnlyList<string> _completedSteps = Array.AsReadOnly(Array.Empty<string>());

    public bool IsCompleted { get; init; }
    public bool IsAuthenticated { get; init; }
    public bool IsCurrentUserTenantAdministrator { get; set; }
    public bool IsCurrentUserPlatformAdministrator { get; set; }
    public Guid TenantId { get; init; }
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    public IReadOnlyList<string> CompletedSteps
    {
        get => _completedSteps;
        init => _completedSteps = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public int ProgressPercentage { get; init; }
}
