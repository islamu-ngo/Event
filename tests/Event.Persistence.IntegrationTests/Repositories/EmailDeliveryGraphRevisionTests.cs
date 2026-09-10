
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryGraphRevisionTests
{
    [Test]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Arguments(false, false)]
    [Arguments(true, false)]
    public async Task UnavailableDirectGraph_KeepsInAppButPersistsSkippedLinkedEmail(bool directRepository, bool disabled)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            if (disabled)
                await SetEnabledAsync(context, services, enabled: false);
            else
                await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.SmtpHost,
                    value: "\"\"", mutationLock: services.MutationLock);
            long revision = await ReadRevisionAsync(context, authority.TenantId);
            var request = CreateRequest(authority);

            if (directRepository)
                await services.Repository.CreateGraphAsync(CreateGraph(request));
            else
                await services.Materializer.MaterializeAsync(request);

            await using var observer = CreateContext(databasePath);
            var graph = await LoadGraphAsync(observer, authority.TenantId, request.Intent.DeduplicationKey!);
            await AssertSuppressedGraphAsync(observer, graph, revision, requireLinkedEmail: true);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task RequiredUnavailableGraph_PersistsCapabilityParkWithoutHandoff()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            await SetEnabledAsync(context, services, enabled: false);
            var request = CreateRequest(authority);
            request = request with
            {
                DeliveryPolicy = NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
                EmailRequired = true,
                Intent = request.Intent with
                {
                    Category = Explore.Application.Notifications.NotificationCategory.TrustSafetyModeration,
                    TemplateKey = NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey
                }
            };
            request.Email!.Kind = EmailDispatchKind.ModerationAvailabilityRequired;

            await services.Materializer.MaterializeAsync(request);

            await using var observer = CreateContext(databasePath);
            var graph = await LoadGraphAsync(observer, authority.TenantId, request.Intent.DeduplicationKey!);
            await AssertInAppAsync(graph);
            var delivery = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
            await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Parked);
            await Assert.That(delivery.EmailDispatchOutbox!.Status).IsEqualTo(EmailDispatchStatus.Parked);
            await Assert.That(delivery.EmailDispatchOutbox.ParkReason).IsEqualTo(EmailDispatchParkReason.CapabilityUnavailable);
            await Assert.That(delivery.EmailDispatchOutbox.AttemptCount).IsEqualTo(0);
            await Assert.That(delivery.EmailDispatchOutbox.ProcessingLeaseToken).IsNull();
            await Assert.That(delivery.EmailDispatchOutbox.NextAttemptAt).IsNull();
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task NewEnabledGraph_UsesCurrentRevisionAndCanEnterRealHandoff()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            await SetEnabledAsync(context, services, enabled: false);
            await SetEnabledAsync(context, services, enabled: true);
            long revision = await ReadRevisionAsync(context, authority.TenantId);
            var request = CreateRequest(authority);
            await services.Materializer.MaterializeAsync(request);

            await using var observer = CreateContext(databasePath);
            var graph = await LoadGraphAsync(observer, authority.TenantId, request.Intent.DeduplicationKey!);
            await Assert.That(graph.EmailDeliveryPolicyRevision).IsEqualTo(revision);
            await AssertInAppAsync(graph);
            var emailDelivery = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
            await Assert.That(emailDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
            var email = emailDelivery.EmailDispatchOutbox!;
            await Assert.That(email.Status).IsEqualTo(EmailDispatchStatus.Pending);
            var lease = Guid.CreateVersion7();
            var claimed = await new EmailDispatchOutboxRepository(observer).TryClaimSpecificAsync(new EmailDispatchSpecificClaimRequest(
                TenantId: authority.TenantId, PublishEventId: email.PublishEventId, LeaseToken: lease,
                GlobalProcessingLimit: 10, TenantProcessingLimit: 5, OptionalReminderBacklogHighWatermark: 100,
                OptionalReminderBacklogLowWatermark: 50, ClaimedAt: DateTime.UtcNow), CancellationToken.None);
            await Assert.That(claimed).IsNotNull();
            var result = await CreateEvaluator(observer).EvaluateAndBeginProviderHandoffAsync(new EmailDispatchEligibilityRequest(
                TenantId: authority.TenantId, OutboxId: email.Id, ProcessingLeaseToken: lease, AttemptNumber: 0,
                GlobalSmtpRateLimitPerMinute: 60, TenantSmtpRateLimitPerMinute: 60,
                ConsumerId: "graph-revision-test", EvaluatedAt: DateTime.UtcNow));
            await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
            await Assert.That(result.ReceiptId).IsNotNull();
            await Assert.That(result.AttemptNumber).IsEqualTo(1);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task OldUnavailableFanoutOccurrence_KeepsOriginalRevisionAfterRestoration(bool disabled)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            if (disabled)
                await SetEnabledAsync(context, services, enabled: false);
            else
                await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.SmtpHost,
                    value: "\"\"", mutationLock: services.MutationLock);
            long originalRevision = await ReadRevisionAsync(context, authority.TenantId);
            var occurrence = await SeedOccurrenceAsync(context, authority, originalRevision);
            if (disabled)
                await SetEnabledAsync(context, services, enabled: true);
            else
                await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.SmtpHost,
                    value: "\"smtp.fixture.test\"", mutationLock: services.MutationLock);
            await Assert.That(await ReadRevisionAsync(context, authority.TenantId)).IsGreaterThan(originalRevision);
            var request = CreateRequest(authority, occurrence);

            await services.Materializer.MaterializeAsync(request);

            await using var observer = CreateContext(databasePath);
            var graph = await LoadGraphAsync(observer, authority.TenantId, request.Intent.DeduplicationKey!);
            await Assert.That(graph.FanoutOccurrenceId).IsEqualTo(occurrence.Id);
            await AssertSuppressedGraphAsync(observer, graph, originalRevision, requireLinkedEmail: true);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task RepairWinningIntent_PreservesOriginalRevisionAndCannotResurrectSkippedEmail()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            await SetEnabledAsync(context, services, enabled: false);
            long originalRevision = await ReadRevisionAsync(context, authority.TenantId);
            var originalRequest = CreateRequest(authority) with { Email = null, EmailSkipReason = "email_not_eligible" };
            await services.Materializer.MaterializeAsync(originalRequest);
            var winner = await LoadGraphAsync(context, authority.TenantId, originalRequest.Intent.DeduplicationKey!);
            var originalNotificationId = winner.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.InApp).NotificationId;
            await SetEnabledAsync(context, services, enabled: true);
            var retryRequest = CreateRequest(authority) with { IntentId = winner.Id, Intent = originalRequest.Intent };
            retryRequest.Email!.SourceId = winner.Id;
            var retry = CreateGraph(retryRequest);
            var expectedNotification = retry.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.InApp).Notification;
            var expectedEmail = retry.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email).EmailDispatchOutbox;

            await services.MutationLock.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]],
                token => services.UnitOfWork.ExecuteInTransactionAsync(async transactionToken =>
                {
                    await services.Repository.RepairMissingRecipientDeliveryRowsAsync(winner, retry.Deliveries.ToArray(),
                        expectedNotification, expectedEmail, transactionToken);
                    return true;
                }, token), CancellationToken.None);

            await using var observer = CreateContext(databasePath);
            var repaired = await LoadGraphAsync(observer, authority.TenantId, originalRequest.Intent.DeduplicationKey!);
            await Assert.That(repaired.Id).IsEqualTo(winner.Id);
            await Assert.That(repaired.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.InApp).NotificationId)
                .IsEqualTo(originalNotificationId);
            await AssertSuppressedGraphAsync(observer, repaired, originalRevision, requireLinkedEmail: false);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task RepairAfterAdmissionAndDisable_PreservesHandoffUntilAcceptedSettlement()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            var request = CreateRequest(authority);
            await using (var creationContext = CreateContext(databasePath))
                await CreateServices(creationContext).Materializer.MaterializeAsync(request);
            Guid lease;
            Guid outboxId;
            await using (var admissionContext = CreateContext(databasePath))
            {
                var graph = await LoadGraphAsync(admissionContext, authority.TenantId, request.Intent.DeduplicationKey!);
                var email = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email).EmailDispatchOutbox!;
                outboxId = email.Id;
                lease = await ClaimAndAdmitAsync(admissionContext, authority, email);
            }

            await using var repairContext = CreateContext(databasePath);
            var services = CreateServices(repairContext);
            await SetEnabledAsync(repairContext, services, enabled: false);
            var winner = await LoadGraphAsync(repairContext, authority.TenantId, request.Intent.DeduplicationKey!);
            var beforeDelivery = winner.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
            await Assert.That(beforeDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
            await Assert.That(beforeDelivery.EmailDispatchOutbox!.Status).IsEqualTo(EmailDispatchStatus.Processing);
            await Assert.That(beforeDelivery.EmailDispatchOutbox.ProcessingLeaseToken).IsEqualTo(lease);
            var receiptBefore = await repairContext.EmailDispatchReceipts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == authority.TenantId && row.EmailDispatchOutboxId == outboxId);
            var attemptBefore = await repairContext.EmailDispatchAttempts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == authority.TenantId && row.EmailDispatchOutboxId == outboxId);
            await Assert.That(receiptBefore.Status).IsEqualTo(EmailDispatchReceiptStatus.Processing);
            await Assert.That(attemptBefore.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
            await Assert.That(attemptBefore.FailureCategory).IsEqualTo("provider_handoff_started");
            var retryRequest = CreateRequest(authority) with { IntentId = winner.Id, Intent = request.Intent };
            retryRequest.Email!.SourceId = winner.Id;
            var retry = CreateGraph(retryRequest);
            await services.MutationLock.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]],
                token => services.UnitOfWork.ExecuteInTransactionAsync(async transactionToken =>
                {
                    await services.Repository.RepairMissingRecipientDeliveryRowsAsync(winner, retry.Deliveries.ToArray(),
                        retry.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.InApp).Notification,
                        retryRequest.Email, transactionToken);
                    return true;
                }, token), CancellationToken.None);

            var repaired = await LoadGraphAsync(repairContext, authority.TenantId, request.Intent.DeduplicationKey!);
            var repairedDelivery = repaired.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
            await Assert.That(repairedDelivery.EmailDispatchOutboxId).IsEqualTo(outboxId);
            await Assert.That(repairedDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
            await Assert.That(repairedDelivery.EmailDispatchOutbox!.Status).IsEqualTo(EmailDispatchStatus.Processing);
            await Assert.That(repairedDelivery.EmailDispatchOutbox.ProcessingLeaseToken).IsEqualTo(lease);
            await Assert.That(repairedDelivery.EmailDispatchOutbox.AttemptCount).IsEqualTo(1);
            await new EmailDispatchOutboxRepository(repairContext).SettleProviderAccepted(new EmailDispatchAcceptedSettlement(
                TenantId: authority.TenantId, OutboxId: outboxId, ProcessingLeaseToken: lease,
                AttemptNumber: 1, SettledAt: DateTime.UtcNow, ProviderMessageId: null), CancellationToken.None);

            await using var observer = CreateContext(databasePath);
            var settled = await LoadGraphAsync(observer, authority.TenantId, request.Intent.DeduplicationKey!);
            var settledDelivery = settled.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
            await Assert.That(settledDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
            await Assert.That(settledDelivery.EmailDispatchOutbox!.Status).IsEqualTo(EmailDispatchStatus.Sent);
            var receiptAfter = await observer.EmailDispatchReceipts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == authority.TenantId && row.EmailDispatchOutboxId == outboxId);
            var attemptAfter = await observer.EmailDispatchAttempts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == authority.TenantId && row.EmailDispatchOutboxId == outboxId);
            await Assert.That(receiptAfter.Id).IsEqualTo(receiptBefore.Id);
            await Assert.That(receiptAfter.Status).IsEqualTo(EmailDispatchReceiptStatus.Completed);
            await Assert.That(attemptAfter.Id).IsEqualTo(attemptBefore.Id);
            await Assert.That(attemptAfter.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Succeeded);
            await AssertInAppAsync(settled);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task RolledBackDisabledGraph_ReusingSameRequestAfterEnableCreatesCoherentFreshEmail()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            await SetEnabledAsync(context, services, enabled: false);
            var request = CreateRequest(authority);
            string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
                context.Model.FindEntityType(typeof(NotificationIntent))!.GetTableName()!);
            string trigger = $"CREATE TRIGGER reject_graph_insert AFTER INSERT ON {table} BEGIN SELECT RAISE(ABORT, 'graph_insert_rejected'); END";
            await context.Database.ExecuteSqlRawAsync(trigger);
            await Assert.That(async () => await services.Materializer.MaterializeAsync(request)).Throws<DbUpdateException>();
            await using (var observer = CreateContext(databasePath))
            {
                await Assert.That(await observer.NotificationIntents
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .CountAsync(row => row.TenantId == authority.TenantId)).IsEqualTo(0);
                await Assert.That(await observer.EmailDispatchOutbox
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .CountAsync(row => row.TenantId == authority.TenantId)).IsEqualTo(0);
            }
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_graph_insert");
            await SetEnabledAsync(context, services, enabled: true);
            long revision = await ReadRevisionAsync(context, authority.TenantId);

            await services.Materializer.MaterializeAsync(request);

            await using var finalContext = CreateContext(databasePath);
            var graph = await LoadGraphAsync(finalContext, authority.TenantId, request.Intent.DeduplicationKey!);
            await Assert.That(graph.Id).IsEqualTo(request.IntentId);
            await Assert.That(graph.EmailDeliveryPolicyRevision).IsEqualTo(revision);
            var delivery = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
            await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
            await Assert.That(delivery.EmailDispatchOutbox!.Status).IsEqualTo(EmailDispatchStatus.Pending);
            await Assert.That(delivery.EmailDispatchOutbox.LastFailureCategory).IsNull();
            await Assert.That(delivery.EmailDispatchOutbox.LastError).IsNull();
            await Assert.That(delivery.EmailDispatchOutbox.ParkedAt).IsNull();
            await Assert.That(delivery.EmailDispatchOutbox.AttemptCount).IsEqualTo(0);
            await AssertInAppAsync(graph);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallerOwnedTransactionWithoutPolicyLock_RejectsBeforeGraphWrite(bool directRepository)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            var request = CreateRequest(authority);

            await Assert.That(async () => await services.UnitOfWork.ExecuteInTransactionAsync(async token =>
            {
                if (directRepository)
                    await services.Repository.CreateGraphAsync(CreateGraph(request), token);
                else
                    await services.Materializer.MaterializeInCurrentTransactionAsync(request, token);
            })).Throws<InvalidOperationException>();

            await using var observer = CreateContext(databasePath);
            await Assert.That(await observer.NotificationIntents
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .CountAsync(row => row.TenantId == authority.TenantId)).IsEqualTo(0);
            await Assert.That(await observer.NotificationDeliveries
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .CountAsync(row => row.TenantId == authority.TenantId)).IsEqualTo(0);
            await Assert.That(await observer.EmailDispatchOutbox
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .CountAsync(row => row.TenantId == authority.TenantId)).IsEqualTo(0);
            await Assert.That(await observer.Notifications
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .CountAsync(row => row.TenantId == authority.TenantId)).IsEqualTo(0);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task AtomicIndependentSmtpBatch_PreservesInheritedGraphSuppressionHistory()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
                value: "false", mutationLock: services.MutationLock);
            var request = CreateRequest(authority);
            await services.Materializer.MaterializeAsync(request);
            var before = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == authority.TenantId);
            await services.MutationLock.ExecuteOrderedGroupsAsync(
                [EmailDeliverySettingKeys.All],
                token => services.UnitOfWork.ExecuteSerializableAsync(async transactionToken =>
                {
                    await ApplyEmailSettingsAsync(context: context, mutations:
                        [
                            new(TenantId: authority.TenantId, Key: GovernanceSettingKeys.Email.SmtpHost,
                                Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.atomic-tenant.test\""),
                            new(TenantId: authority.TenantId, Key: GovernanceSettingKeys.Email.FromAddress,
                                Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"events@atomic-tenant.test\"")
                        ], actorUserId: authority.UserId, mutationLock: services.MutationLock,
                        cancellationToken: transactionToken);
                    return true;
                }, token), CancellationToken.None);

            await using var observer = CreateContext(databasePath);
            var after = await observer.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == authority.TenantId);
            await Assert.That(after.DeliveryPolicyRevision).IsEqualTo(before.DeliveryPolicyRevision + 1);
            await Assert.That(after.OptionalSuppressedThroughRevision).IsEqualTo(before.OptionalSuppressedThroughRevision);
            var graph = await LoadGraphAsync(observer, authority.TenantId, request.Intent.DeduplicationKey!);
            await Assert.That(graph.EmailDeliveryPolicyRevision).IsEqualTo(before.DeliveryPolicyRevision);
            await AssertInAppAsync(graph);
            var email = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email).EmailDispatchOutbox!;
            await ClaimAndAdmitAsync(observer, authority, email);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task EnableMaterializeAndAvailableEdit_InOneTransactionKeepsFreshGraphAdmissible()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var authority = await SeedAuthorityAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var services = CreateServices(context);
            await SetEnabledAsync(context, services, enabled: false);
            long tenantRevisionBefore = await ReadRevisionAsync(context, authority.TenantId);
            long instanceRevisionBefore = await context.EmailDispatchProcessorStates.AsNoTracking()
                .Where(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
                .Select(row => row.DeliveryPolicyRevision).SingleAsync();
            var request = CreateRequest(authority);
            await services.MutationLock.ExecuteOrderedGroupsAsync(
                [EmailDeliverySettingKeys.All],
                token => services.UnitOfWork.ExecuteSerializableAsync(async transactionToken =>
                {
                    await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        value: "true", actorUserId: authority.UserId, mutationLock: services.MutationLock,
                        cancellationToken: transactionToken);
                    await services.Materializer.MaterializeInCurrentTransactionAsync(request, transactionToken);
                    await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.FromName,
                        value: "\"Available sender edit\"", actorUserId: authority.UserId,
                        mutationLock: services.MutationLock, cancellationToken: transactionToken);
                    return true;
                }, token), CancellationToken.None);

            await using var observer = CreateContext(databasePath);
            var tenantControl = await observer.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == authority.TenantId);
            var processor = await observer.EmailDispatchProcessorStates.AsNoTracking()
                .SingleAsync(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode);
            await Assert.That(tenantControl.DeliveryPolicyRevision).IsEqualTo(tenantRevisionBefore + 1);
            await Assert.That(processor.DeliveryPolicyRevision).IsEqualTo(instanceRevisionBefore + 1);
            await Assert.That(tenantControl.OptionalSuppressedThroughRevision).IsEqualTo(tenantRevisionBefore);
            await Assert.That(processor.OptionalSuppressedThroughRevision).IsEqualTo(instanceRevisionBefore);
            var graph = await LoadGraphAsync(observer, authority.TenantId, request.Intent.DeduplicationKey!);
            await Assert.That(graph.EmailDeliveryPolicyRevision).IsEqualTo(tenantControl.DeliveryPolicyRevision);
            var delivery = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
            await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
            await Assert.That(delivery.EmailDispatchOutbox!.Status).IsEqualTo(EmailDispatchStatus.Pending);
            await ClaimAndAdmitAsync(observer, authority, delivery.EmailDispatchOutbox);
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static async Task<Authority> SeedAuthorityAsync(string databasePath)
    {
        await CreateDatabaseAsync(databasePath);
        await using var context = CreateContext(databasePath);
        DateTime now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Graph revision tenant",
            Slug = $"graph-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            EmailVerified = true,
            CreatedAt = now,
            Pii = new UserPii { Email = $"graph-{Guid.CreateVersion7():N}@example.test", FirstName = "Graph", LastName = "Recipient" }
        };
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now,
            CreatedAt = now
        });
        await context.SaveChangesAsync();
        return new(TenantId: tenant.Id, UserId: user.Id, Email: user.Email!);
    }

    private static RecipientNotificationMaterialization CreateRequest(Authority authority, NotificationFanoutOccurrence? occurrence = null)
    {
        Guid intentId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        string template = occurrence?.TemplateKey ?? "registration.confirmed";
        var email = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = authority.TenantId,
            RecipientUserId = authority.UserId,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = authority.Email,
            Kind = occurrence is null ? EmailDispatchKind.RegistrationConfirmation : EmailDispatchKind.EventUpdated,
            SourceType = occurrence is null ? "notification_intent" : "notification_fanout_occurrence",
            SourceId = occurrence?.Id ?? intentId,
            EventId = occurrence?.EventId,
            Subject = "Graph revision notification",
            PlainTextBody = "Notification content.",
            CreatedAt = occurrence?.OccurredAt ?? now
        };
        return new RecipientNotificationMaterialization(
            IntentId: intentId,
            Intent: new NotificationIntentDraft(
                Category: occurrence is null ? Explore.Application.Notifications.NotificationCategory.RegistrationLifecycle : Explore.Application.Notifications.NotificationCategory.EventLifecycle,
                TenantId: authority.TenantId, RecipientKind: nameof(NotificationRecipientKindEnum.User), TemplateKey: template,
                DeduplicationKey: $"graph-revision:{intentId:N}", UserId: authority.UserId,
                EventId: occurrence?.EventId, FanoutOccurrenceId: occurrence?.Id),
            DeliveryPolicy: occurrence is null ? NotificationDeliveryPolicyEnum.RegistrationStatusOptional : NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            DisclosureLevel: "standard",
            InApp: new RecipientInAppNotificationDraft(NotificationTypeId: (int)NotificationTypeEnum.General,
                Title: "Graph revision notification", Body: "In-app remains available.",
                NotificationScopeId: (int)ActorTypeEnum.User, NotificationReasonId: (int)NotificationReasonEnum.System, IsRequired: false),
            Email: email, IncludeEmailChannel: true, EmailRequired: false,
            PreferenceCategoryCode: occurrence is null ? NotificationPreferenceCategoryCodes.RegistrationStatus : NotificationPreferenceCategoryCodes.EventUpdates,
            EmailPreferenceEnabled: true, MaterializedAt: now, IncludeInAppChannel: true, InAppPreferenceEnabled: true);
    }

    private static NotificationIntent CreateGraph(RecipientNotificationMaterialization request)
    {
        var intent = new NotificationIntent
        {
            Id = request.IntentId,
            TenantId = request.Intent.TenantId!.Value,
            RecipientUserId = request.Intent.UserId!.Value,
            CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = request.Intent.TemplateKey!,
            DeduplicationKey = request.Intent.DeduplicationKey!,
            CreatedAt = request.MaterializedAt!.Value
        };
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = intent.TenantId,
            Tenant = null!,
            UserId = intent.RecipientUserId,
            User = null!,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            NotificationTypeId = request.InApp!.NotificationTypeId,
            NotificationType = null!,
            NotificationScopeId = request.InApp.NotificationScopeId,
            NotificationScope = null!,
            NotificationReasonId = request.InApp.NotificationReasonId,
            Title = request.InApp.Title,
            Body = request.InApp.Body,
            DeduplicationKey = $"{intent.DeduplicationKey}:in-app",
            CreatedAt = intent.CreatedAt
        };
        var email = request.Email!;
        email.NotificationIntentId = intent.Id;
        email.NotificationIntent = intent;
        foreach (var channel in new[] { NotificationPreferenceChannelEnum.InApp, NotificationPreferenceChannelEnum.Email })
        {
            bool isEmail = channel == NotificationPreferenceChannelEnum.Email;
            intent.Deliveries.Add(new NotificationDelivery
            {
                Id = Guid.CreateVersion7(),
                TenantId = intent.TenantId,
                NotificationIntentId = intent.Id,
                NotificationIntent = intent,
                ChannelId = (int)channel,
                DeliveryPolicyId = (int)request.DeliveryPolicy,
                IsRequired = false,
                PolicyVersion = 1,
                PreferenceCategoryCode = request.PreferenceCategoryCode,
                DisclosureLevel = request.DisclosureLevel,
                TemplateKey = intent.TemplateKey,
                TemplateVersion = 1,
                StatusId = (int)(isEmail ? NotificationDeliveryStatusEnum.Queued : NotificationDeliveryStatusEnum.Delivered),
                NotificationId = isEmail ? null : notification.Id,
                Notification = isEmail ? null : notification,
                EmailDispatchOutboxId = isEmail ? email.Id : null,
                EmailDispatchOutbox = isEmail ? email : null,
                RecipientAddressSource = isEmail ? email.RecipientAddressSource : null,
                QueuedAt = isEmail ? intent.CreatedAt : null,
                CompletedAt = isEmail ? null : intent.CreatedAt,
                CreatedAt = intent.CreatedAt
            });
        }
        return intent;
    }

    private static async Task<NotificationFanoutOccurrence> SeedOccurrenceAsync(ExploreDbContext context, Authority authority, long revision)
    {
        var principal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"graph-worker-{Guid.CreateVersion7():N}",
            DisplayName = "Graph fanout worker",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Bot,
            ActorType = null!,
            ServicePrincipalId = principal.Id,
            ServicePrincipal = principal,
            Pii = new ActorPii { DisplayName = "Graph fanout worker" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = authority.TenantId,
            Tenant = null!,
            Title = "Graph fanout event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = actor,
            OrganizerActorId = actor.Id,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        DateTime occurredAt = DateTime.UtcNow;
        var occurrence = NotificationFanoutOccurrence.Create(id: Guid.CreateVersion7(), tenantId: authority.TenantId,
            eventId: @event.Id, sessionId: null, occurredAt: occurredAt, audienceCutoffAt: occurredAt,
            aggregateVersion: Guid.CreateVersion7(), changeSetJson: "{}", safeBeforeSnapshotJson: "{}", safeAfterSnapshotJson: "{}",
            templateKey: "event_update", templateVersion: 1, deliveryPolicyId: (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            policyVersion: 1, priority: 1, notBefore: occurredAt, sourceType: "event", sourceId: @event.Id,
            coalescingKey: $"graph:{@event.Id:N}", coalescingWindowEndsAt: null, emailDeliveryPolicyRevision: revision);
        context.AddRange(actor, @event, occurrence);
        await context.SaveChangesAsync();
        return occurrence;
    }

    private static async Task AssertSuppressedGraphAsync(ExploreDbContext context, NotificationIntent graph, long revision, bool requireLinkedEmail)
    {
        await Assert.That(graph.EmailDeliveryPolicyRevision).IsEqualTo(revision);
        await AssertInAppAsync(graph);
        var emailDelivery = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
        await Assert.That(emailDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
        if (requireLinkedEmail)
            await Assert.That(emailDelivery.EmailDispatchOutbox).IsNotNull();
        if (emailDelivery.EmailDispatchOutbox is { } email)
        {
            await Assert.That(email.Status).IsEqualTo(EmailDispatchStatus.Skipped);
            await Assert.That(email.AttemptCount).IsEqualTo(0);
            await Assert.That(email.ProcessingLeaseToken).IsNull();
            await Assert.That(email.NextAttemptAt).IsNull();
        }
        await Assert.That(await context.EmailDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .CountAsync(row => row.TenantId == graph.TenantId && row.NotificationIntentId == graph.Id && row.Status != EmailDispatchStatus.Skipped))
            .IsEqualTo(0);
        await Assert.That(await context.EmailDispatchAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .CountAsync(row => row.TenantId == graph.TenantId)).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .CountAsync(row => row.TenantId == graph.TenantId)).IsEqualTo(0);
    }

    private static async Task<Guid> ClaimAndAdmitAsync(ExploreDbContext context, Authority authority, EmailDispatchOutbox email)
    {
        Guid lease = Guid.CreateVersion7();
        var claimed = await new EmailDispatchOutboxRepository(context).TryClaimSpecificAsync(new EmailDispatchSpecificClaimRequest(
            TenantId: authority.TenantId, PublishEventId: email.PublishEventId, LeaseToken: lease,
            GlobalProcessingLimit: 10, TenantProcessingLimit: 5, OptionalReminderBacklogHighWatermark: 100,
            OptionalReminderBacklogLowWatermark: 50, ClaimedAt: DateTime.UtcNow), CancellationToken.None);
        await Assert.That(claimed).IsNotNull();
        var result = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(new EmailDispatchEligibilityRequest(
            TenantId: authority.TenantId, OutboxId: email.Id, ProcessingLeaseToken: lease, AttemptNumber: 0,
            GlobalSmtpRateLimitPerMinute: 60, TenantSmtpRateLimitPerMinute: 60,
            ConsumerId: "graph-repair-handoff-test", EvaluatedAt: DateTime.UtcNow));
        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
        await Assert.That(result.ReceiptId).IsNotNull();
        await Assert.That(result.AttemptNumber).IsEqualTo(1);
        return lease;
    }

    private static async Task AssertInAppAsync(NotificationIntent graph)
    {
        var inApp = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.InApp);
        await Assert.That(inApp.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
        await Assert.That(inApp.Notification).IsNotNull();
        await Assert.That(inApp.Notification!.IsRead).IsFalse();
    }

    private static async Task<NotificationIntent> LoadGraphAsync(ExploreDbContext context, Guid tenantId, string deduplicationKey) =>
        (await CreateServices(context).Repository.GetGraphByTenantAndDeduplicationKeyAsync(tenantId, deduplicationKey))!;

    private static async Task<long> ReadRevisionAsync(ExploreDbContext context, Guid tenantId) =>
        await context.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .Where(row => row.TenantId == tenantId).Select(row => row.DeliveryPolicyRevision).SingleOrDefaultAsync();

    private static Task SetEnabledAsync(ExploreDbContext context, GraphServices services, bool enabled) =>
        enabled
            ? SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.DeliveryEnabled,
                value: "true", mutationLock: services.MutationLock)
            : ConfirmEmailDisableAsync(context: context, mutationLock: services.MutationLock);

    private static GraphServices CreateServices(ExploreDbContext context)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var repository = new NotificationIntentRepository(context, mutationLock);
        var materializer = new RecipientNotificationMaterializer(notificationGraphRepository: repository,
            unitOfWork: unitOfWork, mutationLock: mutationLock, privacyErasureStateRepository: new PrivacyErasureStateRepository(context));
        return new(Repository: repository, UnitOfWork: unitOfWork, MutationLock: mutationLock, Materializer: materializer);
    }

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"email-graph-revision-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }

    private sealed record Authority(Guid TenantId, Guid UserId, string Email);
    private sealed record GraphServices(NotificationIntentRepository Repository, EfCoreUnitOfWork UnitOfWork,
        RelationalSettingMutationLock MutationLock, RecipientNotificationMaterializer Materializer);
}
