using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.InternalEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services.Moderation;

/// <summary>One bounded recovery pass through the real dispatcher, never a substitute completion writer.</summary>
public sealed class ModerationEffectRecoveryService(
    IServiceScopeFactory scopes, IModerationEffectDispatcher dispatcher, TimeProvider clock,
    ILogger<ModerationEffectRecoveryService> logger)
{
    public async Task<Guid?> ExecutePassAsync(
        Guid tenantId, int batchSize, Guid? afterEffectId, CancellationToken cancellationToken)
    {
        ModerationEffectRecoveryBatch batch;
        await using (var discovery = scopes.CreateAsyncScope())
        {
            var services = discovery.ServiceProvider;
            var tenant = services.GetRequiredService<ITenantContextAccessor>();
            var machine = services.GetRequiredService<IMachinePrincipalExecutionAccessor>();
            tenant.SetTenant(tenantId);
            try
            {
                var credential = await services.GetRequiredService<ModerationReconciliationCredentialProvider>()
                    .AuthenticateAsync(tenantId, cancellationToken);
                if (credential is null)
                    throw new InvalidOperationException("Moderation maintenance credential is unavailable.");
                machine.SetPrincipal(credential);
                batch = await services.GetRequiredService<IModerationReconciliationQuery>()
                    .ReadRecoverableEffectsAsync(tenantId, clock.GetUtcNow(), batchSize, afterEffectId, cancellationToken);
            }
            finally
            {
                machine.Clear();
                tenant.Clear();
            }
        }
        foreach (var target in batch.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await dispatcher.DispatchAsync(
                    ModerationEffectOutboxMessageFactory.Create(target.EffectId, target.CreatedAt), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Source failure stays pending. Do not retain private identifiers or provider error text.
                logger.LogWarning("Moderation effect recovery failed for one intent; continuing the bounded pass.");
            }
        }
        return batch.LastScannedEffectId;
    }
}
