using Explore.Domain;

namespace Event.Application.UnitTests.Services.Registration;

internal static class RefundTestAcceptance
{
    internal static PaidOrderAcceptanceSnapshot Create(
        Guid tenantId,
        Guid orderId,
        long organizerAmountMinor,
        long platformFeeMinor,
        long platformContributionMinor,
        DateTime acceptedAt,
        OrganizerPaymentRecipientSnapshot? recipient = null) => PaidOrderAcceptanceSnapshot.Create(
        Guid.CreateVersion7(), tenantId, tenantId, orderId, Guid.CreateVersion7(), "composition-1", "disclosure-1",
        PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
        PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
        recipient?.OrganizerActorId ?? Guid.CreateVersion7(),
        "Example Organizer",
        PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
            "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
            "https://example.test/terms", "https://example.test/privacy"),
        PaidCheckoutOperatorDisclosure.Create(
            Guid.CreateVersion7(), "Example Operator", false, "https://events.example.test", "BE",
            "https://events.example.test", "https://events.example.test/legal", "https://events.example.test/terms",
            "https://events.example.test/privacy", "complaints@example.test", "Trust and Safety", "Payments Operations",
            "Dispute Operations", "Payment Reconciliation", "approved"),
        PaidOrderDeliverySnapshot.Create(
            DateTimeOffset.Parse("2026-09-10T17:00:00Z"), DateTimeOffset.Parse("2026-09-10T20:00:00Z"), "Europe/Brussels"),
        "EUR", organizerAmountMinor, platformFeeMinor, platformContributionMinor,
        checked(organizerAmountMinor + platformContributionMinor), Guid.CreateVersion7(), 7,
        "Refunds follow accepted policy v7.", "en-GB", "support@example.test",
        PaidCheckoutProviderDisclosure.Create(
            "stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
        [PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Admission", 1, organizerAmountMinor, 0, organizerAmountMinor)],
        acceptedAt, organizerPaymentProviderConnectionId:
            recipient?.OrganizerPaymentProviderConnectionId ?? Guid.CreateVersion7(),
        connectPlatformId: recipient?.ConnectPlatformId ?? "platform-live-eu",
        externalAccountId: recipient?.ExternalAccountId ?? "acct_123",
        merchantCountryCode: recipient?.MerchantCountryCode ?? "BE");
}
