// ABOUTME: Integration tests for runtime database seeding against PostgreSQL.
// ABOUTME: Verifies development catalog seeding remains idempotent and preserves user state across API startups.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Seed;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class DatabaseSeederTests(PostgreSqlContainerFixture fixture)
{
    private static readonly IHostEnvironment DevelopmentEnvironment = new TestHostEnvironment();

    [Test]
    [Category("EventLocationPrivacy")]
    public async Task SeedAsync_InDevelopment_IsIdempotentAcrossStartups()
    {
        await fixture.ResetAsync();

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        CatalogLocationAuthoritySnapshot firstSeed;
        await using (var context = fixture.CreateDbContext())
        {
            firstSeed = await GetCatalogLocationAuthoritySnapshotAsync(context);
        }

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var visibleCatalogCount = await verifyContext.Events
            .Where(e => SeedIds.IslamicEventCatalogIds.Contains(e.Id))
            .CountAsync();
        var unfilteredCatalogCount = await verifyContext.Events
            .IgnoreQueryFilters()
            .Where(e => SeedIds.IslamicEventCatalogIds.Contains(e.Id))
            .CountAsync();
        var softDeletedCatalogCount = await verifyContext.Events
            .IgnoreQueryFilters()
            .Where(e => SeedIds.IslamicEventCatalogIds.Contains(e.Id) && e.IsDeleted)
            .CountAsync();

        await Assert.That(visibleCatalogCount).IsEqualTo(SeedIds.IslamicEventCatalogIds.Length);
        await Assert.That(unfilteredCatalogCount).IsEqualTo(SeedIds.IslamicEventCatalogIds.Length);
        await Assert.That(softDeletedCatalogCount).IsEqualTo(0);

        CatalogLocationAuthoritySnapshot secondSeed = await GetCatalogLocationAuthoritySnapshotAsync(verifyContext);
        await Assert.That(firstSeed.SessionCount).IsEqualTo(9);
        await Assert.That(firstSeed.GroupCount).IsEqualTo(9);
        await Assert.That(firstSeed.EventAgendaItemCount).IsEqualTo(9);
        await Assert.That(firstSeed.SessionAgendaItemCount).IsEqualTo(9);
        await Assert.That(firstSeed.MismatchCount).IsEqualTo(0);
        await Assert.That(firstSeed.DuplicateActivePairCount).IsEqualTo(0);
        await Assert.That(firstSeed.ActiveEventLocationCount)
            .IsEqualTo(firstSeed.CarrierEventLocationIds.Distinct().Count());
        await Assert.That(firstSeed.InitialAuditCount).IsEqualTo(firstSeed.ActiveEventLocationCount);
        await Assert.That(firstSeed.InitialAuditMismatchCount).IsEqualTo(0);
        await Assert.That(secondSeed.MismatchCount).IsEqualTo(0);
        await Assert.That(secondSeed.ActiveEventLocationCount).IsEqualTo(firstSeed.ActiveEventLocationCount);
        await Assert.That(secondSeed.InitialAuditCount).IsEqualTo(firstSeed.InitialAuditCount);
        await Assert.That(secondSeed.ActiveEventLocationIds.SequenceEqual(firstSeed.ActiveEventLocationIds)).IsTrue();
        await Assert.That(secondSeed.CarrierEventLocationIds.SequenceEqual(firstSeed.CarrierEventLocationIds)).IsTrue();
    }

    private static async Task<CatalogLocationAuthoritySnapshot> GetCatalogLocationAuthoritySnapshotAsync(
        ExploreDbContext context)
    {
        Guid[] sessionIds = SeedData.IslamicEventSessions.Select(item => item.Id).ToArray();
        Guid[] groupIds = SeedData.IslamicSessionGroups.Select(item => item.Id).ToArray();
        Guid[] eventAgendaItemIds = SeedData.IslamicEventAgendaItems.Select(item => item.Id).ToArray();
        Guid[] sessionAgendaItemIds = SeedData.IslamicSessionAgendaItems.Select(item => item.Id).ToArray();

        var sessions = await context.EventSessions
            .IgnoreQueryFilters()
            .Where(item => sessionIds.Contains(item.Id))
            .Select(item => new CarrierIdentity(item.TenantId, item.EventId, item.LocationId, item.EventLocationId))
            .ToListAsync();
        var groups = await context.EventSessionGroups
            .IgnoreQueryFilters()
            .Where(item => groupIds.Contains(item.Id))
            .Select(item => new CarrierIdentity(item.TenantId, item.EventId, item.LocationId, item.EventLocationId))
            .ToListAsync();
        var eventAgendaItems = await context.EventAgendaItems
            .IgnoreQueryFilters()
            .Where(item => eventAgendaItemIds.Contains(item.Id))
            .Select(item => new CarrierIdentity(item.TenantId, item.EventId, item.LocationId, item.EventLocationId))
            .ToListAsync();
        var sessionAgendaItems = await context.EventSessionAgendaItems
            .IgnoreQueryFilters()
            .Where(item => sessionAgendaItemIds.Contains(item.Id))
            .Select(item => new CarrierIdentity(
                item.TenantId,
                item.EventSession.EventId,
                item.LocationId,
                item.EventLocationId))
            .ToListAsync();

        CarrierIdentity[] carriers = sessions
            .Concat(groups)
            .Concat(eventAgendaItems)
            .Concat(sessionAgendaItems)
            .ToArray();
        EventLocation[] eventLocations = await context.EventLocations
            .IgnoreQueryFilters()
            .Where(item => !item.IsDeleted && SeedIds.IslamicEventCatalogIds.Contains(item.EventId))
            .ToArrayAsync();
        IReadOnlyDictionary<Guid, EventLocation> eventLocationById = eventLocations.ToDictionary(item => item.Id);
        int mismatchCount = carriers.Count(carrier =>
            carrier.EventLocationId is not { } eventLocationId
            || !eventLocationById.TryGetValue(eventLocationId, out EventLocation? eventLocation)
            || eventLocation.TenantId != carrier.TenantId
            || eventLocation.EventId != carrier.EventId
            || eventLocation.LocationId != carrier.LocationId
            || eventLocation.IsToBeAnnounced != !carrier.LocationId.HasValue);
        int duplicateActivePairCount = eventLocations
            .GroupBy(item => new { item.TenantId, item.EventId, item.LocationId, item.IsToBeAnnounced })
            .Count(group => group.Count() > 1);
        Guid[] eventLocationIds = eventLocations.Select(item => item.Id).ToArray();
        EventLocationDisclosureAudit[] initialAudits = await context.EventLocationDisclosureAudits
            .IgnoreQueryFilters()
            .Where(item => eventLocationIds.Contains(item.EventLocationId)
                && item.PreviousPolicyVersion == 0
                && item.NewPolicyVersion == 1
                && item.Reason == EventLocationDisclosureAuditReasonEnum.AssociationCreated)
            .ToArrayAsync();
        int initialAuditMismatchCount = initialAudits.Count(audit =>
            audit.ActorUserId != SeedIds.AdminUserId
            || !eventLocationById.TryGetValue(audit.EventLocationId, out EventLocation? eventLocation)
            || audit.TenantId != eventLocation.TenantId);

        return new(
            sessions.Count,
            groups.Count,
            eventAgendaItems.Count,
            sessionAgendaItems.Count,
            mismatchCount,
            duplicateActivePairCount,
            eventLocations.Length,
            initialAudits.Length,
            initialAuditMismatchCount,
            eventLocations.Select(item => item.Id).Order().ToArray(),
            carriers
                .Where(item => item.EventLocationId.HasValue)
                .Select(item => item.EventLocationId!.Value)
                .Order()
                .ToArray());
    }

    private sealed record CarrierIdentity(
        Guid TenantId,
        Guid EventId,
        Guid? LocationId,
        Guid? EventLocationId);

    private sealed record CatalogLocationAuthoritySnapshot(
        int SessionCount,
        int GroupCount,
        int EventAgendaItemCount,
        int SessionAgendaItemCount,
        int MismatchCount,
        int DuplicateActivePairCount,
        int ActiveEventLocationCount,
        int InitialAuditCount,
        int InitialAuditMismatchCount,
        Guid[] ActiveEventLocationIds,
        Guid[] CarrierEventLocationIds);

    [Test]
    public async Task LookupSeedAsync_RepairsCanonicalNotificationDeliveryRowsByStableId()
    {
        await fixture.ResetAsync();

        await using (var staleContext = fixture.CreateDbContext())
        {
            NotificationPreferenceChannel channel = await staleContext.NotificationPreferenceChannels
                .SingleAsync(row => row.Id == (int)NotificationPreferenceChannelEnum.InApp);
            channel.MasterCode = "in-app";
            channel.FullName = "Stale channel";
            channel.Description = "Stale channel description";
            channel.SortOrder = 999;

            NotificationDeliveryStatus queued = await staleContext.NotificationDeliveryStatuses
                .SingleAsync(row => row.Id == (int)NotificationDeliveryStatusEnum.Queued);
            queued.MasterCode = "LINKED_TO_EMAIL_DISPATCH";
            queued.FullName = "Stale queued status";
            queued.Description = "Stale queued description";

            NotificationDeliveryStatus delivered = await staleContext.NotificationDeliveryStatuses
                .SingleAsync(row => row.Id == (int)NotificationDeliveryStatusEnum.Delivered);
            delivered.MasterCode = "SENT";
            delivered.FullName = "Stale delivered status";
            delivered.Description = "Stale delivered description";

            NotificationDeliveryPolicy policy = await staleContext.Set<NotificationDeliveryPolicy>()
                .SingleAsync(row => row.Id == (int)NotificationDeliveryPolicyEnum.ReportCaseUpdate);
            policy.MasterCode = "STALE_REPORT_POLICY";
            policy.FullName = "Stale report policy";
            policy.Description = "Stale report policy description";
            await staleContext.SaveChangesAsync();
        }

        await using (var repairContext = fixture.CreateDbContext())
        {
            await LookupTableSeeder.SeedAsync(repairContext);
        }

        await using var verifyContext = fixture.CreateDbContext();
        NotificationPreferenceChannel repairedChannel = await verifyContext.NotificationPreferenceChannels
            .AsNoTracking()
            .SingleAsync(row => row.Id == (int)NotificationPreferenceChannelEnum.InApp);
        NotificationDeliveryStatus repairedQueued = await verifyContext.NotificationDeliveryStatuses
            .AsNoTracking()
            .SingleAsync(row => row.Id == (int)NotificationDeliveryStatusEnum.Queued);
        NotificationDeliveryStatus repairedDelivered = await verifyContext.NotificationDeliveryStatuses
            .AsNoTracking()
            .SingleAsync(row => row.Id == (int)NotificationDeliveryStatusEnum.Delivered);
        NotificationDeliveryPolicy repairedPolicy = await verifyContext.Set<NotificationDeliveryPolicy>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == (int)NotificationDeliveryPolicyEnum.ReportCaseUpdate);

        await Assert.That(repairedChannel.MasterCode).IsEqualTo(NotificationPreferenceChannelCodes.InApp);
        await Assert.That(repairedChannel.FullName).IsEqualTo("In-App");
        await Assert.That(repairedChannel.SortOrder).IsEqualTo(20);
        await Assert.That(repairedQueued.MasterCode).IsEqualTo("QUEUED");
        await Assert.That(repairedQueued.FullName).IsEqualTo("Queued");
        await Assert.That(repairedDelivered.MasterCode).IsEqualTo("DELIVERED");
        await Assert.That(repairedDelivered.FullName).IsEqualTo("Delivered");
        await Assert.That(repairedPolicy.MasterCode).IsEqualTo("REPORT_CASE_UPDATE");
        await Assert.That(repairedPolicy.FullName).IsEqualTo("Report case update");
    }

    [Test]
    public async Task SeedAsync_InDevelopment_RepairsLaunchCatalogDiscoveryFieldsAcrossStartups()
    {
        await fixture.ResetAsync();

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        await using (var staleContext = fixture.CreateDbContext())
        {
            var catalogEvents = await staleContext.Events
                .IgnoreQueryFilters()
                .Where(e => SeedIds.IslamicEventCatalogIds.Contains(e.Id))
                .ToListAsync();
            foreach (var catalogEvent in catalogEvents)
            {
                catalogEvent.LastSessionEndUtc = null;
            }

            await staleContext.EventSessions
                .IgnoreQueryFilters()
                .Where(session => SeedIds.IslamicEventCatalogIds.Contains(session.EventId))
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    session => session.EventSessionStatusId,
                    (int)EventSessionStatusEnum.Draft));

            await staleContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var repairedEvents = await verifyContext.Events
            .IgnoreQueryFilters()
            .Where(e => SeedIds.IslamicEventCatalogIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstSessionStartUtc, e.LastSessionEndUtc })
            .ToListAsync();
        var repairedSessions = await verifyContext.EventSessions
            .IgnoreQueryFilters()
            .Where(session => SeedIds.IslamicEventCatalogIds.Contains(session.EventId))
            .Select(session => new { session.Id, session.EventSessionStatusId })
            .ToListAsync();

        await Assert.That(repairedEvents.Count).IsEqualTo(SeedIds.IslamicEventCatalogIds.Length);
        await Assert.That(repairedSessions.Count).IsEqualTo(SeedData.IslamicEventSessions.Count);
        await Assert.That(repairedEvents.All(e =>
            e.FirstSessionStartUtc.HasValue
            && e.LastSessionEndUtc.HasValue
            && e.LastSessionEndUtc.Value > e.FirstSessionStartUtc.Value)).IsTrue();
        await Assert.That(repairedSessions.All(session =>
            session.EventSessionStatusId == (int)EventSessionStatusEnum.Published)).IsTrue();
    }

    [Test]
    [Arguments(EventSessionStatusEnum.Draft)]
    [Arguments(EventSessionStatusEnum.Submitted)]
    [Arguments(EventSessionStatusEnum.UnderReview)]
    [Arguments(EventSessionStatusEnum.Approved)]
    [Arguments(EventSessionStatusEnum.Published)]
    [Arguments(EventSessionStatusEnum.Rejected)]
    [Arguments(EventSessionStatusEnum.Cancelled)]
    [Arguments(EventSessionStatusEnum.Archived)]
    [Arguments(EventSessionStatusEnum.Completed)]
    [Arguments(EventSessionStatusEnum.Moderated)]
    public async Task SeedAsync_InDevelopment_RepairsOnlyPublishableSessionStatuses(
        EventSessionStatusEnum status)
    {
        await fixture.ResetAsync();

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        Guid sessionId = SeedData.IslamicEventSessions[0].Id;
        await using (var corruptContext = fixture.CreateDbContext())
        {
            await corruptContext.EventSessions
                .IgnoreQueryFilters()
                .Where(session => session.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    session => session.EventSessionStatusId,
                    (int)status));
        }

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        EventSessionStatusEnum expected = status is
            EventSessionStatusEnum.Draft or
            EventSessionStatusEnum.Submitted or
            EventSessionStatusEnum.UnderReview or
            EventSessionStatusEnum.Approved
                ? EventSessionStatusEnum.Published
                : status;
        await using var verifyContext = fixture.CreateDbContext();
        int repairedStatusId = await verifyContext.EventSessions
            .IgnoreQueryFilters()
            .Where(session => session.Id == sessionId)
            .Select(session => session.EventSessionStatusId)
            .SingleAsync();

        await Assert.That(repairedStatusId).IsEqualTo((int)expected);
    }

    [Test]
    [Arguments(EventSessionStatusEnum.Draft)]
    [Arguments(EventSessionStatusEnum.Submitted)]
    [Arguments(EventSessionStatusEnum.UnderReview)]
    [Arguments(EventSessionStatusEnum.Approved)]
    public async Task SeedAsync_InDevelopment_PreservesPublishableStatusWhenScheduleIsInvalid(
        EventSessionStatusEnum status)
    {
        await fixture.ResetAsync();

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        Guid sessionId = SeedData.IslamicEventSessions[0].Id;
        await using (var corruptContext = fixture.CreateDbContext())
        {
            await corruptContext.EventSessions
                .IgnoreQueryFilters()
                .Where(session => session.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.EventSessionStatusId, (int)status)
                    .SetProperty(session => session.StartTime, (DateTimeOffset?)null));
        }

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        await using var verifyContext = fixture.CreateDbContext();
        int repairedStatusId = await verifyContext.EventSessions
            .IgnoreQueryFilters()
            .Where(session => session.Id == sessionId)
            .Select(session => session.EventSessionStatusId)
            .SingleAsync();

        await Assert.That(repairedStatusId).IsEqualTo((int)status);
    }

    [Test]
    public async Task SeedAsync_InDevelopment_PreservesRegistrationOrderAndConsentAcrossStartups()
    {
        await fixture.ResetAsync();

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        var orderId = Guid.Parse("018e4e5c-7f00-7001-8000-000000010001");
        var registrationId = Guid.Parse("018e4e5c-7f00-7001-8000-000000010002");
        Guid consentId;

        await using (var context = fixture.CreateDbContext())
        {
            var eventId = SeedIds.QuranTafsirWomenEventId;
            var sessionId = await context.EventSessions
                .IgnoreQueryFilters()
                .Where(session => session.EventId == eventId)
                .Select(session => session.Id)
                .FirstAsync();
            var recipientActorId = await context.Events
                .IgnoreQueryFilters()
                .Where(e => e.Id == eventId)
                .Select(e => e.ActorId)
                .SingleAsync();

            DateTime now = DateTime.UtcNow;
            EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(
                SeedIds.DefaultTenantId,
                eventId,
                "USD",
                versionNumber: 10_001);
            RegistrationOrder order = RegistrationOrder.Create(
                orderId,
                SeedIds.DefaultTenantId,
                eventId,
                SeedIds.RegularUserId,
                purchaserActorId: null,
                BookingPartyTypeEnum.Individual,
                catalog.Id,
                RegistrationParticipationSnapshot.Create(
                    Guid.CreateVersion7(),
                    4,
                    3,
                    2,
                    GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: null,
                "USD",
                now,
                expiresAt: null);
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, now);
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
            order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
            context.EventTicketCatalogVersions.Add(catalog);
            context.RegistrationOrders.Add(order);
            RegistrationParticipant participant = RegistrationParticipant.Create(
                SeedIds.DefaultTenantId,
                orderId,
                SeedIds.RegularUserId,
                ParticipantTypeEnum.Adult,
                guardian: null);
            context.RegistrationParticipants.Add(participant);
            context.Set<EventRegistration>().Add(new EventRegistration
            {
                Id = registrationId,
                EventId = eventId,
                Event = null!,
                LinkedUserId = SeedIds.RegularUserId,
                LinkedUser = null!,
                EventSessionId = sessionId,
                EventSession = null!,
                RegistrationOrderId = orderId,
                RegistrationParticipantId = participant.Id,
                RegistrationParticipant = participant,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                TenantId = SeedIds.DefaultTenantId,
                Tenant = null!,
                CreatedAt = now
            });
            EventContactShareConsent consent = EventContactShareConsent.Grant(
                SeedIds.DefaultTenantId, ContactShareConsentSubjectTypeEnum.User, SeedIds.RegularUserId,
                recipientActorId, ConsentPurposeCodes.OrganizerFutureCommunications, "user@example.test",
                "Share my email with the organizer.", "v1", now);
            consentId = consent.Id;
            context.Set<EventContactShareConsent>().Add(consent);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var orderExists = await verifyContext.RegistrationOrders
            .IgnoreQueryFilters()
            .AnyAsync(order => order.Id == orderId && !order.IsDeleted);
        var registrationExists = await verifyContext.Set<EventRegistration>()
            .IgnoreQueryFilters()
            .AnyAsync(registration => registration.Id == registrationId && !registration.IsDeleted);
        var consentExists = await verifyContext.Set<EventContactShareConsent>()
            .AnyAsync(consent => consent.Id == consentId && consent.Status == ConsentStatus.Granted);

        await Assert.That(orderExists).IsTrue();
        await Assert.That(registrationExists).IsTrue();
        await Assert.That(consentExists).IsTrue();
    }

    [Test]
    public async Task SeedAsync_InDevelopment_SeedsSmtpSettingsFromConfiguration()
    {
        await fixture.ResetAsync();

        var environmentKeys = new[]
        {
            "MAIL_SMTP_HOST",
            "MAIL_SMTP_PORT",
            "MAIL_SMTP_ENCRYPTION",
            "MAIL_SMTP_FROM_ADDRESS",
            "MAIL_SMTP_FROM_NAME",
            "SMTP_HOST",
            "SMTP_PORT",
            "SMTP_SECURITY",
            "SMTP_FROM_ADDRESS",
            "SMTP_FROM_NAME"
        };
        var originalEnvironment = environmentKeys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MAIL_SMTP_HOST"] = "mailpit",
                ["MAIL_SMTP_PORT"] = "1025",
                ["MAIL_SMTP_ENCRYPTION"] = "None",
                ["MAIL_SMTP_FROM_ADDRESS"] = "noreply@localhost",
                ["MAIL_SMTP_FROM_NAME"] = "ISLAMU Event Dev"
            })
            .Build();

        try
        {
            foreach (var key in environmentKeys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            await using (var resetContext = fixture.CreateDbContext())
            {
                var smtpSettingKeys = new[]
                {
                    GovernanceSettingKeys.Email.SmtpHost,
                    GovernanceSettingKeys.Email.SmtpPort,
                    GovernanceSettingKeys.Email.SmtpSecurity,
                    GovernanceSettingKeys.Email.FromAddress,
                    GovernanceSettingKeys.Email.FromName
                };
                var smtpSettings = await resetContext.Set<SystemSetting>()
                    .Where(setting => smtpSettingKeys.Contains(setting.SettingKey))
                    .ToListAsync();

                foreach (var setting in smtpSettings)
                {
                    setting.Value = setting.SettingKey == GovernanceSettingKeys.Email.SmtpPort
                        ? "587"
                        : "\"\"";
                }

                await resetContext.SaveChangesAsync();
            }

            await using (var context = fixture.CreateDbContext())
            {
                await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment, configuration: configuration);
            }

            await using var verifyContext = fixture.CreateDbContext();
            var settings = await verifyContext.Set<SystemSetting>()
                .Where(setting => new[]
                {
                    GovernanceSettingKeys.Email.SmtpHost,
                    GovernanceSettingKeys.Email.SmtpPort,
                    GovernanceSettingKeys.Email.SmtpSecurity,
                    GovernanceSettingKeys.Email.FromAddress,
                    GovernanceSettingKeys.Email.FromName
                }.Contains(setting.SettingKey))
                .ToDictionaryAsync(setting => setting.SettingKey, setting => setting.Value);

            await Assert.That(settings[GovernanceSettingKeys.Email.SmtpHost]).IsEqualTo("\"mailpit\"");
            await Assert.That(settings[GovernanceSettingKeys.Email.SmtpPort]).IsEqualTo("1025");
            await Assert.That(settings[GovernanceSettingKeys.Email.SmtpSecurity]).IsEqualTo("\"None\"");
            await Assert.That(settings[GovernanceSettingKeys.Email.FromAddress]).IsEqualTo("\"noreply@localhost\"");
            await Assert.That(settings[GovernanceSettingKeys.Email.FromName]).IsEqualTo("\"ISLAMU Event Dev\"");
        }
        finally
        {
            foreach (var (key, value) in originalEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [Test]
    public async Task SeedAsync_InDevelopment_SeedsSvixSecretBindingsFromEnvironment()
    {
        await fixture.ResetAsync();

        var environmentKeys = new[]
        {
            "WEBHOOKS_SVIX_AUTH_TOKEN",
            "WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET"
        };
        var originalEnvironment = environmentKeys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable("WEBHOOKS_SVIX_AUTH_TOKEN", "local-test-svix-token");
            Environment.SetEnvironmentVariable(
                "WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET",
                "whsec_local_test_operational_secret");

            await using (var context = fixture.CreateDbContext())
            {
                await DatabaseSeeder.SeedAsync(context, DevelopmentEnvironment);
            }

            await using var verifyContext = fixture.CreateDbContext();
            var bindings = await verifyContext.Set<SecretBinding>()
                .Where(binding => new[]
                {
                    SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
                    SecretDefinitionRegistry.Keys.Webhooks.SvixOperationalWebhookSecret
                }.Contains(binding.SettingKey))
                .ToDictionaryAsync(binding => binding.SettingKey);

            await Assert.That(bindings.Count).IsEqualTo(2);
            await Assert.That(bindings[SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken].SourceType)
                .IsEqualTo(SecretSourceType.EnvironmentVariable);
            await Assert.That(bindings[SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken].EnvironmentVariableName)
                .IsEqualTo("WEBHOOKS_SVIX_AUTH_TOKEN");
            await Assert.That(bindings[SecretDefinitionRegistry.Keys.Webhooks.SvixOperationalWebhookSecret].SourceType)
                .IsEqualTo(SecretSourceType.EnvironmentVariable);
            await Assert.That(bindings[SecretDefinitionRegistry.Keys.Webhooks.SvixOperationalWebhookSecret].EnvironmentVariableName)
                .IsEqualTo("WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET");
        }
        finally
        {
            foreach (var (key, value) in originalEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Event.Persistence.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
