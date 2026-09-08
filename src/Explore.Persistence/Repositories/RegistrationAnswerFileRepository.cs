// ABOUTME: Persists tenant-scoped registration-file release transitions and append-only audits atomically.
// ABOUTME: Treats repeated release requests as idempotent reads of the immutable first-release record.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationAnswerFileRepository(ExploreDbContext dbContext) : IRegistrationAnswerFileRepository
{
    public Task<RegistrationAnswerFile?> GetAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => dbContext.RegistrationAnswerFiles.AsNoTracking()
            .SingleOrDefaultAsync(file => file.TenantId == tenantId && file.Id == id, cancellationToken);

    public Task<RegistrationOrder?> GetOrderAsync(RegistrationAnswerFile file, CancellationToken cancellationToken)
        => (from submission in dbContext.RegistrationSubmissions.AsNoTracking()
            join order in dbContext.RegistrationOrders.AsNoTracking()
                on new { submission.TenantId, submission.EventId, Id = submission.RegistrationOrderId }
                equals new { order.TenantId, order.EventId, order.Id }
            where submission.TenantId == file.TenantId && submission.EventId == file.EventId &&
                submission.Id == file.RegistrationSubmissionId
            select order).SingleOrDefaultAsync(cancellationToken);

    public Task<RegistrationAnswerFileRelease?> GetReleaseAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => dbContext.RegistrationAnswerFileReleases.AsNoTracking()
            .SingleOrDefaultAsync(release =>
                release.TenantId == tenantId && release.RegistrationAnswerFileId == id,
                cancellationToken);

    public async Task<RegistrationAnswerFileReleaseResult?> ReleaseAsync(
        Guid tenantId,
        Guid id,
        Guid releasedBy,
        string reason,
        DateTime releasedAt,
        CancellationToken cancellationToken)
    {
        RegistrationAnswerFile? file = await dbContext.RegistrationAnswerFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == id, cancellationToken);
        if (file is null)
        {
            return null;
        }

        if (file.IsReleased)
        {
            RegistrationAnswerFileRelease release = await dbContext.RegistrationAnswerFileReleases
                .SingleAsync(candidate =>
                    candidate.TenantId == tenantId && candidate.RegistrationAnswerFileId == id,
                    cancellationToken);
            return new RegistrationAnswerFileReleaseResult(file, release, true);
        }

        RegistrationAnswerFileRelease created = file.ReleaseManually(releasedBy, reason, releasedAt);
        dbContext.RegistrationAnswerFileReleases.Add(created);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new RegistrationAnswerFileReleaseResult(file, created, false);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            RegistrationAnswerFile? releasedFile = await dbContext.RegistrationAnswerFiles.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.TenantId == tenantId && candidate.Id == id &&
                    candidate.QuarantineState == RegistrationAnswerFileQuarantineStates.Released,
                    cancellationToken);
            RegistrationAnswerFileRelease? existingRelease = await dbContext.RegistrationAnswerFileReleases.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.TenantId == tenantId && candidate.RegistrationAnswerFileId == id,
                    cancellationToken);
            if (releasedFile is not null && existingRelease is not null)
            {
                return new RegistrationAnswerFileReleaseResult(releasedFile, existingRelease, true);
            }

            throw;
        }
    }
}
