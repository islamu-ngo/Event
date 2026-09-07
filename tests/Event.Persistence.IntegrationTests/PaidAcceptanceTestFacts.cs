using Explore.Domain;

namespace Event.Persistence.IntegrationTests;

internal static class PaidAcceptanceTestFacts
{
    internal static PaidOrderAcceptanceSnapshot Create(
        Guid tenantId,
        Guid orderId,
        Guid eventId,
        string compositionRevision,
        Guid instancePolicyVersionId,
        long organizerAmountMinor,
        long platformFeeMinor,
        long platformContributionMinor,
        DateTime acceptedAt,
        string currencyCode = "EUR",
        OrganizerPaymentRecipientSnapshot? recipient = null) => PaidOrderAcceptanceSnapshot.Create(
            recipient?.OrganizerActorId ?? Guid.CreateVersion7(),
            tenantId,
            tenantId,
            orderId,
            eventId,
            compositionRevision,
            $"disclosure:{compositionRevision}",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            Guid.CreateVersion7(),
            "Example Organizer is the legal merchant for this order.",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(),
                "Independent Example Operator",
                false,
                "https://events.example.test",
                "BE",
                "https://events.example.test",
                "https://events.example.test/legal",
                "https://events.example.test/terms",
                "https://events.example.test/privacy",
                "complaints@example.test",
                "Trust and Safety",
                "Refund Operations",
                "Dispute Operations",
                "Payment Reconciliation",
                "approved"),
            PaidOrderDeliverySnapshot.Create(
                new DateTimeOffset(acceptedAt.AddDays(10)),
                new DateTimeOffset(acceptedAt.AddDays(10).AddHours(3)),
                "Europe/Brussels"),
            currencyCode,
            organizerAmountMinor,
            platformFeeMinor,
            platformContributionMinor,
            checked(organizerAmountMinor + platformContributionMinor),
            recipient?.InstancePolicyVersionId ?? instancePolicyVersionId,
            1,
            "Refund policy",
            "en-GB",
            "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                recipient?.ProviderCode ?? "stripe",
                recipient?.ProfileCode ?? "OrganizerDirect",
                "direct-charge",
                "EXAMPLE EVENT",
                "test",
                "instance-operator"),
            [
                PaidOrderAcceptanceLineFact.Create(
                    Guid.CreateVersion7(),
                    "Admission",
                    1,
                    organizerAmountMinor,
                    0,
                    organizerAmountMinor)
            ],
            acceptedAt,
            tenantPolicyVersionId: recipient?.TenantPolicyVersionId,
            organizerPaymentProviderConnectionId:
                recipient?.OrganizerPaymentProviderConnectionId ?? Guid.CreateVersion7(),
            connectPlatformId: recipient?.ConnectPlatformId ?? "platform-live-eu",
            externalAccountId: recipient?.ExternalAccountId ?? "acct_123",
            merchantCountryCode: recipient?.MerchantCountryCode ?? "BE");
}
