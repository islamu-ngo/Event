namespace Explore.Domain;

public sealed class WebhookProviderPublicationAttemptOutcomeLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookProviderPublicationAttemptOutcome
{
    PublishingStarted = 1,
    ProviderQueued = 2,
    RetryScheduled = 3,
    PublicationUnknown = 4,
    DeadLettered = 5,
    AutomaticReconciliationStarted = 6,
    AutomaticReconciliationUnresolved = 7,
    ManualReconciliationRequired = 8,
    ReconciledProviderQueued = 9,
    Abandoned = 10,
    ProviderAbsenceConfirmed = 11
}
