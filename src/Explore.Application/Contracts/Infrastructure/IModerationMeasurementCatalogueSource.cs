using Explore.Application.Services.Moderation;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Resolves immutable deployment bytes selected by independently requalified enrollment.
/// Neither a returned artifact nor its storage directory supplies current approval or Passed.
/// </summary>
public interface IModerationMeasurementCatalogueSource
{
    Task<ModerationMeasurementCatalogueArtifact?> ReadAsync(
        string approvedDigest, CancellationToken cancellationToken = default);
}

public sealed record ModerationMeasurementCatalogueArtifact(
    string Digest, ModerationMeasurementCatalogue Catalogue);
