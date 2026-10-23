namespace Explore.Infrastructure.Ai;

/// <summary>Operator-owned response ceilings. Tenant requests cannot raise these limits.</summary>
public sealed class AiResponseLimits
{
    public const string SectionName = "AiResponseLimits";

    public int MaximumDiscoveryBytes { get; set; } = 1024 * 1024;
    public int MaximumChatBytes { get; set; } = 4 * 1024 * 1024;
    public int MaximumDepth { get; set; } = 32;
    public int MaximumCollectionItems { get; set; } = 256;
    public int MaximumModelIdentifierCharacters { get; set; } = 256;
    public int MaximumChoices { get; set; } = 16;
    public int MaximumToolCalls { get; set; } = 128;
    public int MaximumToolArgumentBytes { get; set; } = 64 * 1024;
    public int MaximumScalarBytes { get; set; } = 256 * 1024;

    public bool IsValid =>
        MaximumDiscoveryBytes is > 0 and <= 1024 * 1024
        && MaximumChatBytes is > 0 and <= 4 * 1024 * 1024
        && MaximumDepth is > 0 and <= 32
        && MaximumCollectionItems is > 0 and <= 256
        && MaximumModelIdentifierCharacters is > 0 and <= 256
        && MaximumChoices is > 0 and <= 16
        && MaximumToolCalls is > 0 and <= 128
        && MaximumToolArgumentBytes is > 0 and <= 64 * 1024
        && MaximumScalarBytes is > 0 and <= 256 * 1024;
}
