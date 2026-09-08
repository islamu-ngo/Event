// ABOUTME: Authorizes only pinned free anonymous orders with no exact paid or attendance history.
// ABOUTME: Keeps confirmed terminal in the generic lifecycle while defining a separate cancellation boundary.

using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public sealed record AnonymousCancellationEvidence(
    bool HasPaidAcceptance, bool HasPaymentAttempt, bool HasPaymentSuccessObservation, bool HasAttendance);

public static class AnonymousCancellationRules
{
    public static bool IsEligible(RegistrationOrder order, AnonymousCancellationEvidence evidence) =>
        order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Confirmed &&
        order.ConfirmedAt is not null && order.CancelledAt is null && IsFreeAnonymous(order, evidence);

    public static bool IsCancelled(RegistrationOrder order, AnonymousCancellationEvidence evidence) =>
        order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Cancelled &&
        order.ConfirmedAt is not null && order.CancelledAt >= order.ConfirmedAt && IsFreeAnonymous(order, evidence);

    private static bool IsFreeAnonymous(RegistrationOrder order, AnonymousCancellationEvidence evidence) =>
        !order.IsDeleted && order.AccountUserId is null && order.PurchaserActorId is null &&
        order.BookingPartyTypeId == (int)BookingPartyTypeEnum.Individual &&
        order.GuestAccessTokenHash is not null &&
        order.ParticipationSnapshot.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged &&
        order.ParticipationSnapshot.IdentityAccessModeId is
            (int)IdentityAccessModeEnum.GuestAllowed or (int)IdentityAccessModeEnum.CapabilityTokenAllowed &&
        !evidence.HasPaidAcceptance && !evidence.HasPaymentAttempt && !evidence.HasPaymentSuccessObservation &&
        !evidence.HasAttendance && order.Lines.Count > 0 &&
        order.Lines.All(line => line.TicketPricingModeSnapshot == (int)TicketPricingModeEnum.Free &&
            line.UnitPriceAmountSnapshot == 0 && line.ChosenUnitPriceAmountSnapshot is null &&
            line.LineSubtotalSnapshot == 0 && line.PreDiscountLineSubtotalMinorSnapshot == 0 &&
            line.PostDiscountLineSubtotalMinorSnapshot == 0 && line.PromotionDiscountAmountMinorSnapshot == 0) &&
        order.AddOnLines.All(line => line.LineTotalMinorSnapshot == 0) &&
        order.AddOnTotalMinorSnapshot == 0 && (order.PlatformContribution?.AmountMinor ?? 0) == 0 &&
        order.PreDiscountOrganizerDirectedTotalMinorSnapshot == 0 && order.PromotionDiscountTotalMinorSnapshot == 0 &&
        order.PostDiscountOrganizerDirectedTotalMinorSnapshot == 0 && order.OrganizerDirectedTotalMinorSnapshot == 0 &&
        order.PlatformFeeTotalMinorSnapshot == 0 && order.OrganizerEarningsTotalMinorSnapshot == 0 &&
        order.PlatformContributionTotalMinorSnapshot == 0 && order.TotalDueMinorSnapshot == 0;
}
