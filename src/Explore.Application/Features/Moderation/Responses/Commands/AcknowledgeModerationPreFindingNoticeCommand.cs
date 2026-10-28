using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Responses.Commands;

/// <summary>Explicit recipient acknowledgement only; a GET or elapsed time cannot create response evidence.</summary>
public sealed record AcknowledgeModerationPreFindingNoticeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid OperationId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid NoticeId { get; init; }
    public required long ExpectedNoticeRevision { get; init; }

    public override string ToString() => nameof(AcknowledgeModerationPreFindingNoticeCommand);
}

public sealed class AcknowledgeModerationPreFindingNoticeCommandValidator
    : AbstractValidator<AcknowledgeModerationPreFindingNoticeCommand>
{
    public AcknowledgeModerationPreFindingNoticeCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.CaseId).Must(IsUuidV7);
        RuleFor(command => command.NoticeId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedNoticeRevision).InclusiveBetween(1, long.MaxValue - 1);
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}

public sealed class AcknowledgeModerationPreFindingNoticeCommandHandler(IModerationPreFindingRepository repository)
    : ICommandHandler<AcknowledgeModerationPreFindingNoticeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AcknowledgeModerationPreFindingNoticeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new AcknowledgeModerationPreFindingNoticeCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.ExecutePreFindingAsync(new AcknowledgeModerationPreFindingNotice(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision,
            command.NoticeId, command.ExpectedNoticeRevision), cancellationToken);
        return ModerationPreFindingCommandResponse.FromOutcome(result, command.OperationId);
    }
}
