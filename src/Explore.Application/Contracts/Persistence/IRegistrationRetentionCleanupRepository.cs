namespace Explore.Application.Contracts.Persistence;

public sealed record RegistrationRetentionCleanupResult(
    int AnswersDeleted,
    int SensitiveValuesDeleted,
    int OrderPiiDeleted,
    int ParticipantPiiDeleted)
{
    public int TotalDeleted => AnswersDeleted + SensitiveValuesDeleted + OrderPiiDeleted + ParticipantPiiDeleted;
}

public interface IRegistrationRetentionCleanupRepository
{
    Task<RegistrationRetentionCleanupResult> CleanupTenantAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken);
}
