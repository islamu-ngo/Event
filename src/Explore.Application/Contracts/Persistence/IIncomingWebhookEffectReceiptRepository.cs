using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IIncomingWebhookEffectReceiptRepository
{
    Task<IncomingWebhookEffectReceipt?> GetByIdentityAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        string effectKind,
        CancellationToken cancellationToken);

    Task AddAsync(IncomingWebhookEffectReceipt receipt, CancellationToken cancellationToken);
}
