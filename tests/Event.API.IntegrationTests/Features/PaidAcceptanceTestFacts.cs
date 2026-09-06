// ABOUTME: Builds complete typed paid-acceptance evidence for API payment and scheduler fixtures.
// ABOUTME: Avoids fabricated prose milestones and opaque line JSON in integration setup.

using Explore.Domain;

namespace Event.Api.IntegrationTests.Features;

internal static class PaidAcceptanceTestFacts
{
    internal static PaidOrderAcceptanceSnapshot Create(
        OrganizerPaymentRecipientSnapshot recipient,
        Guid orderId,
        Guid eventId,
        string compositionRevision,
        long organizerAmountMinor,
        long platformFeeMinor,
        long platformContributionMinor,
        DateTime acceptedAt) => PaidOrderAcceptanceSnapshot.Create(
            Guid.CreateVersion7(), recipient.TenantId, recipient.TenantId, orderId, eventId, compositionRevision, "disclosure",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            recipient.OrganizerActorId,
            "Example Organizer, legal merchant for this order",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(), "Independent Operator", false, "https://events.example.test", "BE",
                "https://events.example.test", "https://events.example.test/legal", "https://events.example.test/terms",
                "https://events.example.test/privacy", "complaints@example.test", "Trust and Safety",
                "Payments Operations", "Dispute Operations", "Payment Reconciliation", "approved"),
            PaidOrderDeliverySnapshot.Create(
                new DateTimeOffset(acceptedAt.AddDays(10)), new DateTimeOffset(acceptedAt.AddDays(10).AddHours(3)),
                "Europe/Brussels"),
            recipient.CurrencyCode, organizerAmountMinor, platformFeeMinor, platformContributionMinor,
            checked(organizerAmountMinor + platformContributionMinor), recipient.InstancePolicyVersionId, 1,
            "Refund policy", "en-GB", "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                recipient.ProviderCode, recipient.ProfileCode, "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
            [PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Admission", 1, organizerAmountMinor, 0, organizerAmountMinor)],
            acceptedAt, recipient.TenantPolicyVersionId, recipient.OrganizerPaymentProviderConnectionId,
            recipient.ConnectPlatformId, recipient.ExternalAccountId, recipient.MerchantCountryCode);
}
