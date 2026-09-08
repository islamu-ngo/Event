using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class GuestRegistrationCapabilityRepository(ExploreDbContext dbContext)
    : IGuestRegistrationCapabilityRepository
{
    public Task<RegistrationOrder?> GetExactGuestOrderAsync(
        Guid orderId,
        Guid tenantId,
        Guid eventId,
        CapabilityTokenHash guestAccessTokenHash,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Include(order => order.PlatformContribution)
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.TenantId == tenantId &&
                         order.EventId == eventId && order.GuestAccessTokenHash == guestAccessTokenHash,
                cancellationToken);

    public async Task<RegistrationOrder?> GetGuestStatusOrderForUpdateAsync(
        Guid orderId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await RelationalNamedLock.AcquireTransactionAsync(
                dbContext, $"registration-order:{tenantId:N}:{orderId:N}", cancellationToken);
        }
        await RelationalEntityRowFence.AcquireAsync<RegistrationOrder>(
            dbContext, tenantId, order => order.Id, orderId, cancellationToken);
        return await dbContext.RegistrationOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.TenantId == tenantId,
                cancellationToken);
    }

    public async Task<bool> TryExtendGuestStatusAccessAsync(
        RegistrationOrder expected, DateTime deadlineUtc, CancellationToken cancellationToken)
    {
        RegistrationInventoryTime.RequireUtc(deadlineUtc);
        int affected = await dbContext.RegistrationOrders
            .Where(order => order.Id == expected.Id && order.TenantId == expected.TenantId &&
                order.EventId == expected.EventId && order.ConcurrencyStamp == expected.ConcurrencyStamp &&
                order.GuestAccessTokenHash == expected.GuestAccessTokenHash &&
                order.RegistrationOrderStatusId == expected.RegistrationOrderStatusId &&
                order.GuestStatusAccessUntilUtc == expected.GuestStatusAccessUntilUtc &&
                order.GuestStatusAccessUntilUtc != null && order.GuestStatusAccessUntilUtc < deadlineUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(order => order.GuestStatusAccessUntilUtc, deadlineUtc)
                .SetProperty(order => order.ConcurrencyStamp, Guid.CreateVersion7()), cancellationToken);
        return affected == 1;
    }
}
