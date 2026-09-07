using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Events.Sessions;
using Explore.Blazor.Client.Services;
using NSubstitute;
using ComposerCreateEventSessionRequest = Explore.Blazor.Client.Clients.CreateEventSessionDto;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventSessionSaveCoordinatorTests
{
    [Test]
    public async Task SaveCreateSessionAsync_CreatesSessionThenAssignsSelectedProgramSection()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = new ComposerCreateEventSessionRequest { Title = "Workshop" };
        eventService.CreateSessionAsync(request).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        eventService.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var result = await EventSessionSaveCoordinator.SaveCreateSessionAsync(
            eventService,
            request,
            eventId,
            groupId,
            savedSessionId: null);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.SessionId).IsEqualTo(sessionId);
        await eventService.Received(1).CreateSessionAsync(request);
        await eventService.Received(1).AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0);
    }

    [Test]
    public async Task SaveCreateSessionAsync_ReusesExistingSavedSessionWhenRetryingAssignment()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = new ComposerCreateEventSessionRequest { Title = "Workshop" };
        eventService.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var result = await EventSessionSaveCoordinator.SaveCreateSessionAsync(
            eventService,
            request,
            eventId,
            groupId,
            sessionId);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.SessionId).IsEqualTo(sessionId);
        await eventService.DidNotReceive().CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>());
        await eventService.Received(1).AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0);
    }

    [Test]
    public async Task SaveUpdateSessionAsync_UnassignsPreviousProgramSectionWhenSelectionIsCleared()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = CreateUpdateRequest(eventId);
        eventService.UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        eventService.UnassignSessionFromGroupAsync(eventId, groupId, sessionId).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var result = await EventSessionSaveCoordinator.SaveUpdateSessionAsync(
            eventService,
            request,
            eventId,
            sessionId,
            expectedConcurrencyStamp,
            selectedSessionGroupId: null,
            initialSessionGroupId: groupId);

        await Assert.That(result.Success).IsTrue();
        await eventService.Received(1).UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request);
        await eventService.Received(1).UnassignSessionFromGroupAsync(eventId, groupId, sessionId);
        await eventService.DidNotReceive().AssignSessionToGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<int>());
    }

    [Test]
    public async Task SaveUpdateSessionAsync_ReturnsAssignmentFailureWithoutClaimingSuccess()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = CreateUpdateRequest(eventId);
        eventService.UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        eventService.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0).Returns(new BaseCommandResponseOfGuid
        {
            Success = false,
            Message = "Nope"
        });

        var result = await EventSessionSaveCoordinator.SaveUpdateSessionAsync(
            eventService,
            request,
            eventId,
            sessionId,
            expectedConcurrencyStamp,
            selectedSessionGroupId: groupId,
            initialSessionGroupId: null);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsEqualTo("Nope");
    }

    [Test]
    public async Task SaveCreateSessionAsync_WhenAssignmentFails_ReturnsCreatedSessionIdForRetry()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = new ComposerCreateEventSessionRequest { Title = "Workshop" };
        eventService.CreateSessionAsync(request).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        eventService.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0).Returns(new BaseCommandResponseOfGuid
        {
            Success = false,
            Message = "Assignment failed"
        });

        var result = await EventSessionSaveCoordinator.SaveCreateSessionAsync(
            eventService,
            request,
            eventId,
            groupId,
            savedSessionId: null);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.SessionId).IsEqualTo(sessionId);
        await Assert.That(result.ErrorMessage).IsEqualTo("Assignment failed");
    }

    [Test]
    public async Task SaveUpdateSessionAsync_WhenProgramSectionIsUnchanged_DoesNotReassign()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = CreateUpdateRequest(eventId);
        eventService.UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var result = await EventSessionSaveCoordinator.SaveUpdateSessionAsync(
            eventService,
            request,
            eventId,
            sessionId,
            expectedConcurrencyStamp,
            selectedSessionGroupId: groupId,
            initialSessionGroupId: groupId);

        await Assert.That(result.Success).IsTrue();
        await eventService.Received(1).UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request);
        await eventService.DidNotReceive().AssignSessionToGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<int>());
        await eventService.DidNotReceive().UnassignSessionFromGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Test]
    public async Task SaveUpdateSessionAsync_AssignsNewProgramSectionAfterUpdate()
    {
        var eventId = Guid.NewGuid();
        var oldGroupId = Guid.NewGuid();
        var newGroupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = CreateUpdateRequest(eventId);
        eventService.UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        eventService.AssignSessionToGroupAsync(eventId, newGroupId, sessionId, true, 0).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var result = await EventSessionSaveCoordinator.SaveUpdateSessionAsync(
            eventService,
            request,
            eventId,
            sessionId,
            expectedConcurrencyStamp,
            selectedSessionGroupId: newGroupId,
            initialSessionGroupId: oldGroupId);

        await Assert.That(result.Success).IsTrue();
        await eventService.Received(1).UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request);
        await eventService.Received(1).AssignSessionToGroupAsync(eventId, newGroupId, sessionId, true, 0);
        await eventService.DidNotReceive().UnassignSessionFromGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Test]
    public async Task SaveUpdateSessionAsync_ReturnsUnassignFailureMessage()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var request = CreateUpdateRequest(eventId);
        eventService.UpdateSessionAsync(sessionId, expectedConcurrencyStamp, request).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        eventService.UnassignSessionFromGroupAsync(eventId, groupId, sessionId).Returns(new BaseCommandResponseOfGuid
        {
            Success = false,
            Message = "Cannot unassign"
        });

        var result = await EventSessionSaveCoordinator.SaveUpdateSessionAsync(
            eventService,
            request,
            eventId,
            sessionId,
            expectedConcurrencyStamp,
            selectedSessionGroupId: null,
            initialSessionGroupId: groupId);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsEqualTo("Cannot unassign");
        await eventService.Received(1).UnassignSessionFromGroupAsync(eventId, groupId, sessionId);
    }

    private static UpdateEventSessionDto CreateUpdateRequest(Guid eventId) => new()
    {
        Event = new UpdateEventSessionEventDto { EventId = eventId },
        Title = new UpdateEventSessionTitleDto
        {
            Value = new OptionalUpdateOfstring { HasValue = true, Value = "Panel" }
        }
    };
}
