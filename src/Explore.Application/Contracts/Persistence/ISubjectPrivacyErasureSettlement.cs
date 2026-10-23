using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Fixed subject disposal within the caller's transaction. Preparation exposes only
/// provider locators to the application protector; disposal returns non-private effects.
/// Neither operation accepts a table, predicate, entity graph, or executable command.
/// </summary>
public interface ISubjectPrivacyErasureSettlement
{
    Task<IReadOnlyList<PrivacyErasureProviderCandidate>> PrepareAsync(
        PrivacyErasureIntent intent, IPrivacyErasureAuthority authority, CancellationToken cancellationToken);

    Task<SubjectPrivacyErasureEffects> DisposeAsync(
        PrivacyErasureIntent intent, DateTime appliedAtUtc, CancellationToken cancellationToken);
}

public sealed record SubjectPrivacyErasureEffects(
    IReadOnlyList<ErasedSubjectHome> Homes,
    IReadOnlyList<ErasedSubjectEventLocation> EventLocations,
    IReadOnlyList<Guid> TenantIds);

public sealed record ErasedSubjectHome(Guid Id, Guid TenantId, Guid ConcurrencyStamp);

public sealed record ErasedSubjectEventLocation(
    Guid Id, Guid TenantId, Guid EventId, Guid? LocationId, int PolicyVersion)
{
    /// <summary>Native disposal already retained this original message identity in its transaction.</summary>
    public Guid? RetainedCorrectionMessageId { get; init; }
}
