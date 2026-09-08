using System.Text.Json;
using Blazouter.Services;
using Explore.Blazor.Client.Components.EventReporting;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Shared;

namespace Explore.Blazor.Client.Tests.Security;

[Category("EventLocationPrivacy")]
public sealed class EventLocationPrivacyStageAContractTests : IDisposable
{
    private const string LocationName = "PRIVATE-VENUE-NAME";
    private const string RoomName = "PRIVATE-ROOM-NAME";
    private const string Address = "PRIVATE-STREET-ADDRESS";
    private const string Postcode = "PRIVATE-POSTCODE";
    private const string City = "PRIVATE-CITY";
    private const string Country = "PRIVATE-COUNTRY";
    private const string Latitude = "50.8466001";
    private const string Longitude = "4.3528001";

    private readonly BlazorTestContext _context = new();

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task PublicFilterBoundary_DoesNotExposePhysicalLocationIds()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var filterBar = new EventFilterBar();

        var url = EventFilterUrlHelper.BuildUrl(navigation, filterBar);

        await Assert.That(typeof(EventFilterBar).GetProperty("SelectedLocationIds")).IsNull();
        await Assert.That(url).DoesNotContain("locationIds");
    }

    [Test]
    public async Task EventDetailJsonLd_OmitsSeededPhysicalSessionValues()
    {
        var (eventDto, session, physicalValues) = CreatePhysicalEvent();
        RegisterEventDetailServices(eventDto, session);

        var head = _context.Render<HeadOutlet>();
        _context.RenderMudComponent<EventDetail>();
        var script = head.WaitForElement("script[type='application/ld+json']", TimeSpan.FromSeconds(3));
        using var json = JsonDocument.Parse(script.TextContent);
        var serializedJsonLd = json.RootElement.GetRawText();

        foreach (var physicalValue in physicalValues)
        {
            await Assert.That(serializedJsonLd).DoesNotContain(physicalValue);
        }
    }

    [Test]
    public async Task EventDetailVisibleCopy_OmitsSeededPhysicalSessionValuesAndPrivateAddressPromise()
    {
        var (eventDto, session, physicalValues) = CreatePhysicalEvent();
        RegisterEventDetailServices(eventDto, session);

        var cut = _context.RenderMudComponent<EventDetail>();
        cut.WaitForState(
            () => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(3));

        foreach (var physicalValue in physicalValues)
        {
            await Assert.That(cut.Markup).DoesNotContain(physicalValue);
        }

        await Assert.That(cut.Markup)
            .DoesNotContain("Register to see any private address details");
    }

    private void RegisterEventDetailServices(EventDto eventDto, EventSessionListDto session)
    {
        _context.SetAnonymousUser();
        _context.JSInterop.SetupVoid("window.scrollTo", _ => true).SetVoidResult();
        _context.JSInterop
            .Setup<string>("Blazor._internal.PageTitle.getAndRemoveExistingTitle")
            .SetResult(string.Empty);

        var eventService = Substitute.For<IEventService>();
        eventService.GetEventByIdAsync(Arg.Any<Guid>()).Returns(eventDto);

        var eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        eventSessionService.GetSessionsByEventAsync(Arg.Any<Guid>(), Arg.Any<bool>())
            .Returns(new List<EventSessionListDto> { session });

        var eventDayService = Substitute.For<IEventDayService>();
        eventDayService.GetDaysByEventAsync(Arg.Any<Guid>())
            .Returns(new List<EventDayListDto>());

        var eventAgendaItemService = Substitute.For<IEventAgendaItemService>();
        eventAgendaItemService.GetAgendaItemsByEventAsync(Arg.Any<Guid>())
            .Returns(new List<EventAgendaItemListDto>());

        var sessionAgendaItemService = Substitute.For<IEventSessionAgendaItemService>();
        sessionAgendaItemService.GetAgendaItemsBySessionAsync(Arg.Any<Guid>())
            .Returns(new List<EventSessionAgendaItemListDto>());

        _context.Services.AddSingleton(eventService);
        _context.Services.AddSingleton(eventSessionService);
        _context.Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventModerationService>());
        _context.Services.AddSingleton(Substitute.For<IMapsService>());
        _context.Services.AddScoped<RouterStateService>();
        _context.Services.AddSingleton(Substitute.For<IUserService>());
        _context.Services.AddSingleton(Substitute.For<IEventAspectService>());
        _context.Services.AddSingleton(sessionAgendaItemService);
        _context.Services.AddSingleton(eventAgendaItemService);
        _context.Services.AddSingleton(eventDayService);
        _context.Services.AddSingleton(Substitute.For<IActorSubscriptionService>());
        _context.Services.AddScoped<MainContentAppearanceState>();
        _context.Services.AddSingleton(Substitute.For<ILogger<EventDetail>>());
    }

    private static (EventDto Event, EventSessionListDto Session, string[] PhysicalValues) CreatePhysicalEvent()
    {
        var eventId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var start = TestTime.UtcNow.AddDays(7);
        var session = new EventSessionListDto
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventTitle = "Public privacy contract event",
            Title = "Public session",
            StartTime = start,
            EndTime = start.AddHours(2),
            LocationId = locationId,
            LocationFullName = LocationName,
            LocationCity = City,
            RoomId = roomId,
            RoomName = RoomName,
            AdditionalProperties = new Dictionary<string, object>
            {
                ["locationAddress"] = Address,
                ["postcode"] = Postcode,
                ["locationCountry"] = Country,
                ["latitude"] = Latitude,
                ["longitude"] = Longitude
            }
        };

        var eventDto = new EventDto
        {
            Id = eventId,
            ConcurrencyStamp = Guid.NewGuid(),
            Title = "Public privacy contract event",
            Content = "A public event whose physical venue remains private.",
            ActorId = Guid.NewGuid(),
            ActorDisplayName = "Privacy organizer",
            ActorTypeId = 2,
            ActorTypeFullName = "Organization",
            EventTypeFullName = "Program",
            EventStatusId = 2,
            EventStatusFullName = "Published",
            EventStatusMasterCode = "PUBLISHED",
            EventFormatId = 1,
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON",
            VisibilityTypeId = 1,
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            FirstSessionDate = start,
            LastSessionDate = start.AddHours(2)
        };

        return (
            eventDto,
            session,
            [
                locationId.ToString(),
                LocationName,
                roomId.ToString(),
                RoomName,
                Address,
                Postcode,
                City,
                Country,
                Latitude,
                Longitude
            ]);
    }
}
