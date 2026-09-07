using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Scheduling;

/// <summary>
/// Read and control access to the instance scheduler. The read methods return null when the host does not expose
/// the administration API, which is how the settings sidebar decides whether the scheduler section exists at all:
/// availability is discovered from the server rather than inferred from the caller's roles.
/// </summary>
public interface ISchedulerAdminService
{
    Task<HalResourceOfSchedulerAdminOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<HalCollectionResourceOfSchedulerAdminJobDto?> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses all background work. <paramref name="confirmationText"/> must equal the running scheduler's name;
    /// the server refuses otherwise, so the UI must collect it rather than assume it.
    /// </summary>
    Task<BaseCommandResponseOfstring> PauseSchedulerAsync(
        string confirmationText,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfstring> ResumeSchedulerAsync(CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfstring> PauseJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfstring> ResumeJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfstring> TriggerJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Clears a job's triggers out of the scheduler error state so they fire on schedule again.</summary>
    Task<BaseCommandResponseOfstring> ResetJobErrorStateAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Requests cooperative cancellation of a job's currently executing instances.</summary>
    Task<BaseCommandResponseOfstring> InterruptJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default);
}
