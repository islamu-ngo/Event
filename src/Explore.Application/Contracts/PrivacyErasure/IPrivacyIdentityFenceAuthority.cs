using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

/// <summary>Serializes binding capture and enrollment through its application commit.</summary>
public interface IPrivacyIdentityFenceAuthority
{
    Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task ValidateKeyAsync(string keyId, string verificationTag, CancellationToken cancellationToken);

    Task<PrivacyErasureIntent?> FindAsync(
        PrivacyIdentityFingerprint fingerprint, CancellationToken cancellationToken);

    Task<bool> IsSubjectFencedAsync(Guid userId, CancellationToken cancellationToken);
}
