using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationClassificationMaterialReviewReadRepository
{
    Task<ModerationClassificationMaterialReviewReadAcquisition> AcquireReviewReadAsync(
        ModerationClassificationMaterialReviewReadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Only the source identity is authorable; human and tenant are captured by the server.</summary>
public sealed record ModerationClassificationMaterialReviewReadRequest(Guid RequestTenantId, Guid? HumanId, Guid SourceId);

public sealed record ModerationClassificationMaterialReviewReadAcquisition(
    ModerationCurrentPolicyReadOutcome Outcome, IModerationClassificationMaterialReviewReadLease? Lease = null);

/// <summary>Exact unaccepted proposal and complete original inventory, retained through nominated-reviewer output.</summary>
public interface IModerationClassificationMaterialReviewReadLease : IModerationClassificationMaterialInventoryReadLease
{
    ModerationClassificationMaterialSource Source { get; }
}
