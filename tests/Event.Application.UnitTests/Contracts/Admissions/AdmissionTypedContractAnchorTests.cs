using ApplicationUnitTests.Contracts.Admissions.Support;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionTypedContractAnchorTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task IssuanceUsesDirectServiceRequestAndResultTypes()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Free(UtcNow, []);
        AdmissionIssuanceService service = AdmissionIssuancePorts.TypedService(scenario);
        AdmissionIssuanceRequest request = AdmissionIssuancePorts.TypedRequest(scenario);

        AdmissionIssuanceResult result = await service.IssueConfirmedAsync(request, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.NoAssignments);
    }

    [Test]
    public async Task CheckInUsesDirectServiceRequestAndResultTypes()
    {
        var scenario = new CheckInScenario(UtcNow);
        AdmissionCheckInService service = CheckInPorts.TypedService(scenario);
        AdmissionCheckInRequest request = CheckInPorts.TypedRequest(scenario);

        AdmissionCheckInResult result = await service.ProcessAsync(request, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionCheckInOutcome.CheckedIn);
        await Assert.That(scenario.AppendCount).IsEqualTo(1);
    }

    [Test]
    public async Task RevocationUsesDirectServiceRequestAndResultTypes()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Free(
            UtcNow,
            AdmissionRevocationRow.Assignments());
        AdmissionIssuanceResult issued = await AdmissionIssuancePorts.TypedService(scenario)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(scenario), CancellationToken.None);
        AdmissionRevocationRow row = AdmissionRevocationRow.For("full-relevant", scenario);
        AdmissionRevocationService service = AdmissionRevocationPorts.TypedService(scenario);

        AdmissionRevocationResult result = await service.ReconcileAsync(
            AdmissionRevocationPorts.TypedRequest(scenario, row),
            CancellationToken.None);

        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(result.Outcome).IsEqualTo(AdmissionRevocationOutcome.Applied);
        await Assert.That(result.RevokedTicketIds).IsEquivalentTo(row.ExpectedRevoked);
        await Assert.That(result.PreservedTicketIds).IsEquivalentTo(row.ExpectedPreserved);
    }

    [Test]
    public async Task RecoveryUsesDirectServiceRequestAndResultTypes()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Recovery(UtcNow, identityPresent: false);
        AdmissionRecoveryService service = AdmissionRecoveryPorts.TypedService(scenario);
        AdmissionRecoveryRequest request = AdmissionRecoveryPorts.TypedRequest(scenario);

        AdmissionRecoveryRequestResult result = await service.RequestAsync(request, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionRecoveryRequestOutcome.Accepted);
        await Assert.That(scenario.RecoveryRequestStageCalls).IsEqualTo(1);
    }

    [Test]
    public async Task PublicAdmissionSignaturesRemainProviderNeutralThroughDirectTypes()
    {
        Type[] contracts =
        [
            typeof(AdmissionIssuanceService),
            typeof(AdmissionCheckInService),
            typeof(AdmissionRevocationService),
            typeof(AdmissionRecoveryService),
            typeof(IAdmissionIssuanceService),
            typeof(IAdmissionRevocationService),
            typeof(IAdmissionRecoveryRepository)
        ];
        Type[] leaked = ProviderNeutralTypeGraph.Closure(
                contracts.SelectMany(ProviderNeutralTypeGraph.PublicSignatureTypes))
            .Where(ProviderNeutralTypeGraph.IsProviderSpecific)
            .Distinct()
            .ToArray();

        await Assert.That(leaked).IsEmpty();
    }
}
