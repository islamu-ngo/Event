using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Services.Registration;

/// <summary>
/// Specifies comparison of supplied acceptance facts. Persisted authority admission
/// is covered separately by PaidOrderAcceptanceFreshnessNativeAuthenticationTests.
/// </summary>
public sealed class PaidOrderAcceptanceFreshnessServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid EventId = Guid.CreateVersion7();
    private static readonly Guid OrganizerId = Guid.CreateVersion7();
    private static readonly Guid ConnectionId = Guid.CreateVersion7();
    private static readonly Guid OperatorId = Guid.CreateVersion7();
    private static readonly Guid DirectoryDocumentId = Guid.CreateVersion7();
    private static readonly Guid DirectoryRevision = Guid.CreateVersion7();
    private static readonly Guid InstancePolicyId = Guid.CreateVersion7();
    private static readonly Guid TenantPolicyId = Guid.CreateVersion7();
    private static readonly Guid LineId = Guid.CreateVersion7();
    private static readonly Guid Composition = Guid.CreateVersion7();

    [Test]
    public async Task CopiedRevisionCannotAuthorizeFabricatedMerchantDisclosure()
    {
        var disclosure = Disclosure();
        var subject = CreateSubject(disclosure with
        {
            OrganizerMerchant = disclosure.OrganizerMerchant with { MerchantDisclosureText = "Fabricated merchant" }
        });
        await Assert.That(await subject.Service.IsCurrentAsync(subject.Attempt, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task EveryServerAuthoredAcceptanceFactMustRemainCurrent()
    {
        Func<PaidOrderAcceptanceDisclosureDto, PaidOrderAcceptanceDisclosureDto>[] changes =
        [
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { OrganizerActorId = Guid.CreateVersion7() } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { OrganizerPaymentProviderConnectionId = Guid.CreateVersion7() } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { ConnectPlatformId = "platform-live-us" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { ExternalAccountId = "acct_changed" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { MerchantCountryCode = "FR" } },
            d => d with { AcceptanceTemplateIdentifier = "paid-order-acceptance.v2" },
            d => d with { AcceptanceTemplateText = "Changed template" },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { DocumentId = Guid.CreateVersion7() } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { RevisionId = Guid.CreateVersion7() } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { PublicName = "Changed directory" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { LegalName = "Changed Directory ASBL" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { OperatorKindCode = "public_body" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { JurisdictionCountryCode = "FR" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { RegistrationIdentifier = "FR 123" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { PublicContactEmail = "changed@example.test" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { LegalNoticeUrl = "https://changed.example.test/legal" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { TermsUrl = "https://changed.example.test/terms" } },
            d => d with { TenantDirectoryOperator = d.TenantDirectoryOperator with { PrivacyUrl = "https://changed.example.test/privacy" } },
            d => d with { InstanceOperator = d.InstanceOperator with { LegalName = "Changed Operator ASBL" } },
            d => d with { InstanceOperator = d.InstanceOperator with { OperatorKindCode = "public_body" } },
            d => d with { InstanceOperator = d.InstanceOperator with { RegistrationIdentifier = "FR 123" } },
            d => d with { CurrencyCode = "USD" },
            d => d with { OrganizerAmountMinor = 999 },
            d => d with { PlatformFeeMinor = 74 },
            d => d with { PlatformContributionMinor = 124 },
            d => d with { TotalMinor = 1_124 },
            d => d with { DisclosureRevision = "changed-disclosure" },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { MerchantDisclosureText = "Changed merchant" } },
            d => d with { InstanceOperator = d.InstanceOperator with { PublicName = "Changed operator" } },
            d => d with { InstanceOperator = d.InstanceOperator with { IsOfficialInstance = true } },
            d => d with { InstanceOperator = d.InstanceOperator with { OfficialOrigin = "https://changed.example.test" } },
            d => d with { InstanceOperator = d.InstanceOperator with { JurisdictionCountryCode = "FR" } },
            d => d with { InstanceOperator = d.InstanceOperator with { WebsiteUrl = "https://changed.example.test" } },
            d => d with { InstanceOperator = d.InstanceOperator with { LegalNoticeUrl = "https://changed.example.test/legal" } },
            d => d with { InstanceOperator = d.InstanceOperator with { TermsUrl = "https://changed.example.test/terms" } },
            d => d with { InstanceOperator = d.InstanceOperator with { PrivacyUrl = "https://changed.example.test/privacy" } },
            d => d with { PaymentOperations = d.PaymentOperations with { ActivationStatus = "suspended" } },
            d => d with { DeliveryStartsAtUtc = new DateTimeOffset(UtcNow.AddDays(11)) },
            d => d with { DeliveryEndsAtUtc = new DateTimeOffset(UtcNow.AddDays(11).AddHours(3)) },
            d => d with { EventTimeZoneId = "Europe/Paris" },
            d => d with { RefundPolicyVersion = 2 },
            d => d with { RefundPolicyText = "Changed refund policy" },
            d => d with { RefundPolicyLanguageTag = "fr-FR" },
            d => d with { SupportContact = "changed-support@example.test" },
            d => d with { PaymentOperations = d.PaymentOperations with { ComplaintContact = "changed-complaints@example.test" } },
            d => d with { PaymentOperations = d.PaymentOperations with { ComplaintOwner = "Changed complaints owner" } },
            d => d with { PaymentOperations = d.PaymentOperations with { RefundOwner = "Changed refund owner" } },
            d => d with { PaymentOperations = d.PaymentOperations with { DisputeOwner = "Changed dispute owner" } },
            d => d with { PaymentOperations = d.PaymentOperations with { ReconciliationOwner = "Changed reconciliation owner" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { ProviderCode = "adyen" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { ProviderProfileCode = "platform-live-eu" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { ProviderEnvironment = "live" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { ProviderCredentialOwner = "tenant" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { ChargeType = "destination-charge" } },
            d => d with { OrganizerMerchant = d.OrganizerMerchant with { StatementDescriptor = "CHANGED EVENT" } }
        ];
        foreach (var change in changes)
        {
            var subject = CreateSubject(change(Disclosure()));
            await Assert.That(await subject.Service.IsCurrentAsync(subject.Attempt, CancellationToken.None)).IsFalse();
        }

        PaidOrderAcceptanceAuthorityFacts authority = Authority();
        PaidOrderAcceptanceAuthorityFacts[] authorities =
        [
            authority with { OrganizerActorId = Guid.CreateVersion7() },
            authority with { OperatorId = Guid.CreateVersion7() },
            authority with { TenantDirectoryOperatorDocumentId = Guid.CreateVersion7() },
            authority with { TenantDirectoryOperatorRevisionId = Guid.CreateVersion7() },
            authority with { InstancePolicyVersionId = Guid.CreateVersion7() },
            authority with { TenantPolicyVersionId = Guid.CreateVersion7() }
        ];
        foreach (var changed in authorities)
        {
            var subject = CreateSubject(Disclosure(), changed);
            await Assert.That(await subject.Service.IsCurrentAsync(subject.Attempt, CancellationToken.None)).IsFalse();
        }
        var changedComposition = CreateSubject(Disclosure());
        changedComposition.Order.ConcurrencyStamp = Guid.CreateVersion7();
        await Assert.That(await changedComposition.Service.IsCurrentAsync(changedComposition.Attempt, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task ExactSnapshotAndLineFactsRemainCurrent()
    {
        var subject = CreateSubject(Disclosure());
        await Assert.That(subject.Attempt.OrganizerAmountMinor).IsEqualTo(1_000L);
        await Assert.That(subject.Attempt.PlatformFeeMinor).IsEqualTo(75L);
        await Assert.That(subject.Attempt.PlatformContributionMinor).IsEqualTo(125L);
        await Assert.That(subject.Attempt.TotalMinor).IsEqualTo(1_125L);
        await Assert.That(subject.Order.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
        await Assert.That(await subject.Service.IsCurrentAsync(subject.Attempt, CancellationToken.None)).IsTrue();
    }

    [Test]
    public async Task EveryAcceptedLineFactMustRemainCurrent()
    {
        PaidOrderAcceptanceLineDto line = Disclosure().Lines.Single();
        PaidOrderAcceptanceLineDto[] changes =
        [
            line with { OrderLineId = Guid.CreateVersion7() },
            line with { Name = "Changed admission" },
            line with { Quantity = 2, LineTotalMinor = 2_000 },
            line with { UnitAmountMinor = 999, LineTotalMinor = 999 },
            line with { DiscountAmountMinor = 1, LineTotalMinor = 999 },
            line with { UnitAmountMinor = 1_001, LineTotalMinor = 1_001 }
        ];
        foreach (var changed in changes)
        {
            var subject = CreateSubject(Disclosure() with { Lines = [changed] });
            await Assert.That(await subject.Service.IsCurrentAsync(subject.Attempt, CancellationToken.None)).IsFalse();
        }
    }

    [Test]
    public async Task MalformedAcceptedLineMoneyFailsClosed()
    {
        var disclosure = Disclosure();
        var subject = CreateSubject(disclosure with { Lines = [disclosure.Lines.Single() with { Quantity = 2 }] });
        await Assert.That(await subject.Service.IsCurrentAsync(subject.Attempt, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task MissingSnapshotOrderDisclosureOrAuthorityFailsClosed()
    {
        var missingSnapshot = CreateSubject(Disclosure(), attachSnapshot: false);
        await Assert.That(await missingSnapshot.Service.IsCurrentAsync(missingSnapshot.Attempt, CancellationToken.None)).IsFalse();
        var missingOrder = CreateSubject(Disclosure(), includeOrder: false);
        await Assert.That(await missingOrder.Service.IsCurrentAsync(missingOrder.Attempt, CancellationToken.None)).IsFalse();
        var missingDisclosure = CreateSubject(null);
        await Assert.That(await missingDisclosure.Service.IsCurrentAsync(missingDisclosure.Attempt, CancellationToken.None)).IsFalse();
        var missingAuthority = CreateSubject(Disclosure(), includeAuthority: false);
        await Assert.That(await missingAuthority.Service.IsCurrentAsync(missingAuthority.Attempt, CancellationToken.None)).IsFalse();
    }

    private static (RegistrationOrder Order, PaymentAttempt Attempt, PaidOrderAcceptanceFreshnessService Service) CreateSubject(
        PaidOrderAcceptanceDisclosureDto? disclosure,
        PaidOrderAcceptanceAuthorityFacts? authority = null,
        bool attachSnapshot = true,
        bool includeOrder = true,
        bool includeAuthority = true)
    {
        RegistrationOrder order = RegistrationOrder.Create(OrderId, TenantId, EventId, Guid.CreateVersion7(),
            Guid.CreateVersion7(), BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null, null, "EUR", UtcNow, UtcNow.AddMinutes(30));
        order.ConcurrencyStamp = Composition;
        var recipient = OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerId, ConnectionId, "stripe",
            "platform-live-eu", "acct_123", "BE", "EUR", InstancePolicyId, TenantPolicyId, UtcNow);
        PaymentAttempt attempt = PaymentAttempt.Create(Guid.CreateVersion7(), TenantId, OrderId, recipient,
            "OrganizerDirect", "2026-08-24", Composition.ToString("N"), Money.Create(1_000, "EUR"),
            Money.Create(75, "EUR"), Money.Create(125, "EUR"), "checkout:comparison", UtcNow, UtcNow.AddMinutes(30));
        if (attachSnapshot)
            attempt.AttachAcceptance(Snapshot());
        var orders = Substitute.For<IRegistrationInventoryRepository>();
        orders.GetOrderWithLinesAsync(OrderId, TenantId, Arg.Any<CancellationToken>()).Returns(includeOrder ? order : null);
        var acceptances = Substitute.For<IPaidOrderAcceptanceService>();
        acceptances.DescribeAsync(order, attempt.Id, Arg.Any<CancellationToken>()).Returns(
            new PaidOrderAcceptanceResult(disclosure, null, null, null, includeAuthority ? authority ?? Authority() : null));
        return (order, attempt, new PaidOrderAcceptanceFreshnessService(orders, acceptances));
    }

    private static PaidOrderAcceptanceAuthorityFacts Authority() =>
        new(OrganizerId, OperatorId, DirectoryDocumentId, DirectoryRevision, InstancePolicyId, TenantPolicyId);

    private static PaidOrderAcceptanceSnapshot Snapshot() => PaidOrderAcceptanceSnapshot.Create(
        Guid.CreateVersion7(), TenantId, TenantId, OrderId, EventId, Composition.ToString("N"), "disclosure",
        PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier, PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
        OrganizerId, "Example Organizer, legal merchant for this order",
        PaidCheckoutTenantDirectoryOperatorDisclosure.Create(DirectoryDocumentId, DirectoryRevision,
            "Community Events", "Community Events ASBL", "registered_organization", "BE", "BE 0123.456.789",
            "contact@example.test", "https://example.test/legal", "https://example.test/terms", "https://example.test/privacy"),
        PaidCheckoutOperatorDisclosure.Create(OperatorId, "Independent Operator", false,
            "https://events.example.test", "BE", "https://events.example.test", "https://events.example.test/legal",
            "https://events.example.test/terms", "https://events.example.test/privacy", "complaints@example.test",
            "Trust and Safety", "Payments Operations", "Dispute Operations", "Payment Reconciliation", "approved",
            "Independent Operator ASBL", "registered_organization", "BE 0987.654.321"),
        PaidOrderDeliverySnapshot.Create(new DateTimeOffset(UtcNow.AddDays(10)),
            new DateTimeOffset(UtcNow.AddDays(10).AddHours(3)), "Europe/Brussels"),
        "EUR", 1_000, 75, 125, 1_125, InstancePolicyId, 1, "Refund policy", "en-GB", "support@example.test",
        PaidCheckoutProviderDisclosure.Create("stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
        [PaidOrderAcceptanceLineFact.Create(LineId, "Admission", 1, 1_000, 0, 1_000)],
        UtcNow, TenantPolicyId, ConnectionId, "platform-live-eu", "acct_123", "BE");

    private static PaidOrderAcceptanceDisclosureDto Disclosure() => new()
    {
        DisclosureRevision = "disclosure",
        AcceptanceTemplateIdentifier = PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
        AcceptanceTemplateText = PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
        OrganizerMerchant = new()
        {
            OrganizerActorId = OrganizerId, MerchantDisclosureText = "Example Organizer, legal merchant for this order",
            OrganizerPaymentProviderConnectionId = ConnectionId, ConnectPlatformId = "platform-live-eu",
            ExternalAccountId = "acct_123", MerchantCountryCode = "BE", ProviderCode = "stripe",
            ProviderProfileCode = "OrganizerDirect", ProviderEnvironment = "test", ProviderCredentialOwner = "instance-operator",
            ChargeType = "direct-charge", StatementDescriptor = "EXAMPLE EVENT"
        },
        TenantDirectoryOperator = new()
        {
            DocumentId = DirectoryDocumentId, RevisionId = DirectoryRevision, PublicName = "Community Events",
            LegalName = "Community Events ASBL", OperatorKindCode = "registered_organization", JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE 0123.456.789", PublicContactEmail = "contact@example.test",
            LegalNoticeUrl = "https://example.test/legal", TermsUrl = "https://example.test/terms", PrivacyUrl = "https://example.test/privacy"
        },
        InstanceOperator = new()
        {
            OperatorId = OperatorId, PublicName = "Independent Operator", LegalName = "Independent Operator ASBL",
            OperatorKindCode = "registered_organization", RegistrationIdentifier = "BE 0987.654.321",
            IsOfficialInstance = false, OfficialOrigin = "https://events.example.test", JurisdictionCountryCode = "BE",
            WebsiteUrl = "https://events.example.test/", LegalNoticeUrl = "https://events.example.test/legal",
            TermsUrl = "https://events.example.test/terms", PrivacyUrl = "https://events.example.test/privacy"
        },
        PaymentOperations = new()
        {
            ComplaintContact = "complaints@example.test", ComplaintOwner = "Trust and Safety", RefundOwner = "Payments Operations",
            DisputeOwner = "Dispute Operations", ReconciliationOwner = "Payment Reconciliation", ActivationStatus = "approved"
        },
        DeliveryStartsAtUtc = new DateTimeOffset(UtcNow.AddDays(10)),
        DeliveryEndsAtUtc = new DateTimeOffset(UtcNow.AddDays(10).AddHours(3)), EventTimeZoneId = "Europe/Brussels",
        CurrencyCode = "EUR", CurrencyMinorUnitDigits = 2, OrganizerAmountMinor = 1_000, PlatformFeeMinor = 75,
        PlatformContributionMinor = 125, TotalMinor = 1_125, RefundPolicyVersion = 1, RefundPolicyText = "Refund policy",
        RefundPolicyLanguageTag = "en-GB", SupportContact = "support@example.test",
        Lines = [new() { OrderLineId = LineId, Name = "Admission", Quantity = 1, UnitAmountMinor = 1_000, DiscountAmountMinor = 0, LineTotalMinor = 1_000 }]
    };
}
