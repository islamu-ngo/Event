using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationMaterialChangeChoiceRepository(ExploreDbContext dbContext)
    : IRegistrationMaterialChangeChoiceRepository
{
    public async Task<IReadOnlyList<RegistrationMaterialChangeChoice>> GetByPaymentAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationMaterialChangeChoices
            .AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.PaymentAttemptId == paymentAttemptId &&
                            value.Status != Explore.Domain.Enums.MaterialChangeChoiceStatusEnum.NotApplicable)
            .OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);

    public Task<RegistrationMaterialChangeChoice?> GetAsync(
        Guid tenantId,
        Guid campaignId,
        Guid registrationOrderId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationMaterialChangeChoices
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TenantId == tenantId &&
                                           value.RefundCampaignId == campaignId &&
                                           value.RegistrationOrderId == registrationOrderId,
                cancellationToken);

    public async Task<bool> AcceptAsync(
        Guid tenantId,
        Guid choiceId,
        Guid actorId,
        DateTime decidedAt,
        CancellationToken cancellationToken)
    {
        RegistrationMaterialChangeChoice? choice = await dbContext.RegistrationMaterialChangeChoices.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == choiceId,
            cancellationToken);
        if (choice is null)
        {
            return false;
        }

        try
        {
            bool changed = choice.AcceptNewTerms(actorId, decidedAt);
            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return true;
        }
        catch (InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
