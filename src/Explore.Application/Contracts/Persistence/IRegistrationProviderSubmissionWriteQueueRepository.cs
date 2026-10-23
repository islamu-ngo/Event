namespace Explore.Application.Contracts.Persistence;

/// <summary>Claims identities on Global authority without hydrating provider delivery data.</summary>
public interface IRegistrationProviderSubmissionWriteQueueRepository
{
    Task<IReadOnlyList<RegistrationProviderSubmissionWriteClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}
