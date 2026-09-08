using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.EventAgendaItems.Handlers.Commands;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.EventSessionAgendaItems.Handlers.Commands;
using Explore.Application.Features.EventSessionGroups.Handlers.Commands;
using Explore.Application.Features.EventSessions.Handlers.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using TUnit.Core;

namespace Event.Application.UnitTests.Services;

[Category("EventLocationPrivacy")]
public sealed class EventLocationAttachmentServiceTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid ActorUserId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 10, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task ResolveAsyncCreatesFailClosedAuditedPolicyForNewPhysicalPlacement()
    {
        var repository = new StrictEventLocationRepository(TenantId);
        var service = CreateService(repository);
        Guid eventId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();

        EventLocation placement = await service.ResolveAsync(
            eventId,
            locationId,
            currentEventLocationId: null,
            CancellationToken.None);

        EventLocationDisclosureAudit audit = placement.CreateInitialDisclosureAudit();
        await Assert.That(placement.TenantId).IsEqualTo(TenantId);
        await Assert.That(placement.EventId).IsEqualTo(eventId);
        await Assert.That(placement.LocationId).IsEqualTo(locationId);
        await Assert.That(placement.IsToBeAnnounced).IsFalse();
        await Assert.That(placement.NeedsPrivacyReview).IsTrue();
        await Assert.That(placement.FullDetailsAudienceId)
            .IsEqualTo((int)LocationDisclosureAudienceEnum.Never);
        await Assert.That(placement.PolicyVersion).IsEqualTo(1);
        await Assert.That(placement.LastPolicyActorUserId).IsEqualTo(ActorUserId);
        await Assert.That(audit.Reason)
            .IsEqualTo(EventLocationDisclosureAuditReasonEnum.AssociationCreated);
        await Assert.That(audit.NewFields).IsEqualTo(EventLocationDisclosureFields.None);
        await Assert.That(repository.AddCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveAsyncCreatesExplicitFailClosedTbaForNullPhysicalLocation()
    {
        var repository = new StrictEventLocationRepository(TenantId);
        var service = CreateService(repository);

        EventLocation placement = await service.ResolveAsync(
            Guid.CreateVersion7(),
            locationId: null,
            currentEventLocationId: null,
            CancellationToken.None);

        await Assert.That(placement.HasValidLocationOrTbaShape).IsTrue();
        await Assert.That(placement.IsToBeAnnounced).IsTrue();
        await Assert.That(placement.LocationId).IsNull();
        await Assert.That(placement.ShowVenueName).IsFalse();
        await Assert.That(placement.ShowCity).IsFalse();
        await Assert.That(placement.ShowCountry).IsFalse();
        await Assert.That(placement.ShowRoomName).IsFalse();
        await Assert.That(placement.ShowStreetAddress).IsFalse();
        await Assert.That(placement.ShowPostcode).IsFalse();
        await Assert.That(placement.ShowCoordinates).IsFalse();
    }

    [Test]
    public async Task ResolveAsyncReusesActivePhysicalAndTbaPlacements()
    {
        var repository = new StrictEventLocationRepository(TenantId);
        var service = CreateService(repository);
        Guid eventId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();

        EventLocation firstPhysical = await service.ResolveAsync(eventId, locationId, null, CancellationToken.None);
        EventLocation secondPhysical = await service.ResolveAsync(eventId, locationId, null, CancellationToken.None);
        EventLocation firstTba = await service.ResolveAsync(eventId, null, null, CancellationToken.None);
        EventLocation secondTba = await service.ResolveAsync(eventId, null, null, CancellationToken.None);

        await Assert.That(secondPhysical.Id).IsEqualTo(firstPhysical.Id);
        await Assert.That(secondTba.Id).IsEqualTo(firstTba.Id);
        await Assert.That(repository.AddCalls).IsEqualTo(2);
    }

    [Test]
    public async Task ResolveAsyncCreatesIndependentPoliciesForSamePhysicalLocationAcrossEvents()
    {
        var repository = new StrictEventLocationRepository(TenantId);
        var service = CreateService(repository);
        Guid locationId = Guid.CreateVersion7();

        EventLocation first = await service.ResolveAsync(
            Guid.CreateVersion7(), locationId, null, CancellationToken.None);
        EventLocation second = await service.ResolveAsync(
            Guid.CreateVersion7(), locationId, null, CancellationToken.None);

        await Assert.That(second.Id).IsNotEqualTo(first.Id);
        await Assert.That(second.EventId).IsNotEqualTo(first.EventId);
        await Assert.That(second.LocationId).IsEqualTo(first.LocationId);
        await Assert.That(second.PolicyVersion).IsEqualTo(1);
    }

    [Test]
    public async Task DetachIfUnreferencedAsyncOnlySoftDeletesAfterFinalCarrierReference()
    {
        var repository = new StrictEventLocationRepository(TenantId);
        var service = CreateService(repository);
        EventLocation placement = await service.ResolveAsync(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, CancellationToken.None);
        repository.ActiveCarrierReferences.Add(placement.Id);

        await service.DetachIfUnreferencedAsync(placement.Id, CancellationToken.None);
        await Assert.That(placement.IsDeleted).IsFalse();

        repository.ActiveCarrierReferences.Remove(placement.Id);
        await service.DetachIfUnreferencedAsync(placement.Id, CancellationToken.None);

        await Assert.That(placement.IsDeleted).IsTrue();
        await Assert.That(placement.DeletedBy).IsEqualTo(ActorUserId);
        await Assert.That(placement.DeletedAt).IsEqualTo(Now.UtcDateTime);
        await Assert.That(repository.SaveChangesCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveAsyncCreatesFreshVersionOneAssociationAfterFinalDetach()
    {
        var repository = new StrictEventLocationRepository(TenantId);
        var service = CreateService(repository);
        Guid eventId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();
        EventLocation detached = await service.ResolveAsync(eventId, locationId, null, CancellationToken.None);
        await service.DetachIfUnreferencedAsync(detached.Id, CancellationToken.None);

        EventLocation replacement = await service.ResolveAsync(
            eventId, locationId, detached.Id, CancellationToken.None);

        await Assert.That(replacement.Id).IsNotEqualTo(detached.Id);
        await Assert.That(replacement.PolicyVersion).IsEqualTo(1);
        await Assert.That(replacement.IsDeleted).IsFalse();
        await Assert.That(detached.IsDeleted).IsTrue();
        await Assert.That(repository.AddCalls).IsEqualTo(2);
    }

    [Test]
    public async Task ResolveAndDetachAsyncPropagateCancellationToken()
    {
        var repository = new StrictEventLocationRepository(TenantId);
        var service = CreateService(repository);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ResolveAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), null, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.DetachIfUnreferencedAsync(Guid.CreateVersion7(), cancellation.Token));
    }

    [Test]
    public async Task CarrierHandlersRequireServerAttachmentAuthorityAndImportHasNoCarrierSurface()
    {
        Type[] carrierHandlers =
        [
            typeof(CreateEventCommandHandler),
            typeof(CreateEventSessionCommandHandler),
            typeof(CreateDraftEventSessionCommandHandler),
            typeof(UpdateEventSessionCommandHandler),
            typeof(DeleteEventSessionCommandHandler),
            typeof(CreateEventSessionGroupCommandHandler),
            typeof(UpdateEventSessionGroupCommandHandler),
            typeof(DeleteEventSessionGroupCommandHandler),
            typeof(CreateEventAgendaItemCommandHandler),
            typeof(UpdateEventAgendaItemCommandHandler),
            typeof(DeleteEventAgendaItemCommandHandler),
            typeof(CreateEventSessionAgendaItemCommandHandler),
            typeof(UpdateEventSessionAgendaItemCommandHandler),
            typeof(DeleteEventSessionAgendaItemCommandHandler)
        ];

        foreach (Type handler in carrierHandlers)
        {
            bool requiresAttachmentService = handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(EventLocationAttachmentService));
            await Assert.That(requiresAttachmentService).IsTrue();
        }

        string[] carrierPropertyNames = ["LocationId", "RoomId", "EventLocationId", "Sessions", "Groups", "AgendaItems"];
        string[] importProperties = typeof(ImportEventRequestDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(importProperties.Intersect(carrierPropertyNames)).IsEmpty();
        await Assert.That(typeof(ImportEventCommandHandler).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(EventLocationAttachmentService))).IsFalse();
    }

    [Test]
    public async Task ResolveAsyncRejectsMalformedEmptyEventAndLocationIds()
    {
        var service = CreateService(new StrictEventLocationRepository(TenantId));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResolveAsync(Guid.Empty, Guid.CreateVersion7(), null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResolveAsync(Guid.CreateVersion7(), Guid.Empty, null, CancellationToken.None));
    }

    private static EventLocationAttachmentService CreateService(StrictEventLocationRepository repository) =>
        new(
            repository,
            new TestUserContext(ActorUserId),
            new TestTenantContext(TenantId),
            new FixedTimeProvider(Now));

    private sealed class StrictEventLocationRepository(Guid tenantId) : IEventLocationRepository
    {
        private readonly Dictionary<Guid, EventLocation> _placements = [];

        public HashSet<Guid> ActiveCarrierReferences { get; } = [];
        public int AddCalls { get; private set; }
        public int SaveChangesCalls { get; private set; }

        public Task<EventLocation> AddAsync(EventLocation eventLocation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (eventLocation.TenantId != tenantId)
            {
                throw new InvalidOperationException("Cross-tenant EventLocation creation is forbidden.");
            }

            AddCalls++;
            _placements.Add(eventLocation.Id, eventLocation);
            return Task.FromResult(eventLocation);
        }

        public Task<EventLocation?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventLocation? result = _placements.GetValueOrDefault(id);
            return Task.FromResult(result is { IsDeleted: false } && result.TenantId == tenantId ? result : null);
        }

        public Task<IReadOnlyList<EventLocation>> GetByIdsAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EventLocation>> GetByEventIdAsync(
            Guid eventId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EventLocation?> FindActivePhysicalAsync(
            Guid eventId,
            Guid locationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventLocation? result = _placements.Values.SingleOrDefault(item =>
                !item.IsDeleted
                && item.TenantId == tenantId
                && item.EventId == eventId
                && item.LocationId == locationId
                && !item.IsToBeAnnounced);
            return Task.FromResult(result);
        }

        public Task<EventLocation?> FindActiveToBeAnnouncedAsync(
            Guid eventId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventLocation? result = _placements.Values.SingleOrDefault(item =>
                !item.IsDeleted
                && item.TenantId == tenantId
                && item.EventId == eventId
                && item.IsToBeAnnounced);
            return Task.FromResult(result);
        }

        public Task<bool> HasActiveCarrierReferencesAsync(
            Guid eventLocationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ActiveCarrierReferences.Contains(eventLocationId));
        }

        public Task<IReadOnlyList<EventLocation>> GetActiveForGovernanceUpdateAsync(
            Guid? requestedTenantId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountNeedingPrivacyReviewAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveGovernanceChangesAsync(
            IReadOnlyCollection<EventLocationDisclosureAudit> audits,
            IReadOnlyCollection<OutboxMessage> outboxMessages,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record TestUserContext(Guid RequiredUserId) : IUserContext
    {
        public Guid? UserId => RequiredUserId;
        public string? Email => null;
        public string? Username => null;
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => RequiredUserId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
