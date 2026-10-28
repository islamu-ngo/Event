using System.Collections.Immutable;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Independently observes loaded deployment modules and their current file bytes.
/// Unavailable observations return null; observations confer no approval or execution attestation.
/// </summary>
public interface IModerationMeasurementRuntimeArtifacts
{
    Task<ModerationMeasurementRuntimeArtifactObservation?> ReadAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ModerationMeasurementRuntimeArtifactObservation(
    string ProductArtifactDigest, ImmutableArray<ModerationMeasurementRuntimeModule> Modules);

public sealed record ModerationMeasurementRuntimeModule(
    string Assembly, Guid ModuleVersionId, string Sha256);
