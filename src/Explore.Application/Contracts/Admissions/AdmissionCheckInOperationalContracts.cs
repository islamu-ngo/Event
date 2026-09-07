using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public enum AdmissionCheckInOperationalAction
{
    Stop = 1,
    Restore = 2,
    Reconcile = 3
}

public enum AdmissionCheckInOperationalReasonCode
{
    DeviceLoss = 1,
    ConnectivityOutage = 2,
    OperatorCorrection = 3,
    PostIncidentReconciliation = 4
}

public enum AdmissionCheckInOperationalStatus
{
    Active = 1,
    Stopped = 2,
    Unavailable = 3
}

public enum AdmissionCheckInDependencyStatus
{
    Available = 1,
    Unavailable = 2
}

public sealed record AdmissionCheckInOperationalRequest(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    Guid StaffActorId,
    AdmissionCheckInOperationalAction Action,
    AdmissionCheckInOperationalReasonCode ReasonCode);

public sealed record AdmissionCheckInHealthRequest(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    Guid StaffActorId);

public sealed record AdmissionCheckInOperationalResult(
    Guid TargetId,
    AdmissionCheckInOperationalAction Action,
    AdmissionCheckInOperationalStatus Status,
    AdmissionCheckInOperationalReasonCode ReasonCode,
    DateTimeOffset OccurredAtUtc);

public sealed record AdmissionCheckInHealthResult(
    Guid TargetId,
    AdmissionCheckInOperationalStatus Status,
    AdmissionCheckInDependencyStatus InfrastructureStatus);

public interface IAdmissionTargetOperationsRepository
{
    Task<AdmissionTarget?> GetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        CancellationToken cancellationToken);

    Task<AdmissionTarget> UpdateAsync(
        AdmissionTarget target,
        CancellationToken cancellationToken);
}

public interface IAdmissionCheckInHealthProbe
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
