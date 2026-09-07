using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionRecoveryRedemptionService(
    IAdmissionRecoveryRepository repository,
    IAdmissionRecoveryCapabilityService capabilityService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IAdmissionRecoveryTicketDocumentService ticketDocumentService,
    IAdmissionRecoveryAuditService auditService)
{
    public async Task<AdmissionRecoveryConsumeResult> RedeemAsync(
        Guid tenantId,
        string capability,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(capability))
        {
            return Invalid();
        }

        IReadOnlyList<AdmissionRecoveryLocatorDigest> locators;
        try
        {
            locators = await capabilityService.DigestLocatorsAsync(capability, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Invalid();
        }

        AdmissionRecoveryCapability? state = await repository.FindByLocatorAsync(
            tenantId,
            locators,
            cancellationToken);
        if (state is null ||
            !string.Equals(
                state.Purpose,
                AdmissionRecoveryPurpose.TicketRecovery.ToString(),
                StringComparison.Ordinal))
        {
            return Invalid();
        }

        AdmissionRecoveryCapabilityDigest proof = await capabilityService.DigestAsync(
            new AdmissionRecoveryCapabilityDigestRequest(
                state.TenantId,
                state.RecoveryRequestId,
                state.AdmissionTicketId,
                AdmissionRecoveryPurpose.TicketRecovery,
                capability,
                state.LookupKeyVersion),
            cancellationToken);
        AdmissionRecoveryCapability? verified = await repository.FindByProofDigestAsync(
            state.TenantId,
            state.RecoveryRequestId,
            state.AdmissionTicketId,
            AdmissionRecoveryPurpose.TicketRecovery,
            proof.KeyVersion,
            proof.LookupDigest,
            cancellationToken);
        if (verified is null || verified.RotatedAt.HasValue || verified.ConsumedAt.HasValue ||
            verified.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            return Invalid();
        }

        AdmissionRecoveryConsumeResult result = Invalid();
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    bool consumed = await repository.TryConsumeAsync(
                        verified.TenantId,
                        verified.Id,
                        verified.LookupKeyVersion,
                        verified.LookupDigest,
                        verified.ConcurrencyStamp,
                        timeProvider.GetUtcNow().UtcDateTime,
                        token);
                    if (!consumed)
                    {
                        return;
                    }

                    AdmissionRecoveryTicketDocument? document =
                        await ticketDocumentService.RotateAndCreateAsync(
                            verified.TenantId,
                            verified.AdmissionTicketId,
                            token);
                    if (document is null)
                    {
                        throw new RecoveryDocumentUnavailableException();
                    }

                    result = new AdmissionRecoveryConsumeResult(
                        AdmissionRecoveryConsumeOutcome.Consumed,
                        verified.Id,
                        document);
                    await auditService.AppendAsync(
                        new AdmissionRecoveryAuditFact(
                            verified.TenantId,
                            verified.RecoveryRequestId,
                            "AdmissionRecoveryRedeemed",
                            verified.CapabilityVersion,
                            timeProvider.GetUtcNow()),
                        token);
                },
                cancellationToken);
        }
        catch (RecoveryDocumentUnavailableException)
        {
            return Invalid();
        }

        return result;
    }

    private static AdmissionRecoveryConsumeResult Invalid() =>
        new(AdmissionRecoveryConsumeOutcome.Invalid);

    private sealed class RecoveryDocumentUnavailableException : Exception
    {
    }
}
