using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.DTOs.Scheduling;
using Explore.Application.Features.Scheduling.Requests.Queries;
using Explore.Application.Hateoas;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class SchedulerAdminHateoasTests
{
    [Test]
    public async Task OverviewLinks_ExposeInstanceSettingPermissionMetadata()
    {
        var links = OverviewLinks(Overview(SchedulerAdminStates.Running, available: true, readOnly: false));

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetSchedulerAdminOverview);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetSchedulerAdminOverviewQuery.SettingKey);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var jobs = links.Single(link => link.Rel == LinkRelations.SchedulerJobs);
        await Assert.That(jobs.RouteName).IsEqualTo(RouteNames.GetSchedulerAdminJobs);
        await Assert.That(jobs.Method).IsEqualTo("GET");
    }

    [Test]
    public async Task OverviewControlLinks_RequireUpdateAuthority()
    {
        var links = OverviewLinks(Overview(SchedulerAdminStates.Running, available: true, readOnly: false));

        var pause = links.Single(link => link.Rel == LinkRelations.SchedulerPause);
        await Assert.That(pause.RouteName).IsEqualTo(RouteNames.PauseScheduler);
        await Assert.That(pause.Method).IsEqualTo("POST");
        await Assert.That(pause.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
    }

    /// <summary>
    /// A read-only host must advertise no control affordance. A client that gates its buttons on link presence
    /// then cannot offer an action the command handler would refuse.
    /// </summary>
    [Test]
    public async Task OverviewLinks_WhenHostIsReadOnly_OmitControlAffordances()
    {
        var links = OverviewLinks(Overview(SchedulerAdminStates.Running, available: true, readOnly: true));

        await Assert.That(links.Any(link => link.Rel == LinkRelations.SchedulerPause)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.SchedulerResume)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
    }

    [Test]
    public async Task OverviewLinks_WhenSchedulerUnavailable_OmitControlAffordances()
    {
        var links = OverviewLinks(Overview(SchedulerAdminStates.Disabled, available: false, readOnly: false));

        await Assert.That(links.Any(link => link.Rel == LinkRelations.SchedulerPause)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.SchedulerResume)).IsFalse();
    }

    [Test]
    public async Task OverviewLinks_AdvertiseOnlyTheMeaningfulLifecycleTransition()
    {
        var running = OverviewLinks(Overview(SchedulerAdminStates.Running, available: true, readOnly: false));
        await Assert.That(running.Any(link => link.Rel == LinkRelations.SchedulerPause)).IsTrue();
        await Assert.That(running.Any(link => link.Rel == LinkRelations.SchedulerResume)).IsFalse();

        var standby = OverviewLinks(Overview(SchedulerAdminStates.Standby, available: true, readOnly: false));
        await Assert.That(standby.Any(link => link.Rel == LinkRelations.SchedulerResume)).IsTrue();
        await Assert.That(standby.Any(link => link.Rel == LinkRelations.SchedulerPause)).IsFalse();
    }

    [Test]
    public async Task JobLinks_CarryRouteIdentityForTheirOwnJob()
    {
        var links = JobLinks(Job("email-dispatch-drain", SchedulerAdminStates.Active));

        var trigger = links.Single(link => link.Rel == LinkRelations.SchedulerTrigger);
        await Assert.That(trigger.RouteName).IsEqualTo(RouteNames.TriggerSchedulerJob);
        await Assert.That(trigger.Method).IsEqualTo("POST");
        await Assert.That(trigger.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);

        var routeValues = trigger.RouteValues!.GetType();
        await Assert.That(routeValues.GetProperty("group")!.GetValue(trigger.RouteValues)).IsEqualTo("DEFAULT");
        await Assert.That(routeValues.GetProperty("name")!.GetValue(trigger.RouteValues))
            .IsEqualTo("email-dispatch-drain");
    }

    [Test]
    public async Task JobLinks_OfferPauseForActiveJobAndResumeForPausedJob()
    {
        var active = JobLinks(Job("active-job", SchedulerAdminStates.Active));
        await Assert.That(active.Any(link => link.Rel == LinkRelations.SchedulerPause)).IsTrue();
        await Assert.That(active.Any(link => link.Rel == LinkRelations.SchedulerResume)).IsFalse();

        var paused = JobLinks(Job("paused-job", SchedulerAdminStates.Paused));
        await Assert.That(paused.Any(link => link.Rel == LinkRelations.SchedulerResume)).IsTrue();
        await Assert.That(paused.Any(link => link.Rel == LinkRelations.SchedulerPause)).IsFalse();
    }

    /// <summary>
    /// An on-demand job carries no trigger of its own, so pausing it would be a no-op dressed as an action.
    /// Running it now remains meaningful.
    /// </summary>
    [Test]
    public async Task JobLinks_ForOnDemandJob_OfferTriggerButNeitherPauseNorResume()
    {
        var links = JobLinks(Job("event-reminder-dispatch", SchedulerAdminStates.OnDemand));

        await Assert.That(links.Any(link => link.Rel == LinkRelations.SchedulerTrigger)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.SchedulerPause)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.SchedulerResume)).IsFalse();
    }

    /// <summary>
    /// Regression guard: a job row does not carry the host's read-only state in its own DTO, so the per-row policy
    /// must consult the host policy independently. Without this the table would keep offering controls that the
    /// command handlers refuse, even while the overview correctly hid them.
    /// </summary>
    [Test]
    public async Task JobLinks_WhenHostIsReadOnly_OmitEveryControlAffordance()
    {
        var links = JobLinks(Job("email-dispatch-drain", SchedulerAdminStates.Active), readOnly: true);

        await Assert.That(links).IsEmpty();
    }

    /// <summary>
    /// The table renders an error chip for a job whose triggers are in the scheduler's error state. That state is
    /// only clearable by an operator, so the row must also carry the action that clears it — otherwise the surface
    /// reports a problem it cannot act on.
    /// </summary>
    [Test]
    public async Task JobLinks_ForJobInErrorState_OfferRecovery()
    {
        var links = JobLinks(Job("failing-job", SchedulerAdminStates.Error));

        var reset = links.Single(link => link.Rel == LinkRelations.SchedulerResetError);
        await Assert.That(reset.RouteName).IsEqualTo(RouteNames.ResetSchedulerJobErrorState);
        await Assert.That(reset.Method).IsEqualTo("POST");
        await Assert.That(reset.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);

        var healthy = JobLinks(Job("healthy-job", SchedulerAdminStates.Active));
        await Assert.That(healthy.Any(link => link.Rel == LinkRelations.SchedulerResetError)).IsFalse();
    }

    /// <summary>
    /// Likewise for the running chip: an executing job must offer interruption, and an idle one must not.
    /// </summary>
    [Test]
    public async Task JobLinks_ForExecutingJob_OfferInterrupt()
    {
        var executing = JobLinks(Job("running-job", SchedulerAdminStates.Active, executing: true));

        var interrupt = executing.Single(link => link.Rel == LinkRelations.SchedulerInterrupt);
        await Assert.That(interrupt.RouteName).IsEqualTo(RouteNames.InterruptSchedulerJob);
        await Assert.That(interrupt.Method).IsEqualTo("POST");

        var idle = JobLinks(Job("idle-job", SchedulerAdminStates.Active));
        await Assert.That(idle.Any(link => link.Rel == LinkRelations.SchedulerInterrupt)).IsFalse();
    }

    [Test]
    public async Task JobLinks_WhenHostIsReadOnly_OmitRecoveryAffordancesToo()
    {
        var links = JobLinks(Job("failing-job", SchedulerAdminStates.Error, executing: true), readOnly: true);

        await Assert.That(links).IsEmpty();
    }

    private static LinkDefinition[] OverviewLinks(SchedulerAdminOverviewDto dto) =>
        [.. new SchedulerAdminOverviewLinkPolicy().GetLinks(dto, user: null)];

    private static LinkDefinition[] JobLinks(SchedulerAdminJobDto dto, bool readOnly = false) =>
        [.. new SchedulerAdminJobCollectionLinkPolicy(SchedulerPolicy(readOnly)).GetItemLinks(dto, user: null)];

    private static ISchedulerAdminPolicy SchedulerPolicy(bool readOnly)
    {
        var policy = Substitute.For<ISchedulerAdminPolicy>();
        policy.IsEnabled.Returns(true);
        policy.IsReadOnly.Returns(readOnly);
        return policy;
    }

    private static SchedulerAdminOverviewDto Overview(string state, bool available, bool readOnly) => new()
    {
        State = state,
        Available = available,
        ReadOnly = readOnly
    };

    private static SchedulerAdminJobDto Job(string name, string state, bool executing = false) => new()
    {
        Name = name,
        Group = "DEFAULT",
        State = state,
        Executing = executing
    };
}
