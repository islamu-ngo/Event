using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record RecordModerationContributionSelectionCommand : ModerationQualificationCommand
{
    public required Guid SelectionId { get; init; }
    public required Guid EffectId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid DecisionId { get; init; }
    public required long ExpectedDecisionRevision { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationPolicyContribution Contribution { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RecordModerationContributionSelectionSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SelectionId = SelectionId, EffectId = EffectId, CaseId = CaseId, ExpectedCaseRevision = ExpectedCaseRevision,
        DecisionId = DecisionId, ExpectedDecisionRevision = ExpectedDecisionRevision, PolicyId = PolicyId,
        ExpectedPolicyRevision = ExpectedPolicyRevision, PublicationId = PublicationId, ProfileId = ProfileId,
        Contribution = Contribution, ValidUntil = ValidUntil
    };
}
