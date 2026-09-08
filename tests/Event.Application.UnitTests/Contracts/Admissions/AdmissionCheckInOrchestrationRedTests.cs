using ApplicationUnitTests.Contracts.Admissions.Support;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionCheckInOrchestrationRedTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("Staff")]
    [Arguments("ScannerCapability")]
    public async Task CheckInUsesOneTenantDigestLookupAndRequiresStaffOrScopedScannerAuthority(string authorityKind)
    {
        CheckInScenario scenario = new(UtcNow);
        Guid? scannerCapabilityId = null;
        Guid? staffActorId = scenario.StaffActorId;
        if (authorityKind == "ScannerCapability")
        {
            AdmissionScannerCapabilityIssuedResult issued = await IssueScannerCapabilityAsync(scenario);
            scannerCapabilityId = issued.ScannerCapabilityId;
            staffActorId = null;
        }

        AdmissionCheckInResult result = await CheckInAsync(
            scenario,
            scenario.Credential,
            "CheckIn",
            staffActorId,
            scannerCapabilityId);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionCheckInOutcome.CheckedIn);
        await Assert.That(scenario.CredentialDigestCalls).IsEqualTo(1);
        await Assert.That(scenario.TenantDigestLookupCalls).IsEqualTo(1);
        await Assert.That(scenario.ObservedLookupTenantId).IsEqualTo(scenario.TenantId);
        await Assert.That(scenario.ObservedLookupDigests.Length).IsEqualTo(2);
        await Assert.That(scenario.ObservedLookupDigests[0]).IsEqualTo(scenario.Digest(scenario.Credential));
        await Assert.That(scenario.ObservedLookupDigests[1])
            .IsEqualTo(scenario.Digest($"{scenario.Credential}:retained"));
        await Assert.That(scenario.ObservedLookupEventId).IsEqualTo(scenario.EventId);
        await Assert.That(scenario.ObservedLookupTargetId).IsEqualTo(scenario.TargetId);
        await Assert.That(scenario.AuthorityChecks).IsEqualTo(1);
        await Assert.That(scenario.LastAuthorityKind).IsEqualTo(authorityKind);
        await Assert.That(scenario.AppendCount).IsEqualTo(1);
        await Assert.That(scenario.TelemetryCalls).Contains("RecordOperation");
    }

    [Test]
    [Arguments("WrongTenant")]
    [Arguments("WrongEvent")]
    [Arguments("WrongTarget")]
    [Arguments("RevokedCredential")]
    [Arguments("ExpiredCredential")]
    public async Task WrongLineageAndInactiveCredentialsReturnTheSameGenericOutcome(string rejection)
    {
        CheckInScenario scenario = new(UtcNow);
        Guid tenantId = rejection == "WrongTenant" ? Guid.CreateVersion7() : scenario.TenantId;
        Guid eventId = rejection == "WrongEvent" ? Guid.CreateVersion7() : scenario.EventId;
        Guid targetId = rejection == "WrongTarget" ? Guid.CreateVersion7() : scenario.TargetId;
        if (rejection == "RevokedCredential") scenario.CredentialState = "Revoked";
        if (rejection == "ExpiredCredential") scenario.CredentialState = "Expired";

        AdmissionCheckInResult result = await CheckInPorts.TypedService(scenario).ProcessAsync(
            CheckInPorts.Request(
                scenario,
                scenario.Credential,
                "CheckIn",
                tenantId,
                eventId,
                targetId,
                scenario.StaffActorId,
                null),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionCheckInOutcome.Rejected);
        await Assert.That(typeof(AdmissionCheckInResult).GetProperties().Select(property => property.Name))
            .DoesNotContain("AdmissionTicketId");
        await Assert.That(scenario.AppendCount).IsEqualTo(0);
    }

    [Test]
    public async Task ScannerCapabilityIssueAndRevokeRequireExactEventTargetLineage()
    {
        CheckInScenario scenario = new(UtcNow);
        AdmissionScannerCapabilityService service = ScannerCapabilityPorts.TypedService(scenario);
        AdmissionScannerCapabilityIssuedResult mismatchedIssue = await service.IssueAsync(
            ScannerCapabilityPorts.IssueRequest(
                scenario,
                eventId: Guid.CreateVersion7()),
            CancellationToken.None);
        await Assert.That(mismatchedIssue.Outcome).IsEqualTo(AdmissionScannerCapabilityIssueOutcome.Rejected);
        await Assert.That(scenario.ScannerMaterialIssueCalls).IsEqualTo(0);
        await Assert.That(scenario.ScannerCapabilities).IsEmpty();

        scenario.TargetStopped = true;
        AdmissionScannerCapabilityIssuedResult stoppedIssue = await IssueScannerCapabilityAsync(scenario);
        await Assert.That(stoppedIssue.Outcome).IsEqualTo(AdmissionScannerCapabilityIssueOutcome.Rejected);
        await Assert.That(scenario.ScannerMaterialIssueCalls).IsEqualTo(0);
        scenario.TargetStopped = false;

        scenario.ScannerCapabilityStoreRejected = true;
        AdmissionScannerCapabilityIssuedResult fenceRejectedIssue = await IssueScannerCapabilityAsync(scenario);
        await Assert.That(fenceRejectedIssue.Outcome).IsEqualTo(AdmissionScannerCapabilityIssueOutcome.Rejected);
        await Assert.That(fenceRejectedIssue.ScannerCapabilityId).IsEqualTo(Guid.Empty);
        await Assert.That(fenceRejectedIssue.PlaintextCapability).IsNull();
        await Assert.That(fenceRejectedIssue.Descriptor).IsNull();
        await Assert.That(scenario.ScannerMaterialIssueCalls).IsEqualTo(1);
        await Assert.That(scenario.ScannerCapabilities).IsEmpty();
        scenario.ScannerCapabilityStoreRejected = false;

        AdmissionScannerCapabilityIssuedResult issued = await IssueScannerCapabilityAsync(scenario);
        Guid capabilityId = issued.ScannerCapabilityId;
        AdmissionScannerCapabilityRevocationResult mismatchedRevoke = await service.RevokeAsync(
            ScannerCapabilityPorts.RevokeRequest(
                scenario,
                capabilityId,
                eventId: Guid.CreateVersion7()),
            CancellationToken.None);
        await Assert.That(mismatchedRevoke.Outcome).IsEqualTo(AdmissionScannerCapabilityRevocationOutcome.Rejected);
        await Assert.That(scenario.ScannerCapabilities[capabilityId].RevokedAt).IsNull();
    }

    [Test]
    public async Task BatchRejectsMoreThanOneHundredAndReturnsIndependentOrderedPartialResults()
    {
        CheckInScenario overLimit = new(UtcNow);
        AdmissionCheckInBatchResult tooLarge = await CheckInPorts.TypedService(overLimit).ProcessBatchAsync(
            CheckInPorts.BatchRequest(
                overLimit,
                Enumerable.Range(0, 101).Select(_ => RuntimeCapability.New()).ToArray()),
            CancellationToken.None);

        await Assert.That(tooLarge.Outcome).IsEqualTo(AdmissionCheckInBatchOutcome.BatchLimitExceeded);
        await Assert.That(tooLarge.Items).IsEmpty();
        await Assert.That(overLimit.TenantDigestLookupCalls).IsEqualTo(0);
        await Assert.That(overLimit.AppendCount).IsEqualTo(0);
        await Assert.That(overLimit.TelemetryCalls).Contains("RecordSaturation");

        CheckInScenario partial = new(UtcNow);
        string revoked = RuntimeCapability.New();
        string validSecond = RuntimeCapability.New();
        partial.CredentialStates[partial.Digest(revoked)] = "Revoked";
        partial.CredentialStates[partial.Digest(validSecond)] = "Active";
        string[] credentials = [partial.Credential, revoked, validSecond];
        AdmissionCheckInBatchResult batch = await CheckInPorts.TypedService(partial).ProcessBatchAsync(
            CheckInPorts.BatchRequest(partial, credentials),
            CancellationToken.None);
        IReadOnlyList<AdmissionCheckInBatchItemResult> items = batch.Items;

        await Assert.That(batch.Outcome).IsEqualTo(AdmissionCheckInBatchOutcome.Completed);
        await Assert.That(items.Count).IsEqualTo(3);
        await Assert.That(string.Join(',', items.Select(item => item.Outcome)))
            .IsEqualTo("CheckedIn,Rejected,CheckedIn");
        await Assert.That(string.Join(',', items.Select(item => item.Index)))
            .IsEqualTo("0,1,2");
        await Assert.That(partial.AppendCount).IsEqualTo(2);
        await Assert.That(partial.TenantDigestLookupCalls).IsEqualTo(3);
        await Assert.That(partial.UnitOfWork.TransactionCount).IsEqualTo(3);
        await Assert.That(partial.TelemetryCalls).Contains("RecordBatch");
        await Assert.That(partial.TelemetryCalls.Count(call => call == "RecordOperation")).IsEqualTo(3);
    }

    [Test]
    public async Task BatchAbortsOnInfrastructureFailureAndStopsQueuedWork()
    {
        CheckInScenario scenario = new(UtcNow);
        string unavailable = RuntimeCapability.New();
        string validLast = RuntimeCapability.New();
        scenario.CredentialStates[scenario.Digest(unavailable)] = "Active";
        scenario.CredentialStates[scenario.Digest(validLast)] = "Active";
        scenario.UnavailableCredentialDigests.Add(scenario.Digest(unavailable));

        AdmissionCheckInUnavailableException exception = await Assert.ThrowsAsync<AdmissionCheckInUnavailableException>(async () =>
            await CheckInPorts.TypedService(scenario).ProcessBatchAsync(
                CheckInPorts.BatchRequest(scenario, [scenario.Credential, unavailable, validLast]),
                CancellationToken.None));

        await Assert.That(exception).IsNotNull();
        await Assert.That(scenario.UnitOfWork.TransactionCount).IsEqualTo(2);
        await Assert.That(scenario.AppendCount).IsEqualTo(1);
        await Assert.That(scenario.TelemetryCalls.Last()).IsEqualTo("RecordBacklog");
    }

    [Test]
    public async Task DuplicateCheckInAndUndoAreDeterministicAndHistoryRemainsAppendOnly()
    {
        CheckInScenario scenario = new(UtcNow) { MaximumEntries = 2 };

        AdmissionCheckInResult first = await CheckInAsync(scenario, scenario.Credential, "CheckIn", scenario.StaffActorId, null);
        AdmissionCheckInResult duplicate = await CheckInAsync(scenario, scenario.Credential, "CheckIn", scenario.StaffActorId, null);
        AdmissionCheckInResult undo = await CheckInAsync(scenario, scenario.Credential, "Undo", scenario.StaffActorId, null);
        AdmissionCheckInResult duplicateUndo = await CheckInAsync(scenario, scenario.Credential, "Undo", scenario.StaffActorId, null);
        AdmissionCheckInResult reentry = await CheckInAsync(scenario, scenario.Credential, "CheckIn", scenario.StaffActorId, null);

        await Assert.That(string.Join(',', new[]
        {
            first.Outcome,
            duplicate.Outcome,
            undo.Outcome,
            duplicateUndo.Outcome,
            reentry.Outcome
        })).IsEqualTo("CheckedIn,AlreadyCheckedIn,Undone,NotCheckedIn,CheckedIn");
        await Assert.That(string.Join(',', scenario.History.Select(value => value.Action)))
            .IsEqualTo("CheckIn,Undo,CheckIn");
        await Assert.That(string.Join(',', scenario.History.Select(value => value.Sequence)))
            .IsEqualTo("1,2,3");

        CheckInScenario singleEntry = new(UtcNow);
        _ = await CheckInAsync(singleEntry, singleEntry.Credential, "CheckIn", singleEntry.StaffActorId, null);
        _ = await CheckInAsync(singleEntry, singleEntry.Credential, "Undo", singleEntry.StaffActorId, null);
        AdmissionCheckInResult prohibitedReentry = await CheckInAsync(
            singleEntry, singleEntry.Credential, "CheckIn", singleEntry.StaffActorId, null);

        await Assert.That(prohibitedReentry.Outcome).IsEqualTo(AdmissionCheckInOutcome.Rejected);
        await Assert.That(singleEntry.History.Count).IsEqualTo(2);
    }

    [Test]
    public async Task UndoRequiresTheExactActiveCheckInFactIdentity()
    {
        CheckInScenario scenario = new(UtcNow);
        AdmissionCheckInResult checkedIn = await CheckInAsync(
            scenario, scenario.Credential, "CheckIn", scenario.StaffActorId, null);
        Guid activeCheckInId = checkedIn.CheckInId!.Value;

        AdmissionCheckInService service = CheckInPorts.TypedService(scenario);
        AdmissionCheckInResult mismatched = await service.ProcessAsync(
            CheckInPorts.Request(
                scenario,
                scenario.Credential,
                "Undo",
                staffActorId: scenario.StaffActorId,
                checkInId: Guid.CreateVersion7()),
            CancellationToken.None);
        await Assert.That(mismatched.Outcome).IsEqualTo(AdmissionCheckInOutcome.Rejected);
        await Assert.That(scenario.History).HasSingleItem();

        AdmissionCheckInResult exact = await service.ProcessAsync(
            CheckInPorts.Request(
                scenario,
                scenario.Credential,
                "Undo",
                staffActorId: scenario.StaffActorId,
                checkInId: activeCheckInId),
            CancellationToken.None);
        await Assert.That(exact.Outcome).IsEqualTo(AdmissionCheckInOutcome.Undone);
        await Assert.That(scenario.History.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScannerCapabilityIsDisclosedOnceNeverReturnedByReadAndRevocationIsImmediate()
    {
        CheckInScenario scenario = new(UtcNow);
        AdmissionScannerCapabilityService service = ScannerCapabilityPorts.TypedService(scenario);
        AdmissionScannerCapabilityIssuedResult issued = await service.IssueAsync(
            ScannerCapabilityPorts.IssueRequest(scenario), CancellationToken.None);
        string plaintext = issued.PlaintextCapability!;
        Guid capabilityId = issued.ScannerCapabilityId;
        AdmissionScannerCapabilityDescriptor read = await service.ReadAsync(
            ScannerCapabilityPorts.ReadRequest(scenario, capabilityId), CancellationToken.None);

        await Assert.That(string.IsNullOrWhiteSpace(plaintext)).IsFalse();
        await Assert.That(issued.ToString()).DoesNotContain(plaintext);
        await Assert.That(read.ToString()).DoesNotContain(plaintext);
        await Assert.That(read.GetType().GetProperties().Select(property => property.Name))
            .DoesNotContain("PlaintextCapability");
        await Assert.That(scenario.StoredScannerPlaintextCount).IsEqualTo(0);
        await Assert.That(scenario.ScannerMaterialIssueCalls).IsEqualTo(1);

        AdmissionScannerCapabilityIssuedResult duplicateIssue = await service.IssueAsync(
            ScannerCapabilityPorts.IssueRequest(scenario), CancellationToken.None);
        await Assert.That(duplicateIssue.Outcome).IsEqualTo(AdmissionScannerCapabilityIssueOutcome.AlreadyIssued);
        await Assert.That(duplicateIssue.ScannerCapabilityId).IsEqualTo(capabilityId);
        await Assert.That(duplicateIssue.PlaintextCapability).IsNull();
        await Assert.That(scenario.ScannerCapabilities.Count).IsEqualTo(1);

        AdmissionScannerCapabilityRevocationResult revoked = await service.RevokeAsync(
            ScannerCapabilityPorts.RevokeRequest(scenario, capabilityId), CancellationToken.None);
        AdmissionCheckInResult denied = await CheckInAsync(scenario, scenario.Credential, "CheckIn", null, capabilityId);

        await Assert.That(revoked.Outcome).IsEqualTo(AdmissionScannerCapabilityRevocationOutcome.Revoked);
        await Assert.That(denied.Outcome).IsEqualTo(AdmissionCheckInOutcome.Rejected);
        await Assert.That(scenario.AppendCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("WrongEvent")]
    [Arguments("WrongTarget")]
    [Arguments("Expired")]
    [Arguments("StolenFromOtherTenant")]
    public async Task ScannerCapabilityWrongScopeExpiryAndTheftFailWithOneGenericOutcome(string rejection)
    {
        CheckInScenario scenario = new(UtcNow);
        AdmissionScannerCapabilityIssuedResult issued = await IssueScannerCapabilityAsync(scenario);
        Guid capabilityId = issued.ScannerCapabilityId;
        Guid tenantId = scenario.TenantId;
        Guid eventId = scenario.EventId;
        Guid targetId = scenario.TargetId;
        if (rejection == "WrongEvent") eventId = Guid.CreateVersion7();
        if (rejection == "WrongTarget") targetId = Guid.CreateVersion7();
        if (rejection == "Expired") scenario.Clock.Advance(TimeSpan.FromHours(2));
        if (rejection == "StolenFromOtherTenant") tenantId = Guid.CreateVersion7();

        AdmissionCheckInResult result = await CheckInPorts.TypedService(scenario).ProcessAsync(
            CheckInPorts.Request(
                scenario,
                scenario.Credential,
                "CheckIn",
                tenantId,
                eventId,
                targetId,
                null,
                capabilityId),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionCheckInOutcome.Rejected);
        await Assert.That(scenario.AppendCount).IsEqualTo(0);
    }

    [Test]
    public async Task DoorResultIsBoundedAndExcludesCredentialRosterOrderPaymentAndParticipantData()
    {
        CheckInScenario scenario = new(UtcNow);
        AdmissionCheckInResult result = await CheckInAsync(
            scenario, scenario.Credential, "CheckIn", scenario.StaffActorId, null);
        string[] forbiddenFragments =
        [
            "Credential", "Capability", "Digest", "Attendee", "Participant", "Email", "Order",
            "Payment", "Answer", "Address", "Phone", "Roster"
        ];
        PropertyInfo[] properties = result.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);

        await Assert.That(properties.Length).IsLessThanOrEqualTo(8);
        await Assert.That(properties.Select(property => property.Name).Any(name =>
            forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
        await Assert.That(properties.Where(property => property.PropertyType == typeof(string))
            .Select(property => (string?)property.GetValue(result))
            .Where(value => value is not null)
            .All(value => value!.Length <= 128)).IsTrue();
        await Assert.That(result.ToString()).DoesNotContain(scenario.Credential);
    }

    [Test]
    public async Task RepositoryAndConnectivityErrorsFailClosedWithoutAdmissionSuccess()
    {
        CheckInScenario scenario = new(UtcNow) { FailRepository = true };
        AdmissionCheckInService service = CheckInPorts.TypedService(scenario);
        AdmissionCheckInRequest request = CheckInPorts.Request(scenario, scenario.Credential, "CheckIn");

        AdmissionCheckInUnavailableException exception = await Assert.ThrowsAsync<AdmissionCheckInUnavailableException>(async () =>
            await service.ProcessAsync(request, CancellationToken.None));

        await Assert.That(exception).IsNotNull();
        await Assert.That(scenario.AppendCount).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationTokenPropagatesToAuthorityDigestAndRepositoryWithoutWriting()
    {
        CheckInScenario scenario = new(UtcNow);
        AdmissionCheckInService service = CheckInPorts.TypedService(scenario);
        AdmissionCheckInRequest request = CheckInPorts.Request(scenario, scenario.Credential, "CheckIn");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.ProcessAsync(request, cancellation.Token));

        await Assert.That(scenario.ObservedCancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(scenario.AppendCount).IsEqualTo(0);
    }

    private static Task<AdmissionCheckInResult> CheckInAsync(
        CheckInScenario scenario,
        string credential,
        string action,
        Guid? staffActorId,
        Guid? scannerCapabilityId) => CheckInPorts.TypedService(scenario).ProcessAsync(
            CheckInPorts.Request(
                scenario,
                credential,
                action,
                scenario.TenantId,
                scenario.EventId,
                scenario.TargetId,
                staffActorId,
                scannerCapabilityId),
            CancellationToken.None);

    private static Task<AdmissionScannerCapabilityIssuedResult> IssueScannerCapabilityAsync(CheckInScenario scenario) =>
        ScannerCapabilityPorts.TypedService(scenario).IssueAsync(
            ScannerCapabilityPorts.IssueRequest(scenario), CancellationToken.None);
}

internal static class CheckInPorts
{
    internal static AdmissionCheckInService TypedService(CheckInScenario scenario) => new(
        new CheckInPortFake(scenario),
        new CheckInPortFake(scenario),
        new CheckInPortFake(scenario),
        new CheckInPortFake(scenario),
        scenario.UnitOfWork,
        scenario.Clock);

    internal static AdmissionCheckInRequest TypedRequest(CheckInScenario scenario) => new(
        scenario.TenantId,
        scenario.EventId,
        scenario.TargetId,
        scenario.Credential,
        AdmissionCheckInAction.CheckIn,
        null,
        scenario.StaffActorId,
        null);

    internal static AdmissionCheckInRequest Request(
        CheckInScenario scenario,
        string credential,
        string action,
        Guid? tenantId = null,
        Guid? eventId = null,
        Guid? targetId = null,
        Guid? staffActorId = null,
        Guid? scannerCapabilityId = null,
        Guid? checkInId = null) => new(
            tenantId ?? scenario.TenantId,
            eventId ?? scenario.EventId,
            targetId ?? scenario.TargetId,
            credential,
            action == "Undo" ? AdmissionCheckInAction.Undo : AdmissionCheckInAction.CheckIn,
            action == "Undo" ? AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection : null,
            staffActorId ?? (scannerCapabilityId is null ? scenario.StaffActorId : null),
            scannerCapabilityId,
            checkInId ?? (action == "Undo"
                ? scenario.History.LastOrDefault(fact => fact.Action == "CheckIn")?.Id
                : null));

    internal static AdmissionCheckInBatchRequest BatchRequest(
        CheckInScenario scenario,
        IReadOnlyList<string> credentials) => new(
            scenario.TenantId,
            scenario.EventId,
            scenario.TargetId,
            scenario.StaffActorId,
            null,
            credentials.Select((credential, index) => new AdmissionCheckInBatchItem(
                index,
                credential,
                AdmissionCheckInAction.CheckIn,
                null)).ToArray());
}

internal static class ScannerCapabilityPorts
{
    internal static AdmissionScannerCapabilityService TypedService(CheckInScenario scenario) => new(
        new CheckInPortFake(scenario),
        new CheckInPortFake(scenario),
        scenario.UnitOfWork,
        scenario.Clock);

    internal static AdmissionScannerCapabilityIssueRequest IssueRequest(
        CheckInScenario scenario,
        Guid? eventId = null,
        Guid? targetId = null) => new(
            scenario.ScannerCapabilityIssueRequestId,
            scenario.TenantId,
            eventId ?? scenario.EventId,
            targetId ?? scenario.TargetId,
            [AdmissionCheckInAction.CheckIn, AdmissionCheckInAction.Undo],
            "North entrance scanner",
            scenario.Clock.GetUtcNow().AddHours(1),
            scenario.StaffActorId);

    internal static AdmissionScannerCapabilityReadRequest ReadRequest(
        CheckInScenario scenario,
        Guid capabilityId) => new(scenario.TenantId, capabilityId);

    internal static AdmissionScannerCapabilityRevokeRequest RevokeRequest(
        CheckInScenario scenario,
        Guid capabilityId,
        Guid? eventId = null) => new(
            scenario.TenantId,
            eventId ?? scenario.EventId,
            capabilityId,
            scenario.StaffActorId,
            "DeviceLost");
}

internal sealed class CheckInScenario
{
    internal CheckInScenario(DateTime utcNow)
    {
        Clock = new CheckInTimeProvider(utcNow);
        UnitOfWork = new CheckInUnitOfWork();
        CredentialStates[Digest(Credential)] = "Active";
    }

    internal Guid TenantId { get; } = Guid.CreateVersion7();
    internal Guid EventId { get; } = Guid.CreateVersion7();
    internal Guid TargetId { get; } = Guid.CreateVersion7();
    internal Guid AdmissionTicketId { get; } = Guid.CreateVersion7();
    internal Guid StaffActorId { get; } = Guid.CreateVersion7();
    internal Guid ScannerCapabilityIssueRequestId { get; } = Guid.CreateVersion7();
    internal string Credential { get; } = RuntimeCapability.New();
    internal CheckInTimeProvider Clock { get; }
    internal CheckInUnitOfWork UnitOfWork { get; }
    internal Dictionary<string, string> CredentialStates { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> UnavailableCredentialDigests { get; } = new(StringComparer.Ordinal);
    internal Dictionary<Guid, AdmissionScannerCapability> ScannerCapabilities { get; } = [];
    internal List<CheckInFact> History { get; } = [];
    internal List<string> TelemetryCalls { get; } = [];
    internal string CredentialState
    {
        set => CredentialStates[Digest(Credential)] = value;
    }
    internal bool FailRepository { get; set; }
    internal bool TargetStopped { get; set; }
    internal bool ScannerCapabilityStoreRejected { get; set; }
    internal int CredentialDigestCalls { get; set; }
    internal int TenantDigestLookupCalls { get; set; }
    internal int AuthorityChecks { get; set; }
    internal int ScannerMaterialIssueCalls { get; set; }
    internal Guid ObservedLookupTenantId { get; set; }
    internal string[] ObservedLookupDigests { get; set; } = [];
    internal Guid ObservedLookupEventId { get; set; }
    internal Guid ObservedLookupTargetId { get; set; }
    internal int MaximumEntries { get; set; } = 1;
    internal string? LastAuthorityKind { get; set; }
    internal CancellationToken ObservedCancellationToken { get; set; }
    internal int AppendCount => History.Count;
    internal int StoredScannerPlaintextCount => ScannerCapabilities.Values.Count(value =>
        value.GetType().GetProperties().Any(property =>
            property.Name.Contains("Plaintext", StringComparison.OrdinalIgnoreCase) &&
            property.GetValue(value) is string text && text.Length > 0));

    internal string Digest(string plaintext) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{TenantId:N}:{plaintext}")));
}

internal sealed record CheckInFact(Guid Id, int Sequence, string Action, string CredentialDigest);
internal sealed class CheckInTimeProvider(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset now = new(utcNow);
    public override DateTimeOffset GetUtcNow() => now;
    internal void Advance(TimeSpan duration) => now = now.Add(duration);
}

internal sealed class CheckInUnitOfWork : IUnitOfWork
{
    internal int TransactionCount { get; private set; }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        TransactionCount++;
        return operation(ct);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        TransactionCount++;
        return operation(ct);
    }

    public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        operation(ct);
}

internal sealed class CheckInPortFake(CheckInScenario scenario) :
    IAdmissionCheckInTransaction,
    IAdmissionCheckInCredentialDigestService,
    IAdmissionCheckInAuthority,
    IAdmissionCheckInTelemetry,
    IAdmissionScannerCapabilityMaterialService,
    IAdmissionScannerCapabilityRepository
{
    public Task<AdmissionCheckInCredentialDigest> DigestAsync(
        AdmissionCheckInCredentialDigestRequest request,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        scenario.CredentialDigestCalls++;
        return Task.FromResult(new AdmissionCheckInCredentialDigest(
        [
            new AdmissionCheckInCredentialDigestCandidate(scenario.Digest(request.Credential), 7),
            new AdmissionCheckInCredentialDigestCandidate(scenario.Digest($"{request.Credential}:retained"), 6)
        ]));
    }

    public Task<AdmissionCheckInAuthorizationDecision> AuthorizeAsync(
        AdmissionCheckInAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        scenario.AuthorityChecks++;
        bool authorized;
        if (request.StaffActorId is not null)
        {
            scenario.LastAuthorityKind = "Staff";
            authorized = request.StaffActorId == scenario.StaffActorId && request.TenantId == scenario.TenantId;
        }
        else
        {
            scenario.LastAuthorityKind = "ScannerCapability";
            AdmissionScannerCapability? row = request.ScannerCapabilityId.HasValue &&
                scenario.ScannerCapabilities.TryGetValue(request.ScannerCapabilityId.Value, out AdmissionScannerCapability? found)
                    ? found
                    : null;
            AdmissionScannerCapabilityAction domainAction = request.Action == AdmissionCheckInAction.CheckIn
                ? AdmissionScannerCapabilityAction.CheckIn
                : AdmissionScannerCapabilityAction.Undo;
            authorized = row is not null && row.TenantId == request.TenantId && row.EventId == request.EventId &&
                         row.Permits(request.TargetId, domainAction, scenario.Clock.GetUtcNow().UtcDateTime);
        }
        return Task.FromResult(new AdmissionCheckInAuthorizationDecision(
            authorized ? AdmissionCheckInAuthorizationOutcome.Authorized : AdmissionCheckInAuthorizationOutcome.Denied));
    }

    public Task<AdmissionCheckInDecision?> ExecuteAsync(
        AdmissionCheckInTransactionRequest request,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        scenario.TenantDigestLookupCalls++;
        string[] digests = request.CredentialDigestCandidates.Select(candidate => candidate.LookupDigest).ToArray();
        string action = request.Action.ToString();
        scenario.ObservedLookupTenantId = request.TenantId;
        scenario.ObservedLookupEventId = request.EventId;
        scenario.ObservedLookupTargetId = request.TargetId;
        scenario.ObservedLookupDigests = digests;
        if (digests.Any(scenario.UnavailableCredentialDigests.Contains))
            throw new TimeoutException("simulated per-item admission repository outage");
        if (scenario.FailRepository) throw new TimeoutException("simulated admission repository outage");
        bool validLineage = request.TenantId == scenario.TenantId && request.EventId == scenario.EventId &&
                            request.TargetId == scenario.TargetId;
        string? matchedDigest = digests.SingleOrDefault(digest =>
            scenario.CredentialStates.TryGetValue(digest, out string? state) && state == "Active");
        CheckInFact? latest = scenario.History.LastOrDefault(fact => fact.CredentialDigest == matchedDigest);
        bool currentlyCheckedIn = latest?.Action == "CheckIn";
        int priorEntries = scenario.History.Count(fact =>
            fact.CredentialDigest == matchedDigest && fact.Action == "CheckIn");
        AdmissionCheckInResultCodeEnum? resultCode;
        if (!validLineage || matchedDigest is null) resultCode = null;
        else if (request.Action == AdmissionCheckInAction.CheckIn && currentlyCheckedIn)
            resultCode = AdmissionCheckInResultCodeEnum.AlreadyCheckedIn;
        else if (request.Action == AdmissionCheckInAction.Undo && !currentlyCheckedIn)
            resultCode = AdmissionCheckInResultCodeEnum.NotCheckedIn;
        else if (request.Action == AdmissionCheckInAction.Undo && request.CheckInId != latest!.Id)
            resultCode = null;
        else if (request.Action == AdmissionCheckInAction.CheckIn && priorEntries >= scenario.MaximumEntries)
            resultCode = null;
        else
        {
            resultCode = request.Action == AdmissionCheckInAction.Undo
                ? AdmissionCheckInResultCodeEnum.Undone
                : priorEntries == 0
                    ? AdmissionCheckInResultCodeEnum.CheckedIn
                    : AdmissionCheckInResultCodeEnum.ReEntered;
            scenario.History.Add(new CheckInFact(
                Guid.CreateVersion7(),
                scenario.History.Count + 1,
                action,
                matchedDigest));
        }
        if (!resultCode.HasValue)
        {
            return Task.FromResult<AdmissionCheckInDecision?>(null);
        }

        int entryCount = scenario.History.Count(fact =>
            fact.CredentialDigest == matchedDigest && fact.Action == "CheckIn");
        bool active = scenario.History.LastOrDefault(fact =>
            fact.CredentialDigest == matchedDigest)?.Action == "CheckIn";
        AdmissionCheckInState state = AdmissionCheckInState.Rehydrate(
            Guid.CreateVersion7(),
            scenario.TenantId,
            scenario.AdmissionTicketId,
            request.TargetId,
            active ? scenario.History[^1].Id : null,
            entryCount,
            active ? (entryCount * 2L) - 1L : entryCount * 2L,
            Guid.CreateVersion7());
        return Task.FromResult<AdmissionCheckInDecision?>(new AdmissionCheckInDecision(resultCode.Value, null, state));
    }

    public Task<AdmissionScannerCapabilityMaterial> IssueAsync(
        AdmissionScannerCapabilityMaterialRequest request,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        scenario.ScannerMaterialIssueCalls++;
        string plaintext = RuntimeCapability.New();
        return Task.FromResult(new AdmissionScannerCapabilityMaterial(plaintext, scenario.Digest(plaintext), 7));
    }

    public Task<AdmissionScannerCapabilityStoreResult> StoreAsync(
        AdmissionScannerCapability capability,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        EnsureNoPlaintextProperty(capability);
        AdmissionScannerCapability? existing = scenario.ScannerCapabilities.Values.SingleOrDefault(
            row => row.IssueRequestId == capability.IssueRequestId);
        bool rejected = scenario.ScannerCapabilityStoreRejected;
        bool created = !rejected && existing is null;
        AdmissionScannerCapability stored = existing ?? capability;
        if (created)
        {
            scenario.ScannerCapabilities.Add(stored.Id, stored);
        }
        return Task.FromResult(new AdmissionScannerCapabilityStoreResult(created, stored) { Rejected = rejected });
    }

    public Task<AdmissionScannerCapability?> GetAsync(
        Guid tenantId,
        Guid scannerCapabilityId,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        scenario.ScannerCapabilities.TryGetValue(scannerCapabilityId, out AdmissionScannerCapability? capability);
        return Task.FromResult(capability?.TenantId == tenantId ? capability : null);
    }

    public Task<AdmissionTarget?> FindPlatformManagedTargetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        AdmissionTarget? target = tenantId == scenario.TenantId && eventId == scenario.EventId && targetId == scenario.TargetId
            ? AdmissionTarget.Create(
                scenario.TargetId,
                scenario.TenantId,
                scenario.EventId,
                AdmissionTargetTypeEnum.Event,
                null,
                null)
            : null;
        if (scenario.TargetStopped)
        {
            target?.Stop();
        }
        return Task.FromResult(target);
    }

    public Task<AdmissionScannerCapability> UpdateAsync(
        AdmissionScannerCapability capability,
        CancellationToken cancellationToken)
    {
        Observe(cancellationToken);
        scenario.ScannerCapabilities[capability.Id] = capability;
        return Task.FromResult(capability);
    }

    public Task<IReadOnlyList<AdmissionScannerCapability>> ListAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AdmissionScannerCapability?> FindByDigestCandidatesAsync(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AdmissionScannerCapabilityDigestCandidates> DigestCandidatesAsync(
        AdmissionScannerCapabilityDigestCandidatesRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public void RecordOperation(
        AdmissionCheckInAction action,
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        AdmissionCheckInTelemetryOutcome outcome,
        double durationMilliseconds) => scenario.TelemetryCalls.Add(nameof(RecordOperation));

    public void RecordBatch(
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        int batchSize) => scenario.TelemetryCalls.Add(nameof(RecordBatch));

    public void RecordSaturation(
        AdmissionCheckInSaturationKind kind,
        AdmissionCheckInTelemetryOutcome outcome) => scenario.TelemetryCalls.Add(nameof(RecordSaturation));

    public void RecordBacklog(
        AdmissionCheckInBacklogKind kind,
        AdmissionTargetTypeEnum? targetType,
        long depth) => scenario.TelemetryCalls.Add(nameof(RecordBacklog));

    private void Observe(CancellationToken cancellationToken)
    {
        scenario.ObservedCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void EnsureNoPlaintextProperty(AdmissionScannerCapability capability)
    {
        if (capability.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Any(property =>
                property.Name.Contains("Plaintext", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Capability", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Scanner capability persistence must remain digest-only.");
        }
    }
}
