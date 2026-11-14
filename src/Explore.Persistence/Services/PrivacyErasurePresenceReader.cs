using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>Uses the existing Global subject-column permission while leaving the caller's owner and saga receipts untouched.</summary>
public sealed class PrivacyErasurePresenceReader(IGlobalExploreDbContextFactory globalFactory)
    : IPrivacyErasurePresenceReader
{
    public async Task<bool> ExistsBySubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await context.PrivacyErasureSagas.IgnoreAutoIncludes().AsNoTracking()
            .AnyAsync(saga => saga.SubjectId == subjectId, cancellationToken);
    }
}
