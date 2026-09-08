namespace Event.Domain.UnitTests.Entities;

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

public class EventTests
{
    [Test]
    public async Task Event_ImplementsTenantEntityInterface_ExpectedBehavior()
    {
        await Assert.That(typeof(global::Explore.Domain.Event).GetInterfaces().Contains(typeof(ITenantEntity))).IsTrue();
    }

    [Test]
    public async Task Event_ImplementsAuditableEntityInterface_ExpectedBehavior()
    {
        await Assert.That(typeof(global::Explore.Domain.Event).GetInterfaces().Contains(typeof(IAuditableEntity))).IsTrue();
    }

    [Test]
    public async Task Event_ImplementsSoftDeletableInterface_ExpectedBehavior()
    {
        await Assert.That(typeof(global::Explore.Domain.Event).GetInterfaces().Contains(typeof(ISoftDeletable))).IsTrue();
    }

    [Test]
    public async Task RequiredProperties_AreMarkedAsRequired_ExpectedBehavior()
    {
        await Assert.That(IsRequiredProperty<global::Explore.Domain.Event>(nameof(global::Explore.Domain.Event.Title))).IsTrue();
        await Assert.That(IsRequiredProperty<global::Explore.Domain.Event>(nameof(global::Explore.Domain.Event.Actor))).IsTrue();
        await Assert.That(IsRequiredProperty<global::Explore.Domain.Event>(nameof(global::Explore.Domain.Event.Tenant))).IsTrue();
        await Assert.That(IsRequiredProperty<global::Explore.Domain.Event>(nameof(global::Explore.Domain.Event.VisibilityType))).IsTrue();
        await Assert.That(IsRequiredProperty<global::Explore.Domain.Event>(nameof(global::Explore.Domain.Event.EventStatus))).IsTrue();
        await Assert.That(IsRequiredProperty<global::Explore.Domain.Event>(nameof(global::Explore.Domain.Event.EventFormat))).IsTrue();
    }

    [Test]
    public async Task Event_WhenCreated_HasExpectedDefaults()
    {
        var entity = CreateEvent();

        await Assert.That(entity.TotalViews).IsEqualTo(0);
        await Assert.That(entity.IsDeleted).IsFalse();
    }

    [Test]
    public async Task TicketingNavigations_AreReadonlyAggregateOwnedCollections()
    {
        var catalogProperty = typeof(global::Explore.Domain.Event).GetProperty(
            nameof(global::Explore.Domain.Event.TicketCatalogVersions));
        var poolProperty = typeof(global::Explore.Domain.Event).GetProperty(
            nameof(global::Explore.Domain.Event.CapacityPools));

        await Assert.That(catalogProperty?.PropertyType)
            .IsEqualTo(typeof(IReadOnlyCollection<Explore.Domain.EventTicketCatalogVersion>));
        await Assert.That(poolProperty?.PropertyType)
            .IsEqualTo(typeof(IReadOnlyCollection<Explore.Domain.EventCapacityPool>));
        await Assert.That(catalogProperty?.SetMethod).IsNull();
        await Assert.That(poolProperty?.SetMethod).IsNull();
    }

    [Test]
    public async Task NullableTextProperties_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.Subtitle).IsNull();
        await Assert.That(entity.Description).IsNull();
        await Assert.That(entity.Slug).IsNull();
        await Assert.That(entity.Timezone).IsNull();
        await Assert.That(entity.BackgroundColor).IsNull();
        await Assert.That(entity.BackgroundImageId).IsNull();
        await Assert.That(entity.BackgroundEffect).IsNull();
    }

    [Test]
    public async Task NullableRelationshipProperties_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.EventTypeId).IsNull();
        await Assert.That(entity.EventType).IsNull();
        await Assert.That(entity.AudienceGenderId).IsNull();
        await Assert.That(entity.AudienceGender).IsNull();
        await Assert.That(entity.AudienceAgeId).IsNull();
        await Assert.That(entity.AudienceAge).IsNull();
        await Assert.That(entity.FeaturedImageId).IsNull();
        await Assert.That(entity.FeaturedImage).IsNull();
        await Assert.That(entity.MadhabId).IsNull();
        await Assert.That(entity.Madhab).IsNull();
        await Assert.That(entity.AtprotoRecordId).IsNull();
        await Assert.That(entity.AtprotoRecord).IsNull();
    }

    [Test]
    public async Task IslamicAspect_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.IslamicAspect).IsNull();
    }

    [Test]
    public async Task TechAspect_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.TechAspect).IsNull();
    }

    [Test]
    public async Task ParticipationConfiguration_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.ParticipationConfiguration).IsNull();
    }

    [Test]
    public async Task LegacyParticipationProperties_AreAbsent()
    {
        var eventType = typeof(global::Explore.Domain.Event);

        await Assert.That(eventType.GetProperty("IsRegistrationRequired")).IsNull();
        await Assert.That(eventType.GetProperty("ExternalRegistrationUrl")).IsNull();
    }

    [Test]
    public async Task LegacyEventAndSessionPriceAndCurrencyCodeProperties_ShouldBeAbsent()
    {
        var eventType = typeof(global::Explore.Domain.Event);
        var sessionType = typeof(global::Explore.Domain.EventSession);

        await Assert.That(eventType.GetProperty("Price")).IsNull();
        await Assert.That(eventType.GetProperty("CurrencyCode")).IsNull();
        await Assert.That(sessionType.GetProperty("Price")).IsNull();
        await Assert.That(sessionType.GetProperty("CurrencyCode")).IsNull();
    }

    [Test]
    public async Task SessionCount_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.SessionCount).IsNull();
    }

    [Test]
    public async Task FirstSessionDate_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.FirstSessionDate).IsNull();
    }

    [Test]
    public async Task LastSessionDate_DefaultValue_IsExpected()
    {
        var entity = CreateEvent();

        await Assert.That(entity.LastSessionDate).IsNull();
    }

    [Test]
    public async Task ForeignKeyIds_WhenCreated_HaveExpectedValues()
    {
        var entity = CreateEvent();

        await Assert.That(entity.ActorId).IsEqualTo(Guid.Empty);
        await Assert.That(entity.TenantId).IsEqualTo(Guid.Empty);
        await Assert.That(entity.VisibilityTypeId).IsEqualTo(0);
        await Assert.That(entity.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(entity.EventFormatId).IsEqualTo(0);
    }

    [Test]
    public async Task Event_DoesNotExposeWorkspaceOrOrganizerScopeOwnership_ExpectedBehavior()
    {
        var eventType = typeof(global::Explore.Domain.Event);

        await Assert.That(eventType.GetProperty("WorkspaceId")).IsNull();
        await Assert.That(eventType.GetProperty("OrganizerScopeId")).IsNull();
        await Assert.That(eventType.GetProperty("OrganizationScopeId")).IsNull();
        await Assert.That(eventType.GetProperty("SubTenantId")).IsNull();
    }

    [Test]
    public async Task Event_WhenCreated_HasRequiredComposedObjects()
    {
        var entity = CreateEvent();

        await Assert.That(entity.Actor).IsNotNull();
        await Assert.That(entity.Tenant).IsNotNull();
        await Assert.That(entity.VisibilityType).IsNotNull();
        await Assert.That(entity.EventStatus).IsNotNull();
        await Assert.That(entity.EventFormat).IsNotNull();
    }

    private static bool IsRequiredProperty<T>(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        return property is not null && property.GetCustomAttributes(inherit: false).Any(a => a.GetType().Name == "RequiredMemberAttribute");
    }

    private static global::Explore.Domain.Event CreateEvent()
    {
        return new global::Explore.Domain.Event
        {
            Title = "Test Event",
            Actor = CreateActor(),
            Tenant = CreateTenant(),
            VisibilityType = CreateVisibilityType(),
            EventStatus = CreateEventStatus(),
            EventFormat = CreateEventFormat()
        };
    }

    private static Actor CreateActor()
    {
        return new Actor
        {
            Pii = new ActorPii { DisplayName = "Actor" },
            ActorType = CreateActorType()
        };
    }

    private static ActorType CreateActorType()
    {
        return new ActorType
        {
            FullName = "User",
            MasterCode = "USER"
        };
    }

    private static Tenant CreateTenant()
    {
        return new Tenant
        {
            FullName = "Tenant",
            Slug = "tenant",
            TenantStatusId = 2,
            TenantStatus = new TenantStatus { Id = 2, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true }
        };
    }

    private static VisibilityType CreateVisibilityType()
    {
        return new VisibilityType
        {
            MasterCode = "PUBLIC",
            FullName = "Public"
        };
    }

    private static EventStatus CreateEventStatus()
    {
        return new EventStatus
        {
            MasterCode = "DRAFT",
            FullName = "Draft"
        };
    }

    private static EventFormat CreateEventFormat()
    {
        return new EventFormat
        {
            MasterCode = "ONLINE",
            FullName = "Online"
        };
    }
}
