using Explore.Application.Services.Moderation;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Trusted host composition observes its actual native consumer artifacts. This is not
/// a conformance verdict, client-supplied mode, publication approval or substitute for measurement.
/// </summary>
public interface IModerationConsumerRuntimeSource
{
    string ConsumerId { get; }
    ModerationNativeMode Mode { get; }
    ModerationConsumerRuntimeObservation? ReadCurrent();
}

public sealed record ModerationConsumerRuntimeObservation(
    string ConsumerId, ModerationNativeMode Mode, string RuntimeGeneration, string InventoryFingerprint,
    string? ProductArtifactDigest);
