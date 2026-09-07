using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IPrivacyErasureCredentialCleanupService
{
    Task<PrivacyErasureCredentialCleanupResult> CleanupAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
