using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IEmailDispatchRetentionCleanupService
{
    Task<EmailDispatchRetentionCleanupResult> CleanupAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
