using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Responses;

/// <summary>Recipient intent for one attributed subject case, never confidential occurrence identity.</summary>
public sealed record SubmitModerationPreFindingResponseCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid NoticeId { get; init; }
    public required long ExpectedNoticeRevision { get; init; }
    public required string Statement { get; init; }

    public override string ToString() => nameof(SubmitModerationPreFindingResponseCommand);
}

public sealed class SubmitModerationPreFindingResponseCommandValidator
    : AbstractValidator<SubmitModerationPreFindingResponseCommand>
{
    public SubmitModerationPreFindingResponseCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.CaseId).Must(IsUuidV7);
        RuleFor(command => command.NoticeId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedNoticeRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.Statement).Must(value =>
            !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value) <= 65536);
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}

public sealed class SubmitModerationPreFindingResponseCommandHandler(IModerationPreFindingRepository repository)
    : ICommandHandler<SubmitModerationPreFindingResponseCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        SubmitModerationPreFindingResponseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new SubmitModerationPreFindingResponseCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.ExecutePreFindingAsync(new RespondToModerationPreFindingNotice(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision,
            command.NoticeId, command.ExpectedNoticeRevision, command.Statement), cancellationToken);
        return ModerationPreFindingCommandResponse.FromOutcome(result, command.OperationId);
    }
}

internal static class ModerationPreFindingCommandResponse
{
    internal static BaseCommandResponse<Guid> FromOutcome(ModerationPreFindingWriteResult result, Guid operationId) =>
        result.Outcome switch
        {
            ModerationQualificationWriteOutcome.Known when result.Id is { } id =>
                BaseCommandResponse.Success(id),
            ModerationQualificationWriteOutcome.Unauthenticated => BaseCommandResponse.Authentication<Guid>(),
            ModerationQualificationWriteOutcome.NotFound or ModerationQualificationWriteOutcome.Denied =>
                BaseCommandResponse.NotFound<Guid>(),
            ModerationQualificationWriteOutcome.Conflict => BaseCommandResponse.Conflict(operationId),
            _ => BaseCommandResponse.Failure<Guid>("ModerationPreFindingUnavailable")
        };
}
