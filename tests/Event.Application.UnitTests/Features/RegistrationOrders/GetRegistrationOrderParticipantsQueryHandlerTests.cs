using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.RegistrationOrders.Handlers.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using NSubstitute;

namespace Event.Application.UnitTests.Features.RegistrationOrders;

public sealed class GetRegistrationOrderParticipantsQueryHandlerTests
{
    [Test]
    public async Task Handle_ProjectsPinnedLineModeAndGuardianFacts()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        var child = EventTicketType.Create(
            Guid.CreateVersion7(), tenantId, catalog.Id, "Child", "EUR",
            TicketPricingModeEnum.Free, null, null, null,
            ParticipantDataCollectionModeEnum.PerTicketRequired, null,
            null, 17, true, false, null, null, null, null);
        catalog.AddTicketType(child, null);
        catalog.AddEntitlement(child, TicketTypeEntitlement.CreateForEvent(child.Id, tenantId, eventId, 1));
        catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
        catalog.Publish();
        var now = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
        var order = RegistrationOrder.Create(
            tenantId, eventId, Guid.CreateVersion7(), null, BookingPartyTypeEnum.Household,
            catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null, null, "EUR", now, now.AddMinutes(15));
        RegistrationOrderLine line = RegistrationOrderLine.Create(catalog, child, order.Id, 2, null, null);
        order.AddLine(line);

        var inventory = Substitute.For<IRegistrationInventoryRepository>();
        var catalogs = Substitute.For<IEventTicketCatalogRepository>();
        var participants = Substitute.For<IRegistrationParticipantRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        inventory.GetOrderWithLinesAsync(order.Id, tenantId, Arg.Any<CancellationToken>()).Returns(order);
        catalogs.GetOrderCatalogAsync(catalog.Id, eventId, tenantId, Arg.Any<CancellationToken>()).Returns(catalog);
        participants.GetParticipantsByOrderAsync(order.Id, tenantId, Arg.Any<CancellationToken>()).Returns([]);
        participants.GetAssignmentsWithParticipantsByOrderAsync(order.Id, tenantId, Arg.Any<CancellationToken>()).Returns([]);
        var handler = new GetRegistrationOrderParticipantsQueryHandler(inventory, catalogs, participants, tenant);

        var result = await handler.Handle(new GetRegistrationOrderParticipantsQuery(order.Id), CancellationToken.None);

        var projected = result!.Lines.Single();
        await Assert.That(projected.Id).IsEqualTo(line.Id);
        await Assert.That(projected.ParticipantDataCollectionModeCode).IsEqualTo("PER_TICKET_REQUIRED");
        await Assert.That(projected.RequiresGuardian).IsTrue();
        await Assert.That(projected.Quantity).IsEqualTo(2);
    }
}
