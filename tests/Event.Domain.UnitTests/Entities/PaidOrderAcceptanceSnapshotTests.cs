using Explore.Domain;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class PaidOrderAcceptanceSnapshotTests
{
    private static readonly DateTime AcceptedAt = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    [Test]
    public async Task DirectoryOperatorDocumentOwnershipIsRequired()
    {
        Action action = () => PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
            Guid.Empty, Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
            "registered_organization", "BE", null, "contact@example.test",
            "https://example.test/legal", "https://example.test/terms", "https://example.test/privacy");

        await Assert.That(action).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreatePinsExactScheduleMerchantOperatorProviderOwnershipAndTypedLines()
    {
        PaidOrderAcceptanceSnapshot snapshot = Create();

        await Assert.That(snapshot.MerchantDisclosureText).IsEqualTo("Example Organizer, legal merchant for this order");
        await Assert.That(snapshot.Operator.OperatorDisplayName).IsEqualTo("Independent Example Operator");
        await Assert.That(snapshot.Operator.RegionCode).IsEqualTo("BE");
        await Assert.That(snapshot.Operator.LegalNoticeUrl).IsEqualTo("https://events.example.test/legal");
        await Assert.That(snapshot.Delivery.StartsAtUtc).IsEqualTo(DateTimeOffset.Parse("2026-09-10T17:00:00Z"));
        await Assert.That(snapshot.Delivery.EndsAtUtc).IsEqualTo(DateTimeOffset.Parse("2026-09-10T20:00:00Z"));
        await Assert.That(snapshot.Delivery.TimeZoneId).IsEqualTo("Europe/Brussels");
        await Assert.That(snapshot.Provider.Environment).IsEqualTo("test");
        await Assert.That(snapshot.Provider.CredentialOwner).IsEqualTo("instance-operator");
        await Assert.That(snapshot.Operator.ComplaintOwner).IsEqualTo("Trust and Safety");
        await Assert.That(snapshot.Operator.RefundOwner).IsEqualTo("Payments Operations");
        await Assert.That(snapshot.Operator.DisputeOwner).IsEqualTo("Dispute Operations");
        await Assert.That(snapshot.Operator.ReconciliationOwner).IsEqualTo("Payment Reconciliation");
        await Assert.That(snapshot.Operator.ActivationStatus).IsEqualTo("approved");
        await Assert.That(snapshot.InstanceOperator.LegalName).IsEqualTo("Independent Example Operator");
        await Assert.That(snapshot.PaymentOperations.ComplaintOwner).IsEqualTo("Trust and Safety");
        await Assert.That(snapshot.Lines.Count).IsEqualTo(1);
        await Assert.That(snapshot.Lines.Single().TenantId).IsEqualTo(TenantId);
        await Assert.That(snapshot.Lines.Single().LineTotalMinor).IsEqualTo(1_000);
    }

    [Test]
    public async Task CreateRejectsInvalidScheduleLineMoneyAndCrossTenantLineage()
    {
        await Assert.That(() => Create(snapshotTenantId: Guid.CreateVersion7())).Throws<ArgumentException>();
        await Assert.That(() => Create(delivery: PaidOrderDeliverySnapshot.Create(
            DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
            DateTimeOffset.Parse("2026-09-10T17:00:00Z"),
            "Europe/Brussels"))).Throws<ArgumentException>();
        await Assert.That(() => Create(lines:
        [
            PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Ticket", 0, 1_000, 0, 0)
        ])).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Create(lines:
        [
            PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Ticket", 1, 1_000, 1, 1_000)
        ])).Throws<ArgumentException>();
    }

    [Test]
    public async Task SnapshotEqualityIncludesTypedLineFacts()
    {
        PaidOrderAcceptanceSnapshot snapshot = Create();
        PaidOrderAcceptanceLineFact[] changedLines =
        [
            PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Ticket", 2, 500, 0, 1_000)
        ];

        await Assert.That(snapshot.MatchesLineFacts(snapshot.Lines.Select(PaidOrderAcceptanceLineFact.FromSnapshot))).IsTrue();
        await Assert.That(snapshot.MatchesLineFacts(changedLines)).IsFalse();
    }

    private static PaidOrderAcceptanceSnapshot Create(
        Guid? snapshotTenantId = null,
        PaidOrderDeliverySnapshot? delivery = null,
        IReadOnlyCollection<PaidOrderAcceptanceLineFact>? lines = null)
    {
        Guid orderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");
        return PaidOrderAcceptanceSnapshot.Create(
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000003"),
            snapshotTenantId ?? TenantId,
            TenantId,
            orderId,
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000004"),
            "composition-1",
            "disclosure-1",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000008"),
            "Example Organizer, legal merchant for this order",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000009"),
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000010"),
                "Community Events", "Community Events ASBL", "registered_organization", "BE",
                "BE 0123.456.789", "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000005"),
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
                "Payments Operations",
                "Dispute Operations",
                "Payment Reconciliation",
                "approved"),
            delivery ?? PaidOrderDeliverySnapshot.Create(
                DateTimeOffset.Parse("2026-09-10T17:00:00Z"),
                DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
                "Europe/Brussels"),
            "EUR",
            1_000,
            75,
            125,
            1_125,
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000006"),
            7,
            "Refunds follow policy v7.",
            "en-GB",
            "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                "stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
            lines ??
            [
                PaidOrderAcceptanceLineFact.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000007"), "Ticket", 1, 1_000, 0, 1_000)
            ],
            AcceptedAt,
            organizerPaymentProviderConnectionId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000011"),
            connectPlatformId: "platform-live-eu",
            externalAccountId: "acct_123",
            merchantCountryCode: "BE");
    }
}
