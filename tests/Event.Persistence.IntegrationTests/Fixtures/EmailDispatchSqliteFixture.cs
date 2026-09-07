// ABOUTME: Shared real SQLite setup for email eligibility, rate admission, and delivery policy fences.
// ABOUTME: Seeds an optional verified-recipient dispatch with production lookup rows and transaction interceptors.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema.ProviderPrimitives;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Explore.Infrastructure.Mail;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal static class EmailDispatchSqliteFixture
{
    internal static async Task CreateDatabaseAsync(string databasePath)
    {
        await using ExploreDbContext context = CreateContext(databasePath);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        await EnableInstanceEmailAsync(context);
    }

    internal static Task EnableInstanceEmailAsync(ExploreDbContext context) => ApplyEmailSettingsAsync(context,
        [new(TenantId: null, Key: GovernanceSettingKeys.Email.DeliveryEnabled, Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true"),
         new(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpHost, Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.fixture.test\""),
         new(TenantId: null, Key: GovernanceSettingKeys.Email.FromAddress, Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"events@fixture.test\""),
         new(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpSecurity, Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"StartTls\"")]);

    internal static EmailDeliverySettingsWriter CreateEmailSettingsWriter(ExploreDbContext context,
        ISettingMutationLock? mutationLock = null, EmailDeliveryDisableTokenService? tokenService = null) =>
        new(context: context, mutationLock: mutationLock ?? new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)),
            unitOfWork: new EfCoreUnitOfWork(context), impactReader: new EmailDeliveryDisableImpactReader(context),
            tokenService: tokenService ?? new EmailDeliveryDisableTokenService(new EphemeralDataProtectionProvider()));

    internal static async Task ApplyEmailSettingsAsync(ExploreDbContext context,
        System.Collections.Immutable.ImmutableArray<EmailDeliverySettingMutation> mutations,
        Guid? actorUserId = null, ISettingMutationLock? mutationLock = null, CancellationToken cancellationToken = default)
    {
        var outcome = await CreateEmailSettingsWriter(context, mutationLock).ApplyAsync(mutations, actorUserId, cancellationToken);
        if (outcome.Status is not (EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange))
            throw new InvalidOperationException($"Email fixture mutation rejected: {outcome.Status}.");
    }

    internal static Task SetEmailSettingAsync(ExploreDbContext context, string key, string value,
        Guid? tenantId = null, Guid? actorUserId = null, ISettingMutationLock? mutationLock = null,
        CancellationToken cancellationToken = default) => key == GovernanceSettingKeys.Email.DeliveryEnabled && value == "false"
            ? ConfirmEmailDisableAsync(context, tenantId, actorUserId, mutationLock, cancellationToken)
            : ApplyEmailSettingsAsync(context,
                [new(TenantId: tenantId, Key: key, Kind: EmailDeliverySettingMutationKind.SetValue, Value: value)],
                actorUserId, mutationLock, cancellationToken);

    internal static async Task ConfirmEmailDisableAsync(ExploreDbContext context, Guid? tenantId = null,
        Guid? actorUserId = null, ISettingMutationLock? mutationLock = null, CancellationToken cancellationToken = default)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        mutationLock ??= new RelationalSettingMutationLock(context, unitOfWork);
        var tokens = new EmailDeliveryDisableTokenService(new EphemeralDataProtectionProvider());
        var writer = CreateEmailSettingsWriter(context, mutationLock, tokens);
        async Task<bool> ConfirmAsync(CancellationToken token)
        {
            var impact = (await new EmailDeliveryDisableImpactReader(context).ReadAsync(tenantId, token))!;
            Guid actor = actorUserId ?? await context.Users.AsNoTracking().Select(user => user.Id).FirstOrDefaultAsync(token);
            if (actor == Guid.Empty)
            {
                actor = Guid.CreateVersion7();
                context.Users.Add(new User
                {
                    Id = actor, CreatedAt = DateTime.UtcNow,
                    Pii = new UserPii { Email = $"email-fixture-actor-{actor:N}@example.test", FirstName = "Email", LastName = "Operator" }
                });
                await context.SaveChangesAsync(token);
            }
            var result = impact.CanDisable
                ? await writer.DisableAsync(new(TenantId: tenantId, ActorUserId: actor, ExpectedRevision: impact.Revision,
                    ConfirmationToken: tokens.Issue(actor, impact).Token,
                    Acknowledgement: EmailDeliveryDisableConfirmation.RequiredAcknowledgement), token)
                : await writer.ApplyAsync([new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "false")], actorUserId, token);
            if (result.Status is not (EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange))
                throw new InvalidOperationException($"Email fixture disable rejected: {result.Status}.");
            return true;
        }
        if (context.Database.CurrentTransaction is not null)
            await ConfirmAsync(cancellationToken);
        else
            await mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => unitOfWork.ExecuteSerializableAsync(ConfirmAsync, token), cancellationToken);
    }

    internal static ExploreDbContext CreateContext(string databasePath, params IInterceptor[] interceptors)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = true
        }.ToString();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance)
            .AddInterceptors(interceptors)
            .Options;
        return new ExploreDbContext(options);
    }

    internal static EmailDispatchEligibilityEvaluator CreateEvaluator(
        ExploreDbContext context, ISettingMutationLock? mutationLock = null) =>
        new(context, new NotificationDeliveryPolicyResolver(), new NotificationPreferenceResolver(context),
            mutationLock ?? new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

    internal static async Task<SeededDispatch> SeedProcessingDispatchAsync(
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

    internal sealed record SeededDispatch(Guid TenantId, Guid OutboxId, Guid LeaseToken);
}
