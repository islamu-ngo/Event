using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.Scheduling.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Scheduling.Handlers.Commands;

public sealed class PauseSchedulerCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        IRequestHandler<PauseSchedulerCommand, BaseCommandResponse<string>>
{
    public async Task<BaseCommandResponse<string>> Handle(
        PauseSchedulerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Confirmation is checked against live scheduler identity rather than a constant, so an operator has to
        // have actually looked at the instance they are about to silence.
        var snapshot = await SchedulerOperations.GetSnapshotAsync(cancellationToken);
        if (!string.Equals(request.ConfirmationText?.Trim(), snapshot.SchedulerName, StringComparison.Ordinal))
        {
            return ConfirmationMismatch(
                SchedulerAdminCommandBase.SettingKey,
                $"Type the scheduler name '{snapshot.SchedulerName}' to confirm pausing all background work.");
        }

        return await ExecuteAsync(
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
        IRequestHandler<ResumeSchedulerCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> Handle(
        ResumeSchedulerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteAsync(
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
        IRequestHandler<PauseSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> Handle(
        PauseSchedulerJobCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteAsync(
            JobOperationId(request.Group, request.Name),
            token => SchedulerOperations.PauseJobAsync(request.Group, request.Name, token),
            "The job is paused. Its triggers stop firing until it is resumed.",
            cancellationToken);
    }
}

public sealed class ResumeSchedulerJobCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        IRequestHandler<ResumeSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> Handle(
        ResumeSchedulerJobCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteAsync(
            JobOperationId(request.Group, request.Name),
            token => SchedulerOperations.ResumeJobAsync(request.Group, request.Name, token),
            "The job resumed and its triggers fire again.",
            cancellationToken);
    }
}

public sealed class ResetSchedulerJobErrorStateCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        IRequestHandler<ResetSchedulerJobErrorStateCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> Handle(
        ResetSchedulerJobErrorStateCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteAsync(
            JobOperationId(request.Group, request.Name),
            token => SchedulerOperations.ResetJobErrorStateAsync(request.Group, request.Name, token),
            "The job's triggers were cleared from the error state and will fire on their normal schedule.",
            cancellationToken);
    }
}

public sealed class InterruptSchedulerJobCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        IRequestHandler<InterruptSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> Handle(
        InterruptSchedulerJobCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The wording promises a request, not a stop: interruption signals the running job's cancellation token,
        // and a job that does not observe it will keep going.
        return ExecuteAsync(
            JobOperationId(request.Group, request.Name),
            token => SchedulerOperations.InterruptJobAsync(request.Group, request.Name, token),
            "Cancellation was signalled to the running job. It stops at its next cancellation checkpoint.",
            cancellationToken);
    }
}

public sealed class TriggerSchedulerJobCommandHandler(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
    : SchedulerAdminCommandHandlerBase(schedulerOperations, policy),
        IRequestHandler<TriggerSchedulerJobCommand, BaseCommandResponse<string>>
{
    public Task<BaseCommandResponse<string>> Handle(
        TriggerSchedulerJobCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteAsync(
            JobOperationId(request.Group, request.Name),
            token => SchedulerOperations.TriggerJobAsync(request.Group, request.Name, token),
            "The job was queued for an immediate run. Its existing schedule is unchanged.",
            cancellationToken);
    }
}
