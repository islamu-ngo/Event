using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

public sealed class CheckoutDispatchRuntimeScopeFactory(
    IServiceScopeFactory scopes, ITenantExploreDbContextFactory tenants, TimeProvider clock) : ICheckoutDispatchRuntimeScopeFactory
{
    public async ValueTask<ICheckoutDispatchRuntimeScope?> CreateAsync(
        CheckoutDispatchClaim claim, DateTime observedAt, CancellationToken cancellationToken)
    {
        if (claim.TenantId == Guid.Empty || claim.EffectId == Guid.Empty || claim.PaymentAttemptId == Guid.Empty ||
            claim.RegistrationOrderId == Guid.Empty || claim.LeaseToken == Guid.Empty || claim.ProcessingFence <= 0 ||
            claim.AttemptCount <= 0 || observedAt.Kind != DateTimeKind.Utc ||
            claim.LeaseExpiresAt is not { Kind: DateTimeKind.Utc } expiry || expiry <= observedAt)
            return null;
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(claim.TenantId);
            context = await tenants.CreateDbContextAsync(claim.TenantId, cancellationToken);
            // Claim validation uses only the already selected Runtime context, before
            // constructing the DI repository graph, provider routing or provider clients.
            if (await new RegistrationPaymentAttemptRepository(context)
                    .GetClaimedAttemptAsync(claim, observedAt, cancellationToken) is null ||
                expiry <= clock.GetUtcNow().UtcDateTime)
            {
                await context.DisposeAsync();
                context = null;
                await scope.DisposeAsync();
                return null;
            }
            scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(context, claim.TenantId);
            if (!ReferenceEquals(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), context))
                throw new InvalidOperationException("checkout_runtime_context_not_selected");
            captured = true;
            return new RuntimeScope(scope);
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class RuntimeScope(AsyncServiceScope scope) : ICheckoutDispatchRuntimeScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
