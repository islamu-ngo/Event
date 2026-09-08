using System.Linq.Expressions;
using Explore.Application.Contracts.Waitlist;
using Explore.Domain;
using Explore.Domain.Interfaces;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class FairReturnWaitlistRepository(
    ExploreDbContext dbContext) :
    IFairReturnWaitlistRepository
{
    public const string LiteralQueueOrder =
        "priority>enqueued-at>id";
    public const string CanonicalFenceOrder =
        "policy>supply>queue>offer>binding>" +
        "payment-dispatch>provider-observation>" +
        "refund-intent";

    public async Task<
        FairReturnWaitlistAccessContext?>
        GetAccessAsync(
            Guid tenantId,
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            CancellationToken cancellationToken)
    {
        RegistrationOrder? order =
            await dbContext.RegistrationOrders
                .AsNoTracking()
                .Include(value => value.Lines)
                    .ThenInclude(value =>
                        value.Assignments)
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id ==
                        registrationOrderId,
                    cancellationToken);
        RegistrationOrderLine? line = order?.Lines
            .SingleOrDefault(value =>
                value.Id ==
                    registrationOrderLineId);
        if (order is null || line is null)
        {
            return null;
        }

        EventWaitlistEntry? entry =
            await dbContext.EventWaitlistEntries
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.RegistrationOrderId ==
                        registrationOrderId
                    && value
                        .RegistrationOrderLineId ==
                        registrationOrderLineId)
                .OrderByDescending(value =>
                    value.OpenRegistrationOrderLineId
                        .HasValue)
                .ThenByDescending(value =>
                    value.CreatedAt)
                .FirstOrDefaultAsync(
                    cancellationToken);
        EventWaitlistOffer? offer = entry is null
            ? null
            : await dbContext.EventWaitlistOffers
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.EventWaitlistEntryId ==
                        entry.Id)
                .OrderByDescending(value =>
                    value.OpenEventWaitlistEntryId
                        .HasValue)
                .ThenByDescending(value =>
                    value.CreatedAt)
                .FirstOrDefaultAsync(
                    cancellationToken);
        FairReturnSupplyUnit? supply =
            offer is not null
                ? await dbContext
                    .FairReturnSupplyUnits
                    .AsNoTracking()
                    .SingleOrDefaultAsync(value =>
                        value.TenantId == tenantId
                        && value.Id ==
                            offer
                                .FairReturnSupplyUnitId,
                        cancellationToken)
                : await dbContext
                    .FairReturnSupplyUnits
                    .AsNoTracking()
                    .Where(value =>
                        value.TenantId == tenantId
                        && value.EventId == eventId
                        && value
                            .SellerRegistrationOrderLineId ==
                            registrationOrderLineId)
                    .OrderByDescending(value =>
                        value.CreatedAt)
                    .FirstOrDefaultAsync(
                        cancellationToken);
        FairReturnSourceBinding? binding =
            await dbContext.FairReturnSourceBindings
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && (value
                            .BuyerRegistrationOrderLineId ==
                        registrationOrderLineId
                        || supply != null
                        && value
                            .FairReturnSupplyUnitId ==
                            supply.Id))
                .OrderByDescending(value =>
                    value.CreatedAt)
                .FirstOrDefaultAsync(
                    cancellationToken);
        FairReturnSupplyPolicy? policy =
            await dbContext.FairReturnSupplyPolicies
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.TicketCatalogVersionId ==
                        line.TicketCatalogVersionId
                    && value.EventTicketTypeId ==
                        line.TicketTypeId,
                    cancellationToken);
        TicketPurchaseOperation? purchaseOperation =
            await dbContext.TicketPurchaseOperations
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.OrderId ==
                        registrationOrderId
                    && value.Disposition ==
                        TicketPurchaseReservationDisposition
                            .Reserved)
                .OrderByDescending(value =>
                    value.CreatedAt)
                .FirstOrDefaultAsync(
                    cancellationToken);
        long position = 0;
        if (entry?.StatusId ==
            (int)EventWaitlistEntryStatus.Queued)
        {
            Guid[] ordered = await dbContext
                .EventWaitlistEntries
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.EventTicketTypeId ==
                        entry.EventTicketTypeId
                    && value.TicketCatalogVersionId ==
                        entry.TicketCatalogVersionId
                    && value.PurchasePolicySnapshotId ==
                        entry.PurchasePolicySnapshotId
                    && value.CurrencyCode ==
                        entry.CurrencyCode
                    && value.CommercialTermsDigest ==
                        entry.CommercialTermsDigest
                    && value.AdmissionEntitlementDigest ==
                        entry.AdmissionEntitlementDigest
                    && value.GrossMinorUnits ==
                        entry.GrossMinorUnits
                    && value.RefundFundingModeId ==
                        entry.RefundFundingModeId
                    && value.StatusId ==
                        (int)EventWaitlistEntryStatus
                            .Queued)
                .OrderByDescending(value =>
                    value.Priority)
                .ThenBy(value =>
                    value.EnqueuedAt)
                .ThenBy(value => value.Id)
                .Select(value => value.Id)
                .Take(1_000)
                .ToArrayAsync(
                    cancellationToken);
            int index = Array.IndexOf(
                ordered,
                entry.Id);
            position = index >= 0
                ? index + 1L
                : 999;
        }
        return new FairReturnWaitlistAccessContext(
            order,
            line,
            entry,
            offer,
            supply,
            binding,
            policy,
            purchaseOperation,
            position);
    }

    public Task<EventWaitlistEntry> EnqueueAsync(
        EventWaitlistEntry entry,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            await FenceAsync<RegistrationOrderLine>(
                entry.TenantId,
                entry.RegistrationOrderLineId,
                line => line.Id,
                cancellationToken);
            EventWaitlistEntry? existing =
                await dbContext.EventWaitlistEntries
                    .SingleOrDefaultAsync(value =>
                        value.TenantId ==
                            entry.TenantId
                        && value
                            .OpenRegistrationOrderLineId ==
                            entry
                                .RegistrationOrderLineId,
                        cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            dbContext.EventWaitlistEntries.Add(entry);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return entry;
        }, cancellationToken);

    public Task<EventWaitlistEntry?> LeaveAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderLineId,
        DateTime withdrawnAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            EventWaitlistEntry? identity =
                await dbContext.EventWaitlistEntries
                    .AsNoTracking()
                    .SingleOrDefaultAsync(value =>
                        value.TenantId == tenantId
                        && value.EventId == eventId
                        && value
                            .OpenRegistrationOrderLineId ==
                            registrationOrderLineId,
                        cancellationToken);
            if (identity is null)
            {
                return null;
            }
            EventWaitlistEntry entry =
                await LockEntryAsync(
                    tenantId,
                    identity.Id,
                    cancellationToken);
            if (entry.StatusId !=
                (int)EventWaitlistEntryStatus.Queued)
            {
                return null;
            }
            entry.Withdraw(withdrawnAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return entry;
        }, cancellationToken);

    public Task<bool>
        HasReplacementSettlementAsync(
            Guid tenantId,
            Guid fairReturnSourceBindingId,
            CancellationToken cancellationToken) =>
        dbContext.WaitlistPaymentIntents
            .AsNoTracking()
            .AnyAsync(value =>
                value.TenantId == tenantId
                && value.FairReturnSourceBindingId ==
                    fairReturnSourceBindingId
                && value.ReplacementPaymentSettledAt
                    .HasValue,
                cancellationToken);

    public Task<FairReturnWaitlistResult> AllocateAsync(
        FairReturnAllocationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            FairReturnSupplyPolicy? policy =
                await LockPolicyAsync(
                    request.TenantId,
                    request.EventId,
                    request.FairReturnSupplyPolicyId,
                    cancellationToken);
            if (policy is null || !policy.IsEnabled)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }

            EventWaitlistEntry? candidate =
                await FindNextEntryAsync(
                    request.TenantId,
                    request.EventId,
                    policy,
                    cancellationToken);
            if (candidate is null)
            {
                return Unavailable(
                    FairReturnOutcome
                        .NoCommerciallyEquivalentSupply);
            }
            FairReturnSupplyUnit? supply =
                await LockAvailableSupplyAsync(
                    request.TenantId,
                    request.EventId,
                    policy,
                    candidate,
                    cancellationToken);
            EventWaitlistEntry? entry =
                supply is null
                    ? null
                    : await LockQueuedEntryAsync(
                        request.TenantId,
                        request.EventId,
                        candidate.Id,
                        cancellationToken);
            if (supply is null
                || entry is null
                || !entry.IsCommerciallyEquivalentTo(
                    supply))
            {
                return Unavailable(
                    FairReturnOutcome
                        .NoCommerciallyEquivalentSupply);
            }

            FairReturnSourceBinding binding =
                FairReturnSourceBinding.Create(
                    request.SourceBindingId,
                    supply,
                    entry,
                    request.AllocatedAtUtc);
            EventWaitlistOffer offer =
                EventWaitlistOffer.Create(
                    request.EventWaitlistOfferId,
                    policy,
                    entry,
                    supply,
                    binding.Id,
                    request.ExistingCapacityHoldId,
                    request.AllocatedAtUtc);
            dbContext.FairReturnSourceBindings.Add(
                binding);
            dbContext.EventWaitlistOffers.Add(offer);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return new FairReturnWaitlistResult(
                FairReturnOutcome.Allocated,
                supply,
                entry,
                offer,
                binding);
        }, cancellationToken);

    public Task<FairReturnWaitlistResult> WithdrawAsync(
        FairReturnWithdrawalRequest request,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            FairReturnSupplyUnit? supply =
                await LockSupplyAsync(
                    request.TenantId,
                    request.EventId,
                    request.FairReturnSupplyUnitId,
                    cancellationToken);
            if (supply is null)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }
            FairReturnSourceBinding? bindingSnapshot =
                await FindBindingBySupplyAsync(
                    request.TenantId,
                    supply.Id,
                    cancellationToken);
            if (bindingSnapshot is null)
            {
                supply.Withdraw(
                    request.WithdrawnAtUtc);
                await dbContext.SaveChangesAsync(
                    cancellationToken);
                return new FairReturnWaitlistResult(
                    FairReturnOutcome.Withdrawn,
                    supply,
                    null,
                    null,
                    null);
            }
            FairReturnSupplyUnit? replacement =
                await LockEquivalentSupplyAsync(
                    request.TenantId,
                    request.EventId,
                    supply,
                    cancellationToken);
            if (replacement is null)
            {
                return new FairReturnWaitlistResult(
                    FairReturnOutcome
                        .PrivateConflict,
                    supply,
                    null,
                    null,
                    bindingSnapshot);
            }
            FairReturnSourceBinding? binding =
                await LockBindingBySupplyAsync(
                    request.TenantId,
                    supply.Id,
                    cancellationToken);
            if (binding is null)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }
            if (binding.PaymentDispatchClaimedAt
                .HasValue)
            {
                return new FairReturnWaitlistResult(
                    FairReturnOutcome
                        .PaymentHandoffWon,
                    supply,
                    null,
                    null,
                    binding);
            }
            binding.SubstituteSource(
                supply,
                replacement,
                request.WithdrawnAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return new FairReturnWaitlistResult(
                FairReturnOutcome.SourceSubstituted,
                replacement,
                null,
                null,
                binding);
        }, cancellationToken);

    public Task<FairReturnWaitlistResult> SubstituteAsync(
        FairReturnSubstitutionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            FairReturnSourceBinding? snapshot =
                await dbContext.FairReturnSourceBindings
                    .AsNoTracking()
                    .SingleOrDefaultAsync(value =>
                        value.TenantId ==
                            request.TenantId
                        && value.EventId ==
                            request.EventId
                        && value.Id ==
                            request
                                .FairReturnSourceBindingId,
                        cancellationToken);
            if (snapshot is null)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }
            FairReturnSupplyUnit? current =
                await LockSupplyAsync(
                    request.TenantId,
                    request.EventId,
                    snapshot.FairReturnSupplyUnitId,
                    cancellationToken);
            FairReturnSupplyUnit? replacement =
                await LockSupplyAsync(
                    request.TenantId,
                    request.EventId,
                    request.ReplacementSupplyUnitId,
                    cancellationToken);
            FairReturnSourceBinding? binding =
                await LockBindingAsync(
                    request.TenantId,
                    request.EventId,
                    request.FairReturnSourceBindingId,
                    cancellationToken);
            if (current is null
                || replacement is null
                || binding is null
                || binding.FairReturnSupplyUnitId !=
                    current.Id
                || !current.IsCommerciallyEquivalentTo(
                    replacement))
            {
                return new FairReturnWaitlistResult(
                    FairReturnOutcome
                        .NoCommerciallyEquivalentSupply,
                    current,
                    null,
                    null,
                    binding);
            }
            try
            {
                binding.SubstituteSource(
                    current,
                    replacement,
                    request.SubstitutedAtUtc);
            }
            catch (InvalidOperationException)
            {
                return new FairReturnWaitlistResult(
                    binding.PaymentDispatchClaimedAt
                        .HasValue
                        ? FairReturnOutcome
                            .PaymentHandoffWon
                        : FairReturnOutcome
                            .PrivateConflict,
                    current,
                    null,
                    null,
                    binding);
            }
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return new FairReturnWaitlistResult(
                FairReturnOutcome.SourceSubstituted,
                replacement,
                null,
                null,
                binding);
        }, cancellationToken);

    public Task<FairReturnWaitlistResult> ExpireOfferAsync(
        WaitlistOfferExpiryRequest request,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            EventWaitlistOffer? identity =
                await FindOfferAsync(
                    request.TenantId,
                    request.EventId,
                    request.EventWaitlistOfferId,
                    cancellationToken);
            if (identity is null)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }
            FairReturnSupplyUnit supply =
                await LockRequiredSupplyAsync(
                    request.TenantId,
                    identity.FairReturnSupplyUnitId,
                    cancellationToken);
            EventWaitlistEntry entry =
                await LockEntryAsync(
                    request.TenantId,
                    identity.EventWaitlistEntryId,
                    cancellationToken);
            EventWaitlistOffer? offer =
                await LockOfferAsync(
                    request.TenantId,
                    request.EventId,
                    request.EventWaitlistOfferId,
                    cancellationToken);
            if (offer is null)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }
            bool expired = offer.Expire(
                entry,
                supply,
                request.ExpiredAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return new FairReturnWaitlistResult(
                expired
                    ? FairReturnOutcome.OfferExpired
                    : FairReturnOutcome.AlreadyApplied,
                supply,
                entry,
                offer,
                null);
        }, cancellationToken);

    public Task<FairReturnWaitlistResult>
        FinalizeReplacementAsync(
            WaitlistReplacementFinalizeRequest request,
            CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            EventWaitlistOffer? identity =
                await FindOfferAsync(
                    request.TenantId,
                    request.EventId,
                    request.EventWaitlistOfferId,
                    cancellationToken);
            if (identity is null)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }
            FairReturnSupplyUnit supply =
                await LockRequiredSupplyAsync(
                    request.TenantId,
                    identity.FairReturnSupplyUnitId,
                    cancellationToken);
            EventWaitlistEntry entry =
                await LockEntryAsync(
                    request.TenantId,
                    identity.EventWaitlistEntryId,
                    cancellationToken);
            EventWaitlistOffer? offer =
                await LockOfferAsync(
                    request.TenantId,
                    request.EventId,
                    request.EventWaitlistOfferId,
                    cancellationToken);
            if (offer is null)
            {
                return Unavailable(
                    FairReturnOutcome.PrivateConflict);
            }
            if (offer.StatusId !=
                (int)EventWaitlistOfferStatus.Active)
            {
                return new FairReturnWaitlistResult(
                    FairReturnOutcome.AlreadyApplied,
                    supply,
                    entry,
                    offer,
                    null);
            }
            if (request.FinalizedAtUtc >=
                offer.ExpiresAt)
            {
                offer.Expire(
                    entry,
                    supply,
                    request.FinalizedAtUtc);
                await dbContext.SaveChangesAsync(
                    cancellationToken);
                return new FairReturnWaitlistResult(
                    FairReturnOutcome.OfferExpired,
                    supply,
                    entry,
                    offer,
                    null);
            }
            FairReturnSourceBinding binding =
                await LockRequiredBindingAsync(
                    request.TenantId,
                    identity.FairReturnSourceBindingId,
                    cancellationToken);
            bool finalized = offer.Finalize(
                entry,
                request.FinalizedAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return new FairReturnWaitlistResult(
                finalized
                    ? FairReturnOutcome
                        .ReplacementFinalized
                    : FairReturnOutcome.AlreadyApplied,
                supply,
                entry,
                offer,
                binding);
        }, cancellationToken);

    private async Task<T> ExecuteFencedAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction
            is not null)
        {
            return await operation();
        }
        return await new EfCoreUnitOfWork(dbContext)
            .ExecuteInTransactionAsync(
                _ => operation(),
                cancellationToken);
    }

    private async Task<FairReturnSupplyPolicy?>
        LockPolicyAsync(
            Guid tenantId,
            Guid eventId,
            Guid id,
            CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence
            .AcquireAsync<FairReturnSupplyPolicy>(
                dbContext,
                tenantId,
                policy => policy.Id,
                id,
                cancellationToken);
        return await dbContext.FairReturnSupplyPolicies
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == id,
                cancellationToken);
    }

    private async Task<FairReturnSupplyUnit?>
        LockAvailableSupplyAsync(
            Guid tenantId,
            Guid eventId,
            FairReturnSupplyPolicy policy,
            EventWaitlistEntry entry,
            CancellationToken cancellationToken)
    {
        Guid? supplyId =
            await dbContext.FairReturnSupplyUnits
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.EventTicketTypeId ==
                        policy.EventTicketTypeId
                    && value.TicketCatalogVersionId ==
                        policy.TicketCatalogVersionId
                    && value.PurchasePolicySnapshotId ==
                        entry.PurchasePolicySnapshotId
                    && value.CurrencyCode ==
                        entry.CurrencyCode
                    && value.CommercialTermsDigest ==
                        entry.CommercialTermsDigest
                    && value.AdmissionEntitlementDigest ==
                        entry.AdmissionEntitlementDigest
                    && value.GrossMinorUnits ==
                        entry.GrossMinorUnits
                    && value.RefundFundingModeId ==
                        entry.RefundFundingModeId
                    && value.StatusId ==
                        (int)FairReturnSupplyStatus
                            .Available)
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Select(value => (Guid?)value.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (!supplyId.HasValue)
        {
            return null;
        }
        await RelationalEntityRowFence
            .AcquireAsync<FairReturnSupplyUnit>(
                dbContext,
                tenantId,
                supply => supply.Id,
                supplyId.Value,
                cancellationToken);
        return await dbContext.FairReturnSupplyUnits
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId
                && value.EventId == eventId
                && value.Id == supplyId.Value
                && value.StatusId ==
                    (int)FairReturnSupplyStatus.Available,
                cancellationToken);
    }

    private Task<EventWaitlistEntry?>
        FindNextEntryAsync(
            Guid tenantId,
            Guid eventId,
            FairReturnSupplyPolicy policy,
            CancellationToken cancellationToken)
        =>
        dbContext.EventWaitlistEntries
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId
                && value.EventId == eventId
                && value.EventTicketTypeId ==
                    policy.EventTicketTypeId
                && value.TicketCatalogVersionId ==
                    policy.TicketCatalogVersionId
                && value.StatusId ==
                    (int)EventWaitlistEntryStatus.Queued)
            .OrderByDescending(value =>
                value.Priority)
            .ThenBy(value => value.EnqueuedAt)
            .ThenBy(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<EventWaitlistEntry?>
        LockQueuedEntryAsync(
            Guid tenantId,
            Guid eventId,
            Guid entryId,
            CancellationToken cancellationToken)
    {
        await FenceAsync<EventWaitlistEntry>(
            tenantId,
            entryId,
            entry => entry.Id,
            cancellationToken);
        return await dbContext.EventWaitlistEntries
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId
                && value.EventId == eventId
                && value.Id == entryId
                && value.StatusId ==
                    (int)EventWaitlistEntryStatus.Queued,
                cancellationToken);
    }

    private async Task<FairReturnSupplyUnit?> LockSupplyAsync(
        Guid tenantId,
        Guid eventId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence
            .AcquireAsync<FairReturnSupplyUnit>(
                dbContext,
                tenantId,
                supply => supply.Id,
                id,
                cancellationToken);
        return await dbContext.FairReturnSupplyUnits
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == id,
                cancellationToken);
    }

    private async Task<FairReturnSourceBinding?>
        LockBindingBySupplyAsync(
            Guid tenantId,
            Guid supplyId,
            CancellationToken cancellationToken)
    {
        Guid? bindingId =
            await dbContext.FairReturnSourceBindings
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.FairReturnSupplyUnitId ==
                        supplyId)
                .Select(value => (Guid?)value.Id)
                .SingleOrDefaultAsync(cancellationToken);
        if (!bindingId.HasValue)
        {
            return null;
        }
        await RelationalEntityRowFence
            .AcquireAsync<FairReturnSourceBinding>(
                dbContext,
                tenantId,
                binding => binding.Id,
                bindingId.Value,
                cancellationToken);
        return await dbContext.FairReturnSourceBindings
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.Id == bindingId.Value
                    && value.FairReturnSupplyUnitId == supplyId,
                cancellationToken);
    }

    private Task<FairReturnSourceBinding?>
        FindBindingBySupplyAsync(
            Guid tenantId,
            Guid supplyId,
            CancellationToken cancellationToken) =>
        dbContext.FairReturnSourceBindings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.FairReturnSupplyUnitId ==
                        supplyId,
                cancellationToken);

    private async Task<FairReturnSourceBinding?>
        LockBindingAsync(
            Guid tenantId,
            Guid eventId,
            Guid id,
            CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence
            .AcquireAsync<FairReturnSourceBinding>(
                dbContext,
                tenantId,
                binding => binding.Id,
                id,
                cancellationToken);
        return await dbContext.FairReturnSourceBindings
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == id,
                cancellationToken);
    }

    private async Task<FairReturnSupplyUnit?>
        LockEquivalentSupplyAsync(
            Guid tenantId,
            Guid eventId,
            FairReturnSupplyUnit source,
            CancellationToken cancellationToken)
    {
        Guid[] candidateIds =
            await dbContext.FairReturnSupplyUnits
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id != source.Id
                    && value.StatusId ==
                        (int)FairReturnSupplyStatus
                            .Available)
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Select(value => value.Id)
                .ToArrayAsync(cancellationToken);
        foreach (Guid candidateId in candidateIds)
        {
            await RelationalEntityRowFence
                .AcquireAsync<FairReturnSupplyUnit>(
                    dbContext,
                    tenantId,
                    supply => supply.Id,
                    candidateId,
                    cancellationToken);
            FairReturnSupplyUnit? candidate =
                await dbContext.FairReturnSupplyUnits
                    .SingleOrDefaultAsync(value =>
                        value.TenantId == tenantId
                        && value.EventId == eventId
                        && value.Id == candidateId
                        && value.StatusId ==
                            (int)FairReturnSupplyStatus
                                .Available,
                        cancellationToken);
            if (candidate is not null
                && source.IsCommerciallyEquivalentTo(
                    candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private async Task<EventWaitlistOffer?> LockOfferAsync(
        Guid tenantId,
        Guid eventId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence
            .AcquireAsync<EventWaitlistOffer>(
                dbContext,
                tenantId,
                offer => offer.Id,
                id,
                cancellationToken);
        return await dbContext.EventWaitlistOffers
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == id,
                cancellationToken);
    }

    private Task<EventWaitlistOffer?> FindOfferAsync(
        Guid tenantId,
        Guid eventId,
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.EventWaitlistOffers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.EventId == eventId
                    && value.Id == id,
                cancellationToken);

    private async Task<EventWaitlistEntry> LockEntryAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await FenceAsync<EventWaitlistEntry>(
            tenantId,
            id,
            entry => entry.Id,
            cancellationToken);
        return await dbContext.EventWaitlistEntries
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.Id == id,
                cancellationToken)
        ?? throw new InvalidOperationException(
            "Waitlist entry fence is unavailable.");
    }

    private async Task<FairReturnSupplyUnit>
        LockRequiredSupplyAsync(
            Guid tenantId,
            Guid id,
            CancellationToken cancellationToken)
    {
        await FenceAsync<FairReturnSupplyUnit>(
            tenantId,
            id,
            supply => supply.Id,
            cancellationToken);
        return await dbContext.FairReturnSupplyUnits
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.Id == id,
                cancellationToken)
        ?? throw new InvalidOperationException(
            "Supply fence is unavailable.");
    }

    private async Task<FairReturnSourceBinding>
        LockRequiredBindingAsync(
            Guid tenantId,
            Guid id,
            CancellationToken cancellationToken)
    {
        await FenceAsync<FairReturnSourceBinding>(
            tenantId,
            id,
            binding => binding.Id,
            cancellationToken);
        return await dbContext.FairReturnSourceBindings
            .SingleOrDefaultAsync(
                value =>
                    value.TenantId == tenantId
                    && value.Id == id,
                cancellationToken)
        ?? throw new InvalidOperationException(
            "Source binding fence is unavailable.");
    }

    private Task FenceAsync<TEntity>(
        Guid tenantId,
        Guid id,
        Expression<Func<TEntity, Guid>> keyPropertyExpression,
        CancellationToken cancellationToken)
        where TEntity : class, ITenantEntity =>
        RelationalEntityRowFence.AcquireAsync<TEntity>(
            dbContext,
            tenantId,
            keyPropertyExpression,
            id,
            cancellationToken);

    private static FairReturnWaitlistResult Unavailable(
        FairReturnOutcome outcome) =>
        new(outcome, null, null, null, null);
}
