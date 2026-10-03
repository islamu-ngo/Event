using Explore.Application.DTOs.InstanceAdmin;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.InstanceAdmin;

internal static class InstanceTenantMapper
{
    public static InstanceTenantListItemDto ToListItem(Tenant tenant) => new()
    {
        Id = tenant.Id,
        FullName = tenant.FullName,
        Slug = tenant.Slug,
        Description = tenant.Description,
        StatusId = tenant.TenantStatusId,
        StatusCode = StatusCode(tenant.TenantStatusId),
        StatusName = StatusName(tenant.TenantStatusId),
        IsActive = tenant.TenantStatusId == (int)TenantStatusEnum.Active,
        CreatedAt = tenant.CreatedAt,
        UpdatedAt = tenant.UpdatedAt
    };

    public static InstanceTenantDetailDto ToDetail(
        Tenant tenant,
        IReadOnlyList<TenantLifecycleLog> lifecycleLogs) => new()
        {
            Id = tenant.Id,
            FullName = tenant.FullName,
            Slug = tenant.Slug,
            Description = tenant.Description,
            StatusId = tenant.TenantStatusId,
            StatusCode = StatusCode(tenant.TenantStatusId),
            StatusName = StatusName(tenant.TenantStatusId),
            IsActive = tenant.TenantStatusId == (int)TenantStatusEnum.Active,
            CreatedAt = tenant.CreatedAt,
            CreatedBy = tenant.CreatedBy,
            UpdatedAt = tenant.UpdatedAt,
            UpdatedBy = tenant.UpdatedBy,
            LifecycleHistory = lifecycleLogs.Select(ToLifecycleEntry).ToArray()
        };

    public static InstanceTenantLifecycleEntryDto ToLifecycleEntry(TenantLifecycleLog log) => new()
    {
        Id = log.Id,
        TenantId = log.TenantId,
        OldStatusId = log.OldStatusId,
        OldStatusCode = log.OldStatusId is null ? null : StatusCode(log.OldStatusId.Value),
        OldStatusName = log.OldStatusId is null ? null : StatusName(log.OldStatusId.Value),
        NewStatusId = log.NewStatusId,
        NewStatusCode = StatusCode(log.NewStatusId),
        NewStatusName = StatusName(log.NewStatusId),
        TransitionedByUserId = log.TransitionedByUserId,
        Reason = log.Reason,
        TransitionedAt = log.TransitionedAt
    };

    public static InstanceTenantLifecycleTransitionDto ToTransition(
        Guid tenantId,
        int oldStatusId,
        int newStatusId,
        Guid transitionedByUserId,
        string? reason,
        DateTime transitionedAt) => new()
        {
            TenantId = tenantId,
            OldStatusId = oldStatusId,
            OldStatusCode = StatusCode(oldStatusId),
            OldStatusName = StatusName(oldStatusId),
            NewStatusId = newStatusId,
            NewStatusCode = StatusCode(newStatusId),
            NewStatusName = StatusName(newStatusId),
            TransitionedByUserId = transitionedByUserId,
            Reason = reason,
            TransitionedAt = transitionedAt
        };

    private static string StatusCode(int statusId) =>
        Enum.IsDefined(typeof(TenantStatusEnum), statusId)
            ? ((TenantStatusEnum)statusId).ToString().ToUpperInvariant()
            : $"UNKNOWN_{statusId}";

    private static string StatusName(int statusId) =>
        Enum.IsDefined(typeof(TenantStatusEnum), statusId)
            ? ((TenantStatusEnum)statusId).ToString()
            : "Unknown";
}
