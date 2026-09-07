using Explore.Domain;

namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookAuditEventWriter
{
    Task<WebhookAuditEvent> AppendAsync(
        WebhookAuditWriteRequest request,
        CancellationToken cancellationToken);
}

public sealed record WebhookAuditWriteRequest(
    Guid? TenantId,
    WebhookAuditAction Action,
    WebhookAuditTargetKind TargetKind,
    Guid TargetId,
    string ReasonCode,
    WebhookAuditOutcome Outcome,
    string? SafeBeforeJson = null,
    string? SafeAfterJson = null,
    string? ConfigurationVersion = null,
    string? CorrelationId = null,
    WebhookAuditScopeKind EffectiveScopeKind = WebhookAuditScopeKind.Tenant,
    Guid? EffectiveScopeId = null,
    WebhookAuditPrincipalKind? PrincipalKind = null,
    string? PrincipalReference = null);
