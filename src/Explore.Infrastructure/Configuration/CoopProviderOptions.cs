namespace Explore.Infrastructure.Configuration;

public sealed class CoopProviderOptions
{
    public const string SectionName = "Reporting:Coop";

    public bool Enabled { get; set; }
    public string EndpointUrl { get; set; } = string.Empty;
    public string MirrorPath { get; set; } = "/api/v1/items";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyHeaderName { get; set; } = "Authorization";
    public string ItemType { get; set; } = "event_report";
    public int TimeoutSeconds { get; set; } = 10;
    public bool AllowLocalProviderEndpoints { get; set; }
    public string WebhookSecret { get; set; } = string.Empty;
    public string WebhookSignatureHeaderName { get; set; } = "X-Coop-Signature";
    public string WebhookTimestampHeaderName { get; set; } = "X-Coop-Timestamp";
    public int WebhookToleranceSeconds { get; set; } = 300;
    public long WebhookMaxBodyBytes { get; set; } = 65_536;
}
