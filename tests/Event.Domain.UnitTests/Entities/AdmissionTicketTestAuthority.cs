using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

internal sealed record AdmissionTicketTestAuthority(
    RegistrationOrder Order,
    RegistrationOrderLine OrderLine,
    RegistrationTicketAssignment Assignment,
    RegistrationParticipant Participant,
    EventTicketCatalogVersion Catalog,
    EventTicketType TicketType)
{
    internal static AdmissionTicketTestAuthority Create(DateTime now, bool confirmed = true)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        EventTicketType ticketType = EventTicketType.Create(
            Guid.CreateVersion7(),
            tenantId,
            catalog.Id,
            "General admission",
            "EUR",
            TicketPricingModeEnum.Free,
            fixedPrice: null,
            minimumPrice: null,
            suggestedPrice: null,
            ParticipantDataCollectionModeEnum.PerTicketRequired,
            capacityPoolId: null,
            minimumAge: null,
            maximumAge: null,
            requiresGuardian: false,
            requiresApproval: false,
            perOrderLimit: null,
            perAccountLimit: null,
            perVerifiedContactLimit: null,
            perBookingPartyLimit: null);
        catalog.AddTicketType(ticketType, capacityPool: null);
        catalog.AddEntitlement(
            ticketType,
            TicketTypeEntitlement.CreateForEvent(ticketType.Id, tenantId, eventId, 1));
        catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
        catalog.Publish();

        RegistrationOrder order = RegistrationOrder.Create(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            purchaserActorId: null,
            BookingPartyTypeEnum.Individual,
            catalog.Id,
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(),
                participationHandlingModeId: 1,
                advanceRegistrationObligationId: 1,
                identityAccessModeId: 1,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            registrationWorkflowVersionId: null,
            guestAccessTokenHash: null,
            "EUR",
            now,
            now.AddMinutes(15));
        RegistrationOrderLine line = RegistrationOrderLine.Create(
            catalog,
            ticketType,
            order.Id,
            quantity: 1,
            chosenUnitPriceAmount: null,
            platformFeePolicy: null);
        order.AddLine(line);
        RegistrationParticipant participant = RegistrationParticipant.Create(
            tenantId,
            order.Id,
            linkedUserId: null,
            ParticipantTypeEnum.Adult,
            guardian: null);
        RegistrationTicketAssignment assignment = RegistrationTicketAssignment.CreateAssigned(
            Guid.CreateVersion7(),
            line.Id,
            ordinal: 1,
            participant,
            now);
        order.AddParticipant(participant);
        order.AddAssignment(line, assignment, participant);

        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("EUR", 0, 0, 0, 0));
        if (confirmed)
        {
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
            order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
        }

        return new(order, line, assignment, participant, catalog, ticketType);
    }
}
