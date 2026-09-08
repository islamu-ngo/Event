using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public class EventSessionSpeakerServiceTests
{
    private readonly IEventSessionSpeakerClient _apiClient;
    private readonly EventSessionSpeakerService _service;

    public EventSessionSpeakerServiceTests()
    {
        _apiClient = Substitute.For<IEventSessionSpeakerClient>();
        _service = new EventSessionSpeakerService(_apiClient);
    }

    [Test]
    public async Task GetSpeakersBySessionAsync_ForwardsSessionIdAndReturnsHalCollection()
    {
        var sessionId = Guid.NewGuid();
        var collection = new HalCollectionResourceOfEventSessionSpeakerListDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["create"] = new() { Href = $"/api/eventsessionspeaker/management/by-session/{sessionId}" }
            }
        };

        _apiClient.GetEventSessionSpeakersBySessionAsync(
                sessionId,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(collection);

        var result = await _service.GetSpeakersBySessionAsync(sessionId);

        await Assert.That(result).IsSameReferenceAs(collection);
        await Assert.That(result.HasLink("create")).IsTrue();
        await _apiClient.Received(1).GetEventSessionSpeakersBySessionAsync(
            sessionId,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetSpeakersBySessionAsync_ReturnsEmptyCollection_WhenApiRejectsRequest()
    {
        var sessionId = Guid.NewGuid();
        _apiClient.GetEventSessionSpeakersBySessionAsync(
                sessionId,
                cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException(403));

        var result = await _service.GetSpeakersBySessionAsync(sessionId);

        await Assert.That(result.GetItems()).IsEmpty();
        await Assert.That(result.HasLink("create")).IsFalse();
    }

    [Test]
    public async Task AddSpeakerToSessionAsync_UsesSessionRouteAndTypedBody()
    {
        var sessionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var response = new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = Guid.NewGuid(),
            Message = "Speaker assigned to session successfully."
        };

        _apiClient.CreateEventSessionSpeakerAsync(
                sessionId,
                Arg.Any<CreateEventSessionSpeakerDto>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.AddSpeakerToSessionAsync(sessionId, actorId);

        await Assert.That(result).IsSameReferenceAs(response);
        await _apiClient.Received(1).CreateEventSessionSpeakerAsync(
            sessionId,
            Arg.Is<CreateEventSessionSpeakerDto>(dto =>
                dto.EventSessionId == sessionId &&
                dto.ActorId == actorId),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddSpeakerToSessionAsync_ReturnsNull_WhenApiRejectsRequest()
    {
        var sessionId = Guid.NewGuid();
        _apiClient.CreateEventSessionSpeakerAsync(
                sessionId,
                Arg.Any<CreateEventSessionSpeakerDto>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException(403));

        var result = await _service.AddSpeakerToSessionAsync(sessionId, Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task RemoveSpeakerFromSessionAsync_UsesSessionRouteAndSpeakerId()
    {
        var sessionId = Guid.NewGuid();
        var speakerId = Guid.NewGuid();
        _apiClient.DeleteEventSessionSpeakerAsync(
                sessionId,
                speakerId,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _service.RemoveSpeakerFromSessionAsync(sessionId, speakerId);

        await Assert.That(result).IsTrue();
        await _apiClient.Received(1).DeleteEventSessionSpeakerAsync(
            sessionId,
            speakerId,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RemoveSpeakerFromSessionAsync_ReturnsFalse_WhenApiRejectsRequest()
    {
        var sessionId = Guid.NewGuid();
        var speakerId = Guid.NewGuid();
        _apiClient.DeleteEventSessionSpeakerAsync(
                sessionId,
                speakerId,
                cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException(404));

        var result = await _service.RemoveSpeakerFromSessionAsync(sessionId, speakerId);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task EventSessionSpeakerHalCollection_GetItemsPreservesItemLinks()
    {
        var speakerId = Guid.NewGuid();
        var embeddedItem = HalLinkTestFactory.WithLinks(new HalResourceOfEventSessionSpeakerListDto
        {
            Id = speakerId,
            ActorDisplayName = "Speaker One"
        }, new HalLinkTestLink("delete", "/api/eventsessionspeaker/management/by-session/session/speaker"));
        var collection = new HalCollectionResourceOfEventSessionSpeakerListDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["create"] = new() { Href = "/api/eventsessionspeaker/management/by-session/session" }
            },
            _embedded = new HalCollectionEmbeddedOfEventSessionSpeakerListDto
            {
                Items = new List<HalResourceOfEventSessionSpeakerListDto> { embeddedItem }
            }
        };

        var item = collection.GetItems().Single();

        await Assert.That(collection.HasLink("create")).IsTrue();
        await Assert.That(item.Id).IsEqualTo(speakerId);
        await Assert.That(item.ActorDisplayName).IsEqualTo("Speaker One");
        await Assert.That(item.HasHalLink("delete")).IsTrue();
    }

    private static ApiException CreateApiException(int statusCode)
        => new(
            "API request failed.",
            statusCode,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            null);
}
