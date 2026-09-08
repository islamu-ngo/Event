using ApplicationUnitTests.Contracts.Admissions.Support;
using Explore.Application.Contracts.Admissions;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionRevocationOrchestrationRedTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("full-relevant")]
    [Arguments("one-relevant-partial")]
    [Arguments("add-on-only")]
    [Arguments("mixed-full-ticket-and-addon")]
    [Arguments("multiple-ticket-lines-partial")]
    [Arguments("zero-relevant")]
    [Arguments("zero-over-zero")]
    [Arguments("negative")]
    [Arguments("over-allocation")]
    [Arguments("cancellation")]
    public async Task RefundAndCancellationReturnExactRevokedAndPreservedTicketIdentities(string matrixCase)
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Free(UtcNow, AdmissionRevocationRow.Assignments());
        AdmissionIssuanceResult issued = await AdmissionIssuancePorts.TypedService(scenario)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(scenario), CancellationToken.None);
        await Assert.That(issued.IssuedTicketIds.Count).IsEqualTo(3);

        AdmissionRevocationRow row = AdmissionRevocationRow.For(matrixCase, scenario);
        AdmissionRevocationResult result = await AdmissionRevocationPorts.TypedService(scenario)
            .ReconcileAsync(AdmissionRevocationPorts.TypedRequest(scenario, row), CancellationToken.None);
        Guid[] revoked = result.RevokedTicketIds.ToArray();
        Guid[] preserved = result.PreservedTicketIds.ToArray();

        await Assert.That(revoked.Order()).IsEquivalentTo(row.ExpectedRevoked.Order());
        await Assert.That(preserved.Order()).IsEquivalentTo(row.ExpectedPreserved.Order());
        await Assert.That(revoked.Intersect(preserved)).IsEmpty();
        await Assert.That(revoked.Concat(preserved).Order()).IsEquivalentTo(
            scenario.TicketsByAssignment.Values.Select(ticket => ticket.Id).Order());

        if (row.Invalid)
        {
            await Assert.That(result.Outcome).IsEqualTo(AdmissionRevocationOutcome.InvalidAllocation);
            await Assert.That(scenario.RevocationWriteCalls).IsEqualTo(0);
        }
        else
        {
            await Assert.That(result.Outcome).IsEqualTo(AdmissionRevocationOutcome.Applied);
            await Assert.That(scenario.RevocationWriteCalls).IsEqualTo(1);
        }
    }
}
