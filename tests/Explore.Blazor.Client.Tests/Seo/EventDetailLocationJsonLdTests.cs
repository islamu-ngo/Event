using System.Reflection;
using System.Text.Json;
using Explore.Blazor.Client.Models.Events;
using Explore.Blazor.Client.Pages.Events;

namespace Explore.Blazor.Client.Tests.Seo;

public sealed class EventDetailLocationJsonLdTests
{
    [Test]
    public async Task JsonLdLocation_EmitsOnlyVenueNameCityAndCountry()
    {
        var page = CreatePageWithPublicView(new EventLocationPublicFieldsDto
        {
            VenueName = "Community Centre",
            City = "Brussels",
            Country = "BE",
            // Even if a policy ever released these publicly, structured data must not carry them.
            StreetAddress = "Rue Neuve 1",
            Postcode = "1000",
            Latitude = 50.85,
            Longitude = 4.35,
            Geohash = "u151"
        });

        string json = SerializeLocation(page);

        await Assert.That(json).Contains("\"Place\"");
        await Assert.That(json).Contains("Community Centre");
        await Assert.That(json).Contains("Brussels");
        await Assert.That(json).Contains("\"addressCountry\":\"BE\"");
        await Assert.That(json).DoesNotContain("Rue Neuve");
        await Assert.That(json).DoesNotContain("1000");
        await Assert.That(json).DoesNotContain("50.85");
        await Assert.That(json).DoesNotContain("u151");
        await Assert.That(json).DoesNotContain("streetAddress");
        await Assert.That(json).DoesNotContain("postalCode");
        await Assert.That(json).DoesNotContain("geo");
    }

    [Test]
    public async Task JsonLdLocation_IsOmittedForToBeAnnouncedEvents()
    {
        var page = CreatePageWithPublicView(
            new EventLocationPublicFieldsDto { City = "Brussels" },
            EventLocationDisclosureState.To_be_announced);

        await Assert.That(InvokeBuildSchemaLocation(page)).IsNull();
    }

    [Test]
    public async Task JsonLdLocation_IsOmittedWhenNothingWasDisclosedPublicly()
    {
        var page = new EventDetail();

        await Assert.That(InvokeBuildSchemaLocation(page)).IsNull();
    }

    [Test]
    public async Task JsonLdLocation_IsOmittedWhenTheVenueNeedsPrivacyReview()
    {
        var page = CreatePageWithPublicView(
            new EventLocationPublicFieldsDto { VenueName = "Community Centre" },
            EventLocationDisclosureState.Needs_privacy_review);

        await Assert.That(InvokeBuildSchemaLocation(page)).IsNull();
    }

    [Test]
    public async Task JsonLdLocation_NeverReadsTheAttendeeProjection()
    {
        var page = new EventDetail();
        SetPrivateField(page, "_attendeeLocationView", EventLocationDisclosureView.FromAttendee(
            new EventLocationAttendeeDto
            {
                EventLocationId = Guid.NewGuid(),
                State = EventLocationDisclosureState.Available,
                Fields = new EventLocationAttendeeFieldsDto
                {
                    VenueName = "Private venue",
                    StreetAddress = "Rue Neuve 1"
                }
            }));

        await Assert.That(InvokeBuildSchemaLocation(page)).IsNull();
    }

    private static EventDetail CreatePageWithPublicView(
        EventLocationPublicFieldsDto fields,
        EventLocationDisclosureState state = EventLocationDisclosureState.Available)
    {
        var page = new EventDetail();
        SetPrivateField(page, "_publicLocationView", EventLocationDisclosureView.FromPublic(
            new EventLocationPublicDto
            {
                EventLocationId = Guid.NewGuid(),
                State = state,
                Fields = fields
            }));
        return page;
    }

    private static string SerializeLocation(EventDetail page) =>
        JsonSerializer.Serialize(InvokeBuildSchemaLocation(page));

    private static Dictionary<string, object?>? InvokeBuildSchemaLocation(EventDetail page)
    {
        var method = typeof(EventDetail).GetMethod(
            "BuildSchemaLocation",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildSchemaLocation was not found.");
        return (Dictionary<string, object?>?)method.Invoke(page, null);
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(instance, value);
    }
}
