using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PaidEventPolicyRepository(ExploreDbContext dbContext) : IPaidEventPolicyRepository
{
    private readonly List<PaidEventPolicyVersion> _importRevisions = [];

    private const string ActiveTenantPolicyValidationBypassReason =
        "Instance paid-policy revision validates bounded pages of every active tenant policy before narrowing the instance ceiling.";

    public Task<PaidEventPolicyVersion?> GetActiveInstanceAsync(CancellationToken cancellationToken) =>
        dbContext.IsConfigurationInstanceImportOwner
            && ConfigurationImportRepositoryNativeOperations.For(dbContext) is { } native
            ? native.ReadActivePolicyAsync(null, cancellationToken)
            :
        dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .SingleOrDefaultAsync(policy => policy.TenantId == null && policy.IsActive, cancellationToken);

    public Task<PaidEventPolicyVersion?> GetActiveTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.IsConfigurationInstanceImportOwner
            && ConfigurationImportRepositoryNativeOperations.For(dbContext) is { } native
            ? native.ReadActivePolicyAsync(tenantId, cancellationToken)
            :
        dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .SingleOrDefaultAsync(policy => policy.TenantId == tenantId && policy.IsActive, cancellationToken);

    public async Task<PaidEventPolicyVersion[]> ListActiveTenantsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Active tenant paid-policy pages must contain between 1 and {IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize} entities.");
        }

        if (dbContext.IsConfigurationInstanceImportOwner
            && ConfigurationImportRepositoryNativeOperations.For(dbContext) is { } native)
            return await native.ReadActiveTenantPolicyPageAsync(offset, limit, cancellationToken);

        if (PaidPolicyCeilingPageQuery.ReadNativeAsync(dbContext, offset, limit, cancellationToken) is { } page)
        {
            string[] documents = await page;
            return documents.Select(document =>
            {
                CeilingAggregate aggregate = JsonSerializer.Deserialize<CeilingAggregate>(document)
                    ?? throw new InvalidOperationException("Active tenant ceiling validation aggregate is required.");
                return PaidEventPolicyVersion.RestoreActiveForPolicyEvaluation(
                    aggregate.Id,
                    aggregate.TenantId,
                    aggregate.VersionNumber,
                    aggregate.IsPaymentsEnabled,
                    aggregate.AllowedOrganizerKindIds.Select(id => (ActorTypeEnum)id),
                    aggregate.RequiresLocalVerification,
                    aggregate.AllowedCurrencyCodes,
                    aggregate.DefaultCurrencyCode,
                    aggregate.RefundProtectionIds.Select(id => (PaidEventRefundProtection)id),
                    aggregate.CurrencyRiskLimits.Select(limit => PaidEventPolicyCurrencyRiskLimit.Create(
                        limit.CurrencyCode,
                        limit.PerEventSalesCeilingMinor,
                        limit.PerEventSalesCountCeiling,
                        limit.RollingOrganizerSalesCeilingMinor,
                        limit.RollingOrganizerSalesCountCeiling,
                        limit.RollingOrganizerWindowDays,
                        limit.HighValueReviewThresholdMinor)),
                    aggregate.RequiresFirstPaidEventReview,
                    aggregate.FarFutureReviewThresholdDays);
            }).ToArray();
        }

        return await dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(ActiveTenantPolicyValidationBypassReason)
            .AsNoTracking()
            .Where(policy => policy.TenantId != null && policy.IsActive)
            .OrderBy(policy => policy.TenantId)
            .Skip(offset)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PaidEventPolicyVersion[]> ListTenantHistoryAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.PaidEventPolicyVersions
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(policy => policy.TenantId == tenantId)
            .OrderBy(policy => policy.VersionNumber)
            .ToArrayAsync(cancellationToken);

    public async Task AddAsync(PaidEventPolicyVersion policy, CancellationToken cancellationToken)
    {
        if (dbContext.IsConfigurationInstanceImportOwner
            && ConfigurationImportRepositoryNativeOperations.For(dbContext) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(policy);
            _importRevisions.Add(policy);
            return;
        }
        await dbContext.PaidEventPolicyVersions.AddAsync(policy, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (PaidEventPolicyVersion revision in _importRevisions)
            await PostgresConfigurationImportPaidPolicyContract.WriteAsync(dbContext, revision, cancellationToken);
        _importRevisions.Clear();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record CeilingAggregate(
        Guid Id,
        Guid TenantId,
        int VersionNumber,
        bool IsPaymentsEnabled,
        int[] AllowedOrganizerKindIds,
        bool RequiresLocalVerification,
        string[] AllowedCurrencyCodes,
        string? DefaultCurrencyCode,
        int[] RefundProtectionIds,
        CeilingRiskLimit[] CurrencyRiskLimits,
        bool RequiresFirstPaidEventReview,
        int? FarFutureReviewThresholdDays);

    private sealed record CeilingRiskLimit(
        string CurrencyCode,
        long? PerEventSalesCeilingMinor,
        int? PerEventSalesCountCeiling,
        long? RollingOrganizerSalesCeilingMinor,
        int? RollingOrganizerSalesCountCeiling,
        int? RollingOrganizerWindowDays,
        long? HighValueReviewThresholdMinor);
}
