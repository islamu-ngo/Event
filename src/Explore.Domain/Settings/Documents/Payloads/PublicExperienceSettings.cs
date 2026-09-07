namespace Explore.Domain.Settings.Documents.Payloads;

public sealed record PublicExperienceSettings
{
    public string Mode { get; init; } = "tenant";

    public string EventCatalogLabel { get; init; } = "Events";
}
