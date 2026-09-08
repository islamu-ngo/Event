using Bogus;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Common;

/// <summary>
/// Provides Bogus-based fake data generators for all domain entities.
/// Used for unit testing to generate realistic test data.
/// </summary>
public static class DataBuilder
{
    #region Core Entities

    public static Faker<Explore.Domain.Event> Event => new Faker<Explore.Domain.Event>()
        .CustomInstantiator(f => CreateEvent((EventStatusEnum)f.Random.Int(1, 4)))
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.Title, f => f.Lorem.Sentence())
        .RuleFor(e => e.Description, f => f.Lorem.Sentence(8))
        .RuleFor(e => e.Content, f => f.Lorem.Paragraph())
        .RuleFor(e => e.FirstSessionDate, f => f.Date.FutureDateOnly())
        .RuleFor(e => e.LastSessionDate, (f, e) => e.FirstSessionDate?.AddDays(1))
        .RuleFor(e => e.TotalViews, f => f.Random.Int(0, 10000))
        .RuleFor(e => e.EventTypeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.AudienceGenderId, f => f.Random.Int(1, 4))
        .RuleFor(e => e.AudienceAgeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.VisibilityTypeId, f => f.Random.Int(1, 3))
        .RuleFor(e => e.EventFormatId, f => f.Random.Int(1, 3))
        .RuleFor(e => e.Slug, f => f.Lorem.Slug());

    public static Faker<Explore.Domain.Event> EventWithStatus(EventStatusEnum status) =>
        Event.CustomInstantiator(_ => CreateEvent(status));

    public static Faker<EventSession> EventSession => new Faker<EventSession>()
        .CustomInstantiator(_ => new EventSession(EventSessionStatusEnum.Draft)
        {
            Event = null!,
            Tenant = null!
        })
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.Title, f => f.Lorem.Sentence())
        .RuleFor(e => e.Description, f => f.Lorem.Paragraph())
        .RuleFor(e => e.StartTime, f => f.Date.FutureOffset())
        .RuleFor(e => e.EndTime, (f, e) => e.StartTime!.Value.AddHours(2))
        .RuleFor(e => e.MaxAudienceAttendees, f => f.Random.Int(10, 500))
        .RuleFor(e => e.CurrentAudienceAttendees, f => 0)
        .RuleFor(e => e.Slug, f => f.Lorem.Slug());

    private static Explore.Domain.Event CreateEvent(EventStatusEnum status) => new(status)
    {
        Title = null!,
        Actor = null!,
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

    public static Faker<EventSessionAgendaItem> EventSessionAgendaItem => new Faker<EventSessionAgendaItem>()
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.Title, f => f.Lorem.Sentence())
        .RuleFor(e => e.Description, f => f.Lorem.Paragraph())
        .RuleFor(e => e.StartTime, f => f.Date.FutureOffset())
        .RuleFor(e => e.EndTime, (f, e) => e.StartTime.AddMinutes(30));

    public static Faker<EventRegistration> EventRegistration => new Faker<EventRegistration>()
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.ApprovalStatusId, f => f.Random.Int(1, 4));

    #endregion

    #region User & Actor Entities

    public static Faker<User> User => new Faker<User>()
        .RuleFor(u => u.Pii, f => new UserPii { Email = "", FirstName = "", LastName = "" })
        .RuleFor(u => u.Id, f => Guid.NewGuid())
        .RuleFor(u => u.Email, f => f.Internet.Email())
        .RuleFor(u => u.FirstName, f => f.Name.FirstName())
        .RuleFor(u => u.LastName, f => f.Name.LastName())
        .RuleFor(u => u.EmailVerified, f => f.Random.Bool());

    public static Faker<Actor> Actor => new Faker<Actor>()
        .RuleFor(a => a.Pii, f => new ActorPii { DisplayName = "" })
        .RuleFor(a => a.Id, f => Guid.NewGuid())
        .RuleFor(a => a.DisplayName, f => f.Name.FullName())
        .RuleFor(a => a.ActorTypeId, f => f.Random.Int(1, 4))
        .RuleFor(a => a.Description, f => f.Lorem.Sentence());

    public static Faker<ActorKeyStore> ActorKeyStore => new Faker<ActorKeyStore>()
        .RuleFor(a => a.Id, f => Guid.NewGuid())
        .RuleFor(a => a.KeyPurpose, f => f.PickRandom("signing", "rotation", "encryption"))
        .RuleFor(a => a.PublicKey, f => f.Random.Hash(64))
        .RuleFor(a => a.PrivateKeyEncrypted, f => f.Random.Hash(128))
        .RuleFor(a => a.IsActive, f => f.Random.Bool());

    #endregion

    #region Organization Entities

    public static Faker<Organization> Organization => new Faker<Organization>()
        .RuleFor(o => o.Pii, f => new OrganizationPii { FullName = "" })
        .RuleFor(o => o.Id, f => Guid.NewGuid())
        .RuleFor(o => o.FullName, f => f.Company.CompanyName())
        .RuleFor(o => o.Email, f => f.Internet.Email())
        .RuleFor(o => o.Country, f => f.Address.Country())
        .RuleFor(o => o.City, f => f.Address.City())
        .RuleFor(o => o.Address, f => f.Address.StreetAddress())
        .RuleFor(o => o.Postcode, f => f.Address.ZipCode())
        .RuleFor(o => o.WebsiteUrl, f => f.Internet.Url());

    public static Faker<OrganizationMember> OrganizationMember => new Faker<OrganizationMember>()
        .RuleFor(o => o.Id, f => Guid.NewGuid())
        .RuleFor(o => o.RoleId, f => f.Random.Int(1, 3))
        .RuleFor(o => o.OrganizationPositionId, f => f.Random.Int(1, 5));

    public static Faker<OrganizationReview> OrganizationReview => new Faker<OrganizationReview>()
        .RuleFor(o => o.Id, f => Guid.NewGuid())
        .RuleFor(o => o.Rating, f => f.Random.Int(1, 5))
        .RuleFor(o => o.Comment, f => f.Lorem.Paragraph());

    #endregion

    #region Location & Storage Entities

    public static Faker<Location> Location => new Faker<Location>()
        .CustomInstantiator(faker =>
        {
            var location = new Location
            {
                Id = Guid.CreateVersion7(),
                FullName = faker.Company.CompanyName(),
                Country = faker.Address.Country(),
                City = faker.Address.City(),
                Timezone = faker.Date.TimeZoneString()
            };
            location.SetProviderAddress(
                faker.Address.StreetAddress(),
                faker.Address.ZipCode(),
                Explore.Domain.ValueObjects.GeoCoordinate.Create(
                    faker.Address.Latitude(),
                    faker.Address.Longitude()));
            return location;
        });

    public static Faker<StorageObject> StorageObject => new Faker<StorageObject>()
        .RuleFor(s => s.Id, f => Guid.NewGuid())
        .RuleFor(s => s.FullName, f => f.System.FileName())
        .RuleFor(s => s.Extension, f => f.System.FileExt())
        .RuleFor(s => s.Uri, f => f.Internet.Url())
        .RuleFor(s => s.Size, f => f.Random.Long(1000, 10000000))
        .RuleFor(s => s.FileTypeId, f => f.Random.Int(1, 4));

    #endregion

    #region Category & Tag Entities

    public static Faker<Category> Category => new Faker<Category>()
        .RuleFor(c => c.Id, f => Guid.NewGuid())
        .RuleFor(c => c.MasterCode, f => f.Random.AlphaNumeric(10).ToUpper())
        .RuleFor(c => c.FullName, f => f.Commerce.Categories(1)[0]);

    public static Faker<Tag> Tag => new Faker<Tag>()
        .RuleFor(t => t.Id, f => Guid.NewGuid())
        .RuleFor(t => t.MasterCode, f => f.Random.AlphaNumeric(10).ToUpper())
        .RuleFor(t => t.FullName, f => f.Lorem.Word());

    #endregion

    #region Scheduling & Registration Entities

    public static Faker<EventDay> EventDay => new Faker<EventDay>()
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.EventId, f => Guid.NewGuid())
        .RuleFor(e => e.LocalDate, f => f.Date.FutureDateOnly())
        .RuleFor(e => e.Label, f => f.Lorem.Sentence(3))
        .RuleFor(e => e.Description, f => f.Lorem.Paragraph())
        .RuleFor(e => e.BannerText, f => f.Lorem.Sentence())
        .RuleFor(e => e.IsPublished, f => true)
        .RuleFor(e => e.SortOrder, f => f.Random.Int(0, 10))
        .RuleFor(e => e.AllowsDayScopeRegistration, f => f.Random.Bool());

    public static Faker<EventAgendaItem> EventAgendaItem => new Faker<EventAgendaItem>()
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.EventId, f => Guid.NewGuid())
        .RuleFor(e => e.Title, f => f.Lorem.Sentence())
        .RuleFor(e => e.Description, f => f.Lorem.Paragraph())
        .RuleFor(e => e.StartTime, f => f.Date.FutureOffset())
        .RuleFor(e => e.EndTime, (f, e) => e.StartTime.AddMinutes(45))
        .RuleFor(e => e.SortOrder, f => f.Random.Int(0, 10));

    public static Faker<LocationRoom> LocationRoom => new Faker<LocationRoom>()
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.LocationId, f => Guid.NewGuid())
        .RuleFor(e => e.Name, f => f.Commerce.ProductName())
        .RuleFor(e => e.Slug, f => f.Lorem.Slug())
        .RuleFor(e => e.Description, f => f.Lorem.Paragraph())
        .RuleFor(e => e.Capacity, f => f.Random.Int(10, 500))
        .RuleFor(e => e.SortOrder, f => f.Random.Int(0, 10));

    public static Faker<RegistrationScope> RegistrationScope => new Faker<RegistrationScope>()
        .RuleFor(e => e.Id, f => f.Random.Int(1, 3))
        .RuleFor(e => e.MasterCode, f => f.PickRandom("EVENT", "DAY", "SESSION_SELECTION"))
        .RuleFor(e => e.FullName, f => f.PickRandom("Event", "Day", "Session Selection"));

    public static Faker<ScheduleItemKind> ScheduleItemKind => new Faker<ScheduleItemKind>()
        .RuleFor(e => e.Id, f => f.Random.Int(1, 5))
        .RuleFor(e => e.MasterCode, f => f.Random.AlphaNumeric(10).ToUpper())
        .RuleFor(e => e.FullName, f => f.Lorem.Word());

    #endregion

    #region Tenant Entities

    public static Faker<Tenant> Tenant => new Faker<Tenant>()
        .RuleFor(t => t.Id, f => Guid.NewGuid())
        .RuleFor(t => t.FullName, f => f.Company.CompanyName())
        .RuleFor(t => t.Slug, f => f.Lorem.Slug())
        .RuleFor(t => t.TenantStatusId, f => (int)Explore.Domain.Enums.TenantStatusEnum.Active)
        .RuleFor(t => t.TenantStatus, f => new TenantStatus
        {
            Id = (int)Explore.Domain.Enums.TenantStatusEnum.Active,
            MasterCode = "ACTIVE",
            FullName = "Active",
            IsActiveState = true
        });

    public static Faker<TenantUserRoleGrant> TenantUserRoleGrant => new Faker<TenantUserRoleGrant>()
        .RuleFor(t => t.Id, f => Guid.NewGuid())
        .RuleFor(t => t.RoleId, f => f.Random.Int(1, 3))
        .RuleFor(t => t.RoleScopeId, _ => (int)Explore.Domain.Enums.RoleScopeEnum.Tenant)
        .RuleFor(t => t.GrantedAt, f => f.Date.Past())
        .RuleFor(t => t.CreatedAt, f => f.Date.Past());

    #endregion

    #region Federation Entities

    public static Faker<AtprotoRecord> AtprotoRecord => new Faker<AtprotoRecord>()
        .RuleFor(a => a.Id, f => Guid.NewGuid())
        .RuleFor(a => a.Did, f => $"did:plc:{f.Random.AlphaNumeric(24)}")
        .RuleFor(a => a.Collection, f => f.PickRandom("app.bsky.feed.post", "ngo.islamu.event.event"))
        .RuleFor(a => a.RecordKey, f => f.Random.AlphaNumeric(13))
        .RuleFor(a => a.Cid, f => $"bafyrei{f.Random.AlphaNumeric(50)}");

    public static Faker<SyncState> SyncState => new Faker<SyncState>()
        .RuleFor(s => s.Id, f => f.Random.Int(1, 10000))
        .RuleFor(s => s.Service, f => f.Internet.Url())
        .RuleFor(s => s.Cursor, f => f.Random.Long(1, 1000000000))
        .RuleFor(s => s.UpdatedAt, f => f.Date.Recent());

    #endregion
}
