// ABOUTME: File-backed SQLite regressions for provider-portable email repository claims and suppression.
// ABOUTME: Proves one-winner leases, provider fences, and reminder/fanout ledger alignment through public APIs.

using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchRepositories")]
public sealed class EmailDispatchRepositoriesSqliteTests
{
    [Test]
    public async Task ClaimsAndStaleRecovery_PreserveOneWinnerPauseStateAndProviderFence()
    {
        string databasePath = DatabasePath("claim");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch dispatch;
            await using (ExploreDbContext seedContext = CreateContext(databasePath))
            {
                dispatch = await SeedDispatchAsync(seedContext, "claim", EmailDispatchStatus.Pending);
                var repository = new EmailDispatchOutboxRepository(seedContext);
                DateTime changedAt = DateTime.UtcNow;
                EmailDispatchProcessorState rateState = await repository.SetGlobalSmtpRateLimitOverride(
                    37,
                    changedBy: null,
                    changedAt,
                    CancellationToken.None);
                await Assert.That(rateState.GlobalSmtpRateLimitPerMinuteOverride).IsEqualTo(37);

                await repository.SetTenantPauseState(
                    dispatch.TenantId,
                    true,
                    "maintenance",
                    changedBy: null,
                    changedAt,
                    CancellationToken.None);
                await Assert.That(await repository.TryClaimSpecificAsync(
                    ClaimRequest(dispatch, Guid.CreateVersion7()),
                    CancellationToken.None)).IsNull();
                await repository.SetTenantPauseState(
                    dispatch.TenantId,
                    false,
                    pauseReason: null,
                    changedBy: null,
                    changedAt,
                    CancellationToken.None);

                await repository.SetProcessorPauseState(
                    true,
                    "maintenance",
                    changedBy: null,
                    changedAt,
                    CancellationToken.None);
                await Assert.That(await repository.TryClaimSpecificAsync(
                    ClaimRequest(dispatch, Guid.CreateVersion7()),
                    CancellationToken.None)).IsNull();
                await repository.SetProcessorPauseState(
                    false,
                    pauseReason: null,
                    changedBy: null,
                    changedAt,
                    CancellationToken.None);
            }

            ExploreDbContext[] contexts = [CreateContext(databasePath), CreateContext(databasePath)];
            Guid[] leaseTokens = [Guid.CreateVersion7(), Guid.CreateVersion7()];
            try
            {
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<EmailDispatchOutbox?>[] claims = contexts.Select(async (context, index) =>
                {
                    await start.Task;
                    return await new EmailDispatchOutboxRepository(context).TryClaimSpecificAsync(
                        ClaimRequest(dispatch, leaseTokens[index]),
                        CancellationToken.None);
                }).ToArray();
                start.SetResult();
                EmailDispatchOutbox?[] results = await Task.WhenAll(claims)
                    .WaitAsync(TimeSpan.FromSeconds(15));

                await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
            }
            finally
            {
                foreach (ExploreDbContext context in contexts)
                {
                    await context.DisposeAsync();
                }
            }

            await using (ExploreDbContext recoveryContext = CreateContext(databasePath))
            {
                EmailDispatchOutbox processing = await recoveryContext.EmailDispatchOutbox
                    .IgnoreQueryFilters()
                    .SingleAsync(value => value.Id == dispatch.OutboxId);
                Guid winningLease = processing.ProcessingLeaseToken!.Value;
                recoveryContext.EmailDispatchAttempts.Add(new EmailDispatchAttempt
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = dispatch.TenantId,
                    EmailDispatchOutboxId = dispatch.OutboxId,
                    AttemptNumber = 0,
                    Outcome = EmailDispatchAttemptOutcome.Unknown,
                    StartedAt = DateTime.UtcNow.AddMinutes(-2),
                    FailureCategory = "provider_handoff_started",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2)
                });
                recoveryContext.EmailDispatchReceipts.Add(new EmailDispatchReceipt
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = dispatch.TenantId,
                    PublishEventId = dispatch.PublishEventId,
                    EmailDispatchOutboxId = dispatch.OutboxId,
                    Status = EmailDispatchReceiptStatus.Processing,
                    ConsumerId = "sqlite-worker",
                    FirstSeenAt = DateTime.UtcNow.AddMinutes(-2),
                    ProcessingStartedAt = DateTime.UtcNow.AddMinutes(-2),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2)
                });
                processing.ProcessingStartedAt = DateTime.UtcNow.AddMinutes(-2);
                processing.ProcessingLeaseToken = winningLease;
                await recoveryContext.SaveChangesAsync();
                recoveryContext.ChangeTracker.Clear();

                EmailDispatchStaleRecoveryResult recovered = await new EmailDispatchOutboxRepository(recoveryContext)
                    .RecoverStaleProcessing(
                        new EmailDispatchStaleRecoveryRequest(
                            DateTime.UtcNow.AddMinutes(-1),
                            DateTime.UtcNow,
                            "stale_before_handoff",
                            "Retryable stale lease.",
                            "stale_after_handoff",
                            "Provider handoff settlement is unknown.",
                            BatchSize: 10),
                        CancellationToken.None);
                await Assert.That(recovered).IsEqualTo(new EmailDispatchStaleRecoveryResult(0, 1));

                EmailDispatchOutbox persisted = await recoveryContext.EmailDispatchOutbox
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == dispatch.OutboxId);
                EmailDispatchAttempt attempt = await recoveryContext.EmailDispatchAttempts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.EmailDispatchOutboxId == dispatch.OutboxId);
                EmailDispatchReceipt receipt = await recoveryContext.EmailDispatchReceipts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.EmailDispatchOutboxId == dispatch.OutboxId);
                await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Unknown);
                await Assert.That(persisted.ProcessingLeaseToken).IsNull();
                await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
                await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Unknown);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task BatchClaim_ReplaysTheSameLeaseAndRespectsTenantLimit()
    {
        string databasePath = DatabasePath("batch");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch first;
            SeededDispatch second;
            await using (ExploreDbContext seedContext = CreateContext(databasePath))
            {
                first = await SeedDispatchAsync(seedContext, "batch-first", EmailDispatchStatus.Pending);
                second = await SeedDispatchAsync(seedContext, "batch-second", EmailDispatchStatus.Pending, first.TenantId);
            }

            await using (ExploreDbContext context = CreateContext(databasePath))
            {
                var repository = new EmailDispatchOutboxRepository(context);
                Guid leaseToken = Guid.CreateVersion7();
                var request = new EmailDispatchBatchClaimRequest(
                    leaseToken,
                    BatchSize: 2,
                    MaxRowsPerTenant: 2,
                    GlobalProcessingLimit: 10,
                    TenantProcessingLimit: 1,
                    OptionalReminderBacklogHighWatermark: 100,
                    OptionalReminderBacklogLowWatermark: 50,
                    ClaimedAt: DateTime.UtcNow);
                IReadOnlyList<EmailDispatchOutbox> firstClaim = await repository.ClaimPendingBatchAsync(
                    request,
                    CancellationToken.None);
                IReadOnlyList<EmailDispatchOutbox> replay = await repository.ClaimPendingBatchAsync(
                    request,
                    CancellationToken.None);

                await Assert.That(firstClaim.Count).IsEqualTo(1);
                await Assert.That(replay.Select(value => value.Id)).IsEquivalentTo(firstClaim.Select(value => value.Id));
                await Assert.That(firstClaim[0].ProcessingLeaseToken).IsEqualTo(leaseToken);
                await Assert.That(new[] { first.OutboxId, second.OutboxId }).Contains(firstClaim[0].Id);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task FanoutSuppression_SuppressesPreHandoffRowsButPreservesProviderFence()
    {
        string databasePath = DatabasePath("fanout");
        try
        {
            await CreateDatabaseAsync(databasePath);
            FanoutScope scope;
            await using (ExploreDbContext seedContext = CreateContext(databasePath))
            {
                scope = await SeedFanoutScopeAsync(seedContext);
            }

            await using (ExploreDbContext context = CreateContext(databasePath))
            {
                var unitOfWork = new EfCoreUnitOfWork(context);
                var repository = new NotificationFanoutEmailSuppressionRepository(context);
                DateTime suppressedAt = DateTime.UtcNow;
                NotificationFanoutEmailSuppressionResult first = await unitOfWork.ExecuteInTransactionAsync(
                    token => repository.SuppressPreHandoffAsync(
                        scope.TenantId,
                        scope.OccurrenceId,
                        suppressedAt,
                        token));
                NotificationFanoutEmailSuppressionResult replay = await unitOfWork.ExecuteInTransactionAsync(
                    token => repository.SuppressPreHandoffAsync(
                        scope.TenantId,
                        scope.OccurrenceId,
                        suppressedAt,
                        token));

                await Assert.That(first.OutboxRowsSkipped).IsEqualTo(1);
                await Assert.That(first.DeliveryRowsSuperseded).IsEqualTo(1);
                await Assert.That(first.NotificationsSuppressed).IsEqualTo(2);
                await Assert.That(first.InAppDeliveryRowsSuperseded).IsEqualTo(2);
                await Assert.That(replay).IsEqualTo(new NotificationFanoutEmailSuppressionResult(0, 0));

                EmailDispatchOutbox suppressible = await context.EmailDispatchOutbox
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == scope.SuppressibleOutboxId);
                EmailDispatchOutbox fenced = await context.EmailDispatchOutbox
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == scope.FencedOutboxId);
                await Assert.That(suppressible.Status).IsEqualTo(EmailDispatchStatus.Skipped);
                await Assert.That(fenced.Status).IsEqualTo(EmailDispatchStatus.Processing);
                await Assert.That(fenced.ProcessingLeaseToken).IsNotNull();
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task ReminderRescheduleThenSuppress_AlignsAllDeliveryLedgers()
    {
        string databasePath = DatabasePath("reminder");
        try
        {
            await CreateDatabaseAsync(databasePath);
            ReminderScope scope;
            await using (ExploreDbContext seedContext = CreateContext(databasePath))
            {
                scope = await SeedReminderScopeAsync(seedContext);
            }

            await using (ExploreDbContext context = CreateContext(databasePath))
            {
                var repository = new EmailDispatchOutboxRepository(context);
                var unitOfWork = new EfCoreUnitOfWork(context);
                DateTime changedAt = DateTime.UtcNow;
                EventReminderStateChangeResult rescheduled = await unitOfWork.ExecuteInTransactionAsync(
                    token => repository.RescheduleEventRemindersInCurrentTransactionAsync(
                        new EventReminderRescheduleRequest(
                            scope.TenantId,
                            scope.EventId,
                            scope.RegistrationOrderId,
                            scope.SessionId,
                            "Portable Event",
                            TimeSpan.FromMinutes(30),
                            changedAt,
                            "UTC"),
                        token));

                await Assert.That(rescheduled.OutboxRowsChanged).IsEqualTo(1);
                await Assert.That(rescheduled.EmailDeliveryRowsChanged).IsEqualTo(1);
                EmailDispatchOutbox rescheduledOutbox = await context.EmailDispatchOutbox
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == scope.OutboxId);
                await Assert.That(rescheduledOutbox.Status).IsEqualTo(EmailDispatchStatus.Pending);
                await Assert.That(rescheduledOutbox.Subject).IsEqualTo("Reminder: Portable Event");
                await Assert.That(rescheduledOutbox.NextAttemptAt).IsNotNull();
                await Assert.That(rescheduledOutbox.CorrelationId).StartsWith($"event-reminder:v2:{scope.SessionId:N}:");

                DateTime suppressedAt = changedAt.AddMinutes(1);
                EventReminderStateChangeResult suppressed = await unitOfWork.ExecuteInTransactionAsync(
                    token => repository.SuppressEventRemindersInCurrentTransactionAsync(
                        new EventReminderSupersessionRequest(
                            scope.TenantId,
                            scope.EventId,
                            scope.RegistrationOrderId,
                            scope.SessionId,
                            suppressedAt,
                            "event_reminder_cancelled"),
                        token));
                await Assert.That(suppressed.OutboxRowsChanged).IsEqualTo(1);
                await Assert.That(suppressed.EmailDeliveryRowsChanged).IsEqualTo(1);
                await Assert.That(suppressed.NotificationsChanged).IsEqualTo(1);
                await Assert.That(suppressed.InAppDeliveryRowsChanged).IsEqualTo(1);

                EmailDispatchOutbox persisted = await context.EmailDispatchOutbox
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == scope.OutboxId);
                NotificationIntent intent = await context.NotificationIntents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == scope.IntentId);
                NotificationDelivery[] deliveries = await context.NotificationDeliveries
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(value => value.NotificationIntentId == scope.IntentId)
                    .ToArrayAsync();
                Notification notification = await context.Notifications
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(value => value.NotificationIntentId == scope.IntentId);
                await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
                await Assert.That(persisted.ProcessingLeaseToken).IsNull();
                await Assert.That(intent.StatusId).IsEqualTo((int)NotificationIntentStatusEnum.Resolved);
                await Assert.That(deliveries.All(value => value.StatusId == (int)NotificationDeliveryStatusEnum.Superseded)).IsTrue();
                await Assert.That(notification.IsDeleted).IsTrue();
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static EmailDispatchSpecificClaimRequest ClaimRequest(SeededDispatch dispatch, Guid leaseToken) =>
        new(
            dispatch.TenantId,
            dispatch.PublishEventId,
            leaseToken,
            GlobalProcessingLimit: 10,
            TenantProcessingLimit: 5,
            OptionalReminderBacklogHighWatermark: 100,
            OptionalReminderBacklogLowWatermark: 50,
            ClaimedAt: DateTime.UtcNow);

    private static async Task CreateDatabaseAsync(string databasePath)
    {
        await using ExploreDbContext context = CreateContext(databasePath);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
    }

    private static ExploreDbContext CreateContext(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = true
        }.ToString();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance)
            .Options;
        return new ExploreDbContext(options);
    }

    private static async Task<SeededDispatch> SeedDispatchAsync(
        ExploreDbContext context,
        string suffix,
        EmailDispatchStatus status,
        Guid? existingTenantId = null)
    {
        DateTime now = DateTime.UtcNow;
        Tenant tenant;
        if (existingTenantId.HasValue)
        {
            tenant = await context.Tenants.IgnoreQueryFilters().SingleAsync(value => value.Id == existingTenantId.Value);
        }
        else
        {
            tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = $"SQLite email {suffix}",
                Slug = $"sqlite-email-{suffix}-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            context.Tenants.Add(tenant);
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"sqlite-{suffix}-{Guid.CreateVersion7():N}@example.test",
                FirstName = "SQLite",
                LastName = "Recipient"
            },
            EmailVerified = true,
            CreatedAt = now
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now,
            CreatedAt = now
        };
        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "registration.confirmed",
            DeduplicationKey = $"sqlite-dispatch:{Guid.CreateVersion7():N}",
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            CreatedAt = now
        };
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.RegistrationConfirmation,
            SourceType = "notification_intent",
            SourceId = intent.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = user.Email,
            Subject = "Registration confirmation",
            PlainTextBody = "Registration confirmed.",
            Status = status,
            MaxAttempts = 5,
            CreatedAt = now,
            UpdatedAt = now
        };
        var delivery = CreateEmailDelivery(intent, dispatch, now, NotificationDeliveryPolicyEnum.RegistrationStatusOptional);
        intent.Deliveries.Add(delivery);
        context.Users.Add(user);
        context.TenantUsers.Add(tenantUser);
        context.NotificationIntents.Add(intent);
        await context.SaveChangesAsync();
        return new SeededDispatch(tenant.Id, dispatch.Id, dispatch.PublishEventId);
    }

    private static async Task<FanoutScope> SeedFanoutScopeAsync(ExploreDbContext context)
    {
        EventScope scope = await SeedEventScopeAsync(context, "fanout");
        NotificationFanoutOccurrence occurrence = NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(), scope.TenantId, scope.EventId, null,
            DateTime.UtcNow, DateTime.UtcNow, Guid.CreateVersion7(),
            "{\"fields\":[\"title\"]}",
            "{\"title\":\"old\"}",
            "{\"title\":\"new\"}",
            "event.updated", 1,
            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional, 1,
            30, DateTime.UtcNow.AddMinutes(5), "event", scope.EventId,
            $"event:{scope.EventId:N}:update", DateTime.UtcNow.AddMinutes(5));
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();

        EmailDispatchOutbox suppressible = await SeedFanoutDispatchAsync(
            context,
            scope,
            occurrence.Id,
            "suppressible",
            EmailDispatchStatus.Pending,
            providerFenced: false);
        EmailDispatchOutbox fenced = await SeedFanoutDispatchAsync(
            context,
            scope,
            occurrence.Id,
            "fenced",
            EmailDispatchStatus.Processing,
            providerFenced: true);
        return new FanoutScope(scope.TenantId, occurrence.Id, suppressible.Id, fenced.Id);
    }

    private static async Task<EmailDispatchOutbox> SeedFanoutDispatchAsync(
        ExploreDbContext context,
        EventScope scope,
        Guid occurrenceId,
        string suffix,
        EmailDispatchStatus status,
        bool providerFenced)
    {
        DateTime now = DateTime.UtcNow;
        Guid recipientUserId = scope.UserId;
        TenantUser recipientTenantUser = scope.TenantUser;
        string recipientEmail = scope.Email;
        if (providerFenced)
        {
            recipientEmail = $"sqlite-fenced-{Guid.CreateVersion7():N}@example.test";
            var recipient = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii
                {
                    Email = recipientEmail,
                    FirstName = "Fenced",
                    LastName = "Recipient"
                },
                EmailVerified = true,
                CreatedAt = now
            };
            recipientTenantUser = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = scope.TenantId,
                Tenant = null!,
                UserId = recipient.Id,
                User = recipient,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = now,
                CreatedAt = now
            };
            recipientUserId = recipient.Id;
            context.TenantUsers.Add(recipientTenantUser);
        }

        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            CategoryId = (int)NotificationCategoryEnum.EventLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "event.updated",
            DeduplicationKey = $"fanout:{occurrenceId:N}:{suffix}",
            RecipientUserId = recipientUserId,
            RecipientTenantUser = recipientTenantUser,
            FanoutOccurrenceId = occurrenceId,
            EventId = scope.EventId,
            CreatedAt = now
        };
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.EventUpdated,
            SourceType = "notification_intent",
            SourceId = intent.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            EventId = scope.EventId,
            RecipientUserId = recipientUserId,
            RecipientTenantUser = recipientTenantUser,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = recipientEmail,
            Subject = "Event updated",
            PlainTextBody = "Event information changed.",
            Status = status,
            AttemptCount = providerFenced ? 1 : 0,
            MaxAttempts = 5,
            ProcessingStartedAt = providerFenced ? now : null,
            ProcessingLeaseToken = providerFenced ? Guid.CreateVersion7() : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        NotificationDelivery emailDelivery = CreateEmailDelivery(
            intent,
            dispatch,
            now,
            NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional);
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            Tenant = null!,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            UserId = recipientUserId,
            User = null!,
            NotificationTypeId = (int)NotificationTypeEnum.EventUpdated,
            NotificationType = null!,
            Title = "Stale event details",
            Body = "This notification is superseded.",
            DeduplicationKey = $"{intent.DeduplicationKey}:in-app",
            NotificationScopeId = (int)ActorTypeEnum.User,
            NotificationScope = null!,
            NotificationReasonId = (int)NotificationReasonEnum.System,
            CreatedAt = now
        };
        var inAppDelivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.InApp,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            PolicyVersion = 1,
            DisclosureLevel = "standard",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            NotificationId = notification.Id,
            Notification = notification,
            StatusId = (int)NotificationDeliveryStatusEnum.Pending,
            CreatedAt = now
        };
        intent.Deliveries.Add(emailDelivery);
        intent.Deliveries.Add(inAppDelivery);
        context.NotificationIntents.Add(intent);
        if (providerFenced)
        {
            context.EmailDispatchAttempts.Add(new EmailDispatchAttempt
            {
                Id = Guid.CreateVersion7(),
                TenantId = scope.TenantId,
                EmailDispatchOutboxId = dispatch.Id,
                AttemptNumber = dispatch.AttemptCount,
                Outcome = EmailDispatchAttemptOutcome.Unknown,
                StartedAt = now,
                FailureCategory = "provider_handoff_started",
                CreatedAt = now
            });
        }

        await context.SaveChangesAsync();
        return dispatch;
    }

    private static async Task<ReminderScope> SeedReminderScopeAsync(ExploreDbContext context)
    {
        EventScope scope = await SeedEventScopeAsync(context, "reminder");
        DateTime now = DateTime.UtcNow;
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            Tenant = null!,
            EventId = scope.EventId,
            Event = null!,
            Title = "Portable session",
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = now
        };
        session.Reschedule(
            UtcInstantRange.Create(new DateTimeOffset(now.AddHours(2), TimeSpan.Zero), new DateTimeOffset(now.AddHours(3), TimeSpan.Zero)),
            "UTC",
            new EventScheduleProjectionCalculator());
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(
            scope.TenantId,
            scope.EventId,
            "EUR",
            1);
        RegistrationOrder order = RegistrationOrder.Create(
            scope.TenantId,
            scope.EventId,
            scope.UserId,
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
            "EUR",
            now,
            expiresAt: null);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, now);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
        RegistrationParticipant participant = RegistrationParticipant.Create(
            scope.TenantId,
            order.Id,
            scope.UserId,
            ParticipantTypeEnum.Adult,
            null);
        var registration = new EventRegistration
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            Tenant = null!,
            EventId = scope.EventId,
            Event = null!,
            LinkedUserId = scope.UserId,
            LinkedUser = null!,
            EventSessionId = session.Id,
            EventSession = null!,
            RegistrationOrderId = order.Id,
            RegistrationOrder = order,
            RegistrationParticipantId = participant.Id,
            RegistrationParticipant = participant,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null,
            CoverageEstablishedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = now
        };
        context.AddRange(catalog, order, participant, session, registration);
        await context.SaveChangesAsync();

        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            CategoryId = (int)NotificationCategoryEnum.EventLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "event.reminder",
            DeduplicationKey = $"reminder:{order.Id:N}",
            SafePayloadReference = $"registration-order:{order.Id:N}:session:{session.Id:N}",
            RecipientUserId = scope.UserId,
            RecipientTenantUser = scope.TenantUser,
            EventId = scope.EventId,
            CreatedAt = now
        };
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.EventReminder,
            SourceType = "notification_intent",
            SourceId = intent.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            EventId = scope.EventId,
            RegistrationOrderId = order.Id,
            RecipientUserId = scope.UserId,
            RecipientTenantUser = scope.TenantUser,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = scope.Email,
            Subject = "Old reminder",
            PlainTextBody = "Old reminder details.",
            Status = EmailDispatchStatus.RetryScheduled,
            NextAttemptAt = now.AddMinutes(5),
            MaxAttempts = 5,
            CreatedAt = now,
            UpdatedAt = now
        };
        NotificationDelivery emailDelivery = CreateEmailDelivery(
            intent,
            dispatch,
            now,
            NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional);
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            Tenant = null!,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            UserId = scope.UserId,
            User = null!,
            NotificationTypeId = (int)NotificationTypeEnum.EventUpdated,
            NotificationType = null!,
            Title = "Old reminder",
            Body = "Old reminder details.",
            DeduplicationKey = $"{intent.DeduplicationKey}:in-app",
            NotificationScopeId = (int)ActorTypeEnum.User,
            NotificationScope = null!,
            NotificationReasonId = (int)NotificationReasonEnum.System,
            CreatedAt = now
        };
        var inAppDelivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.InApp,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            PolicyVersion = 1,
            DisclosureLevel = "standard",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            NotificationId = notification.Id,
            Notification = notification,
            StatusId = (int)NotificationDeliveryStatusEnum.Queued,
            CreatedAt = now
        };
        intent.Deliveries.Add(emailDelivery);
        intent.Deliveries.Add(inAppDelivery);
        context.NotificationIntents.Add(intent);
        await context.SaveChangesAsync();
        return new ReminderScope(
            scope.TenantId,
            scope.EventId,
            order.Id,
            session.Id,
            dispatch.Id,
            intent.Id);
    }

    private static NotificationDelivery CreateEmailDelivery(
        NotificationIntent intent,
        EmailDispatchOutbox dispatch,
        DateTime createdAt,
        NotificationDeliveryPolicyEnum policy) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = intent.TenantId,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            DeliveryPolicyId = (int)policy,
            PolicyVersion = 1,
            PreferenceCategoryCode = "event-updates",
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            DisclosureLevel = "standard",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            EmailDispatchOutboxId = dispatch.Id,
            EmailDispatchOutbox = dispatch,
            StatusId = (int)NotificationDeliveryStatusEnum.Queued,
            ProviderStatus = "queued",
            QueuedAt = createdAt,
            CreatedAt = createdAt
        };

    private static async Task<EventScope> SeedEventScopeAsync(ExploreDbContext context, string suffix)
    {
        DateTime now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"SQLite {suffix}",
            Slug = $"sqlite-{suffix}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var servicePrincipal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"sqlite-email-{Guid.CreateVersion7():N}",
            DisplayName = "SQLite email source",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Bot,
            ActorType = null!,
            ServicePrincipalId = servicePrincipal.Id,
            ServicePrincipal = servicePrincipal,
            Pii = new ActorPii { DisplayName = "SQLite email source" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var eventRow = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Portable Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = actor,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventTimeZoneId = "UTC",
            Timezone = "UTC",
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = now
        };
        string email = $"sqlite-{suffix}-{Guid.CreateVersion7():N}@example.test";
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = email,
                FirstName = "SQLite",
                LastName = "Recipient"
            },
            EmailVerified = true,
            CreatedAt = now
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now,
            CreatedAt = now
        };
        context.AddRange(tenant, actor, eventRow, user, tenantUser);
        await context.SaveChangesAsync();
        return new EventScope(tenant.Id, eventRow.Id, user.Id, tenantUser, email);
    }

    private static string DatabasePath(string suffix) => Path.Combine(
        Path.GetTempPath(),
        $"email-dispatch-repositories-{suffix}-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-shm");
        File.Delete(databasePath + "-wal");
    }

    private sealed record SeededDispatch(Guid TenantId, Guid OutboxId, Guid PublishEventId);
    private sealed record FanoutScope(
        Guid TenantId,
        Guid OccurrenceId,
        Guid SuppressibleOutboxId,
        Guid FencedOutboxId);
    private sealed record ReminderScope(
        Guid TenantId,
        Guid EventId,
        Guid RegistrationOrderId,
        Guid SessionId,
        Guid OutboxId,
        Guid IntentId);
    private sealed record EventScope(
        Guid TenantId,
        Guid EventId,
        Guid UserId,
        TenantUser TenantUser,
        string Email);
}
