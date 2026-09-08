using Explore.Application.Contracts.Identity;
using Explore.Domain.Enums;

namespace Explore.Application.SupportAccess;

public sealed record SupportAccessContext(
    bool IsActive,
    Guid? SessionId = null,
    Guid? ActorUserId = null,
    Guid? TargetTenantId = null,
    Guid? TargetTenantUserId = null,
    SupportAccessModeEnum? Mode = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? ExpiresAtUtc = null,
    string? ReasonCode = null,
    string? TicketReference = null,
    bool WasForwarded = false) : ISupportAccessContext
{
    public static SupportAccessContext Inactive { get; } = new(false);
    public static SupportAccessContext InactiveForwarded { get; } = new(false, WasForwarded: true);

    public bool AllowsWrites => IsActive && Mode == SupportAccessModeEnum.Write;
}
