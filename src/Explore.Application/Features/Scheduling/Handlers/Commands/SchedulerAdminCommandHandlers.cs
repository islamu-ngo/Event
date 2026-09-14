using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.Scheduling.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Scheduling.Handlers.Commands;

public sealed class PauseSchedulerCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        ICommandHandler<PauseSchedulerCommand, BaseCommandResponse<string>>
{
    public async Task<BaseCommandResponse<string>> ExecuteAsync(
        PauseSchedulerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Confirmation is checked against live scheduler identity rather than a constant, so an operator has to
        // have actually looked at the instance they are about to silence.
        var snapshot = await SchedulerOperations.GetSnapshotAsync(cancellationToken);
        if (!string.Equals(command.ConfirmationText?.Trim(), snapshot.SchedulerName, StringComparison.Ordinal))
        {
            return ConfirmationMismatch(
                SchedulerAdminCommandBase.SettingKey,
                $"Type the scheduler name '{snapshot.SchedulerName}' to confirm pausing all background work.");
        }

        return await ExecuteOperationAsync(
            SchedulerAdminCommandBase.SettingKey,
            SchedulerOperations.PauseAllAsync,
            "The scheduler moved to standby. Running jobs finish, and no further triggers fire.",
            cancellationToken);
    }
}

public sealed class ResumeSchedulerCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        ICommandHandler<ResumeSchedulerCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> ExecuteAsync(
        ResumeSchedulerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteOperationAsync(
            SchedulerAdminCommandBase.SettingKey,
            SchedulerOperations.ResumeAllAsync,
            "The scheduler resumed and triggers fire again.",
            cancellationToken);
    }
}

public sealed class PauseSchedulerJobCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        ICommandHandler<PauseSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> ExecuteAsync(
        PauseSchedulerJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteOperationAsync(
            JobOperationId(command.Group, command.Name),
            token => SchedulerOperations.PauseJobAsync(command.Group, command.Name, token),
            "The job is paused. Its triggers stop firing until it is resumed.",
            cancellationToken);
    }
}

public sealed class ResumeSchedulerJobCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        ICommandHandler<ResumeSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> ExecuteAsync(
        ResumeSchedulerJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteOperationAsync(
            JobOperationId(command.Group, command.Name),
            token => SchedulerOperations.ResumeJobAsync(command.Group, command.Name, token),
            "The job resumed and its triggers fire again.",
            cancellationToken);
    }
}

public sealed class ResetSchedulerJobErrorStateCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        ICommandHandler<ResetSchedulerJobErrorStateCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> ExecuteAsync(
        ResetSchedulerJobErrorStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteOperationAsync(
            JobOperationId(command.Group, command.Name),
            token => SchedulerOperations.ResetJobErrorStateAsync(command.Group, command.Name, token),
            "The job's triggers were cleared from the error state and will fire on their normal schedule.",
            cancellationToken);
    }
}

public sealed class InterruptSchedulerJobCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        ICommandHandler<InterruptSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> ExecuteAsync(
        InterruptSchedulerJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The wording promises a request, not a stop: interruption signals the running job's cancellation token,
        // and a job that does not observe it will keep going.
        return ExecuteOperationAsync(
            JobOperationId(command.Group, command.Name),
            token => SchedulerOperations.InterruptJobAsync(command.Group, command.Name, token),
            "Cancellation was signalled to the running job. It stops at its next cancellation checkpoint.",
            cancellationToken);
    }
}

public sealed class TriggerSchedulerJobCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        ICommandHandler<TriggerSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> ExecuteAsync(
        TriggerSchedulerJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteOperationAsync(
            JobOperationId(command.Group, command.Name),
            token => SchedulerOperations.TriggerJobAsync(command.Group, command.Name, token),
            "The job was queued for an immediate run. Its existing schedule is unchanged.",
            cancellationToken);
    }
}
