using System.ComponentModel.DataAnnotations;
using Explore.Application.Contracts.Admissions;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.Admissions;

public sealed record AdmissionCheckInRequestDto
{
    public required Guid TargetId { get; init; }

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public required string Credential { get; init; }
}

public sealed record AdmissionScannerCheckInRequestDto
{
    [Required]
    [StringLength(512, MinimumLength = 1)]
    public required string Credential { get; init; }
}

public sealed record AdmissionCheckInUndoRequestDto
{
    public required Guid TargetId { get; init; }

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public required string Credential { get; init; }

    [EnumDataType(typeof(AdmissionCheckInUndoReasonCodeEnum))]
    public required AdmissionCheckInUndoReasonCodeEnum ReasonCode { get; init; }
}

public sealed record AdmissionScannerCheckInUndoRequestDto
{
    [Required]
    [StringLength(512, MinimumLength = 1)]
    public required string Credential { get; init; }

    [EnumDataType(typeof(AdmissionCheckInUndoReasonCodeEnum))]
    public required AdmissionCheckInUndoReasonCodeEnum ReasonCode { get; init; }
}

public sealed record AdmissionCheckInBatchItemRequestDto
{
    [Required]
    [StringLength(512, MinimumLength = 1)]
    public required string Credential { get; init; }
}

public sealed record AdmissionCheckInBatchRequestDto
{
    public required Guid TargetId { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public required IReadOnlyList<AdmissionCheckInBatchItemRequestDto> Items { get; init; }
}

public sealed record AdmissionScannerCheckInBatchRequestDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public required IReadOnlyList<AdmissionCheckInBatchItemRequestDto> Items { get; init; }
}

public sealed record AdmissionCheckInResultDto(
    string OutcomeCode,
    Guid TargetId,
    DateTimeOffset? OccurredAt,
    Guid? CheckInId);

public sealed record AdmissionCheckInBatchItemResultDto(
    int Index,
    string OutcomeCode,
    Guid TargetId,
    DateTimeOffset? OccurredAt,
    Guid? CheckInId);

public sealed record AdmissionCheckInBatchResultDto(
    string OutcomeCode,
    IReadOnlyList<AdmissionCheckInBatchItemResultDto> Items);

public sealed record AdmissionCheckInSummaryDto(
    string TargetType,
    long CheckedInCount,
    long UndoneCount,
    long ActiveCount,
    long InactiveCount,
    DateTimeOffset? LastActivityTimeBucketUtc);

public sealed record AdmissionCheckInAuditItemDto(
    string Cursor,
    string Action,
    string Outcome,
    string TargetType,
    DateTimeOffset OccurredAtTimeBucketUtc);

public sealed record AdmissionCheckInAuditPageDto(
    IReadOnlyList<AdmissionCheckInAuditItemDto> Items,
    string? NextCursor);

public sealed record AdmissionCheckInOperationalRequestDto
{
    public required Guid TargetId { get; init; }
    public required AdmissionCheckInOperationalReasonCode ReasonCode { get; init; }
}

public sealed record AdmissionCheckInOperationalResultDto(
    Guid TargetId,
    AdmissionCheckInOperationalAction Action,
    AdmissionCheckInOperationalStatus Status,
    AdmissionCheckInOperationalReasonCode ReasonCode,
    DateTimeOffset OccurredAtUtc);

public sealed record AdmissionCheckInHealthDto(
    Guid TargetId,
    AdmissionCheckInOperationalStatus Status,
    AdmissionCheckInDependencyStatus InfrastructureStatus);

public sealed record IssueAdmissionScannerCapabilityRequestDto
{
    public required Guid IssueRequestId { get; init; }

    public required Guid TargetId { get; init; }

    [Required]
    [MinLength(1)]
    public required IReadOnlyList<AdmissionCheckInAction> Actions { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string DeviceLabel { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record RevokeAdmissionScannerCapabilityRequestDto
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public sealed record AdmissionScannerCapabilityDto(
    Guid ScannerCapabilityId,
    Guid EventId,
    Guid TargetId,
    IReadOnlyList<AdmissionCheckInAction> Actions,
    string DeviceLabel,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string MaskedCapability);

public sealed record AdmissionScannerCapabilityIssuedDto(
    Guid ScannerCapabilityId,
    Guid EventId,
    Guid TargetId,
    IReadOnlyList<AdmissionCheckInAction> Actions,
    string DeviceLabel,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string MaskedCapability,
    string? Capability);
