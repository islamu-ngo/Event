namespace Explore.Application.DTOs.Onboarding;

public sealed record OnboardingPreflightDto
{
    private List<OnboardingPreflightCheckDto> _blockingChecks = [];
    private List<OnboardingPreflightCheckDto> _warningChecks = [];

    public string DeploymentMode { get; set; } = "SingleTenant";
    public bool IsReadyToLaunch => BlockingChecks.All(check => check.Status == OnboardingPreflightCheckStatus.Pass);
    public IReadOnlyList<OnboardingPreflightCheckDto> BlockingChecks
    {
        get => _blockingChecks.AsReadOnly();
        init => _blockingChecks = value is null ? null! : value.ToList();
    }

    public IReadOnlyList<OnboardingPreflightCheckDto> WarningChecks
    {
        get => _warningChecks.AsReadOnly();
        init => _warningChecks = value is null ? null! : value.ToList();
    }

    internal void AddBlockingCheck(OnboardingPreflightCheckDto check) => _blockingChecks.Add(check);
    internal void AddWarningCheck(OnboardingPreflightCheckDto check) => _warningChecks.Add(check);
}

public sealed record OnboardingPreflightCheckDto
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Severity { get; init; } = OnboardingPreflightCheckSeverity.Blocking;
    public string Status { get; init; } = OnboardingPreflightCheckStatus.Pass;
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
}

public static class OnboardingPreflightCheckSeverity
{
    public const string Blocking = "Blocking";
    public const string Warning = "Warning";
}

public static class OnboardingPreflightCheckStatus
{
    public const string Pass = "Pass";
    public const string Fail = "Fail";
    public const string Warning = "Warning";
}
