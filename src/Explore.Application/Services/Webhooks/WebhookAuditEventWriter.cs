using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;

namespace Explore.Application.Services.Webhooks;

public sealed class WebhookAuditEventWriter(
    IWebhookAuditEventRepository repository,
    ICurrentUserService currentUserService,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    IWebhookRetentionPolicyResolver retentionPolicyResolver,
    TimeProvider timeProvider) : IWebhookAuditEventWriter
{
    public async Task<WebhookAuditEvent> AppendAsync(
        WebhookAuditWriteRequest request,
        CancellationToken cancellationToken)
    {
        var (principalKind, principalReference) = ResolvePrincipal(request);
        var now = timeProvider.GetUtcNow();
        var retention = retentionPolicyResolver.Resolve(now, now);
        var auditEvent = WebhookAuditEvent.Create(
            request.TenantId,
            principalKind,
            principalReference,
            request.EffectiveScopeKind,
            request.EffectiveScopeKind == WebhookAuditScopeKind.Tenant
                ? request.EffectiveScopeId ?? request.TenantId
                : request.EffectiveScopeId,
            request.Action,
            request.TargetKind,
            request.TargetId,
            request.SafeBeforeJson,
            request.SafeAfterJson,
            request.ConfigurationVersion,
            request.CorrelationId,
            request.ReasonCode,
            request.Outcome,
            retention.PolicyVersion,
            retention.AdministrativeAuditRetentionUntil.UtcDateTime);
        return await repository.AppendAsync(auditEvent, cancellationToken);
    }

    private (WebhookAuditPrincipalKind Kind, string Reference) ResolvePrincipal(
        WebhookAuditWriteRequest request)
    {
        if (request.PrincipalKind is { } explicitKind)
        {
            if (string.IsNullOrWhiteSpace(request.PrincipalReference))
            {
                throw new InvalidOperationException("An explicit webhook audit principal requires a reference.");
            }

            return (explicitKind, request.PrincipalReference);
        }

        if (currentUserService.UserId is { } userId)
        {
            return (WebhookAuditPrincipalKind.User, $"user:{userId:D}");
        }

        if (machinePrincipalAccessor.Current is { } machine)
        {
            return (
                WebhookAuditPrincipalKind.Machine,
                $"machine:{machine.OwnerType}:{machine.OwnerId:D}");
        }

        throw new InvalidOperationException("A user, machine, or explicit system principal is required for webhook audit.");
    }
}
