namespace Explore.Application.Telemetry;

public enum WebhookTelemetryProvider
{
    Local = 1,
    Svix = 2,
    Coop = 3
}

public enum WebhookTelemetryOperation
{
    Delivery = 1,
    Publication = 2,
    Reconciliation = 3,
    Recovery = 4,
    Readiness = 5,
    IncomingEffect = 6
}

public enum WebhookTelemetryOutcome
{
    Claimed = 1,
    Succeeded = 2,
    ProviderQueued = 3,
    RetryScheduled = 4,
    DeadLettered = 5,
    PublicationUnknown = 6,
    ManualReconciliation = 7,
    Deferred = 8,
    LeaseLost = 9,
    Failed = 10,
    Abandoned = 11,
    AutoPaused = 12,
    Recovered = 13,
    Healthy = 14,
    Degraded = 15,
    Unhealthy = 16,
    NotSelected = 17,
    Disabled = 18
}

internal static class WebhookTelemetryDimensionCodes
{
    public static string Provider(WebhookTelemetryProvider provider) => provider switch
    {
        WebhookTelemetryProvider.Local => "local",
        WebhookTelemetryProvider.Svix => "svix",
        WebhookTelemetryProvider.Coop => "coop",
        _ => "unknown"
    };

    public static string Operation(WebhookTelemetryOperation operation) => operation switch
    {
        WebhookTelemetryOperation.Delivery => "delivery",
        WebhookTelemetryOperation.Publication => "publication",
        WebhookTelemetryOperation.Reconciliation => "reconciliation",
        WebhookTelemetryOperation.Recovery => "recovery",
        WebhookTelemetryOperation.Readiness => "readiness",
        WebhookTelemetryOperation.IncomingEffect => "incoming_effect",
        _ => "unknown"
    };

    public static string Outcome(WebhookTelemetryOutcome outcome) => outcome switch
    {
        WebhookTelemetryOutcome.Claimed => "claimed",
        WebhookTelemetryOutcome.Succeeded => "succeeded",
        WebhookTelemetryOutcome.ProviderQueued => "provider_queued",
        WebhookTelemetryOutcome.RetryScheduled => "retry_scheduled",
        WebhookTelemetryOutcome.DeadLettered => "dead_lettered",
        WebhookTelemetryOutcome.PublicationUnknown => "publication_unknown",
        WebhookTelemetryOutcome.ManualReconciliation => "manual_reconciliation",
        WebhookTelemetryOutcome.Deferred => "deferred",
        WebhookTelemetryOutcome.LeaseLost => "lease_lost",
        WebhookTelemetryOutcome.Failed => "failed",
        WebhookTelemetryOutcome.Abandoned => "abandoned",
        WebhookTelemetryOutcome.AutoPaused => "auto_paused",
        WebhookTelemetryOutcome.Recovered => "recovered",
        WebhookTelemetryOutcome.Healthy => "healthy",
        WebhookTelemetryOutcome.Degraded => "degraded",
        WebhookTelemetryOutcome.Unhealthy => "unhealthy",
        WebhookTelemetryOutcome.NotSelected => "not_selected",
        WebhookTelemetryOutcome.Disabled => "disabled",
        _ => "unknown"
    };
}
