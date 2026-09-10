using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Privacy;

[Category("EventLocationPrivacy")]
[ClassDataSource<ExternalDatabasePrivacyErasurePostgreSqlFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class UserLocationPrivacyErasureRepositoryProviderMetadataTests(
    ExternalDatabasePrivacyErasurePostgreSqlFixture fixture)
{
    [Test]
    public async Task ProviderBackedLocalMetadata_ErasesExactSubjectAcrossTenantsWithoutTouchingUnrelatedRows()
    {
        await using var seedContext = fixture.CreateDbContext();
        await LookupTableSeeder.SeedAsync(seedContext, CancellationToken.None);
        var tenantA = CreateTenant("provider-metadata-a");
        var tenantB = CreateTenant("provider-metadata-b");
        var owner = CreateUser("provider-metadata-owner");
        var unrelated = CreateUser("provider-metadata-unrelated");
        seedContext.AddRange(tenantA, tenantB, owner, unrelated);
        var ownerTenantUserA = CreateTenantUser(tenantA, owner);
        var ownerTenantUserB = CreateTenantUser(tenantB, owner);
        var unrelatedTenantUser = CreateTenantUser(tenantA, unrelated);
        seedContext.TenantUsers.AddRange(ownerTenantUserA, ownerTenantUserB, unrelatedTenantUser);

        var ownerActor = CreateUserActor(owner);
        var unrelatedActor = CreateUserActor(unrelated);
        seedContext.Actors.AddRange(ownerActor, unrelatedActor);

        var ownerExternalLoginA = CreateExternalLogin(tenantA, owner, "keycloak", "kc-owner-a");
        var ownerExternalLoginB = CreateExternalLogin(tenantB, owner, "keycloak", "kc-owner-b");
        var unrelatedExternalLogin = CreateExternalLogin(tenantA, unrelated, "keycloak", "kc-unrelated");
        seedContext.UserExternalLogins.AddRange(ownerExternalLoginA, ownerExternalLoginB, unrelatedExternalLogin);

        var ownerPushSubscriptionA = WebPushSubscription.Create(
            tenantA.Id,
            owner.Id,
            "device-owner-a",
            "https://push.example.invalid/owner-a",
            "p256dh-owner-a",
            "auth-owner-a",
            null,
            DateTime.UtcNow);
        var ownerPushSubscriptionB = WebPushSubscription.Create(
            tenantB.Id,
            owner.Id,
            "device-owner-b",
            "https://push.example.invalid/owner-b",
            "p256dh-owner-b",
            "auth-owner-b",
            null,
            DateTime.UtcNow);
        var unrelatedPushSubscription = WebPushSubscription.Create(
            tenantA.Id,
            unrelated.Id,
            "device-unrelated",
            "https://push.example.invalid/unrelated",
            "p256dh-unrelated",
            "auth-unrelated",
            null,
            DateTime.UtcNow);
        seedContext.WebPushSubscriptions.AddRange(ownerPushSubscriptionA, ownerPushSubscriptionB, unrelatedPushSubscription);

        var notificationCategory = await seedContext.NotificationPreferenceCategories
            .SingleAsync(category => category.MasterCode == NotificationPreferenceCategoryCodes.AccountSecurity);
        seedContext.WebPushDispatchOutbox.AddRange(
            CreateWebPushDispatch(tenantA, owner, ownerPushSubscriptionA, notificationCategory, "{\"owner\":1}"),
            CreateWebPushDispatch(tenantB, owner, ownerPushSubscriptionB, notificationCategory, "{\"owner\":2}"),
            CreateWebPushDispatch(tenantA, unrelated, unrelatedPushSubscription, notificationCategory, "{\"unrelated\":1}"));

        var notificationCategoryLookup = await seedContext.NotificationCategories
            .SingleAsync(category => category.Id == (int)NotificationCategoryEnum.IdentityLifecycle);
        var notificationOwnershipType = await seedContext.NotificationOwnershipTypes
            .SingleAsync(type => type.MasterCode == "ACCOUNT_AUTHORITY");
        var notificationRecipientKind = await seedContext.NotificationRecipientKinds
            .SingleAsync(kind => kind.MasterCode == "USER");
        var notificationIntentStatus = await seedContext.NotificationIntentStatuses
            .SingleAsync(status => status.MasterCode == "PENDING");
        var ownerNotificationIntentA = CreateNotificationIntent(
            tenantA,
            owner,
            notificationCategoryLookup,
            notificationOwnershipType,
            notificationRecipientKind,
            notificationIntentStatus,
            "account-security-owner-a");
        var ownerNotificationIntentB = CreateNotificationIntent(
            tenantB,
            owner,
            notificationCategoryLookup,
            notificationOwnershipType,
            notificationRecipientKind,
            notificationIntentStatus,
            "account-security-owner-b");
        var unrelatedNotificationIntent = CreateNotificationIntent(
            tenantA,
            unrelated,
            notificationCategoryLookup,
            notificationOwnershipType,
            notificationRecipientKind,
            notificationIntentStatus,
            "account-security-unrelated");
        seedContext.NotificationIntents.AddRange(
            ownerNotificationIntentA,
            ownerNotificationIntentB,
            unrelatedNotificationIntent);
        EmailDispatchOutbox ownerEmailWithoutLocator = CreateEmailDispatch(
            tenantA,
            owner,
            ownerNotificationIntentA,
            "owner-a@example.invalid",
            "Owner A subject",
            "owner body A");
        ownerEmailWithoutLocator.ProviderMessageId = null;
        seedContext.EmailDispatchOutbox.AddRange(
            ownerEmailWithoutLocator,
            CreateEmailDispatch(tenantB, owner, ownerNotificationIntentB, "owner-b@example.invalid", "Owner B subject", "owner body B"),
            CreateEmailDispatch(tenantA, unrelated, unrelatedNotificationIntent, "unrelated@example.invalid", "Unrelated subject", "Unrelated body"));
        IntegrationSyncOutbox unrelatedIntegrationSyncSeed = CreateIntegrationSync(
            tenantA,
            unrelated,
            "unrelated@example.invalid",
            "Unrelated",
            "{\"email\":\"unrelated@example.invalid\"}");
        seedContext.IntegrationSyncOutbox.AddRange(
            CreateIntegrationSync(tenantA, owner, "owner-a@example.invalid", "Owner A", "{\"email\":\"owner-a@example.invalid\"}"),
            CreateIntegrationSync(tenantB, owner, "owner-b@example.invalid", "Owner B", "{\"email\":\"owner-b@example.invalid\"}"),
            unrelatedIntegrationSyncSeed);

        var fileType = await seedContext.FileTypes.FirstAsync();
        var ownerStorageA = CreateStorageObject(tenantA, ownerActor, fileType, null, "Owner A", "Owner A");
        var ownerStorageB = CreateStorageObject(tenantB, ownerActor, fileType, "tenants/b/owner.png", "Owner B", "Owner B");
        var unrelatedStorage = CreateStorageObject(tenantA, unrelatedActor, fileType, "tenants/a/unrelated.png", "Unrelated", "Unrelated");
        seedContext.StorageObjects.AddRange(ownerStorageA, ownerStorageB, unrelatedStorage);
        var ownerUpload = CreateStorageUploadSession(tenantA, owner, "tenants/a/uploads/owner.txt");
        var unrelatedUpload = CreateStorageUploadSession(tenantA, unrelated, "tenants/a/uploads/unrelated.txt");
        seedContext.StorageUploadSessions.AddRange(ownerUpload, unrelatedUpload);
        var storageUsageCounter = new StorageUsageCounter
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantA.Id,
            Tenant = tenantA,
            Provider = StorageProviders.Local,
            ReservedBytes = ownerUpload.ReservedBytes + unrelatedUpload.ReservedBytes,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        seedContext.StorageUsageCounters.Add(storageUsageCounter);

        var webhookConsumerKind = await seedContext.WebhookConsumerKinds.SingleAsync(kind => kind.MasterCode == "USER");
        var webhookConsumerStatus = await seedContext.WebhookConsumerStatuses.SingleAsync(status => status.MasterCode == "ACTIVE");
        var webhookProviderMode = await seedContext.WebhookProviderModes.SingleAsync(mode => mode.MasterCode == "SVIX");
        var webhookEndpointStatus = await seedContext.WebhookEndpointStatuses.SingleAsync(status => status.MasterCode == "ACTIVE");
        var ownerConsumerA = CreateWebhookConsumer(tenantA, owner, webhookConsumerKind, webhookConsumerStatus, webhookProviderMode, "svix-owner-a");
        var ownerConsumerB = CreateWebhookConsumer(tenantB, owner, webhookConsumerKind, webhookConsumerStatus, webhookProviderMode, "svix-owner-b");
        var unrelatedConsumer = CreateWebhookConsumer(tenantA, unrelated, webhookConsumerKind, webhookConsumerStatus, webhookProviderMode, "svix-unrelated");
        seedContext.WebhookConsumers.AddRange(ownerConsumerA, ownerConsumerB, unrelatedConsumer);
        var ownerEndpointA = CreateWebhookEndpoint(tenantA, ownerConsumerA, webhookEndpointStatus, "https://hooks.example.invalid/owner-a", null);
        var ownerEndpointB = CreateWebhookEndpoint(tenantB, ownerConsumerB, webhookEndpointStatus, "https://hooks.example.invalid/owner-b", "svix-endpoint-owner-b");
        var unrelatedEndpointSeed = CreateWebhookEndpoint(tenantA, unrelatedConsumer, webhookEndpointStatus, "https://hooks.example.invalid/unrelated", "svix-endpoint-unrelated");
        seedContext.WebhookEndpoints.AddRange(ownerEndpointA, ownerEndpointB, unrelatedEndpointSeed);

        await seedContext.SaveChangesAsync();
        string unrelatedIntegrationPayloadBefore = await seedContext.IntegrationSyncOutbox
            .AsNoTracking()
            .Where(row => row.Id == unrelatedIntegrationSyncSeed.Id)
            .Select(row => row.SubscriberPayloadJson)
            .SingleAsync();

        await using var runtimeContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));
        var repository = new UserLocationPrivacyErasureRepository(runtimeContext);
        await using var transaction = await runtimeContext.Database.BeginTransactionAsync();
        IReadOnlyList<PrivacyErasureProviderCandidate> candidates = await repository.GetProviderCandidatesAsync(owner.Id, CancellationToken.None);
        await Assert.That(candidates.Any(candidate => candidate.ProviderKind == PrivacyErasureProviderKind.Keycloak)).IsTrue();
        await Assert.That(candidates.Any(candidate => candidate.ProviderKind == PrivacyErasureProviderKind.Smtp)).IsTrue();
        await Assert.That(candidates.Any(candidate => candidate.ProviderKind == PrivacyErasureProviderKind.WebPush)).IsTrue();
        await Assert.That(candidates.Any(candidate => candidate.ProviderKind == PrivacyErasureProviderKind.ObjectStorage)).IsTrue();
        await Assert.That(candidates.Any(candidate =>
            candidate.ProviderKind == PrivacyErasureProviderKind.ObjectStorage
            && candidate.TargetId == ownerUpload.Id
            && candidate.Locator == "tenants/a/uploads/owner.txt")).IsTrue();
        await Assert.That(candidates.Any(candidate => candidate.ProviderKind == PrivacyErasureProviderKind.Webhook)).IsTrue();
        await Assert.That(candidates.Any(candidate => candidate.ProviderKind == PrivacyErasureProviderKind.Osprey || candidate.ProviderKind == PrivacyErasureProviderKind.Coop)).IsFalse();
        await repository.EraseProviderBackedLocalUserMetadataAsync(owner.Id, CancellationToken.None);
        await repository.AnonymizeRetainedAuditEvidenceAsync(owner.Id, CancellationToken.None);
        await repository.EraseRegistrationAndLocalNotificationsAsync(owner.Id, CancellationToken.None);
        await repository.EraseMembershipsAndPreferencesAsync(owner.Id, CancellationToken.None);
        await Assert.That(await runtimeContext.UserExternalLogins
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .CountAsync(login => login.UserId == owner.Id)).IsEqualTo(0);
        await Assert.That(await runtimeContext.UserExternalLogins
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .CountAsync(login => login.UserId == unrelated.Id)).IsEqualTo(1);

        int ownerSubscriptionCount = await runtimeContext.WebPushSubscriptions
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .CountAsync(subscription => subscription.UserId == owner.Id);
        WebPushSubscription unrelatedSubscription = await runtimeContext.WebPushSubscriptions
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(subscription => subscription.UserId == unrelated.Id);
        await Assert.That(ownerSubscriptionCount).IsEqualTo(0);
        await Assert.That(unrelatedSubscription.Endpoint).IsEqualTo("https://push.example.invalid/unrelated");
        await Assert.That(unrelatedSubscription.P256Dh).IsEqualTo("p256dh-unrelated");
        await Assert.That(unrelatedSubscription.AuthSecret).IsEqualTo("auth-unrelated");

        await Assert.That(await runtimeContext.WebPushDispatchOutbox
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .CountAsync(row => row.UserId == owner.Id)).IsEqualTo(0);
        await Assert.That(await runtimeContext.WebPushDispatchOutbox
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .CountAsync(row => row.UserId == unrelated.Id)).IsEqualTo(1);

        int ownerEmailCount = await runtimeContext.EmailDispatchOutbox
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .CountAsync(row => row.RecipientUserId == owner.Id);
        EmailDispatchOutbox unrelatedEmail = await runtimeContext.EmailDispatchOutbox
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.RecipientUserId == unrelated.Id);
        await Assert.That(ownerEmailCount).IsEqualTo(0);
        await Assert.That(unrelatedEmail.ProviderMessageId).IsNotNull();
        await Assert.That(unrelatedEmail.RecipientEmail).IsEqualTo("unrelated@example.invalid");

        await Assert.That(await runtimeContext.IntegrationSyncOutbox
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .CountAsync(row => row.UserId == owner.Id)).IsEqualTo(0);
        IntegrationSyncOutbox unrelatedIntegrationSync = await runtimeContext.IntegrationSyncOutbox
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.UserId == unrelated.Id);
        await Assert.That(unrelatedIntegrationSync.SubscriberEmail).IsEqualTo("unrelated@example.invalid");
        await Assert.That(unrelatedIntegrationSync.SubscriberName).IsEqualTo("Unrelated");
        await Assert.That(unrelatedIntegrationSync.SubscriberPayloadJson).IsEqualTo(unrelatedIntegrationPayloadBefore);

        StorageObject[] ownerObjects = await runtimeContext.StorageObjects
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.Id == ownerStorageA.Id || row.Id == ownerStorageB.Id)
            .OrderBy(row => row.TenantId)
            .ToArrayAsync();
        StorageObject unrelatedObject = await runtimeContext.StorageObjects
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.Id == unrelatedStorage.Id);
        await Assert.That(ownerObjects.Length).IsEqualTo(2);
        await Assert.That(ownerObjects.All(row =>
            row.ObjectKey is null
            && row.LifecycleState == StorageObjectLifecycleStates.Deleted
            && row.IsDeleted
            && row.Uri == string.Empty
            && row.FullName == string.Empty
            && row.SafeDisplayName == string.Empty
            && row.Provider == StorageProviders.Local)).IsTrue();
        await Assert.That(unrelatedObject.ObjectKey).IsEqualTo("tenants/a/unrelated.png");
        await Assert.That(unrelatedObject.Provider).IsEqualTo("s3_compatible");

        StorageUploadSession ownerUploadRow = await runtimeContext.StorageUploadSessions
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.Id == ownerUpload.Id);
        StorageUploadSession unrelatedUploadRow = await runtimeContext.StorageUploadSessions
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.Id == unrelatedUpload.Id);
        await Assert.That(ownerUploadRow.UserId).IsNull();
        await Assert.That(ownerUploadRow.ObjectKey).IsNull();
        await Assert.That(ownerUploadRow.ReservedBytes).IsEqualTo(0);
        await Assert.That(ownerUploadRow.SafeDisplayName).IsEmpty();
        await Assert.That(ownerUploadRow.Status).IsEqualTo(StorageUploadSessionStates.Uploading);
        await Assert.That(ownerUploadRow.CreatedBy).IsNull();
        await Assert.That(ownerUploadRow.UpdatedBy).IsNull();
        await Assert.That(unrelatedUploadRow.UserId).IsEqualTo(unrelated.Id);
        await Assert.That(unrelatedUploadRow.ObjectKey).IsEqualTo("tenants/a/uploads/unrelated.txt");
        await Assert.That(unrelatedUploadRow.ReservedBytes).IsEqualTo(8);
        StorageUsageCounter storageUsageCounterRow = await runtimeContext.StorageUsageCounters
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.Id == storageUsageCounter.Id);
        await Assert.That(storageUsageCounterRow.ReservedBytes).IsEqualTo(8);

        WebhookConsumer[] ownerConsumers = await runtimeContext.WebhookConsumers
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.Id == ownerConsumerA.Id || row.Id == ownerConsumerB.Id)
            .OrderBy(row => row.TenantId)
            .ToArrayAsync();
        WebhookConsumer unrelatedConsumerRow = await runtimeContext.WebhookConsumers
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.Id == unrelatedConsumer.Id);
        await Assert.That(ownerConsumers.Length).IsEqualTo(2);
        await Assert.That(ownerConsumers.All(row =>
            row.OwnerUserId is null
            && row.Name == $"Deleted user {row.Id:N}"
            && row.ConsumerKindId == (int)WebhookConsumerKind.Tenant
            && row.StatusId == (int)WebhookConsumerStatus.Archived
            && row.ExternalProviderAppId is null
            && row.ProviderModeId == (int)WebhookProviderMode.Local)).IsTrue();
        await Assert.That(unrelatedConsumerRow.ExternalProviderAppId).IsEqualTo("svix-unrelated");

        WebhookEndpoint[] ownerEndpoints = await runtimeContext.WebhookEndpoints
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.Id == ownerEndpointA.Id || row.Id == ownerEndpointB.Id)
            .OrderBy(row => row.TenantId)
            .ToArrayAsync();
        WebhookEndpoint unrelatedEndpoint = await runtimeContext.WebhookEndpoints
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(row => row.Consumer != null && row.Consumer.OwnerUserId == unrelated.Id);
        await Assert.That(ownerEndpoints.Length).IsEqualTo(2);
        await Assert.That(ownerEndpoints.All(row =>
            row.ProviderEndpointId is null
            && row.Url == string.Empty
            && row.SecretRef == string.Empty
            && row.StatusId == (int)WebhookEndpointStatus.Archived)).IsTrue();
        await Assert.That(unrelatedEndpoint.ProviderEndpointId).IsEqualTo("svix-endpoint-unrelated");
        await Assert.That(unrelatedEndpoint.Url).IsEqualTo("https://hooks.example.invalid/unrelated");

        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ProviderBackedLocalMetadata_CapturesDistinctProviderWorkBeforeExactCrossTenantClearing()
    {
        Guid intentId = Guid.CreateVersion7();
        Guid ownerId;
        Guid unrelatedId;
        Guid ownerActorId;
        Guid unrelatedActorId;
        Guid ownerIdentityId;
        Guid unrelatedIdentityId;
        Guid ownerOspreyLinkId;
        Guid ownerCoopLinkId;
        Guid unrelatedLinkId;
        Guid ownerEndpointAId;
        Guid ownerEndpointBId;
        Guid unrelatedEndpointId;
        Guid ownerTargetAId;
        Guid ownerTargetBId;
        Guid unrelatedTargetId;

        await using (var seedContext = fixture.CreateDbContext())
        {
            await LookupTableSeeder.SeedAsync(seedContext, CancellationToken.None);
            var tenantA = CreateTenant("provider-clearing-a");
            var tenantB = CreateTenant("provider-clearing-b");
            var owner = CreateUser("provider-clearing-owner");
            var unrelated = CreateUser("provider-clearing-unrelated");
            ownerId = owner.Id;
            unrelatedId = unrelated.Id;
            seedContext.AddRange(tenantA, tenantB, owner, unrelated);
            seedContext.TenantUsers.AddRange(
                CreateTenantUser(tenantA, owner),
                CreateTenantUser(tenantB, owner),
                CreateTenantUser(tenantA, unrelated));

            var ownerActor = CreateUserActor(owner);
            var unrelatedActor = CreateUserActor(unrelated);
            ownerActor.CreatedBy = owner.Id;
            ownerActor.UpdatedBy = owner.Id;
            ownerActor.DeletedBy = owner.Id;
            AtprotoIdentity seededOwnerIdentity = ownerActor.AtprotoIdentities.Single();
            seededOwnerIdentity.CreatedBy = owner.Id;
            seededOwnerIdentity.UpdatedBy = owner.Id;
            seededOwnerIdentity.DeletedBy = owner.Id;
            unrelatedActor.CreatedBy = unrelated.Id;
            unrelatedActor.UpdatedBy = unrelated.Id;
            unrelatedActor.DeletedBy = unrelated.Id;
            AtprotoIdentity seededUnrelatedIdentity = unrelatedActor.AtprotoIdentities.Single();
            seededUnrelatedIdentity.CreatedBy = unrelated.Id;
            seededUnrelatedIdentity.UpdatedBy = unrelated.Id;
            seededUnrelatedIdentity.DeletedBy = unrelated.Id;
            ownerActorId = ownerActor.Id;
            unrelatedActorId = unrelatedActor.Id;
            ownerIdentityId = seededOwnerIdentity.Id;
            unrelatedIdentityId = seededUnrelatedIdentity.Id;
            seedContext.Actors.AddRange(ownerActor, unrelatedActor);

            int eventFormatId = await seedContext.Set<EventFormat>().Select(format => format.Id).FirstAsync();
            int eventStatusId = await seedContext.Set<EventStatus>().Select(status => status.Id).FirstAsync();
            int visibilityTypeId = await seedContext.Set<VisibilityType>().Select(visibility => visibility.Id).FirstAsync();
            int eventProvenanceTypeId = await seedContext.Set<EventProvenanceType>()
                .Select(provenance => provenance.Id)
                .FirstAsync();
            var ownerEventA = CreateEvent(
                tenantA,
                ownerActor,
                eventFormatId,
                eventStatusId,
                visibilityTypeId,
                eventProvenanceTypeId,
                "provider-clearing-owner-a");
            var ownerEventB = CreateEvent(
                tenantB,
                ownerActor,
                eventFormatId,
                eventStatusId,
                visibilityTypeId,
                eventProvenanceTypeId,
                "provider-clearing-owner-b");
            var unrelatedEvent = CreateEvent(
                tenantA,
                unrelatedActor,
                eventFormatId,
                eventStatusId,
                visibilityTypeId,
                eventProvenanceTypeId,
                "provider-clearing-unrelated");
            seedContext.Events.AddRange(ownerEventA, ownerEventB, unrelatedEvent);

            EventReport ownerReportA = CreateReport(tenantA, ownerEventA, owner, ownerActor);
            EventReport ownerReportB = CreateReport(tenantB, ownerEventB, owner, ownerActor);
            EventReport unrelatedReport = CreateReport(tenantA, unrelatedEvent, unrelated, unrelatedActor);
            seedContext.EventReports.AddRange(ownerReportA, ownerReportB, unrelatedReport);

            EventReportExternalLink ownerOspreyLink = CreateExternalReportLink(
                tenantA,
                ownerReportA,
                EventReportExternalProvider.Osprey,
                "owner-osprey-case",
                "owner-osprey-signal");
            EventReportExternalLink ownerCoopLink = CreateExternalReportLink(
                tenantB,
                ownerReportB,
                EventReportExternalProvider.Coop,
                null,
                "owner-coop-signal");
            EventReportExternalLink unrelatedLink = CreateExternalReportLink(
                tenantA,
                unrelatedReport,
                EventReportExternalProvider.Osprey,
                "unrelated-osprey-case",
                null);
            ownerOspreyLinkId = ownerOspreyLink.Id;
            ownerCoopLinkId = ownerCoopLink.Id;
            unrelatedLinkId = unrelatedLink.Id;
            seedContext.EventReportExternalLinks.AddRange(ownerOspreyLink, ownerCoopLink, unrelatedLink);

            WebhookConsumerKindLookup webhookConsumerKind = await seedContext.WebhookConsumerKinds
                .SingleAsync(kind => kind.MasterCode == "USER");
            WebhookConsumerStatusLookup webhookConsumerStatus = await seedContext.WebhookConsumerStatuses
                .SingleAsync(status => status.MasterCode == "ACTIVE");
            WebhookProviderModeLookup webhookProviderMode = await seedContext.WebhookProviderModes
                .SingleAsync(mode => mode.MasterCode == "COMPOSITE");
            WebhookEndpointStatusLookup webhookEndpointStatus = await seedContext.WebhookEndpointStatuses
                .SingleAsync(status => status.MasterCode == "ACTIVE");
            var ownerConsumerA = CreateWebhookConsumer(
                tenantA,
                owner,
                webhookConsumerKind,
                webhookConsumerStatus,
                webhookProviderMode,
                "provider-owner-a");
            var ownerConsumerB = CreateWebhookConsumer(
                tenantB,
                owner,
                webhookConsumerKind,
                webhookConsumerStatus,
                webhookProviderMode,
                "provider-owner-b");
            var unrelatedConsumer = CreateWebhookConsumer(
                tenantA,
                unrelated,
                webhookConsumerKind,
                webhookConsumerStatus,
                webhookProviderMode,
                "provider-unrelated");
            seedContext.WebhookConsumers.AddRange(ownerConsumerA, ownerConsumerB, unrelatedConsumer);

            WebhookEndpoint ownerEndpointA = CreateWebhookEndpoint(
                tenantA,
                ownerConsumerA,
                webhookEndpointStatus,
                "https://hooks.example.invalid/provider-owner-a",
                "endpoint-owner-a");
            WebhookEndpoint ownerEndpointB = CreateWebhookEndpoint(
                tenantB,
                ownerConsumerB,
                webhookEndpointStatus,
                "https://hooks.example.invalid/provider-owner-b",
                "endpoint-owner-b");
            WebhookEndpoint unrelatedEndpoint = CreateWebhookEndpoint(
                tenantA,
                unrelatedConsumer,
                webhookEndpointStatus,
                "https://hooks.example.invalid/provider-unrelated",
                "endpoint-unrelated");
            ownerEndpointAId = ownerEndpointA.Id;
            ownerEndpointBId = ownerEndpointB.Id;
            unrelatedEndpointId = unrelatedEndpoint.Id;
            seedContext.WebhookEndpoints.AddRange(ownerEndpointA, ownerEndpointB, unrelatedEndpoint);

            WebhookTargetGraph ownerTargetA = CreateWebhookTargetGraph(tenantA, ownerConsumerA, ownerEndpointA);
            WebhookTargetGraph ownerTargetB = CreateWebhookTargetGraph(tenantB, ownerConsumerB, ownerEndpointB);
            WebhookTargetGraph unrelatedTarget = CreateWebhookTargetGraph(tenantA, unrelatedConsumer, unrelatedEndpoint);
            ownerTargetAId = ownerTargetA.Target.Id;
            ownerTargetBId = ownerTargetB.Target.Id;
            unrelatedTargetId = unrelatedTarget.Target.Id;
            seedContext.AddRange(
                ownerTargetA.Message,
                ownerTargetA.Plan,
                ownerTargetA.Target,
                ownerTargetB.Message,
                ownerTargetB.Plan,
                ownerTargetB.Target,
                unrelatedTarget.Message,
                unrelatedTarget.Plan,
                unrelatedTarget.Target);
            await seedContext.SaveChangesAsync();
        }

        await using (var candidateContext = fixture.CreateTenantFilteredDbContext())
        {
            var repository = new UserLocationPrivacyErasureRepository(candidateContext);
            IReadOnlyList<PrivacyErasureProviderCandidate> candidates =
                await repository.GetProviderCandidatesAsync(ownerId, CancellationToken.None);
            PrivacyErasureProviderCandidate[] reportCandidates = candidates
                .Where(candidate => candidate.ProviderKind is
                    PrivacyErasureProviderKind.Osprey or PrivacyErasureProviderKind.Coop)
                .ToArray();

            await Assert.That(candidates.Count).IsEqualTo(5);
            await Assert.That(candidates.Select(candidate => (
                    candidate.ProviderKind,
                    candidate.Action,
                    candidate.TenantId,
                    candidate.TargetId)).Distinct().Count())
                .IsEqualTo(candidates.Count);
            await Assert.That(reportCandidates.Length).IsEqualTo(2);
            await Assert.That(reportCandidates.Single(candidate =>
                    candidate.TargetId == ownerOspreyLinkId).Locator)
                .IsEqualTo("owner-osprey-case");
            await Assert.That(reportCandidates.Single(candidate =>
                    candidate.TargetId == ownerCoopLinkId).Locator)
                .IsEqualTo("owner-coop-signal");
        }

        var authority = new RecordingPrivacyErasureAuthority();
        await using (var runtimeContext = fixture.CreateTenantFilteredDbContext())
        await using (GlobalLocationPrivacyErasureTests.ErasureRuntime runtime =
            GlobalLocationPrivacyErasureTests.CreateRuntime(
                runtimeContext,
                authority))
        {
            runtimeContext.CurrentUserService = new TestCurrentUserService(ownerId);
            await runtime.Service.EraseUserAsync(ownerId, intentId, CancellationToken.None);
            await runtime.ReplayService.ReplayAsync(CancellationToken.None);
        }

        await using var verifyContext = fixture.CreateDbContext();
        PrivacyErasureProviderWork[] providerWork = await verifyContext.PrivacyErasureProviderWork
            .AsNoTracking()
            .Where(work => work.IntentId == intentId)
            .OrderBy(work => work.ProviderKind)
            .ToArrayAsync();
        await Assert.That(providerWork.Length).IsEqualTo(5);
        await Assert.That(providerWork.All(work =>
            work.ProtectedLocator is { Length: > 0 }
            && work.Status == PrivacyErasureProviderWorkStatus.Pending)).IsTrue();
        await Assert.That(providerWork.Select(work => (
                work.ProviderKind,
                work.Action,
                work.TenantId,
                work.TargetId)).Distinct().Count())
            .IsEqualTo(providerWork.Length);
        await Assert.That(providerWork.Select(work => work.TargetId))
            .IsEquivalentTo(new Guid?[]
            {
                ownerIdentityId,
                ownerOspreyLinkId,
                ownerCoopLinkId,
                ownerEndpointAId,
                ownerEndpointBId
            });
        await Assert.That(providerWork.Select(work => work.TargetId))
            .DoesNotContain(unrelatedIdentityId);
        await Assert.That(providerWork.Select(work => work.TargetId))
            .DoesNotContain(unrelatedLinkId);
        await Assert.That(providerWork.Select(work => work.TargetId))
            .DoesNotContain(unrelatedEndpointId);

        AtprotoIdentity ownerIdentity = await verifyContext.AtprotoIdentities
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(identity => identity.Id == ownerIdentityId);
        AtprotoIdentity unrelatedIdentity = await verifyContext.AtprotoIdentities
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(identity => identity.Id == unrelatedIdentityId);
        Actor ownerActorRow = await verifyContext.Actors
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(actor => actor.Id == ownerActorId);
        Actor unrelatedActorRow = await verifyContext.Actors
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(actor => actor.Id == unrelatedActorId);
        await Assert.That(ownerActorRow.UserId).IsNull();
        await Assert.That(ownerActorRow.IsDeleted).IsTrue();
        await Assert.That(ownerActorRow.DeletedAt).IsNotNull();
        await Assert.That(ownerActorRow.CreatedBy).IsNull();
        await Assert.That(ownerActorRow.UpdatedBy).IsNull();
        await Assert.That(ownerActorRow.DeletedBy).IsNull();
        await Assert.That(ownerIdentity.Did).IsEqualTo($"did:deleted:{ownerIdentityId:N}");
        await Assert.That(ownerIdentity.Handle).IsNull();
        await Assert.That(ownerIdentity.PdsHost).IsEmpty();
        await Assert.That(ownerIdentity.IsDeleted).IsTrue();
        await Assert.That(ownerIdentity.CreatedBy).IsNull();
        await Assert.That(ownerIdentity.UpdatedBy).IsNull();
        await Assert.That(ownerIdentity.DeletedBy).IsNull();
        await Assert.That(unrelatedIdentity.Handle).IsNotNull();
        await Assert.That(unrelatedIdentity.PdsHost).IsNotEmpty();
        await Assert.That(unrelatedIdentity.IsDeleted).IsFalse();
        await Assert.That(unrelatedActorRow.CreatedBy).IsEqualTo(unrelatedId);
        await Assert.That(unrelatedActorRow.UpdatedBy).IsEqualTo(unrelatedId);
        await Assert.That(unrelatedActorRow.DeletedBy).IsEqualTo(unrelatedId);
        await Assert.That(unrelatedIdentity.CreatedBy).IsEqualTo(unrelatedId);
        await Assert.That(unrelatedIdentity.UpdatedBy).IsEqualTo(unrelatedId);
        await Assert.That(unrelatedIdentity.DeletedBy).IsEqualTo(unrelatedId);

        EventReportExternalLink[] ownerLinks = await verifyContext.EventReportExternalLinks
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(link => link.Id == ownerOspreyLinkId || link.Id == ownerCoopLinkId)
            .ToArrayAsync();
        EventReportExternalLink unrelatedLinkRow = await verifyContext.EventReportExternalLinks
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(link => link.Id == unrelatedLinkId);
        await Assert.That(ownerLinks).IsEmpty();
        await Assert.That(unrelatedLinkRow.ProviderCaseId).IsNotNull();
        await Assert.That(unrelatedLinkRow.SyncState).IsEqualTo(EventReportSyncState.Synced);

        WebhookLocalTargetSnapshot[] ownerTargets = await verifyContext.WebhookLocalTargetSnapshots
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(target => target.Id == ownerTargetAId || target.Id == ownerTargetBId)
            .ToArrayAsync();
        WebhookLocalTargetSnapshot unrelatedTargetRow = await verifyContext.WebhookLocalTargetSnapshots
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(target => target.Id == unrelatedTargetId);
        await Assert.That(ownerTargets.Length).IsEqualTo(2);
        await Assert.That(ownerTargets.Select(target => target.TenantId).Distinct().Count()).IsEqualTo(2);
        await Assert.That(ownerTargets.All(target =>
            target.DestinationUrl == string.Empty
            && target.CredentialReference == string.Empty)).IsTrue();
        await Assert.That(unrelatedTargetRow.DestinationUrl).IsNotEmpty();
        await Assert.That(unrelatedTargetRow.CredentialReference).IsNotEmpty();
    }

    [Test]
    public async Task ActorOwnershipConstraint_RejectsLiveOwnerlessActor()
    {
        await using var context = fixture.CreateDbContext();
        Actor liveOwnerlessActor = CreateUserActor(CreateUser("live-ownerless-actor"));
        liveOwnerlessActor.UserId = null;
        liveOwnerlessActor.AtprotoIdentities.Clear();
        context.Actors.Add(liveOwnerlessActor);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task ProviderBackedLocalMetadata_RejectsEmptySubjectBeforeDatabaseAccess()
    {
        await using var context = fixture.CreateDbContext();
        var repository = new UserLocationPrivacyErasureRepository(context);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.EraseProviderBackedLocalUserMetadataAsync(Guid.Empty, CancellationToken.None));

        await Assert.That(exception.ParamName).IsEqualTo("subjectId");
    }

    [Test]
    public async Task IntegrationSyncActiveClaim_ReloadsOnlyExactTenantProcessingLease()
    {
        await using var seedContext = fixture.CreateDbContext();
        var tenantA = CreateTenant("integration-sync-claim-a");
        var tenantB = CreateTenant("integration-sync-claim-b");
        var owner = CreateUser("integration-sync-claim-owner");
        seedContext.AddRange(tenantA, tenantB, owner);

        Guid activeLeaseToken = Guid.CreateVersion7();
        DateTime activeStartedAt = DateTime.UtcNow;
        IntegrationSyncOutbox activeRow = CreateIntegrationSync(
            tenantA,
            owner,
            "owner@example.invalid",
            "Owner",
            "{\"email\":\"owner@example.invalid\"}");
        activeRow.Status = IntegrationSyncStatus.Processing;
        activeRow.ProcessingLeaseToken = activeLeaseToken;
        activeRow.ProcessingStartedAt = activeStartedAt;

        IntegrationSyncOutbox pendingRow = CreateIntegrationSync(
            tenantB,
            owner,
            "pending@example.invalid",
            "Pending",
            "{\"email\":\"pending@example.invalid\"}");
        pendingRow.ProcessingLeaseToken = activeLeaseToken;
        seedContext.IntegrationSyncOutbox.AddRange(activeRow, pendingRow);
        await seedContext.SaveChangesAsync();

        await using var runtimeContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));
        var repository = new IntegrationSyncOutboxRepository(runtimeContext);

        IntegrationSyncOutbox? activeClaim = await repository.GetActiveClaimAsync(
            new IntegrationSyncClaimIdentity(tenantA.Id, activeRow.Id, activeLeaseToken, activeStartedAt),
            CancellationToken.None);
        IntegrationSyncOutbox? wrongTenant = await repository.GetActiveClaimAsync(
            new IntegrationSyncClaimIdentity(tenantB.Id, activeRow.Id, activeLeaseToken, activeStartedAt),
            CancellationToken.None);
        IntegrationSyncOutbox? staleLease = await repository.GetActiveClaimAsync(
            new IntegrationSyncClaimIdentity(tenantA.Id, activeRow.Id, Guid.CreateVersion7(), activeStartedAt),
            CancellationToken.None);
        IntegrationSyncOutbox? pendingClaim = await repository.GetActiveClaimAsync(
            new IntegrationSyncClaimIdentity(tenantB.Id, pendingRow.Id, activeLeaseToken, activeStartedAt),
            CancellationToken.None);

        await Assert.That(activeClaim?.Id).IsEqualTo(activeRow.Id);
        await Assert.That(wrongTenant).IsNull();
        await Assert.That(staleLease).IsNull();
        await Assert.That(pendingClaim).IsNull();
    }

    [Test]
    public async Task IntegrationSyncActiveClaim_AfterExactUserErasureReloadHasNoUserAndUnrelatedClaimRemains()
    {
        await using var seedContext = fixture.CreateDbContext();
        var tenantA = CreateTenant("integration-sync-erasure-a");
        var tenantB = CreateTenant("integration-sync-erasure-b");
        var owner = CreateUser("integration-sync-erasure-owner");
        var unrelated = CreateUser("integration-sync-erasure-unrelated");
        seedContext.AddRange(tenantA, tenantB, owner, unrelated);

        Guid ownerLeaseToken = Guid.CreateVersion7();
        DateTime ownerStartedAt = DateTime.UtcNow;
        IntegrationSyncOutbox ownerRow = CreateIntegrationSync(
            tenantA,
            owner,
            "owner-erasure@example.invalid",
            "Owner Erasure",
            "{\"email\":\"owner-erasure@example.invalid\"}");
        ownerRow.Status = IntegrationSyncStatus.Processing;
        ownerRow.ProcessingLeaseToken = ownerLeaseToken;
        ownerRow.ProcessingStartedAt = ownerStartedAt;

        Guid unrelatedLeaseToken = Guid.CreateVersion7();
        DateTime unrelatedStartedAt = ownerStartedAt.AddTicks(1);
        IntegrationSyncOutbox unrelatedRow = CreateIntegrationSync(
            tenantB,
            unrelated,
            "unrelated-erasure@example.invalid",
            "Unrelated Erasure",
            "{\"email\":\"unrelated-erasure@example.invalid\"}");
        unrelatedRow.Status = IntegrationSyncStatus.Processing;
        unrelatedRow.ProcessingLeaseToken = unrelatedLeaseToken;
        unrelatedRow.ProcessingStartedAt = unrelatedStartedAt;
        seedContext.IntegrationSyncOutbox.AddRange(ownerRow, unrelatedRow);
        await seedContext.SaveChangesAsync();

        await using var runtimeContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));
        var outboxRepository = new IntegrationSyncOutboxRepository(runtimeContext);
        var erasureRepository = new UserLocationPrivacyErasureRepository(runtimeContext);

        IntegrationSyncOutbox? claimBeforeErasure = await outboxRepository.GetActiveClaimAsync(
            new IntegrationSyncClaimIdentity(tenantA.Id, ownerRow.Id, ownerLeaseToken, ownerStartedAt),
            CancellationToken.None);
        await erasureRepository.EraseProviderBackedLocalUserMetadataAsync(owner.Id, CancellationToken.None);
        IntegrationSyncOutbox? claimAfterErasure = await outboxRepository.GetActiveClaimAsync(
            new IntegrationSyncClaimIdentity(tenantA.Id, ownerRow.Id, ownerLeaseToken, ownerStartedAt),
            CancellationToken.None);
        IntegrationSyncOutbox? unrelatedClaim = await outboxRepository.GetActiveClaimAsync(
            new IntegrationSyncClaimIdentity(tenantB.Id, unrelatedRow.Id, unrelatedLeaseToken, unrelatedStartedAt),
            CancellationToken.None);

        await Assert.That(claimBeforeErasure?.Id).IsEqualTo(ownerRow.Id);
        await Assert.That(claimAfterErasure?.UserId).IsNull();
        await Assert.That(unrelatedClaim?.Id).IsEqualTo(unrelatedRow.Id);
    }

    private static Tenant CreateTenant(string slug) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = slug,
        Slug = $"{slug}-{Guid.NewGuid():N}",
        TenantStatus = null!,
        TenantStatusId = (int)TenantStatusEnum.Active
    };

    private static User CreateUser(string emailPrefix) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new UserPii
        {
            Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
            FirstName = "Privacy",
            LastName = "Owner"
        },
        EmailVerified = true,
        CreatedAt = DateTime.UtcNow
    };

    private static TenantUser CreateTenantUser(Tenant tenant, User user) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant.Id,
        Tenant = tenant,
        UserId = user.Id,
        User = user,
        StatusId = (int)TenantUserStatusEnum.Active,
        CreatedAt = DateTime.UtcNow
    };

    private static Actor CreateUserActor(User user)
    {
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Owner" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        actor.AtprotoIdentities.Add(new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse($"did:plc:{Guid.NewGuid():N}"))
        {
            Id = Guid.CreateVersion7(),
            ActorId = actor.Id,
            Actor = actor,

            Handle = $"owner-{Guid.NewGuid():N}.example.invalid",
            PdsHost = "https://pds.example.invalid",
            IsActive = true,
            LastResolvedAt = DateTime.UtcNow
        });
        return actor;
    }

    private static UserExternalLogin CreateExternalLogin(
        Tenant tenant,
        User user,
        string provider,
        string providerKey) => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)provider.ParseAuthenticationProviderKind(),
            AuthenticationProvider = null!,
            ProviderKey = providerKey,
            ProviderDisplayName = "Keycloak",
            CreatedAt = DateTime.UtcNow
        };

    private static WebPushDispatchOutbox CreateWebPushDispatch(
        Tenant tenant,
        User user,
        WebPushSubscription subscription,
        NotificationPreferenceCategory category,
        string payloadJson) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            NotificationId = Guid.CreateVersion7(),
            CategoryId = category.Id,
            Category = category,
            SubscriptionId = subscription.Id,
            Subscription = subscription,
            UserId = user.Id,
            User = user,
            PayloadJson = payloadJson,
            CreatedAt = DateTime.UtcNow
        };

    private static NotificationIntent CreateNotificationIntent(
        Tenant tenant,
        User user,
        NotificationCategory category,
        NotificationOwnershipType ownershipType,
        NotificationRecipientKind recipientKind,
        NotificationIntentStatus status,
        string deduplicationKey) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            CategoryId = category.Id,
            Category = category,
            OwnershipTypeId = ownershipType.Id,
            OwnershipType = ownershipType,
            RecipientKindId = recipientKind.Id,
            RecipientKind = recipientKind,
            StatusId = status.Id,
            Status = status,
            TemplateKey = "privacy-erasure-canary",
            DeduplicationKey = deduplicationKey,
            RecipientUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

    private static EmailDispatchOutbox CreateEmailDispatch(
        Tenant tenant,
        User user,
        NotificationIntent intent,
        string recipientEmail,
        string subject,
        string body) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Kind = EmailDispatchKind.OrganizerNotification,
            SourceType = "privacy-erasure-canary",
            SourceId = Guid.CreateVersion7(),
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            RecipientUserId = user.Id,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = recipientEmail,
            Subject = subject,
            PlainTextBody = body,
            HtmlBody = $"<p>{body}</p>",
            ReplyTo = "reply@example.invalid",
            ProviderMessageId = $"provider-{Guid.NewGuid():N}",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            Status = EmailDispatchStatus.Sent,
            CreatedAt = DateTime.UtcNow
        };

    private static IntegrationSyncOutbox CreateIntegrationSync(
        Tenant tenant,
        User user,
        string subscriberEmail,
        string subscriberName,
        string subscriberPayloadJson) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            Kind = IntegrationKind.Listmonk,
            SourceType = "privacy-erasure-canary",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = subscriberEmail,
            SubscriberName = subscriberName,
            SubscriberPayloadJson = subscriberPayloadJson,
            ListmonkListId = 42,
            PreconfirmSubscriptions = true,
            Status = IntegrationSyncStatus.Pending,
            MaxAttempts = 5,
            CreatedAt = DateTime.UtcNow
        };

    private static StorageObject CreateStorageObject(
        Tenant tenant,
        Actor actor,
        FileType fileType,
        string? objectKey,
        string fullName,
        string safeDisplayName) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            ActorId = actor.Id,
            Actor = actor,
            FileTypeId = fileType.Id,
            FileType = fileType,
            Provider = "s3_compatible",
            Uri = objectKey is null ? string.Empty : $"/storage/{objectKey}",
            ObjectKey = objectKey,
            FullName = fullName,
            SafeDisplayName = safeDisplayName,
            Extension = ".png",
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.ProfileImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            Size = 128,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static StorageUploadSession CreateStorageUploadSession(
        Tenant tenant,
        User user,
        string objectKey) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            Provider = StorageProviders.Local,
            RouteKey = StorageRouteKeys.General,
            PolicyMaxUploadBytes = 8,
            PolicyVersion = "1",
            ExpectedSizeBytes = 8,
            ReservedBytes = 8,
            ContentType = "text/plain",
            OriginalFileName = "owner.txt",
            SafeDisplayName = "owner.txt",
            Extension = "txt",
            Purpose = StorageObjectPurposes.Attachment,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Status = StorageUploadSessionStates.Uploading,
            ObjectKey = objectKey,
            IdempotencyKey = $"upload-{Guid.CreateVersion7():N}",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            UploadStartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user.Id,
            UpdatedBy = user.Id,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static WebhookConsumer CreateWebhookConsumer(
        Tenant tenant,
        User owner,
        WebhookConsumerKindLookup kind,
        WebhookConsumerStatusLookup status,
        WebhookProviderModeLookup providerMode,
        string externalProviderAppId) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            OwnerUserId = owner.Id,
            ConsumerKindId = kind.Id,
            ConsumerKindLookup = kind,
            StatusId = status.Id,
            StatusLookup = status,
            ProviderModeId = providerMode.Id,
            ProviderModeLookup = providerMode,
            Name = $"consumer-{externalProviderAppId}",
            ExternalProviderAppId = externalProviderAppId,
            ConfigurationVersion = 1,
            CreatedAt = DateTime.UtcNow
        };

    private static WebhookEndpoint CreateWebhookEndpoint(
        Tenant tenant,
        WebhookConsumer consumer,
        WebhookEndpointStatusLookup status,
        string url,
        string? providerEndpointId) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            ConsumerId = consumer.Id,
            Consumer = consumer,
            StatusId = status.Id,
            StatusLookup = status,
            Url = url,
            SecretRef = $"secret-{Guid.NewGuid():N}",
            SecretVersion = 1,
            SecretActivatedAt = DateTime.UtcNow,
            ProviderEndpointId = providerEndpointId,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            ConfigurationVersion = 1,
            CreatedAt = DateTime.UtcNow
        };

    private static Explore.Domain.Event CreateEvent(
        Tenant tenant,
        Actor actor,
        int eventFormatId,
        int eventStatusId,
        int visibilityTypeId,
        int eventProvenanceTypeId,
        string title) => new((EventStatusEnum)eventStatusId)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            ActorId = actor.Id,
            Actor = actor,
            Title = title,
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            VisibilityTypeId = visibilityTypeId,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = eventFormatId,
            EventFormat = null!,
            EventProvenanceTypeId = eventProvenanceTypeId,
            EventProvenanceType = null!
        };

    private static EventReport CreateReport(
        Tenant tenant,
        Explore.Domain.Event @event,
        User reporter,
        Actor reporterActor) =>
        EventReport.Create(
            tenant.Id,
            @event.Id,
            reporter.Id,
            reporterActor.Id,
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "privacy-erasure-canary",
            null,
            EventReportPriority.Normal,
            null,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: false,
            null,
            null,
            null);

    private static EventReportExternalLink CreateExternalReportLink(
        Tenant tenant,
        EventReport report,
        EventReportExternalProvider provider,
        string? providerCaseId,
        string? providerSignalId)
    {
        EventReportExternalLink link = EventReportExternalLink.CreatePending(
            tenant.Id,
            report.Id,
            null,
            provider,
            $"privacy-erasure-{Guid.CreateVersion7():N}");
        link.MarkSynced(
            providerCaseId,
            providerSignalId,
            "https://provider.example.invalid/report",
            DateTime.UtcNow);
        return link;
    }

    private static WebhookTargetGraph CreateWebhookTargetGraph(
        Tenant tenant,
        WebhookConsumer consumer,
        WebhookEndpoint endpoint)
    {
        DateTime utcNow = DateTime.UtcNow;
        WebhookMessage message = WebhookMessage.Create(
            tenant.Id,
            "privacy.erasure.canary",
            Guid.CreateVersion7().ToString("N"),
            "user",
            Guid.CreateVersion7(),
            consumer.Id,
            System.Text.Encoding.UTF8.GetBytes("{\"canary\":true}"),
            "application/json",
            "utf-8",
            utcNow,
            utcNow.AddDays(30),
            utcNow);
        var capturedAtUtc = new DateTimeOffset(utcNow);
        WebhookDeliveryPlanSnapshot plan = WebhookDeliveryPlanSnapshot.Create(
            tenant.Id,
            message.Id,
            consumer.Id,
            WebhookProviderMode.Composite,
            $"consumer-v{consumer.ConfigurationVersion}",
            "contract-v1",
            "standard",
            "retention-v1",
            capturedAtUtc.AddDays(30),
            capturedAtUtc.AddDays(60),
            capturedAtUtc.AddDays(90),
            capturedAtUtc.AddDays(90),
            capturedAtUtc.AddDays(30),
            capturedAtUtc);
        WebhookLocalTargetSnapshot target = WebhookLocalTargetSnapshot.Create(
            plan,
            endpoint,
            endpoint.ConfigurationVersion,
            new DateTimeOffset(endpoint.SecretActivatedAt),
            null,
            capturedAtUtc);
        return new WebhookTargetGraph(message, plan, target);
    }

    private sealed record WebhookTargetGraph(
        WebhookMessage Message,
        WebhookDeliveryPlanSnapshot Plan,
        WebhookLocalTargetSnapshot Target);

    private sealed record TestCurrentUserService(Guid Id) : ICurrentUserService
    {
        public Guid? UserId => Id;
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingPrivacyErasureAuthority : IPrivacyErasureAuthority
    {
        private PrivacyErasureIntent? _intent;

        public Task<PrivacyErasureAuthorityState> GetStateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long highWater = _intent?.AuthoritySequence ?? 0;
            return Task.FromResult(new PrivacyErasureAuthorityState(highWater, 0));
        }

        public Task<PrivacyErasureIntent> AppendAsync(
            PrivacyErasureRequest intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime utcNow = DateTime.UtcNow;
            utcNow = new DateTime(utcNow.Ticks - (utcNow.Ticks % 10), DateTimeKind.Utc);
            _intent ??= PrivacyErasureIntent.Record(
                intent.IntentId,
                1,
                intent.SubjectKind,
                intent.SubjectId,
                intent.ReasonCode,
                intent.PolicyVersion,
                utcNow,
                utcNow);
            return Task.FromResult(_intent);
        }

        public Task<IReadOnlyList<PrivacyErasureIntent>> ReadAfterAsync(
            long authoritySequence,
            int limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<PrivacyErasureIntent> result =
                _intent is not null && _intent.AuthoritySequence > authoritySequence && limit > 0
                    ? [_intent]
                    : [];
            return Task.FromResult(result);
        }
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
