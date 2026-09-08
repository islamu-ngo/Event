using System.Text.Json.Serialization;
using Explore.Application.Responses;

namespace Explore.Application.Features.Promotions;

public sealed record PromotionManagementCommandResponseDto : BaseCommandResponse<Guid>
{
    private PromotionManagementCommandResponseDto(BaseCommandResponse<Guid> state, PromotionManagementDto? promotion) : base(state, true)
    {
        Promotion = promotion;
    }

    [JsonConstructor]
    internal PromotionManagementCommandResponseDto(Guid id, bool isSuccess, string? message, IReadOnlyList<string>? errors, string? failureCode, QuotaExceededDetails? quotaExceeded, PromotionManagementDto? promotion)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), promotion)
    {
    }

    public PromotionManagementDto? Promotion { get; }

    public static PromotionManagementCommandResponseDto Success(Guid id, string? message, PromotionManagementDto? promotion) =>
        new(BaseCommandResponse.Success(id, message), promotion);

    public static PromotionManagementCommandResponseDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}

public sealed record PromotionCodeIssuedCommandResponseDto : BaseCommandResponse<Guid>
{
    private PromotionCodeIssuedCommandResponseDto(BaseCommandResponse<Guid> state, PromotionManagementDto? promotion, string? issuedCode) : base(state, true)
    {
        Promotion = promotion;
        IssuedCode = issuedCode;
    }

    [JsonConstructor]
    internal PromotionCodeIssuedCommandResponseDto(Guid id, bool isSuccess, string? message, IReadOnlyList<string>? errors, string? failureCode, QuotaExceededDetails? quotaExceeded, PromotionManagementDto? promotion, string? issuedCode)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), promotion, issuedCode)
    {
    }

    public PromotionManagementDto? Promotion { get; }
    public string? IssuedCode { get; }

    public static PromotionCodeIssuedCommandResponseDto Success(Guid id, string? message, PromotionManagementDto? promotion, string? issuedCode) =>
        new(BaseCommandResponse.Success(id, message), promotion, issuedCode);

    public static PromotionCodeIssuedCommandResponseDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null, null);
}

public sealed record PromotionManagementDto
{
    public Guid EventId { get; set; }

    public Guid TicketCatalogVersionId { get; set; }

    public int TicketCatalogVersionNumber { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public Guid DefinitionId { get; set; }

    public Guid DefinitionGroupId { get; set; }

    public int VersionNumber { get; set; }

    public int StatusId { get; set; }

    public string StatusCode { get; set; } = string.Empty;

    public string StatusName { get; set; } = string.Empty;

    public string DisplayLabel { get; set; } = string.Empty;

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public int? TotalRedemptionLimit { get; set; }

    public int? PerVerifiedPurchaserLimit { get; set; }

    public string DiscountKind { get; set; } = string.Empty;

    public long? FixedDiscountMinor { get; set; }

    public int? BasisPointDiscount { get; set; }

    public long? MaximumDiscountMinor { get; set; }

    public bool IncludesAllTickets { get; set; }

    public IReadOnlyList<Guid> EligibleTicketTypeIds { get; set; } = [];

    public string? PromotionCodeDisplayLabel { get; set; }

    [JsonIgnore]
    public Guid TenantId { get; set; }

    [JsonIgnore]
    public Guid? ActorId { get; set; }

    [JsonIgnore]
    public Guid? ActorUserId { get; set; }

    [JsonIgnore]
    public Guid? ActorOrganizationId { get; set; }

    [JsonIgnore]
    public Guid? ActorGroupId { get; set; }

    [JsonIgnore]
    public Guid? OrganizerActorId { get; set; }

    [JsonIgnore]
    public Guid? OrganizerUserId { get; set; }

    [JsonIgnore]
    public Guid? OrganizerOrganizationId { get; set; }

    [JsonIgnore]
    public Guid? OrganizerGroupId { get; set; }
}
