using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Lookup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public class EventLookupServiceTests
{
    private readonly IEventTypeClient _eventTypeClient = Substitute.For<IEventTypeClient>();
    private readonly IEventFormatClient _eventFormatClient = Substitute.For<IEventFormatClient>();
    private readonly IEventStatusClient _eventStatusClient = Substitute.For<IEventStatusClient>();
    private readonly IEventSessionKindClient _eventSessionKindClient = Substitute.For<IEventSessionKindClient>();
    private readonly IRegistrationModeClient _registrationModeClient = Substitute.For<IRegistrationModeClient>();
    private readonly IVisibilityTypeClient _visibilityTypeClient = Substitute.For<IVisibilityTypeClient>();
    private readonly EventLookupService _service;

    public EventLookupServiceTests()
    {
        _service = new EventLookupService(
            _eventTypeClient,
            _eventFormatClient,
            _eventStatusClient,
            _eventSessionKindClient,
            _registrationModeClient,
            _visibilityTypeClient,
            NullLogger<EventLookupService>.Instance);
    }

    [Test]
    public async Task GetEventTypesAsync_Success_ReturnsItems()
    {
        var items = new List<EventTypeListDto> { new() { Id = 1, FullName = "Conference" } };
        _eventTypeClient.GetEventTypesAsync().Returns(items);

        var result = await _service.GetEventTypesAsync();

        await Assert.That(result).IsNotEmpty();
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetEventTypesAsync_ApiException_ReturnsEmpty()
    {
        _eventTypeClient.GetEventTypesAsync().ThrowsAsync(new ApiException("Error", 500, "", new Dictionary<string, IEnumerable<string>>(), null));

        var result = await _service.GetEventTypesAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetEventFormatsAsync_Success_ReturnsItems()
    {
        var items = new List<EventFormatListDto> { new() { Id = 1, FullName = "In-Person" } };
        _eventFormatClient.GetEventFormatOptionsAsync().Returns(items);

        var result = await _service.GetEventFormatsAsync();

        await Assert.That(result).IsNotEmpty();
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetRegistrationModesAsync_Success_ReturnsItems()
    {
        var items = new List<RegistrationModeListDto> { new() { Id = 1, FullName = "Open" } };
        _registrationModeClient.GetRegistrationModesAsync().Returns(items);

        var result = await _service.GetRegistrationModesAsync();

        await Assert.That(result).IsNotEmpty();
        await Assert.That(result.Count).IsEqualTo(1);
    }
}
