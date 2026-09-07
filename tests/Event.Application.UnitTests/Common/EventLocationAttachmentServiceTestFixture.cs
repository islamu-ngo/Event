using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Common;

public static class EventLocationAttachmentServiceTestFixture
{
    public static EventLocationAttachmentService ForExistingEvent(
        IEventRepository eventRepository,
        Guid actorUserId)
    {
        ArgumentNullException.ThrowIfNull(eventRepository);
        RequireId(actorUserId, nameof(actorUserId));
        var placements = new Dictionary<Guid, EventLocation>();
        Guid? resolvedTenantId = null;
        var repository = Substitute.For<IEventLocationRepository>();
        repository.FindActivePhysicalAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => CreatePlacementAsync(call.ArgAt<Guid>(0), call.ArgAt<Guid>(1)));
        repository.FindActiveToBeAnnouncedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => CreatePlacementAsync(call.ArgAt<Guid>(0), null));
        repository.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => placements.GetValueOrDefault(call.ArgAt<Guid>(0)));
        var userContext = Substitute.For<IUserContext>();
        userContext.GetRequiredUserId().Returns(actorUserId);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(_ => resolvedTenantId
            ?? throw new InvalidOperationException("The fixture tenant is unavailable before its parent event is resolved."));

        return new(repository, userContext, tenantContext, TimeProvider.System);

        async Task<EventLocation?> CreatePlacementAsync(Guid eventId, Guid? locationId)
        {
            Explore.Domain.Event? parentEvent = await eventRepository.GetById(eventId);
            Guid tenantId = parentEvent?.TenantId
                ?? throw new InvalidOperationException("The fixture requires an explicit persisted parent event.");
            if (resolvedTenantId.HasValue && resolvedTenantId.Value != tenantId)
            {
                throw new InvalidOperationException("One fixture cannot attach events from different tenants.");
            }

            resolvedTenantId = tenantId;
            EventLocation placement = locationId.HasValue
                ? EventLocation.CreatePhysical(tenantId, eventId, locationId.Value, actorUserId, DateTime.UnixEpoch)
                : EventLocation.CreateToBeAnnounced(tenantId, eventId, actorUserId, DateTime.UnixEpoch);
            placements.Add(placement.Id, placement);
            return placement;
        }
    }

    public static EventLocationAttachmentService ForCreateEvent(
        Guid tenantId,
        Guid actorUserId)
    {
        RequireId(tenantId, nameof(tenantId));
        RequireId(actorUserId, nameof(actorUserId));
        var repository = Substitute.For<IEventLocationRepository>();
        repository.AddAsync(Arg.Any<EventLocation>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<EventLocation>(0));
        return Create(repository, tenantId, actorUserId);
    }

    private static EventLocationAttachmentService Create(
        IEventLocationRepository repository,
        Guid tenantId,
        Guid actorUserId)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetRequiredUserId().Returns(actorUserId);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        return new(repository, userContext, tenantContext, TimeProvider.System);
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }
}
