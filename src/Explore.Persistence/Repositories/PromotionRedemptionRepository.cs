using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PromotionRedemptionRepository(ExploreDbContext dbContext) : IPromotionRedemptionRepository
{
    private static readonly int[] ActiveOrConsumedStatuses = [(int)PromotionReservationStatusEnum.Active, (int)PromotionReservationStatusEnum.Consumed];

    public async Task<IReadOnlyList<int>> GetDistinctLookupKeyVersionsAsync(Guid tenantId, Guid eventId, Guid ticketCatalogVersionId, CancellationToken cancellationToken) =>
        await dbContext.PromotionCodes.AsNoTracking()
            .Where(code => code.TenantId == tenantId && EF.Property<Guid>(code, "ScopeEventId") == eventId && EF.Property<Guid>(code, "ScopeTicketCatalogVersionId") == ticketCatalogVersionId && EF.Property<bool>(code, "IsActive"))
            .Select(code => EF.Property<int>(code, "LookupKeyVersion"))
            .Distinct()
            .OrderBy(version => version)
            .ToListAsync(cancellationToken);

    public async Task<PromotionCodeMatch?> GetCodeForUpdateAsync(Guid tenantId, Guid eventId, Guid ticketCatalogVersionId, IReadOnlyCollection<PromotionCodeDigest> candidateDigests, CancellationToken cancellationToken)
    {
        if (candidateDigests.Count == 0)
        {
            return null;
        }

        int[] keyVersions = candidateDigests.Select(digest => digest.KeyVersion).Distinct().ToArray();
        string[] digestValues = candidateDigests.Select(digest => digest.Value).Distinct(StringComparer.Ordinal).ToArray();
        var possibleCodes = await dbContext.PromotionCodes.AsNoTracking()
            .Where(code => code.TenantId == tenantId && EF.Property<Guid>(code, "ScopeEventId") == eventId && EF.Property<Guid>(code, "ScopeTicketCatalogVersionId") == ticketCatalogVersionId && EF.Property<bool>(code, "IsActive"))
            .Where(code => keyVersions.Contains(EF.Property<int>(code, "LookupKeyVersion")) && digestValues.Contains(EF.Property<string>(code, "LookupDigest")))
            .Select(code => new
            {
                code.Id,
                code.PromotionDefinitionVersionId,
                LookupKeyVersion = EF.Property<int>(code, "LookupKeyVersion"),
                LookupDigest = EF.Property<string>(code, "LookupDigest")
            })
            .ToArrayAsync(cancellationToken);
        var matchedCode = possibleCodes.FirstOrDefault(code => candidateDigests.Any(digest =>
            digest.KeyVersion == code.LookupKeyVersion &&
            digest.Value == code.LookupDigest));
        if (matchedCode is null)
        {
            return null;
        }

        await AcquirePromotionLocksIfTransactionalAsync(tenantId, matchedCode.PromotionDefinitionVersionId, matchedCode.Id, cancellationToken);
        PromotionCode? code = await dbContext.PromotionCodes.AsNoTracking().FirstOrDefaultAsync(
            item => item.TenantId == tenantId
                && item.Id == matchedCode.Id
                && item.PromotionDefinitionVersionId == matchedCode.PromotionDefinitionVersionId
                && EF.Property<Guid>(item, "ScopeEventId") == eventId
                && EF.Property<Guid>(item, "ScopeTicketCatalogVersionId") == ticketCatalogVersionId
                && EF.Property<bool>(item, "IsActive")
                && EF.Property<int>(item, "LookupKeyVersion") == matchedCode.LookupKeyVersion
                && EF.Property<string>(item, "LookupDigest") == matchedCode.LookupDigest,
            cancellationToken);
        if (code is null || !candidateDigests.Any(digest => digest.KeyVersion == matchedCode.LookupKeyVersion && digest.Value == matchedCode.LookupDigest))
        {
            return null;
        }

        PromotionDefinition? definition = await dbContext.PromotionDefinitions.AsNoTracking().FirstOrDefaultAsync(
            item => item.TenantId == tenantId
                && item.Id == code.PromotionDefinitionVersionId
                && EF.Property<Guid>(item, "ScopeEventId") == eventId
                && EF.Property<Guid>(item, "ScopeTicketCatalogVersionId") == ticketCatalogVersionId,
            cancellationToken);
        return definition is null ? null : new PromotionCodeMatch(code, definition);
    }

    public async Task<PromotionReservation?> GetActiveReservationForUpdateAsync(Guid tenantId, Guid registrationOrderId, CancellationToken cancellationToken)
    {
        var active = await dbContext.PromotionReservations.AsNoTracking()
            .Where(reservation => reservation.TenantId == tenantId
                && reservation.RegistrationOrderId == registrationOrderId
                && reservation.PromotionReservationStatusId == (int)PromotionReservationStatusEnum.Active)
            .Select(reservation => new
            {
                reservation.Id,
                reservation.PromotionDefinitionVersionId,
                reservation.PromotionCodeId
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (active is null)
        {
            return null;
        }

        await AcquirePromotionLocksIfTransactionalAsync(
            tenantId,
            active.PromotionDefinitionVersionId,
            active.PromotionCodeId,
            cancellationToken);
        return await dbContext.PromotionReservations.FirstOrDefaultAsync(
            reservation => reservation.TenantId == tenantId
                && reservation.Id == active.Id
                && reservation.RegistrationOrderId == registrationOrderId
                && reservation.PromotionReservationStatusId == (int)PromotionReservationStatusEnum.Active,
            cancellationToken);
    }

    public async Task<int> GetTotalActiveOrConsumedCountAsync(Guid tenantId, Guid promotionDefinitionVersionId, CancellationToken cancellationToken) =>
        await dbContext.PromotionReservations.CountAsync(
            reservation => reservation.TenantId == tenantId && reservation.PromotionDefinitionVersionId == promotionDefinitionVersionId && ActiveOrConsumedStatuses.Contains(reservation.PromotionReservationStatusId),
            cancellationToken);

    public async Task<int> GetVerifiedPurchaserActiveOrConsumedCountAsync(Guid tenantId, Guid promotionDefinitionVersionId, VerifiedPurchaserIdentity purchaser, CancellationToken cancellationToken)
    {
        var query = from reservation in dbContext.PromotionReservations
                    join order in dbContext.RegistrationOrders on new { reservation.TenantId, reservation.RegistrationOrderId } equals new { order.TenantId, RegistrationOrderId = order.Id }
                    join pii in dbContext.RegistrationOrderPii on new { reservation.TenantId, reservation.RegistrationOrderId } equals new { pii.TenantId, pii.RegistrationOrderId } into piiRows
                    from pii in piiRows.DefaultIfEmpty()
                    where reservation.TenantId == tenantId
                          && reservation.PromotionDefinitionVersionId == promotionDefinitionVersionId
                          && ActiveOrConsumedStatuses.Contains(reservation.PromotionReservationStatusId)
                    select new { order, pii };

        return purchaser.Kind switch
        {
            nameof(VerifiedPurchaserIdentity.Account) => await query.CountAsync(value => value.order.AccountUserId == Guid.Parse(purchaser.Value), cancellationToken),
            nameof(VerifiedPurchaserIdentity.Email) => await query.CountAsync(value => value.order.AccountUserId == null && value.pii != null && value.pii.IsEmailVerified && value.pii.NormalizedEmail == purchaser.Value, cancellationToken),
            nameof(VerifiedPurchaserIdentity.Actor) => await query.CountAsync(value => value.order.AccountUserId == null && (value.pii == null || !value.pii.IsEmailVerified) && value.order.PurchaserActorId == Guid.Parse(purchaser.Value), cancellationToken),
            _ => 0
        };
    }

    public async Task AddReservationAsync(PromotionReservation reservation, CancellationToken cancellationToken)
    {
        await AcquirePromotionLocksIfTransactionalAsync(reservation.TenantId, reservation.PromotionDefinitionVersionId, reservation.PromotionCodeId, cancellationToken);
        await dbContext.PromotionReservations.AddAsync(reservation, cancellationToken);
    }

    private async Task AcquirePromotionLocksIfTransactionalAsync(Guid tenantId, Guid definitionId, Guid codeId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            return;
        }

        await RelationalNamedLock.AcquireTransactionAsync(dbContext, $"promotion:{tenantId:N}:{definitionId:N}", cancellationToken);
        await RelationalNamedLock.AcquireTransactionAsync(dbContext, $"promotion-code:{tenantId:N}:{codeId:N}", cancellationToken);
    }
}
