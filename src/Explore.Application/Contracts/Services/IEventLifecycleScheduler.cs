namespace Explore.Application.Contracts.Services;

public interface IEventLifecycleScheduler
{
    Task SuppressEventRemindersInCurrentTransactionAsync(
        EventReminderSuppressionInput request,
        CancellationToken cancellationToken);

    Task ReprojectEventRemindersInCurrentTransactionAsync(
        EventReminderReprojectionInput request,
        CancellationToken cancellationToken);
}

public sealed record EventReminderSuppressionInput(
    Guid TenantId,
    Guid EventId,
    Guid? RegistrationOrderId,
    Guid? SessionId,
    DateTime SuppressedAt,
    string ReasonCode);

public sealed record EventReminderReprojectionInput(
    Guid TenantId,
    Guid EventId,
    Guid? RegistrationOrderId,
    Guid? SessionId,
    string EventTitle,
    DateTimeOffset ChangedAt,
    string EventTimeZoneId = "UTC");
