using Explore.Domain;
using Explore.Domain.Learning;

namespace Explore.Application.Features.Learning;

/// <summary>Entity results of one original-human operation, not a reusable authorization grant.</summary>
public sealed record LearningOperationState(
    Guid TenantId,
    Guid UserId,
    string ContextKey,
    Guid? EventContextId,
    TenantLearningSpace? Space,
    UserLearningState? State,
    LearningPracticeState? Practice,
    IReadOnlyList<Actor> AvailableActors,
    bool CanManageSpace,
    bool CanManageEvent)
{
    public bool StudioAvailable { get; init; }
    public bool NormalAiAvailable { get; init; }
    public DateTime ObservedAtUtc { get; init; }
}
