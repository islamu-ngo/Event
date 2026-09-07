using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain;

namespace Event.Application.UnitTests.Services.Registration;

internal static class PaidAcceptanceTestFacts
{
    internal static PaidOrderAcceptanceSnapshot Create(
        Guid tenantId,
        Guid orderId,
        Guid eventId,
        string compositionRevision,
        Guid instancePolicyVersionId,
        Guid? tenantPolicyVersionId,
        long organizerAmountMinor,
        long platformFeeMinor,
        long platformContributionMinor,
        DateTime acceptedAt,
        OrganizerPaymentRecipientSnapshot? recipient = null) => PaidOrderAcceptanceSnapshot.Create(
            Guid.CreateVersion7(), tenantId, tenantId, orderId, eventId, compositionRevision, "disclosure",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            recipient?.OrganizerActorId ?? Guid.CreateVersion7(),
            "Example Organizer, legal merchant for this order",
            DirectoryOperator(),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(), "Independent Operator", false, "https://events.example.test", "BE",
                "https://events.example.test", "https://events.example.test/legal", "https://events.example.test/terms",
                "https://events.example.test/privacy", "complaints@example.test", "Trust and Safety",
                "Payments Operations", "Dispute Operations", "Payment Reconciliation", "approved"),
            PaidOrderDeliverySnapshot.Create(
                new DateTimeOffset(acceptedAt.AddDays(10)), new DateTimeOffset(acceptedAt.AddDays(10).AddHours(3)),
                "Europe/Brussels"),
            "EUR", organizerAmountMinor, platformFeeMinor, platformContributionMinor,
            checked(organizerAmountMinor + platformContributionMinor), instancePolicyVersionId, 1,
            "Refund policy", "en-GB", "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                "stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
            [PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Admission", 1, organizerAmountMinor, 0, organizerAmountMinor)],
            acceptedAt, tenantPolicyVersionId,
            recipient?.OrganizerPaymentProviderConnectionId ?? Guid.CreateVersion7(),
            recipient?.ConnectPlatformId ?? "platform-live-eu",
            recipient?.ExternalAccountId ?? "acct_123",
            recipient?.MerchantCountryCode ?? "BE");

    internal static PaidOrderAcceptanceDisclosureDto ToDisclosure(PaidOrderAcceptanceSnapshot snapshot) => new()
    {
        DisclosureRevision = snapshot.DisclosureRevision,
        AcceptanceTemplateIdentifier = snapshot.AcceptanceTemplateIdentifier,
        AcceptanceTemplateText = snapshot.AcceptanceTemplateText,
        OrganizerMerchant = new PaidOrderAcceptanceOrganizerMerchantDto
        {
            OrganizerActorId = snapshot.OrganizerActorId,
            MerchantDisclosureText = snapshot.MerchantDisclosureText,
            ProviderCode = snapshot.ProviderCode,
            ProviderProfileCode = snapshot.ProviderProfileCode,
            ProviderEnvironment = snapshot.ProviderEnvironment,
            ProviderCredentialOwner = snapshot.ProviderCredentialOwner,
            ChargeType = snapshot.ChargeType,
            StatementDescriptor = snapshot.StatementDescriptor
            ,OrganizerPaymentProviderConnectionId = snapshot.OrganizerPaymentProviderConnectionId
            ,ConnectPlatformId = snapshot.ConnectPlatformId
            ,ExternalAccountId = snapshot.ExternalAccountId
            ,MerchantCountryCode = snapshot.MerchantCountryCode
        },
        TenantDirectoryOperator = new PaidOrderAcceptanceTenantDirectoryOperatorDto
        {
            DocumentId = snapshot.TenantDirectoryOperatorDocumentId,
            RevisionId = snapshot.TenantDirectoryOperatorRevisionId,
            PublicName = snapshot.TenantDirectoryOperatorPublicName,
            LegalName = snapshot.TenantDirectoryOperatorLegalName,
            OperatorKindCode = snapshot.TenantDirectoryOperatorKindCode,
            JurisdictionCountryCode = snapshot.TenantDirectoryOperatorCountryCode,
            RegistrationIdentifier = snapshot.TenantDirectoryOperatorRegistrationIdentifier,
            PublicContactEmail = snapshot.TenantDirectoryOperatorPublicContactEmail,
            LegalNoticeUrl = snapshot.TenantDirectoryOperatorLegalNoticeUrl,
            TermsUrl = snapshot.TenantDirectoryOperatorTermsUrl,
            PrivacyUrl = snapshot.TenantDirectoryOperatorPrivacyUrl
        },
        InstanceOperator = new PaidOrderAcceptanceInstanceOperatorDto
        {
            OperatorId = snapshot.OperatorId,
            PublicName = snapshot.OperatorDisplayName,
            LegalName = snapshot.OperatorLegalName,
            OperatorKindCode = snapshot.OperatorKindCode,
            RegistrationIdentifier = snapshot.OperatorRegistrationIdentifier,
            IsOfficialInstance = snapshot.IsOfficialInstance,
            OfficialOrigin = snapshot.OfficialOrigin,
            JurisdictionCountryCode = snapshot.OperatorRegionCode,
            WebsiteUrl = snapshot.OperatorWebsiteUrl,
            LegalNoticeUrl = snapshot.OperatorLegalNoticeUrl,
            TermsUrl = snapshot.OperatorTermsUrl,
            PrivacyUrl = snapshot.OperatorPrivacyUrl
        },
        PaymentOperations = new PaidOrderAcceptancePaymentOperationsDto
        {
            ComplaintContact = snapshot.ComplaintContact,
            ComplaintOwner = snapshot.ComplaintOwner,
            RefundOwner = snapshot.RefundOwner,
            DisputeOwner = snapshot.DisputeOwner,
            ReconciliationOwner = snapshot.ReconciliationOwner,
            ActivationStatus = snapshot.ActivationStatus
        },
        DeliveryStartsAtUtc = snapshot.DeliveryStartsAtUtc,
        DeliveryEndsAtUtc = snapshot.DeliveryEndsAtUtc,
        EventTimeZoneId = snapshot.EventTimeZoneId,
        CurrencyCode = snapshot.CurrencyCode,
        CurrencyMinorUnitDigits = 2,
        OrganizerAmountMinor = snapshot.OrganizerAmountMinor,
        PlatformFeeMinor = snapshot.PlatformFeeMinor,
        PlatformContributionMinor = snapshot.PlatformContributionMinor,
        TotalMinor = snapshot.TotalMinor,
        RefundPolicyVersion = snapshot.RefundPolicyVersion,
        RefundPolicyText = snapshot.RefundPolicyText,
        RefundPolicyLanguageTag = snapshot.RefundPolicyLanguageTag,
        SupportContact = snapshot.SupportContact,
        Lines = snapshot.Lines.Select(line => new PaidOrderAcceptanceLineDto
        {
            OrderLineId = line.OrderLineId,
            Name = line.Name,
            Quantity = line.Quantity,
            UnitAmountMinor = line.UnitAmountMinor,
            DiscountAmountMinor = line.DiscountAmountMinor,
            LineTotalMinor = line.LineTotalMinor
        }).ToArray()
    };

    private static PaidCheckoutTenantDirectoryOperatorDisclosure DirectoryOperator() =>
        PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
            "registered_organization", "BE", "BE 0123.456.789", "contact@example.test",
            "https://example.test/legal", "https://example.test/terms", "https://example.test/privacy");
}
