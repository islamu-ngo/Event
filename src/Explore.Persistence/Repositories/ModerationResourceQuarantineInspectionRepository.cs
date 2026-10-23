using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;

namespace Explore.Persistence.Repositories;

/// <summary>Scoped adapter; the existing release owner retains all native qualification and transaction ownership.</summary>
public sealed class ModerationResourceQuarantineInspectionRepository(
    ModerationResourceQuarantineReleaseRepository owner,
    IPrivacyIdentityFenceAuthority privacy,
    ModerationResourceInspectionMaterialReader material)
    : IModerationResourceQuarantineInspectionRepository
{
    public Task<bool> DiscloseAsync(ModerationResourceQuarantineInspectionIntent intent,
        ModerationResourceInspectionOutput write, CancellationToken cancellationToken) =>
        owner.InspectMaterialAsync(intent, privacy, material, write, cancellationToken);
}
