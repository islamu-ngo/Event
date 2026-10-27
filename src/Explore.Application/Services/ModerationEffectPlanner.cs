using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Services;

/// <summary>
/// Retains the exact committed lifecycle coordinates without copying case evidence
/// or allocating a different identity on transaction retry. Delivery belongs to the durable drain.
/// </summary>
public sealed class ModerationEffectPlanner : IModerationEffectPlanner
{
    public ModerationPlannedEffect Plan(
        ModerationOperation operation, ModerationDecision decision,
        ModerationSanction? sanction, ModerationSanction? supersededSanction) =>
        new(operation.EffectId, operation.OperationId, decision.Id, decision.Revision,
            sanction?.Id, sanction?.Revision, decision.Subject, decision.Scope, operation.Kind,
            supersededSanction?.Id, supersededSanction?.Revision);
}
