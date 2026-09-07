using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionRecoveryRepository(ExploreDbContext dbContext) :
    IAdmissionRecoveryRepository
{
    public async Task<AdmissionRecoveryCapability> AddAsync(
        AdmissionRecoveryCapability capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        await dbContext.AdmissionRecoveryCapabilities.AddAsync(capability, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return capability;
    }

    public Task<AdmissionRecoveryCapability?> FindByProofDigestAsync(
        Guid tenantId,
        Guid recoveryRequestId,
        Guid admissionTicketId,
        AdmissionRecoveryPurpose purpose,
        int keyVersion,
        string lookupDigest,
        CancellationToken cancellationToken) =>
        dbContext.AdmissionRecoveryCapabilities
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId &&
                value.RecoveryRequestId == recoveryRequestId &&
                value.AdmissionTicketId == admissionTicketId &&
                value.Purpose == purpose.ToString() &&
                value.LookupKeyVersion == keyVersion &&
                value.LookupDigest == lookupDigest,
                cancellationToken);

    public async Task<AdmissionRecoveryCapability?> FindByLocatorAsync(
        Guid tenantId,
        IReadOnlyList<AdmissionRecoveryLocatorDigest> locators,
        CancellationToken cancellationToken)
    {
        foreach (AdmissionRecoveryLocatorDigest locator in locators)
        {
            AdmissionRecoveryCapability? entity = await dbContext.AdmissionRecoveryCapabilities
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId &&
                    value.LookupKeyVersion == locator.KeyVersion &&
                    value.LocatorDigest == locator.LocatorDigest,
                    cancellationToken);
            if (entity is not null)
            {
                return entity;
            }
        }

        return null;
    }

    public Task<AdmissionRecoveryCapability?> FindLatestByRequestIdAsync(
        Guid tenantId,
        Guid recoveryRequestId,
        AdmissionRecoveryPurpose purpose,
        CancellationToken cancellationToken) =>
        dbContext.AdmissionRecoveryCapabilities
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId &&
                value.RecoveryRequestId == recoveryRequestId &&
                value.Purpose == purpose.ToString())
            .OrderByDescending(value => value.CapabilityVersion)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AdmissionRecoveryCapability?> FindLatestByTicketIdAsync(
        Guid tenantId,
        Guid admissionTicketId,
        AdmissionRecoveryPurpose purpose,
        CancellationToken cancellationToken) =>
        dbContext.AdmissionRecoveryCapabilities
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId &&
                value.AdmissionTicketId == admissionTicketId &&
                value.Purpose == purpose.ToString())
            .OrderByDescending(value => value.CapabilityVersion)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryConsumeAsync(
        Guid tenantId,
        Guid capabilityId,
        int keyVersion,
        string lookupDigest,
        Guid expectedConcurrencyStamp,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        Guid nextStamp = Guid.CreateVersion7();
        int changed = await dbContext.AdmissionRecoveryCapabilities
            .Where(value =>
                value.TenantId == tenantId &&
                value.Id == capabilityId &&
                value.LookupKeyVersion == keyVersion &&
                value.LookupDigest == lookupDigest &&
                value.ConcurrencyStamp == expectedConcurrencyStamp &&
                value.ConsumedAt == null &&
                value.RotatedAt == null &&
                value.ExpiresAt > occurredAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.ConsumedAt, occurredAtUtc)
                    .SetProperty(
                        value => value.ActiveUniquenessSlot,
                        value => value.CapabilityVersion)
                    .SetProperty(value => value.UpdatedAt, occurredAtUtc)
                    .SetProperty(value => value.ConcurrencyStamp, nextStamp),
                cancellationToken);
        return changed == 1;
    }

    public async Task<bool> TryRotateAsync(
        AdmissionRecoveryCapability current,
        AdmissionRecoveryCapability replacement,
        DateTime rotatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.TenantId != current.TenantId ||
            replacement.RecoveryRequestId != current.RecoveryRequestId ||
            replacement.AdmissionTicketId != current.AdmissionTicketId ||
            !string.Equals(replacement.Purpose, current.Purpose, StringComparison.Ordinal) ||
            replacement.CapabilityVersion != current.CapabilityVersion + 1)
        {
            throw new ArgumentException("Recovery replacement lineage is invalid.", nameof(replacement));
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await TryRotateCoreAsync(
                current,
                replacement,
                rotatedAtUtc,
                cancellationToken);
        }

        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteInTransactionAsync(
            operation: token => TryRotateCoreAsync(
                current,
                replacement,
                rotatedAtUtc,
                token),
            verifySucceeded: token => dbContext.AdmissionRecoveryCapabilities
                .AsNoTracking()
                .AnyAsync(value =>
                    value.TenantId == replacement.TenantId &&
                    value.Id == replacement.Id,
                    token),
            cancellationToken);
    }

    private async Task<bool> TryRotateCoreAsync(
        AdmissionRecoveryCapability current,
        AdmissionRecoveryCapability replacement,
        DateTime rotatedAtUtc,
        CancellationToken cancellationToken)
    {
        int changed = await dbContext.AdmissionRecoveryCapabilities
            .Where(value =>
                value.TenantId == current.TenantId &&
                value.Id == current.Id &&
                value.LookupKeyVersion == current.LookupKeyVersion &&
                value.LookupDigest == current.LookupDigest &&
                value.ConcurrencyStamp == current.ConcurrencyStamp &&
                value.ConsumedAt == null &&
                value.RotatedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.RotatedAt, rotatedAtUtc)
                    .SetProperty(
                        value => value.ActiveUniquenessSlot,
                        value => value.CapabilityVersion)
                    .SetProperty(value => value.UpdatedAt, rotatedAtUtc)
                    .SetProperty(value => value.ConcurrencyStamp, Guid.CreateVersion7()),
                cancellationToken);
        if (changed != 1)
        {
            return false;
        }

        await dbContext.AdmissionRecoveryCapabilities.AddAsync(replacement, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
