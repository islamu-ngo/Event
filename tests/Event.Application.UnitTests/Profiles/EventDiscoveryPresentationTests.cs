using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventDiscoveryPresentationTests
{
    [Test]
    public async Task ExplicitNativeNullCannotFallBackToPrivateActorGraph()
    {
        var entity = Create();
        entity.SetDiscoveryPresentation(new(null, null, null, 0, null, null, null, null, false, null));
        var result = EventMapper.ToListItem(entity);
        await Assert.That(result.ActorDisplayName).IsNull();
        await Assert.That(result.ActorProfilePictureUri).IsNull();
        await Assert.That(result.ActorUserId).IsNull();
        await Assert.That(result.FeaturedImageUri).IsNull();
        await Assert.That(result.TicketPriceSummary).IsNull();
    }

    [Test]
    public async Task PublicOutcomesMapWithoutStorageFormOrTicketEntities()
    {
        var entity = Create();
        entity.Actor = null!;
        entity.SetDiscoveryPresentation(new("/api/storageobject/01900000-0000-7000-8000-000000000001/public",
            "Public organizer", "https://images.example.org/actor.png", 1, "User", null, null, null, false,
            new("MIXED_WITH_FREE", "EUR", 0)));
        var result = EventMapper.ToListItem(entity);
        await Assert.That(result.ActorDisplayName).IsEqualTo("Public organizer");
        await Assert.That(result.ActorProfilePictureUri).IsEqualTo("https://images.example.org/actor.png");
        await Assert.That(result.TicketPriceSummary!.CurrencyMinorUnitDigits).IsEqualTo(2);
        await Assert.That(result.TicketPriceSummary.FromAmountMinor).IsEqualTo(0);
        await Assert.That(entity.FeaturedImage).IsNull();
        await Assert.That(entity.TicketCatalogVersions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NonNativeListRetainsExistingActorProjection()
    {
        var result = EventMapper.ToListItem(Create());
        await Assert.That(result.ActorDisplayName).IsEqualTo("Private graph witness");
    }

    private static Explore.Domain.Event Create() => new(EventStatusEnum.Published)
    {
        Title = "Card",
        Actor = new Actor
        {
            ActorType = null!,
            UserId = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = "Private graph witness", ExternalProfilePictureUri = "https://example.org/private.png" }
        },
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };
}
