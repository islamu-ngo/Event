using Explore.Application.Contracts.Operations;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

public sealed class FooterExecutionScopeFactory(IServiceProvider provider, IPolicyMutationExecutionScopeFactory scopes)
    : IFooterExecutionScopeFactory
{
    public async ValueTask<IPolicyMutationExecutionScope> CreateAsync(
        PolicyMutationScope selection, CancellationToken cancellationToken)
    {
        if (selection.Purpose != PolicyMutationPurpose.Standard || selection.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Footer requires an ordinary Global or exact RuntimeTenant owner.");
        var scope = await scopes.CreateAsync(selection, cancellationToken);
        try
        {
            RequireOwner(scope.Services, selection);
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public void RequireCurrentOwner(PolicyMutationScope selection) => RequireOwner(provider, selection);

    private static void RequireOwner(IServiceProvider services, PolicyMutationScope selection)
    {
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        var context = services.GetRequiredService<ExploreDbContext>();
        lease.RequireOriginalOwner(context);
        if (selection.Purpose != PolicyMutationPurpose.Standard || selection.TenantId == Guid.Empty
            || lease.SelectedTenantId != selection.TenantId
            || (lease.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
            || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
            || lease.HumanScope is { } human && human.Purpose != (selection.TenantId.HasValue
                ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance))
            throw new UnauthorizedAccessException("A broader source owner cannot be borrowed for Footer.");
    }
}
