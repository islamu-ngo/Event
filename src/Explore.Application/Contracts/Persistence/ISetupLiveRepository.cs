namespace Explore.Application.Contracts.Persistence;

using Explore.Domain.SetupLive;

public interface ISetupLiveRepository
{
    Task AddAsync(
        SetupTargetEnrollment enrollment,
        CancellationToken cancellationToken);

    Task AddAsync(
        SetupEnrollmentIssuanceClaim claim,
        CancellationToken cancellationToken);

    Task AddAsync(
        SetupSecretBindingOperation operation,
        CancellationToken cancellationToken);

    Task<SetupTargetEnrollment?> FindEnrollmentAsync(
        Guid tenantId,
        Guid enrollmentId,
        CancellationToken cancellationToken);

    Task<SetupTargetEnrollment?> FindCurrentEnrollmentAsync(
        Guid tenantId,
        Guid enrollmentId,
        CancellationToken cancellationToken);

    Task<SetupEnrollmentIssuanceClaim?> FindIssuanceClaimAsync(
        Guid tenantId,
        Guid operationKey,
        CancellationToken cancellationToken);

    Task<SetupSecretBindingOperation?> FindOperationAsync(
        Guid tenantId,
        Guid operationKey,
        CancellationToken cancellationToken);

    Task<SetupSecretBindingOperation?> FindOperationByIdAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
