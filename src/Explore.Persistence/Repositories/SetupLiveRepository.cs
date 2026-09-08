namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain.SetupLive;
using Microsoft.EntityFrameworkCore;

public sealed class SetupLiveRepository(ExploreDbContext dbContext)
    : ISetupLiveRepository
{
    public Task AddAsync(
        SetupTargetEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        return dbContext.SetupTargetEnrollments.AddAsync(
            enrollment,
            cancellationToken).AsTask();
    }

    public Task AddAsync(
        SetupEnrollmentIssuanceClaim claim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return dbContext.SetupEnrollmentIssuanceClaims.AddAsync(
            claim,
            cancellationToken).AsTask();
    }

    public Task AddAsync(
        SetupSecretBindingOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return dbContext.SetupSecretBindingOperations.AddAsync(
            operation,
            cancellationToken).AsTask();
    }

    public Task<SetupTargetEnrollment?> FindEnrollmentAsync(
        Guid tenantId,
        Guid enrollmentId,
        CancellationToken cancellationToken) =>
        dbContext.SetupTargetEnrollments.SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.Id == enrollmentId,
            cancellationToken);

    public Task<SetupTargetEnrollment?> FindCurrentEnrollmentAsync(
        Guid tenantId,
        Guid enrollmentId,
        CancellationToken cancellationToken) =>
        dbContext.SetupTargetEnrollments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.TenantId == tenantId && value.Id == enrollmentId,
                cancellationToken);

    public Task<SetupEnrollmentIssuanceClaim?> FindIssuanceClaimAsync(
        Guid tenantId,
        Guid operationKey,
        CancellationToken cancellationToken) =>
        dbContext.SetupEnrollmentIssuanceClaims.SingleOrDefaultAsync(
            value => value.TenantId == tenantId
                && value.OperationKey == operationKey,
            cancellationToken);

    public Task<SetupSecretBindingOperation?> FindOperationAsync(
        Guid tenantId,
        Guid operationKey,
        CancellationToken cancellationToken) =>
        dbContext.SetupSecretBindingOperations.SingleOrDefaultAsync(
            value => value.TenantId == tenantId
                && value.OperationKey == operationKey,
            cancellationToken);

    public Task<SetupSecretBindingOperation?> FindOperationByIdAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        dbContext.SetupSecretBindingOperations.SingleOrDefaultAsync(
            value => value.TenantId == tenantId
                && value.Id == operationId,
            cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _ = await dbContext.SaveChangesAsync(cancellationToken);
}
