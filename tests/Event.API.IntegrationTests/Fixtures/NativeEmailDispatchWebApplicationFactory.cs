using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

internal sealed class NativeEmailDispatchWebApplicationFactory : AuthenticatedWebApplicationFactory
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"native-email-dispatch-{Guid.CreateVersion7():N}.db");
    public Guid AdminId { get; private set; }
    public Guid TenantAdminId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid OtherTenantId { get; private set; }
    public IInterceptor? Interceptor { get; set; }

    public static async Task<NativeEmailDispatchWebApplicationFactory> CreateAsync(IInterceptor? interceptor = null)
    {
        var factory = new NativeEmailDispatchWebApplicationFactory { Interceptor = interceptor };
        factory.AdditionalConfiguration["Authorization:Provider"] = "local";
        factory.AdditionalConfiguration["EmailDispatchProcessor:Enabled"] = "false";
        factory.AdditionalConfiguration["EmailDispatchRabbitMq:Enabled"] = "false";
        try
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            factory.ConfigureDatabase(options);
            await using (var db = new ExploreDbContext(options.Options))
            {
                await db.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(db, default);
            }
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            factory.AdminId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context)).UserId;
            factory.TenantAdminId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context)).UserId;
            factory.MemberId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context)).UserId;
            var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context);
            factory.OtherTenantId = other.TenantId;
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = other.TenantId, Tenant = null!,
                UserId = other.UserId, User = null!, ActorId = other.ActorId, Actor = null!,
                StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });
            var role = await context.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
            context.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = factory.AdminId, User = null!, RoleId = role.Id, Role = role
            });
            var membership = await context.TenantUsers.SingleAsync(item => item.UserId == factory.TenantAdminId);
            context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = membership.TenantId, Tenant = null!,
                TenantUserId = membership.Id, TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
            });
            await context.SaveChangesAsync();
            return factory;
        }
        catch { await factory.DisposeAsync(); throw; }
    }

    public HttpClient Client(Guid? userId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/problem+json");
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId ?? AdminId));
        return client;
    }

    public async Task<Guid> SeedDispatchAsync(EmailDispatchStatus status, bool redacted = false, Guid? tenantId = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid tenant = tenantId ?? PlatformDefaults.DefaultTenantId;
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenant);
        var member = await db.TenantUsers.FirstAsync(item => item.TenantId == tenant);
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant,
            CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "registration.confirmed", DeduplicationKey = Guid.CreateVersion7().ToString("N"),
            RecipientUserId = member.UserId, RecipientTenantUser = member, CreatedAt = now
        };
        var row = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(), TenantId = tenant, NotificationIntent = intent,
            NotificationIntentId = intent.Id, SourceType = "notification_intent", SourceId = intent.Id,
            Kind = EmailDispatchKind.RegistrationConfirmation, RecipientUserId = member.UserId,
            RecipientTenantUser = member, RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = redacted ? string.Empty : "private-recipient@example.test",
            Subject = redacted ? string.Empty : "Private subject canary",
            PlainTextBody = redacted ? null : "Private body canary",
            ProviderMessageId = redacted ? null : "private-provider-canary",
            LastError = redacted ? null : "private-error-canary",
            LastFailureCategory = "smtp_send_failed", LastFailureAt = now,
            Status = status, AttemptCount = 3, MaxAttempts = 5, CreatedAt = now, UpdatedAt = now,
            ProcessingStartedAt = status == EmailDispatchStatus.Processing ? now : null,
            ProcessingLeaseToken = status == EmailDispatchStatus.Processing ? Guid.CreateVersion7() : null,
            UnknownAt = status == EmailDispatchStatus.Unknown ? now : null,
            ParkedAt = status == EmailDispatchStatus.Parked ? now : null,
            ParkReason = status == EmailDispatchStatus.Parked ? EmailDispatchParkReason.Operator : null,
            ContentRedactedAt = redacted ? now : null
        };
        db.EmailDispatchOutbox.Add(row);
        db.EmailDispatchReceipts.Add(new EmailDispatchReceipt
        {
            Id = Guid.CreateVersion7(), TenantId = tenant, EmailDispatchOutbox = row,
            EmailDispatchOutboxId = row.Id, PublishEventId = row.PublishEventId,
            Status = status == EmailDispatchStatus.Unknown ? EmailDispatchReceiptStatus.Unknown : EmailDispatchReceiptStatus.Failed,
            FirstSeenAt = now, FailedAt = now, FailureCode = "smtp_send_failed", CreatedAt = now
        });
        if (status == EmailDispatchStatus.Unknown)
        {
            db.EmailDispatchAttempts.Add(new EmailDispatchAttempt
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, EmailDispatchOutboxId = row.Id,
                AttemptNumber = row.AttemptCount, Outcome = EmailDispatchAttemptOutcome.Unknown,
                StartedAt = now, FailureCategory = "smtp_outcome_unknown", CreatedAt = now
            });
            db.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, NotificationIntent = intent,
                NotificationIntentId = intent.Id, EmailDispatchOutbox = row, EmailDispatchOutboxId = row.Id,
                ChannelId = (int)NotificationPreferenceChannelEnum.Email,
                DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
                PolicyVersion = 1, RecipientAddressSource = row.RecipientAddressSource,
                DisclosureLevel = "standard", TemplateKey = intent.TemplateKey, TemplateVersion = 1,
                StatusId = (int)NotificationDeliveryStatusEnum.Unknown, CreatedAt = now
            });
        }
        await db.SaveChangesAsync();
        return row.Id;
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _database
        });
        options.UseSnakeCaseNamingConvention();
        if (Interceptor is not null) options.AddInterceptors(Interceptor);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();
            services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
            services.AddScoped(provider =>
            {
                var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                db.TenantContext = provider.GetRequiredService<ITenantContext>();
                db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                return db;
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_database);
        File.Delete(_database + "-wal");
        File.Delete(_database + "-shm");
    }
}
