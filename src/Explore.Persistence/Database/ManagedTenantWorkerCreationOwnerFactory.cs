using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

/// <summary>
/// Selects one independent creator before any creator transaction. An implemented
/// invocation interface or copied claim cannot pass the original Runtime issuer.
/// </summary>
internal sealed class ManagedTenantWorkerCreationOwnerFactory(
    ManagedTenantWorkerCreationAdmission admission,
    IServiceScopeFactory scopes,
    IGlobalExploreDbContextFactory global) : IManagedTenantWorkerCreationOwnerFactory
{
    public async ValueTask<IManagedTenantWorkerCreationOwner> CreateAsync(
        IManagedTenantWorkerCreationInvocation invocation, CancellationToken cancellationToken = default)
    {
        admission.RequireIssued(invocation);
        await invocation.RequireCurrentAsync(cancellationToken);
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        ExploreDbContext.ManagedWorkerCreationContextLease? custody = null;
        bool captured = false;
        try
        {
            context = await global.CreateDbContextAsync(cancellationToken);
            var selection = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
            selection.Bind(context, selectedTenantId: null);
            if (!ReferenceEquals(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), context))
                throw new UnauthorizedAccessException("managed_worker_creator_selection_invalid");
            captured = true;
            await context.Database.OpenConnectionAsync(cancellationToken);
            custody = context.BindManagedWorkerCreation(admission, invocation);
            await invocation.RequireCurrentAsync(cancellationToken);
            return new Owner(scope, custody, invocation,
                scope.ServiceProvider.GetRequiredService<ManagedTenantWorkerCreationGraphWriter>());
        }
        catch
        {
            custody?.Dispose();
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class Owner(AsyncServiceScope scope,
        ExploreDbContext.ManagedWorkerCreationContextLease custody,
        IManagedTenantWorkerCreationInvocation invocation,
        ManagedTenantWorkerCreationGraphWriter writer) : IManagedTenantWorkerCreationOwner
    {
        private bool attempted;
        private bool disposed;

        public Task<Explore.Application.Responses.BaseCommandResponse<ManagedTenantWorkerCreationOutcome>>
            CreateAsync(CancellationToken cancellationToken = default)
        {
            if (disposed || attempted)
                throw new UnauthorizedAccessException("managed_worker_creator_replay");
            attempted = true;
            custody.RequireCurrent();
            return writer.CreateAsync(invocation, custody, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            // The Runtime invocation outlives this disposal in the Infrastructure port.
            custody.Dispose();
            await scope.DisposeAsync();
        }
    }
}
