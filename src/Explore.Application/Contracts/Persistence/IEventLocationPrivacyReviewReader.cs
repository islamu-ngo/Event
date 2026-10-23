namespace Explore.Application.Contracts.Persistence;

/// <summary>Count-only instance observation; no location identities or private payloads are returned.</summary>
public interface IEventLocationPrivacyReviewReader
{
    Task<int> CountNeedingPrivacyReviewAsync(CancellationToken cancellationToken);
}
