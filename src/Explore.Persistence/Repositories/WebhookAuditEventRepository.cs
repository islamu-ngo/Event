using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public sealed class WebhookAuditEventRepository(ExploreDbContext dbContext) : IWebhookAuditEventRepository
{
    public async Task<WebhookAuditEvent> AppendAsync(
        WebhookAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        dbContext.WebhookAuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return auditEvent;
    }
}
