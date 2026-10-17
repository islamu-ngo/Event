using System.Text.Json;
using Event.Persistence.IntegrationTests;
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
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Mail.Unsubscribe;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Fixtures;

internal sealed class PersistedEmailDispatchScenario(MailpitContainerFixture mailpit)
{
    internal static ServiceProvider CreateServiceProvider(DispatchDatabase database, IEmailService transport, string consumerId = "mailpit-drain-test")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<BusinessMetrics>();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped(provider => database.CreateContext(
            provider.GetRequiredService<ITenantContextAccessor>().TenantId));
        services.AddSingleton<IEmailDispatchAdmissionDbContextFactory>(database);
        services.AddSingleton<ITenantExploreDbContextFactory>(database);
        services.AddScoped<IEmailDispatchAdmission, EmailDispatchAdmission>();
        services.AddScoped<IEmailDispatchOutboxRepository, EmailDispatchOutboxRepository>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<ISettingMutationLock, RelationalSettingMutationLock>();
        services.AddSingleton<NotificationDeliveryPolicyResolver>();
        services.AddScoped<INotificationPreferenceResolver, NotificationPreferenceResolver>();
        services.AddScoped<IEmailDispatchEligibilityEvaluator, EmailDispatchEligibilityEvaluator>();
        services.AddScoped<ISystemSettingRepository>(provider => SystemSettingRepository.CreateReadOnly(
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<ISettingMutationLock>()));
        services.AddSingleton<IEmailService>(transport);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IEmailUnsubscribeTokenService, EmailUnsubscribeTokenService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Options.Create(new EmailDispatchProcessorSettings
        {
            BatchSize = 10,
            ConsumerId = consumerId
        }));
        services.AddSingleton<EmailDispatchDrainService>();
        services.AddSingleton<IEmailDispatchDrainService>(provider =>
            provider.GetRequiredService<EmailDispatchDrainService>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    internal async Task<DispatchDatabase> CreateDatabaseAsync(string subject, string body)
    {
        var database = new DispatchDatabase();
        try
        {
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var settings = await new InternalEmailDeliverySettingsWriter(context, mutationLock, unitOfWork)
                .ApplyInternalAsync(
                    [new(null, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true"),
                     new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, JsonSerializer.Serialize(mailpit.SmtpHost)),
                     new(null, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, mailpit.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                     new(null, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"noreply@example.test\""),
                     new(null, GovernanceSettingKeys.Email.SmtpSecurity, EmailDeliverySettingMutationKind.SetValue, "\"None\"")],
                    actorUserId: null);
            await Assert.That(settings.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await database.SeedAsync(context, subject, body);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    internal static async Task AssertSentStateAsync(DispatchDatabase database)
    {
        await using var context = database.CreateContext(database.TenantId);
        var dispatch = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Sent);
        await Assert.That(dispatch.SentAt).IsNotNull();
        await Assert.That(dispatch.ProviderMessageId).IsNull();
        await Assert.That(dispatch.ProcessingLeaseToken).IsNull();
        await Assert.That(dispatch.ProcessingStartedAt).IsNull();
        await Assert.That(dispatch.AttemptCount).IsEqualTo(1);
        var attempt = await context.EmailDispatchAttempts.AsNoTracking().SingleAsync();
        await Assert.That(attempt.EmailDispatchOutboxId).IsEqualTo(dispatch.Id);
        await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Succeeded);
        await Assert.That(attempt.ProviderMessageId).IsNull();
        var receipt = await context.EmailDispatchReceipts.AsNoTracking().SingleAsync();
        await Assert.That(receipt.EmailDispatchOutboxId).IsEqualTo(dispatch.Id);
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Completed);
        await Assert.That(receipt.ProviderMessageId).IsNull();
        var delivery = await context.NotificationDeliveries.AsNoTracking().SingleAsync();
        await Assert.That(delivery.EmailDispatchOutboxId).IsEqualTo(dispatch.Id);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
    }

    internal sealed class DispatchDatabase : IAsyncDisposable,
        IEmailDispatchAdmissionDbContextFactory, ITenantExploreDbContextFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"mailpit-dispatch-{Guid.CreateVersion7():N}.db");
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid PublishEventId { get; } = Guid.CreateVersion7();

        public ExploreDbContext CreateContext(Guid? tenantId = null)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                DefaultTimeout = 30,
                Pooling = false
            }.ToString();
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                    SqliteProjectionLockTransactionInterceptor.Instance)
                .Options;
            return new ExploreDbContext(options)
            {
                TenantContext = tenantId is { } id ? new TenantIdentity(id) : null
            };
        }

        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateContext());
        }

        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateContext(tenantId));
        }

        public async Task SeedAsync(ExploreDbContext context, string subject, string body)
        {
            DateTime now = DateTime.UtcNow;
            var tenant = new Tenant
            {
                Id = TenantId,
                FullName = "Mailpit dispatch tenant",
                Slug = $"mailpit-dispatch-{TenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii { Email = "attendee@example.test", FirstName = "Mailpit", LastName = "Recipient" },
                EmailVerified = true,
                CreatedAt = now
            };
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
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
                TenantId = TenantId,
                CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
                OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
                RecipientKindId = (int)NotificationRecipientKindEnum.User,
                StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
                TemplateKey = "registration.confirmed",
                DeduplicationKey = $"mailpit-dispatch:{Guid.CreateVersion7():N}",
                RecipientUserId = user.Id,
                RecipientTenantUser = membership,
                CreatedAt = now
            };
            var dispatch = new EmailDispatchOutbox
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                PublishEventId = PublishEventId,
                Kind = EmailDispatchKind.RegistrationConfirmation,
                SourceType = "notification_intent",
                SourceId = intent.Id,
                NotificationIntentId = intent.Id,
                NotificationIntent = intent,
                RecipientUserId = user.Id,
                RecipientTenantUser = membership,
                RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
                RecipientEmail = user.Email,
                Subject = subject,
                PlainTextBody = body,
                HtmlBody = $"<p>{body}</p>",
                Status = EmailDispatchStatus.Pending,
                MaxAttempts = 5,
                CorrelationId = $"corr-{Guid.CreateVersion7():N}",
                CreatedAt = now,
                UpdatedAt = now
            };
            intent.Deliveries.Add(new NotificationDelivery
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                NotificationIntentId = intent.Id,
                NotificationIntent = intent,
                ChannelId = (int)NotificationPreferenceChannelEnum.Email,
                DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
                PolicyVersion = 1,
                PreferenceCategoryCode = NotificationPreferenceCategoryCodes.RegistrationStatus,
                RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
                DisclosureLevel = "standard",
                TemplateKey = intent.TemplateKey,
                TemplateVersion = 1,
                EmailDispatchOutboxId = dispatch.Id,
                EmailDispatchOutbox = dispatch,
                StatusId = (int)NotificationDeliveryStatusEnum.Queued,
                QueuedAt = now,
                CreatedAt = now
            });
            var binding = new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Google,
                AuthenticationProvider = null!,
                ProviderKey = Explore.Application.Authentication.PlatformIdentityPrincipalExtensions
                    .CreateOidcAccountKey("https://accounts.google.com", Guid.CreateVersion7().ToString("N")).Value,
                CreatedAt = now
            };
            var claim = UserIdentityEmailClaim.Create(user.Id, user.Email!);
            context.AddRange(tenant, user, membership, intent, binding, claim,
                UserIdentityEmailEvidence.Create(user.Id, claim.Id, binding.Id, now),
                new NotificationChannelPreference
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = TenantId,
                    Tenant = tenant,
                    ScopeId = (int)ConfigurationScopeEnum.User,
                    Scope = null!,
                    UserId = user.Id,
                    User = user,
                    CategoryId = (int)NotificationPreferenceCategoryEnum.RegistrationStatus,
                    Category = null!,
                    ChannelId = (int)NotificationPreferenceChannelEnum.Email,
                    Channel = null!,
                    IsEnabled = true,
                    CreatedAt = now
                });
            await context.SaveChangesAsync();
        }

        public ValueTask DisposeAsync()
        {
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                File.Delete(_path + suffix);
            }
            return ValueTask.CompletedTask;
        }

        private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    }
}
