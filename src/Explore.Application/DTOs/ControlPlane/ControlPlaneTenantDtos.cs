namespace Explore.Application.DTOs.ControlPlane;

public sealed record ControlPlaneTenantListItemDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record ControlPlaneTenantDetailDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Guid? UpdatedBy { get; init; }
    public IReadOnlyList<ControlPlaneTenantLifecycleEntryDto> LifecycleHistory { get; init; } = [];
}

public sealed record ControlPlaneTenantLifecycleEntryDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public int? OldStatusId { get; init; }
    public string? OldStatusCode { get; init; }
    public string? OldStatusName { get; init; }
    public int NewStatusId { get; init; }
    public string NewStatusCode { get; init; } = string.Empty;
    public string NewStatusName { get; init; } = string.Empty;
    public Guid? TransitionedByUserId { get; init; }
    public string? Reason { get; init; }
    public DateTime TransitionedAt { get; init; }
}

public sealed record ControlPlaneTenantLifecycleTransitionRequestDto
{
    public string? Reason { get; init; }
    public string? ConfirmationText { get; init; }
}

public sealed record ControlPlaneTenantLifecycleTransitionDto
{
    public Guid TenantId { get; init; }
    public int OldStatusId { get; init; }
    public string OldStatusCode { get; init; } = string.Empty;
    public string OldStatusName { get; init; } = string.Empty;
    public int NewStatusId { get; init; }
    public string NewStatusCode { get; init; } = string.Empty;
    public string NewStatusName { get; init; } = string.Empty;
    public Guid TransitionedByUserId { get; init; }
    public string? Reason { get; init; }
    public DateTime TransitionedAt { get; init; }
}
