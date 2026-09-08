namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// Pointer keys are persisted in scheduler rows, so they are a durable contract between the code that
/// registers a deadline and the job that services it after a restart. Centralizing them keeps a rename on
/// one side from silently producing a job that finds nothing to do.
/// </summary>
public static class ScheduledDeadlinePointerKeys
{
    public const string TenantId = "tenantId";
    public const string PublishEventId = "publishEventId";
    public const string UseCase = "useCase";
    public const string RegistrationOrderId = "registrationOrderId";
}
