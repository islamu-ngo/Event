using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Requests finite case access; original coordinates identify custody, never permission.</summary>
public sealed record GrantModerationAdjudicatedCaseReadPurposeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid GranteeUserId { get; init; }
    public required ModerationOriginalEffectAnchor Original { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    public override string ToString() => nameof(GrantModerationAdjudicatedCaseReadPurposeCommand);
}

public sealed record RevokeModerationAdjudicatedCaseReadPurposeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid GrantId { get; init; }
    public required long ExpectedGrantRevision { get; init; }

    public override string ToString() => nameof(RevokeModerationAdjudicatedCaseReadPurposeCommand);
}
