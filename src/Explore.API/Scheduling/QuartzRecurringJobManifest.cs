using Quartz;

namespace Explore.API.Scheduling;

public sealed record QuartzRecurringJobManifest(
    IReadOnlySet<JobKey> Owned,
    IReadOnlySet<JobKey> Desired);
