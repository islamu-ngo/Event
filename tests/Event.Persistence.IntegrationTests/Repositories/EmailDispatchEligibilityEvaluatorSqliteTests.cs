// ABOUTME: File-backed SQLite regressions for atomic email dispatch eligibility and SMTP rate admission.
// ABOUTME: Proves concurrent evaluators reserve one global token without executing PostgreSQL-only SQL.

using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema.ProviderPrimitives;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDispatchEligibilityEvaluatorSqliteTests
{
    [Test]
    [Arguments(RelationalNamedLock.PostgreSqlProvider, "SELECT clock_timestamp() AS \"Value\"")]
    [Arguments(RelationalNamedLock.SqliteProvider, "SELECT CURRENT_TIMESTAMP AS \"Value\"")]
    [Arguments(RelationalNamedLock.SqlServerProvider, "SELECT SYSUTCDATETIME() AS [Value]")]
    [Arguments(RelationalNamedLock.MySqlProvider, "SELECT UTC_TIMESTAMP(6) AS `Value`")]
    public async Task DatabaseUtcClockSelector_UsesTheProviderNativeUtcClock(
        string providerName,
        string expectedSql)
    {
        await Assert.That(RelationalDatabaseClock.SelectUtcNowSql(providerName))
            .IsEqualTo(expectedSql);
    }

    [Test]
    public async Task ConcurrentGlobalRateAdmission_AllowsExactlyOneProviderHandoff()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"email-dispatch-eligibility-{Guid.CreateVersion7():N}.db");

        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch[] dispatches;
            await using (ExploreDbContext seedContext = CreateContext(databasePath))
            {
                dispatches =
                [
                    await SeedProcessingDispatchAsync(seedContext, "first"),
                    await SeedProcessingDispatchAsync(seedContext, "second")
                ];
            }

            ExploreDbContext[] contexts =
            [CreateContext(databasePath), CreateContext(databasePath)];
            try
            {
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<EmailDispatchEligibilityResult>[] attempts = contexts
                    .Select(async (context, index) =>
                    {
                        await start.Task;
                        SeededDispatch seeded = dispatches[index];
                        return await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
                            new EmailDispatchEligibilityRequest(
                                seeded.TenantId,
                                seeded.OutboxId,
                                seeded.LeaseToken,
                                AttemptNumber: 0,
                                GlobalSmtpRateLimitPerMinute: 1,
                                TenantSmtpRateLimitPerMinute: 1,
                                ConsumerId: $"sqlite-worker-{index}",
                                EvaluatedAt: DateTime.UtcNow),
                            CancellationToken.None);
                    })
                    .ToArray();

                start.SetResult();
                EmailDispatchEligibilityResult[] results = await Task.WhenAll(attempts)
                    .WaitAsync(TimeSpan.FromSeconds(15));

                await Assert.That(results.Count(result => result.Outcome == EmailDispatchEligibilityOutcome.Eligible))
                    .IsEqualTo(1);
                await Assert.That(results.Count(result => result.Outcome == EmailDispatchEligibilityOutcome.RateDeferred))
                    .IsEqualTo(1);
            }
            finally
            {
                foreach (ExploreDbContext context in contexts)
                {
                    await context.DisposeAsync();
                }
            }

            await using (ExploreDbContext assertContext = CreateContext(databasePath))
            {
                EmailDispatchOutbox[] persisted = await assertContext.EmailDispatchOutbox
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(row => dispatches.Select(value => value.OutboxId).Contains(row.Id))
                    .ToArrayAsync();
                EmailDispatchProcessorState processor = await assertContext.EmailDispatchProcessorStates
                    .AsNoTracking()
                    .SingleAsync(row => row.ProcessorCode == "smtp");

                await Assert.That(persisted.Count(row => row.Status == EmailDispatchStatus.Processing))
                    .IsEqualTo(1);
                await Assert.That(persisted.Count(row => row.Status == EmailDispatchStatus.RetryScheduled))
                    .IsEqualTo(1);
                await Assert.That(processor.SmtpAvailableTokens).IsEqualTo(0);
                await Assert.That(await assertContext.EmailDispatchAttempts.IgnoreQueryFilters().CountAsync())
                    .IsEqualTo(1);
                await Assert.That(await assertContext.EmailDispatchReceipts.IgnoreQueryFilters().CountAsync())
                    .IsEqualTo(1);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task SmtpRateAuthorityUsesDatabaseClockInsteadOfApplicationEvaluationTime()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"email-dispatch-database-clock-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch dispatch;
            await using (ExploreDbContext seedContext = CreateContext(databasePath))
            {
                dispatch = await SeedProcessingDispatchAsync(seedContext, "database-clock");
            }

            DateTime applicationTime = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime beforeDatabaseRead = DateTime.UtcNow.AddSeconds(-2);
            await using (ExploreDbContext evaluationContext = CreateContext(databasePath))
            {
                EmailDispatchEligibilityResult result =
                    await CreateEvaluator(evaluationContext).EvaluateAndBeginProviderHandoffAsync(
                        new EmailDispatchEligibilityRequest(
                            dispatch.TenantId,
                            dispatch.OutboxId,
                            dispatch.LeaseToken,
                            AttemptNumber: 0,
                            GlobalSmtpRateLimitPerMinute: 60,
                            TenantSmtpRateLimitPerMinute: 60,
                            ConsumerId: "database-clock-worker",
                            EvaluatedAt: applicationTime),
                        CancellationToken.None);
                await Assert.That(result.Outcome)
                    .IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
            }

            DateTime afterDatabaseRead = DateTime.UtcNow.AddSeconds(2);
            await using ExploreDbContext assertContext = CreateContext(databasePath);
            EmailDispatchProcessorState processor =
                await assertContext.EmailDispatchProcessorStates.AsNoTracking().SingleAsync();
            await Assert.That(processor.UpdatedAt).IsNotEqualTo(applicationTime);
            await Assert.That(processor.UpdatedAt).IsGreaterThanOrEqualTo(beforeDatabaseRead);
            await Assert.That(processor.UpdatedAt).IsLessThanOrEqualTo(afterDatabaseRead);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

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

    private static EmailDispatchEligibilityEvaluator CreateEvaluator(ExploreDbContext context) =>
        new(
            context,
            new NotificationDeliveryPolicyResolver(),
            new NotificationPreferenceResolver(context));

    private static async Task<SeededDispatch> SeedProcessingDispatchAsync(
        ExploreDbContext context,
        string suffix)
    {
        DateTime now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"SQLite email eligibility {suffix}",
            Slug = $"sqlite-email-{suffix}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"sqlite-email-{suffix}@example.test",
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
            DeduplicationKey = $"sqlite-email-eligibility:{Guid.CreateVersion7():N}",
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            CreatedAt = now
        };
        Guid leaseToken = Guid.CreateVersion7();
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
            Status = EmailDispatchStatus.Processing,
            AttemptCount = 0,
            MaxAttempts = 5,
            ProcessingStartedAt = now,
            ProcessingLeaseToken = leaseToken,
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
            ProviderStatus = "queued",
            QueuedAt = now,
            CreatedAt = now
        };
        intent.Deliveries.Add(delivery);

        context.Tenants.Add(tenant);
        context.Users.Add(user);
        context.TenantUsers.Add(tenantUser);
        context.NotificationIntents.Add(intent);
        await context.SaveChangesAsync();
        return new SeededDispatch(tenant.Id, dispatch.Id, leaseToken);
    }

    private sealed record SeededDispatch(Guid TenantId, Guid OutboxId, Guid LeaseToken);
}
