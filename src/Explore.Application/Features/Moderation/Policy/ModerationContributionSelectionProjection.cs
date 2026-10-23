using System.Collections.Immutable;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Policy;

/// <summary>Pure normalized contribution round-trip, never current target qualification.</summary>
public static class ModerationContributionSelectionProjection
{
    public static ImmutableArray<ModerationContributionBinding> Bindings(Guid selectionId, ModerationPolicyContribution contribution)
    {
        var bindings = ImmutableArray.CreateBuilder<ModerationContributionBinding>();
        foreach (var row in contribution.Effects)
            bindings.Add(new()
            {
                SelectionId = selectionId,
                Ordinal = bindings.Count,
                Kind = ModerationContributionBindingKind.Effect,
                EffectKind = row.Effect.Kind,
                EffectMode = row.Effect.Mode,
                EffectTarget = row.Effect.Target,
                Capability = row.Effect.Capability,
                Execution = row.Effect.Execution,
                Strategy = null,
                Scope = row.Target.Scope,
                SubjectKind = row.Target.SubjectKind,
                SubjectId = row.Target.SubjectId,
                TargetId = row.Target.TargetId,
                TargetRevision = row.Target.TargetRevision,
                Operation = row.Target.Operation,
                WindowEnd = row.Target.WindowEnd
            });
        foreach (var row in contribution.Consequences)
            bindings.Add(new()
            {
                SelectionId = selectionId,
                Ordinal = bindings.Count,
                Kind = ModerationContributionBindingKind.Consequence,
                EffectKind = null,
                EffectMode = null,
                EffectTarget = null,
                Capability = null,
                Execution = null,
                Strategy = row.Strategy,
                Scope = row.Target.Scope,
                SubjectKind = row.Target.SubjectKind,
                SubjectId = row.Target.SubjectId,
                TargetId = row.Target.TargetId,
                TargetRevision = row.Target.TargetRevision,
                Operation = row.Target.Operation,
                WindowEnd = row.Target.WindowEnd
            });
        return bindings.ToImmutable();
    }

    public static ModerationPolicyContribution Restore(ModerationContributionSelection selection, ModerationDecision decision)
    {
        selection.ValidatePersistedState();
        decision.ValidatePersistedState();
        if (selection.DecisionId != decision.Id || selection.CaseId != decision.CaseId
            || selection.TrustDomainId != decision.Scope.TrustDomainId || selection.StartsAt != decision.StartsAt)
            throw new ArgumentException("A contribution cannot be reconstructed from another decision.");
        static ModerationPolicyTarget Target(ModerationContributionBinding row) =>
            new(row.Scope, row.SubjectKind, row.SubjectId, row.TargetId, row.TargetRevision, row.Operation, row.WindowEnd);
        return new(decision.Id, decision.Rule, decision.Scope, decision.Subject.Kind, decision.Subject.Id,
            selection.StartsAt, selection.EventWindowEnd,
            selection.Bindings.Where(row => row.Kind == ModerationContributionBindingKind.Effect)
                .OrderBy(row => row.Ordinal).Select(row => new ModerationPolicyEffectTarget(row.Effect!, Target(row))).ToImmutableArray(),
            selection.Bindings.Where(row => row.Kind == ModerationContributionBindingKind.Consequence)
                .OrderBy(row => row.Ordinal).Select(row => new ModerationPolicyConsequenceTarget(row.Strategy, Target(row))).ToImmutableArray());
    }
}
