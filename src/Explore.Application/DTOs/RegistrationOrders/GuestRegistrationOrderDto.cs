using System.Text.Json.Serialization;
using Explore.Application.Responses;

namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record GuestRegistrationOrderDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public int StatusId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
    public long PreDiscountOrganizerDirectedTotalMinor { get; init; }
    public long PromotionDiscountTotalMinor { get; init; }
    public long PostDiscountOrganizerDirectedTotalMinor { get; init; }
    public string? AppliedPromotionDisplayLabel { get; init; }
    public long OrganizerDirectedTotalMinor { get; init; }
    public long PlatformFeeTotalMinor { get; init; }
    public long OrganizerEarningsTotalMinor { get; init; }
    public long PlatformContributionTotalMinor { get; init; }
    public long TotalDueMinor { get; init; }
    public RegistrationOrderPlatformContributionDto? PlatformContribution { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public DateTime? RejectedAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public IReadOnlyList<RegistrationOrderLineDto> Lines { get; init; } = [];
    [JsonIgnore]
    public bool PaidCheckoutActivationAvailable { get; init; }

    public static GuestRegistrationOrderDto From(RegistrationOrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new GuestRegistrationOrderDto
        {
            Id = order.Id,
            EventId = order.EventId,
            StatusId = order.StatusId,
            StatusCode = order.StatusCode,
            StatusName = order.StatusName,
            CurrencyCode = order.CurrencyCode,
            PreDiscountOrganizerDirectedTotalMinor = order.PreDiscountOrganizerDirectedTotalMinor,
            PromotionDiscountTotalMinor = order.PromotionDiscountTotalMinor,
            PostDiscountOrganizerDirectedTotalMinor = order.PostDiscountOrganizerDirectedTotalMinor,
            AppliedPromotionDisplayLabel = order.AppliedPromotionDisplayLabel,
            OrganizerDirectedTotalMinor = order.OrganizerDirectedTotalMinor,
            PlatformFeeTotalMinor = order.PlatformFeeTotalMinor,
            OrganizerEarningsTotalMinor = order.OrganizerEarningsTotalMinor,
            PlatformContributionTotalMinor = order.PlatformContributionTotalMinor,
            TotalDueMinor = order.TotalDueMinor,
            PlatformContribution = order.PlatformContribution,
            ExpiresAt = order.ExpiresAt,
            SubmittedAt = order.SubmittedAt,
            ConfirmedAt = order.ConfirmedAt,
            RejectedAt = order.RejectedAt,
            CancelledAt = order.CancelledAt,
            Lines = order.Lines,
            PaidCheckoutActivationAvailable = order.PaidCheckoutActivationAvailable
        };
    }
}

public sealed record GuestRegistrationOrderLifecycleResponseDto : BaseCommandResponse<Guid>
{
    private GuestRegistrationOrderLifecycleResponseDto(
        BaseCommandResponse<Guid> state,
        GuestRegistrationOrderDto? order) : base(state, true)
    {
        Order = order;
    }

    [JsonConstructor]
    internal GuestRegistrationOrderLifecycleResponseDto(
        Guid id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded,
        GuestRegistrationOrderDto? order)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), order)
    {
    }

    public GuestRegistrationOrderDto? Order { get; }

    public static GuestRegistrationOrderLifecycleResponseDto Success(
        Guid id,
        string? message,
        GuestRegistrationOrderDto? order) =>
        new(BaseCommandResponse.Success(id, message), order);

    public static GuestRegistrationOrderLifecycleResponseDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);

    internal static GuestRegistrationOrderLifecycleResponseDto From(RegistrationOrderLifecycleResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new GuestRegistrationOrderLifecycleResponseDto(
            response,
            response.Order is null ? null : GuestRegistrationOrderDto.From(response.Order));
    }
}
