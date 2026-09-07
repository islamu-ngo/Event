using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Admissions;

public enum AdmissionCheckInSummaryState
{
    Active = 1,
    Inactive = 2
}

public enum AdmissionCheckInAuthorityKind
{
    Staff = 1,
    Scanner = 2,
    Unknown = 3
}

public sealed record AdmissionCheckInSummaryRequest(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    Guid StaffActorId);

public sealed record AdmissionCheckInDetailRequest(
    Guid TenantId,
    Guid EventId,
    Guid CheckInId,
    Guid StaffActorId);

public sealed record AdmissionCheckInDetail(
    AdmissionCheckInResult Result,
    bool CanUndo);

public sealed record AdmissionCheckInResultCount(
    AdmissionCheckInOutcome Outcome,
    long Count);

public sealed record AdmissionCheckInStateCount(
    AdmissionCheckInSummaryState State,
    long Count);

public sealed record AdmissionCheckInSummary(
    AdmissionTargetTypeEnum TargetType,
    IReadOnlyList<AdmissionCheckInResultCount> ResultCounts,
    IReadOnlyList<AdmissionCheckInStateCount> StateCounts,
    DateTimeOffset? LastActivityTimeBucketUtc);

public sealed record AdmissionCheckInSummaryProjection(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    AdmissionTargetTypeEnum TargetType,
    long CheckedInCount,
    long UndoneCount,
    long ActiveStateCount,
    long InactiveStateCount,
    DateTime? LastActivityUtc);

public interface IAdmissionCheckInSummaryQuery
{
    Task<AdmissionCheckInSummaryProjection?> GetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        CancellationToken cancellationToken);
}

public sealed record AdmissionCheckInAuditPageRequest(
    Guid TenantId,
    Guid EventId,
    Guid StaffActorId,
    string? Cursor,
    int PageSize);

public sealed record AdmissionCheckInAuditItem(
    string Cursor,
    AdmissionCheckInAction Action,
    AdmissionCheckInOutcome Outcome,
    AdmissionTargetTypeEnum TargetType,
    DateTimeOffset OccurredAtTimeBucketUtc);

public sealed record AdmissionCheckInAuditPage(
    IReadOnlyList<AdmissionCheckInAuditItem> Items,
    string? NextCursor);

public interface IAdmissionCheckInReportingRepository
{
    Task<AdmissionCheckInEvent?> GetEventAsync(
        Guid tenantId,
        Guid eventId,
        Guid checkInId,
        CancellationToken cancellationToken);

    Task<AdmissionCheckInEvent?> GetActiveEventAsync(
        Guid tenantId,
        Guid ticketId,
        Guid targetId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdmissionCheckInEvent>> ListEventAuditPageAsync(
        Guid tenantId,
        Guid eventId,
        AdmissionCheckInEvent? cursor,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdmissionTarget>> ListTargetsAsync(
        Guid tenantId,
        Guid eventId,
        IReadOnlyList<Guid> targetIds,
        CancellationToken cancellationToken);
}
