using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPrivacyErasureStateRepository
{
    Task<PrivacyErasureSaga?> GetBySubjectAsync(Guid subjectId, CancellationToken cancellationToken);
    Task<PrivacyErasureSaga?> GetByIntentAsync(Guid intentId, CancellationToken cancellationToken);
    Task<PrivacyErasureSaga?> FindByReceiptHashAsync(byte[] receiptHash, CancellationToken cancellationToken);
    Task<int> ClearExpiredReceiptHashesAsync(
        DateTime utcNow,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken);
    Task<bool> HasCoverageAsync(Guid intentId, int policyVersion, CancellationToken cancellationToken);
    Task AddSagaAsync(PrivacyErasureSaga saga, CancellationToken cancellationToken);
    Task AddCoverageAsync(PrivacyErasurePolicyCoverage coverage, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
