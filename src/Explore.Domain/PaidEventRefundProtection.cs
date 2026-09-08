namespace Explore.Domain;

public enum PaidEventRefundProtection
{
    OrganizerCancellationFullRefund = 1,
    MaterialChangeBuyerChoiceOrFullRefund = 2,
    DuplicateOrIncorrectChargeFullRefund = 3,
    SubstantialNonDeliveryRemedy = 4,
    AttendeeBuyerChangeTermsDisclosedSubjectToLaw = 5,
    CardDisputeRightsNotWaived = 6,
    CancelledEventPlatformAmountsRefundedByDefault = 7
}
