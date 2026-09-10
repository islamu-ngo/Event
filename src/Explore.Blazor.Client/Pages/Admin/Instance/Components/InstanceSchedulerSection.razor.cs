using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Scheduling;
using Explore.Blazor.Client.Contracts.Services.Scheduling;
using Explore.Blazor.Client.Helpers;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Admin.Instance.Components;

public partial class InstanceSchedulerSection : ComponentBase
{
    private HalResourceOfSchedulerAdminOverviewDto? _overview;
    private IReadOnlyList<HalResourceOfSchedulerAdminJobDto> _jobs = [];
    private IReadOnlyList<string> _plannedJobs = [];
    private bool _isLoading = true;
    private bool _isBusy;
    private string? _message;
    private Severity _messageSeverity = Severity.Info;

    [Inject]
    private ISchedulerAdminService SchedulerAdminService { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            _overview = await SchedulerAdminService.GetOverviewAsync();

            // The job list is fetched only when the overview proved the surface exists, so a disabled host makes
            // one request that returns nothing rather than two that both fail.
            if (_overview is null)
            {
                _jobs = [];
                _plannedJobs = [];
                return;
            }

            _plannedJobs = _overview.PlannedJobs is { } planned ? [.. planned] : [];
            _jobs = (await SchedulerAdminService.GetJobsAsync()).SchedulerJobs();
        }
        catch (ApiException)
        {
            _overview = null;
            _jobs = [];
            _plannedJobs = [];
            _message = "Scheduler data could not be loaded. Refresh to try again.";
            _messageSeverity = Severity.Error;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private Task RefreshAsync()
    {
        ClearMessage();
        return LoadAsync();
    }

    private void ClearMessage() => _message = null;

    private bool _isConfirmingPause;
    private string _pauseConfirmation = string.Empty;

    /// <summary>
    /// Confirmation is required by the server, so the UI collects it rather than sending a guess. Matching locally
    /// only enables the button; the server still validates, and remains the authority.
    /// </summary>
    private bool CanConfirmPause =>
        string.Equals(_pauseConfirmation.Trim(), _overview?.SchedulerName, StringComparison.Ordinal);

    private void BeginPauseScheduler()
    {
        _pauseConfirmation = string.Empty;
        _isConfirmingPause = true;
    }

    private void CancelPauseScheduler()
    {
        _isConfirmingPause = false;
        _pauseConfirmation = string.Empty;
    }

    private async Task ConfirmPauseSchedulerAsync()
    {
        var confirmation = _pauseConfirmation.Trim();
        await ExecuteAsync(token => SchedulerAdminService.PauseSchedulerAsync(confirmation, token));
        CancelPauseScheduler();
    }

    private Task ResumeSchedulerAsync() =>
        ExecuteAsync(token => SchedulerAdminService.ResumeSchedulerAsync(token));

    private Task PauseJobAsync(HalResourceOfSchedulerAdminJobDto job) =>
        ExecuteAsync(token => SchedulerAdminService.PauseJobAsync(job.Group ?? string.Empty, job.Name ?? string.Empty, token));

    private Task ResumeJobAsync(HalResourceOfSchedulerAdminJobDto job) =>
        ExecuteAsync(token => SchedulerAdminService.ResumeJobAsync(job.Group ?? string.Empty, job.Name ?? string.Empty, token));

    private Task TriggerJobAsync(HalResourceOfSchedulerAdminJobDto job) =>
        ExecuteAsync(token => SchedulerAdminService.TriggerJobAsync(job.Group ?? string.Empty, job.Name ?? string.Empty, token));

    private Task ResetJobErrorStateAsync(HalResourceOfSchedulerAdminJobDto job) =>
        ExecuteAsync(token => SchedulerAdminService.ResetJobErrorStateAsync(job.Group ?? string.Empty, job.Name ?? string.Empty, token));

    private Task InterruptJobAsync(HalResourceOfSchedulerAdminJobDto job) =>
        ExecuteAsync(token => SchedulerAdminService.InterruptJobAsync(job.Group ?? string.Empty, job.Name ?? string.Empty, token));

    /// <summary>
    /// Runs one control action and then reloads from the server. Local state is never patched optimistically:
    /// pausing a job changes which affordances the server will emit for it, and only a reload can reflect that.
    /// </summary>
    private async Task ExecuteAsync(Func<CancellationToken, Task<BaseCommandResponseOfstring>> action)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        try
        {
            var response = await action(CancellationToken.None);
            _message = response.Message;
            _messageSeverity = response.Success == true ? Severity.Success : Severity.Error;
            await LoadAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }

    private string StateLabel => _overview?.State switch
    {
        SchedulerAdminStates.Running => "Running",
        SchedulerAdminStates.Standby => "Standby",
        SchedulerAdminStates.Shutdown => "Shut down",
        SchedulerAdminStates.Disabled => "Disabled",
        _ => "Unknown"
    };

    private Color StateChipColor => _overview?.State switch
    {
        SchedulerAdminStates.Running => Color.Success,
        SchedulerAdminStates.Standby => Color.Warning,
        SchedulerAdminStates.Shutdown => Color.Error,
        SchedulerAdminStates.Disabled => Color.Default,
        _ => Color.Default
    };

    private string StateIcon => _overview?.State switch
    {
        SchedulerAdminStates.Running => Icons.Material.Filled.PlayCircle,
        SchedulerAdminStates.Standby => Icons.Material.Filled.PauseCircle,
        SchedulerAdminStates.Shutdown => Icons.Material.Filled.StopCircle,
        _ => Icons.Material.Filled.HelpOutline
    };

    private static Color JobStateColor(string? state) => state switch
    {
        SchedulerAdminStates.Active => Color.Success,
        SchedulerAdminStates.Paused => Color.Warning,
        SchedulerAdminStates.Error => Color.Error,
        SchedulerAdminStates.Blocked => Color.Info,
        SchedulerAdminStates.Complete => Color.Default,
        SchedulerAdminStates.OnDemand => Color.Info,
        _ => Color.Default
    };

    private static string JobStateLabel(string? state) => state switch
    {
        SchedulerAdminStates.Active => "active",
        SchedulerAdminStates.Paused => "paused",
        SchedulerAdminStates.Error => "error",
        SchedulerAdminStates.Blocked => "blocked",
        SchedulerAdminStates.Complete => "complete",
        SchedulerAdminStates.OnDemand => "on demand",
        _ => "unknown"
    };

    /// <summary>
    /// Joins the schedule descriptions of a job's triggers. A job with several triggers has several cadences, and
    /// showing only the first would misreport when the job actually runs.
    /// </summary>
    private static string ScheduleSummary(HalResourceOfSchedulerAdminJobDto job)
    {
        if (job.Triggers is not { Count: > 0 } triggers)
        {
            return job.Durable == true ? "on demand" : "—";
        }

        var summaries = triggers
            .Select(trigger => trigger.ScheduleSummary)
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .ToArray();

        return summaries.Length == 0 ? "—" : string.Join(" · ", summaries);
    }

    private static string FormatFireTime(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatFireTime(DateTime? value) =>
        value is null ? "—" : value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
