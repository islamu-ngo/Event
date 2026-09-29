namespace Explore.Domain;

public sealed class WebhookPayloadProvenanceLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookPayloadProvenance
{
    ExactBytes = 1,
    LegacyJsonNormalized = 2,
    NormalizedProviderEnvelope = 3
}
