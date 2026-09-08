using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApplicationUnitTests.Contracts.Admissions.Support;

internal static class AdmissionRecoveryPorts
{
    internal static AdmissionRecoveryRequest Request(AdmissionTestScenario scenario, string purpose) => new(
        scenario.TenantId,
        scenario.NormalizedIdentity,
        Purpose(purpose));

    internal static AdmissionRecoveryConsumeRequest Consume(
        AdmissionTestScenario scenario,
        string capability,
        string purpose,
        Guid tenantId) => new(
            tenantId,
            scenario.RecoveryRequestId,
            capability,
            Purpose(purpose));

    internal static AdmissionRecoveryResendRequest Resend(AdmissionTestScenario scenario) => new(
        scenario.TenantId,
        scenario.RecoveryRequestId,
        AdmissionRecoveryPurpose.TicketRecovery);

    internal static AdmissionRecoveryService TypedService(AdmissionTestScenario scenario) => new(
        new RecoveryRepositoryFake(scenario),
        new RecoveryIdentityResolverFake(scenario),
        new RecoveryCapabilityFake(scenario),
        scenario.UnitOfWork,
        scenario.Clock,
        new RecoveryDeliveryStagerFake(scenario),
        new RecoveryAuditFake(),
        new RecoveryRateLimiterFake(),
        new RecoveryRequestStagerFake(scenario),
        NullLogger<AdmissionRecoveryService>.Instance);

    internal static AdmissionRecoveryRequest TypedRequest(AdmissionTestScenario scenario) => new(
        scenario.TenantId,
        scenario.NormalizedIdentity,
        AdmissionRecoveryPurpose.TicketRecovery);

    private static AdmissionRecoveryPurpose Purpose(string value) => value == "TransferAcceptance"
        ? AdmissionRecoveryPurpose.TransferAcceptance
        : AdmissionRecoveryPurpose.TicketRecovery;
}

internal sealed class RecoveryDeliveryStagerFake(AdmissionTestScenario scenario) :
    IAdmissionRecoveryDeliveryStager
{
    public Task<AdmissionRecoveryDeliveryResult> StageAsync(
        AdmissionRecoveryDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        if (!scenario.UnitOfWork.InTransaction)
        {
            throw new InvalidOperationException("Protected recovery staging must be transactional.");
        }

        scenario.DeliverCapability(request.Capability);
        return Task.FromResult(
            new AdmissionRecoveryDeliveryResult(AdmissionRecoveryDeliveryOutcome.Accepted));
    }
}

internal sealed class RecoveryAuditFake : IAdmissionRecoveryAuditService
{
    public Task AppendAsync(
        AdmissionRecoveryAuditFact fact,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class RecoveryRateLimiterFake : IAdmissionRecoveryRateLimiter
{
    public AdmissionRecoveryRateLimitDecision TryAcquire(
        Guid tenantId,
        string normalizedIdentity,
        DateTimeOffset occurredAtUtc) =>
        new(true);
}

internal sealed class RecoveryRequestStagerFake(AdmissionTestScenario scenario) :
    IAdmissionRecoveryRequestStager
{
    public Task StageAsync(
        Guid tenantId,
        AdmissionRecoveryRequestEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (tenantId != scenario.TenantId ||
            envelope.Purpose != AdmissionRecoveryPurpose.TicketRecovery ||
            envelope.NormalizedIdentity != scenario.NormalizedIdentity.ToUpperInvariant())
        {
            throw new InvalidOperationException("Recovery request staging facts do not match.");
        }

        scenario.RecoveryRequestStageCalls++;
        if (scenario.FailRecoveryRequestStaging)
        {
            throw new InvalidOperationException("simulated durable staging failure");
        }

        return Task.CompletedTask;
    }
}

internal sealed class RecoveryCapabilityFake(AdmissionTestScenario scenario) : IAdmissionRecoveryCapabilityService
{
    public Task<AdmissionRecoveryCapabilityMaterial> IssueAsync(
        AdmissionRecoveryCapabilityIssueRequest request,
        CancellationToken cancellationToken)
    {
        RequireLineage(request);
        scenario.DigestIssueCalls++;
        string capability = RuntimeCapability.New();
        string digest = Digest(capability);
        return Task.FromResult(new AdmissionRecoveryCapabilityMaterial(
            capability,
            digest,
            7,
            request.Purpose,
            scenario.Clock.GetUtcNow().AddHours(1),
            digest));
    }

    public Task<AdmissionRecoveryCapabilityDigest> DigestAsync(
        AdmissionRecoveryCapabilityDigestRequest request,
        CancellationToken cancellationToken)
    {
        RequireLineage(request);
        return Task.FromResult(new AdmissionRecoveryCapabilityDigest(Digest(request.Capability), 7));
    }

    public Task<IReadOnlyList<AdmissionRecoveryLocatorDigest>> DigestLocatorsAsync(
        string capability,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    private static string Digest(string capability) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capability)));

    private void RequireLineage(AdmissionRecoveryCapabilityIssueRequest request)
    {
        if (request.TenantId != scenario.TenantId ||
            request.RecoveryRequestId != scenario.RecoveryRequestId ||
            request.AdmissionTicketId != scenario.CurrentAdmissionTicketId ||
            request.Purpose != AdmissionRecoveryPurpose.TicketRecovery)
        {
            throw new InvalidOperationException("Recovery capability lineage does not match.");
        }
    }

    private void RequireLineage(AdmissionRecoveryCapabilityDigestRequest request)
    {
        if (request.TenantId != scenario.TenantId ||
            request.RecoveryRequestId != scenario.RecoveryRequestId ||
            request.AdmissionTicketId != scenario.CurrentAdmissionTicketId ||
            request.Purpose != AdmissionRecoveryPurpose.TicketRecovery)
        {
            throw new InvalidOperationException("Recovery capability lineage does not match.");
        }
    }
}
