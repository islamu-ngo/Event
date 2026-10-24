using ISLAMU.Moderation.Domain;

namespace ISLAMU.Moderation.Application;

/// <summary>Builds the exact minimized durable change pointer synchronously; never delivers or allocates retry identities.</summary>
public interface IModerationEffectPlanner
{
    ModerationPlannedEffect Plan(
        ModerationOperation operation, ModerationDecision decision, ModerationSanction? sanction,
        ModerationSanction? supersededSanction);
}
