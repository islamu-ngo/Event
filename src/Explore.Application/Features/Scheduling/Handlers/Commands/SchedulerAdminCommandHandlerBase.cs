using Explore.Application.Contracts.Scheduling;
using Explore.Application.Responses;

namespace Explore.Application.Features.Scheduling.Handlers.Commands;

/// <summary>
/// Template for scheduler control actions. Each concrete handler supplies only the operation to run and the
/// identifier to echo back; refusal mapping lives here so read-only enforcement and failure codes stay identical
/// across all five actions rather than being re-implemented per handler.
/// </summary>
public abstract class SchedulerAdminCommandHandlerBase(
    ISchedulerOperations schedulerOperations,
    ISchedulerAdminPolicy policy)
{
    protected ISchedulerOperations SchedulerOperations { get; } = schedulerOperations;

    protected async Task<BaseCommandResponse<string>> ExecuteAsync(
        string operationId,
        Func<CancellationToken, Task<SchedulerOperationResult>> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // The gate is checked before the scheduler is touched. A read-only host must not partially apply an action
        // and then report refusal, so refusal happens ahead of any state change rather than after it.
        if (policy.IsReadOnly)
        {
            return Failure(
                operationId,
                FailureCodes.SchedulerReadOnly,
                "The scheduler administration surface is configured as read-only on this host.");
        }

        var result = await operation(cancellationToken);

        return result.Outcome switch
        {
            SchedulerOperationOutcome.Succeeded => BaseCommandResponse.Success(operationId, successMessage),
            SchedulerOperationOutcome.SchedulerUnavailable => Failure(
                operationId,
                FailureCodes.SchedulerUnavailable,
                "Background scheduling is disabled on this host."),
            SchedulerOperationOutcome.JobNotFound => BaseCommandResponse.NotFound(
                "The requested scheduled job does not exist.",
                operationId),
            SchedulerOperationOutcome.ReadOnly => Failure(
                operationId,
                FailureCodes.SchedulerReadOnly,
                "The scheduler administration surface is configured as read-only on this host."),

            // Not a fault: the job's state moved on between the operator reading the table and pressing the
            // button. Saying so is more useful than reporting a success that changed nothing.
            SchedulerOperationOutcome.NotApplicable => Failure(
                operationId,
                FailureCodes.SchedulerActionNotApplicable,
                "The action no longer applies to this job. Refresh to see its current state."),
            _ => Failure(
                operationId,
                FailureCodes.SchedulerUnavailable,
                "The scheduler could not complete the requested action.")
        };
    }

    protected static string JobOperationId(string group, string name) => $"{group}.{name}";

    /// <summary>
    /// Refusal for a guarded action whose typed confirmation did not match. Distinguished from a permission or
    /// state refusal so the caller can re-prompt rather than report the action as impossible.
    /// </summary>
    protected static BaseCommandResponse<string> ConfirmationMismatch(string operationId, string message) =>
        Failure(operationId, FailureCodes.SchedulerConfirmationRequired, message);

    protected static BaseCommandResponse<string> Failure(string operationId, string failureCode, string message) =>
        BaseCommandResponse.Failure<string>(failureCode, message, [message], operationId);
}
