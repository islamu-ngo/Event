using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Application;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class ProposeModerationDecisionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<ProposeModerationDecisionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        ProposeModerationDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<ProposeModerationDecisionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class RecordModerationDecisionResponseCommandHandler(ModerationOperationService operations)
    : ICommandHandler<RecordModerationDecisionResponseCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        RecordModerationDecisionResponseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<RecordModerationDecisionResponseCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class AcceptModerationFindingCommandHandler(ModerationOperationService operations)
    : ICommandHandler<AcceptModerationFindingCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        AcceptModerationFindingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<AcceptModerationFindingCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class ApproveModerationDecisionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<ApproveModerationDecisionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        ApproveModerationDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<ApproveModerationDecisionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class ActivateModerationDecisionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<ActivateModerationDecisionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        ActivateModerationDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<ActivateModerationDecisionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class RejectModerationDecisionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<RejectModerationDecisionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        RejectModerationDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<RejectModerationDecisionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class WithdrawModerationDecisionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<WithdrawModerationDecisionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        WithdrawModerationDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<WithdrawModerationDecisionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class RevokeModerationSanctionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<RevokeModerationSanctionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        RevokeModerationSanctionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<RevokeModerationSanctionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class ReconcileModerationSanctionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<ReconcileModerationSanctionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        ReconcileModerationSanctionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<ReconcileModerationSanctionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class SupersedeModerationSanctionCommandHandler(ModerationOperationService operations)
    : ICommandHandler<SupersedeModerationSanctionCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        SupersedeModerationSanctionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleCommandValidator<SupersedeModerationSanctionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await operations.ExecuteAsync(command.ToOperation(), cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.OperationId, result);
    }
}

/// <summary>Typed and shared entrypoints apply the same intent rules before the store acquires actual authority.</summary>
public sealed class ModerationLifecycleCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationLifecycleCommand
{
    public ModerationLifecycleCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            ApplyModerationLifecycleCommand intent;
            try
            {
                intent = new(command.ToOperation());
            }
            catch (ArgumentException)
            {
                context.AddFailure("Lifecycle intent requires qualified coordinates, stable identities and matching references.");
                return;
            }
            var validation = new ModerationLifecycleValidator().Validate(intent);
            foreach (var error in validation.Errors)
                context.AddFailure(error);
        });
    }
}
