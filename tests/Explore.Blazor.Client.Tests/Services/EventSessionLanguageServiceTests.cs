namespace Explore.Blazor.Client.Tests.Services;

public class EventSessionLanguageServiceTests
{
    private readonly IEventSessionLanguageClient _apiClient;
    private readonly EventSessionLanguageService _service;

    public EventSessionLanguageServiceTests()
    {
        _apiClient = Substitute.For<IEventSessionLanguageClient>();
        _service = new EventSessionLanguageService(_apiClient);
    }

    [Test]
    public async Task GetLanguagesBySessionAsync_ForwardsSessionId()
    {
        var sessionId = Guid.NewGuid();
        var languages = new List<EventSessionLanguageListDto>
        {
            new() { Id = 7, EventSessionId = sessionId, LanguageId = 1, LanguageFullName = "English" }
        };
        _apiClient.GetEventSessionLanguagesAsync(sessionId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ToHalCollection(languages)));

        var result = await _service.GetLanguagesBySessionAsync(sessionId);

        await Assert.That(result).IsEquivalentTo(languages);
        await _apiClient.Received(1).GetEventSessionLanguagesAsync(sessionId, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetManagedLanguagesBySessionAsync_ForwardsEventAndSessionIds()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _apiClient.GetManagedEventSessionLanguagesAsync(
                eventId,
                sessionId,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ToHalCollection([])));

        await _service.GetManagedLanguagesBySessionAsync(eventId, sessionId);

        await _apiClient.Received(1).GetManagedEventSessionLanguagesAsync(
            eventId,
            sessionId,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncLanguagesForSessionAsync_CreatesMissingAndDeletesRemovedLanguages()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _apiClient.GetManagedEventSessionLanguagesAsync(eventId, sessionId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ToHalCollection(new List<EventSessionLanguageListDto>
            {
                new() { Id = 10, EventSessionId = sessionId, LanguageId = 1 },
                new() { Id = 11, EventSessionId = sessionId, LanguageId = 3 }
            })));
        _apiClient.CreateEventSessionLanguageAsync(
                Arg.Any<CreateEventSessionLanguageDto>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfint { Success = true, Id = 12 });

        var result = await _service.SyncLanguagesForSessionAsync(eventId, sessionId, [1, 2]);

        await Assert.That(result).IsTrue();
        await _apiClient.Received(1).CreateEventSessionLanguageAsync(
            Arg.Is<CreateEventSessionLanguageDto>(dto => dto.EventSessionId == sessionId && dto.LanguageId == 2),
            cancellationToken: Arg.Any<CancellationToken>());
        await _apiClient.Received(1).DeleteEventSessionLanguageAsync(11, cancellationToken: Arg.Any<CancellationToken>());
        await _apiClient.DidNotReceive().DeleteEventSessionLanguageAsync(10, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncLanguagesForSessionAsync_ReturnsFalse_WhenCreateFails()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _apiClient.GetManagedEventSessionLanguagesAsync(eventId, sessionId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ToHalCollection([])));
        _apiClient.CreateEventSessionLanguageAsync(
                Arg.Any<CreateEventSessionLanguageDto>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfint { Success = false, Message = "Nope" });

        var result = await _service.SyncLanguagesForSessionAsync(eventId, sessionId, [2]);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SyncLanguagesForSessionAsync_ReturnsFalse_WhenDeleteApiFails()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _apiClient.GetManagedEventSessionLanguagesAsync(eventId, sessionId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ToHalCollection(new List<EventSessionLanguageListDto>
            {
                new() { Id = 11, EventSessionId = sessionId, LanguageId = 3 }
            })));
        _apiClient.DeleteEventSessionLanguageAsync(11, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(
                "Not found",
                404,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));

        var result = await _service.SyncLanguagesForSessionAsync(eventId, sessionId, []);

        await Assert.That(result).IsFalse();
    }

    private static HalCollectionResourceOfEventSessionLanguageListDto ToHalCollection(
        IEnumerable<EventSessionLanguageListDto> languages) => new()
        {
            _embedded = new HalCollectionEmbeddedOfEventSessionLanguageListDto
            {
                Items = languages.Select(language => new HalResourceOfEventSessionLanguageListDto
                {
                    Id = language.Id,
                    EventSessionId = language.EventSessionId,
                    EventId = language.EventId,
                    TenantId = language.TenantId,
                    LanguageId = language.LanguageId,
                    LanguageFullName = language.LanguageFullName
                }).ToList()
            }
        };
}
