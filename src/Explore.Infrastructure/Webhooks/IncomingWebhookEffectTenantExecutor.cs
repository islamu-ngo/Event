using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Webhooks;

public sealed class IncomingWebhookEffectTenantExecutor(IServiceScopeFactory scopeFactory)
    : IIncomingWebhookEffectClaimExecutor
{
    public async Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(
        IncomingWebhookEffectClaim claim,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var machineAccessor = scope.ServiceProvider.GetRequiredService<IMachinePrincipalExecutionAccessor>();
        var principal = new ApiKeyPrincipalContext(
            $"internal:webhook-coop-effect:{claim.TenantId:N}",
            claim.TenantId,
            ExternalApiKeyOwnerType.Tenant,
            claim.TenantId,
            [InternalMachineScopes.ProcessIncomingWebhook]);

        tenantAccessor.SetTenant(claim.TenantId);
        machineAccessor.SetPrincipal(principal);

        try
        {
            var authorizationProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationProvider>();
            var decision = await authorizationProvider.AuthorizeAsync(
                new AuthorizationRequest(
                    AuthorizationCapabilityCatalog.Require(ResourceKinds.Webhook, AuthorizationActions.Webhooks.ProcessIncoming),
                    claim.EffectOutboxId.ToString("N"),
                    Facts: new TenantScopedAuthorizationFacts(claim.TenantId)),
                cancellationToken);
            if (!decision.IsAllowed)
            {
                return IncomingWebhookClaimExecutionResult.AuthorizationDenied();
            }

            var processor = scope.ServiceProvider.GetRequiredService<IIncomingWebhookEffectProcessingService>();
            return await processor.ProcessAsync(claim, cancellationToken);
        }
        finally
        {
            machineAccessor.Clear();
            tenantAccessor.Clear();
        }
    }
}
