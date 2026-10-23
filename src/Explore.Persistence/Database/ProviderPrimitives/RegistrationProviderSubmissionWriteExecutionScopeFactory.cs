using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration.Commands;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class RegistrationProviderSubmissionWriteExecutionScopeFactory(
    IServiceScopeFactory scopes,
    ITenantExploreDbContextFactory tenantFactory) : IRegistrationProviderSubmissionWriteExecutionScopeFactory
{
    public async ValueTask<IRegistrationProviderSubmissionWriteExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Provider delivery requires an exact tenant.", nameof(tenantId));

        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
            if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenantId)
                throw new InvalidOperationException("provider_write_runtime_tenant_not_selected");
            scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(context, tenantId);
            var selected = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            if (!ReferenceEquals(selected, context))
                throw new InvalidOperationException("provider_write_runtime_context_not_selected");
            captured = true;
            // Registry, sink, repositories and their units of work must share this exact lease.
            var delivery = scope.ServiceProvider.GetRequiredService<RegistrationProviderSubmissionWriteEffectDeliveryService>();
            return new ExecutionScope(scope, delivery);
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(
        AsyncServiceScope scope,
        RegistrationProviderSubmissionWriteEffectDeliveryService delivery) : IRegistrationProviderSubmissionWriteExecutionScope
    {
        public Task<bool> DrainAsync(RegistrationProviderSubmissionWriteClaim claim, CancellationToken cancellationToken) =>
            delivery.DrainAsync(claim, cancellationToken);

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
