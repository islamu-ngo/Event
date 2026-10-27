using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private void ValidateAdjudicatedCaseReadGrants()
    {
        foreach (var entry in ChangeTracker.Entries<ModerationAdjudicatedCaseReadGrant>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            entry.Entity.ValidatePersistedState();
            // Retention and erasure owners may delete; this guard does not establish their authority.
            if (entry.State != EntityState.Modified)
                continue;

            var original = (ModerationAdjudicatedCaseReadGrant)entry.OriginalValues.ToObject();
            original.ValidatePersistedState();
            if (original.Revision != 1 || original.RevokedAt is not null || entry.Entity.Revision != 2)
                throw new InvalidOperationException("Adjudicated-case read grants permit only one complete revocation transition.");

            foreach (var property in entry.Properties.Where(property => property.Metadata.Name is not
                         (nameof(ModerationAdjudicatedCaseReadGrant.Revision)
                         or nameof(ModerationAdjudicatedCaseReadGrant.RevokedAt)
                         or nameof(ModerationAdjudicatedCaseReadGrant.RevocationOperationId)
                         or nameof(ModerationAdjudicatedCaseReadGrant.RevokedByUserId)
                         or nameof(ModerationAdjudicatedCaseReadGrant.RevokedByRoleGrantId)
                         or nameof(ModerationAdjudicatedCaseReadGrant.RevokedByPlatformGrant))))
            {
                if (!Equals(property.OriginalValue, property.CurrentValue))
                    throw new InvalidOperationException("Adjudicated-case read grant original coordinates cannot be rewritten or renewed.");
            }
        }
    }
}
