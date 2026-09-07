namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.DTOs.Scheduling;
using Explore.Application.Features.Scheduling.Requests.Queries;
using Explore.Application.Hateoas;

/// <summary>
/// Affordances for the scheduler overview. Control links are withheld whenever the scheduler is unavailable or
/// the host is read-only, so a client that gates its buttons on link presence can never offer an action that the
/// command handler would then refuse.
/// </summary>
public sealed class SchedulerAdminOverviewLinkPolicy : ILinkPolicy<SchedulerAdminOverviewDto>
{
    public IEnumerable<LinkDefinition> GetLinks(SchedulerAdminOverviewDto dto, ClaimsPrincipal? user)
    {
        ArgumentNullException.ThrowIfNull(dto);
        _ = user;

        yield return SchedulerAdminLinks.View(
            LinkRelations.Self,
            RouteNames.GetSchedulerAdminOverview,
            "Scheduler administration");

        yield return SchedulerAdminLinks.View(
            LinkRelations.SchedulerJobs,
            RouteNames.GetSchedulerAdminJobs,
            "Scheduled jobs");

        if (!SchedulerAdminLinks.AllowsControl(dto))
        {
            yield break;
        }

        // Pause and resume are advertised according to the scheduler's current lifecycle so the overview offers
        // the one transition that is actually meaningful rather than both at once.
        if (!string.Equals(dto.State, SchedulerAdminStates.Standby, StringComparison.Ordinal))
        {
            yield return SchedulerAdminLinks.Control(
                LinkRelations.SchedulerPause,
                RouteNames.PauseScheduler,
                "Pause the scheduler");
        }

        if (!string.Equals(dto.State, SchedulerAdminStates.Running, StringComparison.Ordinal))
        {
            yield return SchedulerAdminLinks.Control(
                LinkRelations.SchedulerResume,
                RouteNames.ResumeScheduler,
                "Resume the scheduler");
        }
    }
}

public sealed class SchedulerAdminOverviewCollectionLinkPolicy : ICollectionLinkPolicy<SchedulerAdminOverviewDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(SchedulerAdminOverviewDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class SchedulerAdminJobLinkPolicy(ISchedulerAdminPolicy schedulerPolicy)
    : ILinkPolicy<SchedulerAdminJobDto>
{
    public IEnumerable<LinkDefinition> GetLinks(SchedulerAdminJobDto dto, ClaimsPrincipal? user) =>
        SchedulerAdminJobCollectionLinkPolicy.BuildJobLinks(dto, schedulerPolicy);
}

/// <summary>
/// Per-job affordances. Each row carries its own concrete action URLs, which is why jobs are their own collection
/// resource rather than an inline array: an inline array would have to share the parent's single link map and
/// could not express "this job may be resumed but that one may not".
/// <para>
/// The host policy is consulted here as well as on the overview. A job row does not carry the host's read-only
/// state in its own DTO, so without this the rows would keep advertising controls on a read-only host even while
/// the overview correctly withheld them — and the command handlers would then refuse what the table offered.
/// </para>
/// </summary>
public sealed class SchedulerAdminJobCollectionLinkPolicy(ISchedulerAdminPolicy schedulerPolicy)
    : ICollectionLinkPolicy<SchedulerAdminJobDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(SchedulerAdminJobDto dto, ClaimsPrincipal? user)
    {
        _ = user;
        return BuildJobLinks(dto, schedulerPolicy);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];

    internal static IEnumerable<LinkDefinition> BuildJobLinks(
        SchedulerAdminJobDto dto,
        ISchedulerAdminPolicy schedulerPolicy)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(schedulerPolicy);

        if (!schedulerPolicy.IsEnabled || schedulerPolicy.IsReadOnly)
        {
            yield break;
        }

        var routeValues = new { group = dto.Group, name = dto.Name };

        // Triggering an on-demand job is legitimate, but pausing one that carries no trigger of its own is not:
        // there is nothing to pause, so that affordance is withheld rather than offered and then no-opped.
        var hasTriggers = !string.Equals(dto.State, SchedulerAdminStates.OnDemand, StringComparison.Ordinal);
        var isPaused = string.Equals(dto.State, SchedulerAdminStates.Paused, StringComparison.Ordinal);

        yield return SchedulerAdminLinks.Control(
            LinkRelations.SchedulerTrigger,
            RouteNames.TriggerSchedulerJob,
            "Run this job now",
            routeValues);

        if (hasTriggers && !isPaused)
        {
            yield return SchedulerAdminLinks.Control(
                LinkRelations.SchedulerPause,
                RouteNames.PauseSchedulerJob,
                "Pause this job",
                routeValues);
        }

        if (hasTriggers && isPaused)
        {
            yield return SchedulerAdminLinks.Control(
                LinkRelations.SchedulerResume,
                RouteNames.ResumeSchedulerJob,
                "Resume this job",
                routeValues);
        }

        // Recovery affordances are emitted only for the states that make them meaningful, which is what keeps the
        // table honest: every condition it reports as a problem also carries the action that clears it.
        if (string.Equals(dto.State, SchedulerAdminStates.Error, StringComparison.Ordinal))
        {
            yield return SchedulerAdminLinks.Control(
                LinkRelations.SchedulerResetError,
                RouteNames.ResetSchedulerJobErrorState,
                "Clear this job's error state",
                routeValues);
        }

        if (dto.Executing)
        {
            yield return SchedulerAdminLinks.Control(
                LinkRelations.SchedulerInterrupt,
                RouteNames.InterruptSchedulerJob,
                "Request cancellation of this run",
                routeValues);
        }
    }
}

/// <summary>
/// Shared construction for scheduler affordances. Both policies route through it so read, control, permission,
/// and resource-attribute metadata stay identical between the overview and the job rows.
/// </summary>
internal static class SchedulerAdminLinks
{
    public static bool AllowsControl(SchedulerAdminOverviewDto dto) => dto.Available && !dto.ReadOnly;

    public static LinkDefinition View(string rel, string routeName, string title) =>
        Build(rel, routeName, "GET", title, AuthorizationActions.InstanceSettings.View, routeValues: null);

    public static LinkDefinition Control(string rel, string routeName, string title, object? routeValues = null) =>
        Build(rel, routeName, "POST", title, AuthorizationActions.InstanceSettings.Update, routeValues);

    private static LinkDefinition Build(
        string rel,
        string routeName,
        string method,
        string title,
        string action,
        object? routeValues) =>
        new LinkDefinition(rel, routeName, routeValues, method, title, RequiresAuth: true)
            .RequirePermission(
                action,
                ResourceKinds.InstanceSetting,
                GetSchedulerAdminOverviewQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}
