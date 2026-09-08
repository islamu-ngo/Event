using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventAddOnPersistenceTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task BuyerMaySelectZeroOneOrSeveralItemsWithLiteralIndependentTotals()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        RegistrationOrder emptyOrder = CreateOrder(tenantId, eventId);
        await Assert.That(emptyOrder.AddOnLines.Count)
            .IsEqualTo(0);
        await Assert.That(emptyOrder.AddOnTotalMinorSnapshot)
            .IsEqualTo(0L);

        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(
                tenantId,
                eventId,
                "EUR",
                1);
        EventAddOnCatalogItem lunch = CreateItem(
            catalog,
            "Lunch package",
            "One prepared meal.",
            1_200,
            50,
            "Collect at the event service desk.",
            "Unfulfilled quantities may be refunded under the accepted policy.");
        EventAddOnCatalogItem parking = CreateItem(
            catalog,
            "Parking pass",
            "One event-day parking allocation.",
            500,
            100,
            "Present the fulfillment receipt at the parking desk.",
            "Unfulfilled quantities may be refunded under the accepted policy.");
        catalog.AddItem(lunch);
        catalog.AddItem(parking);
        catalog.Publish(UtcNow);

        RegistrationOrder mixedOrder = CreateOrder(tenantId, eventId);
        mixedOrder.PinAddOnCatalog(catalog);
        RegistrationOrderAddOnLine lunchLine =
            RegistrationOrderAddOnLine.Create(
                Guid.CreateVersion7(),
                mixedOrder,
                catalog,
                lunch,
                quantity: 2);
        RegistrationOrderAddOnLine parkingLine =
            RegistrationOrderAddOnLine.Create(
                Guid.CreateVersion7(),
                mixedOrder,
                catalog,
                parking,
                quantity: 1);
        mixedOrder.AddAddOnLine(lunchLine);
        mixedOrder.AddAddOnLine(parkingLine);

        await Assert.That(mixedOrder.AddOnLines.Count)
            .IsEqualTo(2);
        await Assert.That(lunchLine.LineTotalMinorSnapshot)
            .IsEqualTo(2_400L);
        await Assert.That(parkingLine.LineTotalMinorSnapshot)
            .IsEqualTo(500L);
        await Assert.That(mixedOrder.AddOnTotalMinorSnapshot)
            .IsEqualTo(2_900L);
        await Assert.That(lunchLine.NameSnapshot)
            .IsEqualTo("Lunch package");
        await Assert.That(lunchLine.CurrencyCodeSnapshot)
            .IsEqualTo("EUR");
    }

    [Test]
    public async Task OverflowFailsBeforeOrderInventoryFulfillmentOrRefundEffects()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventAddOnCatalogVersion catalog =
            EventAddOnCatalogVersion.Create(
                tenantId,
                eventId,
                "EUR",
                1);
        EventAddOnCatalogItem item = CreateItem(
            catalog,
            "Overflow sentinel",
            null,
            long.MaxValue,
            2,
            "No fulfillment should be created.",
            "No refund should be created.");
        catalog.AddItem(item);
        catalog.Publish(UtcNow);
        RegistrationOrder order = CreateOrder(tenantId, eventId);

        await Assert.That(() =>
                RegistrationOrderAddOnLine.Create(
                    Guid.CreateVersion7(),
                    order,
                    catalog,
                    item,
                    quantity: 2))
            .Throws<OverflowException>();
        await Assert.That(order.AddOnLines.Count)
            .IsEqualTo(0);
        await Assert.That(order.AddOnTotalMinorSnapshot)
            .IsEqualTo(0L);
    }

    [Test]
    public async Task PersistenceModelEnforcesTenantLineageReplayAndAdmissionSeparation()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;
        (Type Type, string[][] UniqueSlots)[] expectations =
        [
            (typeof(EventAddOnCatalogVersion), [[nameof(EventAddOnCatalogVersion.TenantId), nameof(EventAddOnCatalogVersion.EventId), nameof(EventAddOnCatalogVersion.VersionNumber)]]),
            (typeof(EventAddOnCatalogItem), [[nameof(EventAddOnCatalogItem.TenantId), nameof(EventAddOnCatalogItem.EventAddOnCatalogVersionId), nameof(EventAddOnCatalogItem.Id)]]),
            (typeof(RegistrationOrderAddOnLine), [[nameof(RegistrationOrderAddOnLine.TenantId), nameof(RegistrationOrderAddOnLine.RegistrationOrderId), nameof(RegistrationOrderAddOnLine.EventAddOnCatalogItemId)]]),
            (typeof(EventAddOnInventoryAllocation),
                [
                    [nameof(EventAddOnInventoryAllocation.TenantId), nameof(EventAddOnInventoryAllocation.OperationId)],
                    [nameof(EventAddOnInventoryAllocation.TenantId), nameof(EventAddOnInventoryAllocation.RegistrationOrderAddOnLineId), nameof(EventAddOnInventoryAllocation.ActiveUniquenessSlot)],
                ]),
            (typeof(EventAddOnFulfillment),
                [
                    [nameof(EventAddOnFulfillment.TenantId), nameof(EventAddOnFulfillment.OperationId)],
                    [nameof(EventAddOnFulfillment.TenantId), nameof(EventAddOnFulfillment.RegistrationOrderAddOnLineId)],
                ]),
            (typeof(EventAddOnRefundAllocation), [[nameof(EventAddOnRefundAllocation.TenantId), nameof(EventAddOnRefundAllocation.RefundOperationId)]]),
        ];

        foreach ((Type type, string[][] uniqueSlots) in expectations)
        {
            IEntityType? entity = model.FindEntityType(type);
            await Assert.That(entity).IsNotNull()
                .Because($"{type.Name} must be a first-class tenant-scoped persistence concept");
            if (entity is null)
            {
                continue;
            }

            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
            foreach (string[] slot in uniqueSlots)
            {
                await Assert.That(entity.GetIndexes().Any(index =>
                        index.IsUnique &&
                        index.Properties.Select(property => property.Name).SequenceEqual(slot)))
                    .IsTrue()
                    .Because($"{type.Name} must own the replay/winner slot {string.Join(',', slot)}");
            }

            await Assert.That(entity.GetForeignKeys().Any(foreignKey =>
                    foreignKey.PrincipalEntityType.ClrType.Name.Contains(
                        "Admission",
                        StringComparison.OrdinalIgnoreCase)))
                .IsFalse();
            await Assert.That(entity.GetProperties().Any(property =>
                    ForbiddenAdmissionFragments.Any(fragment =>
                        property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
                .IsFalse();
        }
    }

    [Test]
    public async Task TwoBuyersRacingForTheLastUnitProduceExactlyOneWinner()
    {
        AddOnSeed seed = await SeedAsync(
            "inventory-race",
            capacity: 1,
            unitPriceMinor: 500,
            orderCount: 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var gate = new ExactRaceGate(2);

        async Task<EventAddOnInventoryResult>
            ReserveAsync(Guid lineId)
        {
            await using ExploreDbContext context =
                fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
            var repository = new EventAddOnRepository(context);
            await gate.ArriveAsync(timeout.Token);
            return await repository.ReserveInventoryAsync(
                seed.TenantId,
                seed.EventId,
                lineId,
                Guid.CreateVersion7(),
                UtcNow,
                timeout.Token);
        }

        Task<EventAddOnInventoryResult>[] contenders =
        [
            ReserveAsync(seed.LineIds[0]),
            ReserveAsync(seed.LineIds[1]),
        ];
        await gate.AllArrived.WaitAsync(timeout.Token);
        gate.Release();
        EventAddOnInventoryResult[] results =
            await Task.WhenAll(contenders);

        await Assert.That(results.Count(result =>
                result.Outcome ==
                EventAddOnInventoryOutcome.Reserved))
            .IsEqualTo(1);
        await Assert.That(results.Count(result =>
                result.Outcome ==
                EventAddOnInventoryOutcome
                    .InsufficientInventory))
            .IsEqualTo(1);
        await using ExploreDbContext verification =
            fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
        EventAddOnInventoryAllocation[] allocations =
            await verification
                .EventAddOnInventoryAllocations
                .AsNoTracking()
                .ToArrayAsync(timeout.Token);
        await Assert.That(allocations.Length)
            .IsEqualTo(1);
        await Assert.That(allocations.Sum(allocation =>
                allocation.Quantity))
            .IsEqualTo(1);
    }

    [Test]
    public async Task PartialRefundConservesValueAndFulfillmentReplayCreatesOneOutcome()
    {
        AddOnSeed seed = await SeedAsync(
            "refund-fulfillment",
            capacity: 3,
            unitPriceMinor: 333,
            orderCount: 1,
            quantity: 3);
        Guid lineId = seed.LineIds.Single();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using ExploreDbContext context =
            fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
        var repository = new EventAddOnRepository(context);

        EventAddOnInventoryResult reservation =
            await repository.ReserveInventoryAsync(
            seed.TenantId,
            seed.EventId,
            lineId,
            Guid.CreateVersion7(),
            UtcNow,
            timeout.Token);
        await Assert.That(reservation.Outcome)
            .IsEqualTo(EventAddOnInventoryOutcome.Reserved);

        Guid fulfillmentOperationId = Guid.CreateVersion7();
        EventAddOnFulfillmentResult firstFulfillment =
            await repository.FulfillAsync(
            seed.TenantId,
            seed.EventId,
            lineId,
            fulfillmentOperationId,
            UtcNow.AddMinutes(1),
            timeout.Token);
        EventAddOnFulfillmentResult replayedFulfillment =
            await repository.FulfillAsync(
            seed.TenantId,
            seed.EventId,
            lineId,
            fulfillmentOperationId,
            UtcNow.AddMinutes(1),
            timeout.Token);

        Guid refundOperationId = Guid.CreateVersion7();
        await SeedRefundAttemptAsync(
            context,
            seed,
            lineId,
            refundOperationId,
            purchasedQuantity: 3,
            unitPriceMinor: 333,
            amountMinor: 666,
            timeout.Token);
        EventAddOnRefundResult firstRefund =
            await repository.AllocateRefundAsync(
            seed.TenantId,
            seed.EventId,
            lineId,
            refundOperationId,
            quantity: 2,
            allocatedAtUtc: UtcNow.AddMinutes(2),
            cancellationToken: timeout.Token);
        EventAddOnRefundResult replayedRefund =
            await repository.AllocateRefundAsync(
            seed.TenantId,
            seed.EventId,
            lineId,
            refundOperationId,
            quantity: 2,
            allocatedAtUtc: UtcNow.AddMinutes(2),
            cancellationToken: timeout.Token);
        EventAddOnRefundResult excessRefund =
            await repository.AllocateRefundAsync(
            seed.TenantId,
            seed.EventId,
            lineId,
            Guid.CreateVersion7(),
            quantity: 2,
            allocatedAtUtc: UtcNow.AddMinutes(3),
            cancellationToken: timeout.Token);

        await Assert.That(firstFulfillment.Outcome)
            .IsEqualTo(EventAddOnFulfillmentOutcome.Fulfilled);
        await Assert.That(replayedFulfillment.Outcome)
            .IsEqualTo(
                EventAddOnFulfillmentOutcome.AlreadyFulfilled);
        await Assert.That(firstRefund.Outcome)
            .IsEqualTo(EventAddOnRefundOutcome.Allocated);
        await Assert.That(replayedRefund.Outcome)
            .IsEqualTo(EventAddOnRefundOutcome.AlreadyAllocated);
        await Assert.That(excessRefund.Outcome)
            .IsEqualTo(
                EventAddOnRefundOutcome.ExceedsCapturedAmount);
        context.ChangeTracker.Clear();
        EventAddOnFulfillment[] fulfillments =
            await context.EventAddOnFulfillments
                .AsNoTracking()
                .ToArrayAsync(timeout.Token);
        EventAddOnRefundAllocation[] refunds =
            await context.EventAddOnRefundAllocations
                .AsNoTracking()
                .ToArrayAsync(timeout.Token);
        EventAddOnInventoryAllocation[] allocations =
            await context.EventAddOnInventoryAllocations
                .AsNoTracking()
                .ToArrayAsync(timeout.Token);
        await Assert.That(fulfillments.Length).IsEqualTo(1);
        await Assert.That(refunds.Length).IsEqualTo(1);
        await Assert.That(allocations.Length).IsEqualTo(1);
        await Assert.That(allocations[0].ReleasedQuantity)
            .IsEqualTo(0);
        await Assert.That(allocations[0].ActiveUniquenessSlot)
            .IsNotNull();
        await Assert.That(refunds[0].Status)
            .IsEqualTo(
                EventAddOnRefundAllocationStatus.PendingProvider);
        await Assert.That(refunds.Sum(refund => refund.AmountMinor))
            .IsEqualTo(666L);
        await Assert.That(refunds.Sum(refund => refund.Quantity))
            .IsEqualTo(2);

        EventAddOnRefundAllocation? resolved =
            await repository.ResolveRefundAsync(
            seed.TenantId,
            refundOperationId,
            providerSucceeded: true,
            resolvedAtUtc: UtcNow.AddMinutes(4),
            cancellationToken: timeout.Token);
        await Assert.That(resolved).IsNotNull();
        context.ChangeTracker.Clear();
        allocations = await context.EventAddOnInventoryAllocations
            .AsNoTracking()
            .ToArrayAsync(timeout.Token);
        refunds = await context.EventAddOnRefundAllocations
            .AsNoTracking()
            .ToArrayAsync(timeout.Token);
        await Assert.That(allocations[0].ReleasedQuantity)
            .IsEqualTo(2);
        await Assert.That(refunds[0].Status)
            .IsEqualTo(EventAddOnRefundAllocationStatus.Confirmed);
    }

    [Test]
    public async Task TenantReadsFailClosedAndAddOnOperationsNeverCreateAdmissionState()
    {
        AddOnSeed tenantA = await SeedAsync(
            "tenant-a",
            capacity: 2,
            unitPriceMinor: 700,
            orderCount: 1);
        AddOnSeed tenantB = await SeedAsync(
            "tenant-b",
            capacity: 2,
            unitPriceMinor: 700,
            orderCount: 1,
            resetDatabase: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using ExploreDbContext context =
            fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        var repository = new EventAddOnRepository(context);

        RegistrationOrder? foreignOrder =
            await repository.GetOrderWithAddOnsAsync(
            tenantB.TenantId,
            tenantB.EventId,
            tenantB.OrderIds.Single(),
            timeout.Token);
        await Assert.That(foreignOrder).IsNull();
        await Assert.That(
                await context.RegistrationOrderAddOnLines
                    .AsNoTracking()
                    .AllAsync(line =>
                        line.TenantId == tenantA.TenantId,
                        timeout.Token))
            .IsTrue();

        int ticketCountBefore = await context.AdmissionTickets.CountAsync(timeout.Token);
        int credentialCountBefore = await context.AdmissionTicketCredentials.CountAsync(timeout.Token);
        int checkInCountBefore = await context.AdmissionCheckInEvents.CountAsync(timeout.Token);
        Guid lineId = tenantA.LineIds.Single();
        _ = await repository.ReserveInventoryAsync(
            tenantA.TenantId,
            tenantA.EventId,
            lineId,
            Guid.CreateVersion7(),
            UtcNow,
            timeout.Token);
        _ = await repository.FulfillAsync(
            tenantA.TenantId,
            tenantA.EventId,
            lineId,
            Guid.CreateVersion7(),
            UtcNow.AddMinutes(1),
            timeout.Token);
        Guid refundOperationId = Guid.CreateVersion7();
        await SeedRefundAttemptAsync(
            context,
            tenantA,
            lineId,
            refundOperationId,
            purchasedQuantity: 1,
            unitPriceMinor: 700,
            amountMinor: 700,
            timeout.Token);
        _ = await repository.AllocateRefundAsync(
            tenantA.TenantId,
            tenantA.EventId,
            lineId,
            refundOperationId,
            quantity: 1,
            allocatedAtUtc: UtcNow.AddMinutes(2),
            cancellationToken: timeout.Token);

        await Assert.That(await context.AdmissionTickets.CountAsync(timeout.Token)).IsEqualTo(ticketCountBefore);
        await Assert.That(await context.AdmissionTicketCredentials.CountAsync(timeout.Token)).IsEqualTo(credentialCountBefore);
        await Assert.That(await context.AdmissionCheckInEvents.CountAsync(timeout.Token)).IsEqualTo(checkInCountBefore);
    }

    private static async Task SeedRefundAttemptAsync(
        ExploreDbContext context,
        AddOnSeed seed,
        Guid lineId,
        Guid refundAttemptId,
        int purchasedQuantity,
        long unitPriceMinor,
        long amountMinor,
        CancellationToken cancellationToken)
    {
        Guid paymentAttemptId = Guid.CreateVersion7();
        OrganizerPaymentRecipientSnapshot recipient =
            OrganizerPaymentRecipientSnapshot.Create(
                seed.TenantId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "stripe",
                "platform-live-eu",
                "acct_add_on",
                "BE",
                "EUR",
                Guid.CreateVersion7(),
                null,
                UtcNow);
        PaidOrderAcceptanceSnapshot acceptance =
            PaidOrderAcceptanceSnapshot.Create(
                paymentAttemptId,
                seed.TenantId,
                seed.TenantId,
                seed.OrderIds.Single(),
                seed.EventId,
                "event-add-on-test",
                "disclosure-1",
                PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
                PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
                recipient.OrganizerActorId,
                "Add-on Organizer",
                PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    "Community Events",
                    "Community Events ASBL",
                    "registered_organization",
                    "BE",
                    null,
                    "directory@example.test",
                    "https://example.test/legal",
                    "https://example.test/terms",
                    "https://example.test/privacy"),
                PaidCheckoutOperatorDisclosure.Create(
                    Guid.CreateVersion7(),
                    "Example Operator",
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
                PaidOrderDeliverySnapshot.Create(
                    new DateTimeOffset(UtcNow.AddDays(1)),
                    new DateTimeOffset(UtcNow.AddDays(1).AddHours(2)),
                    "Europe/Brussels"),
                "EUR",
                MinorUnitMath.Multiply(
                    unitPriceMinor,
                    purchasedQuantity),
                0,
                0,
                MinorUnitMath.Multiply(
                    unitPriceMinor,
                    purchasedQuantity),
                recipient.InstancePolicyVersionId,
                1,
                "Add-on refunds follow the accepted policy.",
                "en-GB",
                "support@example.test",
                PaidCheckoutProviderDisclosure.Create(
                    "stripe",
                    "OrganizerDirect",
                    "direct-charge",
                    "EXAMPLE EVENT",
                    "test",
                    "instance-operator"),
                [
                    PaidOrderAcceptanceLineFact.Create(
                        lineId,
                        "Add-on refund line",
                        purchasedQuantity,
                        unitPriceMinor,
                        0,
                        MinorUnitMath.Multiply(
                            unitPriceMinor,
                            purchasedQuantity)),
                ],
                UtcNow,
                recipient.TenantPolicyVersionId,
                recipient.OrganizerPaymentProviderConnectionId,
                recipient.ConnectPlatformId,
                recipient.ExternalAccountId,
                recipient.MerchantCountryCode);
        PaymentAttempt payment = PaymentAttempt.Create(
            paymentAttemptId,
            seed.TenantId,
            seed.OrderIds.Single(),
            recipient,
            "OrganizerDirect",
            "2026-08-20.acacia",
            "event-add-on-test",
            Money.Create(
                MinorUnitMath.Multiply(
                    unitPriceMinor,
                    purchasedQuantity),
                "EUR"),
            Money.Create(0, "EUR"),
            Money.Create(0, "EUR"),
            $"payment:{seed.TenantId:N}:{paymentAttemptId:N}",
            UtcNow,
            UtcNow.AddMinutes(30));
        payment.AttachAcceptance(acceptance);
        payment.MarkSucceeded(
            $"pi_{paymentAttemptId:N}",
            UtcNow.AddSeconds(1),
            "req_add_on_payment");
        RefundAttempt refund = RefundAttempt.Create(
            refundAttemptId,
            seed.TenantId,
            paymentAttemptId,
            acceptance,
            "acct_add_on",
            $"pi_{paymentAttemptId:N}",
            $"refund:{refundAttemptId:N}",
            amountMinor,
            UtcNow.AddMinutes(2));
        context.PaymentAttempts.Add(payment);
        context.RefundAttempts.Add(refund);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<AddOnSeed> SeedAsync(
        string suffix,
        int capacity,
        long unitPriceMinor,
        int orderCount,
        int quantity = 1,
        bool resetDatabase = true)
    {
        if (resetDatabase)
        {
            await fixture.ResetAsync();
        }

        await using ExploreDbContext context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = $"Add-on {suffix}",
            Slug = $"add-on-{suffix}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"add-on-{suffix}-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Add-on",
                LastName = suffix,
            },
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = $"Add-on {suffix}" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        Guid eventId = Guid.CreateVersion7();
        var eventEntity = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = $"Add-on {suffix}",
            Subtitle = string.Empty,
            Description = string.Empty,
            FirstSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            TotalViews = 0,
        };
        EventTicketCatalogVersion ticketCatalog =
            EventTicketCatalogVersion.Create(tenant.Id, eventId, "EUR", 1);
        EventTicketType ticketType = EventTicketType.Create(
            Guid.CreateVersion7(),
            tenant.Id,
            ticketCatalog.Id,
            "General admission",
            "EUR",
            TicketPricingModeEnum.Free,
            null,
            null,
            null,
            ParticipantDataCollectionModeEnum.None,
            null,
            null,
            null,
            false,
            false,
            null,
            null,
            null,
            null);
        ticketCatalog.AddTicketType(ticketType, null);
        ticketCatalog.AddEntitlement(
            ticketType,
            TicketTypeEntitlement.CreateForEvent(ticketType.Id, tenant.Id, eventId, 1));
        ticketCatalog.Publish();

        EventAddOnCatalogVersion addOnCatalog =
            EventAddOnCatalogVersion.Create(
                tenant.Id,
                eventId,
                "EUR",
                1);
        EventAddOnCatalogItem addOnItem = CreateItem(
            addOnCatalog,
            $"Add-on {suffix}",
            null,
            unitPriceMinor,
            capacity,
            "Fulfilled at the event service desk.",
            "Unfulfilled quantities may be refunded under the accepted policy.");
        addOnCatalog.AddItem(addOnItem);
        addOnCatalog.Publish(UtcNow);

        var orderIds = new List<Guid>(orderCount);
        var lineIds = new List<Guid>(orderCount);
        for (int index = 0; index < orderCount; index++)
        {
            RegistrationOrder order = RegistrationOrder.Create(
                tenant.Id,
                eventId,
                user.Id,
                actor.Id,
                BookingPartyTypeEnum.Individual,
                ticketCatalog.Id,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
                null,
                null,
                "EUR",
                UtcNow,
                UtcNow.AddMinutes(30));
            order.PinAddOnCatalog(addOnCatalog);
            order.AddLine(RegistrationOrderLine.Create(
                ticketCatalog,
                ticketType,
                order.Id,
                1,
                null,
                null));
            RegistrationOrderAddOnLine addOnLine =
                RegistrationOrderAddOnLine.Create(
                    Guid.CreateVersion7(),
                    order,
                    addOnCatalog,
                    addOnItem,
                    quantity);
            order.AddAddOnLine(addOnLine);
            long addOnTotal = MinorUnitMath.Multiply(unitPriceMinor, quantity);
            order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create(
                "EUR",
                addOnTotal,
                0,
                addOnTotal,
                0));
            context.RegistrationOrders.Add(order);
            orderIds.Add(order.Id);
            lineIds.Add(addOnLine.Id);
        }

        context.Add(eventEntity);
        context.Add(ticketCatalog);
        context.Add(addOnCatalog);
        await context.SaveChangesAsync();
        return new AddOnSeed(tenant.Id, eventId, orderIds, lineIds);
    }

    private static EventAddOnCatalogItem CreateItem(
        EventAddOnCatalogVersion catalog,
        string name,
        string? description,
        long unitPriceMinor,
        int inventoryCapacity,
        string fulfillmentDisclosure,
        string refundDisclosure) =>
        EventAddOnCatalogItem.Create(
            Guid.CreateVersion7(),
            catalog.TenantId,
            catalog.Id,
            name,
            description,
            Money.Create(
                unitPriceMinor,
                catalog.CurrencyCode),
            inventoryCapacity,
            fulfillmentDisclosure,
            refundDisclosure);

    private static RegistrationOrder CreateOrder(Guid tenantId, Guid eventId) =>
        RegistrationOrder.Create(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
            null,
            null,
            "EUR",
            UtcNow,
            UtcNow.AddMinutes(30));

    private static readonly string[] ForbiddenAdmissionFragments =
    [
        "AdmissionTicket",
        "Credential",
        "CheckIn",
        "ParticipantReadiness",
        "TicketCapacity",
    ];

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record AddOnSeed(
        Guid TenantId,
        Guid EventId,
        IReadOnlyList<Guid> OrderIds,
        IReadOnlyList<Guid> LineIds);

    private sealed class ExactRaceGate(int expected)
    {
        private readonly TaskCompletionSource allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        internal Task AllArrived => allArrived.Task;

        internal async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == expected)
            {
                allArrived.TrySetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
        }

        internal void Release() => release.TrySetResult();
    }
}
