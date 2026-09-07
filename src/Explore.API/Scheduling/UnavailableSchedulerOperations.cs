using Explore.Application.Contracts.Scheduling;

namespace Explore.API.Scheduling;

/// <summary>
/// Stands in for a scheduler that was never started. Registering this instead of leaving the contract unresolved
/// means a host with scheduling turned off answers the administration endpoint with an explicit disabled state,
/// rather than throwing a dependency-resolution error that reads like a deployment fault.
/// </summary>
public sealed class UnavailableSchedulerOperations : ISchedulerOperations
{
    public Task<SchedulerRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(SchedulerRuntimeSnapshot.Unavailable);

    public Task<SchedulerOperationResult> PauseAllAsync(CancellationToken cancellationToken) => Unavailable();

    public Task<SchedulerOperationResult> ResumeAllAsync(CancellationToken cancellationToken) => Unavailable();

    public Task<SchedulerOperationResult> PauseJobAsync(string group, string name, CancellationToken cancellationToken) =>
        Unavailable();

    public Task<SchedulerOperationResult> ResumeJobAsync(string group, string name, CancellationToken cancellationToken) =>
        Unavailable();

    public Task<SchedulerOperationResult> TriggerJobAsync(string group, string name, CancellationToken cancellationToken) =>
        Unavailable();

    public Task<SchedulerOperationResult> ResetJobErrorStateAsync(string group, string name, CancellationToken cancellationToken) =>
        Unavailable();

    public Task<SchedulerOperationResult> InterruptJobAsync(string group, string name, CancellationToken cancellationToken) =>
        Unavailable();

    private static Task<SchedulerOperationResult> Unavailable() =>
        Task.FromResult(SchedulerOperationResult.SchedulerUnavailable);
}
