using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.TenantOnboarding.Requests.Commands;

public sealed record SaveTenantOnboardingStepCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    private IReadOnlyList<string> _completedSteps = Array.AsReadOnly(Array.Empty<string>());

    public IReadOnlyList<string> CompletedSteps
    {
        get => _completedSteps;
        init => _completedSteps = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}
