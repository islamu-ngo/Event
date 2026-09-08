using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.TenantOnboarding.Requests.Commands;

public sealed record SaveTenantOnboardingStepCommand : IRequest<BaseCommandResponse<Guid>>
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
