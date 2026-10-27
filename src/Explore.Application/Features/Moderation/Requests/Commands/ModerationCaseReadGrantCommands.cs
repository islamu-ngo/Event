using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record GrantModerationCaseReadPurposeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid GranteeUserId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }

    public override string ToString() => nameof(GrantModerationCaseReadPurposeCommand);
}

public sealed record RevokeModerationCaseReadPurposeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid GrantId { get; init; }
    public required long ExpectedGrantRevision { get; init; }

    public override string ToString() => nameof(RevokeModerationCaseReadPurposeCommand);
}
