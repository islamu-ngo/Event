namespace Explore.Application.Models;

public sealed record WebPushSendEnvelope(
    string Endpoint,
    string P256Dh,
    string AuthSecret,
    string PayloadJson,
    string CorrelationId,
    int TimeToLiveSeconds,
    string Topic,
    WebPushUrgency Urgency);

public enum WebPushUrgency
{
    VeryLow = 1,
    Low = 2,
    Normal = 3,
    High = 4
}
