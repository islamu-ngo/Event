using Explore.Application.Responses;
using Explore.Domain.Enums;
using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.SupportAccess;

public sealed record StartSupportAccessSessionRequestDto
{
    public Guid TargetTenantId { get; init; }
    public Guid? TargetTenantUserId { get; init; }
    public SupportAccessModeEnum Mode { get; init; } = SupportAccessModeEnum.ReadOnly;
    public int DurationMinutes { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string ReasonText { get; init; } = string.Empty;
    public string? TicketReference { get; init; }
}

public sealed record StopSupportAccessSessionRequestDto
{
    public string? EndReasonText { get; init; }
}

public sealed record ForceStopSupportAccessSessionRequestDto
{
    public string? EndReasonText { get; init; }
}

public sealed record SupportAccessSessionDto
{
    public Guid Id { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid TargetTenantId { get; init; }
    public Guid? TargetTenantUserId { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int ModeId { get; init; }
    public string ModeName { get; init; } = string.Empty;
    public bool AllowsWrites { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string TicketReference { get; init; } = string.Empty;
    public Guid? ApprovedByUserId { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public int? EndReasonId { get; init; }
    public string? EndReasonName { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SupportAccessSessionCommandResponseDto : BaseCommandResponse<Guid>
{
    private SupportAccessSessionCommandResponseDto(BaseCommandResponse<Guid> state, SupportAccessSessionDto? session) : base(state, true)
    {
        Session = session;
    }

    [JsonConstructor]
    internal SupportAccessSessionCommandResponseDto(Guid id, bool isSuccess, string? message, IReadOnlyList<string>? errors, string? failureCode, QuotaExceededDetails? quotaExceeded, SupportAccessSessionDto? session)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), session)
    {
    }

    public SupportAccessSessionDto? Session { get; }

    public static SupportAccessSessionCommandResponseDto Success(Guid id, string? message, SupportAccessSessionDto? session) =>
        new(BaseCommandResponse.Success(id, message), session);

    public static SupportAccessSessionCommandResponseDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}

public sealed record CurrentSupportAccessSessionDto
{
    public bool IsActive { get; init; }
    public SupportAccessSessionDto? Session { get; init; }
}

public sealed record SupportAccessAuditEventDto
{
    public Guid Id { get; init; }
    public Guid SupportAccessSessionId { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public int EventTypeId { get; init; }
    public string EventTypeName { get; init; } = string.Empty;
    public Guid? ActorUserId { get; init; }
    public Guid TargetTenantId { get; init; }
    public Guid? TargetTenantUserId { get; init; }
    public string? RouteName { get; init; }
    public string? RequestName { get; init; }
    public string? ResourceKind { get; init; }
    public string? ResourceId { get; init; }
    public string? Action { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public int? HttpStatusCode { get; init; }
    public string? CorrelationId { get; init; }
    public string? TraceId { get; init; }
    public string? SanitizedMetadataJson { get; init; }
}
