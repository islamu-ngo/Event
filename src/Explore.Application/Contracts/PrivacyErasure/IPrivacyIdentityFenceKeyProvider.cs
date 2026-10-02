namespace Explore.Application.Contracts.PrivacyErasure;

public interface IPrivacyIdentityFenceKeyProvider
{
    Task<PrivacyIdentityFenceKey> ResolveAsync(CancellationToken cancellationToken);
}
