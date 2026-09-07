using System.Text.Json.Serialization;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record RegistrationOrderDto
{
    public Guid Id { get; init; }
    [JsonIgnore]
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    [JsonIgnore]
    public Guid? AccountUserId { get; init; }
    [JsonIgnore]
    public Guid? PurchaserActorId { get; init; }
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

    public static RegistrationOrderDto From(
        RegistrationOrder order,
        RegistrationOrderStatusEnum? statusOverride = null,
        PlatformContributionSetting? contributionSetting = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        RegistrationOrderStatusEnum status = statusOverride ?? (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
        return new RegistrationOrderDto
        {
            Id = order.Id,
            TenantId = order.TenantId,
            EventId = order.EventId,
            AccountUserId = order.AccountUserId,
            PurchaserActorId = order.PurchaserActorId,
            StatusId = (int)status,
            StatusCode = ToCode(status),
            StatusName = ToName(status),
            CurrencyCode = order.CurrencyCode,
            PreDiscountOrganizerDirectedTotalMinor = order.PreDiscountOrganizerDirectedTotalMinorSnapshot,
            PromotionDiscountTotalMinor = order.PromotionDiscountTotalMinorSnapshot,
            PostDiscountOrganizerDirectedTotalMinor = order.PostDiscountOrganizerDirectedTotalMinorSnapshot,
            AppliedPromotionDisplayLabel = order.AppliedPromotionDisplayLabelSnapshot,
            OrganizerDirectedTotalMinor = order.OrganizerDirectedTotalMinorSnapshot,
            PlatformFeeTotalMinor = order.PlatformFeeTotalMinorSnapshot,
            OrganizerEarningsTotalMinor = order.OrganizerEarningsTotalMinorSnapshot,
            PlatformContributionTotalMinor = order.PlatformContributionTotalMinorSnapshot,
            TotalDueMinor = order.TotalDueMinorSnapshot,
            PlatformContribution = RegistrationOrderPlatformContributionDto.From(order, contributionSetting),
            ExpiresAt = order.ExpiresAt,
            SubmittedAt = order.SubmittedAt,
            ConfirmedAt = order.ConfirmedAt,
            RejectedAt = order.RejectedAt,
            CancelledAt = order.CancelledAt,
            Lines = order.Lines.Select(RegistrationOrderLineDto.From).ToArray()
        };
    }

    private static string ToCode(RegistrationOrderStatusEnum status) => status switch
    {
        RegistrationOrderStatusEnum.Draft => "DRAFT",
        RegistrationOrderStatusEnum.AwaitingIdentity => "AWAITING_IDENTITY",
        RegistrationOrderStatusEnum.AwaitingParticipantDetails => "AWAITING_PARTICIPANT_DETAILS",
        RegistrationOrderStatusEnum.AwaitingRequirements => "AWAITING_REQUIREMENTS",
        RegistrationOrderStatusEnum.ReadyForCheckout => "READY_FOR_CHECKOUT",
        RegistrationOrderStatusEnum.AwaitingPayment => "AWAITING_PAYMENT",
        RegistrationOrderStatusEnum.AwaitingApproval => "AWAITING_APPROVAL",
        RegistrationOrderStatusEnum.Waitlisted => "WAITLISTED",
        RegistrationOrderStatusEnum.Confirmed => "CONFIRMED",
        RegistrationOrderStatusEnum.Rejected => "REJECTED",
        RegistrationOrderStatusEnum.Expired => "EXPIRED",
        RegistrationOrderStatusEnum.Cancelled => "CANCELLED",
        RegistrationOrderStatusEnum.NeedsReconciliation => "NEEDS_RECONCILIATION",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string ToName(RegistrationOrderStatusEnum status) => status switch
    {
        RegistrationOrderStatusEnum.Draft => "Draft",
        RegistrationOrderStatusEnum.AwaitingIdentity => "Awaiting identity",
        RegistrationOrderStatusEnum.AwaitingParticipantDetails => "Awaiting participant details",
        RegistrationOrderStatusEnum.AwaitingRequirements => "Awaiting requirements",
        RegistrationOrderStatusEnum.ReadyForCheckout => "Ready for checkout",
        RegistrationOrderStatusEnum.AwaitingPayment => "Awaiting payment",
        RegistrationOrderStatusEnum.AwaitingApproval => "Awaiting approval",
        RegistrationOrderStatusEnum.Waitlisted => "Waitlisted",
        RegistrationOrderStatusEnum.Confirmed => "Confirmed",
        RegistrationOrderStatusEnum.Rejected => "Rejected",
        RegistrationOrderStatusEnum.Expired => "Expired",
        RegistrationOrderStatusEnum.Cancelled => "Cancelled",
        RegistrationOrderStatusEnum.NeedsReconciliation => "Needs reconciliation",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}

public sealed record RegistrationOrderPlatformContributionDto
{
    public string Heading { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public int SelectedBasisPoints { get; init; }
    public long SelectedAmountMinor { get; init; }
    public IReadOnlyList<RegistrationOrderPlatformContributionOptionDto> Options { get; init; } = [];

    public static RegistrationOrderPlatformContributionDto? From(
        RegistrationOrder order,
        PlatformContributionSetting? setting)
    {
        if (setting is not { IsEnabled: true } || order.OrganizerDirectedTotalMinorSnapshot == 0)
        {
            return null;
        }

        return new RegistrationOrderPlatformContributionDto
        {
            Heading = setting.Heading,
            Body = setting.Body,
            SelectedBasisPoints = order.PlatformContribution?.ContributionBasisPointsSnapshot ?? 0,
            SelectedAmountMinor = order.PlatformContribution?.AmountMinor ?? 0,
            Options = setting.Options
                .OrderBy(option => option.SortOrder)
                .Select(option => new RegistrationOrderPlatformContributionOptionDto
                {
                    ContributionBasisPoints = option.ContributionBasisPoints,
                    AmountMinor = option.CalculateAmountMinor(order.OrganizerDirectedTotalMinorSnapshot),
                    IsDefault = option.IsDefault
                })
                .ToArray()
        };
    }
}

public sealed record RegistrationOrderPlatformContributionOptionDto
{
    public int ContributionBasisPoints { get; init; }
    public long AmountMinor { get; init; }
    public bool IsDefault { get; init; }
}

public sealed record RegistrationOrderLineDto
{
    public Guid Id { get; init; }
    public Guid TicketTypeId { get; init; }
    public int Quantity { get; init; }
    public string TicketTypeName { get; init; } = string.Empty;
    public long UnitPriceMinor { get; init; }
    public long? ChosenUnitPriceMinor { get; init; }
    public long SubtotalMinor { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    public static RegistrationOrderLineDto From(RegistrationOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new RegistrationOrderLineDto
        {
            Id = line.Id,
            TicketTypeId = line.TicketTypeId,
            Quantity = line.Quantity,
            TicketTypeName = line.TicketTypeNameSnapshot,
            UnitPriceMinor = line.UnitPriceAmountSnapshot,
            ChosenUnitPriceMinor = line.ChosenUnitPriceAmountSnapshot,
            SubtotalMinor = line.LineSubtotalSnapshot,
            CurrencyCode = line.CurrencyCodeSnapshot
        };
    }
}
