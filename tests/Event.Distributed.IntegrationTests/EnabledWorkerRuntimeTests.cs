using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Event.Distributed.IntegrationTests;

[Category("Distributed")]
public sealed class EnabledWorkerRuntimeTests
{
    [Test]
    public async Task EnabledWorkerDeliversPendingWorkAndRecoversTheOriginalPreHandoffClaimAfterRestart()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        CancellationToken cancellationToken = deadline.Token;
        await using var owner = await OwnedAppHostFixture.CreateAsync("Split", true, "MultiTenant", null,
            builder =>
            {
                var api = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>()
                    .Single(resource => resource.Name == "explore-api"));
                api.WithEnvironment("EmailDispatchProcessor__Enabled", "true")
                    .WithEnvironment("EmailDispatchProcessor__Mode", "HostedService")
                    .WithEnvironment("EmailDispatchProcessor__PollingIntervalSeconds", "1")
                    .WithEnvironment("EmailDispatchProcessor__ProcessingLeaseTimeoutSeconds", "1");
                return Task.CompletedTask;
            }, cancellationToken);
        await owner.StartAsync(cancellationToken);
        using var api = owner.CreateHttpClient("explore-api");
        Guid userId = await owner.AuthenticateBootstrapAsync(api, cancellationToken);
        Guid tenantId = await CreateActiveTenantAsync(owner, api, cancellationToken);
        using (var enabled = await api.PatchAsJsonAsync("/api/instance/settings/smtp",
            new { deliveryEnabled = true }, cancellationToken))
            enabled.EnsureSuccessStatusCode();

        var tenantFactory = new TenantExploreDbContextFactory(DatabaseOptions(owner, PrimaryDatabaseRole.Runtime));
        var admission = new EmailDispatchAdmission(
            new GlobalExploreDbContextFactory(DatabaseOptions(owner, PrimaryDatabaseRole.Global)), tenantFactory);
        using var mailpit = owner.CreateHttpClient("mailpit");
        var pending = await SeedPendingAsync(tenantFactory, tenantId, userId, cancellationToken);
        await RequireDeliveredAsync(api, mailpit, pending, cancellationToken);

        Task stopped = owner.Application.ResourceNotifications.WaitForResourceAsync(
            "explore-api", KnownResourceStates.Exited, cancellationToken);
        var stop = await owner.Application.ResourceCommands.ExecuteCommandAsync(
            "explore-api", KnownResourceCommands.StopCommand, cancellationToken);
        await Assert.That(stop.Success).IsTrue();
        await stopped;
        using (var operations = owner.CreateHttpClient("explore-api", "operations"))
        {
            bool unavailable;
            try
            {
                using var response = await operations.GetAsync("/health", cancellationToken);
                unavailable = response.StatusCode == HttpStatusCode.ServiceUnavailable;
            }
            catch (HttpRequestException) { unavailable = true; }
            await Assert.That(unavailable).IsTrue();
        }

        var original = await SeedPendingAsync(tenantFactory, tenantId, userId, cancellationToken);
        Guid originalLease = Guid.CreateVersion7();
        DateTime claimedAt = DateTime.UtcNow;
        var claimed = await admission.TryClaimSpecificAsync(new(tenantId, original.PublishEventId,
            originalLease, 8, 2, 1000, 500, claimedAt), cancellationToken);
        await Assert.That(claimed).IsNotNull();
        await Assert.That(claimed!.Id).IsEqualTo(original.OutboxId);
        await Assert.That(claimed.SourceId).IsEqualTo(original.SourceId);
        await Assert.That(claimed.Status).IsEqualTo(EmailDispatchStatus.Processing);
        await Assert.That(claimed.ProcessingLeaseToken).IsEqualTo(originalLease);
        await Assert.That(claimed.AttemptCount).IsEqualTo(0);
        await WaitUntilAsync(() => Task.FromResult(DateTime.UtcNow > claimedAt.AddSeconds(1)), cancellationToken);

        Task running = owner.Application.ResourceNotifications.WaitForResourceAsync(
            "explore-api", KnownResourceStates.Running, cancellationToken);
        var start = await owner.Application.ResourceCommands.ExecuteCommandAsync(
            "explore-api", KnownResourceCommands.StartCommand, cancellationToken);
        await Assert.That(start.Success).IsTrue();
        await running;
        await owner.Application.ResourceNotifications.WaitForResourceHealthyAsync("explore-api", cancellationToken);
        await RequireDeliveredAsync(api, mailpit, original, cancellationToken);
        await RequireDeliveredAsync(api, mailpit, pending, cancellationToken);
        await owner.DisposeAsync();
        await Assert.That(Directory.Exists(owner.Root)).IsFalse();
    }

    private static async Task<Guid> CreateActiveTenantAsync(
        OwnedAppHostFixture owner, HttpClient api, CancellationToken cancellationToken)
    {
        using var created = await api.PostAsJsonAsync("/api/tenant", new
        {
            fullName = "Enabled worker assurance",
            slug = $"worker-{owner.RunId}",
            isActive = true,
            assignCurrentUserAsTenantAdmin = true,
            directoryOperatorIdentity = new
            {
                publicName = "Assurance operator",
                legalName = "Assurance operator",
                operatorKindCode = "registered_organization",
                jurisdictionCountryCode = "BE",
                publicContactEmail = owner.BootstrapIdentifier,
                legalNoticeUrl = "https://example.invalid/legal",
                termsUrl = "https://example.invalid/terms",
                privacyUrl = "https://example.invalid/privacy"
            }
        }, cancellationToken);
        created.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
        await Assert.That(result.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        return result.RootElement.GetProperty("id").GetGuid();
    }

    private static PrimaryDatabaseConnectionOptions DatabaseOptions(OwnedAppHostFixture owner, PrimaryDatabaseRole role)
    {
        Uri endpoint = owner.Application.GetEndpoint("postgres", "tcp");
        string purpose = role == PrimaryDatabaseRole.Runtime ? "RUNTIME" : "GLOBAL";
        return new()
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Database = owner.EnvironmentAuthority["DATABASE_NAME"],
            Schema = owner.EnvironmentAuthority["DATABASE_SCHEMA"]!,
            Username = owner.EnvironmentAuthority[$"DATABASE_{purpose}_USERNAME"],
            Password = owner.EnvironmentAuthority[$"DATABASE_{purpose}_PASSWORD"],
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
    }

    private static async Task<Dispatch> SeedPendingAsync(TenantExploreDbContextFactory factory,
        Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(tenantId, cancellationToken);
        context.CurrentUserService = new SeedActor(userId);
        var recipient = await context.Users.Include(user => user.IdentityEmailClaims).ThenInclude(claim => claim.Evidence)
            .SingleAsync(user => user.Id == userId, cancellationToken);
        var address = RecipientEmailAddressResolver.Resolve(recipient, userId);
        await Assert.That(address.HasVerifiedEmail).IsTrue();
        var membership = await context.TenantUsers.SingleAsync(row => row.TenantId == tenantId
            && row.UserId == userId && row.StatusId == (int)TenantUserStatusEnum.Active, cancellationToken);
        long policyRevision = await context.EmailDispatchTenantControls.Where(row => row.TenantId == tenantId)
            .Select(row => row.DeliveryPolicyRevision).SingleOrDefaultAsync(cancellationToken);
        DateTime now = DateTime.UtcNow;
        var intent = new NotificationIntent
        {
            TenantId = tenantId,
            RecipientUserId = userId,
            RecipientTenantUser = membership,
            CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "registration.confirmed",
            DeduplicationKey = $"worker-assurance:{Guid.CreateVersion7():N}",
            EmailDeliveryPolicyRevision = policyRevision,
            CreatedAt = now
        };
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Kind = EmailDispatchKind.RegistrationConfirmation,
            SourceType = "notification_intent",
            SourceId = intent.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            RecipientUserId = userId,
            RecipientTenantUser = membership,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = address.Email!,
            Subject = $"Worker assurance {Guid.CreateVersion7():N}",
            PlainTextBody = "The original registration notification was delivered.",
            CreatedAt = now,
            UpdatedAt = now
        };
        intent.Deliveries.Add(new NotificationDelivery
        {
            TenantId = tenantId,
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
            ProviderStatus = "queued",
            QueuedAt = now,
            CreatedAt = now
        });
        context.NotificationIntents.Add(intent);
        await context.SaveChangesAsync(cancellationToken);
        return new(tenantId, dispatch.Id, intent.Id, dispatch.PublishEventId, dispatch.Subject, address.Email!);
    }

    private static async Task RequireDeliveredAsync(HttpClient api, HttpClient mailpit,
        Dispatch dispatch, CancellationToken cancellationToken)
    {
        await WaitUntilAsync(async () =>
        {
            using var response = await api.GetAsync($"/api/admin/email-dispatch/status?tenantId={dispatch.TenantId:D}", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var rows = status.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray()
                .Where(row => row.GetProperty("outboxId").GetGuid() == dispatch.OutboxId).ToArray();
            if (rows.Length != 1 || rows[0].GetProperty("deliveryStatus").GetString() != "Sent") return false;
            await Assert.That(rows[0].GetProperty("sourceId").GetGuid()).IsEqualTo(dispatch.SourceId);
            await Assert.That(rows[0].GetProperty("attemptCount").GetInt32()).IsEqualTo(1);
            await Assert.That(rows[0].GetProperty("deliveredAt").ValueKind).IsEqualTo(JsonValueKind.String);
            return true;
        }, cancellationToken);
        await WaitUntilAsync(async () =>
        {
            using var messages = await mailpit.GetAsync("/api/v1/messages", cancellationToken);
            messages.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(await messages.Content.ReadAsStringAsync(cancellationToken));
            var delivered = result.RootElement.GetProperty("messages").EnumerateArray()
                .Where(message => message.GetProperty("Subject").GetString() == dispatch.Subject
                    && message.GetProperty("To").EnumerateArray().Any(recipient =>
                        recipient.GetProperty("Address").GetString() == dispatch.Recipient)).ToArray();
            await Assert.That(delivered.Length <= 1).IsTrue();
            return delivered.Length == 1;
        }, cancellationToken);
    }

    internal static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (!await condition()) await timer.WaitForNextTickAsync(cancellationToken);
    }

    private sealed record Dispatch(Guid TenantId, Guid OutboxId, Guid SourceId, Guid PublishEventId, string Subject, string Recipient);
    private sealed record SeedActor(Guid? UserId) : ICurrentUserService { public bool IsAuthenticated => true; }
}
