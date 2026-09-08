using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;

namespace Explore.Persistence.Seed;

/// <summary>
/// Centralized seed data objects for Development environment.
/// All seed data references SeedIds for consistent, deterministic IDs.
///
/// NOTE: Lookup/enum tables are seeded via LookupTableSeeder at runtime in ALL environments.
/// This class contains business entity seed data applied only in Development.
///
/// Seeding order matters due to FK constraints and circular dependencies (User/Org ↔ Actor):
/// 1. Tenant → 2. Users (no ActorId) → 3. Organizations (no ActorId) → 4. Actors →
/// 5. Update Users/Orgs with ActorId → 6. TenantUsers, role grants, OrgMembers, Storage →
/// 7. Settings, Capabilities → 8. Categories, Tags, Location → 9. Islamic event catalog
/// </summary>
public static class SeedData
{
    private static readonly DateTime SeedTimestamp = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ===== Tenant =====
    public static Tenant DefaultTenant => new()
    {
        Id = SeedIds.DefaultTenantId,
        FullName = "ISLAMU Default Tenant",
        Slug = "default",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    // ===== Users =====
    public static User AdminUser => new()
    {
        Id = SeedIds.AdminUserId,
        Pii = new UserPii
        {
            Email = "admin@islamu.dev",
            FirstName = "Admin",
            LastName = "User"
        },
        EmailVerified = true,
        CreatedAt = SeedTimestamp
    };

    public static User RegularUser => new()
    {
        Id = SeedIds.RegularUserId,
        Pii = new UserPii
        {
            Email = "user@islamu.dev",
            FirstName = "Regular",
            LastName = "User"
        },
        EmailVerified = true,
        CreatedAt = SeedTimestamp
    };

    public static User ModeratorUser => new()
    {
        Id = SeedIds.ModeratorUserId,
        Pii = new UserPii
        {
            Email = "moderator@islamu.dev",
            FirstName = "Moderator",
            LastName = "User"
        },
        EmailVerified = true,
        CreatedAt = SeedTimestamp
    };

    public static UserExternalLogin AdminDevelopmentLogin => new()
    {
        Id = SeedIds.AdminDevelopmentLoginId,
        UserId = SeedIds.AdminUserId,
        User = null!,
        AuthenticationProviderId = (int)AuthenticationProviderKind.Development,
        AuthenticationProvider = null!,
        ProviderKey = "admin-001",
        ProviderDisplayName = "Development",
        CreatedAt = SeedTimestamp,
    };

    public static UserExternalLogin RegularDevelopmentLogin => new()
    {
        Id = SeedIds.RegularDevelopmentLoginId,
        UserId = SeedIds.RegularUserId,
        User = null!,
        AuthenticationProviderId = (int)AuthenticationProviderKind.Development,
        AuthenticationProvider = null!,
        ProviderKey = "user-001",
        ProviderDisplayName = "Development",
        CreatedAt = SeedTimestamp,
    };

    public static UserExternalLogin ModeratorDevelopmentLogin => new()
    {
        Id = SeedIds.ModeratorDevelopmentLoginId,
        UserId = SeedIds.ModeratorUserId,
        User = null!,
        AuthenticationProviderId = (int)AuthenticationProviderKind.Development,
        AuthenticationProvider = null!,
        ProviderKey = "moderator-001",
        ProviderDisplayName = "Development",
        CreatedAt = SeedTimestamp,
    };

    // ===== Organizations =====
    public static Organization IslamuOrg => new()
    {
        Id = SeedIds.IslamuOrgId,
        Pii = new OrganizationPii
        {
            FullName = "ISLAMU",
            Email = "contact@openislamu.org",
            Country = "Belgium",
            City = "Brussels",
            Postcode = "1070",
            Address = "Parc Du Peterbos"
        },
        WebsiteUrl = "https://islamu.ngo",
        CreatedAt = SeedTimestamp
    };

    public static Organization TechOrg => new()
    {
        Id = SeedIds.TechOrgId,
        Pii = new OrganizationPii
        {
            FullName = "Tech Community",
            Email = "hello@techcommunity.dev",
            Country = "Belgium",
            City = "Antwerp",
            Postcode = "2000",
            Address = "Tech Hub 1"
        },
        WebsiteUrl = "https://techcommunity.dev",
        CreatedAt = SeedTimestamp
    };

    public static OrganizationTenant IslamuOrgTenant => new()
    {
        Id = SeedIds.IslamuOrgTenantId,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        OrganizationId = SeedIds.IslamuOrgId,
        Organization = null!,
        ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
        ApprovalStatus = null!,
        IsVisible = true,
        IsOrganizerEligible = true,
        ApprovedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    public static OrganizationTenant TechOrgTenant => new()
    {
        Id = SeedIds.TechOrgTenantId,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        OrganizationId = SeedIds.TechOrgId,
        Organization = null!,
        ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
        ApprovalStatus = null!,
        IsVisible = true,
        IsOrganizerEligible = true,
        ApprovedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    // ===== User Actors =====
    public static Actor AdminUserActor => new()
    {
        Id = SeedIds.AdminUserActorId,
        ActorTypeId = (int)ActorTypeEnum.User,
        ActorType = null!,
        Pii = new ActorPii
        {
            DisplayName = "Admin User"
        },
        Description = "Platform administrator",
        UserId = SeedIds.AdminUserId,
        OrganizationId = null
    };

    public static Actor RegularUserActor => new()
    {
        Id = SeedIds.RegularUserActorId,
        ActorTypeId = (int)ActorTypeEnum.User,
        ActorType = null!,
        Pii = new ActorPii
        {
            DisplayName = "Regular User"
        },
        Description = "Regular platform user",
        UserId = SeedIds.RegularUserId,
        OrganizationId = null
    };

    public static Actor ModeratorUserActor => new()
    {
        Id = SeedIds.ModeratorUserActorId,
        ActorTypeId = (int)ActorTypeEnum.User,
        ActorType = null!,
        Pii = new ActorPii
        {
            DisplayName = "Moderator User"
        },
        Description = "Platform moderator",
        UserId = SeedIds.ModeratorUserId,
        OrganizationId = null
    };

    // ===== Organization Actors =====
    public static Actor IslamuOrgActor => new()
    {
        Id = SeedIds.IslamuOrgActorId,
        ActorTypeId = (int)ActorTypeEnum.Organization,
        ActorType = null!,
        Pii = new ActorPii
        {
            DisplayName = "ISLAMU"
        },
        Description = "ISLAMU NGO - Islamic Learning and Media Union",
        UserId = null,
        OrganizationId = SeedIds.IslamuOrgId
    };

    public static Actor TechOrgActor => new()
    {
        Id = SeedIds.TechOrgActorId,
        ActorTypeId = (int)ActorTypeEnum.Organization,
        ActorType = null!,
        Pii = new ActorPii
        {
            DisplayName = "Tech Community"
        },
        Description = "Tech Community Belgium",
        UserId = null,
        OrganizationId = SeedIds.TechOrgId
    };

    // ===== Tenant Users And Role Grants =====
    public static TenantUser AdminTenantUser => new()
    {
        Id = SeedIds.AdminTenantUserId,
        UserId = SeedIds.AdminUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ActorId = SeedIds.AdminUserActorId,
        Actor = null,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    public static TenantUser RegularTenantUser => new()
    {
        Id = SeedIds.RegularTenantUserId,
        UserId = SeedIds.RegularUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ActorId = SeedIds.RegularUserActorId,
        Actor = null,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    public static TenantUser ModeratorTenantUser => new()
    {
        Id = SeedIds.ModeratorTenantUserId,
        UserId = SeedIds.ModeratorUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ActorId = SeedIds.ModeratorUserActorId,
        Actor = null,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    public static TenantUserRoleGrant AdminTenantUserRoleGrant => new()
    {
        Id = SeedIds.AdminTenantUserRoleGrantId,
        TenantUserId = SeedIds.AdminTenantUserId,
        TenantUser = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.TenantAdmin,
        Role = null!,
        RoleScopeId = (int)RoleScopeEnum.Tenant,
        GrantedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    public static TenantUserRoleGrant RegularTenantUserRoleGrant => new()
    {
        Id = SeedIds.RegularTenantUserRoleGrantId,
        TenantUserId = SeedIds.RegularTenantUserId,
        TenantUser = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.TenantMember,
        Role = null!,
        RoleScopeId = (int)RoleScopeEnum.Tenant,
        GrantedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    public static TenantUserRoleGrant ModeratorTenantUserRoleGrant => new()
    {
        Id = SeedIds.ModeratorTenantUserRoleGrantId,
        TenantUserId = SeedIds.ModeratorTenantUserId,
        TenantUser = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.TenantModerator,
        Role = null!,
        RoleScopeId = (int)RoleScopeEnum.Tenant,
        GrantedAt = SeedTimestamp,
        CreatedAt = SeedTimestamp
    };

    // ===== Organization Members =====
    // Admin is Creator of ISLAMU (Founder position)
    public static OrganizationMember AdminIslamuCreator => new()
    {
        Id = SeedIds.AdminIslamuCreatorId,
        OrganizationTenantId = SeedIds.IslamuOrgTenantId,
        OrganizationTenant = null!,
        UserId = SeedIds.AdminUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.OrgAdmin,
        Role = null!,
        OrganizationPositionId = (int)OrganizationPositionEnum.Founder,
        CreatedAt = SeedTimestamp
    };

    // Regular user is Member of ISLAMU
    public static OrganizationMember RegularIslamuMember => new()
    {
        Id = SeedIds.RegularIslamuMemberId,
        OrganizationTenantId = SeedIds.IslamuOrgTenantId,
        OrganizationTenant = null!,
        UserId = SeedIds.RegularUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.OrgMember,
        Role = null!,
        OrganizationPositionId = (int)OrganizationPositionEnum.Volunteer,
        CreatedAt = SeedTimestamp
    };

    // Moderator is Moderator of ISLAMU
    public static OrganizationMember ModeratorIslamuMod => new()
    {
        Id = SeedIds.ModeratorIslamuModId,
        OrganizationTenantId = SeedIds.IslamuOrgTenantId,
        OrganizationTenant = null!,
        UserId = SeedIds.ModeratorUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.OrgModerator,
        Role = null!,
        OrganizationPositionId = (int)OrganizationPositionEnum.Coordinator,
        CreatedAt = SeedTimestamp
    };

    // Admin is Admin of Tech org
    public static OrganizationMember AdminTechCoOwner => new()
    {
        Id = SeedIds.AdminTechCoOwnerId,
        OrganizationTenantId = SeedIds.TechOrgTenantId,
        OrganizationTenant = null!,
        UserId = SeedIds.AdminUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.OrgAdmin,
        Role = null!,
        OrganizationPositionId = (int)OrganizationPositionEnum.Director,
        CreatedAt = SeedTimestamp
    };

    // Regular user is Admin of Tech org (Founder position)
    public static OrganizationMember RegularTechCreator => new()
    {
        Id = SeedIds.RegularTechCreatorId,
        OrganizationTenantId = SeedIds.TechOrgTenantId,
        OrganizationTenant = null!,
        UserId = SeedIds.RegularUserId,
        User = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        RoleId = (int)RoleEnum.OrgAdmin,
        Role = null!,
        OrganizationPositionId = (int)OrganizationPositionEnum.Founder,
        CreatedAt = SeedTimestamp
    };

    // ===== Storage Objects =====
    public static StorageObject DefaultEventImage => new()
    {
        Id = SeedIds.DefaultEventImageId,
        Uri = "https://placeholder.islamu.org/event-default.jpg",
        Provider = StorageProviders.LegacyExternal,
        ObjectKey = null,
        FullName = "Default Event Image",
        SafeDisplayName = "Default Event Image",
        Extension = ".jpg",
        ContentType = "image/jpeg",
        Size = 0,
        Visibility = StorageObjectVisibilities.PublicImage,
        Purpose = StorageObjectPurposes.EventImage,
        LifecycleState = StorageObjectLifecycleStates.Active,
        FileTypeId = (int)FileTypeEnum.Image,
        FileType = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ActorId = SeedIds.AdminUserActorId
    };

    public static StorageObject DefaultProfileImage => new()
    {
        Id = SeedIds.DefaultProfileImageId,
        Uri = "https://placeholder.islamu.org/profile-default.jpg",
        Provider = StorageProviders.LegacyExternal,
        ObjectKey = null,
        FullName = "Default Profile Image",
        SafeDisplayName = "Default Profile Image",
        Extension = ".jpg",
        ContentType = "image/jpeg",
        Size = 0,
        Visibility = StorageObjectVisibilities.PublicImage,
        Purpose = StorageObjectPurposes.ProfileImage,
        LifecycleState = StorageObjectLifecycleStates.Active,
        FileTypeId = (int)FileTypeEnum.Image,
        FileType = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ActorId = SeedIds.AdminUserActorId
    };

    public static StorageObject DefaultOrganizationLogo => new()
    {
        Id = SeedIds.DefaultOrganizationLogoId,
        Uri = "https://placeholder.islamu.org/org-default.jpg",
        Provider = StorageProviders.LegacyExternal,
        ObjectKey = null,
        FullName = "Default Organization Logo",
        SafeDisplayName = "Default Organization Logo",
        Extension = ".jpg",
        ContentType = "image/jpeg",
        Size = 0,
        Visibility = StorageObjectVisibilities.PublicImage,
        Purpose = StorageObjectPurposes.ProfileImage,
        LifecycleState = StorageObjectLifecycleStates.Active,
        FileTypeId = (int)FileTypeEnum.Image,
        FileType = null!,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ActorId = SeedIds.AdminUserActorId
    };

    // ===== Tenant Capabilities =====
    public static TenantCapability DefaultTenantCoreCapability => new()
    {
        Id = SeedIds.DefaultTenantCoreCapabilityId,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ModuleId = SeedIds.ModuleCoreId,
        Module = null!,
        IsEnabled = true,
        EnabledAt = SeedTimestamp
    };

    public static TenantCapability DefaultTenantIslamicCapability => new()
    {
        Id = SeedIds.DefaultTenantIslamicCapabilityId,
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ModuleId = SeedIds.ModuleIslamicId,
        Module = null!,
        IsEnabled = true,
        EnabledAt = SeedTimestamp
    };

    // ===== Location =====
    public static Location OnlineLocation => CreateSeedLocation(
        SeedIds.OnlineLocationId,
        "Online / Virtual",
        "Internet",
        "Virtual",
        "Virtual",
        "00000",
        "UTC");

    // ===== Categories =====
    public static Category IslamicStudiesCategory => new()
    {
        Id = SeedIds.IslamicStudiesCategoryId,
        MasterCode = "ISLAMIC_STUDIES",
        FullName = "Islamic Studies",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = null
    };

    public static Category QuranCategory => new()
    {
        Id = SeedIds.QuranCategoryId,
        MasterCode = "QURAN",
        FullName = "Quran & Tafsir",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = SeedIds.IslamicStudiesCategoryId
    };

    public static Category HadithCategory => new()
    {
        Id = SeedIds.HadithCategoryId,
        MasterCode = "HADITH",
        FullName = "Hadith Sciences",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = SeedIds.IslamicStudiesCategoryId
    };

    public static Category FiqhCategory => new()
    {
        Id = SeedIds.FiqhCategoryId,
        MasterCode = "FIQH",
        FullName = "Fiqh (Islamic Jurisprudence)",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = SeedIds.IslamicStudiesCategoryId
    };

    public static Category AqeedahCategory => new()
    {
        Id = SeedIds.AqeedahCategoryId,
        MasterCode = "AQEEDAH",
        FullName = "Aqeedah (Islamic Creed)",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = SeedIds.IslamicStudiesCategoryId
    };

    public static Category SeerahCategory => new()
    {
        Id = SeedIds.SeerahCategoryId,
        MasterCode = "SEERAH",
        FullName = "Seerah (Prophetic Biography)",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = SeedIds.IslamicStudiesCategoryId
    };

    public static Category ArabicLanguageCategory => new()
    {
        Id = SeedIds.ArabicLanguageCategoryId,
        MasterCode = "ARABIC",
        FullName = "Arabic Language",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = null
    };

    public static Category CommunityEventsCategory => new()
    {
        Id = SeedIds.CommunityEventsCategoryId,
        MasterCode = "COMMUNITY",
        FullName = "Community Events",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!,
        ParentId = null
    };

    // ===== Tags =====
    public static Tag BeginnerTag => new()
    {
        Id = SeedIds.BeginnerTagId,
        MasterCode = "BEGINNER",
        FullName = "Beginner",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!
    };

    public static Tag IntermediateTag => new()
    {
        Id = SeedIds.IntermediateTagId,
        MasterCode = "INTERMEDIATE",
        FullName = "Intermediate",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!
    };

    public static Tag AdvancedTag => new()
    {
        Id = SeedIds.AdvancedTagId,
        MasterCode = "ADVANCED",
        FullName = "Advanced",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!
    };

    public static Tag FreeTag => new()
    {
        Id = SeedIds.FreeTagId,
        MasterCode = "FREE",
        FullName = "Free",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!
    };

    public static Tag PaidTag => new()
    {
        Id = SeedIds.PaidTagId,
        MasterCode = "PAID",
        FullName = "Paid",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!
    };

    public static Tag OnlineTag => new()
    {
        Id = SeedIds.OnlineTagId,
        MasterCode = "ONLINE",
        FullName = "Online",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!
    };

    public static Tag InPersonTag => new()
    {
        Id = SeedIds.InPersonTagId,
        MasterCode = "IN_PERSON",
        FullName = "In-Person",
        TenantId = SeedIds.DefaultTenantId,
        Tenant = null!
    };

    // ===== Additional development locations and rooms =====
    public static IReadOnlyList<Location> IslamicEventLocations =>
    [
        CreateSeedLocation(
            SeedIds.BrusselsIslamicCenterLocationId,
            "Brussels Islamic Learning Center",
            "Belgium",
            "Brussels",
            "Rue de l'Instruction 12",
            "1070",
            BrusselsTimezone,
            50.8369,
            4.3264),
        CreateSeedLocation(
            SeedIds.AntwerpMasjidLocationId,
            "Antwerp Masjid Community Hall",
            "Belgium",
            "Antwerp",
            "Gemeentestraat 44",
            "2060",
            BrusselsTimezone,
            51.2213,
            4.4210)
    ];

    private static Location CreateSeedLocation(
        Guid id,
        string fullName,
        string country,
        string city,
        string address,
        string postcode,
        string timezone,
        double? latitude = null,
        double? longitude = null)
    {
        var location = new Location
        {
            Id = id,
            FullName = fullName,
            Country = country,
            City = city,
            Timezone = timezone,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!
        };
        if (latitude is { } exactLatitude && longitude is { } exactLongitude)
        {
            location.SetProviderAddress(
                address,
                postcode,
                Explore.Domain.ValueObjects.GeoCoordinate.Create(exactLatitude, exactLongitude));
        }
        else
        {
            location.SetManualAddress(address, postcode);
        }
        return location;
    }

    public static IReadOnlyList<LocationRoom> IslamicEventRooms =>
    [
        CreateRoom(10, SeedIds.BrusselsIslamicCenterLocationId, "Main Prayer Hall", "main-prayer-hall", "Large hall with configurable family and segregated seating.", 220, 1),
        CreateRoom(11, SeedIds.BrusselsIslamicCenterLocationId, "Sisters Classroom", "sisters-classroom", "Women-only teaching room with privacy screens and audio relay.", 80, 2),
        CreateRoom(12, SeedIds.BrusselsIslamicCenterLocationId, "Brothers Study Room", "brothers-study-room", "Men-only study room for fiqh and hadith circles.", 90, 3),
        CreateRoom(20, SeedIds.AntwerpMasjidLocationId, "Antwerp Prayer Hall", "antwerp-prayer-hall", "Community masjid hall for lectures, youth circles, and iftar.", 180, 1),
        CreateRoom(21, SeedIds.AntwerpMasjidLocationId, "Youth Activity Room", "youth-activity-room", "Smaller room for supervised youth workshops.", 45, 2)
    ];

    // ===== Islamic event catalog =====
    public static IReadOnlyList<Event> IslamicEvents => IslamicEventSpecs
        .Select(CreateEvent)
        .ToList();

    public static IReadOnlyList<EventParticipationConfiguration> IslamicEventParticipationConfigurations => IslamicEventSpecs
        .Select(CreateParticipationConfiguration)
        .ToList();

    public static IReadOnlyList<EventIslamicAspect> IslamicEventAspects => IslamicEventSpecs
        .Select(spec => new EventIslamicAspect
        {
            Id = spec.Id,
            MadhabId = (int?)spec.Madhab,
            ReferencePrayer = spec.ReferencePrayer,
            PrayerTimeOffset = spec.PrayerOffsetMinutes,
            GenderMode = spec.GenderMode,
            IncludesQuranRecitation = spec.IncludesQuranRecitation,
            PrimaryLanguageId = spec.PrimaryLanguageId
        })
        .ToList();

    public static IReadOnlyList<EventDay> IslamicEventDays => IslamicEventSpecs
        .SelectMany(spec => spec.Days.Select(day => new EventDay
        {
            Id = SeedIds.EventDayId(spec.Number, day.Number),
            EventId = spec.Id,
            Event = null!,
            LocalDate = day.LocalDate,
            Label = day.Label,
            Description = day.Description,
            BannerText = day.BannerText,
            BannerImageId = SeedIds.DefaultEventImageId,
            IsPublished = true,
            SortOrder = day.Number,
            AllowsDayScopeRegistration = spec.Participation.AdvanceRegistrationObligation
                != AdvanceRegistrationObligationEnum.NotApplicable,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp,
            ConcurrencyStamp = SeedIds.EventDayId(spec.Number, day.Number)
        }))
        .ToList();

    public static IReadOnlyList<EventSession> IslamicEventSessions
    {
        get
        {
            var calculator = new EventScheduleProjectionCalculator();
            return IslamicEventSpecs
                .SelectMany(spec => spec.Sessions.Select(session => CreateSession(spec, session, calculator)))
                .ToList();
        }
    }

    public static IReadOnlyList<EventSessionIslamicAspect> IslamicSessionAspects => IslamicEventSpecs
        .SelectMany(spec => spec.Sessions.Select(session => CreateSessionIslamicAspect(spec, session)))
        .ToList();

    public static IReadOnlyList<EventSessionGroup> IslamicSessionGroups => IslamicEventSpecs
        .SelectMany(spec => spec.Groups.Select(group => new EventSessionGroup
        {
            Id = SeedIds.EventSessionGroupId(spec.Number, group.Number),
            EventId = spec.Id,
            Event = null!,
            Name = group.Name,
            Slug = group.Slug,
            Description = group.Description,
            LocationId = group.LocationId,
            RoomId = group.RoomId,
            Color = group.Color,
            SortOrder = group.Number,
            IsPublished = true,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp,
            ConcurrencyStamp = SeedIds.EventSessionGroupId(spec.Number, group.Number)
        }))
        .ToList();

    public static IReadOnlyList<EventSessionGroupSession> IslamicSessionGroupSessions => IslamicEventSpecs
        .SelectMany(spec => spec.Sessions.Select(session => new EventSessionGroupSession
        {
            Id = SeedIds.EventSessionGroupSessionId(spec.Number, session.Number, session.GroupNumber),
            EventSessionGroupId = SeedIds.EventSessionGroupId(spec.Number, session.GroupNumber),
            EventSessionGroup = null!,
            EventSessionId = SeedIds.EventSessionId(spec.Number, session.Number),
            EventSession = null!,
            EventId = spec.Id,
            Event = null!,
            IsPrimary = true,
            SortOrder = session.Number,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp
        }))
        .ToList();

    public static IReadOnlyList<EventAgendaItem> IslamicEventAgendaItems
    {
        get
        {
            var calculator = new EventScheduleProjectionCalculator();
            return IslamicEventSpecs
                .SelectMany(spec => spec.AgendaItems.Select(item => CreateEventAgendaItem(spec, item, calculator)))
                .ToList();
        }
    }

    public static IReadOnlyList<EventSessionAgendaItem> IslamicSessionAgendaItems => IslamicEventSpecs
        .SelectMany(spec => spec.Sessions.SelectMany(session => session.AgendaItems.Select(item => new EventSessionAgendaItem
        {
            Id = SeedIds.EventSessionAgendaItemId(spec.Number, session.Number, item.Number),
            EventSessionId = SeedIds.EventSessionId(spec.Number, session.Number),
            EventSession = null!,
            StartTime = item.StartUtc,
            EndTime = item.EndUtc,
            Title = item.Title,
            Description = item.Description,
            LocationId = session.LocationId,
            Location = null,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!
        })))
        .ToList();

    public static IReadOnlyList<EventCategories> IslamicEventCategories => IslamicEventSpecs
        .SelectMany(spec => spec.CategoryIds.Select((categoryId, index) => new EventCategories
        {
            Id = SeedIds.EventCategoryId(spec.Number, index + 1),
            EventId = spec.Id,
            Event = null!,
            CategoryId = categoryId,
            Category = null!,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp
        }))
        .ToList();

    public static IReadOnlyList<EventTags> IslamicEventTags => IslamicEventSpecs
        .SelectMany(spec => spec.TagIds.Select((tagId, index) => new EventTags
        {
            Id = SeedIds.EventTagId(spec.Number, index + 1),
            EventId = spec.Id,
            Event = null!,
            TagId = tagId,
            Tag = null!,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp
        }))
        .ToList();

    public static IReadOnlyList<EventSessionCategory> IslamicSessionCategories => IslamicEventSpecs
        .SelectMany(spec => spec.Sessions.SelectMany(session => session.CategoryIds.Select((categoryId, index) => new EventSessionCategory
        {
            Id = SeedIds.EventSessionCategoryId(spec.Number, session.Number, index + 1),
            EventSessionId = SeedIds.EventSessionId(spec.Number, session.Number),
            EventSession = null!,
            CategoryId = categoryId,
            Category = null!,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp
        })))
        .ToList();

    public static IReadOnlyList<EventSessionTag> IslamicSessionTags => IslamicEventSpecs
        .SelectMany(spec => spec.Sessions.SelectMany(session => session.TagIds.Select((tagId, index) => new EventSessionTag
        {
            Id = SeedIds.EventSessionTagId(spec.Number, session.Number, index + 1),
            EventSessionId = SeedIds.EventSessionId(spec.Number, session.Number),
            EventSession = null!,
            TagId = tagId,
            Tag = null!,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp
        })))
        .ToList();

    public static IReadOnlyList<EventSessionLanguage> IslamicSessionLanguages => IslamicEventSpecs
        .SelectMany(spec => spec.Sessions.SelectMany(session => session.LanguageIds.Select(languageId => new EventSessionLanguage
        {
            EventSessionId = SeedIds.EventSessionId(spec.Number, session.Number),
            EventSession = null!,
            LanguageId = languageId,
            Language = null!,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!
        })))
        .ToList();

    public static IReadOnlyList<EventSessionSpeaker> IslamicSessionSpeakers => IslamicEventSpecs
        .SelectMany(spec => spec.Sessions.SelectMany(session => session.SpeakerActorIds.Select((actorId, index) => new EventSessionSpeaker
        {
            Id = SeedIds.EventSessionSpeakerId(spec.Number, session.Number, index + 1),
            ActorId = actorId,
            Actor = null!,
            EventSessionId = SeedIds.EventSessionId(spec.Number, session.Number),
            EventSession = null!,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!
        })))
        .ToList();

    private const string BrusselsTimezone = "Europe/Brussels";

    private static readonly ParticipationConfigurationSpec PlatformManagedRequiredAccountParticipation = new(
        ParticipationHandlingModeEnum.PlatformManaged,
        AdvanceRegistrationObligationEnum.Required,
        IdentityAccessModeEnum.AccountRequired,
        GuestRecoveryPolicy: null);

    private static readonly IReadOnlyList<IslamicEventSpec> IslamicEventSpecs =
    [
        new(
            1,
            SeedIds.QuranTafsirWomenEventId,
            "Sisters Quran & Tafsir Morning",
            "sisters-quran-tafsir-morning",
            "Women-only public halaqa with Quran recitation, tafsir, and practical reflection time.",
            EventTypeEnum.Workshop,
            EventFormatEnum.Local,
            AudienceGenderEnum.Woman,
            AudienceAgeEnum.AdultsOnly18Plus,
            GenderSegregationMode.WomenOnly,
            MadhabEnum.Maliki,
            1,
            PrayerTime.Dhuhr,
            -120,
            IncludesQuranRecitation: true,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.BrusselsIslamicCenterLocationId,
            [SeedIds.QuranCategoryId, SeedIds.IslamicStudiesCategoryId],
            [SeedIds.FreeTagId, SeedIds.InPersonTagId, SeedIds.BeginnerTagId],
            [new(1, new DateOnly(2026, 7, 6), "Sisters tafsir day", "Women-only public Quran study and reflection.", "Women-only public event")],
            [new(1, "Sisters Track", "sisters-track", "Women-only learning room", SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(11), "#D946EF")],
            [new(1, "Surah Al-Hujurat: adab and community", "surah-al-hujurat-adab", "Interactive tafsir session for sisters.", new DateTimeOffset(2026, 7, 6, 8, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 6, 10, 30, 0, TimeSpan.Zero), 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(11), EventSessionKindEnum.Class, RegistrationModeEnum.Open, [1, 2], [SeedIds.ModeratorUserActorId], [SeedIds.QuranCategoryId], [SeedIds.BeginnerTagId], PrayerTime.Dhuhr, -150, true, "{\"notes\":\"Bring a mushaf and notebook.\"}", [new(1, "Opening recitation", "Short recitation circle before tafsir.", new DateTimeOffset(2026, 7, 30, 8, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 29, 8, 45, 0, TimeSpan.Zero))])],
            [new(1, "Dhuhr preparation break", "Time reserved for wudu and prayer preparation.", new DateTimeOffset(2026, 7, 6, 10, 35, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 29, 11, 0, 0, TimeSpan.Zero), ScheduleItemKindEnum.Prayer, 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(11))]),
        new(
            2,
            SeedIds.BrothersFiqhEventId,
            "Brothers Fiqh of Purification Intensive",
            "brothers-fiqh-purification-intensive",
            "Men-only fiqh workshop focused on taharah, wudu, ghusl, and practical Q&A.",
            EventTypeEnum.Workshop,
            EventFormatEnum.Local,
            AudienceGenderEnum.Man,
            AudienceAgeEnum.AdultsOnly18Plus,
            GenderSegregationMode.MenOnly,
            MadhabEnum.Hanafi,
            2,
            PrayerTime.Asr,
            30,
            IncludesQuranRecitation: false,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.BrusselsIslamicCenterLocationId,
            [SeedIds.FiqhCategoryId, SeedIds.IslamicStudiesCategoryId],
            [SeedIds.PaidTagId, SeedIds.InPersonTagId, SeedIds.IntermediateTagId],
            [new(1, new DateOnly(2026, 7, 13), "Brothers fiqh day", "Men-only practical fiqh workshop.", "Men only")],
            [new(1, "Brothers Track", "brothers-track", "Men-only study room", SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(12), "#2563EB")],
            [new(1, "Taharah foundations", "taharah-foundations", "Evidence-based fiqh lesson with practical scenarios.", new DateTimeOffset(2026, 7, 13, 12, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 13, 14, 30, 0, TimeSpan.Zero), 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(12), EventSessionKindEnum.Workshop, RegistrationModeEnum.ApprovalRequired, [2], [SeedIds.AdminUserActorId], [SeedIds.FiqhCategoryId], [SeedIds.IntermediateTagId], PrayerTime.Asr, -30, true, "{\"materials\":\"Printed scenario booklet.\"}", [new(1, "Case study Q&A", "Attendee questions on purification edge cases.", new DateTimeOffset(2026, 7, 13, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 13, 14, 30, 0, TimeSpan.Zero))])],
            [new(1, "Asr prayer", "Congregational Asr after the workshop.", new DateTimeOffset(2026, 7, 13, 14, 35, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 13, 15, 0, 0, TimeSpan.Zero), ScheduleItemKindEnum.Prayer, 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(12))]),
        new(
            3,
            SeedIds.FamilySeerahEventId,
            "Family Seerah Story Night",
            "family-seerah-story-night",
            "Both-gender family event with shared seating, children-friendly storytelling, nasheed, and reminders.",
            EventTypeEnum.Conference,
            EventFormatEnum.Hybrid,
            AudienceGenderEnum.Both,
            AudienceAgeEnum.AllAges,
            GenderSegregationMode.Family,
            MadhabEnum.Maliki,
            2,
            PrayerTime.Maghrib,
            20,
            IncludesQuranRecitation: false,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.AntwerpMasjidLocationId,
            [SeedIds.SeerahCategoryId, SeedIds.CommunityEventsCategoryId],
            [SeedIds.FreeTagId, SeedIds.InPersonTagId, SeedIds.OnlineTagId, SeedIds.BeginnerTagId],
            [new(1, new DateOnly(2026, 7, 20), "Family seerah night", "Family-oriented Prophetic biography gathering.", "Families welcome")],
            [new(1, "Family Hall", "family-hall", "Family seating and livestream track", SeedIds.AntwerpMasjidLocationId, SeedIds.RoomId(20), "#16A34A")],
            [new(1, "Stories from the Hijrah", "stories-from-the-hijrah", "Family seerah lecture with lessons for children and parents.", new DateTimeOffset(2026, 7, 20, 17, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 20, 19, 0, 0, TimeSpan.Zero), 1, SeedIds.AntwerpMasjidLocationId, SeedIds.RoomId(20), EventSessionKindEnum.Lecture, RegistrationModeEnum.Open, [2, 11], [SeedIds.IslamuOrgActorId], [SeedIds.SeerahCategoryId], [SeedIds.FreeTagId], PrayerTime.Maghrib, -60, false, null, [new(1, "Children reflection activity", "Guided reflection cards for families.", new DateTimeOffset(2026, 7, 20, 18, 40, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 20, 19, 0, 0, TimeSpan.Zero))])],
            [new(1, "Maghrib and refreshments", "Prayer followed by tea and snacks.", new DateTimeOffset(2026, 7, 20, 19, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 20, 19, 45, 0, TimeSpan.Zero), ScheduleItemKindEnum.Prayer, 1, SeedIds.AntwerpMasjidLocationId, SeedIds.RoomId(20))]),
        new(
            4,
            SeedIds.SegregatedRamadanEventId,
            "Ramadan Preparation Conference",
            "ramadan-preparation-conference-segregated",
            "Both-gender public conference with strictly segregated brothers and sisters rooms, prayer breaks, and multilingual sessions.",
            EventTypeEnum.Conference,
            EventFormatEnum.Local,
            AudienceGenderEnum.Both,
            AudienceAgeEnum.AllAges,
            GenderSegregationMode.Segregated,
            MadhabEnum.Shafii,
            2,
            PrayerTime.Isha,
            -30,
            IncludesQuranRecitation: true,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.BrusselsIslamicCenterLocationId,
            [SeedIds.FiqhCategoryId, SeedIds.QuranCategoryId, SeedIds.CommunityEventsCategoryId],
            [SeedIds.PaidTagId, SeedIds.InPersonTagId, SeedIds.IntermediateTagId],
            [new(1, new DateOnly(2026, 7, 4), "Ramadan conference", "Segregated preparation conference.", "Separate brothers and sisters areas")],
            [new(1, "Brothers Hall", "brothers-hall", "Brothers seating area", SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(12), "#1D4ED8"), new(2, "Sisters Hall", "sisters-hall", "Sisters seating area", SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(11), "#BE185D")],
            [new(1, "Fiqh of fasting for brothers", "fiqh-fasting-brothers", "Rules, concessions, and Q&A for brothers.", new DateTimeOffset(2026, 7, 4, 8, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero), 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(12), EventSessionKindEnum.Talk, RegistrationModeEnum.Open, [2], [SeedIds.AdminUserActorId], [SeedIds.FiqhCategoryId], [SeedIds.IntermediateTagId], PrayerTime.Dhuhr, -150, false, null, [new(1, "Brothers Q&A", "Focused questions before the break.", new DateTimeOffset(2026, 7, 4, 9, 35, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero))]), new(2, "Quran routines for sisters", "quran-routines-sisters", "Sisters-only planning workshop for Ramadan recitation routines.", new DateTimeOffset(2026, 7, 4, 8, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero), 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(11), EventSessionKindEnum.Workshop, RegistrationModeEnum.Open, [1, 2], [SeedIds.ModeratorUserActorId], [SeedIds.QuranCategoryId], [SeedIds.IntermediateTagId], PrayerTime.Dhuhr, -150, true, "{\"privacy\":\"Women-only room.\"}", [new(1, "Sisters action planning", "Build a realistic Ramadan Quran plan.", new DateTimeOffset(2026, 7, 4, 9, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero))])],
            [new(1, "Opening reminders", "Joint opening with room audio relay.", new DateTimeOffset(2026, 7, 4, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 4, 8, 20, 0, TimeSpan.Zero), ScheduleItemKindEnum.Intro, 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10)), new(2, "Dhuhr prayer break", "Coordinated prayer break with separate spaces.", new DateTimeOffset(2026, 7, 4, 10, 10, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 4, 10, 40, 0, TimeSpan.Zero), ScheduleItemKindEnum.Prayer, 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10))]),
        new(
            5,
            SeedIds.OnlineHadithEventId,
            "Online Hadith Methodology Webinar",
            "online-hadith-methodology-webinar",
            "Both-gender online webinar on hadith terminology, isnad basics, and how to study narrations responsibly.",
            EventTypeEnum.Webinar,
            EventFormatEnum.Digital,
            AudienceGenderEnum.Both,
            AudienceAgeEnum.Teens16Plus,
            GenderSegregationMode.Mixed,
            MadhabEnum.Maliki,
            2,
            null,
            null,
            IncludesQuranRecitation: false,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.OnlineLocationId,
            [SeedIds.HadithCategoryId, SeedIds.IslamicStudiesCategoryId],
            [SeedIds.FreeTagId, SeedIds.OnlineTagId, SeedIds.BeginnerTagId],
            [new(1, new DateOnly(2026, 7, 11), "Online hadith webinar", "Remote hadith methodology session.", "Online")],
            [new(1, "Webinar Room", "webinar-room", "Virtual webinar track", SeedIds.OnlineLocationId, null, "#7C3AED")],
            [new(1, "What makes a hadith authentic?", "what-makes-hadith-authentic", "Introductory methodology webinar with moderated chat Q&A.", new DateTimeOffset(2026, 7, 11, 18, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 11, 19, 30, 0, TimeSpan.Zero), 1, SeedIds.OnlineLocationId, null, EventSessionKindEnum.Lecture, RegistrationModeEnum.Open, [2, 3], [SeedIds.IslamuOrgActorId], [SeedIds.HadithCategoryId], [SeedIds.OnlineTagId], null, null, false, null, [new(1, "Live chat Q&A", "Moderated questions from online attendees.", new DateTimeOffset(2026, 7, 11, 19, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 11, 19, 30, 0, TimeSpan.Zero))])],
            [new(1, "Webinar opening", "Technical checks and adab reminder.", new DateTimeOffset(2026, 7, 11, 17, 50, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 11, 18, 0, 0, TimeSpan.Zero), ScheduleItemKindEnum.Logistics, 1, SeedIds.OnlineLocationId, null)]),
        new(
            6,
            SeedIds.ArabicWorkshopEventId,
            "Arabic for Quran Beginners",
            "arabic-for-quran-beginners",
            "Both-gender mixed beginner workshop for recognizing Quranic vocabulary, roots, and simple grammar patterns.",
            EventTypeEnum.Workshop,
            EventFormatEnum.Hybrid,
            AudienceGenderEnum.Both,
            AudienceAgeEnum.Teens16Plus,
            GenderSegregationMode.Mixed,
            MadhabEnum.Maliki,
            1,
            null,
            null,
            IncludesQuranRecitation: true,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.BrusselsIslamicCenterLocationId,
            [SeedIds.ArabicLanguageCategoryId, SeedIds.QuranCategoryId],
            [SeedIds.PaidTagId, SeedIds.InPersonTagId, SeedIds.OnlineTagId, SeedIds.BeginnerTagId],
            [new(1, new DateOnly(2026, 7, 18), "Arabic workshop", "Beginner Arabic workshop with hybrid participation.", "Hybrid beginner workshop")],
            [new(1, "Arabic Lab", "arabic-lab", "Interactive Arabic learning room", SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10), "#EA580C")],
            [new(1, "Roots and repeated Quran words", "roots-repeated-quran-words", "Hands-on vocabulary workshop with worksheets.", new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 18, 11, 0, 0, TimeSpan.Zero), 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10), EventSessionKindEnum.Workshop, RegistrationModeEnum.Open, [1, 2], [SeedIds.RegularUserActorId], [SeedIds.ArabicLanguageCategoryId], [SeedIds.BeginnerTagId], null, null, false, null, [new(1, "Vocabulary drill", "Small-group practice with common Quran words.", new DateTimeOffset(2026, 7, 18, 10, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 18, 11, 0, 0, TimeSpan.Zero))])],
            [new(1, "Hybrid check-in", "Onsite and online attendance check-in.", new DateTimeOffset(2026, 7, 18, 8, 45, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.Zero), ScheduleItemKindEnum.Logistics, 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10))]),
        new(
            7,
            SeedIds.YouthAqeedahEventId,
            "Youth Aqeedah Circle",
            "youth-aqeedah-circle",
            "Both-gender youth circle with supervised mixed seating and discussion on belief, identity, and worship.",
            EventTypeEnum.Workshop,
            EventFormatEnum.Local,
            AudienceGenderEnum.Both,
            AudienceAgeEnum.YouthUnder18,
            GenderSegregationMode.Mixed,
            MadhabEnum.Maliki,
            2,
            PrayerTime.Asr,
            45,
            IncludesQuranRecitation: false,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.AntwerpMasjidLocationId,
            [SeedIds.AqeedahCategoryId, SeedIds.CommunityEventsCategoryId],
            [SeedIds.FreeTagId, SeedIds.InPersonTagId, SeedIds.BeginnerTagId],
            [new(1, new DateOnly(2026, 7, 25), "Youth circle", "Supervised youth aqeedah session.", "Youth under 18")],
            [new(1, "Youth Room", "youth-room", "Supervised youth room", SeedIds.AntwerpMasjidLocationId, SeedIds.RoomId(21), "#0891B2")],
            [new(1, "Allah's names and daily choices", "allahs-names-daily-choices", "Interactive youth discussion with activities.", new DateTimeOffset(2026, 7, 25, 13, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 25, 15, 0, 0, TimeSpan.Zero), 1, SeedIds.AntwerpMasjidLocationId, SeedIds.RoomId(21), EventSessionKindEnum.Activity, RegistrationModeEnum.ApprovalRequired, [2, 11], [SeedIds.ModeratorUserActorId], [SeedIds.AqeedahCategoryId], [SeedIds.BeginnerTagId], PrayerTime.Asr, -30, false, "{\"guardianConsent\":true}", [new(1, "Reflection worksheet", "Youth complete a short names-of-Allah worksheet.", new DateTimeOffset(2026, 7, 25, 14, 35, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 25, 15, 0, 0, TimeSpan.Zero))])],
            [new(1, "Guardian pickup window", "Post-session pickup and feedback.", new DateTimeOffset(2026, 7, 25, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 25, 15, 20, 0, TimeSpan.Zero), ScheduleItemKindEnum.Logistics, 1, SeedIds.AntwerpMasjidLocationId, SeedIds.RoomId(21))]),
        new(
            8,
            SeedIds.CommunityIftarEventId,
            "Community Iftar & New Muslim Welcome",
            "community-iftar-new-muslim-welcome",
            "Both-gender segregated community iftar with welcome table, Maghrib prayer, meal service, and multilingual support.",
            EventTypeEnum.Conference,
            EventFormatEnum.Local,
            AudienceGenderEnum.Both,
            AudienceAgeEnum.AllAges,
            GenderSegregationMode.Segregated,
            MadhabEnum.Maliki,
            2,
            PrayerTime.Maghrib,
            -20,
            IncludesQuranRecitation: true,
            Participation: PlatformManagedRequiredAccountParticipation,
            SeedIds.BrusselsIslamicCenterLocationId,
            [SeedIds.CommunityEventsCategoryId, SeedIds.IslamicStudiesCategoryId],
            [SeedIds.FreeTagId, SeedIds.InPersonTagId, SeedIds.BeginnerTagId],
            [new(1, new DateOnly(2026, 7, 1), "Community iftar", "Segregated iftar and new Muslim welcome.", "Segregated iftar")],
            [new(1, "Main Hall", "main-hall", "Shared program with segregated seating", SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10), "#CA8A04")],
            [new(1, "Welcome to the masjid", "welcome-to-the-masjid", "Orientation and support session for new Muslims and guests.", new DateTimeOffset(2026, 7, 1, 17, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 1, 18, 15, 0, TimeSpan.Zero), 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10), EventSessionKindEnum.Talk, RegistrationModeEnum.Open, [2, 3, 11], [SeedIds.IslamuOrgActorId, SeedIds.RegularUserActorId], [SeedIds.CommunityEventsCategoryId], [SeedIds.FreeTagId], PrayerTime.Maghrib, -90, false, null, [new(1, "Support table intro", "Introduce mentors and language support tables.", new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 1, 18, 15, 0, TimeSpan.Zero))])],
            [new(1, "Maghrib and iftar", "Dates, water, prayer, and meal service.", new DateTimeOffset(2026, 7, 1, 18, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 1, 19, 30, 0, TimeSpan.Zero), ScheduleItemKindEnum.Prayer, 1, SeedIds.BrusselsIslamicCenterLocationId, SeedIds.RoomId(10))])
    ];

    private static LocationRoom CreateRoom(
        int roomNumber,
        Guid locationId,
        string name,
        string slug,
        string description,
        int capacity,
        int sortOrder) => new()
        {
            Id = SeedIds.RoomId(roomNumber),
            LocationId = locationId,
            Location = null!,
            Name = name,
            Slug = slug,
            Description = description,
            Capacity = capacity,
            SortOrder = sortOrder,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp,
            ConcurrencyStamp = SeedIds.RoomId(roomNumber)
        };

    private static Event CreateEvent(IslamicEventSpec spec)
    {
        var firstSession = spec.Sessions.Min(session => session.StartUtc);
        var lastSession = spec.Sessions.Max(session => session.StartUtc);
        var lastSessionEnd = spec.Sessions.Max(session => session.EndUtc);

        return new Event(EventStatusEnum.Published)
        {
            Id = spec.Id,
            Title = spec.Title,
            Description = BuildCardDescription(spec.Description),
            Content = spec.Description,
            Slug = spec.Slug,
            PublicCode = $"seed{spec.Number:D8}",
            EventTypeId = (int)spec.EventType,
            AudienceGenderId = (int)spec.AudienceGender,
            AudienceAgeId = (int)spec.AudienceAge,
            ActorId = SeedIds.IslamuOrgActorId,
            Actor = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            OrganizerActorId = SeedIds.IslamuOrgActorId,
            FeaturedImageId = SeedIds.DefaultEventImageId,
            TotalViews = spec.Number * 11,
            MadhabId = (int?)spec.Madhab,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)spec.EventFormat,
            EventFormat = null!,
            SessionCount = spec.Sessions.Count,
            FirstSessionDate = spec.Days.Min(day => day.LocalDate),
            LastSessionDate = spec.Days.Max(day => day.LocalDate),
            FirstSessionStartUtc = firstSession,
            LastSessionStartUtc = lastSession,
            LastSessionEndUtc = lastSessionEnd,
            Timezone = BrusselsTimezone,
            EventTimeZoneId = BrusselsTimezone,
            RegistrationPolicyId = (int)EventRegistrationPolicyEnum.SessionSelectionOnly,
            CreatedAt = SeedTimestamp,
            ConcurrencyStamp = spec.Id,
            BackgroundColor = spec.GenderMode switch
            {
                GenderSegregationMode.WomenOnly => "#FDF2F8",
                GenderSegregationMode.MenOnly => "#EFF6FF",
                GenderSegregationMode.Segregated => "#FFFBEB",
                GenderSegregationMode.Family => "#F0FDF4",
                _ => "#F8FAFC"
            },
            BackgroundEffect = "subtle-islamic-pattern"
        };
    }

    private static string BuildCardDescription(string description) =>
        description.Length <= 150 ? description : description[..150];

    private static EventParticipationConfiguration CreateParticipationConfiguration(IslamicEventSpec spec) =>
        EventParticipationConfiguration.Create(
            spec.Id,
            SeedIds.DefaultTenantId,
            (int)spec.Participation.HandlingMode,
            (int)spec.Participation.AdvanceRegistrationObligation,
            spec.Participation.IdentityAccessMode is { } identityAccessMode ? (int)identityAccessMode : null,
            spec.Participation.GuestRecoveryPolicy,
            SeedTimestamp);

    private static EventSession CreateSession(
        IslamicEventSpec spec,
        SessionSpec session,
        IEventScheduleProjectionCalculator calculator)
    {
        var entity = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = SeedIds.EventSessionId(spec.Number, session.Number),
            EventId = spec.Id,
            Event = null!,
            EventDayId = SeedIds.EventDayId(spec.Number, session.DayNumber),
            EventDay = null,
            SortOrder = session.Number,
            LocationId = session.LocationId,
            Location = null,
            RoomId = session.RoomId,
            Room = null,
            Title = session.Title,
            Slug = session.Slug,
            Description = session.Description,
            EventSessionKindId = (int)session.Kind,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            MaxAudienceAttendees = session.RoomId.HasValue ? 80 : 500,
            CurrentAudienceAttendees = session.Number * 7,
            RegistrationModeId = (int)session.RegistrationMode,
            FeaturedImageId = SeedIds.DefaultEventImageId,
            CreatedAt = SeedTimestamp,
            ConcurrencyStamp = SeedIds.EventSessionId(spec.Number, session.Number)
        };

        entity.Reschedule(UtcInstantRange.Create(session.StartUtc, session.EndUtc), BrusselsTimezone, calculator);
        return entity;
    }

    private static EventSessionIslamicAspect CreateSessionIslamicAspect(
        IslamicEventSpec spec,
        SessionSpec session)
    {
        bool hasRelativeStart = session.ReferencePrayer.HasValue && session.PrayerOffsetMinutes.HasValue;

        return new EventSessionIslamicAspect
        {
            EventSessionId = SeedIds.EventSessionId(spec.Number, session.Number),
            StartTimeType = hasRelativeStart ? SessionStartTimeType.RelativeToPrayer : SessionStartTimeType.Fixed,
            ReferencePrayer = hasRelativeStart ? session.ReferencePrayer : null,
            OffsetMinutes = hasRelativeStart ? session.PrayerOffsetMinutes : null,
            RequiresWudu = session.RequiresWudu,
            RitualRequirementsJson = session.RitualRequirementsJson
        };
    }

    private static EventAgendaItem CreateEventAgendaItem(
        IslamicEventSpec spec,
        AgendaItemSpec item,
        IEventScheduleProjectionCalculator calculator)
    {
        var entity = new EventAgendaItem
        {
            Id = SeedIds.EventAgendaItemId(spec.Number, item.Number),
            EventId = spec.Id,
            Event = null!,
            EventDayId = SeedIds.EventDayId(spec.Number, item.DayNumber),
            EventDay = null,
            Title = item.Title,
            Description = item.Description,
            LocationId = item.LocationId,
            Location = null,
            RoomId = item.RoomId,
            Room = null,
            KindId = (int)item.Kind,
            SortOrder = item.Number,
            TenantId = SeedIds.DefaultTenantId,
            Tenant = null!,
            CreatedAt = SeedTimestamp,
            ConcurrencyStamp = SeedIds.EventAgendaItemId(spec.Number, item.Number)
        };

        entity.Reschedule(UtcInstantRange.Create(item.StartUtc, item.EndUtc), BrusselsTimezone, calculator);
        return entity;
    }

    private sealed record IslamicEventSpec(
        int Number,
        Guid Id,
        string Title,
        string Slug,
        string Description,
        EventTypeEnum EventType,
        EventFormatEnum EventFormat,
        AudienceGenderEnum AudienceGender,
        AudienceAgeEnum AudienceAge,
        GenderSegregationMode GenderMode,
        MadhabEnum Madhab,
        int PrimaryLanguageId,
        PrayerTime? ReferencePrayer,
        int? PrayerOffsetMinutes,
        bool IncludesQuranRecitation,
        ParticipationConfigurationSpec Participation,
        Guid PrimaryLocationId,
        IReadOnlyList<Guid> CategoryIds,
        IReadOnlyList<Guid> TagIds,
        IReadOnlyList<DaySpec> Days,
        IReadOnlyList<GroupSpec> Groups,
        IReadOnlyList<SessionSpec> Sessions,
        IReadOnlyList<AgendaItemSpec> AgendaItems);

    private sealed record ParticipationConfigurationSpec(
        ParticipationHandlingModeEnum HandlingMode,
        AdvanceRegistrationObligationEnum AdvanceRegistrationObligation,
        IdentityAccessModeEnum? IdentityAccessMode,
        GuestRecoveryPolicyEnum? GuestRecoveryPolicy);

    private sealed record DaySpec(
        int Number,
        DateOnly LocalDate,
        string Label,
        string Description,
        string BannerText);

    private sealed record GroupSpec(
        int Number,
        string Name,
        string Slug,
        string Description,
        Guid? LocationId,
        Guid? RoomId,
        string Color);

    private sealed record SessionSpec(
        int Number,
        string Title,
        string Slug,
        string Description,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        int DayNumber,
        Guid? LocationId,
        Guid? RoomId,
        EventSessionKindEnum Kind,
        RegistrationModeEnum RegistrationMode,
        IReadOnlyList<int> LanguageIds,
        IReadOnlyList<Guid> SpeakerActorIds,
        IReadOnlyList<Guid> CategoryIds,
        IReadOnlyList<Guid> TagIds,
        PrayerTime? ReferencePrayer,
        int? PrayerOffsetMinutes,
        bool RequiresWudu,
        string? RitualRequirementsJson,
        IReadOnlyList<SessionAgendaItemSpec> AgendaItems)
    {
        public int GroupNumber => Number == 2 ? 2 : 1;
    }

    private sealed record AgendaItemSpec(
        int Number,
        string Title,
        string Description,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        ScheduleItemKindEnum Kind,
        int DayNumber,
        Guid? LocationId,
        Guid? RoomId);

    private sealed record SessionAgendaItemSpec(
        int Number,
        string Title,
        string Description,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);
}
