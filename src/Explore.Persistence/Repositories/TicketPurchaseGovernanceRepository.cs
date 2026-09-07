using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class TicketPurchaseGovernanceRepository(
    ExploreDbContext dbContext,
    TimeProvider? timeProvider = null) : ITicketPurchaseGovernanceRepository
{
    private readonly TimeProvider _timeProvider =
        timeProvider ?? TimeProvider.System;

    public Task<TicketPurchasePolicyVersion?> GetPolicyVersionAsync(
        Guid tenantId,
        Guid eventId,
        Guid policyVersionId,
        CancellationToken cancellationToken) =>
        dbContext.TicketPurchasePolicyVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                policy =>
                    policy.TenantId == tenantId
                    && policy.EventId == eventId
                    && policy.Id == policyVersionId,
                cancellationToken);

    public Task<TicketPurchasePolicyVersion?>
        GetCurrentPolicyVersionAsync(
            Guid tenantId,
            Guid eventId,
            CancellationToken cancellationToken) =>
        dbContext.TicketPurchasePolicyVersions
            .AsNoTracking()
            .Where(policy =>
                policy.TenantId == tenantId
                && policy.EventId == eventId)
            .OrderByDescending(policy => policy.CreatedAt)
            .ThenByDescending(policy => policy.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddPolicyVersionAsync(
        TicketPurchasePolicyVersion policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        dbContext.TicketPurchasePolicyVersions.Add(policy);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TicketPurchaseReservationResult> ReserveAsync(
        TicketPurchasePolicyVersion policy,
        TicketPurchaseReservationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(policy, request);

        return await TicketPurchaseProviderOperations
            .ExecuteSerializableAsync(
                dbContext,
                CreateCanonicalLockScopes(request),
                async operationCancellationToken =>
            {
                TicketPurchaseOperation? existing =
                    await dbContext.TicketPurchaseOperations
                        .SingleOrDefaultAsync(
                            operation =>
                                operation.TenantId == request.TenantId
                                && operation.KeyHash ==
                                request.Operation.KeyHash,
                            operationCancellationToken);
                if (existing is not null)
                {
                    TicketPurchaseReservationResult replay =
                        string.Equals(
                            existing.FingerprintHash,
                            request.Operation.FingerprintHash,
                            StringComparison.Ordinal)
                            ? existing.ToReplayResult()
                            : new TicketPurchaseReservationResult(
                                TicketPurchaseReservationDisposition.OperationConflict,
                                null,
                                existing.EffectiveCeiling,
                                existing.ConsumedQuantity);
                    return replay;
                }

                TicketPurchasePolicyVersion persistedPolicy =
                    await LockPolicyAsync(
                        policy,
                        operationCancellationToken);

                TicketPurchaseAuthorityUsage? usage =
                    await dbContext.TicketPurchaseAuthorityUsages
                        .SingleOrDefaultAsync(
                            candidate =>
                                candidate.TenantId == request.TenantId
                                && candidate.EventId == request.EventId
                                && candidate.EnforcementKey ==
                                request.Authority.EnforcementKey,
                            operationCancellationToken);
                DateTime timestamp =
                    _timeProvider.GetUtcNow().UtcDateTime;
                if (usage is null)
                {
                    usage = TicketPurchaseAuthorityUsage.Create(
                        request.TenantId,
                        request.EventId,
                        request.Authority,
                        timestamp);
                }

                bool reserved = usage.TryConsume(
                    request.Quantity,
                    persistedPolicy.EffectiveCeiling,
                    timestamp);
                if (reserved
                    && dbContext.Entry(usage).State ==
                    EntityState.Detached)
                {
                    dbContext.TicketPurchaseAuthorityUsages.Add(usage);
                }

                TicketPurchaseOperation operation =
                    TicketPurchaseOperation.Record(
                        persistedPolicy,
                        request,
                        reserved ? usage.Id : null,
                        reserved
                            ? TicketPurchaseReservationDisposition.Reserved
                            : TicketPurchaseReservationDisposition.CeilingExceeded,
                        usage.ConsumedQuantity,
                        timestamp);
                dbContext.TicketPurchaseOperations.Add(operation);
                await dbContext.SaveChangesAsync(
                    operationCancellationToken);
                return operation.ToInitialResult();
            },
            cancellationToken);
    }

    private static void ValidateScope(
        TicketPurchasePolicyVersion policy,
        TicketPurchaseReservationRequest request)
    {
        if (policy.TenantId != request.TenantId
            || policy.EventId != request.EventId)
        {
            throw new InvalidOperationException(
                "Purchase policy and reservation scope must match.");
        }

        if (request.Authority.AccessMode ==
            TicketPurchaseAccessMode.NameOnly
            && request.Authority.OrderId != request.OrderId)
        {
            throw new InvalidOperationException(
                "Name-only authority must be scoped to the reserved order.");
        }
    }

    private async Task<TicketPurchasePolicyVersion> LockPolicyAsync(
        TicketPurchasePolicyVersion policy,
        CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence
            .AcquireAsync<TicketPurchasePolicyVersion>(
                dbContext,
                policy.TenantId,
                candidate => candidate.Id,
                policy.Id,
                cancellationToken);

        return await dbContext.TicketPurchasePolicyVersions
            .SingleAsync(
                candidate =>
                    candidate.Id == policy.Id
                    && candidate.TenantId == policy.TenantId
                    && candidate.EventId == policy.EventId,
                cancellationToken);
    }

    private static IReadOnlyList<string> CreateCanonicalLockScopes(
        TicketPurchaseReservationRequest request) =>
        new[]
        {
            $"purchase-operation:{request.TenantId:N}:{request.Operation.KeyHash}",
            $"purchase-authority:{request.TenantId:N}:{request.EventId:N}:{request.Authority.EnforcementKey}",
        }
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
