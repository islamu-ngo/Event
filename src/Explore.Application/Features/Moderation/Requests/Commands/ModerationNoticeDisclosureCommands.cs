using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record ApproveModerationNoticeDisclosureCommand : ModerationQualificationCommand
{
    public required ModerationNoticeDisclosureIntent Intent { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new ApproveModerationNoticeDisclosureSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        Intent = Intent
    };
}

public sealed record RevokeModerationNoticeDisclosureCommand : ModerationQualificationCommand
{
    public required Guid ApprovalId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required long ExpectedApprovalRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RevokeModerationNoticeDisclosureSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        ApprovalId = ApprovalId, CaseId = CaseId, ExpectedCaseRevision = ExpectedCaseRevision,
        ExpectedApprovalRevision = ExpectedApprovalRevision
    };
}
