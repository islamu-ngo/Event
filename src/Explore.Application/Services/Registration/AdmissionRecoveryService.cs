using System.Collections.Concurrent;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionRecoveryService(
    IAdmissionRecoveryRepository repository,
    IAdmissionRecoveryIdentityResolver identityResolver,
    IAdmissionRecoveryCapabilityService capabilityService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IAdmissionRecoveryDeliveryStager deliveryStager,
    IAdmissionRecoveryAuditService auditService,
    IAdmissionRecoveryRateLimiter rateLimiter,
    IAdmissionRecoveryRequestStager requestStager,
    ILogger<AdmissionRecoveryService> logger)
{
    private readonly ConcurrentDictionary<Guid, RecoveryLineage> knownLineage = [];

    public async Task<AdmissionRecoveryRequestResult> RequestAsync(
        AdmissionRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryRequest normalized = request with
        {
            NormalizedIdentity = NormalizeIdentity(request.NormalizedIdentity)
        };
        AdmissionRecoveryRateLimitDecision decision = rateLimiter.TryAcquire(
            normalized.TenantId,
            normalized.NormalizedIdentity,
            timeProvider.GetUtcNow());
        if (!decision.Allowed)
        {
            throw new AdmissionRecoveryRateLimitExceededException(decision.RetryAfterSeconds);
        }

        try
        {
            await requestStager.StageAsync(
                normalized.TenantId,
                new AdmissionRecoveryRequestEnvelope(
                    normalized.NormalizedIdentity,
                    normalized.Purpose),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Admission recovery request staging failed uniformly for tenant {TenantId}; failure type {FailureType}",
                normalized.TenantId,
                exception.GetType().Name);
            throw new AdmissionRecoveryUnavailableException();
        }

        return Accepted();
    }

    public async Task ProcessRequestAsync(
        AdmissionRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        List<PreparedRecovery> committed = [];
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                committed = await ProcessRequestCoreAsync(request, token);
            },
            cancellationToken);
        RememberCommittedLineage(committed);
    }

    public async Task ProcessStagedRequestAsync(
        AdmissionRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        _ = await ProcessRequestCoreAsync(request, cancellationToken);
    }

    private async Task<List<PreparedRecovery>> ProcessRequestCoreAsync(
        AdmissionRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryRequest normalized = request with
        {
            NormalizedIdentity = NormalizeIdentity(request.NormalizedIdentity)
        };
        AdmissionRecoveryIdentityResult identity =
            await identityResolver.FindAsync(normalized, cancellationToken);
        if (!identity.IdentityPresent || identity.AdmissionTicketIds.Count == 0)
        {
            return [];
        }

        var prepared = new List<PreparedRecovery>(identity.AdmissionTicketIds.Count);
        DateTimeOffset createdAtUtc = timeProvider.GetUtcNow();
        foreach (Guid admissionTicketId in identity.AdmissionTicketIds)
        {
            AdmissionRecoveryCapability? current = await repository.FindLatestByTicketIdAsync(
                normalized.TenantId,
                admissionTicketId,
                normalized.Purpose,
                cancellationToken);
            var lineage = new RecoveryLineage(
                normalized.TenantId,
                current?.RecoveryRequestId ?? identity.RecoveryRequestId,
                admissionTicketId,
                normalized.Purpose,
                0);
            AdmissionRecoveryCapabilityMaterial material = await capabilityService.IssueAsync(
                new AdmissionRecoveryCapabilityIssueRequest(
                    lineage.TenantId,
                    lineage.RecoveryRequestId,
                    lineage.AdmissionTicketId,
                    lineage.Purpose,
                    lineage.KeyVersion),
                cancellationToken);
            prepared.Add(new PreparedRecovery(
                lineage,
                material,
                current,
                Guid.CreateVersion7(),
                current is null ? 1 : current.CapabilityVersion + 1));
        }

        var committed = new List<PreparedRecovery>(prepared.Count);
        foreach (PreparedRecovery recovery in prepared)
        {
            AdmissionRecoveryCapability replacement =
                CreateCapability(recovery, createdAtUtc);
            bool persisted;
            if (recovery.Existing is { ConsumedAt: null, RotatedAt: null } current)
            {
                persisted = await repository.TryRotateAsync(
                    current,
                    replacement,
                    createdAtUtc.UtcDateTime,
                    cancellationToken);
            }
            else
            {
                _ = await repository.AddAsync(replacement, cancellationToken);
                persisted = true;
            }
            if (!persisted)
            {
                continue;
            }

            committed.Add(recovery);
            await deliveryStager.StageAsync(
                new AdmissionRecoveryDeliveryRequest(
                    recovery.Lineage.TenantId,
                    recovery.Lineage.RecoveryRequestId,
                    recovery.Lineage.AdmissionTicketId,
                    recovery.Lineage.Purpose,
                    recovery.Material.Capability,
                    recovery.CapabilityVersion),
                cancellationToken);
            await auditService.AppendAsync(
                new AdmissionRecoveryAuditFact(
                    recovery.Lineage.TenantId,
                    recovery.Lineage.RecoveryRequestId,
                    recovery.Existing is null
                        ? "AdmissionRecoveryIssued"
                        : "AdmissionRecoveryRotated",
                    recovery.CapabilityVersion,
                    createdAtUtc),
                cancellationToken);
        }

        return committed;
    }

    private void RememberCommittedLineage(IEnumerable<PreparedRecovery> committed)
    {
        foreach (PreparedRecovery recovery in committed)
        {
            knownLineage[recovery.Lineage.RecoveryRequestId] =
                recovery.Lineage with { KeyVersion = recovery.Material.KeyVersion };
        }
    }

    public async Task<AdmissionRecoveryConsumeResult> ConsumeAsync(
        AdmissionRecoveryConsumeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Purpose != AdmissionRecoveryPurpose.TicketRecovery)
        {
            return new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.WrongPurpose);
        }

        RecoveryLineage? lineage = await ResolveLineageAsync(request, cancellationToken);
        if (lineage is null || lineage.TenantId != request.TenantId)
        {
            return new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.WrongTenant);
        }

        if (lineage.Purpose != request.Purpose)
        {
            return new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.WrongPurpose);
        }

        AdmissionRecoveryCapabilityDigest digest = await capabilityService.DigestAsync(
            new AdmissionRecoveryCapabilityDigestRequest(
                lineage.TenantId,
                lineage.RecoveryRequestId,
                lineage.AdmissionTicketId,
                lineage.Purpose,
                request.Capability,
                lineage.KeyVersion),
            cancellationToken);
        AdmissionRecoveryCapability? state = await repository.FindByProofDigestAsync(
            lineage.TenantId,
            lineage.RecoveryRequestId,
            lineage.AdmissionTicketId,
            lineage.Purpose,
            digest.KeyVersion,
            digest.LookupDigest,
            cancellationToken);
        if (state is null)
        {
            return new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.Invalid);
        }

        if (state.RotatedAt.HasValue)
        {
            return new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.Rotated);
        }

        if (state.ConsumedAt.HasValue)
        {
            return new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.AlreadyConsumed);
        }

        if (state.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            return new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.Expired);
        }

        bool consumed = await repository.TryConsumeAsync(
            state.TenantId,
            state.Id,
            state.LookupKeyVersion,
            state.LookupDigest,
            state.ConcurrencyStamp,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        return consumed
            ? new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.Consumed)
            : new AdmissionRecoveryConsumeResult(AdmissionRecoveryConsumeOutcome.AlreadyConsumed);
    }

    public async Task<AdmissionRecoveryResendResult> ResendAsync(
        AdmissionRecoveryResendRequest request,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryCapability? current = await repository.FindLatestByRequestIdAsync(
            request.TenantId,
            request.RecoveryRequestId,
            request.Purpose,
            cancellationToken);
        if (current is null || current.ConsumedAt.HasValue || current.RotatedAt.HasValue ||
            request.Purpose != AdmissionRecoveryPurpose.TicketRecovery)
        {
            return new AdmissionRecoveryResendResult(AdmissionRecoveryRequestOutcome.Accepted);
        }

        AdmissionRecoveryCapabilityMaterial replacement = await capabilityService.IssueAsync(
            new AdmissionRecoveryCapabilityIssueRequest(
                current.TenantId,
                current.RecoveryRequestId,
                current.AdmissionTicketId,
                request.Purpose,
                0),
            cancellationToken);
        DateTimeOffset rotatedAtUtc = timeProvider.GetUtcNow();
        AdmissionRecoveryCapability replacementEntity = AdmissionRecoveryCapability.Create(
            Guid.CreateVersion7(),
            current.TenantId,
            current.RecoveryRequestId,
            current.AdmissionTicketId,
            current.Purpose,
            current.CapabilityVersion + 1,
            replacement.KeyVersion,
            replacement.LookupDigest,
            replacement.ExpiresAtUtc.UtcDateTime,
            rotatedAtUtc.UtcDateTime,
            replacement.LocatorDigest);

        bool rotation = false;
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                rotation = await repository.TryRotateAsync(
                    current,
                    replacementEntity,
                    rotatedAtUtc.UtcDateTime,
                    token);
                if (rotation)
                {
                    await deliveryStager.StageAsync(
                        new AdmissionRecoveryDeliveryRequest(
                            current.TenantId,
                            current.RecoveryRequestId,
                            current.AdmissionTicketId,
                            request.Purpose,
                            replacement.Capability,
                            current.CapabilityVersion + 1),
                        token);
                }
                if (rotation)
                {
                    await auditService.AppendAsync(
                        new AdmissionRecoveryAuditFact(
                            current.TenantId,
                            current.RecoveryRequestId,
                            "AdmissionRecoveryRotated",
                            current.CapabilityVersion + 1,
                            rotatedAtUtc),
                        token);
                }
            },
            cancellationToken);
        if (!rotation)
        {
            return new AdmissionRecoveryResendResult(AdmissionRecoveryRequestOutcome.Accepted);
        }

        knownLineage[current.RecoveryRequestId] = new RecoveryLineage(
            current.TenantId,
            current.RecoveryRequestId,
            current.AdmissionTicketId,
            request.Purpose,
            replacement.KeyVersion);
        return new AdmissionRecoveryResendResult(AdmissionRecoveryRequestOutcome.Accepted);
    }

    private async Task<RecoveryLineage?> ResolveLineageAsync(
        AdmissionRecoveryConsumeRequest request,
        CancellationToken cancellationToken)
    {
        if (knownLineage.TryGetValue(request.RecoveryRequestId, out RecoveryLineage? known))
        {
            return known;
        }

        try
        {
            AdmissionRecoveryCapability? current = await repository.FindLatestByRequestIdAsync(
                request.TenantId,
                request.RecoveryRequestId,
                request.Purpose,
                cancellationToken);
            if (current is null)
            {
                return null;
            }

            var resolved = new RecoveryLineage(
                current.TenantId,
                current.RecoveryRequestId,
                current.AdmissionTicketId,
                request.Purpose,
                current.LookupKeyVersion);
            knownLineage.TryAdd(resolved.RecoveryRequestId, resolved);
            return resolved;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static AdmissionRecoveryRequestResult Accepted() =>
        new(AdmissionRecoveryRequestOutcome.Accepted);

    private static string NormalizeIdentity(string value) =>
        value.Trim().ToUpperInvariant();

    private static AdmissionRecoveryCapability CreateCapability(
        PreparedRecovery recovery,
        DateTimeOffset createdAtUtc) =>
        AdmissionRecoveryCapability.Create(
            recovery.CapabilityId,
            recovery.Lineage.TenantId,
            recovery.Lineage.RecoveryRequestId,
            recovery.Lineage.AdmissionTicketId,
            recovery.Lineage.Purpose.ToString(),
            recovery.CapabilityVersion,
            recovery.Material.KeyVersion,
            recovery.Material.LookupDigest,
            recovery.Material.ExpiresAtUtc.UtcDateTime,
            createdAtUtc.UtcDateTime,
            recovery.Material.LocatorDigest);

    private sealed record RecoveryLineage(
        Guid TenantId,
        Guid RecoveryRequestId,
        Guid AdmissionTicketId,
        AdmissionRecoveryPurpose Purpose,
        int KeyVersion);

    private sealed record PreparedRecovery(
        RecoveryLineage Lineage,
        AdmissionRecoveryCapabilityMaterial Material,
        AdmissionRecoveryCapability? Existing,
        Guid CapabilityId,
        int CapabilityVersion);

}
