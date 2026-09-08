using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Admissions;

public enum AdmissionCheckInTelemetryOutcome
{
    CheckedIn = 1,
    AlreadyCheckedIn = 2,
    Undone = 3,
    NotCheckedIn = 4,
    Rejected = 5,
    Unavailable = 6
}

public enum AdmissionCheckInSaturationKind
{
    RateLimiter = 1,
    BatchLimit = 2,
    Queue = 3
}

public enum AdmissionCheckInBacklogKind
{
    Transaction = 1,
    Audit = 2
}

public enum AdmissionCheckInLimiterPolicy
{
    StaffCheckIn = 1,
    ScannerCapability = 2,
    ScannerCheckIn = 3
}

public enum AdmissionCheckInInfrastructureKind
{
    AdmissionPath = 1
}

public enum AdmissionCheckInInfrastructureStatus
{
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3
}

public interface IAdmissionCheckInTelemetry
{
    void RecordOperation(
        AdmissionCheckInAction action,
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        AdmissionCheckInTelemetryOutcome outcome,
        double durationMilliseconds);

    void RecordBatch(
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        int batchSize);

    void RecordSaturation(
        AdmissionCheckInSaturationKind kind,
        AdmissionCheckInTelemetryOutcome outcome);

    void RecordBacklog(
        AdmissionCheckInBacklogKind kind,
        AdmissionTargetTypeEnum? targetType,
        long depth);

    void RecordRateLimiterRejection(
        AdmissionCheckInLimiterPolicy policy,
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType) =>
        RecordSaturation(AdmissionCheckInSaturationKind.RateLimiter, AdmissionCheckInTelemetryOutcome.Rejected);

    void RecordInfrastructureState(
        AdmissionCheckInInfrastructureKind kind,
        AdmissionCheckInInfrastructureStatus status)
    {
    }
}
