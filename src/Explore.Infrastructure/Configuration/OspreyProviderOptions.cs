namespace Explore.Infrastructure.Configuration;

public sealed class OspreyProviderOptions
{
    public const string SectionName = "Reporting:Osprey";
    public const string TransportHttpJson = "HttpJson";
    public const string TransportGrpc = "Grpc";

    public bool Enabled { get; set; }
    public string Transport { get; set; } = TransportHttpJson;
    public string EndpointUrl { get; set; } = string.Empty;
    public string GrpcEndpointUrl { get; set; } = string.Empty;
    public string EvaluatePath { get; set; } = "/api/v1/evaluate";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyHeaderName { get; set; } = "Authorization";
    public string EventType { get; set; } = "event_report";
    public int TimeoutSeconds { get; set; } = 10;
    public bool AllowLocalProviderEndpoints { get; set; }
}
