using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Infrastructure;

public interface IS3PreflightVerifier
{
    Task<S3PreflightResult> VerifyAsync(
        S3PreflightRequest request,
        CancellationToken cancellationToken = default);
}
