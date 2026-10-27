using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationCaseNarrativePolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationCaseNarrativePolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationCaseNarrativePolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationCaseNarrativePolicyCommandValidator<RecordModerationCaseNarrativePolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationCaseNarrativePolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationCaseNarrativePolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationCaseNarrativePolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationCaseNarrativePolicyCommandValidator<AdoptModerationCaseNarrativePolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationCaseNarrativePolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationCaseNarrativePolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationCaseNarrativePolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationCaseNarrativePolicyCommandValidator<WithdrawModerationCaseNarrativePolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationCaseNarrativePolicyCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationCaseNarrativePolicyCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(id =>
            id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11);
        RuleFor(command => command).Custom((command, validation) =>
        {
            if (command is not (RecordModerationCaseNarrativePolicyCommand or AdoptModerationCaseNarrativePolicyCommand
                    or WithdrawModerationCaseNarrativePolicyCommand)
                || !ModerationCaseNarrativeQualification.IsValid(command.ToMutation()))
                validation.AddFailure("Narrative custody requires exact tenant coordinates, finite terms and independent ownership.");
        });
    }
}
