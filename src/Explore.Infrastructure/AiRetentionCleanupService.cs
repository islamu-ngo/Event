using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class AiRetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<AiRetentionCleanupSettings> settings,
    BusinessMetrics metrics,
    ILogger<AiRetentionCleanupService> logger) : IAiRetentionCleanupService
{
    private readonly AiRetentionCleanupSettings _settings = settings.Value;

    public async Task<AiRetentionCleanupRunResult> CleanupAllTenantsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var mode = _settings.DryRun ? "dry_run" : "redact";

        try
        {
            using var lookupScope = scopeFactory.CreateScope();
            var tenantLookupSource = lookupScope.ServiceProvider.GetRequiredService<ITenantLookupSource>();
            var tenants = await tenantLookupSource.GetTenantLookupsAsync(cancellationToken);
            var boundedTenants = tenants.Take(_settings.MaxTenantsPerPass).ToList();

            var aggregate = new AiRetentionCleanupAggregate(utcNow, _settings.DryRun);

            foreach (var tenant in boundedTenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await CleanupTenantAsync(tenant.TenantId, utcNow, cancellationToken);
                    aggregate.AddSuccess(result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    aggregate.AddFailure();
                    logger.LogWarning(exception, "AI retention cleanup failed for one tenant during aggregate pass.");
                }
            }

            var runResult = aggregate.ToResult(boundedTenants.Count);
            RecordSuccessMetrics(mode, runResult);

            logger.LogInformation(
                "AI retention cleanup completed for {TenantCount} active tenants in {Mode} mode. Eligible={EligibleConversations}, Redacted={RedactedConversations}.",
                runResult.TenantCount,
                mode,
                runResult.EligibleConversations,
                runResult.RedactedConversations);

            return runResult;
        }
        catch (Exception)
        {
            metrics.RecordAiRetentionCleanupRun(mode, "failed");
            throw;
        }
    }

    private async Task<AiRetentionCleanupResult> CleanupTenantAsync(
        Guid tenantId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        using var tenantScope = scopeFactory.CreateScope();
        var tenantAccessor = tenantScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        tenantAccessor.SetTenant(tenantId);

        try
        {
            var settingsResolver = tenantScope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
            var conversationRepository = tenantScope.ServiceProvider.GetRequiredService<IAiConversationRepository>();
            var aiSettings = await settingsResolver.ResolveGroupAsync<AiAssistantSettingGroup>(
                new SettingContext(TenantId: tenantId),
                cancellationToken);
            var retentionDays = Math.Max(1, aiSettings.RetentionDays);
            var cutoffUtc = utcNow.AddDays(-retentionDays);

            return await conversationRepository.RedactExpiredConversationsAsync(
                cutoffUtc,
                retentionDays,
                utcNow,
                _settings.DryRun,
                cancellationToken);
        }
        finally
        {
            tenantAccessor.Clear();
        }
    }

    private void RecordSuccessMetrics(string mode, AiRetentionCleanupRunResult result)
    {
        metrics.RecordAiRetentionCleanupRun(mode, result.FailedTenantCount == 0 ? "succeeded" : "partial_failure");
        metrics.RecordAiRetentionCleanupRows(result.EligibleConversations, mode, "eligible_conversations");
        metrics.RecordAiRetentionCleanupRows(result.RedactedConversations, mode, "redacted_conversations");
        metrics.RecordAiRetentionCleanupRows(result.RedactedMessages, mode, "redacted_messages");
        metrics.RecordAiRetentionCleanupRows(result.RedactedRuns, mode, "redacted_runs");
        metrics.RecordAiRetentionCleanupRows(result.RedactedReferences, mode, "redacted_references");
        metrics.RecordAiRetentionCleanupRows(result.RedactedProposedActions, mode, "redacted_proposed_actions");
        metrics.RecordAiRetentionCleanupRows(result.RedactedToolExecutions, mode, "redacted_tool_executions");
    }

    private sealed class AiRetentionCleanupAggregate(DateTime utcNow, bool dryRun)
    {
        private int _succeededTenants;
        private int _eligibleConversations;
        private int _redactedConversations;
        private int _redactedMessages;
        private int _redactedRuns;
        private int _redactedReferences;
        private int _redactedProposedActions;
        private int _redactedToolExecutions;
        private int _failedTenants;

        public void AddSuccess(AiRetentionCleanupResult result)
        {
            _succeededTenants++;
            _eligibleConversations += result.EligibleConversations;
            _redactedConversations += result.RedactedConversations;
            _redactedMessages += result.RedactedMessages;
            _redactedRuns += result.RedactedRuns;
            _redactedReferences += result.RedactedReferences;
            _redactedProposedActions += result.RedactedProposedActions;
            _redactedToolExecutions += result.RedactedToolExecutions;
        }

        public void AddFailure()
        {
            _failedTenants++;
        }

        public AiRetentionCleanupRunResult ToResult(int tenantCount)
        {
            return new AiRetentionCleanupRunResult(
                utcNow,
                tenantCount,
                _succeededTenants,
                _failedTenants,
                _eligibleConversations,
                _redactedConversations,
                _redactedMessages,
                _redactedRuns,
                _redactedReferences,
                _redactedProposedActions,
                _redactedToolExecutions,
                dryRun);
        }
    }
}
