using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceAuthoritySnapshotPersistenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionApprovalCannotBeBorrowedFromAnotherOrderLine(bool bindRegistrationLine)
    {
        var scope = await database.SeedScopeAsync();
        Guid userId, selectedEligibilityId;
        EventResource resource;
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(value => value.Id == scope.ActorId)
                .Select(value => value.UserId).SingleAsync())!.Value;
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                UserId = userId, User = null!, ActorId = scope.ActorId,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            });
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            if (parent.EventStatusId != (int)EventStatusEnum.Published) parent.Publish(Now);
            parent.VisibilityTypeId = (int)VisibilityTypeEnum.Public;
            var firstSession = await seed.EventSessions.SingleAsync(value => value.Id == scope.SessionAId);
            var selectedSession = await seed.EventSessions.SingleAsync(value => value.Id == scope.SessionA2Id);
            foreach (var session in new[] { firstSession, selectedSession })
            {
                session.StartTime = new(Now);
                session.EndTime = new(Now.AddHours(1));
                session.Publish(EventStatusEnum.Published, Now);
            }

            var catalog = await seed.EventTicketCatalogVersions.Include(value => value.TicketTypes)
                .SingleAsync(value => value.Id == scope.CatalogAId);
            var firstType = catalog.TicketTypes.Single(value => value.Id == scope.TicketTypeAId);
            var selectedType = EventTicketType.Create(Guid.CreateVersion7(), scope.TenantAId, catalog.Id,
                "Selected session", "USD", TicketPricingModeEnum.Free, null, null, null,
                ParticipantDataCollectionModeEnum.None, null, null, null, false, false, null, null, null, null);
            catalog.AddTicketType(selectedType, null);
            catalog.AddEntitlement(firstType, TicketTypeEntitlement.CreateForEventSession(
                firstType.Id, firstSession, 1, EntitlementSelectionRuleEnum.AllIncluded));
            var selectedEntitlement = TicketTypeEntitlement.CreateForEventSession(
                selectedType.Id, selectedSession, 1, EntitlementSelectionRuleEnum.AllIncluded);
            catalog.AddEntitlement(selectedType, selectedEntitlement);
            catalog.Publish();
            var order = RegistrationOrder.Create(scope.TenantAId, scope.EventAId, userId, scope.ActorId,
                BookingPartyTypeEnum.Individual, catalog.Id,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
                null, null, "USD", Now, null);
            var participant = RegistrationParticipant.Create(scope.TenantAId, order.Id, userId, ParticipantTypeEnum.Adult, null);
            order.AddParticipant(participant);
            var firstLine = RegistrationOrderLine.Create(catalog, firstType, order.Id, 1, null, null);
            var selectedLine = RegistrationOrderLine.Create(catalog, selectedType, order.Id, 1, null, null);
            order.AddLine(firstLine);
            order.AddLine(selectedLine);
            var firstAssignment = RegistrationTicketAssignment.CreateAssigned(
                Guid.CreateVersion7(), firstLine.Id, 1, participant, Now);
            var selectedAssignment = RegistrationTicketAssignment.CreateAssigned(
                Guid.CreateVersion7(), selectedLine.Id, 1, participant, Now);
            order.AddAssignment(firstLine, firstAssignment, participant);
            order.AddAssignment(selectedLine, selectedAssignment, participant);
            order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now);
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now);
            order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, Now);
            var firstEligibility = ParticipantAdmissionEligibility.Create(
                scope.TenantAId, scope.EventAId, firstAssignment, participant, false, false, Now);
            var selectedEligibility = ParticipantAdmissionEligibility.Create(
                scope.TenantAId, scope.EventAId, selectedAssignment, participant, false, false, Now);
            firstEligibility.RecordSubjectCompletion(participant, userId, null, Now, Guid.CreateVersion7());
            selectedEligibility.RecordSubjectCompletion(participant, userId, null, Now, Guid.CreateVersion7());
            firstEligibility.Approve(scope.ActorId, Now, Guid.CreateVersion7());
            selectedEligibilityId = selectedEligibility.Id;
            var resourceId = Guid.CreateVersion7();
            resource = EventResource.CreateDraft(resourceId, scope.TenantAId, scope.EventAId, null,
                new() { Title = "Selected session approval", Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly },
                EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resourceId,
                    EventResourceAudienceKindEnum.SessionRegistrant, sessionId: selectedSession.Id,
                    requireApproval: true, requireCompletion: true)], userId, Now);
            resource.SetStoredFile(scope.StorageAId, resource.ConcurrencyStamp, userId, Now);
            resource.Publish(new(scope.TenantAId, scope.EventAId, null, EventStatusEnum.Published,
                false, true, null, false, new(new(Now), new(Now.AddHours(1)), null, null)),
                true, resource.ConcurrencyStamp, userId, Now);
            seed.AddRange(order, firstEligibility, selectedEligibility, resource, new EventRegistration
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                EventId = scope.EventAId, Event = parent, EventSessionId = selectedSession.Id,
                EventSession = selectedSession, LinkedUserId = userId, RegistrationOrderId = order.Id,
                RegistrationParticipantId = participant.Id, RegistrationParticipant = participant,
                RegistrationOrderLineId = bindRegistrationLine ? selectedLine.Id : null,
                TicketTypeEntitlementId = selectedEntitlement.Id,
                CoverageEstablishedAt = Now, ConcurrencyStamp = Guid.CreateVersion7()
            });
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var reader = new EventResourceAuthoritySnapshotReader(
            new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
        var request = new EventResourceAuthorityRequest(scope.TenantAId, resource.Id, userId, false, "view");
        var before = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(EventResourceAccessRules.Evaluate(resource, before.Access, new(Now)).DisclosePrivateMetadata).IsFalse();

        await using (var writer = database.CreateContext())
        {
            var selected = await writer.Set<ParticipantAdmissionEligibility>().SingleAsync(value => value.Id == selectedEligibilityId);
            selected.Approve(scope.ActorId, Now, Guid.CreateVersion7());
            await writer.SaveChangesAsync();
        }
        var approved = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(EventResourceAccessRules.Evaluate(resource, approved.Access, new(Now)).DisclosePrivateMetadata)
            .IsEqualTo(bindRegistrationLine);

        await using (var writer = database.CreateContext())
        {
            var selected = await writer.Set<ParticipantAdmissionEligibility>().SingleAsync(value => value.Id == selectedEligibilityId);
            selected.Revoke(scope.ActorId, Now, Guid.CreateVersion7());
            await writer.SaveChangesAsync();
        }
        var revoked = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(EventResourceAccessRules.Evaluate(resource, revoked.Access, new(Now)).DisclosePrivateMetadata).IsFalse();
    }
}
