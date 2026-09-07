using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.Scheduling.Handlers.Commands;
using Explore.Application.Features.Scheduling.Requests.Commands;
using Explore.Application.Responses;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Scheduling;

public sealed class SchedulerAdminCommandHandlerTests
{
    /// <summary>
    /// A read-only host must refuse before any scheduler call, not after: a partially applied action followed by
    /// a refusal response would leave the scheduler and the operator's understanding of it out of step.
    /// </summary>
    [Test]
    public async Task PauseScheduler_WhenHostIsReadOnly_RefusesWithoutCallingScheduler()
    {
        var operations = OperationsWithScheduler();
        var handler = new PauseSchedulerCommandHandler(operations, Policy(readOnly: true));

        var response = await handler.Handle(ConfirmedPause(), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.SchedulerReadOnly);
        await operations.DidNotReceive().PauseAllAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Pausing stops every background subsystem at once, so it is the one action guarded by typed confirmation.
    /// An unconfirmed attempt must not reach the scheduler.
    /// </summary>
    [Test]
    public async Task PauseScheduler_WithoutConfirmation_IsRefusedWithoutCallingScheduler()
    {
        var operations = OperationsWithScheduler();
        var handler = new PauseSchedulerCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(new PauseSchedulerCommand(), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.SchedulerConfirmationRequired);
        await operations.DidNotReceive().PauseAllAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PauseScheduler_WithWrongConfirmation_IsRefused()
    {
        var operations = OperationsWithScheduler();
        var handler = new PauseSchedulerCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new PauseSchedulerCommand { ConfirmationText = "some-other-scheduler" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.SchedulerConfirmationRequired);
        await operations.DidNotReceive().PauseAllAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Resume is a recovery action and deliberately carries no confirmation guard.</summary>
    [Test]
    public async Task ResumeScheduler_RequiresNoConfirmation()
    {
        var operations = OperationsWithScheduler();
        operations.ResumeAllAsync(Arg.Any<CancellationToken>()).Returns(SchedulerOperationResult.Succeeded);

        var response = await new ResumeSchedulerCommandHandler(operations, Policy(readOnly: false))
            .Handle(new ResumeSchedulerCommand(), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
    }

    [Test]
    public async Task TriggerJob_WhenHostIsReadOnly_RefusesWithoutCallingScheduler()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        var handler = new TriggerSchedulerJobCommandHandler(operations, Policy(readOnly: true));

        var response = await handler.Handle(
            new TriggerSchedulerJobCommand { Group = "DEFAULT", Name = "email-dispatch-drain" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.SchedulerReadOnly);
        await operations.DidNotReceive().TriggerJobAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PauseScheduler_WhenMutableAndConfirmed_DelegatesToSchedulerAndSucceeds()
    {
        var operations = OperationsWithScheduler();
        operations.PauseAllAsync(Arg.Any<CancellationToken>()).Returns(SchedulerOperationResult.Succeeded);
        var handler = new PauseSchedulerCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(ConfirmedPause(), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.FailureCode).IsNull();
        await operations.Received(1).PauseAllAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResumeScheduler_WhenMutable_DelegatesToScheduler()
    {
        var operations = OperationsWithScheduler();
        operations.ResumeAllAsync(Arg.Any<CancellationToken>()).Returns(SchedulerOperationResult.Succeeded);
        var handler = new ResumeSchedulerCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(new ResumeSchedulerCommand(), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await operations.Received(1).ResumeAllAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PauseJob_WhenJobMissing_MapsToNotFoundFailureCode()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.PauseJobAsync("DEFAULT", "ghost-job", Arg.Any<CancellationToken>())
            .Returns(SchedulerOperationResult.JobNotFound);
        var handler = new PauseSchedulerJobCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new PauseSchedulerJobCommand { Group = "DEFAULT", Name = "ghost-job" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.NotFound);
        await Assert.That(response.Id).IsEqualTo("DEFAULT.ghost-job");
    }

    [Test]
    public async Task ResumeJob_WhenSchedulerDisabled_MapsToSchedulerUnavailableFailureCode()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.ResumeJobAsync("DEFAULT", "idempotency-cleanup", Arg.Any<CancellationToken>())
            .Returns(SchedulerOperationResult.SchedulerUnavailable);
        var handler = new ResumeSchedulerJobCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new ResumeSchedulerJobCommand { Group = "DEFAULT", Name = "idempotency-cleanup" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.SchedulerUnavailable);
    }

    [Test]
    public async Task TriggerJob_WhenMutable_PassesRouteIdentityThrough()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.TriggerJobAsync("DEFAULT", "email-dispatch-drain", Arg.Any<CancellationToken>())
            .Returns(SchedulerOperationResult.Succeeded);
        var handler = new TriggerSchedulerJobCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new TriggerSchedulerJobCommand { Group = "DEFAULT", Name = "email-dispatch-drain" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo("DEFAULT.email-dispatch-drain");
        await operations.Received(1).TriggerJobAsync("DEFAULT", "email-dispatch-drain", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResetJobErrorState_WhenTriggersRecovered_Succeeds()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.ResetJobErrorStateAsync("DEFAULT", "webhook-retention-cleanup", Arg.Any<CancellationToken>())
            .Returns(SchedulerOperationResult.Succeeded);
        var handler = new ResetSchedulerJobErrorStateCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new ResetSchedulerJobErrorStateCommand { Group = "DEFAULT", Name = "webhook-retention-cleanup" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo("DEFAULT.webhook-retention-cleanup");
    }

    /// <summary>
    /// Pressing recover on a job that is no longer in error must not report success — nothing changed, and telling
    /// the operator otherwise would hide that the state they acted on had already moved.
    /// </summary>
    [Test]
    public async Task ResetJobErrorState_WhenNoTriggerIsInError_ReportsNotApplicable()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.ResetJobErrorStateAsync("DEFAULT", "healthy-job", Arg.Any<CancellationToken>())
            .Returns(SchedulerOperationResult.NotApplicable);
        var handler = new ResetSchedulerJobErrorStateCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new ResetSchedulerJobErrorStateCommand { Group = "DEFAULT", Name = "healthy-job" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.SchedulerActionNotApplicable);
    }

    [Test]
    public async Task InterruptJob_WhenExecutionWasSignalled_Succeeds()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.InterruptJobAsync("DEFAULT", "storage-reconciliation", Arg.Any<CancellationToken>())
            .Returns(SchedulerOperationResult.Succeeded);
        var handler = new InterruptSchedulerJobCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new InterruptSchedulerJobCommand { Group = "DEFAULT", Name = "storage-reconciliation" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
    }

    [Test]
    public async Task InterruptJob_WhenNothingWasExecuting_ReportsNotApplicable()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.InterruptJobAsync("DEFAULT", "idle-job", Arg.Any<CancellationToken>())
            .Returns(SchedulerOperationResult.NotApplicable);
        var handler = new InterruptSchedulerJobCommandHandler(operations, Policy(readOnly: false));

        var response = await handler.Handle(
            new InterruptSchedulerJobCommand { Group = "DEFAULT", Name = "idle-job" },
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.SchedulerActionNotApplicable);
    }

    [Test]
    public async Task RecoveryActions_WhenHostIsReadOnly_AreRefusedWithoutCallingScheduler()
    {
        var operations = Substitute.For<ISchedulerOperations>();

        var reset = await new ResetSchedulerJobErrorStateCommandHandler(operations, Policy(readOnly: true))
            .Handle(new ResetSchedulerJobErrorStateCommand { Group = "DEFAULT", Name = "j" }, CancellationToken.None);
        var interrupt = await new InterruptSchedulerJobCommandHandler(operations, Policy(readOnly: true))
            .Handle(new InterruptSchedulerJobCommand { Group = "DEFAULT", Name = "j" }, CancellationToken.None);

        await Assert.That(reset.FailureCode).IsEqualTo(FailureCodes.SchedulerReadOnly);
        await Assert.That(interrupt.FailureCode).IsEqualTo(FailureCodes.SchedulerReadOnly);
        await operations.DidNotReceive().ResetJobErrorStateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await operations.DidNotReceive().InterruptJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private const string SchedulerName = "islamu-event-scheduler";

    private static PauseSchedulerCommand ConfirmedPause() => new() { ConfirmationText = SchedulerName };

    /// <summary>
    /// The pause guard compares against live scheduler identity, so the substitute must report a snapshot.
    /// </summary>
    private static ISchedulerOperations OperationsWithScheduler()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(new SchedulerRuntimeSnapshot(
            Available: true,
            SchedulerName: SchedulerName,
            InstanceId: "node-1",
            Started: true,
            InStandbyMode: false,
            Shutdown: false,
            Clustered: false,
            SupportsPersistence: true,
            ExecutingJobCount: 0,
            Jobs: []));
        return operations;
    }

    private static ISchedulerAdminPolicy Policy(bool readOnly)
    {
        var policy = Substitute.For<ISchedulerAdminPolicy>();
        policy.IsEnabled.Returns(true);
        policy.IsReadOnly.Returns(readOnly);
        return policy;
    }
}
