using ApplicationUnitTests.Contracts.Admissions.Support;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Exceptions;
using Explore.Application.Services.Registration;
using System.Security.Cryptography;
using System.Text;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionRecoveryOrchestrationRedTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task IssueConsumeAndReplayUsesTicketBoundRandomSingleUseCapability()
    {
        AdmissionTestScenario scenario = await PresentScenarioAsync();
        AdmissionRecoveryService service = AdmissionRecoveryPorts.TypedService(scenario);
        AdmissionRecoveryRequestResult issued = await RequestAndProcessAsync(service, scenario);
        string capability = scenario.TakeDeliveredCapability();
        bool separateFromAdmissionCredential = !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(capability)), scenario.AdmissionCredentialDigest);
        AdmissionRecoveryConsumeRequest consumeRequest = AdmissionRecoveryPorts.Consume(
            scenario, capability, "TicketRecovery", scenario.TenantId);
        AdmissionRecoveryConsumeResult first = await service.ConsumeAsync(consumeRequest, CancellationToken.None);
        AdmissionRecoveryConsumeResult replay = await service.ConsumeAsync(consumeRequest, CancellationToken.None);

        await AssertUniformReceiptAsync(issued);
        await Assert.That(first.Outcome).IsEqualTo(AdmissionRecoveryConsumeOutcome.Consumed);
        await Assert.That(replay.Outcome).IsEqualTo(AdmissionRecoveryConsumeOutcome.AlreadyConsumed);
        await Assert.That(separateFromAdmissionCredential).IsTrue();
        await Assert.That(scenario.RecoveryByDigest.Count).IsEqualTo(1);
        await Assert.That(scenario.RecoveryByDigest.Values.Single().AdmissionTicketId)
            .IsEqualTo(scenario.CurrentAdmissionTicketId);
        await Assert.That(scenario.ConsumedRecoveryCount).IsEqualTo(1);
        await Assert.That(scenario.StoredRecoveryPlaintextCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("expired")]
    [Arguments("wrong-purpose")]
    [Arguments("wrong-tenant")]
    public async Task ConsumeRejectsExpiredWrongPurposeAndWrongTenant(string rejection)
    {
        AdmissionTestScenario scenario = await PresentScenarioAsync();
        AdmissionRecoveryService service = AdmissionRecoveryPorts.TypedService(scenario);
        _ = await RequestAndProcessAsync(service, scenario);
        string capability = scenario.TakeDeliveredCapability();
        if (rejection == "expired") scenario.Clock.Advance(TimeSpan.FromHours(2));
        string purpose = rejection == "wrong-purpose" ? "TransferAcceptance" : "TicketRecovery";
        Guid tenantId = rejection == "wrong-tenant" ? Guid.CreateVersion7() : scenario.TenantId;
        AdmissionRecoveryConsumeResult result = await service.ConsumeAsync(
            AdmissionRecoveryPorts.Consume(scenario, capability, purpose, tenantId), CancellationToken.None);
        AdmissionRecoveryConsumeOutcome expected = rejection switch
        {
            "expired" => AdmissionRecoveryConsumeOutcome.Expired,
            "wrong-purpose" => AdmissionRecoveryConsumeOutcome.WrongPurpose,
            "wrong-tenant" => AdmissionRecoveryConsumeOutcome.WrongTenant,
            _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, null)
        };

        await Assert.That(result.Outcome).IsEqualTo(expected);
        await Assert.That(scenario.ConsumedRecoveryCount).IsEqualTo(0);
    }

    [Test]
    public async Task ResendLoadsCurrentRecordAndAtomicallyRotatesToTicketBoundReplacement()
    {
        AdmissionTestScenario scenario = await PresentScenarioAsync();
        AdmissionRecoveryService service = AdmissionRecoveryPorts.TypedService(scenario);
        _ = await RequestAndProcessAsync(service, scenario);
        string original = scenario.TakeDeliveredCapability();
        int commitsBeforeResend = scenario.TransactionCommits;
        _ = await service.ResendAsync(AdmissionRecoveryPorts.Resend(scenario), CancellationToken.None);
        int commitsAfterResend = scenario.TransactionCommits;
        string replacement = scenario.TakeDeliveredCapability();
        bool rotatedValue = !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(original)),
            SHA256.HashData(Encoding.UTF8.GetBytes(replacement)));
        AdmissionRecoveryConsumeResult oldResult = await service.ConsumeAsync(
            AdmissionRecoveryPorts.Consume(scenario, original, "TicketRecovery", scenario.TenantId),
            CancellationToken.None);
        AdmissionRecoveryConsumeResult replacementResult = await service.ConsumeAsync(
            AdmissionRecoveryPorts.Consume(scenario, replacement, "TicketRecovery", scenario.TenantId),
            CancellationToken.None);

        await Assert.That(rotatedValue).IsTrue();
        await Assert.That(oldResult.Outcome).IsEqualTo(AdmissionRecoveryConsumeOutcome.Rotated);
        await Assert.That(replacementResult.Outcome).IsEqualTo(AdmissionRecoveryConsumeOutcome.Consumed);
        await Assert.That(scenario.RecoveryCurrentReadCalls).IsEqualTo(1);
        await Assert.That(scenario.RecoveryRotationCalls).IsEqualTo(1);
        await Assert.That(commitsAfterResend).IsEqualTo(commitsBeforeResend + 1);
        await Assert.That(scenario.DeliveryCalledInsideTransaction).IsTrue();
        await Assert.That(scenario.RecoveryByDigest.Count).IsEqualTo(2);
        await Assert.That(scenario.RecoveryByDigest.Values.All(value =>
            value.AdmissionTicketId == scenario.CurrentAdmissionTicketId &&
            value.TenantId == scenario.TenantId && value.RecoveryRequestId == scenario.RecoveryRequestId &&
            value.Purpose == "TicketRecovery")).IsTrue();
        await Assert.That(scenario.ActiveRecoveryCount).IsEqualTo(0);
    }

    [Test]
    public async Task PresentAndAbsentIdentityReturnSameReceiptButAbsentStoresAndDeliversNothing()
    {
        AdmissionTestScenario present = await PresentScenarioAsync();
        AdmissionTestScenario absent = AdmissionTestScenario.Recovery(UtcNow, identityPresent: false);
        AdmissionRecoveryService presentService = AdmissionRecoveryPorts.TypedService(present);
        AdmissionRecoveryService absentService = AdmissionRecoveryPorts.TypedService(absent);
        AdmissionRecoveryRequestResult presentResult = await presentService.RequestAsync(
            AdmissionRecoveryPorts.Request(present, "TicketRecovery"), CancellationToken.None);
        AdmissionRecoveryRequestResult absentResult = await absentService.RequestAsync(
            AdmissionRecoveryPorts.Request(absent, "TicketRecovery"), CancellationToken.None);

        await AssertUniformReceiptAsync(presentResult);
        await AssertUniformReceiptAsync(absentResult);
        await Assert.That(presentResult).IsEqualTo(absentResult);
        await Assert.That(present.RecoveryRequestStageCalls).IsEqualTo(1);
        await Assert.That(absent.RecoveryRequestStageCalls).IsEqualTo(1);
        await Assert.That(present.RecoveryDeliveryCalls).IsEqualTo(0);
        await Assert.That(absent.RecoveryDeliveryCalls).IsEqualTo(0);
        await Assert.That(present.RecoveryStoreCalls).IsEqualTo(0);
        await Assert.That(absent.RecoveryStoreCalls).IsEqualTo(0);
        await presentService.ProcessRequestAsync(
            AdmissionRecoveryPorts.Request(present, "TicketRecovery"), CancellationToken.None);
        await absentService.ProcessRequestAsync(
            AdmissionRecoveryPorts.Request(absent, "TicketRecovery"), CancellationToken.None);
        await Assert.That(present.RecoveryDeliveryCalls).IsEqualTo(1);
        await Assert.That(present.RecoveryByDigest.Values.Single().AdmissionTicketId)
            .IsEqualTo(present.CurrentAdmissionTicketId);
        await Assert.That(absent.RecoveryDeliveryCalls).IsEqualTo(0);
        await Assert.That(absent.RecoveryStoreCalls).IsEqualTo(0);
        await Assert.That(absent.RecoveryByDigest).IsEmpty();
        await Assert.That(absent.DigestIssueCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DurableStagingFailureNeverReturnsAcceptedReceipt()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Recovery(UtcNow, identityPresent: false);
        scenario.FailRecoveryRequestStaging = true;
        AdmissionRecoveryService service = AdmissionRecoveryPorts.TypedService(scenario);

        AdmissionRecoveryUnavailableException exception = await Assert.ThrowsAsync<AdmissionRecoveryUnavailableException>(async () =>
            await service.RequestAsync(
                AdmissionRecoveryPorts.Request(scenario, "TicketRecovery"), CancellationToken.None));

        await Assert.That(exception).IsNotNull();
        await Assert.That(scenario.RecoveryRequestStageCalls).IsEqualTo(1);
    }

    [Test]
    public async Task RepeatedPublicRequestRotatesExistingTicketRecoveryAuthority()
    {
        AdmissionTestScenario scenario = await PresentScenarioAsync();
        AdmissionRecoveryService service = AdmissionRecoveryPorts.TypedService(scenario);

        AdmissionRecoveryRequestResult first = await RequestAndProcessAsync(service, scenario);
        AdmissionRecoveryRequestResult second = await RequestAndProcessAsync(service, scenario);

        await AssertUniformReceiptAsync(first);
        await AssertUniformReceiptAsync(second);
        await Assert.That(scenario.RecoveryStoreCalls).IsEqualTo(1);
        await Assert.That(scenario.RecoveryRotationCalls).IsEqualTo(1);
        await Assert.That(scenario.RecoveryDeliveryCalls).IsEqualTo(2);
        await Assert.That(scenario.RecoveryByDigest.Count).IsEqualTo(2);
        await Assert.That(scenario.RecoveryByDigest.Values.Count(value => value.Rotated))
            .IsEqualTo(1);
        await Assert.That(scenario.ActiveRecoveryCount).IsEqualTo(1);
        await Assert.That(scenario.RecoveryByDigest.Values.Select(value => value.RecoveryRequestId)
            .Distinct().Single()).IsEqualTo(scenario.RecoveryRequestId);
    }

    [Test]
    public async Task NewPublicRequestAfterConsumptionIssuesNextGeneration()
    {
        AdmissionTestScenario scenario = await PresentScenarioAsync();
        AdmissionRecoveryService service = AdmissionRecoveryPorts.TypedService(scenario);
        await RequestAndProcessAsync(service, scenario);
        string firstCapability = scenario.TakeDeliveredCapability();
        AdmissionRecoveryConsumeResult consumed = await service.ConsumeAsync(
            AdmissionRecoveryPorts.Consume(
                scenario,
                firstCapability,
                "TicketRecovery",
                scenario.TenantId),
            CancellationToken.None);

        AdmissionRecoveryRequestResult requestedAgain = await RequestAndProcessAsync(service, scenario);

        await Assert.That(consumed.Outcome).IsEqualTo(AdmissionRecoveryConsumeOutcome.Consumed);
        await AssertUniformReceiptAsync(requestedAgain);
        await Assert.That(scenario.RecoveryStoreCalls).IsEqualTo(2);
        await Assert.That(scenario.RecoveryRotationCalls).IsEqualTo(0);
        await Assert.That(scenario.ConsumedRecoveryCount).IsEqualTo(1);
        await Assert.That(scenario.ActiveRecoveryCount).IsEqualTo(1);
        await Assert.That(scenario.RecoveryByDigest.Values.Select(value => value.RecoveryRequestId)
            .Distinct().Single()).IsEqualTo(scenario.RecoveryRequestId);
    }

    private static async Task<AdmissionRecoveryRequestResult> RequestAndProcessAsync(
        AdmissionRecoveryService service,
        AdmissionTestScenario scenario)
    {
        AdmissionRecoveryRequestResult result = await service.RequestAsync(
            AdmissionRecoveryPorts.Request(scenario, "TicketRecovery"), CancellationToken.None);
        await service.ProcessRequestAsync(
            AdmissionRecoveryPorts.Request(scenario, "TicketRecovery"), CancellationToken.None);
        return result;
    }

    private static async Task<AdmissionTestScenario> PresentScenarioAsync()
    {
        AdmissionTestScenario scenario = AdmissionTestScenario.Recovery(UtcNow, identityPresent: true);
        AdmissionIssuanceResult issued = await AdmissionIssuancePorts.TypedService(scenario)
            .IssueConfirmedAsync(AdmissionIssuancePorts.TypedRequest(scenario), CancellationToken.None);
        await Assert.That(issued.IssuedTicketIds.Single())
            .IsEqualTo(scenario.CurrentAdmissionTicketId);
        return scenario;
    }

    private static async Task AssertUniformReceiptAsync(AdmissionRecoveryRequestResult result)
    {
        string[] forbidden = ["Found", "Exists", "AdmissionTicketId", "Capability", "Credential"];
        string[] propertyNames = result.GetType().GetProperties().Select(property => property.Name).ToArray();
        await Assert.That(forbidden.Intersect(propertyNames, StringComparer.OrdinalIgnoreCase)).IsEmpty();
        await Assert.That(result.Outcome).IsEqualTo(AdmissionRecoveryRequestOutcome.Accepted);
    }
}
