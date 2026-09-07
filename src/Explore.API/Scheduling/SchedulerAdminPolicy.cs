using Explore.API.Configuration;
using Explore.Application.Contracts.Scheduling;
using Microsoft.Extensions.Options;

namespace Explore.API.Scheduling;

/// <summary>
/// Projects <see cref="QuartzSchedulerSettings"/> onto the Application-facing administration policy. Options are
/// read through <see cref="IOptionsMonitor{TOptions}"/> so a reloaded configuration takes effect without a restart,
/// and both the advertised affordances and the accepted actions move together when it does.
/// </summary>
public sealed class SchedulerAdminPolicy(IOptionsMonitor<QuartzSchedulerSettings> options) : ISchedulerAdminPolicy
{
    public bool IsEnabled
    {
        get
        {
            var settings = options.CurrentValue;
            return settings.Enabled && settings.AdminApiEnabled;
        }
    }

    /// <summary>
    /// A disabled surface is also read-only. Treating "off" as "not mutable" means a misconfiguration can never
    /// produce a host that refuses reads yet still accepts scheduler mutations.
    /// </summary>
    public bool IsReadOnly => !IsEnabled || options.CurrentValue.AdminApiReadOnly;
}
