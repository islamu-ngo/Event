using System.Collections.Immutable;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Policy;

public static partial class ModerationPolicyProposedStateCompiler
{
    private static void ValidateHistory(
        ModerationPolicyProposedState proposed, ModerationPolicyProposedState current,
        ImmutableArray<ModerationPolicyDiagnostic>.Builder issues)
    {
        Check(current.Classes, proposed.Classes, item => item.Reference, (left, right) => left == right,
            (left, right) => left.Ground == right.Ground && left.Basis == right.Basis);
        Check(current.Reasons, proposed.Reasons, item => item.Reference, (left, right) => left == right,
            (left, right) => left.Class.OwnerId == right.Class.OwnerId && left.Class.Code == right.Class.Code);
        Check(current.Profiles, proposed.Profiles, item => item.Rule, SameProfile,
            (left, right) => left.ReferenceProfile == right.ReferenceProfile && left.Id != right.Id);

        void Check<T>(IEnumerable<T> previous, IEnumerable<T> next,
            Func<T, ModerationRuleReference> reference, Func<T, T, bool> same, Func<T, T, bool> sameFamily)
        {
            var old = previous.ToArray();
            var supplied = next.ToArray();
            foreach (var item in old)
            {
                var retained = supplied.Where(candidate => reference(candidate) == reference(item)).ToArray();
                if (retained.Length == 0)
                    issues.Add(new(ModerationPolicyDiagnosticKind.MissingHistoricalDefinition,
                        ModerationPolicyDiagnosticSeverity.Error, reference(item)));
                else if (retained.Length != 1 || !same(item, retained[0]))
                    issues.Add(new(ModerationPolicyDiagnosticKind.MeaningRevisionConflict,
                        ModerationPolicyDiagnosticSeverity.Error, reference(item)));
            }
            foreach (var item in supplied.Where(item => reference(item) is not null
                         && !old.Any(previousItem => reference(previousItem) == reference(item))))
            {
                var family = old.Where(previousItem => reference(previousItem).OwnerId == reference(item).OwnerId
                    && reference(previousItem).Code == reference(item).Code).ToArray();
                if (family.Any(previousItem => reference(previousItem).Revision >= reference(item).Revision
                        || !sameFamily(previousItem, item)))
                    issues.Add(new(ModerationPolicyDiagnosticKind.MeaningRevisionConflict,
                        ModerationPolicyDiagnosticSeverity.Error, reference(item)));
            }
        }
    }

    private static bool SameProfile(ModerationPolicyProfileDefinition left, ModerationPolicyProfileDefinition right) =>
        left.Id == right.Id && left.Rule == right.Rule && left.Reason == right.Reason
        && left.ReferenceProfile == right.ReferenceProfile && left.ReferenceRevision == right.ReferenceRevision
        && left.Grade == right.Grade && SameSet(left.ScopeKinds, right.ScopeKinds)
        && SameSet(left.SubjectKinds, right.SubjectKinds) && SameSet(left.Effects, right.Effects)
        && left.Bundle == right.Bundle && left.ProtectionPurpose == right.ProtectionPurpose
        && SameSet(left.Strategies, right.Strategies) && left.Timing == right.Timing
        && left.RequiredApprovals == right.RequiredApprovals
        && left.ContestationCalendarMonths == right.ContestationCalendarMonths && left.Recognition == right.Recognition;
}
