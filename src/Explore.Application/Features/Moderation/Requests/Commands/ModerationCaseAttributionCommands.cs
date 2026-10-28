using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>
/// Authorized review intent only. The native owner independently qualifies responsibility,
/// retained material and current grants; this command creates neither guilt nor a sanction.
/// </summary>
public sealed record AttributeModerationCaseCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid OccurrenceCaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required long EvidenceCaseRevision { get; init; }
    public required Guid ReadGrantId { get; init; }
    public required ModerationResponsibilityBasis Basis { get; init; }
    public required ModerationSubjectKind SubjectKind { get; init; }
    public required Guid SubjectId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public Guid? InvestigationSourceId { get; init; }
    public long? InvestigationSourceRevision { get; init; }

    internal ModerationCaseAttributionRequest ToRequest() => new(
        OperationId, OccurrenceCaseId, ExpectedCaseRevision, EvidenceCaseRevision,
        ReadGrantId, Basis, SubjectKind, SubjectId, ValidUntil)
    {
        InvestigationSourceId = InvestigationSourceId,
        InvestigationSourceRevision = InvestigationSourceRevision
    };

    public override string ToString() => nameof(AttributeModerationCaseCommand);
}

/// <summary>Renews the same open proceeding with fresh native qualification and an exact expected head.</summary>
public sealed record RenewModerationCaseAttributionCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid ExpectedSourceId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public required long ExpectedHeadRevision { get; init; }
    public required AttributeModerationCaseCommand Qualification { get; init; }

    internal ModerationCaseAttributionRenewalRequest ToRequest() => new(
        CaseId, ExpectedCaseRevision, ExpectedSourceId, ExpectedGeneration, ExpectedHeadRevision, Qualification.ToRequest());

    public override string ToString() => nameof(RenewModerationCaseAttributionCommand);
}

/// <summary>Withdraws current qualification without rewriting historical attribution or findings.</summary>
public sealed record WithdrawModerationCaseAttributionCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid SourceId { get; init; }
    public required Guid OperationId { get; init; }
    public required long ExpectedSourceRevision { get; init; }

    public override string ToString() => nameof(WithdrawModerationCaseAttributionCommand);
}
