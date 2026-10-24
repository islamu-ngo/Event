using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Notices;

public sealed record AcknowledgeModerationNoticeCommand(Guid Id, Guid OperationId, string SnapshotHash)
    : ICommand<BaseCommandResponse<Guid>>;

public sealed class AcknowledgeModerationNoticeCommandValidator : AbstractValidator<AcknowledgeModerationNoticeCommand>
{
    public AcknowledgeModerationNoticeCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.OperationId)
            .Must(id => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11);
        RuleFor(command => command.SnapshotHash)
            .Must(hash => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigitLower));
    }
}

public sealed class AcknowledgeModerationNoticeCommandHandler(
    IModerationNoticeNotificationRepository repository, IAdminContext identity,
    IMachinePrincipalAccessor machine, ITenantContext tenant)
    : ICommandHandler<AcknowledgeModerationNoticeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AcknowledgeModerationNoticeCommand command, CancellationToken cancellationToken = default)
    {
        if (machine.IsMachineCaller || await identity.ResolveUserIdAsync(cancellationToken) is not { } subjectId)
            return BaseCommandResponse.Authentication<Guid>();
        var validation = await new AcknowledgeModerationNoticeCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.AcknowledgeAsync(
            new(tenant.TenantId, subjectId, command.Id, command.OperationId, command.SnapshotHash), cancellationToken);
        return result.Outcome switch
        {
            ModerationNoticeAcknowledgementOutcome.Acknowledged => BaseCommandResponse.Success(result.ReceiptId!.Value),
            ModerationNoticeAcknowledgementOutcome.Unauthenticated => BaseCommandResponse.Authentication<Guid>(),
            ModerationNoticeAcknowledgementOutcome.NotFound => BaseCommandResponse.NotFound<Guid>(),
            ModerationNoticeAcknowledgementOutcome.Conflict => BaseCommandResponse.Conflict(command.Id),
            _ => BaseCommandResponse.Failure<Guid>("ModerationNoticeUnavailable")
        };
    }
}
