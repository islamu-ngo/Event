using System.Data;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Services;

/// <summary>
/// Opt-in selected-owner scheduler. Provider availability is the original live deployment
/// Local/Composite gate supplied by composition, not caller-authored requester authority.
/// All selected dependencies remain lazy until canonical human preparation has completed.
/// </summary>
public static class SelectedWebhookManualRetrySchedulingRegistration
{
    public static IServiceCollection AddSelectedWebhookManualRetryScheduling(
        this IServiceCollection services, Func<IServiceProvider, bool> localProcessingEnabled)
    {
        ArgumentNullException.ThrowIfNull(localProcessingEnabled);
        services.AddScoped<ISelectedWebhookManualRetryScheduler>(provider =>
            new SelectedWebhookManualRetryScheduler(
                () => provider.GetRequiredService<ExploreDbContext>(),
                () => provider.GetRequiredService<ExploreDbContextExecutionLease>(),
                () => new WebhookAuditEventWriter(
                    new WebhookAuditEventRepository(provider.GetRequiredService<ExploreDbContext>()),
                    provider.GetRequiredService<ICurrentUserService>(),
                    provider.GetRequiredService<IMachinePrincipalAccessor>(),
                    provider.GetRequiredService<IWebhookRetentionPolicyResolver>(),
                    provider.GetRequiredService<TimeProvider>()),
                () => localProcessingEnabled(provider),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<BusinessMetrics>()));
        return services;
    }
}

internal sealed class SelectedWebhookManualRetryScheduler(
    Func<ExploreDbContext> resolveContext,
    Func<ExploreDbContextExecutionLease> resolveLease,
    Func<IWebhookAuditEventWriter> resolveAudit,
    Func<bool> localProcessingEnabled,
    TimeProvider clock,
    BusinessMetrics metrics) : ISelectedWebhookManualRetryScheduler
{
    public async Task<WebhookDeliverySingleDrainResult> ScheduleAsync(
        RetryWebhookDeliveryAttemptCommand command,
        IPreparedHumanWebhookMutation preparedRequester,
        WebhookAuditPrincipalKind principalKind,
        string principalReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(preparedRequester);
        var context = resolveContext();
        var lease = resolveLease();
        lease.RequireOriginalOwner(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        void RequireOwner()
        {
            lease.RequireOriginalOwner(context);
            if (lease.SelectedTenantId is null || lease.HumanScope is not { Purpose: HumanAdministratorCommitPurpose.RuntimeTenant }
                || lease.HumanLifetime is null || context.IsTenantFilterBypassed
                || !context.HumanCommitAttemptOwnsTransaction || System.Transactions.Transaction.Current is not null
                || transaction is null || transaction.IsolationLevel != IsolationLevel.Serializable
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !RelationalConnectionIdentity.Capture(context).Owns(transaction))
                throw new InvalidOperationException("Manual webhook retry requires its original selected human Serializable owner.");
        }
        RequireOwner();
        // Only the original private preparation supplied by the parent is accepted integration
        // input. It registers real P/route/H/W and final-time admission on this actual owner.
        await preparedRequester.AcquireAsync(command, cancellationToken);
        RequireOwner();
        var result = await WebhookManualRetryScheduling.ScheduleAsync(
            lease.SelectedTenantId!.Value, command.AttemptId, principalKind, principalReference,
            new WebhookDeliveryAttemptRepository(context), new WebhookLocalTargetRepository(context),
            resolveAudit(), localProcessingEnabled(), clock, metrics, cancellationToken);
        RequireOwner();
        return result;
    }
}
