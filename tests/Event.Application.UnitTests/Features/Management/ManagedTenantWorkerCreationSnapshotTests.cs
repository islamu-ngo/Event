using Explore.Application.Features.Management;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.Management;

public sealed class ManagedTenantWorkerCreationSnapshotTests
{
    [Test]
    public async Task PaddedTenantNameReturnsTheSameNormalizedRequestThatWasHashed()
    {
        var operation = Operation();
        operation.RequestJson = operation.RequestJson!.Replace("Original Tenant", "  Original Tenant  ", StringComparison.Ordinal);

        var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId);

        await Assert.That(snapshot.ReadRequest().TenantName).IsEqualTo("Original Tenant");
        await Assert.That(ManagedTenantProvisioningRequestCodec.ComputeHash(snapshot.ReadRequest()))
            .IsEqualTo(operation.RequestHash);
        snapshot.RequireCurrent(operation);
    }

    [Test]
    public async Task NestedPlanAndQuotaNormalizationIsRetainedWithoutRequiringCanonicalJsonBytes()
    {
        var operation = Operation();
        operation.RequestJson = operation.RequestJson!
            .Replace("\"managed\"", "\" managed \"", StringComparison.Ordinal)
            .Replace("\"quotas\":[]", "\"quotas\":[{\"key\":\" storage.bytes \",\"limit\":100}]", StringComparison.Ordinal);
        operation.RequestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(
            ManagedTenantProvisioningRequestCodec.Normalize(
                ManagedTenantProvisioningRequestCodec.Deserialize(operation.RequestJson)));

        var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId);

        await Assert.That(snapshot.ReadRequest().Plan.Key).IsEqualTo("managed");
        await Assert.That(snapshot.ReadRequest().Plan.Quotas.Single().Key).IsEqualTo("storage.bytes");
        await Assert.That(ManagedTenantProvisioningRequestCodec.ComputeHash(snapshot.ReadRequest()))
            .IsEqualTo(operation.RequestHash);
        snapshot.RequireCurrent(operation);
    }

    [Test]
    public async Task InvalidRawModuleKeyCannotBorrowAValidNormalizedHash()
    {
        var operation = Operation();
        operation.RequestJson = operation.RequestJson!.Replace("\"events\"", "\" events \"", StringComparison.Ordinal);

        await Assert.That(() => ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CompletePersistedRequestRemainsDetachedFromLaterCallerMutation()
    {
        var operation = Operation();
        var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId);
        var copy = snapshot.ReadRequest();
        ((IList<string>)copy.ApprovedModules)[0] = "unrelated";
        operation.RequestJson = operation.RequestJson!.Replace("Original Tenant", "Changed Tenant", StringComparison.Ordinal);

        await Assert.That(snapshot.ReadRequest().TenantName).IsEqualTo("Original Tenant");
        await Assert.That(snapshot.ReadRequest().ApprovedModules.Single()).IsEqualTo("events");
        await Assert.That(() => snapshot.RequireCurrent(operation)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ACorrectlyRehashedReplacementRequestCannotReplaceTheOriginalSource()
    {
        var operation = Operation();
        var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId);
        operation.RequestJson = operation.RequestJson!.Replace("Original Tenant", "Changed Tenant", StringComparison.Ordinal);
        operation.RequestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(
            ManagedTenantProvisioningRequestCodec.Deserialize(operation.RequestJson));

        await Assert.That(() => snapshot.RequireCurrent(operation)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AValidHashDoesNotPermitADifferentPersistedCustomer()
    {
        var operation = Operation();
        operation.ExternalCustomerReference = "different-customer";

        await Assert.That(() => ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TamperedRequestCannotUseTheOriginalHash()
    {
        var operation = Operation();
        operation.RequestJson = operation.RequestJson!.Replace("Original Tenant", "Changed Tenant", StringComparison.Ordinal);

        await Assert.That(() => ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RetriedOutboxGenerationCannotBorrowTheOlderAttempt()
    {
        var operation = Operation();
        var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId);
        operation.CurrentOutboxMessageId = Guid.CreateVersion7();

        await Assert.That(() => snapshot.RequireCurrent(operation)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AdmissionMustNameTheExactCurrentGeneration()
    {
        var operation = Operation();

        await Assert.That(() => ManagedTenantWorkerCreationSnapshot.Capture(operation, Guid.CreateVersion7()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(ManagedTenantProvisioningStatus.Pending)]
    [Arguments(ManagedTenantProvisioningStatus.Cancelled)]
    [Arguments(ManagedTenantProvisioningStatus.Failed)]
    [Arguments(ManagedTenantProvisioningStatus.Succeeded)]
    public async Task OnlyTheStartedOriginalOperationCanBeCaptured(ManagedTenantProvisioningStatus status)
    {
        var operation = Operation();
        operation.Status = status;

        await Assert.That(() => ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OptimisticSourceVersionChangeRequiresFreshAdmission()
    {
        var operation = Operation();
        var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId);
        operation.RowVersion++;

        await Assert.That(() => snapshot.RequireCurrent(operation)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OriginalStartedSourceIsAcceptedWithoutBecomingAuthority()
    {
        var operation = Operation();
        var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId);

        snapshot.RequireCurrent(operation);
        await Assert.That(snapshot.OperationId).IsEqualTo(operation.Id);
        await Assert.That(snapshot.ManagedInstanceId).IsEqualTo(operation.ManagedInstanceId);
        await Assert.That(snapshot.OutboxMessageId).IsEqualTo(operation.CurrentOutboxMessageId);
    }

    private static ManagedTenantProvisioningOperation Operation()
    {
        var request = ManagedTenantProvisioningRequestCodec.Normalize(
            ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson));
        return new()
        {
            Id = Guid.CreateVersion7(),
            ManagedInstanceId = Guid.CreateVersion7(),
            ExternalRequestId = request.ExternalRequestId,
            ExternalCustomerReference = request.ExternalCustomerReference,
            TenantSlug = request.TenantSlug,
            RequestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(request),
            RequestJson = ManagedTenantProvisioningRequestCodec.Serialize(request),
            CurrentOutboxMessageId = Guid.CreateVersion7(),
            Status = ManagedTenantProvisioningStatus.Processing,
            CreatedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
            RowVersion = 7
        };
    }

    private const string RequestJson = """
        {"externalRequestId":"request-1","externalCustomerReference":"customer-1","tenantName":"Original Tenant","tenantSlug":"original-tenant",
         "administrator":{"localIdentity":{"localSubjectId":"0199ec4b-0000-7000-8000-000000000001"}},
         "plan":{"key":"managed","versionId":"0199ec4b-0000-7000-8000-000000000002"},"approvedModules":["events"],
         "directoryOperatorIdentity":{"publicName":"Operator","legalName":"Operator ASBL","operatorKindCode":"registered_organization","jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"contact@example.test","legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms","privacyUrl":"https://example.test/privacy"}}
        """;
}
