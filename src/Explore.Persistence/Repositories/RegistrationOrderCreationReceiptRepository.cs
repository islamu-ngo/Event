using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationOrderCreationReceiptRepository(
    ExploreDbContext dbContext, ITenantExploreDbContextFactory tenantContexts)
    : IRegistrationOrderCreationReceiptRepository
{
    public Task<RegistrationOrderCreationReceipt?> FindAsync(
        Guid tenantId, string operationFingerprint, CancellationToken cancellationToken)
    {
        RequireTenant(dbContext, tenantId);
        return FindAsync(dbContext, tenantId, operationFingerprint, cancellationToken);
    }

    public async Task<RegistrationOrderCreationReceipt?> FindFreshAsync(
        Guid tenantId, string operationFingerprint, CancellationToken cancellationToken)
    {
        RequireTenant(dbContext, tenantId);
        await using var fresh = await tenantContexts.CreateDbContextAsync(tenantId, cancellationToken);
        RequireTenant(fresh, tenantId);
        return await FindAsync(fresh, tenantId, operationFingerprint, cancellationToken);
    }

    public async Task<bool> FenceCurrentAccountAsync(
        Guid tenantId, Guid accountUserId, CancellationToken cancellationToken)
    {
        RequireTransaction(tenantId);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(dbContext, tenantId, cancellationToken);
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(dbContext, accountUserId, cancellationToken);
        return await HasCurrentAccountAsync(dbContext, tenantId, accountUserId, cancellationToken);
    }

    public async Task<RegistrationOrder?> GetRecoverableOrderAsync(
        Guid tenantId, Guid orderId, Guid accountUserId, DateTime utcNow,
        bool fresh, CancellationToken cancellationToken)
    {
        RequireTenant(dbContext, tenantId);
        if (!fresh)
            return await ReadOrderAsync(dbContext, tenantId, orderId, accountUserId, utcNow, cancellationToken);
        await using var context = await tenantContexts.CreateDbContextAsync(tenantId, cancellationToken);
        RequireTenant(context, tenantId);
        return await ReadOrderAsync(context, tenantId, orderId, accountUserId, utcNow, cancellationToken);
    }

    public Task AddAsync(RegistrationOrderCreationReceipt receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireTransaction(receipt.TenantId);
        if (!dbContext.ChangeTracker.Entries<RegistrationOrder>().Any(entry =>
            entry.State == EntityState.Added && entry.Entity.Id == receipt.RegistrationOrderId
            && entry.Entity.TenantId == receipt.TenantId && entry.Entity.AccountUserId != null
            && entry.Entity.GuestAccessTokenHash == null))
            throw new InvalidOperationException("Creation evidence requires its new authenticated order in the same transaction.");
        dbContext.Set<RegistrationOrderCreationReceipt>().Add(receipt);
        return Task.CompletedTask;
    }

    private static Task<RegistrationOrderCreationReceipt?> FindAsync(
        ExploreDbContext context, Guid tenantId, string fingerprint, CancellationToken cancellationToken) =>
        context.Set<RegistrationOrderCreationReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(receipt => receipt.TenantId == tenantId
                && receipt.OperationFingerprint == fingerprint, cancellationToken);

    private static async Task<RegistrationOrder?> ReadOrderAsync(
        ExploreDbContext context, Guid tenantId, Guid orderId, Guid accountUserId,
        DateTime utcNow, CancellationToken cancellationToken)
    {
        if (!await HasCurrentAccountAsync(context, tenantId, accountUserId, cancellationToken))
            return null;
        return await context.RegistrationOrders.AsNoTracking().SingleOrDefaultAsync(order =>
            order.Id == orderId && order.TenantId == tenantId
            && order.AccountUserId == accountUserId && order.GuestAccessTokenHash == null
            && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Expired
            && (order.ExpiresAt == null || order.ExpiresAt > utcNow)
            && context.Events.Any(parent => parent.Id == order.EventId && parent.TenantId == tenantId)
            && context.EventTicketCatalogVersions.Any(catalog => catalog.Id == order.TicketCatalogVersionId
                && catalog.EventId == order.EventId && catalog.TenantId == tenantId), cancellationToken);
    }

    private static async Task<bool> HasCurrentAccountAsync(
        ExploreDbContext context, Guid tenantId, Guid accountUserId, CancellationToken cancellationToken) =>
        await context.Tenants.AsNoTracking().AnyAsync(tenant => tenant.Id == tenantId
            && tenant.TenantStatusId == (int)TenantStatusEnum.Active, cancellationToken)
        && await context.Users.AsNoTracking().AnyAsync(user => user.Id == accountUserId, cancellationToken);

    private void RequireTransaction(Guid tenantId)
    {
        RequireTenant(dbContext, tenantId);
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Creation evidence requires the starter's serializable transaction.");
    }

    private static void RequireTenant(ExploreDbContext context, Guid tenantId)
    {
        if (tenantId == Guid.Empty || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId)
            throw new InvalidOperationException("Creation recovery requires the exact ordinary tenant lease.");
    }
}
