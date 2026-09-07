using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Models.Events;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class EventDetailLocationPrivacyTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task PublicView_RendersCoarseLabelWithoutExactData()
    {
        var view = EventLocationDisclosureView.FromPublic(new EventLocationPublicDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Available,
            Fields = new EventLocationPublicFieldsDto
            {
                VenueName = "Community Centre",
                City = "Brussels",
                Country = "BE"
            }
        });

        var cut = Render(view);

        await Assert.That(cut.Find("[data-testid='event-location-headline']").TextContent.Trim())
            .IsEqualTo("Community Centre");
        await Assert.That(cut.Find("[data-testid='event-location-coarse']").TextContent)
            .Contains("Brussels");
        await Assert.That(cut.FindAll("[data-testid='event-location-exact']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='event-location-street']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='event-location-postcode']")).IsEmpty();
    }

    [Test]
    public async Task PublicPrivateVenue_ShowsGenericLabelAndNoAddress()
    {
        var view = EventLocationDisclosureView.FromPublic(new EventLocationPublicDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Private_venue,
            Fields = new EventLocationPublicFieldsDto { VenueName = "Private venue", City = "Brussels" }
        });

        var cut = Render(view);

        await Assert.That(cut.Find("[data-testid='event-location-headline']").TextContent.Trim())
            .IsEqualTo("Private venue");
        await Assert.That(cut.FindAll("[data-testid='event-location-exact']")).IsEmpty();
        await Assert.That(cut.Find("[data-testid='event-location-private-hint']").TextContent)
            .Contains("shared with participants");
    }

    [Test]
    public async Task ToBeAnnounced_RendersTheBadgeAndNoVenueClaim()
    {
        var view = EventLocationDisclosureView.FromPublic(new EventLocationPublicDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.To_be_announced
        });

        var cut = Render(view);

        await Assert.That(cut.FindAll("[data-testid='event-location-tba-badge']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='event-location-headline']").TextContent)
            .Contains("to be announced");
        await Assert.That(cut.FindAll("[data-testid='event-location-exact']")).IsEmpty();
    }

    [Test]
    public async Task NoDisclosureAtAll_FallsBackToTheToBeAnnouncedState()
    {
        var cut = Render(null);

        await Assert.That(cut.FindAll("[data-testid='event-location-tba-badge']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task AttendeeView_RendersExactAddressAndRetentionNotice()
    {
        var view = EventLocationDisclosureView.FromAttendee(new EventLocationAttendeeDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Available,
            Fields = new EventLocationAttendeeFieldsDto
            {
                VenueName = "Private venue",
                City = "Brussels",
                StreetAddress = "Rue Neuve 1",
                Postcode = "1000",
                Latitude = 50.85,
                Longitude = 4.35,
                MapUrl = "https://maps.example/1"
            }
        });

        var cut = Render(view);

        await Assert.That(cut.Find("[data-testid='event-location-street']").TextContent).Contains("Rue Neuve 1");
        await Assert.That(cut.Find("[data-testid='event-location-postcode']").TextContent).Contains("1000");
        await Assert.That(cut.FindAll("[data-testid='event-location-map']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("do not republish");
    }

    [Test]
    public async Task AttendeeViewBeforeReveal_ShowsNoExactDataEvenWhenRegistered()
    {
        // The server withholds exact fields until reveal time; the client must not invent a placeholder.
        var view = EventLocationDisclosureView.FromAttendee(new EventLocationAttendeeDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Private_venue,
            Fields = new EventLocationAttendeeFieldsDto { VenueName = "Private venue", Country = "BE" }
        });

        var cut = Render(view);

        await Assert.That(cut.FindAll("[data-testid='event-location-exact']")).IsEmpty();
        await Assert.That(view.HasExactDetail).IsFalse();
    }

    [Test]
    public async Task Prefer_UsesTheAttendeeViewOnlyWhenItActuallyAddsDetail()
    {
        var publicView = EventLocationDisclosureView.FromPublic(new EventLocationPublicDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Available,
            Fields = new EventLocationPublicFieldsDto { VenueName = "Community Centre", City = "Brussels" }
        });
        var emptyAttendee = EventLocationDisclosureView.FromAttendee(new EventLocationAttendeeDto
        {
            EventLocationId = publicView.EventLocationId,
            State = EventLocationDisclosureState.Available
        });
        var richAttendee = EventLocationDisclosureView.FromAttendee(new EventLocationAttendeeDto
        {
            EventLocationId = publicView.EventLocationId,
            State = EventLocationDisclosureState.Available,
            Fields = new EventLocationAttendeeFieldsDto { StreetAddress = "Rue Neuve 1" }
        });

        await Assert.That(EventLocationDisclosureView.Prefer(publicView, emptyAttendee)).IsEqualTo(publicView);
        await Assert.That(EventLocationDisclosureView.Prefer(publicView, richAttendee)).IsEqualTo(richAttendee);
        await Assert.That(EventLocationDisclosureView.Prefer(publicView, null)).IsEqualTo(publicView);
        await Assert.That(EventLocationDisclosureView.Prefer(null, richAttendee)).IsEqualTo(richAttendee);
    }

    [Test]
    public async Task PublicProjection_NeverExposesRoomDescriptionField()
    {
        // The public and attendee field contracts intentionally omit room description entirely.
        await Assert.That(typeof(EventLocationPublicFieldsDto).GetProperty("RoomDescription")).IsNull();
        await Assert.That(typeof(EventLocationAttendeeFieldsDto).GetProperty("RoomDescription")).IsNull();
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<EventLocationDisclosureCard> Render(EventLocationDisclosureView? view) =>
        _ctx.Render<EventLocationDisclosureCard>(parameters =>
            parameters.Add(component => component.View, view));
}
