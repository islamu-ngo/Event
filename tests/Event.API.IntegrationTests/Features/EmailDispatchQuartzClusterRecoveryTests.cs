// ABOUTME: Specifies clustered EmailDispatch recovery when transport accepts before local settlement is lost.
// ABOUTME: Uses PostgreSQL Quartz nodes and event barriers to prove ambiguity, fencing, privacy, and no resend.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Configuration;
using Explore.API.Scheduling;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[Category(TestCategories.Email)]
[NotInParallel(SchedulerProofConstraints.LiveScheduler)]
[ClassDataSource<QuartzPostgreSqlSchedulerFixture>(Shared = SharedType.PerAssembly)]
public sealed class EmailDispatchQuartzClusterRecoveryTests(QuartzPostgreSqlSchedulerFixture fixture)
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(60);

    [Test]
    public async Task AcceptedTransportWithLostLocalSettlementBecomesUnknownWithoutAutomaticResend()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await EnsureApplicationSchemaAsync();
        await fixture.EnsureSchedulerSchemaAsync();

        DispatchIdentity dispatch = await SeedPendingDispatchAsync();
        var coordinator = new ClusterRecoveryCoordinator();
        var transport = new AcceptedTransport(coordinator);
        var logs = new PayloadFreeLogSink();
        using var telemetry = new PayloadFreeMetricSink();
        telemetry.Start();

        string clusterName = $"email-recovery-{Guid.CreateVersion7():N}";
        JobKey jobKey = new($"email-dispatch-{Guid.CreateVersion7():N}", "tests");
        JobKey recoveryJobKey = new($"email-recovery-{Guid.CreateVersion7():N}", "tests");
        JobKey secondJobKey = new($"email-dispatch-{Guid.CreateVersion7():N}", "tests");
        await using ServiceProvider firstNode = BuildClusteredNode(clusterName, coordinator, transport, logs);
        await using ServiceProvider secondNode = BuildClusteredNode(clusterName, coordinator, transport, logs);
        IScheduler firstScheduler = await firstNode.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IScheduler secondScheduler = await secondNode.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Task accepted = WaitForTransportAcceptanceAsync(coordinator);
        await firstScheduler.Start();
        await secondScheduler.Start();
        try
        {
            await firstScheduler.AddJob(
                JobBuilder.Create<EmailDispatchDrainJob>().WithIdentity(jobKey).StoreDurably().Build(),
                replace: false);
            await firstScheduler.ScheduleJob(CreateEmptyTrigger(jobKey, "accepted"));

            await accepted;

            await AssertRefusalProbesAsync(dispatch);
            coordinator.ReleaseTransportReturn();
            await coordinator.SettlementEntered.Task.WaitAsync(SignalTimeout);
            coordinator.ReleaseSettlementLoss();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.JobFailed.Task.WaitAsync(SignalTimeout));

            await secondScheduler.AddJob(
                JobBuilder.Create<EmailDispatchRecoveryScanJob>().WithIdentity(recoveryJobKey).StoreDurably().Build(),
                replace: false);
            await secondScheduler.ScheduleJob(CreateEmptyTrigger(recoveryJobKey, "recovery"));
            await coordinator.RecoveryCompleted.Task.WaitAsync(SignalTimeout);

            await secondScheduler.AddJob(
                JobBuilder.Create<EmailDispatchDrainJob>().WithIdentity(secondJobKey).StoreDurably().Build(),
                replace: false);
            Task secondPassCompleted = coordinator.WaitForCompletedPassCountAsync(1, SignalTimeout);
            await secondScheduler.ScheduleJob(CreateEmptyTrigger(secondJobKey, "no-resend"));
            await secondPassCompleted;
            await AssertCancellationRecoveryAsync(dispatch);

            IJobDetail? persistedJob = await secondScheduler.GetJobDetail(jobKey);
            EmailDispatchStatus durableStatus = await ReadStatusAsync(dispatch.OutboxId);

            await Assert.That(transport.CallCount).IsEqualTo(1)
                .Because("a second clustered pass must not resend a provider-accepted fenced row.");
            await Assert.That(persistedJob).IsNotNull();
            await Assert.That(persistedJob!.JobDataMap.Count).IsEqualTo(0)
                .Because("the durable Quartz row is cadence only and must contain no email or tenant payload.");
            await Assert.That(logs.ContainsAny(dispatch.SensitiveCanaries)).IsFalse();
            await Assert.That(telemetry.ContainsAny(dispatch.SensitiveCanaries)).IsFalse();

            await Assert.That(durableStatus).IsEqualTo(EmailDispatchStatus.Unknown)
                .Because("accepted transport with lost local settlement must be durably ambiguous before another node can resend it.");
        }
        finally
        {
            coordinator.ReleaseTransportReturn();
            coordinator.ReleaseSettlementLoss();
            await firstScheduler.Shutdown(waitForJobsToComplete: false);
            await secondScheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static async Task WaitForTransportAcceptanceAsync(ClusterRecoveryCoordinator coordinator)
    {
        Task completed = await Task.WhenAny(
            coordinator.TransportAccepted.Task,
            coordinator.JobFailed.Task,
            Task.Delay(SignalTimeout));
        if (completed == coordinator.JobFailed.Task)
        {
            await coordinator.JobFailed.Task;
        }

        if (completed != coordinator.TransportAccepted.Task)
        {
            throw new TimeoutException("The clustered email drain did not reach transport acceptance.");
        }
    }

    private async Task EnsureApplicationSchemaAsync()
    {
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "CREATE SCHEMA IF NOT EXISTS islamu_event";
            await command.ExecuteNonQueryAsync();
        }

        await using ExploreDbContext context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
    }

    private async Task<DispatchIdentity> SeedPendingDispatchAsync()
    {
        await using ExploreDbContext context = CreateDbContext();
        DateTime now = DateTime.UtcNow;
        string email = $"cluster-{Guid.CreateVersion7():N}@example.test";
        string subject = $"subject-{Guid.CreateVersion7():N}";
        string body = $"body-{Guid.CreateVersion7():N}";
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Cluster recovery tenant",
            Slug = $"cluster-recovery-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = email, FirstName = "Cluster", LastName = "Recipient" },
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
            DeduplicationKey = $"cluster-recovery:{Guid.CreateVersion7():N}",
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            CreatedAt = now
        };

        context.Tenants.Add(tenant);
        context.Users.Add(user);
        context.TenantUsers.Add(tenantUser);
        context.NotificationIntents.Add(intent);
        await context.SaveChangesAsync();

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
            RecipientEmail = email,
            Subject = subject,
            PlainTextBody = body,
            Status = EmailDispatchStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = 5,
            CreatedAt = now,
            UpdatedAt = now
        };
        var delivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
            IsRequired = false,
            PolicyVersion = 1,
            PreferenceCategoryCode = NotificationPreferenceCategoryCodes.RegistrationStatus,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            DisclosureLevel = "standard",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            LinkAllowed = false,
            EmailDispatchOutboxId = dispatch.Id,
            EmailDispatchOutbox = dispatch,
            StatusId = (int)NotificationDeliveryStatusEnum.Queued,
            QueuedAt = now,
            CreatedAt = now
        };
        intent.Deliveries.Add(delivery);

        context.EmailDispatchOutbox.Add(dispatch);
        context.NotificationDeliveries.Add(delivery);
        await context.SaveChangesAsync();

        return new DispatchIdentity(
            tenant.Id,
            dispatch.Id,
            [tenant.Id.ToString(), email, subject, body]);
    }

    private async Task AssertRefusalProbesAsync(DispatchIdentity dispatch)
    {
        await using ExploreDbContext context = CreateDbContext();
        EmailDispatchOutbox row = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == dispatch.OutboxId);
        var repository = new EmailDispatchOutboxRepository(context);
        Guid activeLease = row.ProcessingLeaseToken!.Value;

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SettleProviderAccepted(
            new EmailDispatchAcceptedSettlement(Guid.CreateVersion7(), row.Id, activeLease, row.AttemptCount, DateTime.UtcNow, null),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SettleProviderAccepted(
            new EmailDispatchAcceptedSettlement(Guid.Empty, row.Id, activeLease, row.AttemptCount, DateTime.UtcNow, null),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SettleProviderAccepted(
            new EmailDispatchAcceptedSettlement(dispatch.TenantId, row.Id, Guid.CreateVersion7(), row.AttemptCount, DateTime.UtcNow, null),
            CancellationToken.None));

    }

    private async Task AssertCancellationRecoveryAsync(DispatchIdentity dispatch)
    {
        await using ExploreDbContext context = CreateDbContext();
        var repository = new EmailDispatchOutboxRepository(context);
        var cancellationRow = await SeedCancellationDispatchAsync(context, dispatch.TenantId);
        Guid cancellationLease = Guid.CreateVersion7();
        EmailDispatchOutbox? claimed = await repository.TryClaimSpecificAsync(
            new EmailDispatchSpecificClaimRequest(
                dispatch.TenantId,
                cancellationRow.PublishEventId,
                cancellationLease,
                20,
                5,
                100,
                50,
                DateTime.UtcNow),
            CancellationToken.None);
        await Assert.That(claimed).IsNotNull();
        EmailDispatchPreHandoffReleaseOutcome released = await repository.ReleaseClaimBeforeProviderHandoff(
            new EmailDispatchPreHandoffRelease(
                dispatch.TenantId,
                cancellationRow.Id,
                cancellationLease,
                claimed!.AttemptCount,
                DateTime.UtcNow,
                "processing_cancelled_before_handoff",
                "Cancelled before provider handoff; safe to retry."),
            CancellationToken.None);
        await Assert.That(released).IsEqualTo(EmailDispatchPreHandoffReleaseOutcome.Released);
    }

    private static async Task<EmailDispatchOutbox> SeedCancellationDispatchAsync(ExploreDbContext context, Guid tenantId)
    {
        EmailDispatchOutbox template = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking().FirstAsync();
        NotificationIntent templateIntent = await context.NotificationIntents.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == template.NotificationIntentId);
        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            CategoryId = templateIntent.CategoryId,
            OwnershipTypeId = templateIntent.OwnershipTypeId,
            RecipientKindId = templateIntent.RecipientKindId,
            StatusId = templateIntent.StatusId,
            TemplateKey = templateIntent.TemplateKey,
            DeduplicationKey = $"cluster-cancellation:{Guid.CreateVersion7():N}",
            RecipientUserId = template.RecipientUserId,
            CreatedAt = DateTime.UtcNow
        };
        context.NotificationIntents.Add(intent);
        await context.SaveChangesAsync();

        var row = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            PublishEventId = Guid.CreateVersion7(),
            Kind = template.Kind,
            SourceType = "cancellation_probe",
            SourceId = Guid.CreateVersion7(),
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            RecipientUserId = template.RecipientUserId,
            RecipientAddressSource = template.RecipientAddressSource,
            RecipientEmail = template.RecipientEmail,
            Subject = template.Subject,
            PlainTextBody = template.PlainTextBody,
            Status = EmailDispatchStatus.Pending,
            MaxAttempts = 5,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.EmailDispatchOutbox.Add(row);
        await context.SaveChangesAsync();
        return row;
    }

    private async Task<EmailDispatchStatus> ReadStatusAsync(Guid outboxId)
    {
        await using ExploreDbContext context = CreateDbContext();
        return await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == outboxId)
            .Select(row => row.Status)
            .SingleAsync();
    }

    private string ApplicationConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                SearchPath = "islamu_event"
            };
            return builder.ConnectionString;
        }
    }

    private ExploreDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql(ApplicationConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    private ServiceProvider BuildClusteredNode(
        string clusterName,
        ClusterRecoveryCoordinator coordinator,
        AcceptedTransport transport,
        PayloadFreeLogSink logs)
    {
        var services = new ServiceCollection();
        services.AddSchedulerProofLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddMetrics();
        services.AddSingleton<BusinessMetrics>();
        services.AddSingleton(Options.Create(new EmailDispatchProcessorSettings
        {
            BatchSize = 10,
            MaxConcurrentDispatches = 1,
            MaxConcurrentDispatchesPerTenant = 1,
            GlobalSmtpRateLimitPerMinute = 100,
            TenantSmtpRateLimitPerMinute = 100,
            ConsumerId = "cluster-recovery-test"
        }));
        services.AddPostgreSqlExploreDbContext(ApplicationConnectionString);
        services.AddScoped<EmailDispatchOutboxRepository>();
        services.AddScoped<IEmailDispatchOutboxRepository>(provider =>
            new SettlementLossRepository(provider.GetRequiredService<EmailDispatchOutboxRepository>(), coordinator));
        services.AddScoped<INotificationPreferenceResolver, NotificationPreferenceResolver>();
        services.AddScoped<NotificationDeliveryPolicyResolver>();
        services.AddScoped<IEmailDispatchEligibilityEvaluator, EmailDispatchEligibilityEvaluator>();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<IEmailUnsubscribeTokenService, NoOpUnsubscribeTokenService>();
        services.AddSingleton<IEmailService>(transport);
        services.AddSingleton<ILogger<EmailDispatchDrainJob>>(logs.Create<EmailDispatchDrainJob>());
        services.AddSingleton<IEmailDispatchDrainService, ClusterCrashDrainService>();
        services.AddSingleton(coordinator);
        services.AddQuartz(quartz =>
        {
            quartz.SchedulerName = clusterName;
            quartz.SchedulerId = QuartzSchedulerSettings.AutoInstanceId;
            quartz.UseDefaultThreadPool(1);
            quartz.SetProperty("quartz.scheduler.idleWaitTime", "1000");
            quartz.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseSystemTextJsonSerializer();
                store.PerformSchemaValidation = true;
                store.UsePostgres(
                    ado =>
                    {
                        ado.ConnectionString = fixture.ConnectionString;
                        ado.TablePrefix = QuartzPostgreSqlSchedulerFixture.TablePrefix;
                    },
                    dataSourceName: $"{clusterName}-{Guid.CreateVersion7():N}");
                store.UseClustering(clustering => clustering.CheckinInterval = TimeSpan.FromSeconds(1));
            });
        });
        return services.BuildServiceProvider();
    }

    private static ITrigger CreateEmptyTrigger(JobKey jobKey, string suffix) => TriggerBuilder.Create()
        .WithIdentity($"{jobKey.Name}-{suffix}", jobKey.Group)
        .ForJob(jobKey)
        .StartAt(DateTimeOffset.UtcNow.AddSeconds(2))
        .Build();

    private sealed record DispatchIdentity(Guid TenantId, Guid OutboxId, string[] SensitiveCanaries);

    private sealed class AcceptedTransport(ClusterRecoveryCoordinator coordinator) : IEmailService
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            coordinator.TransportAccepted.TrySetResult();
            await coordinator.TransportReturnReleased.Task.WaitAsync(SignalTimeout, cancellationToken);
            return EmailResult.Ok("accepted");
        }
    }

    private sealed class ClusterCrashDrainService(
        IServiceScopeFactory scopeFactory,
        IEmailService transport,
        ClusterRecoveryCoordinator coordinator) : IEmailDispatchDrainService
    {
        public async Task<EmailDispatchDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await ProcessBatchCoreAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                coordinator.JobFailed.TrySetException(exception);
                throw;
            }
        }

        private async Task<EmailDispatchDrainResult> ProcessBatchCoreAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IEmailDispatchOutboxRepository repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
            IReadOnlyList<EmailDispatchOutbox> claimed = await repository.ClaimPendingBatchAsync(
                new EmailDispatchBatchClaimRequest(Guid.CreateVersion7(), 10, 5, 1, 1, 100, 50, DateTime.UtcNow),
                cancellationToken);
            if (claimed.Count == 0)
            {
                coordinator.RecordCompletedPass();
                return new EmailDispatchDrainResult();
            }

            EmailDispatchOutbox dispatch = claimed.Single();
            IEmailDispatchEligibilityEvaluator eligibility = scope.ServiceProvider.GetRequiredService<IEmailDispatchEligibilityEvaluator>();
            EmailDispatchEligibilityResult admitted = await eligibility.EvaluateAndBeginProviderHandoffAsync(
                new EmailDispatchEligibilityRequest(
                    dispatch.TenantId,
                    dispatch.Id,
                    dispatch.ProcessingLeaseToken!.Value,
                    dispatch.AttemptCount,
                    100,
                    100,
                    "cluster-recovery-test",
                    DateTime.UtcNow),
                cancellationToken);
            if (admitted.Outcome != EmailDispatchEligibilityOutcome.Eligible)
            {
                throw new InvalidOperationException($"The real provider-handoff gate returned {admitted.Outcome}.");
            }

            await transport.SendAsync(new EmailMessage
            {
                To = admitted.RecipientEmail!,
                Subject = dispatch.Subject,
                PlainTextBody = dispatch.PlainTextBody
            }, cancellationToken);
            await repository.SettleProviderAccepted(
                new EmailDispatchAcceptedSettlement(
                    dispatch.TenantId,
                    dispatch.Id,
                    dispatch.ProcessingLeaseToken.Value,
                    admitted.AttemptNumber!.Value,
                    DateTime.UtcNow,
                    null),
                cancellationToken);
            coordinator.RecordCompletedPass();
            return new EmailDispatchDrainResult { PendingCount = 1, ProcessedCount = 1, SentCount = 1 };
        }

        public async Task<EmailDispatchRecoveryResult> RecoverStaleProcessingAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IEmailDispatchOutboxRepository repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
            DateTime recoveredAt = DateTime.UtcNow;
            EmailDispatchStaleRecoveryResult result = await repository.RecoverStaleProcessing(
                new EmailDispatchStaleRecoveryRequest(
                    recoveredAt,
                    recoveredAt,
                    "processing_lease_released",
                    "Email dispatch processing lease expired before provider handoff and was released for retry.",
                    "processing_lease_expired",
                    "Email dispatch processing lease expired after provider handoff and requires reconciliation.",
                    10),
                cancellationToken);
            coordinator.RecoveryCompleted.TrySetResult();
            return new EmailDispatchRecoveryResult(result.RecoveredCount, recoveredAt);
        }

        public Task<EmailDispatchSingleDrainResult> ProcessSingleAsync(Guid tenantId, Guid publishEventId, string consumerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class SettlementLossRepository(
        EmailDispatchOutboxRepository inner,
        ClusterRecoveryCoordinator coordinator) : IEmailDispatchOutboxRepository
    {
        public async Task SettleProviderAccepted(EmailDispatchAcceptedSettlement settlement, CancellationToken cancellationToken)
        {
            coordinator.SettlementEntered.TrySetResult();
            await coordinator.SettlementLossReleased.Task.WaitAsync(SignalTimeout, cancellationToken);
            throw new InvalidOperationException("simulated local settlement loss after accepted transport");
        }

        public Task<EmailDispatchOutbox> Create(EmailDispatchOutbox entity, CancellationToken cancellationToken) => inner.Create(entity, cancellationToken);
        public Task<EmailDispatchTenantControl?> GetTenantControl(Guid tenantId, CancellationToken cancellationToken) => inner.GetTenantControl(tenantId, cancellationToken);
        public Task<IReadOnlyList<EmailDispatchOutbox>> ClaimPendingBatchAsync(EmailDispatchBatchClaimRequest request, CancellationToken cancellationToken) => inner.ClaimPendingBatchAsync(request, cancellationToken);
        public Task<EmailDispatchOutbox?> TryClaimSpecificAsync(EmailDispatchSpecificClaimRequest request, CancellationToken cancellationToken) => inner.TryClaimSpecificAsync(request, cancellationToken);
        public Task<EventReminderStateChangeResult> SuppressEventRemindersInCurrentTransactionAsync(EventReminderSupersessionRequest request, CancellationToken cancellationToken) => inner.SuppressEventRemindersInCurrentTransactionAsync(request, cancellationToken);
        public Task<EventReminderStateChangeResult> RescheduleEventRemindersInCurrentTransactionAsync(EventReminderRescheduleRequest request, CancellationToken cancellationToken) => inner.RescheduleEventRemindersInCurrentTransactionAsync(request, cancellationToken);
        public Task<IReadOnlyList<EmailDispatchOutbox>> GetRabbitMqPublishBatch(int batchSize, DateTime now, DateTime retryAttemptsBefore, CancellationToken cancellationToken) => inner.GetRabbitMqPublishBatch(batchSize, now, retryAttemptsBefore, cancellationToken);
        public Task<int> CountDueDispatchAsync(DateTime now, CancellationToken cancellationToken) => inner.CountDueDispatchAsync(now, cancellationToken);
        public Task<DateTime?> GetOldestDueCreatedAtAsync(DateTime now, CancellationToken cancellationToken) => inner.GetOldestDueCreatedAtAsync(now, cancellationToken);
        public Task<IReadOnlyDictionary<Guid, int>> CountDueDispatchByTenantAsync(DateTime now, int tenantLimit, CancellationToken cancellationToken) => inner.CountDueDispatchByTenantAsync(now, tenantLimit, cancellationToken);
        public Task<int> CountRetryScheduledAsync(CancellationToken cancellationToken) => inner.CountRetryScheduledAsync(cancellationToken);
        public Task<int> CountStaleProcessingAsync(DateTime processingStartedBefore, CancellationToken cancellationToken) => inner.CountStaleProcessingAsync(processingStartedBefore, cancellationToken);
        public Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken) => inner.CountDeadLetteredAsync(cancellationToken);
        public Task<int> CountUnknownAsync(CancellationToken cancellationToken) => inner.CountUnknownAsync(cancellationToken);
        public Task<int> CountParkedAsync(CancellationToken cancellationToken) => inner.CountParkedAsync(cancellationToken);
        public Task<bool> IsOptionalReminderDeferralActiveAsync(CancellationToken cancellationToken) => inner.IsOptionalReminderDeferralActiveAsync(cancellationToken);
        public Task<EmailDispatchProcessorState?> GetProcessorState(CancellationToken cancellationToken) => inner.GetProcessorState(cancellationToken);
        public Task<EmailDispatchProcessorState> SetProcessorPauseState(bool isPaused, string? pauseReason, Guid? changedBy, DateTime changedAt, CancellationToken cancellationToken) => inner.SetProcessorPauseState(isPaused, pauseReason, changedBy, changedAt, cancellationToken);
        public Task<EmailDispatchProcessorState> SetGlobalSmtpRateLimitOverride(int? rateLimitPerMinute, Guid? changedBy, DateTime changedAt, CancellationToken cancellationToken) => inner.SetGlobalSmtpRateLimitOverride(rateLimitPerMinute, changedBy, changedAt, cancellationToken);
        public Task<IReadOnlyList<EmailDispatchOutbox>> GetStatusRows(Guid tenantId, int limit, CancellationToken cancellationToken) => inner.GetStatusRows(tenantId, limit, cancellationToken);
        public Task<EmailDispatchOutbox?> GetByTenantAndId(Guid tenantId, Guid outboxId, CancellationToken cancellationToken) => inner.GetByTenantAndId(tenantId, outboxId, cancellationToken);
        public Task<EmailDispatchOutbox?> GetByTenantAndPublishEventId(Guid tenantId, Guid publishEventId, CancellationToken cancellationToken) => inner.GetByTenantAndPublishEventId(tenantId, publishEventId, cancellationToken);
        public Task<bool> IsTenantPaused(Guid tenantId, CancellationToken cancellationToken) => inner.IsTenantPaused(tenantId, cancellationToken);
        public Task<EmailDispatchTenantControl> SetTenantPauseState(Guid tenantId, bool isPaused, string? pauseReason, Guid? changedBy, DateTime changedAt, CancellationToken cancellationToken) => inner.SetTenantPauseState(tenantId, isPaused, pauseReason, changedBy, changedAt, cancellationToken);
        public Task<bool> TryParkForOperator(Guid tenantId, Guid outboxId, string reason, Guid? changedBy, DateTime parkedAt, CancellationToken cancellationToken) => inner.TryParkForOperator(tenantId, outboxId, reason, changedBy, parkedAt, cancellationToken);
        public Task<bool> TryReplayForOperator(Guid tenantId, Guid outboxId, Guid? changedBy, DateTime replayAt, CancellationToken cancellationToken) => inner.TryReplayForOperator(tenantId, outboxId, changedBy, replayAt, cancellationToken);
        public Task<bool> TryResolveWithoutReplay(Guid tenantId, Guid outboxId, string reason, Guid? changedBy, DateTime resolvedAt, CancellationToken cancellationToken) => inner.TryResolveWithoutReplay(tenantId, outboxId, reason, changedBy, resolvedAt, cancellationToken);
        public Task<bool> TryReconcileUnknown(Guid tenantId, Guid outboxId, EmailDispatchUnknownReconciliationOutcome outcome, string reason, string? providerMessageId, Guid? changedBy, DateTime reconciledAt, CancellationToken cancellationToken) => inner.TryReconcileUnknown(tenantId, outboxId, outcome, reason, providerMessageId, changedBy, reconciledAt, cancellationToken);
        public Task<IReadOnlyList<Guid>> GetRetentionTenantIds(DateTime cutoffUtc, int maxTenants, CancellationToken cancellationToken) => inner.GetRetentionTenantIds(cutoffUtc, maxTenants, cancellationToken);
        public Task<int> CountRetentionRedactionEligible(Guid tenantId, DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) => inner.CountRetentionRedactionEligible(tenantId, cutoffUtc, batchSize, cancellationToken);
        public Task<int> RedactRetentionEligible(Guid tenantId, DateTime cutoffUtc, DateTime redactedAt, int batchSize, CancellationToken cancellationToken) => inner.RedactRetentionEligible(tenantId, cutoffUtc, redactedAt, batchSize, cancellationToken);
        public Task<int> SuppressAndRedactTenant(Guid tenantId, Guid? changedBy, DateTime redactedAt, CancellationToken cancellationToken) => inner.SuppressAndRedactTenant(tenantId, changedBy, redactedAt, cancellationToken);
        public Task<EmailDispatchStaleRecoveryResult> RecoverStaleProcessing(EmailDispatchStaleRecoveryRequest request, CancellationToken cancellationToken) => inner.RecoverStaleProcessing(request, cancellationToken);
        public Task<EmailDispatchPreHandoffReleaseOutcome> ReleaseClaimBeforeProviderHandoff(EmailDispatchPreHandoffRelease request, CancellationToken cancellationToken) => inner.ReleaseClaimBeforeProviderHandoff(request, cancellationToken);
        public Task MarkRabbitMqPublishSucceeded(Guid id, DateTime publishedAt, CancellationToken cancellationToken) => inner.MarkRabbitMqPublishSucceeded(id, publishedAt, cancellationToken);
        public Task MarkRabbitMqPublishFailed(Guid id, string failureCategory, DateTime attemptedAt, CancellationToken cancellationToken) => inner.MarkRabbitMqPublishFailed(id, failureCategory, attemptedAt, cancellationToken);
        public Task<EmailDispatchFailureSettlementOutcome> SettleProviderFailure(EmailDispatchFailureSettlement settlement, CancellationToken cancellationToken) => inner.SettleProviderFailure(settlement, cancellationToken);
        public Task<EmailDispatchAcceptedReconciliationOutcome> ReconcileProviderAccepted(EmailDispatchAcceptedSettlement settlement, CancellationToken cancellationToken) => inner.ReconcileProviderAccepted(settlement, cancellationToken);
    }

    private sealed class ClusterRecoveryCoordinator
    {
        private readonly object _gate = new();
        private readonly List<(int Expected, TaskCompletionSource Signal)> _passWaiters = [];
        private int _completedPasses;

        public TaskCompletionSource TransportAccepted { get; } = NewSignal();
        public TaskCompletionSource JobFailed { get; } = NewSignal();
        public TaskCompletionSource TransportReturnReleased { get; } = NewSignal();
        public TaskCompletionSource SettlementEntered { get; } = NewSignal();
        public TaskCompletionSource SettlementLossReleased { get; } = NewSignal();
        public TaskCompletionSource RecoveryCompleted { get; } = NewSignal();

        public void ReleaseTransportReturn() => TransportReturnReleased.TrySetResult();
        public void ReleaseSettlementLoss() => SettlementLossReleased.TrySetResult();

        public void RecordCompletedPass()
        {
            lock (_gate)
            {
                _completedPasses++;
                foreach ((int expected, TaskCompletionSource signal) in _passWaiters.Where(waiter => _completedPasses >= waiter.Expected))
                {
                    signal.TrySetResult();
                }
            }
        }

        public Task WaitForCompletedPassCountAsync(int additionalPasses, TimeSpan timeout)
        {
            TaskCompletionSource signal;
            lock (_gate)
            {
                int expected = _completedPasses + additionalPasses;
                signal = NewSignal();
                _passWaiters.Add((expected, signal));
            }

            return signal.Task.WaitAsync(timeout);
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class NoOpUnsubscribeTokenService : IEmailUnsubscribeTokenService
    {
        public string GenerateToken(EmailUnsubscribeTokenPayload payload, TimeSpan? lifetime = null) => string.Empty;
        public EmailUnsubscribeTokenValidationResult ValidateToken(string? token) => new(false, null, "not_used");
    }

    private sealed class PayloadFreeLogSink
    {
        private readonly ConcurrentQueue<string> _messages = new();
        public ILogger<T> Create<T>() => new CapturingLogger<T>(_messages);
        public bool ContainsAny(IEnumerable<string> canaries) =>
            _messages.Any(message => canaries.Any(canary => message.Contains(canary, StringComparison.Ordinal)));

        private sealed class CapturingLogger<T>(ConcurrentQueue<string> messages) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    private sealed class PayloadFreeMetricSink : IDisposable
    {
        private readonly ConcurrentQueue<string> _values = new();
        private readonly MeterListener _listener = new();

        public void Start()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BusinessMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) => Capture(measurement, tags));
            _listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) => Capture(measurement, tags));
            _listener.Start();
        }

        public bool ContainsAny(IEnumerable<string> canaries) =>
            _values.Any(value => canaries.Any(canary => value.Contains(canary, StringComparison.Ordinal)));

        private void Capture<T>(T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            _values.Enqueue(measurement?.ToString() ?? string.Empty);
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                _values.Enqueue(tag.Key);
                _values.Enqueue(tag.Value?.ToString() ?? string.Empty);
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
