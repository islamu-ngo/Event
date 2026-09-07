using Explore.Application.Contracts.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationRetentionCleanupRepository(ExploreDbContext dbContext, IUnitOfWork unitOfWork)
    : IRegistrationRetentionCleanupRepository
{
    public async Task<RegistrationRetentionCleanupResult> CleanupTenantAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Cleanup time must be UTC.", nameof(utcNow));
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Guid[] answerIds = await dbContext.RegistrationAnswers
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(answer => answer.TenantId == tenantId && answer.RetentionUntil <= utcNow)
                .OrderBy(answer => answer.RetentionUntil)
                .Select(answer => answer.Id)
                .Take(batchSize)
                .ToArrayAsync(token);
            Guid[] sensitiveValueIds = await dbContext.RegistrationAnswers
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(answer => answerIds.Contains(answer.Id) && answer.SensitiveAnswerValueId != null)
                .Select(answer => answer.SensitiveAnswerValueId!.Value)
                .ToArrayAsync(token);
            int answersDeleted = await dbContext.RegistrationAnswers
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(answer => answer.TenantId == tenantId && answerIds.Contains(answer.Id))
                .ExecuteDeleteAsync(token);
            int sensitiveValuesDeleted = await dbContext.RegistrationSensitiveAnswerValues
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(value => value.TenantId == tenantId && sensitiveValueIds.Contains(value.Id))
                .ExecuteDeleteAsync(token);
            int orderPiiDeleted = await dbContext.RegistrationOrderPii
                .IgnoreQueryFilters([QueryFilterNames.Tenant])
                .Where(pii => pii.TenantId == tenantId && pii.RetentionUntil <= utcNow)
                .Take(batchSize)
                .ExecuteDeleteAsync(token);
            int participantPiiDeleted = await dbContext.RegistrationParticipantPii
                .IgnoreQueryFilters([QueryFilterNames.Tenant])
                .Where(pii => pii.TenantId == tenantId && pii.RetentionUntil <= utcNow)
                .Take(batchSize)
                .ExecuteDeleteAsync(token);

            return new RegistrationRetentionCleanupResult(
                answersDeleted, sensitiveValuesDeleted, orderPiiDeleted, participantPiiDeleted);
        }, cancellationToken);
    }
}
