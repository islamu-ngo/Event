using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

internal sealed class HumanTenantPreferenceStore(
    ExploreDbContext context,
    ExploreDbContextExecutionLease lease,
    IHumanTenantPreferencePreparation preparation,
    IServiceProvider services) : IHumanTenantPreferenceStore
{
    private IHumanTenantPreferenceMutation? _completed;
    private int _used;

    public async Task<bool> WriteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        RequireOwner(mutation);
        if (Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("A preference writer permits one original effect.");
        await mutation.RequireCurrentAsync(token);
        bool written = await HumanTenantPreferenceDataOperations.WriteAsync(context, services, mutation, token);
        if (!written) return false;
        _completed = mutation;
        await RequireCompleteAsync(mutation, token);
        return true;
    }

    public async Task RequireCompleteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        RequireOwner(mutation);
        if (!ReferenceEquals(_completed, mutation))
            throw new UnauthorizedAccessException("The original preference effect is incomplete.");
        bool matches = await HumanTenantPreferenceDataOperations.MatchesAsync(context, services, mutation, token);
        if (!matches) throw new UnauthorizedAccessException("The original preference image changed before settlement.");
    }

    private void RequireOwner(IHumanTenantPreferenceMutation mutation)
    {
        lease.RequireOriginalOwner(context);
        var human = lease.HumanScope
            ?? throw new UnauthorizedAccessException("Preference data requires the original human scope.");
        if (!ReferenceEquals(preparation.CurrentMutation, mutation)
            || human.Subject.PrincipalId != mutation.SubjectId
            || human.Purpose != HumanAdministratorCommitPurpose.GlobalInstance
            || lease.SelectedTenantId is not null || lease.PolicyPurpose != PolicyMutationPurpose.Standard
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("Preference data requires the original prepared Serializable owner.");
    }
}
