using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationOperationalBudgetCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationOperationalBudgetCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationOperationalBudgetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationOperationalBudgetCommandValidator<RecordModerationOperationalBudgetCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationOperationalBudgetCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationOperationalBudgetCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationOperationalBudgetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationOperationalBudgetCommandValidator<AdoptModerationOperationalBudgetCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationOperationalBudgetCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationOperationalBudgetCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationOperationalBudgetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationOperationalBudgetCommandValidator<WithdrawModerationOperationalBudgetCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationOperationalBudgetCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationOperationalBudgetCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(id =>
            id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11);
        RuleFor(command => command).Custom((command, validation) =>
        {
            if (command is not (RecordModerationOperationalBudgetCommand or AdoptModerationOperationalBudgetCommand
                    or WithdrawModerationOperationalBudgetCommand)
                || !ModerationOperationalBudgetQualification.IsValid(command.ToMutation()))
                validation.AddFailure("Operational budgets require exact tenant generations, finite terms and independent native operators.");
        });
    }
}
