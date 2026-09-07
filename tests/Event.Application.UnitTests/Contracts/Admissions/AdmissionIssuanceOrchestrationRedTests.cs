using ApplicationUnitTests.Contracts.Admissions.Support;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;
using Explore.Domain;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionIssuanceOrchestrationRedTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task FreeConfirmedIssuanceCreatesOneTicketPerAssignmentAndReplayKeepsEveryIdentity()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Free(UtcNow, Assignments());
        AdmissionIssuanceService service = AdmissionIssuancePorts.TypedService(scenario);
        AdmissionIssuanceRequest request = AdmissionIssuancePorts.TypedRequest(scenario);

        AdmissionIssuanceResult first = await service.IssueConfirmedAsync(request, CancellationToken.None);
        int intentsAfterFirst = scenario.PersistedDeliveryIntentCount;
        int dispatchesAfterFirst = scenario.IssuanceDeliveryCalls;
        int commitsAfterFirst = scenario.TransactionCommits;
        AdmissionIssuanceResult replay = await service.IssueConfirmedAsync(request, CancellationToken.None);
        Guid[] firstIds = first.IssuedTicketIds.ToArray();
        Guid[] replayIds = replay.ExistingTicketIds.ToArray();
        AdmissionOneTimeCredential firstCredential = first.OneTimeCredentials.First();
        string plaintext = firstCredential.PlaintextCredential;

        await Assert.That(first.ToString()).DoesNotContain(plaintext);
        await Assert.That(firstCredential.ToString()).DoesNotContain(plaintext);
        await Assert.That(firstIds.Length).IsEqualTo(2);
        await Assert.That(firstIds.Distinct().Count()).IsEqualTo(2);
        await Assert.That(replayIds.Order()).IsEquivalentTo(firstIds.Order());
        await Assert.That(scenario.TicketsByAssignment.Keys)
            .IsEquivalentTo(scenario.Assignments.Select(value => value.AssignmentId));
        await Assert.That(scenario.IssuanceWriteCalls).IsEqualTo(1);
        await Assert.That(scenario.DigestIssueCalls).IsEqualTo(2);
        await Assert.That(intentsAfterFirst).IsEqualTo(2);
        await Assert.That(dispatchesAfterFirst).IsEqualTo(intentsAfterFirst);
        await Assert.That(scenario.PersistedDeliveryIntentCount).IsEqualTo(intentsAfterFirst);
        await Assert.That(scenario.IssuanceDeliveryCalls).IsEqualTo(dispatchesAfterFirst);
        await Assert.That(scenario.TransactionCommits).IsEqualTo(commitsAfterFirst + 1);
        await Assert.That(scenario.IssuanceDispatchCommitCounts.All(value => value == 1)).IsTrue();
        await Assert.That(scenario.PendingDeliveryIntentIds).IsEmpty();
        await AssertCanonicalCredentialChildrenAsync(scenario.TicketsByAssignment.Values);
    }

    [Test]
    public async Task MalformedLineageAndPreCommitCancellationReturnTypedFailuresWithoutWriting()
    {
        AdmissionTestScenario malformedScenario = AdmissionTestScenario.Free(UtcNow, [Assignments()[0]]);
        AdmissionIssuanceRequest malformedRequest = new(
            Guid.Empty,
            malformedScenario.OrderId,
            malformedScenario.FinalizationEffectId,
            malformedScenario.Authority);
        AdmissionIssuanceResult malformed = await AdmissionIssuancePorts.TypedService(malformedScenario)
            .IssueConfirmedAsync(malformedRequest, CancellationToken.None);

        AdmissionTestScenario cancelledScenario = AdmissionTestScenario.Free(UtcNow, [Assignments()[0]]);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AdmissionIssuanceResult cancelledResult = await AdmissionIssuancePorts.TypedService(cancelledScenario)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(cancelledScenario), cancelled.Token);

        await Assert.That(malformed.Outcome).IsEqualTo(AdmissionIssuanceOutcome.InvalidRequest);
        await Assert.That(cancelledResult.Outcome).IsEqualTo(AdmissionIssuanceOutcome.CancelledBeforeCommit);
        await Assert.That(malformedScenario.IssuanceWriteCalls).IsEqualTo(0);
        await Assert.That(cancelledScenario.IssuanceWriteCalls).IsEqualTo(0);
    }

    [Test]
    public async Task PaidIssuanceRequiresReconciledFinalizationAuthorityAndReplaysOnce()
    {
        AdmissionAssignmentSeed assignment = Assignments()[0];
        AdmissionTestScenario paymentOnly = AdmissionTestScenario.Paid(UtcNow, [assignment], reconciled: false);
        AdmissionIssuanceResult denied = await AdmissionIssuancePorts.TypedService(paymentOnly)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(paymentOnly), CancellationToken.None);

        await Assert.That(denied.Outcome).IsEqualTo(AdmissionIssuanceOutcome.NotConfirmed);
        await Assert.That(paymentOnly.TicketsByAssignment).IsEmpty();
        await Assert.That(paymentOnly.IssuanceWriteCalls).IsEqualTo(0);

        AdmissionTestScenario reconciled = AdmissionTestScenario.Paid(UtcNow, [assignment], reconciled: true);
        AdmissionIssuanceResult accepted = await AdmissionIssuancePorts.TypedService(reconciled)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(reconciled), CancellationToken.None);
        AdmissionIssuanceResult replay = await AdmissionIssuancePorts.TypedService(reconciled)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(reconciled), CancellationToken.None);

        await Assert.That(accepted.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(replay.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(accepted.IssuedTicketIds.Count).IsEqualTo(1);
        await Assert.That(replay.ExistingTicketIds).IsEquivalentTo(accepted.IssuedTicketIds);
        await Assert.That(reconciled.TicketsByAssignment.Count).IsEqualTo(1);
        await Assert.That(reconciled.IssuanceWriteCalls).IsEqualTo(1);
    }

    [Test]
    public async Task TicketAndDeliveryIntentPersistenceIsAtomicAndDispatchIsPostCommit()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Free(UtcNow, Assignments());
        _ = await AdmissionIssuancePorts.TypedService(scenario)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(scenario), CancellationToken.None);

        await Assert.That(scenario.IssuanceWriteCalls).IsEqualTo(1);
        await Assert.That(scenario.AtomicTicketAndIntentWriteObserved).IsTrue();
        await Assert.That(scenario.TicketsByAssignment.Count).IsEqualTo(2);
        await Assert.That(scenario.PersistedDeliveryIntentCount).IsEqualTo(2);
        await Assert.That(scenario.IssuanceDeliveryCalls).IsEqualTo(scenario.PersistedDeliveryIntentCount);
        await Assert.That(scenario.TransactionCommits).IsEqualTo(1);
        await Assert.That(scenario.IssuanceDispatchCommitCounts).IsEquivalentTo([1, 1]);
        await Assert.That(scenario.DeliveryCalledInsideTransaction).IsFalse();
        await Assert.That(scenario.PendingDeliveryIntentIds).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostCommitAcknowledgementReconcilesCommittedRowsAndMaterialBeforeReporting(
        bool reportAsTimeout)
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Free(UtcNow, [Assignments()[0]]);
        scenario.LoseNextCommitAcknowledgement = true;
        scenario.LoseNextCommitAcknowledgementAsTimeout = reportAsTimeout;

        AdmissionIssuanceResult result = await AdmissionIssuancePorts.TypedService(scenario)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(scenario), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(result.DeliveryOutcome).IsEqualTo(AdmissionDeliveryOutcome.Delivered);
        await Assert.That(result.ExistingTicketIds.Count).IsEqualTo(1);
        await Assert.That(result.OneTimeCredentials.Count).IsEqualTo(1);
        await Assert.That(scenario.TransactionCommits).IsEqualTo(1);
        await Assert.That(scenario.DigestIssueCalls).IsEqualTo(1);
        await Assert.That(scenario.PersistedDeliveryIntentCount).IsEqualTo(1);
        await Assert.That(scenario.IssuanceDeliveryCalls).IsEqualTo(1);
    }

    [Test]
    public async Task DispatcherFailureLeavesCommittedIntentAvailableForRetry()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Free(UtcNow, [Assignments()[0]]);
        scenario.FailNextIssuanceDelivery = true;

        AdmissionIssuanceService service = AdmissionIssuancePorts.TypedService(scenario);
        AdmissionIssuanceRequest request = AdmissionIssuancePorts.TypedRequest(scenario);
        AdmissionIssuanceResult committed = await service.IssueConfirmedAsync(request, CancellationToken.None);
        AdmissionIssuanceResult replay = await service.IssueConfirmedAsync(request, CancellationToken.None);

        await Assert.That(committed.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(committed.DeliveryOutcome).IsEqualTo(AdmissionDeliveryOutcome.RecoverablePending);
        await Assert.That(committed.DeliveryFailure).IsEqualTo(AdmissionDeliveryFailure.RouteUnavailable);
        await Assert.That(replay.DeliveryOutcome).IsEqualTo(AdmissionDeliveryOutcome.Delivered);
        await Assert.That(replay.OneTimeCredentials.Count).IsEqualTo(1);
        await Assert.That(scenario.TransactionCommits).IsEqualTo(2);
        await Assert.That(scenario.TicketsByAssignment.Count).IsEqualTo(1);
        await Assert.That(scenario.PersistedDeliveryIntentCount).IsEqualTo(1);
        await Assert.That(scenario.IssuanceDeliveryCalls).IsEqualTo(2);
        await Assert.That(scenario.IssuanceDispatchCommitCounts).IsEquivalentTo([1, 2]);
        await Assert.That(scenario.DeliveryCalledInsideTransaction).IsFalse();
        await Assert.That(scenario.PendingDeliveryIntentIds).IsEmpty();
    }

    internal static AdmissionAssignmentSeed[] Assignments()
    {
        Guid lineId = Guid.CreateVersion7();
        return
        [
            new(Guid.CreateVersion7(), lineId, Guid.CreateVersion7(), 500, true),
            new(Guid.CreateVersion7(), lineId, Guid.CreateVersion7(), 500, true)
        ];
    }

    private static async Task AssertCanonicalCredentialChildrenAsync(IEnumerable<AdmissionTicket> tickets)
    {
        foreach (AdmissionTicket ticket in tickets)
        {
            await Assert.That(ticket.Credentials.Count).IsEqualTo(1);
            await Assert.That(ticket.Credentials.Single().GetType()).IsEqualTo(typeof(AdmissionTicketCredential));
        }
    }
}
