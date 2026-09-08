using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class IncomingWebhookEffectReceiptRepository(ExploreDbContext dbContext)
    : IIncomingWebhookEffectReceiptRepository
{
    public Task<IncomingWebhookEffectReceipt?> GetByIdentityAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        string effectKind,
        CancellationToken cancellationToken)
    {
        var normalizedEffectKind = IncomingWebhookEffectReceipt.NormalizeEffectKind(effectKind);
        return dbContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(receipt =>
                receipt.TenantId == tenantId &&
                receipt.IncomingWebhookMessageId == incomingWebhookMessageId &&
                receipt.EffectKind == normalizedEffectKind,
                cancellationToken);
    }

    public async Task AddAsync(
        IncomingWebhookEffectReceipt receipt,
        CancellationToken cancellationToken)
    {
        await dbContext.IncomingWebhookEffectReceipts.AddAsync(receipt, cancellationToken);
    }
}
