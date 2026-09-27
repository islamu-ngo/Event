using Event.Application.UnitTests.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Events;

public sealed class DeleteEventCommandHandlerTests
{
    [Test]
    public async Task MissingEvent_ReturnsNotFound()
    {
        Guid eventId = Guid.CreateVersion7();
        DeleteEventCommandHandler handler = CreateHandler(eventId, out _, out _);

        BaseCommandResponse<Guid> result = await handler.ExecuteAsync(Command(eventId), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.NotFound);
        await Assert.That(result.Id).IsEqualTo(eventId);
    }

    [Test]
    public async Task CallerWithoutEventAuthority_ReturnsForbiddenOutcome()
    {
        Guid eventId = Guid.CreateVersion7();
        DeleteEventCommandHandler handler = CreateHandler(eventId, out IEventRepository events, out _);
        events.GetById(eventId).Returns(Event(eventId));

        BaseCommandResponse<Guid> result = await handler.ExecuteAsync(Command(eventId), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(DeleteEventFailureCodes.AuthorityDenied);
        await Assert.That(result.Id).IsEqualTo(eventId);
    }

    [Test]
    public async Task PaidEvidence_ReturnsConflictOutcome()
    {
        Guid eventId = Guid.CreateVersion7();
        DeleteEventCommandHandler handler = CreateHandler(
            eventId,
            out IEventRepository events,
            out IRegistrationInventoryRepository inventory,
            out IActorRepository actors);
        Explore.Domain.Event @event = Event(eventId);
        events.GetById(eventId).Returns(@event);
        actors.GetById(@event.ActorId).Returns(Actor(@event.ActorId));
        inventory.HasPaidEvidenceAsync(eventId, @event.TenantId, Arg.Any<CancellationToken>()).Returns(true);

        BaseCommandResponse<Guid> result = await handler.ExecuteAsync(Command(eventId), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(DeleteEventFailureCodes.PaidEvidenceConflict);
        await Assert.That(result.Id).IsEqualTo(eventId);
    }

    private static DeleteEventCommandHandler CreateHandler(
        Guid eventId,
        out IEventRepository events,
        out IRegistrationInventoryRepository inventory) =>
        CreateHandler(eventId, out events, out inventory, out _);

    private static DeleteEventCommandHandler CreateHandler(
        Guid eventId,
        out IEventRepository events,
        out IRegistrationInventoryRepository inventory,
        out IActorRepository actors)
    {
        events = Substitute.For<IEventRepository>();
        inventory = Substitute.For<IRegistrationInventoryRepository>();
        actors = Substitute.For<IActorRepository>();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(UserId);

        return new DeleteEventCommandHandler(
            events,
            Substitute.For<IEventSessionRepository>(),
            inventory,
            actors,
            Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IRoleRepository>(),
            currentUser,
            NullLogger<DeleteEventCommandHandler>.Instance,
            Substitute.For<HybridCache>(),
            Substitute.For<IUnitOfWork>(),
            AtprotoPublicationPlannerTestFactory.Disabled());
    }

    private static DeleteEventCommand Command(Guid eventId) => new()
    {
        Id = eventId,
        UserId = UserId.ToString()
    };

    private static Explore.Domain.Event Event(Guid eventId) => new()
    {
        Id = eventId,
        TenantId = Guid.CreateVersion7(),
        ActorId = Guid.CreateVersion7(),
        Title = "Deletion contract event",
        Actor = null!,
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

    private static Actor Actor(Guid actorId) => new()
    {
        Id = actorId,
        UserId = UserId,
        ActorType = null!,
        Pii = null!
    };

    private static readonly Guid UserId = Guid.Parse("01999a01-0000-7000-8000-000000000001");
}
