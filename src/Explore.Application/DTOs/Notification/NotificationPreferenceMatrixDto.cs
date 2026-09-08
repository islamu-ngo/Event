namespace Explore.Application.DTOs.Notification;

public sealed record NotificationPreferenceMatrixDto
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? GroupId { get; init; }
    public string Scope { get; init; } = "user";
    public IReadOnlyList<NotificationPreferenceCategoryDto> Categories { get; init; } = [];
    public IReadOnlyList<NotificationPreferenceChannelDto> Channels { get; init; } = [];
    public IReadOnlyList<NotificationPreferenceCellDto> Cells { get; init; } = [];
    public NotificationPreferenceMuteDto Mute { get; init; } = new();
}

public sealed record NotificationPreferenceCategoryDto
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

public sealed record NotificationPreferenceChannelDto
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

public sealed record NotificationPreferenceCellDto
{
    public required string CategoryCode { get; init; }
    public required string ChannelCode { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsEditable { get; init; }
    public bool IsRequired { get; init; }
    public bool IsLocked { get; init; }
    public bool IsMuted { get; init; }
    public required string EffectiveSourceScope { get; init; }
    public string? LockReason { get; init; }
}

public sealed record NotificationPreferenceMuteDto
{
    public bool IsMuted { get; init; }
    public bool IsEditable { get; init; } = true;
    public bool IsLocked { get; init; }
    public string? LockReason { get; init; }
}

public sealed record UpdateNotificationPreferenceMatrixDto
{
    public IReadOnlyList<UpdateNotificationPreferenceCellDto>? Cells { get; init; }
}

public sealed record UpdateNotificationPreferenceCellDto
{
    public required string CategoryCode { get; init; }
    public required string ChannelCode { get; init; }
    public bool IsEnabled { get; init; }
}

public sealed record SetNotificationPreferenceMuteDto
{
    public bool IsMuted { get; init; }
}
