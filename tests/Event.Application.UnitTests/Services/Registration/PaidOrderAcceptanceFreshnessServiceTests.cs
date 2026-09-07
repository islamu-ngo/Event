using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Services.Registration;

public sealed class PaidOrderAcceptanceFreshnessServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CopiedRevisionCannotAuthorizeFabricatedMerchantDisclosure()
    {
        (PaidOrderAcceptanceSnapshot snapshot, PaymentAttempt attempt, PaidOrderAcceptanceFreshnessService service, _) =
            CreateSubject();
        Set(snapshot, nameof(PaidOrderAcceptanceSnapshot.MerchantDisclosureText), "Fabricated merchant");

        bool current = await service.IsCurrentAsync(attempt, CancellationToken.None);

        await Assert.That(current).IsFalse();
    }

    [Test]
    public async Task EveryServerAuthoredAcceptanceFactMustRemainCurrent()
    {
        (PaidOrderAcceptanceSnapshot snapshot, PaymentAttempt attempt, PaidOrderAcceptanceFreshnessService service, _) =
            CreateSubject();
        (string PropertyName, object? ChangedValue)[] changes =
        [
            (nameof(PaidOrderAcceptanceSnapshot.OrganizerActorId), Guid.CreateVersion7()),
            (nameof(PaidOrderAcceptanceSnapshot.OrganizerPaymentProviderConnectionId), Guid.CreateVersion7()),
            (nameof(PaidOrderAcceptanceSnapshot.ConnectPlatformId), "platform-live-us"),
            (nameof(PaidOrderAcceptanceSnapshot.ExternalAccountId), "acct_changed"),
            (nameof(PaidOrderAcceptanceSnapshot.MerchantCountryCode), "FR"),
            (nameof(PaidOrderAcceptanceSnapshot.AcceptanceTemplateIdentifier), "paid-order-acceptance.v2"),
            (nameof(PaidOrderAcceptanceSnapshot.AcceptanceTemplateText), "Changed template"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorDocumentId), Guid.CreateVersion7()),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorRevisionId), Guid.CreateVersion7()),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorPublicName), "Changed directory"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorLegalName), "Changed Directory ASBL"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorKindCode), "public_body"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorCountryCode), "FR"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorRegistrationIdentifier), "FR 123"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorPublicContactEmail), "changed@example.test"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorLegalNoticeUrl), "https://changed.example.test/legal"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorTermsUrl), "https://changed.example.test/terms"),
            (nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorPrivacyUrl), "https://changed.example.test/privacy"),
            (nameof(PaidOrderAcceptanceSnapshot.InstancePolicyVersionId), Guid.CreateVersion7()),
            (nameof(PaidOrderAcceptanceSnapshot.TenantPolicyVersionId), Guid.CreateVersion7()),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorId), Guid.CreateVersion7()),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorLegalName), "Changed Operator ASBL"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorKindCode), "public_body"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorRegistrationIdentifier), "FR 123"),
            (nameof(PaidOrderAcceptanceSnapshot.CurrencyCode), "USD"),
            (nameof(PaidOrderAcceptanceSnapshot.OrganizerAmountMinor), 999L),
            (nameof(PaidOrderAcceptanceSnapshot.PlatformFeeMinor), 74L),
            (nameof(PaidOrderAcceptanceSnapshot.PlatformContributionMinor), 124L),
            (nameof(PaidOrderAcceptanceSnapshot.TotalMinor), 1_124L),
            (nameof(PaidOrderAcceptanceSnapshot.DisclosureRevision), "changed-disclosure"),
            (nameof(PaidOrderAcceptanceSnapshot.CompositionRevision), Guid.CreateVersion7().ToString("N")),
            (nameof(PaidOrderAcceptanceSnapshot.MerchantDisclosureText), "Changed merchant"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorDisplayName), "Changed operator"),
            (nameof(PaidOrderAcceptanceSnapshot.IsOfficialInstance), true),
            (nameof(PaidOrderAcceptanceSnapshot.OfficialOrigin), "https://changed.example.test"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorRegionCode), "FR"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorWebsiteUrl), "https://changed.example.test"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorLegalNoticeUrl), "https://changed.example.test/legal"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorTermsUrl), "https://changed.example.test/terms"),
            (nameof(PaidOrderAcceptanceSnapshot.OperatorPrivacyUrl), "https://changed.example.test/privacy"),
            (nameof(PaidOrderAcceptanceSnapshot.ActivationStatus), "suspended"),
            (nameof(PaidOrderAcceptanceSnapshot.DeliveryStartsAtUtc), new DateTimeOffset(UtcNow.AddDays(11))),
            (nameof(PaidOrderAcceptanceSnapshot.DeliveryEndsAtUtc), new DateTimeOffset(UtcNow.AddDays(11).AddHours(3))),
            (nameof(PaidOrderAcceptanceSnapshot.EventTimeZoneId), "Europe/Paris"),
            (nameof(PaidOrderAcceptanceSnapshot.RefundPolicyVersion), 2),
            (nameof(PaidOrderAcceptanceSnapshot.RefundPolicyText), "Changed refund policy"),
            (nameof(PaidOrderAcceptanceSnapshot.RefundPolicyLanguageTag), "fr-FR"),
            (nameof(PaidOrderAcceptanceSnapshot.SupportContact), "changed-support@example.test"),
            (nameof(PaidOrderAcceptanceSnapshot.ComplaintContact), "changed-complaints@example.test"),
            (nameof(PaidOrderAcceptanceSnapshot.ComplaintOwner), "Changed complaints owner"),
            (nameof(PaidOrderAcceptanceSnapshot.RefundOwner), "Changed refund owner"),
            (nameof(PaidOrderAcceptanceSnapshot.DisputeOwner), "Changed dispute owner"),
            (nameof(PaidOrderAcceptanceSnapshot.ReconciliationOwner), "Changed reconciliation owner"),
            (nameof(PaidOrderAcceptanceSnapshot.ProviderCode), "adyen"),
            (nameof(PaidOrderAcceptanceSnapshot.ProviderProfileCode), "platform-live-eu"),
            (nameof(PaidOrderAcceptanceSnapshot.ProviderEnvironment), "live"),
            (nameof(PaidOrderAcceptanceSnapshot.ProviderCredentialOwner), "tenant"),
            (nameof(PaidOrderAcceptanceSnapshot.ChargeType), "destination-charge"),
            (nameof(PaidOrderAcceptanceSnapshot.StatementDescriptor), "CHANGED EVENT")
        ];

        foreach ((string propertyName, object? changedValue) in changes)
        {
            object? originalValue = Get(snapshot, propertyName);
            Set(snapshot, propertyName, changedValue);

            bool current = await service.IsCurrentAsync(attempt, CancellationToken.None);

            await Assert.That(current).IsFalse();
            Set(snapshot, propertyName, originalValue);
        }
    }

    [Test]
    public async Task ExactSnapshotAndLineFactsRemainCurrent()
    {
        (_, PaymentAttempt attempt, PaidOrderAcceptanceFreshnessService service, _) = CreateSubject();

        bool current = await service.IsCurrentAsync(attempt, CancellationToken.None);

        await Assert.That(current).IsTrue();
    }

    [Test]
    public async Task EveryAcceptedLineFactMustRemainCurrent()
    {
        (_, PaymentAttempt attempt, PaidOrderAcceptanceFreshnessService service, var disclosure) = CreateSubject();
        PaidOrderAcceptanceLineDto line = disclosure.Lines.Single();
        Action[] changes =
        [
            () => Set(line, nameof(PaidOrderAcceptanceLineDto.OrderLineId), Guid.CreateVersion7()),
            () => Set(line, nameof(PaidOrderAcceptanceLineDto.Name), "Changed admission"),
            () =>
            {
                Set(line, nameof(PaidOrderAcceptanceLineDto.Quantity), 2);
                Set(line, nameof(PaidOrderAcceptanceLineDto.LineTotalMinor), 2_000L);
            },
            () =>
            {
                Set(line, nameof(PaidOrderAcceptanceLineDto.UnitAmountMinor), 999L);
                Set(line, nameof(PaidOrderAcceptanceLineDto.LineTotalMinor), 999L);
            },
            () =>
            {
                Set(line, nameof(PaidOrderAcceptanceLineDto.DiscountAmountMinor), 1L);
                Set(line, nameof(PaidOrderAcceptanceLineDto.LineTotalMinor), 999L);
            },
            () =>
            {
                Set(line, nameof(PaidOrderAcceptanceLineDto.UnitAmountMinor), 1_001L);
                Set(line, nameof(PaidOrderAcceptanceLineDto.LineTotalMinor), 1_001L);
            }
        ];

        foreach (Action change in changes)
        {
            change();

            bool current = await service.IsCurrentAsync(attempt, CancellationToken.None);

            await Assert.That(current).IsFalse();
            Set(line, nameof(PaidOrderAcceptanceLineDto.OrderLineId), attempt.AcceptanceSnapshot!.Lines.Single().OrderLineId);
            Set(line, nameof(PaidOrderAcceptanceLineDto.Name), attempt.AcceptanceSnapshot.Lines.Single().Name);
            Set(line, nameof(PaidOrderAcceptanceLineDto.Quantity), attempt.AcceptanceSnapshot.Lines.Single().Quantity);
            Set(line, nameof(PaidOrderAcceptanceLineDto.UnitAmountMinor), attempt.AcceptanceSnapshot.Lines.Single().UnitAmountMinor);
            Set(line, nameof(PaidOrderAcceptanceLineDto.DiscountAmountMinor), attempt.AcceptanceSnapshot.Lines.Single().DiscountAmountMinor);
            Set(line, nameof(PaidOrderAcceptanceLineDto.LineTotalMinor), attempt.AcceptanceSnapshot.Lines.Single().LineTotalMinor);
        }
    }

    [Test]
    public async Task MalformedAcceptedLineMoneyFailsClosed()
    {
        (_, PaymentAttempt attempt, PaidOrderAcceptanceFreshnessService service, var disclosure) = CreateSubject();
        Set(disclosure.Lines.Single(), nameof(PaidOrderAcceptanceLineDto.Quantity), 2);

        bool current = await service.IsCurrentAsync(attempt, CancellationToken.None);

        await Assert.That(current).IsFalse();
    }

    [Test]
    public async Task MissingSnapshotOrderDisclosureOrAuthorityFailsClosed()
    {
        (PaidOrderAcceptanceSnapshot snapshot, PaymentAttempt attempt, _, _) = CreateSubject();
        PaymentAttempt missingSnapshotAttempt = PaymentAttempt.Create(
            Guid.CreateVersion7(),
            attempt.TenantId,
            attempt.RegistrationOrderId,
            attempt.RecipientSnapshot,
            "OrganizerDirect",
            "2026-08-24",
            attempt.CompositionRevision,
            Money.Create(attempt.OrganizerAmountMinor, attempt.CurrencyCode),
            Money.Create(attempt.PlatformFeeMinor, attempt.CurrencyCode),
            Money.Create(attempt.PlatformContributionMinor, attempt.CurrencyCode),
            "checkout:missing-snapshot",
            UtcNow,
            UtcNow.AddMinutes(30));
        var missingOrderRepository = Substitute.For<IRegistrationInventoryRepository>();
        var acceptanceService = Substitute.For<IPaidOrderAcceptanceService>();
        var missingOrderService = new PaidOrderAcceptanceFreshnessService(missingOrderRepository, acceptanceService);

        bool missingSnapshot = await missingOrderService.IsCurrentAsync(missingSnapshotAttempt, CancellationToken.None);
        bool missingOrder = await missingOrderService.IsCurrentAsync(attempt, CancellationToken.None);

        await Assert.That(missingSnapshot).IsFalse();
        await Assert.That(missingOrder).IsFalse();

        (RegistrationOrder order, PaymentAttempt configuredAttempt) = CreateOrderAndAttempt(snapshot);
        missingOrderRepository.GetOrderWithLinesAsync(
            order.Id, order.TenantId, Arg.Any<CancellationToken>()).Returns(order);
        acceptanceService.DescribeAsync(order, configuredAttempt.Id, Arg.Any<CancellationToken>())
            .Returns(
                new PaidOrderAcceptanceResult(null, null, null, null, null),
                new PaidOrderAcceptanceResult(
                    PaidAcceptanceTestFacts.ToDisclosure(snapshot), null, null, null, null));

        bool missingDisclosure = await missingOrderService.IsCurrentAsync(configuredAttempt, CancellationToken.None);
        bool missingAuthority = await missingOrderService.IsCurrentAsync(configuredAttempt, CancellationToken.None);

        await Assert.That(missingDisclosure).IsFalse();
        await Assert.That(missingAuthority).IsFalse();
    }

    private static (
        PaidOrderAcceptanceSnapshot Snapshot,
        PaymentAttempt Attempt,
        PaidOrderAcceptanceFreshnessService Service,
        PaidOrderAcceptanceDisclosureDto Disclosure) CreateSubject()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid instancePolicyId = Guid.CreateVersion7();
        PaidOrderAcceptanceSnapshot snapshot = PaidAcceptanceTestFacts.Create(
            tenantId, orderId, eventId, Guid.Empty.ToString("N"), instancePolicyId, null, 1_000, 75, 125, UtcNow);
        (RegistrationOrder order, PaymentAttempt attempt) = CreateOrderAndAttempt(snapshot);
        typeof(RegistrationOrder).GetProperty(nameof(RegistrationOrder.ConcurrencyStamp))!
            .SetValue(order, Guid.Empty);
        var authoritativeDisclosure = PaidAcceptanceTestFacts.ToDisclosure(snapshot);
        var orders = Substitute.For<IRegistrationInventoryRepository>();
        orders.GetOrderWithLinesAsync(orderId, tenantId, Arg.Any<CancellationToken>()).Returns(order);
        var acceptances = Substitute.For<IPaidOrderAcceptanceService>();
        acceptances.DescribeAsync(order, attempt.Id, Arg.Any<CancellationToken>()).Returns(
            new PaidOrderAcceptanceResult(
                authoritativeDisclosure,
                null,
                null,
                null,
                new PaidOrderAcceptanceAuthorityFacts(
                    snapshot.OrganizerActorId,
                    snapshot.OperatorId,
                    snapshot.TenantDirectoryOperatorDocumentId,
                    snapshot.TenantDirectoryOperatorRevisionId,
                    instancePolicyId,
                    null)));
        return (snapshot, attempt, new PaidOrderAcceptanceFreshnessService(orders, acceptances), authoritativeDisclosure);
    }

    private static (RegistrationOrder Order, PaymentAttempt Attempt) CreateOrderAndAttempt(
        PaidOrderAcceptanceSnapshot snapshot)
    {
        RegistrationOrder order = RegistrationOrder.Create(
            snapshot.RegistrationOrderId,
            snapshot.TenantId,
            snapshot.EventId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(),
                1,
                1,
                1,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null,
            null,
            snapshot.CurrencyCode,
            UtcNow,
            UtcNow.AddMinutes(30));
        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(),
            snapshot.TenantId,
            snapshot.RegistrationOrderId,
            OrganizerPaymentRecipientSnapshot.Create(
                snapshot.TenantId,
                snapshot.OrganizerActorId,
                snapshot.OrganizerPaymentProviderConnectionId,
                "stripe",
                snapshot.ConnectPlatformId,
                snapshot.ExternalAccountId,
                snapshot.MerchantCountryCode,
                "EUR",
                snapshot.InstancePolicyVersionId,
                snapshot.TenantPolicyVersionId,
                UtcNow),
            "OrganizerDirect",
            "2026-08-24",
            snapshot.CompositionRevision,
            Money.Create(snapshot.OrganizerAmountMinor, snapshot.CurrencyCode),
            Money.Create(snapshot.PlatformFeeMinor, snapshot.CurrencyCode),
            Money.Create(snapshot.PlatformContributionMinor, snapshot.CurrencyCode),
            "checkout:fabricated",
            UtcNow,
            UtcNow.AddMinutes(30));
        attempt.AttachAcceptance(snapshot);
        return (order, attempt);
    }

    private static object? Get(object target, string propertyName) =>
        target.GetType().GetProperty(propertyName)!.GetValue(target);

    private static void Set(object target, string propertyName, object? value) =>
        target.GetType().GetProperty(propertyName)!.SetValue(target, value);
}
