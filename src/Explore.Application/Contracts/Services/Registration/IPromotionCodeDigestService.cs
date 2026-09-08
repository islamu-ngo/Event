namespace Explore.Application.Contracts.Services.Registration;

public interface IPromotionCodeDigestService
{
    string NormalizeCode(string code);

    Task<PromotionCodeDigest> ComputeActiveAsync(Guid tenantId, Guid eventId, string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromotionCodeDigest>> ComputeCandidatesAsync(
        Guid tenantId,
        Guid eventId,
        string code,
        IReadOnlyCollection<int> persistedKeyVersions,
        CancellationToken cancellationToken = default);

    bool Matches(string candidateDigest, string expectedDigest);
}

public sealed record PromotionCodeDigest(int KeyVersion, string Value);
