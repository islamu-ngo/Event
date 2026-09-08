using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Scheduling;

namespace Explore.Blazor.Client.Services.Scheduling;

/// <summary>
/// Thin transport adapter over the generated client. It deliberately performs no local mapping: HAL resources are
/// handed to components exactly as the server produced them, so affordance gating reads server truth rather than
/// a client-side reconstruction of it.
/// </summary>
public sealed class SchedulerAdminApiAdapter(ISchedulerAdminClient apiClient) : ISchedulerAdminService
{
    public Task<HalResourceOfSchedulerAdminOverviewDto?> GetOverviewAsync(
        CancellationToken cancellationToken = default) =>
        ReadOrNullAsync(
            () => apiClient.GetSchedulerAdminOverviewAsync(cancellationToken: cancellationToken));

    public Task<HalCollectionResourceOfSchedulerAdminJobDto?> GetJobsAsync(
        CancellationToken cancellationToken = default) =>
        ReadOrNullAsync(
            () => apiClient.GetSchedulerAdminJobsAsync(cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfstring> PauseSchedulerAsync(
        string confirmationText,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => apiClient.PauseSchedulerAsync(
            new SchedulerPauseRequestDto { ConfirmationText = confirmationText },
            cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfstring> ResumeSchedulerAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => apiClient.ResumeSchedulerAsync(cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfstring> PauseJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => apiClient.PauseSchedulerJobAsync(group, name, cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfstring> ResumeJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => apiClient.ResumeSchedulerJobAsync(group, name, cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfstring> TriggerJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => apiClient.TriggerSchedulerJobAsync(group, name, cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfstring> ResetJobErrorStateAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => apiClient.ResetSchedulerJobErrorStateAsync(group, name, cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfstring> InterruptJobAsync(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => apiClient.InterruptSchedulerJobAsync(group, name, cancellationToken: cancellationToken));

    /// <summary>
    /// Treats an absent or forbidden surface as "no resource" rather than an error. A host that never enabled the
    /// administration API is a supported configuration, not a fault, so the caller can simply omit the section.
    /// </summary>
    private static async Task<TResource?> ReadOrNullAsync<TResource>(Func<Task<TResource>> read)
        where TResource : class
    {
        try
        {
            return await read();
        }
        catch (ApiException exception) when (exception.StatusCode is 401 or 403 or 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a refused action into the same structured response shape a refusal-by-body would produce, so the
    /// component renders one failure path instead of branching between exceptions and response codes.
    /// </summary>
    private static async Task<BaseCommandResponseOfstring> ExecuteAsync(
        Func<Task<BaseCommandResponseOfstring>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException exception)
        {
            return new BaseCommandResponseOfstring
            {
                Success = false,
                Message = SchedulerAdminFailureMessages.Describe(exception.StatusCode),
                Errors = [SchedulerAdminFailureMessages.Describe(exception.StatusCode)]
            };
        }
    }
}
