using System.Collections.Immutable;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Policy;

/// <summary>Restores retained classification meaning, never current material authority or disclosure permission.</summary>
public static class ModerationClassificationControlSelectionProjection
{
    public static ModerationPolicyContribution Restore(
        ModerationClassificationControlSelection selection, ModerationPolicyCompiledDraft policy)
    {
        selection.ValidatePersistedState();
        var profile = policy.Profiles.SingleOrDefault(row => row.Definition.Id == selection.ProfileId);
        if (policy.Publication.PolicyId != selection.PolicyId
            || policy.Publication.ProposedRevision != selection.PolicyRevision
            || policy.Publication.PublicationId != selection.PublicationId || profile is null
            || profile.Reference.Id != ModerationReferenceProfileId.CFG29
            || profile.Definition.Timing.Kind != ModerationReferenceTimingKind.CurrentControlUntilQualifiedResolution)
            throw new ArgumentException("Classification control must retain its exact selected non-punitive profile.");
        var bindings = selection.Bindings.OrderBy(row => row.Ordinal).Select(row => row.Value).ToArray();
        static ModerationPolicyTarget Target(ModerationContributionBinding row) =>
            new(row.Scope, row.SubjectKind, row.SubjectId, row.TargetId, row.TargetRevision, row.Operation, row.WindowEnd);
        return new(selection.Id, profile.Definition.Rule, bindings[0].Scope, ModerationSubjectKind.Content,
            selection.EventId, selection.StartsAt, null,
            bindings.Where(row => row.Kind == ModerationContributionBindingKind.Effect)
                .Select(row => new ModerationPolicyEffectTarget(row.Effect!, Target(row))).ToImmutableArray(),
            bindings.Where(row => row.Kind == ModerationContributionBindingKind.Consequence)
                .Select(row => new ModerationPolicyConsequenceTarget(row.Strategy, Target(row))).ToImmutableArray());
    }
}
