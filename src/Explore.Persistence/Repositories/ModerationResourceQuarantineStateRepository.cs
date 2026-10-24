using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;

namespace Explore.Persistence.Repositories;

public sealed class ModerationResourceQuarantineStateRepository(
    ModerationResourceQuarantineReleaseRepository owner, IPrivacyIdentityFenceAuthority privacy)
    : IModerationResourceQuarantineStateRepository, IModerationResourceQuarantineReviewStateRepository
{
    public Task<bool> DiscloseAsync(ModerationResourceQuarantineStateIntent intent,
        ModerationResourceQuarantineStateOutput write, CancellationToken cancellationToken) =>
        owner.DiscloseStateAsync(intent, privacy, write, cancellationToken);

    public Task<bool> DiscloseAsync(ModerationResourceQuarantineReviewStateIntent intent,
        ModerationResourceQuarantineReviewStateOutput write, CancellationToken cancellationToken) =>
        owner.DiscloseReviewStateAsync(intent, privacy, write, cancellationToken);
}
