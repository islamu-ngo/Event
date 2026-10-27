using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationClassificationMaterialInventoryReadRepository
{
    Task<ModerationClassificationMaterialInventoryReadAcquisition> AcquireInventoryReadAsync(
        ModerationClassificationMaterialInventoryReadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Tenant and human are server-captured; the mandate is candidate intent, never permission.</summary>
public sealed record ModerationClassificationMaterialInventoryReadRequest(
    Guid RequestTenantId, Guid? HumanId, Guid EventGenerationId, Guid TaxonomyId, Guid ProfileId,
    ModerationNativeMode Mode, ModerationClassificationMaterialMandate Mandate);

public sealed record ModerationClassificationMaterialInventoryReadAcquisition(
    ModerationCurrentPolicyReadOutcome Outcome, IModerationClassificationMaterialInventoryReadLease? Lease = null);

/// <summary>
/// Complete native metadata on the original owned read transaction. Final revalidation does not
/// replace expected coordinates. This is not audience, age assurance or collection authority.
/// </summary>
public interface IModerationClassificationMaterialInventoryReadLease : IAsyncDisposable
{
    AuthorityScope Jurisdiction { get; }
    ModerationQualificationEpoch Epoch { get; }
    ModerationPolicyPublication Publication { get; }
    ModerationClassificationMaterialInventoryObservation Observation { get; }
    long MaterialWriterGeneration { get; }
    Task<ModerationCurrentPolicyReadOutcome> RevalidateAsync(CancellationToken cancellationToken);
}
