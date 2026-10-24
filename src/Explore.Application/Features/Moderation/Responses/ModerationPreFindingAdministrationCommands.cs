using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Responses;

/// <summary>Explicit custody intent; current proposer, privacy owner and reviewer authority remain native facts.</summary>
public sealed record ProposeModerationPreFindingCustodyCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid PrivacyOwnerHumanId { get; init; }
    public required ModerationPreFindingCustodyTerms Terms { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public override string ToString() => nameof(ProposeModerationPreFindingCustodyCommand);
}

public sealed record AcceptModerationPreFindingCustodyCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid CustodyId { get; init; }
    public required long ExpectedCustodyRevision { get; init; }
    public override string ToString() => nameof(AcceptModerationPreFindingCustodyCommand);
}

/// <summary>Independent subject-safe notice intent, never confidential occurrence or reporter narrative.</summary>
public sealed record IssueModerationPreFindingNoticeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid CustodyId { get; init; }
    public required ModerationNoticeContentRepresentative Recipient { get; init; }
    public required ModerationPreFindingText Text { get; init; }
    public required DateTimeOffset RetainUntil { get; init; }
    public override string ToString() => nameof(IssueModerationPreFindingNoticeCommand);
}

public sealed record WithdrawModerationPreFindingCustodyCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid CustodyId { get; init; }
    public required long ExpectedCustodyRevision { get; init; }
    public override string ToString() => nameof(WithdrawModerationPreFindingCustodyCommand);
}
