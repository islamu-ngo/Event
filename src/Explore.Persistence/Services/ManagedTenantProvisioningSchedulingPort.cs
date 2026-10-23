using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Application.Exceptions;
using Explore.Application.Features.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Responses;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>Selects an independent instance owner before any scheduling source or transaction is constructed.</summary>
internal sealed class ManagedTenantProvisioningSchedulingPort(
    IServiceScopeFactory scopes, IGlobalExploreDbContextFactory global,
    PostgresNativeQueueEnrollment? enrollment, IdentityDatabaseTopology topology)
    : IManagedTenantProvisioningSchedulingPort
{
    public async Task<BaseCommandResponse<ManagedTenantProvisioningOperation>> ScheduleAsync(
        ScheduleManagedTenantProvisioningCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ManagedInstanceId == Guid.Empty)
            throw new UnauthorizedAccessException("managed_principal_invalid");
        var original = new ScheduleManagedTenantProvisioningCommand(request.ManagedInstanceId,
            ManagedTenantProvisioningRequestCodec.Deserialize(ManagedTenantProvisioningRequestCodec.Serialize(request.Request)));
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        bool settled = false;
        try
        {
            context = await global.CreateDbContextAsync(cancellationToken);
            var selection = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
            selection.Bind(context, null);
            if (!ReferenceEquals(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), context))
                throw new InvalidOperationException("managed_scheduling_context_not_selected");
            captured = true;
            await context.Database.OpenConnectionAsync(cancellationToken);
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(context, enrollment, topology, cancellationToken);
            var response = await scope.ServiceProvider.GetRequiredService<ManagedTenantProvisioningSchedulingOperation>()
                .ExecuteAsync(original, cancellationToken);
            settled = true;
            return response;
        }
        finally
        {
            try
            {
                if (!captured && context is not null)
                    await context.DisposeAsync();
                await scope.DisposeAsync();
            }
            catch (Exception exception) when (settled && exception is not HumanMutationOutcomeUnknownException)
            {
                throw new HumanMutationOutcomeUnknownException(exception);
            }
        }
    }
}
