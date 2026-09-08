using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebhookAuditEventRepository
{
    Task<WebhookAuditEvent> AppendAsync(
        WebhookAuditEvent auditEvent,
        CancellationToken cancellationToken);
}
