using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

public interface IPrivacyErasureAuthority
{
    Task<PrivacyErasureAuthorityState> GetStateAsync(
        CancellationToken cancellationToken = default);

    Task<PrivacyErasureIntent> AppendAsync(
        PrivacyErasureRequest intent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrivacyErasureIntent>> ReadAfterAsync(
        long authoritySequence,
        int limit,
        CancellationToken cancellationToken = default);
}
