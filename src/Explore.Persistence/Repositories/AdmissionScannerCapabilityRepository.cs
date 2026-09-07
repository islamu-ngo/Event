using System.Linq.Expressions;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionScannerCapabilityRepository(ExploreDbContext dbContext)
    : IAdmissionScannerCapabilityRepository
{
    private const string IssueSavepoint = "admission_scanner_issue";

    public async Task<AdmissionScannerCapabilityStoreResult> StoreAsync(
        AdmissionScannerCapability capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        RequireTransaction();

        await RelationalEntityRowFence.AcquireAsync<AdmissionTarget>(
            dbContext,
            capability.TenantId,
            target => target.Id,
            capability.AdmissionTargetId,
            cancellationToken);
        AdmissionTarget? target = await dbContext.AdmissionTargets
            .SingleOrDefaultAsync(value =>
                value.TenantId == capability.TenantId &&
                value.EventId == capability.EventId &&
                value.Id == capability.AdmissionTargetId &&
                dbContext.EventParticipationConfigurations.Any(configuration =>
                    configuration.TenantId == capability.TenantId &&
                    configuration.Id == capability.EventId &&
                    configuration.ParticipationHandlingModeId ==
                        (int)ParticipationHandlingModeEnum.PlatformManaged),
                cancellationToken);
        if (target is null || !target.IsOperational)
        {
            return new AdmissionScannerCapabilityStoreResult(false, capability)
            {
                Rejected = true
            };
        }

        AdmissionScannerCapability? existing = await FindByIssueRequestAsync(
            capability.TenantId,
            capability.IssueRequestId,
            cancellationToken);
        if (existing is not null)
        {
            return new AdmissionScannerCapabilityStoreResult(false, existing);
        }

        IDbContextTransaction? transaction = dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(IssueSavepoint, cancellationToken);
        }

        try
        {
            await dbContext.AdmissionScannerCapabilities.AddAsync(capability, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(IssueSavepoint, cancellationToken);
            }
            return new AdmissionScannerCapabilityStoreResult(true, capability);
        }
        catch (DbUpdateException exception)
            when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(IssueSavepoint, CancellationToken.None);
            }
            dbContext.ChangeTracker.Clear();
            AdmissionScannerCapability? winner = await FindByIssueRequestAsync(
                capability.TenantId,
                capability.IssueRequestId,
                cancellationToken);
            if (winner is null)
            {
                throw;
            }
            return new AdmissionScannerCapabilityStoreResult(false, winner);
        }
    }

    public Task<AdmissionScannerCapability?> GetAsync(
        Guid tenantId,
        Guid scannerCapabilityId,
        CancellationToken cancellationToken) => dbContext.AdmissionScannerCapabilities
        .SingleOrDefaultAsync(capability =>
            capability.TenantId == tenantId && capability.Id == scannerCapabilityId,
            cancellationToken);

    public async Task<IReadOnlyList<AdmissionScannerCapability>> ListAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) => await dbContext.AdmissionScannerCapabilities
        .AsNoTracking()
        .Where(capability => capability.TenantId == tenantId && capability.EventId == eventId)
        .OrderByDescending(capability => capability.IssuedAt)
        .ToArrayAsync(cancellationToken);

    public Task<AdmissionTarget?> FindPlatformManagedTargetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        CancellationToken cancellationToken) => dbContext.AdmissionTargets
        .AsNoTracking()
        .SingleOrDefaultAsync(target =>
            target.TenantId == tenantId &&
            target.EventId == eventId &&
            target.Id == targetId &&
            dbContext.EventParticipationConfigurations.Any(configuration =>
                configuration.TenantId == tenantId &&
                configuration.Id == eventId &&
                configuration.ParticipationHandlingModeId ==
                    (int)ParticipationHandlingModeEnum.PlatformManaged),
            cancellationToken);

    public Task<AdmissionScannerCapability?> FindByDigestCandidatesAsync(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (!AreValidDigestCandidates(candidates))
        {
            return Task.FromResult<AdmissionScannerCapability?>(null);
        }

        Expression<Func<AdmissionScannerCapability, bool>> predicate =
            BuildDigestCandidatePredicate(candidates);
        return dbContext.AdmissionScannerCapabilities
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.ScannerCapabilityAuthentication)
            .Where(predicate)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AdmissionScannerCapability> UpdateAsync(
        AdmissionScannerCapability capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        RequireTransaction();

        var entry = dbContext.Entry(capability);
        Guid originalConcurrencyStamp = capability.ConcurrencyStamp;
        if (entry.State == EntityState.Detached)
        {
            dbContext.AdmissionScannerCapabilities.Attach(capability);
            entry = dbContext.Entry(capability);
            entry.State = EntityState.Modified;
        }

        entry.Property(value => value.ConcurrencyStamp).OriginalValue = originalConcurrencyStamp;
        capability.ConcurrencyStamp = Guid.CreateVersion7();
        await dbContext.SaveChangesAsync(cancellationToken);
        return capability;
    }

    private static Expression<Func<AdmissionScannerCapability, bool>> BuildDigestCandidatePredicate(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates)
    {
        ParameterExpression capability = Expression.Parameter(
            typeof(AdmissionScannerCapability),
            "capability");
        Expression candidateMatch = Expression.Constant(false);
        foreach (AdmissionScannerCapabilityDigestCandidate candidate in candidates)
        {
            Expression keyMatch = Expression.Equal(
                Expression.Property(capability, nameof(AdmissionScannerCapability.LookupKeyVersion)),
                Expression.Constant(candidate.KeyVersion));
            Expression digestMatch = Expression.Equal(
                Expression.Property(capability, nameof(AdmissionScannerCapability.LookupDigest)),
                Expression.Constant(candidate.LookupDigest));
            candidateMatch = Expression.OrElse(
                candidateMatch,
                Expression.AndAlso(keyMatch, digestMatch));
        }

        return Expression.Lambda<Func<AdmissionScannerCapability, bool>>(
            candidateMatch,
            capability);
    }

    private static bool AreValidDigestCandidates(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate>? candidates) =>
        candidates is { Count: > 0 and <= AdmissionScannerCapabilityDigestOptions.MaximumKeyVersions } &&
        candidates.All(candidate =>
            candidate.KeyVersion > 0 &&
            !string.IsNullOrWhiteSpace(candidate.LookupDigest) &&
            candidate.LookupDigest.Length <= 256) &&
        candidates.Select(candidate => (candidate.KeyVersion, candidate.LookupDigest))
            .Distinct()
            .Count() == candidates.Count;

    private Task<AdmissionScannerCapability?> FindByIssueRequestAsync(
        Guid tenantId,
        Guid issueRequestId,
        CancellationToken cancellationToken) => dbContext.AdmissionScannerCapabilities
        .SingleOrDefaultAsync(capability =>
            capability.TenantId == tenantId && capability.IssueRequestId == issueRequestId,
            cancellationToken);

    private void RequireTransaction()
    {
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Scanner capability persistence requires an active unit-of-work transaction.");
        }
    }
}
