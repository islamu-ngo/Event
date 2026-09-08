// ABOUTME: Application persistence contract for tenant-contained registration-file review and release.
// ABOUTME: Keeps immutable release audit persistence atomic with the quarantine state transition.

using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationAnswerFileRepository
{
    Task<RegistrationAnswerFile?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<RegistrationOrder?> GetOrderAsync(RegistrationAnswerFile file, CancellationToken cancellationToken);
    Task<RegistrationAnswerFileRelease?> GetReleaseAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<RegistrationAnswerFileReleaseResult?> ReleaseAsync(
        Guid tenantId,
        Guid id,
        Guid releasedBy,
        string reason,
        DateTime releasedAt,
        CancellationToken cancellationToken);
}

public sealed record RegistrationAnswerFileReleaseResult(
    RegistrationAnswerFile File,
    RegistrationAnswerFileRelease Release,
    bool WasAlreadyReleased);
