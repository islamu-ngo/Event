using Explore.Application.DTOs.RegistrationOrders;

namespace ApplicationUnitTests.Features.RegistrationOrders;

public sealed class RegistrationPaymentDtoPrivacyTests
{
    [Test]
    public async Task PaymentProjectionContainsOnlyApprovedFields()
    {
        string[] fields = typeof(RegistrationPaymentDto).GetProperties().Select(property => property.Name).Order().ToArray();

        await Assert.That(fields).IsEquivalentTo(new[]
        {
            "CreatedAt", "ExpiresAt", "FailureCode", "HostedRedirectAvailable", "Id", "LastUpdatedAt",
            "RegistrationOrderId", "RetryAvailable", "StatusCode", "StatusName", "RefundedAmountMinor",
            "RefundPendingAmountMinor", "Refunds", "Disputes", "BuyerRefundRequestAvailable", "OrganizerRefundAvailable",
            "CapturedAmountMinor", "CurrencyCode", "CurrencyMinorUnitDigits", "MaterialChangeChoices"
        });

        string[] refundFields = typeof(RegistrationRefundDto).GetProperties().Select(property => property.Name).Order().ToArray();
        await Assert.That(refundFields).IsEquivalentTo(new[]
        {
            "AcceptedRefundPolicyVersion", "AmountMinor", "CreatedAt", "CurrencyCode", "FailureCode", "Id",
            "LastObservedAt", "StatusCode", "StatusName", "SucceededAt"
        });

        string[] disputeFields = typeof(RegistrationPaymentDisputeDto).GetProperties().Select(property => property.Name).Order().ToArray();
        await Assert.That(disputeFields).IsEquivalentTo(new[]
        {
            "AmountMinor", "CurrencyCode", "Id", "LastObservedAt", "ResponseDueAt", "StageCode", "StatusCode"
        });

        string[] choiceFields = typeof(RegistrationMaterialChangeChoiceDto).GetProperties()
            .Select(property => property.Name).Order().ToArray();
        await Assert.That(choiceFields).IsEquivalentTo(new[]
        {
            "CampaignId", "CreatedAt", "DecidedAt", "Id", "StatusCode"
        });

        string[] campaignFields = typeof(RefundCampaignDto).GetProperties()
            .Select(property => property.Name).Order().ToArray();
        await Assert.That(campaignFields).IsEquivalentTo(new[]
        {
            "DecisionAt", "EventId", "FailedCount", "GeneratedCount", "Id", "KindCode", "OperatorCaseCount",
            "PendingCount", "StatusCode", "SucceededCount", "TotalPaymentCount", "UnknownCount"
        });
    }
}
