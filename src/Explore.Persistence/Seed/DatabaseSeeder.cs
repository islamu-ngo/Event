using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Secrets;
using Explore.Domain.Services.Lifecycle;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Persistence.Seed;

/// <summary>
/// Database seeder orchestrator. Called after migrations in application startup.
///
/// ALL environments: Seeds lookup/enum tables via LookupTableSeeder.
/// Development only: Seeds business entities (tenant, users, organizations, members, events)
/// so developers don't have to manually create test data.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ExploreDbContext context,
        IHostEnvironment environment,
        bool seedDevelopmentData = true,
        IConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        var shouldClearBypass = !context.IsTenantFilterBypassed;
        context.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);

        try
        {
            // Lookup tables are required in ALL environments
            await LookupTableSeeder.SeedAsync(context, cancellationToken);

            // Business entities are seeded only in Development for testing
            if (environment.IsDevelopment() && seedDevelopmentData)
            {
                await SeedDevelopmentDataAsync(context, cancellationToken);
                await SeedDevelopmentSmtpAsync(context, configuration, cancellationToken);
                await SeedDevelopmentWebhookSecretsAsync(context, configuration, cancellationToken);
            }

            await EnsureTenantBrandingDocumentsAsync(context, cancellationToken);
        }
        finally
        {
            if (shouldClearBypass)
            {
                context.ClearTenantFilterBypass();
            }
        }
    }


    private static async Task EnsureTenantBrandingDocumentsAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        var tenants = await context.Set<Tenant>()
            .AsNoTracking()
            .Select(tenant => new { tenant.Id, tenant.FullName })
            .ToListAsync(ct);

        if (tenants.Count == 0)
        {
            return;
        }

        var tenantIds = tenants.Select(tenant => tenant.Id).ToList();
        var existingTenantIds = await context.Set<TenantSettingsDocument>()
            .Where(document => document.DocumentKey == SettingsDocumentKeys.Tenant.Branding
                && tenantIds.Contains(document.TenantId))
            .Select(document => document.TenantId)
            .ToListAsync(ct);
        var existing = existingTenantIds.ToHashSet();

        var missingDocuments = tenants
            .Where(tenant => !existing.Contains(tenant.Id))
            .Select(tenant => TenantBrandingSettingsDocumentDefaults.Create(tenant.Id, tenant.FullName))
            .ToList();

        if (missingDocuments.Count == 0)
        {
            return;
        }

        context.Set<TenantSettingsDocument>().AddRange(missingDocuments);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seeds comprehensive test data for Development environment.
    /// Handles circular FK between User/Organization ↔ Actor by inserting in phases.
    /// </summary>
    private static async Task SeedDevelopmentDataAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        var foundationExists = await context.Set<Tenant>().AnyAsync(t => t.Id == SeedIds.DefaultTenantId, ct);
        if (foundationExists)
        {
            await EnsureIslamicEventCatalogAsync(context, ct);
            return;
        }

        // Phase 1: Tenant (foundation for all tenant-scoped entities)
        context.Set<Tenant>().Add(SeedData.DefaultTenant);
        await context.SaveChangesAsync(ct);

        // Phase 2: Global users
        var adminUser = SeedData.AdminUser;
        var regularUser = SeedData.RegularUser;
        var moderatorUser = SeedData.ModeratorUser;
        context.Set<User>().AddRange(adminUser, regularUser, moderatorUser);
        await context.SaveChangesAsync(ct);
        context.Set<UserExternalLogin>().AddRange(
            SeedData.AdminDevelopmentLogin,
            SeedData.RegularDevelopmentLogin,
            SeedData.ModeratorDevelopmentLogin);
        await context.SaveChangesAsync(ct);

        // Phase 3: Global organizations
        var islamuOrg = SeedData.IslamuOrg;
        var techOrg = SeedData.TechOrg;
        context.Set<Organization>().AddRange(islamuOrg, techOrg);
        await context.SaveChangesAsync(ct);

        // Phase 4: Global Actors and tenant organization participation
        context.Set<Actor>().AddRange(
            SeedData.AdminUserActor,
            SeedData.RegularUserActor,
            SeedData.ModeratorUserActor,
            SeedData.IslamuOrgActor,
            SeedData.TechOrgActor);
        await context.SaveChangesAsync(ct);

        context.Set<OrganizationTenant>().AddRange(
            SeedData.IslamuOrgTenant,
            SeedData.TechOrgTenant);
        await context.SaveChangesAsync(ct);

        // Phase 5: Tenant users, tenant role grants, organization members, storage objects
        context.Set<TenantUser>().AddRange(
            SeedData.AdminTenantUser,
            SeedData.RegularTenantUser,
            SeedData.ModeratorTenantUser);

        context.Set<TenantUserRoleGrant>().AddRange(
            SeedData.AdminTenantUserRoleGrant,
            SeedData.RegularTenantUserRoleGrant,
            SeedData.ModeratorTenantUserRoleGrant);

        context.Set<OrganizationMember>().AddRange(
            SeedData.AdminIslamuCreator,
            SeedData.RegularIslamuMember,
            SeedData.ModeratorIslamuMod,
            SeedData.AdminTechCoOwner,
            SeedData.RegularTechCreator);

        context.Set<StorageObject>().AddRange(
            SeedData.DefaultEventImage,
            SeedData.DefaultProfileImage,
            SeedData.DefaultOrganizationLogo);
        await context.SaveChangesAsync(ct);

        // Phase 6: Tenant capabilities
        context.Set<TenantCapability>().AddRange(
            SeedData.DefaultTenantCoreCapability,
            SeedData.DefaultTenantIslamicCapability);
        await context.SaveChangesAsync(ct);

        // Phase 7: Categories, tags, baseline location
        context.Set<Location>().Add(SeedData.OnlineLocation);

        context.Set<Category>().AddRange(
            SeedData.IslamicStudiesCategory,
            SeedData.QuranCategory,
            SeedData.HadithCategory,
            SeedData.FiqhCategory,
            SeedData.AqeedahCategory,
            SeedData.SeerahCategory,
            SeedData.ArabicLanguageCategory,
            SeedData.CommunityEventsCategory);

        context.Set<Tag>().AddRange(
            SeedData.BeginnerTag,
            SeedData.IntermediateTag,
            SeedData.AdvancedTag,
            SeedData.FreeTag,
            SeedData.PaidTag,
            SeedData.OnlineTag,
            SeedData.InPersonTag);
        await context.SaveChangesAsync(ct);

        // Phase 9: Islamic event catalog (depends on actors, storage, tenant, categories, tags)
        await EnsureIslamicEventCatalogAsync(context, ct);
    }

    private static async Task EnsureIslamicEventCatalogAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        await EnsureIslamicEventLocationsAsync(context, ct);

        IReadOnlyList<Event> events = SeedData.IslamicEvents;
        IReadOnlyList<EventSessionGroup> sessionGroups = SeedData.IslamicSessionGroups;
        IReadOnlyList<EventSession> sessions = SeedData.IslamicEventSessions;
        IReadOnlyList<EventAgendaItem> eventAgendaItems = SeedData.IslamicEventAgendaItems;
        IReadOnlyList<EventSessionAgendaItem> sessionAgendaItems = SeedData.IslamicSessionAgendaItems;

        await AddMissingSeedRowsAsync(context, events, e => e.Id, ct);
        await AddMissingSeedRowsAsync(
            context,
            SeedData.IslamicEventParticipationConfigurations,
            configuration => configuration.Id,
            ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicEventAspects, aspect => aspect.Id, ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicEventDays, day => day.Id, ct);
        await EnsureIslamicEventLocationAuthoritiesAsync(
            context,
            sessionGroups,
            sessions,
            eventAgendaItems,
            sessionAgendaItems,
            ct);
        await EnsureIslamicEventSessionStatusesAsync(context, ct);
        await EnsureIslamicEventScheduleSummariesAsync(context, ct);
        await AddMissingSessionAspectsAsync(context, ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicSessionGroupSessions, session => session.Id, ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicEventCategories, category => category.Id, ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicEventTags, tag => tag.Id, ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicSessionCategories, category => category.Id, ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicSessionTags, tag => tag.Id, ct);
        await AddMissingSessionLanguagesAsync(context, ct);
        await AddMissingSeedRowsAsync(context, SeedData.IslamicSessionSpeakers, speaker => speaker.Id, ct);
    }

    private static async Task EnsureIslamicEventLocationAuthoritiesAsync(
        ExploreDbContext context,
        IReadOnlyList<EventSessionGroup> seedGroups,
        IReadOnlyList<EventSession> seedSessions,
        IReadOnlyList<EventAgendaItem> seedEventAgendaItems,
        IReadOnlyList<EventSessionAgendaItem> seedSessionAgendaItems,
        CancellationToken ct)
    {
        Guid[] eventIds = SeedIds.IslamicEventCatalogIds;
        EventLocation[] existingEventLocations = await context.EventLocations
            .IgnoreQueryFilters()
            .Where(item => eventIds.Contains(item.EventId) && !item.IsDeleted)
            .ToArrayAsync(ct);
        var eventLocationByPair = existingEventLocations.ToDictionary(
            item => (item.EventId, item.LocationId));

        var sessionEventIdById = seedSessions.ToDictionary(item => item.Id, item => item.EventId);
        var requiredPairs = seedGroups.Select(item => (item.EventId, item.LocationId))
            .Concat(seedSessions.Select(item => (item.EventId, item.LocationId)))
            .Concat(seedEventAgendaItems.Select(item => (item.EventId, item.LocationId)))
            .Concat(seedSessionAgendaItems.Select(item => (
                EventId: sessionEventIdById[item.EventSessionId],
                LocationId: item.LocationId)))
            .Distinct()
            .ToArray();

        foreach (var pair in requiredPairs)
        {
            if (eventLocationByPair.ContainsKey(pair))
            {
                continue;
            }

            EventLocation eventLocation = pair.LocationId.HasValue
                ? EventLocation.CreatePhysical(
                    SeedIds.DefaultTenantId,
                    pair.EventId,
                    pair.LocationId.Value,
                    SeedIds.AdminUserId,
                    DateTime.UnixEpoch)
                : EventLocation.CreateToBeAnnounced(
                    SeedIds.DefaultTenantId,
                    pair.EventId,
                    SeedIds.AdminUserId,
                    DateTime.UnixEpoch);
            context.EventLocations.Add(eventLocation);
            context.EventLocationDisclosureAudits.Add(eventLocation.CreateInitialDisclosureAudit());
            eventLocationByPair.Add(pair, eventLocation);
        }

        foreach (EventSessionGroup group in seedGroups)
        {
            group.AssignEventLocation(eventLocationByPair[(group.EventId, group.LocationId)]);
        }

        foreach (EventSession session in seedSessions)
        {
            session.AssignEventLocation(eventLocationByPair[(session.EventId, session.LocationId)]);
        }

        foreach (EventAgendaItem item in seedEventAgendaItems)
        {
            item.AssignEventLocation(eventLocationByPair[(item.EventId, item.LocationId)]);
        }

        var seedSessionById = seedSessions.ToDictionary(item => item.Id);
        foreach (EventSessionAgendaItem item in seedSessionAgendaItems)
        {
            item.EventSession = seedSessionById[item.EventSessionId];
            item.AssignEventLocation(eventLocationByPair[(item.EventSession.EventId, item.LocationId)]);
        }

        Guid[] groupIds = seedGroups.Select(item => item.Id).ToArray();
        Guid[] sessionIds = seedSessions.Select(item => item.Id).ToArray();
        Guid[] eventAgendaItemIds = seedEventAgendaItems.Select(item => item.Id).ToArray();
        Guid[] sessionAgendaItemIds = seedSessionAgendaItems.Select(item => item.Id).ToArray();
        EventSessionGroup[] existingGroups = await context.EventSessionGroups
            .IgnoreQueryFilters()
            .Where(item => groupIds.Contains(item.Id))
            .ToArrayAsync(ct);
        EventSession[] existingSessions = await context.EventSessions
            .IgnoreQueryFilters()
            .Where(item => sessionIds.Contains(item.Id))
            .ToArrayAsync(ct);
        EventAgendaItem[] existingEventAgendaItems = await context.EventAgendaItems
            .IgnoreQueryFilters()
            .Where(item => eventAgendaItemIds.Contains(item.Id))
            .ToArrayAsync(ct);
        EventSessionAgendaItem[] existingSessionAgendaItems = await context.EventSessionAgendaItems
            .IgnoreQueryFilters()
            .Include(item => item.EventSession)
            .Where(item => sessionAgendaItemIds.Contains(item.Id))
            .ToArrayAsync(ct);

        foreach (EventSessionGroup group in existingGroups)
        {
            group.AssignEventLocation(eventLocationByPair[(group.EventId, group.LocationId)]);
        }

        foreach (EventSession session in existingSessions)
        {
            session.AssignEventLocation(eventLocationByPair[(session.EventId, session.LocationId)]);
        }

        foreach (EventAgendaItem item in existingEventAgendaItems)
        {
            item.AssignEventLocation(eventLocationByPair[(item.EventId, item.LocationId)]);
        }

        foreach (EventSessionAgendaItem item in existingSessionAgendaItems)
        {
            item.AssignEventLocation(eventLocationByPair[(item.EventSession.EventId, item.LocationId)]);
        }

        var existingGroupIds = existingGroups.Select(item => item.Id).ToHashSet();
        var existingSessionIds = existingSessions.Select(item => item.Id).ToHashSet();
        var existingEventAgendaItemIds = existingEventAgendaItems.Select(item => item.Id).ToHashSet();
        var existingSessionAgendaItemIds = existingSessionAgendaItems.Select(item => item.Id).ToHashSet();
        context.EventSessionGroups.AddRange(seedGroups.Where(item => !existingGroupIds.Contains(item.Id)));
        context.EventSessions.AddRange(seedSessions.Where(item => !existingSessionIds.Contains(item.Id)));
        context.EventAgendaItems.AddRange(
            seedEventAgendaItems.Where(item => !existingEventAgendaItemIds.Contains(item.Id)));
        context.EventSessionAgendaItems.AddRange(
            seedSessionAgendaItems.Where(item => !existingSessionAgendaItemIds.Contains(item.Id)));

        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();
    }

    private static async Task EnsureIslamicEventSessionStatusesAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        var seedSessions = SeedData.IslamicEventSessions.ToDictionary(session => session.Id);
        var seedSessionIds = seedSessions.Keys.ToArray();

        if (seedSessionIds.Length == 0)
        {
            return;
        }

        var sessions = await context.EventSessions
            .IgnoreQueryFilters()
            .Where(session => seedSessionIds.Contains(session.Id))
            .Where(session => session.EventSessionStatusId != (int)EventSessionStatusEnum.Published)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            if (!EventSessionLifecycleRules.CanPublish(
                    (EventSessionStatusEnum)session.EventSessionStatusId,
                    EventStatusEnum.Published,
                    session.StartTime,
                    session.EndTime,
                    session.EndTimeType))
            {
                continue;
            }

            session.Publish(EventStatusEnum.Published, seedSessions[session.Id].CreatedAt);
        }

        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();
    }

    private static async Task EnsureIslamicEventScheduleSummariesAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        var seedEvents = SeedData.IslamicEvents.ToDictionary(@event => @event.Id);
        var seedEventIds = seedEvents.Keys.ToArray();

        if (seedEventIds.Length == 0)
        {
            return;
        }

        var existingEvents = await context.Events
            .IgnoreQueryFilters()
            .Where(@event => seedEventIds.Contains(@event.Id))
            .ToListAsync(ct);

        foreach (var existingEvent in existingEvents)
        {
            var seedEvent = seedEvents[existingEvent.Id];
            existingEvent.SessionCount = seedEvent.SessionCount;
            existingEvent.FirstSessionDate = seedEvent.FirstSessionDate;
            existingEvent.LastSessionDate = seedEvent.LastSessionDate;
            existingEvent.FirstSessionStartUtc = seedEvent.FirstSessionStartUtc;
            existingEvent.LastSessionStartUtc = seedEvent.LastSessionStartUtc;
            existingEvent.LastSessionEndUtc = seedEvent.LastSessionEndUtc;
        }

        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();
    }

    private static async Task AddMissingSeedRowsAsync<T>(
        ExploreDbContext context,
        IReadOnlyCollection<T> seedRows,
        Func<T, Guid> getId,
        CancellationToken ct)
        where T : class
    {
        if (seedRows.Count == 0)
        {
            return;
        }

        var seedIds = seedRows.Select(getId).ToArray();
        var existingIds = await context.Set<T>()
            .IgnoreQueryFilters()
            .Where(row => seedIds.Contains(EF.Property<Guid>(row, "Id")))
            .Select(row => EF.Property<Guid>(row, "Id"))
            .ToArrayAsync(ct);
        var existing = existingIds.ToHashSet();
        var missing = seedRows
            .Where(row => !existing.Contains(getId(row)))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        context.Set<T>().AddRange(missing);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();
    }

    private static async Task AddMissingSessionLanguagesAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        var seedRows = SeedData.IslamicSessionLanguages;
        if (seedRows.Count == 0)
        {
            return;
        }

        var sessionIds = seedRows
            .Select(language => language.EventSessionId)
            .Distinct()
            .ToArray();
        var existingKeys = await context.Set<EventSessionLanguage>()
            .IgnoreQueryFilters()
            .Where(language => sessionIds.Contains(language.EventSessionId))
            .Select(language => new
            {
                language.EventSessionId,
                language.LanguageId
            })
            .ToArrayAsync(ct);
        var existing = existingKeys
            .Select(language => (language.EventSessionId, language.LanguageId))
            .ToHashSet();
        var missing = seedRows
            .Where(language => !existing.Contains((language.EventSessionId, language.LanguageId)))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        context.Set<EventSessionLanguage>().AddRange(missing);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();
    }

    private static async Task AddMissingSessionAspectsAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        var seedRows = SeedData.IslamicSessionAspects;
        if (seedRows.Count == 0)
        {
            return;
        }

        var seedSessionIds = seedRows
            .Select(aspect => aspect.EventSessionId)
            .ToArray();
        var existingSessionIds = await context.Set<EventSessionIslamicAspect>()
            .Where(aspect => seedSessionIds.Contains(aspect.EventSessionId))
            .Select(aspect => aspect.EventSessionId)
            .ToArrayAsync(ct);
        var existing = existingSessionIds.ToHashSet();
        var missing = seedRows
            .Where(aspect => !existing.Contains(aspect.EventSessionId))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        context.Set<EventSessionIslamicAspect>().AddRange(missing);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();
    }

    private static async Task EnsureIslamicEventLocationsAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        var locationIds = SeedData.IslamicEventLocations.Select(location => location.Id).ToList();
        var existingLocationIds = await context.Set<Location>()
            .Where(location => locationIds.Contains(location.Id))
            .Select(location => location.Id)
            .ToListAsync(ct);

        var existingLocations = existingLocationIds.ToHashSet();
        var missingLocations = SeedData.IslamicEventLocations
            .Where(location => !existingLocations.Contains(location.Id))
            .ToList();

        if (missingLocations.Count > 0)
        {
            context.Set<Location>().AddRange(missingLocations);
            await context.SaveChangesAsync(ct);
        }

        var roomIds = SeedData.IslamicEventRooms.Select(room => room.Id).ToList();
        var existingRoomIds = await context.Set<LocationRoom>()
            .Where(room => roomIds.Contains(room.Id))
            .Select(room => room.Id)
            .ToListAsync(ct);

        var existingRooms = existingRoomIds.ToHashSet();
        var missingRooms = SeedData.IslamicEventRooms
            .Where(room => !existingRooms.Contains(room.Id))
            .ToList();

        if (missingRooms.Count > 0)
        {
            context.Set<LocationRoom>().AddRange(missingRooms);
            await context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Pre-configures local Mailpit SMTP in Development for email testing.
    /// Mailpit captures all outbound emails for inspection without delivery.
    /// Same SmtpEmailService code path is used in production with real SMTP credentials.
    /// Idempotent: only applies when SMTP host is empty (not yet manually configured).
    /// </summary>
    private static async Task SeedDevelopmentSmtpAsync(
        ExploreDbContext context,
        IConfiguration? configuration,
        CancellationToken ct)
    {
        var hostSetting = await context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.SettingKey == GovernanceSettingKeys.Email.SmtpHost, ct);

        if (hostSetting is null)
            return;

        var forceLocalAspireRefresh = IsFullLocalAspireMode(configuration);
        var currentHost = hostSetting.Value?.Trim('"');
        if (!forceLocalAspireRefresh
            && !string.IsNullOrWhiteSpace(currentHost)
            && !currentHost.Equals("mailpit.openislamu.org", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var host = ReadEnvironmentValue(configuration, "localhost", "MAIL_SMTP_HOST", "SMTP_HOST", "Smtp:Host");
        var port = ReadEnvironmentValue(configuration, "1025", "MAIL_SMTP_PORT", "SMTP_PORT", "Smtp:Port");
        var security = NormalizeSmtpSecurity(ReadEnvironmentValue(configuration, "None", "MAIL_SMTP_ENCRYPTION", "SMTP_SECURITY", "Smtp:Encryption"));
        var fromAddress = ReadEnvironmentValue(configuration, "noreply@localhost", "MAIL_SMTP_FROM_ADDRESS", "SMTP_FROM_ADDRESS", "Smtp:FromAddress");
        var fromName = ReadEnvironmentValue(configuration, "ISLAMU Event Dev", "MAIL_SMTP_FROM_NAME", "SMTP_FROM_NAME", "Smtp:FromName");

        await UpdateSettingValueAsync(context, GovernanceSettingKeys.Email.SmtpHost, SerializeString(host), now, ct);
        await UpdateSettingValueAsync(context, GovernanceSettingKeys.Email.SmtpPort, port, now, ct);
        await UpdateSettingValueAsync(context, GovernanceSettingKeys.Email.SmtpSecurity, SerializeString(security), now, ct);
        await UpdateSettingValueAsync(context, GovernanceSettingKeys.Email.FromAddress, SerializeString(fromAddress), now, ct);
        await UpdateSettingValueAsync(context, GovernanceSettingKeys.Email.FromName, SerializeString(fromName), now, ct);

        await context.SaveChangesAsync(ct);
    }

    private static bool IsFullLocalAspireMode(IConfiguration? configuration)
    {
        var value = Environment.GetEnvironmentVariable("ISLAMU_ASPIRE_MODE")
            ?? configuration?["ISLAMU_ASPIRE_MODE"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return normalized.Equals("full", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("localfull", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("fulllocal", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadEnvironmentValue(IConfiguration? configuration, string defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        foreach (var key in keys)
        {
            var value = configuration?[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return defaultValue;
    }

    private static async Task SeedDevelopmentWebhookSecretsAsync(
        ExploreDbContext context,
        IConfiguration? configuration,
        CancellationToken ct)
    {
        await EnsureDevelopmentEnvironmentSecretBindingAsync(
            context,
            ResolveKnownSecretRef(
                configuration,
                SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
                "WEBHOOKS_SVIX_AUTH_TOKEN_SECRET_REF",
                "Webhooks:Svix:AuthTokenSecretRef"),
            "WEBHOOKS_SVIX_AUTH_TOKEN",
            configuration,
            ct);

        await EnsureDevelopmentEnvironmentSecretBindingAsync(
            context,
            ResolveKnownSecretRef(
                configuration,
                SecretDefinitionRegistry.Keys.Webhooks.SvixOperationalWebhookSecret,
                "WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET_REF",
                "Webhooks:Svix:OperationalWebhookSecretRef"),
            "WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET",
            configuration,
            ct);
    }

    private static string ResolveKnownSecretRef(
        IConfiguration? configuration,
        string defaultValue,
        params string[] keys)
    {
        var configured = ReadEnvironmentValue(configuration, defaultValue, keys).Trim();
        return SecretDefinitionRegistry.IsKnown(configured) ? configured : defaultValue;
    }

    private static async Task EnsureDevelopmentEnvironmentSecretBindingAsync(
        ExploreDbContext context,
        string settingKey,
        string environmentVariableName,
        IConfiguration? configuration,
        CancellationToken ct)
    {
        if (!HasConfiguredSecretValue(configuration, environmentVariableName))
        {
            return;
        }

        var existing = await context.Set<SecretBinding>()
            .FirstOrDefaultAsync(binding =>
                binding.SettingKey == settingKey
                && binding.SettingScopeId == (int)ConfigurationScopeEnum.Instance
                && binding.ScopeId == null,
                ct);

        if (existing is not null)
        {
            return;
        }

        context.Set<SecretBinding>().Add(SecretBinding.CreateEnvironmentVariable(
            settingKey,
            SecretScope.Instance,
            scopeId: null,
            environmentVariableName,
            isLocked: false));

        await context.SaveChangesAsync(ct);
    }

    private static bool HasConfiguredSecretValue(IConfiguration? configuration, string key) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))
        || !string.IsNullOrWhiteSpace(configuration?[key]);

    private static string NormalizeSmtpSecurity(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "none" or "false" or "off" => "None",
            "starttls" or "start-tls" => "StartTls",
            "ssl" or "tls" or "ssl-on-connect" or "sslonconnect" => "SslOnConnect",
            "auto" => "Auto",
            _ => value
        };
    }

    private static string SerializeString(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static async Task UpdateSettingValueAsync(
        ExploreDbContext context,
        string key,
        string value,
        DateTime timestamp,
        CancellationToken ct)
    {
        var setting = await context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.SettingKey == key, ct);

        if (setting is not null)
        {
            setting.Value = value;
            setting.UpdatedAt = timestamp;
        }
    }
}
