using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private async Task AddEligibilityAsync(Guid subject, Guid operation, Guid actor, Guid login,
        DateTime occurredAt, CancellationToken token)
    {
        RequireEligibilityOwner();
        var row = LocalIdentityEligibility.Begin(subject, operation, actor, login, occurredAt);
        identityDbContext.Add(row);
        try
        {
            await identityDbContext.SaveChangesAsync(token).ConfigureAwait(false);
            RequireEligibilityOwner();
        }
        finally { identityDbContext.Entry(row).State = EntityState.Detached; }
    }

    private async Task ChangeEligibilityAsync(Guid subject, Guid operation,
        Action<LocalIdentityEligibility> mutate, CancellationToken token)
    {
        RequireEligibilityOwner();
        var row = await identityDbContext.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.LocalSubjectId == subject, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The current Local eligibility pointer is absent.");
        if (row.CurrentOperationId != operation)
            throw new UnauthorizedAccessException("The Local eligibility operation is no longer current.");
        var currentOperation = await identityDbContext.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == operation, token).ConfigureAwait(false);
        if (currentOperation is null || currentOperation.LocalSubjectId != subject
            || currentOperation.PersonalActorId != row.PersonalActorId || currentOperation.ExternalLoginId != row.ExternalLoginId)
            throw new UnauthorizedAccessException("The Local eligibility binding differs from its current operation.");
        long generation = row.Generation;
        var state = row.State;
        Guid actor = row.PersonalActorId, login = row.ExternalLoginId;
        DateTime? lockout = row.LockoutUntilUtc;
        mutate(row);
        RequireEligibilityOwner();
        int affected = await identityDbContext.Set<LocalIdentityEligibility>()
            .Where(value => value.Id == row.Id && value.LocalSubjectId == subject
                && value.CurrentOperationId == operation && value.Generation == generation && value.State == state
                && value.PersonalActorId == actor && value.ExternalLoginId == login && value.LockoutUntilUtc == lockout)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.CurrentOperationId, row.CurrentOperationId)
                .SetProperty(value => value.PersonalActorId, row.PersonalActorId)
                .SetProperty(value => value.ExternalLoginId, row.ExternalLoginId)
                .SetProperty(value => value.State, row.State).SetProperty(value => value.Generation, row.Generation)
                .SetProperty(value => value.UpdatedAt, row.UpdatedAt)
                .SetProperty(value => value.LockoutUntilUtc, row.LockoutUntilUtc), token).ConfigureAwait(false);
        if (affected != 1) throw new UnauthorizedAccessException("The current Local eligibility image changed before mutation.");
        RequireEligibilityOwner();
    }

    internal Task RefreshEligibilityAsync(Guid subject, Guid operation, DateTime occurredAt, CancellationToken token) =>
        ChangeEligibilityAsync(subject, operation, row => row.Refresh(row.Generation, operation, occurredAt), token);

    private void RequireEligibilityOwner()
    {
        RequireSelectedLifecycleCurrent();
        LocalIdentityLifecycleGateCustody.RequireHeld(identityDbContext);
        if (identityDbContext.Database.CurrentTransaction is null)
            throw new UnauthorizedAccessException("Local eligibility changes require the original identity transaction.");
    }
}
