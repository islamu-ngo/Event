using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explore.Blazor.Client.Tests.Services;

public class EventSessionServiceTests
{
    private readonly IEventSessionClient _sessionClient;
    private readonly IEventSessionGroupClient _sessionGroupClient;
    private readonly IEventManagementReadClient _managementReadClient;
    private readonly ILogger<EventSessionService> _logger;
    private readonly EventSessionService _service;

    public EventSessionServiceTests()
    {
        _sessionClient = Substitute.For<IEventSessionClient>();
        _sessionGroupClient = Substitute.For<IEventSessionGroupClient>();
        _managementReadClient = Substitute.For<IEventManagementReadClient>();
        _logger = Substitute.For<ILogger<EventSessionService>>();
        _service = new EventSessionService(
            _sessionClient,
            _sessionGroupClient,
            _managementReadClient,
            _logger);
    }

    [Test]
    public async Task UpdateSessionGroupAsync_ForwardsRouteIdGroupedBodyAndIfMatch()
    {
        var sectionId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var request = new UpdateEventSessionGroupRequestDto
        {
            Metadata = new UpdateEventSessionGroupMetadataDto { Name = "Main stage" }
        };
        _sessionGroupClient.UpdateEventSessionGroupAsync(
                sectionId,
                request,
                $"\"{concurrencyStamp:D}\"",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Id = sectionId, Success = true });

        var result = await _service.UpdateSessionGroupAsync(sectionId, concurrencyStamp, request);

        await Assert.That(result.Success).IsTrue();
        await _sessionGroupClient.Received(1).UpdateEventSessionGroupAsync(
            sectionId,
            request,
            $"\"{concurrencyStamp:D}\"",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AssignSessionToGroupAsync_ForwardsParameters()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _sessionGroupClient.AssignEventSessionToGroupAsync(
                groupId,
                Arg.Any<AssignSessionToGroupRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Id = sessionId, Success = true });

        var result = await _service.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 1);

        await Assert.That(result.Success).IsTrue();
        await _sessionGroupClient.Received(1).AssignEventSessionToGroupAsync(
            groupId,
            Arg.Is<AssignSessionToGroupRequestDto>(dto =>
                dto.EventId == eventId &&
                dto.EventSessionGroupId == groupId &&
                dto.EventSessionId == sessionId &&
                dto.IsPrimary == true &&
                dto.SortOrder == 1),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnassignSessionFromGroupAsync_ForwardsParameters()
    {
        var eventId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _sessionGroupClient.UnassignEventSessionFromGroupAsync(
                groupId,
                sessionId,
                eventId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _service.UnassignSessionFromGroupAsync(eventId, groupId, sessionId);

        await Assert.That(result.Success).IsTrue();
        await _sessionGroupClient.Received(1).UnassignEventSessionFromGroupAsync(
            groupId,
            sessionId,
            eventId,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetSessionsByEventAsync_WhenManagedSessionsNotRequested_UsesPublicSessionsOnly()
    {
        var eventId = Guid.NewGuid();
        var session = new HalResourceOfEventSessionListDto { Id = Guid.NewGuid(), Title = "Public session" };

        _sessionClient.GetEventSessionsAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventSessionListDto
            {
                _embedded = new HalCollectionEmbeddedOfEventSessionListDto { Items = [session] }
            });

        var result = await _service.GetSessionsByEventAsync(eventId, includeManagedSessions: false);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result.First().Title).IsEqualTo("Public session");
    }

    [Test]
    public async Task GetSessionsByEventAsync_WhenManagedRequested_ReturnsManagedSessions()
    {
        var eventId = Guid.NewGuid();
        var session = new HalResourceOfEventSessionListDto { Id = Guid.NewGuid(), Title = "Managed session" };

        _sessionClient.GetManagedEventSessionsByEventAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventSessionListDto
            {
                _embedded = new HalCollectionEmbeddedOfEventSessionListDto { Items = [session] }
            });

        var result = await _service.GetSessionsByEventAsync(eventId, includeManagedSessions: true);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result.First().Title).IsEqualTo("Managed session");
    }

    [Test]
    public async Task CreateSessionAsync_ForwardsRequest()
    {
        var sessionDto = new CreateEventSessionDto
        {
            EventId = Guid.NewGuid(),
            Title = "New session"
        };
        var expectedId = Guid.NewGuid();

        _sessionClient.CreateEventSessionAsync(
                sessionDto,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Id = expectedId, Success = true });

        var result = await _service.CreateSessionAsync(sessionDto);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(expectedId);
    }

    [Test]
    public async Task DeleteSessionAsync_CallsClientAndReturnsTrue()
    {
        var sessionId = Guid.NewGuid();

        var result = await _service.DeleteSessionAsync(sessionId);

        await Assert.That(result).IsTrue();
        await _sessionClient.Received(1).DeleteEventSessionAsync(
            sessionId,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetSessionByIdAsync_ReturnsSession_WhenExists()
    {
        var sessionId = Guid.NewGuid();

        _sessionClient.GetEventSessionByIdAsync(sessionId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfEventSessionDto
            {
                Id = sessionId,
                Title = "Found session"
            });

        var result = await _service.GetSessionByIdAsync(sessionId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsEqualTo("Found session");
    }

    [Test]
    public async Task GetManagedSessionByIdAsync_ReturnsManagedSession_WhenExists()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _sessionClient.GetManagedEventSessionByIdAsync(eventId, sessionId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfEventSessionDto
            {
                Id = sessionId,
                EventId = eventId,
                Title = "Managed detail session"
            });

        var result = await _service.GetManagedSessionByIdAsync(eventId, sessionId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsEqualTo("Managed detail session");
    }

    [Test]
    public async Task GetManagedSessionGroupsByEventAsync_ReturnsSessionGroups()
    {
        var eventId = Guid.NewGuid();
        var group = new HalResourceOfEventSessionGroupListDto
        {
            Id = Guid.NewGuid(),
            Name = "Track 1"
        };

        _sessionGroupClient.GetManagedEventSessionGroupsByEventAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventSessionGroupListDto
            {
                _embedded = new HalCollectionEmbeddedOfEventSessionGroupListDto { Items = [group] }
            });

        var result = await _service.GetManagedSessionGroupsByEventAsync(eventId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result.First().Name).IsEqualTo("Track 1");
    }
}
