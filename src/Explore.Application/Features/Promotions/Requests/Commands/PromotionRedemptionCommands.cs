using Explore.Application.Responses;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using MediatR;
using System.Text.Json.Serialization;

namespace Explore.Application.Features.Promotions.Requests.Commands;

public sealed record ApplyPromotionCodeToRegistrationOrderCommand(Guid OrderId, string Code)
    : IRequest<PromotionRedemptionResponseDto>;

public sealed record RemovePromotionFromRegistrationOrderCommand(Guid OrderId)
    : IRequest<PromotionRedemptionResponseDto>;

public sealed record ApplyGuestPromotionCodeToRegistrationOrderCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    string Code)
    : IRequest<PromotionRedemptionResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record RemoveGuestPromotionFromRegistrationOrderCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken)
    : IRequest<PromotionRedemptionResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand(
    Guid EventId,
    Guid OrderId,
    string Code)
    : IRequest<PromotionRedemptionResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record RemoveAuthenticatedPromotionFromRegistrationOrderCommand(Guid EventId, Guid OrderId)
    : IRequest<PromotionRedemptionResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record PromotionRedemptionResponseDto : BaseCommandResponse<Guid>
{
    private PromotionRedemptionResponseDto(
        BaseCommandResponse<Guid> state,
        string? appliedPromotionDisplayLabel,
        long promotionDiscountTotalMinor,
        long totalDueMinor,
        long platformFeeTotalMinor,
        long platformContributionTotalMinor) : base(state, true)
    {
        AppliedPromotionDisplayLabel = appliedPromotionDisplayLabel;
        PromotionDiscountTotalMinor = promotionDiscountTotalMinor;
        TotalDueMinor = totalDueMinor;
        PlatformFeeTotalMinor = platformFeeTotalMinor;
        PlatformContributionTotalMinor = platformContributionTotalMinor;
    }

    [JsonConstructor]
    internal PromotionRedemptionResponseDto(
        Guid id, bool isSuccess, string? message, IReadOnlyList<string>? errors, string? failureCode, QuotaExceededDetails? quotaExceeded,
        string? appliedPromotionDisplayLabel, long promotionDiscountTotalMinor, long totalDueMinor, long platformFeeTotalMinor, long platformContributionTotalMinor)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), appliedPromotionDisplayLabel, promotionDiscountTotalMinor, totalDueMinor, platformFeeTotalMinor, platformContributionTotalMinor)
    {
    }

    public string? AppliedPromotionDisplayLabel { get; }
    public long PromotionDiscountTotalMinor { get; }
    public long TotalDueMinor { get; }
    public long PlatformFeeTotalMinor { get; }
    public long PlatformContributionTotalMinor { get; }

    public static PromotionRedemptionResponseDto Success(
        Guid id,
        string? message,
        string? appliedPromotionDisplayLabel,
        long promotionDiscountTotalMinor,
        long totalDueMinor,
        long platformFeeTotalMinor,
        long platformContributionTotalMinor) =>
        new(BaseCommandResponse.Success(id, message), appliedPromotionDisplayLabel, promotionDiscountTotalMinor, totalDueMinor, platformFeeTotalMinor, platformContributionTotalMinor);

    public static PromotionRedemptionResponseDto Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null, 0, 0, 0, 0);
}

public static class PromotionRedemptionFailureCodes
{
    public const string Unavailable = "promotion_unavailable";
    public const string ValidationFailed = "promotion_request_invalid";
}
