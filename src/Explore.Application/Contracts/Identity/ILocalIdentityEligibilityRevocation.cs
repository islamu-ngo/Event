using Explore.Domain;

namespace Explore.Application.Contracts.Identity;

/// <summary>Retains the Local lifecycle fence for one independently verified retained erasure intent.</summary>
public interface ILocalIdentityEligibilityRevocation
{
    Task<IAsyncDisposable> AcquireAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken);
    Task InvalidateInCurrentTransactionAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken);
}
