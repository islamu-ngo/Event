using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookRetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebhookRetentionSettings> settings,
    IWebhookRetentionPolicyResolver retentionPolicyResolver,
    BusinessMetrics metrics,
    ILogger<WebhookRetentionCleanupService> logger) : IWebhookRetentionCleanupService
{
    private readonly WebhookRetentionSettings _settings = settings.Value;
    private readonly Lock _tenantCursorLock = new();
    private int _nextTenantIndex;

    public async Task<WebhookRetentionCleanupRunResult> CleanupAllTenantsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Cleanup time must use UTC kind.", nameof(utcNow));
        }

        var mode = _settings.DryRun ? "dry_run" : "cleanup";
        try
        {
            await using var lookupScope = scopeFactory.CreateAsyncScope();
            var tenantLookupSource = lookupScope.ServiceProvider.GetRequiredService<ITenantLookupSource>();
            var allTenants = await tenantLookupSource.GetTenantLookupsAsync(cancellationToken);
            var selectedTenantIds = SelectTenantIds(allTenants.Select(tenant => tenant.TenantId).ToArray());
            var aggregate = EmptyResult(_settings.DryRun);
            var succeeded = 0;
            var failed = 0;

            foreach (var tenantId in selectedTenantIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await CleanupTenantAsync(tenantId, utcNow, cancellationToken);
                    aggregate = Add(aggregate, result);
                    succeeded++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failed++;
                    logger.LogWarning(
                        "Webhook retention cleanup failed for one tenant. FailureType={FailureType}",
                        exception.GetType().Name);
                }
            }

            var run = new WebhookRetentionCleanupRunResult(
                selectedTenantIds.Count,
                succeeded,
                failed,
                aggregate);
            RecordMetrics(mode, failed == 0 ? "succeeded" : "partial_failure", aggregate);
            logger.LogInformation(
                "Webhook retention cleanup completed. Tenants={TenantCount}, Succeeded={SucceededTenantCount}, Failed={FailedTenantCount}, Affected={AffectedCount}, Mode={Mode}.",
                run.TenantCount,
                run.SucceededTenantCount,
                run.FailedTenantCount,
                aggregate.TotalAffected,
                mode);
            return run;
        }
        catch (Exception)
        {
            metrics.RecordWebhookRetentionCleanupRun(mode, "failed");
            throw;
        }
    }

    private async Task<WebhookRetentionCleanupResult> CleanupTenantAsync(
        Guid tenantId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        tenantAccessor.SetTenant(tenantId);
        try
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWebhookRetentionCleanupRepository>();
            var auditWriter = scope.ServiceProvider.GetRequiredService<IWebhookAuditEventWriter>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var cleanupRunId = Guid.CreateVersion7();
            var policy = retentionPolicyResolver.Resolve(
                new DateTimeOffset(utcNow),
                new DateTimeOffset(utcNow));

            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var result = await repository.CleanupTenantAsync(
                    tenantId,
                    utcNow,
                    _settings.BatchSize,
                    _settings.DryRun,
                    token);
                if (!result.DryRun && result.TotalAffected > 0)
                {
                    await auditWriter.AppendAsync(
                        new WebhookAuditWriteRequest(
                            tenantId,
                            WebhookAuditAction.RetentionCleanupCompleted,
                            WebhookAuditTargetKind.CleanupRun,
                            cleanupRunId,
                            "scheduled_retention_cleanup",
                            WebhookAuditOutcome.Succeeded,
                            SafeAfterJson: JsonSerializer.Serialize(new
                            {
                                result.OutboundPayloadsCleared,
                                result.InboundPayloadsCleared,
                                result.DeliveryAttemptsDeleted,
                                result.IncomingAttemptsDeleted,
                                result.IncomingRedriveRecordsDeleted,
                                result.ProviderAttemptsDeleted,
                                result.ProviderPublicationsDeleted,
                                result.AdministrativeAuditsDeleted,
                                result.TotalAffected
                            }),
                            ConfigurationVersion: policy.PolicyVersion,
                            PrincipalKind: WebhookAuditPrincipalKind.System,
                            PrincipalReference: "system:webhook-retention-cleanup"),
                        token);
                }

                return result;
            }, cancellationToken);
        }
        finally
        {
            tenantAccessor.Clear();
        }
    }

    private IReadOnlyList<Guid> SelectTenantIds(IReadOnlyList<Guid> tenantIds)
    {
        if (tenantIds.Count == 0)
        {
            return [];
        }

        var ordered = tenantIds.Distinct().Order().ToArray();
        var count = Math.Min(ordered.Length, _settings.MaxTenantsPerPass);
        int start;
        lock (_tenantCursorLock)
        {
            start = _nextTenantIndex % ordered.Length;
            _nextTenantIndex = (start + count) % ordered.Length;
        }

        return Enumerable.Range(0, count)
            .Select(offset => ordered[(start + offset) % ordered.Length])
            .ToArray();
    }

    private void RecordMetrics(
        string mode,
        string outcome,
        WebhookRetentionCleanupResult result)
    {
        metrics.RecordWebhookRetentionCleanupRun(mode, outcome);
        metrics.RecordWebhookRetentionCleanupItems(result.OutboundPayloadsCleared, mode, "outbound_payload");
        metrics.RecordWebhookRetentionCleanupItems(result.InboundPayloadsCleared, mode, "inbound_payload");
        metrics.RecordWebhookRetentionCleanupItems(result.DeliveryAttemptsDeleted, mode, "delivery_attempt");
        metrics.RecordWebhookRetentionCleanupItems(result.IncomingAttemptsDeleted, mode, "incoming_attempt");
        metrics.RecordWebhookRetentionCleanupItems(result.IncomingRedriveRecordsDeleted, mode, "incoming_redrive");
        metrics.RecordWebhookRetentionCleanupItems(result.ProviderAttemptsDeleted, mode, "provider_attempt");
        metrics.RecordWebhookRetentionCleanupItems(result.ProviderPublicationsDeleted, mode, "provider_publication");
        metrics.RecordWebhookRetentionCleanupItems(result.AdministrativeAuditsDeleted, mode, "administrative_audit");
    }

    private static WebhookRetentionCleanupResult EmptyResult(bool dryRun) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, dryRun);

    private static WebhookRetentionCleanupResult Add(
        WebhookRetentionCleanupResult left,
        WebhookRetentionCleanupResult right) =>
        new(
            left.OutboundPayloadsCleared + right.OutboundPayloadsCleared,
            left.InboundPayloadsCleared + right.InboundPayloadsCleared,
            left.DeliveryAttemptsDeleted + right.DeliveryAttemptsDeleted,
            left.IncomingAttemptsDeleted + right.IncomingAttemptsDeleted,
            left.IncomingRedriveRecordsDeleted + right.IncomingRedriveRecordsDeleted,
            left.ProviderAttemptsDeleted + right.ProviderAttemptsDeleted,
            left.ProviderPublicationsDeleted + right.ProviderPublicationsDeleted,
            left.AdministrativeAuditsDeleted + right.AdministrativeAuditsDeleted,
            left.DryRun && right.DryRun);
}
