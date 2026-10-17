using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>Persisted original-human scheduling and settlement; no repository or owner substitutes.</summary>
[NotInParallel]
public sealed class SelectedWebhookManualRetryTests
{
    [Test]
    [Arguments("success")]
    [Arguments("expire_after_reads")]
    [Arguments("replace_plan")]
    [Arguments("change_fence")]
    [Arguments("revoke_identity")]
    public async Task OriginalOwnerCommitsOnlyExactRetryAndRollsBackAuditWithFailedAdmission(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var preparation = Preparation(request.Services, fixture.Clock);
        var command = new RetryWebhookDeliveryAttemptCommand { AttemptId = seed.AttemptId };
        var prepared = await preparation.PrepareAsync(command);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var scheduler = Scheduler(request.Services);
        var execution = request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            var transaction = context.Database.CurrentTransaction!;
            var result = await scheduler.ScheduleAsync(command, prepared, WebhookAuditPrincipalKind.User,
                $"user:{fixture.Binding.LocalSubjectId:D}", token);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            if (change == "replace_plan")
                await context.WebhookLocalTargetSnapshots.Where(row => row.Id == seed.TargetId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryPlanSnapshotId, seed.OtherPlanId), token);
            if (change == "change_fence")
                await context.WebhookLocalTargetSnapshots.Where(row => row.Id == seed.TargetId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryFence, 99), token);
            if (change == "revoke_identity")
                await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.NewGuid().ToString()), token);
            request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
            {
                await context.WebhookLocalTargetSnapshots.AsNoTracking()
                    .SingleAsync(row => row.Id == seed.TargetId, finalToken);
                if (change == "expire_after_reads")
                    fixture.Clock.Advance(seed.Expiry - fixture.Clock.GetUtcNow());
            }, _ => { });
            return result;
        });
        if (change == "success")
            await Assert.That((await execution).Outcome).IsEqualTo(WebhookDeliveryDrainOutcome.RetryScheduled);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        var target = await observer.WebhookLocalTargetSnapshots.AsNoTracking().SingleAsync(row => row.Id == seed.TargetId);
        await Assert.That(target.DeliveryStatus).IsEqualTo(change == "success"
            ? WebhookLocalDeliveryStatus.RetryDue : WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(target.ConcurrencyVersion).IsEqualTo(change == "success" ? 4L : 3L);
        await Assert.That(target.DeliveryFence).IsEqualTo(1L);
        await Assert.That(target.DeliveryPlanSnapshotId).IsEqualTo(seed.PlanId);
        await Assert.That(target.EndpointConfigurationVersion).IsEqualTo(1);
        await Assert.That(target.DestinationUrl).IsEqualTo("https://example.invalid/selected-retry");
        await Assert.That(target.CredentialReference).IsEqualTo("fixture-reference");
        await Assert.That(target.ProcessingLeaseToken).IsNull();
        await Assert.That(await observer.WebhookDeliveryAttempts.AsNoTracking()
            .CountAsync(row => row.MessageId == seed.MessageId)).IsEqualTo(1);
        var attempt = await observer.WebhookDeliveryAttempts.AsNoTracking().SingleAsync(row => row.Id == seed.AttemptId);
        await Assert.That(attempt.Outcome).IsEqualTo(WebhookDeliveryAttemptOutcome.Failed);
        await Assert.That(attempt.ProcessingFence).IsEqualTo(1L);
        await Assert.That(await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .CountAsync(row => row.TargetId == seed.AttemptId)).IsEqualTo(change == "success" ? 1 : 0);
        var endpoint = await observer.WebhookEndpoints.AsNoTracking().SingleAsync(row => row.Id == seed.EndpointId);
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(1L);
        await Assert.That(endpoint.ConfigurationVersion).IsEqualTo(1);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(2);
        await Assert.That(endpoint.CircuitOpenedAt).IsEqualTo(seed.CircuitOpenedAt);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task FreshDuplicateRequestDefersWithoutAnotherTransitionAttemptOrAudit()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var preparation = Preparation(request.Services, fixture.Clock);
        var scheduler = Scheduler(request.Services);
        async Task<WebhookDeliverySingleDrainResult> Execute()
        {
            var command = new RetryWebhookDeliveryAttemptCommand { AttemptId = seed.AttemptId };
            var prepared = await preparation.PrepareAsync(command);
            return await request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(token =>
                scheduler.ScheduleAsync(command, prepared, WebhookAuditPrincipalKind.User,
                    $"user:{fixture.Binding.LocalSubjectId:D}", token));
        }
        await Assert.That((await Execute()).Outcome).IsEqualTo(WebhookDeliveryDrainOutcome.RetryScheduled);
        await Assert.That((await Execute()).Outcome).IsEqualTo(WebhookDeliveryDrainOutcome.Deferred);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookLocalTargetSnapshots.AsNoTracking()
            .SingleAsync(row => row.Id == seed.TargetId)).ConcurrencyVersion).IsEqualTo(4L);
        await Assert.That(await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .CountAsync(row => row.TargetId == seed.AttemptId)).IsEqualTo(1);
        await Assert.That(await observer.WebhookDeliveryAttempts.AsNoTracking()
            .CountAsync(row => row.MessageId == seed.MessageId)).IsEqualTo(1);
    }

    [Test]
    [Arguments("idle")]
    [Arguments("unowned_serializable")]
    [Arguments("copied_command")]
    public async Task MissingOriginalOwnerOrOriginalCommandCannotSchedule(string mode)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var command = new RetryWebhookDeliveryAttemptCommand { AttemptId = seed.AttemptId };
        var prepared = await Preparation(request.Services, fixture.Clock).PrepareAsync(command);
        var scheduler = Scheduler(request.Services);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        Task<WebhookDeliverySingleDrainResult> Schedule(RetryWebhookDeliveryAttemptCommand input) =>
            scheduler.ScheduleAsync(input, prepared, WebhookAuditPrincipalKind.User,
                $"user:{fixture.Binding.LocalSubjectId:D}", CancellationToken.None);
        if (mode == "idle")
            await Assert.ThrowsAsync<InvalidOperationException>(() => Schedule(command));
        else if (mode == "unowned_serializable")
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Schedule(command));
            await transaction.RollbackAsync();
        }
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<ISerializableAttemptOwner>()
                    .ExecuteAsync(_ => Schedule(command with { })));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookLocalTargetSnapshots.AsNoTracking()
            .SingleAsync(row => row.Id == seed.TargetId)).DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .CountAsync(row => row.TargetId == seed.AttemptId)).IsEqualTo(0);
    }

    [Test]
    [Arguments("provider_disabled", WebhookDeliveryDrainOutcome.Skipped)]
    [Arguments("endpoint_disabled", WebhookDeliveryDrainOutcome.Skipped)]
    [Arguments("expired_payload", WebhookDeliveryDrainOutcome.Skipped)]
    [Arguments("attempt_succeeded", WebhookDeliveryDrainOutcome.AlreadySettled)]
    [Arguments("attempt_sending", WebhookDeliveryDrainOutcome.Deferred)]
    [Arguments("target_delivering", WebhookDeliveryDrainOutcome.AlreadyClaimed)]
    [Arguments("target_succeeded", WebhookDeliveryDrainOutcome.AlreadySettled)]
    public async Task OriginalBusinessRejectionsLeaveTargetAndAuditUnchanged(string condition, WebhookDeliveryDrainOutcome expected)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        if (condition == "provider_disabled")
            request.Services.GetRequiredService<IOptionsMonitor<WebhookOptions>>().CurrentValue.Enabled = false;
        await using (var database = fixture.Native.CreateDatabase())
        {
            if (condition == "endpoint_disabled")
                await database.WebhookEndpoints.Where(row => row.Id == seed.EndpointId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId, (int)WebhookEndpointStatus.Disabled));
            if (condition == "expired_payload")
                fixture.Clock.Advance(seed.Expiry - fixture.Clock.GetUtcNow());
            if (condition.StartsWith("attempt_", StringComparison.Ordinal))
                await database.WebhookDeliveryAttempts.Where(row => row.Id == seed.AttemptId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OutcomeId,
                        condition == "attempt_succeeded" ? (int)WebhookDeliveryAttemptOutcome.Succeeded : (int)WebhookDeliveryAttemptOutcome.Sending));
            if (condition.StartsWith("target_", StringComparison.Ordinal))
                await database.WebhookLocalTargetSnapshots.Where(row => row.Id == seed.TargetId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryStatusId,
                        condition == "target_succeeded" ? (int)WebhookLocalDeliveryStatus.Succeeded : (int)WebhookLocalDeliveryStatus.Delivering));
        }
        var command = new RetryWebhookDeliveryAttemptCommand { AttemptId = seed.AttemptId };
        var prepared = await Preparation(request.Services, fixture.Clock).PrepareAsync(command);
        var result = await request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(token =>
            Scheduler(request.Services).ScheduleAsync(command, prepared, WebhookAuditPrincipalKind.User,
                $"user:{fixture.Binding.LocalSubjectId:D}", token));
        await Assert.That(result.Outcome).IsEqualTo(expected);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookLocalTargetSnapshots.AsNoTracking()
            .SingleAsync(row => row.Id == seed.TargetId)).ConcurrencyVersion).IsEqualTo(3L);
        await Assert.That(await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .CountAsync(row => row.TargetId == seed.AttemptId)).IsEqualTo(0);
    }

    private static ISelectedWebhookManualRetryScheduler Scheduler(IServiceProvider services)
    {
        var registrations = new ServiceCollection();
        registrations.AddSelectedWebhookManualRetryScheduling(provider =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<WebhookOptions>>().CurrentValue;
            return !options.IsDisabled && (options.IsProvider(WebhookOptions.ProviderLocal)
                || options.IsProvider(WebhookOptions.ProviderComposite));
        });
        var descriptor = registrations.Single(row => row.ServiceType == typeof(ISelectedWebhookManualRetryScheduler));
        return (ISelectedWebhookManualRetryScheduler)descriptor.ImplementationFactory!(services);
    }

    private static HumanWebhookMutationPreparation Preparation(IServiceProvider services, TimeProvider clock)
    {
        var registrations = new ServiceCollection();
        registrations.AddHumanWebhookResourceSources(IdentityDatabaseTopology.Colocated);
        var descriptor = registrations.Single(row => row.ServiceType == typeof(IHumanWebhookResourceSourceFactory));
        var sources = (IHumanWebhookResourceSourceFactory)descriptor.ImplementationFactory!(services);
        return new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), clock,
            services.GetRequiredService<CerbosAuthorizationService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "local" }),
            Options.Create(new CerbosSettings()),
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => sources, () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => services.GetRequiredService<ISerializableAttemptOwner>());
    }

    private sealed record RetrySeed(Guid AttemptId, Guid TargetId, Guid MessageId, Guid EndpointId,
        Guid PlanId, Guid OtherPlanId, DateTimeOffset Expiry, DateTime CircuitOpenedAt);

    private static async Task<RetrySeed> SeedAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var database = fixture.Native.CreateDatabase();
        var now = fixture.Clock.GetUtcNow();
        var expiry = now.AddMinutes(1);
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(WebhookConsumerKind.User,
            PlatformDefaults.DefaultTenantId, null, null, null, fixture.Binding.LocalSubjectId),
            "selected-retry", WebhookProviderMode.Local, now.UtcDateTime);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            ConsumerId = consumer.Id,
            Url = "https://example.invalid/selected-retry",
            SecretRef = "fixture-reference",
            SecretVersion = 1,
            SecretActivatedAt = now.UtcDateTime,
            ConfigurationVersion = 1,
            Status = WebhookEndpointStatus.Active,
            DeliveryStateVersion = 1,
            ConsecutiveFailureCount = 2,
            CircuitOpenedAt = now.AddHours(-1).UtcDateTime,
            MaxAttempts = 3,
            TimeoutSeconds = 10,
            CreatedAt = now.UtcDateTime
        };
        WebhookMessage Message() => WebhookMessage.Create(PlatformDefaults.DefaultTenantId,
            "test.selected", Guid.CreateVersion7().ToString("D"), "event", Guid.CreateVersion7(), consumer.Id,
            "{}"u8, "application/json", "identity", now.UtcDateTime, expiry.UtcDateTime, now.UtcDateTime);
        var message = Message();
        var otherMessage = Message();
        WebhookDeliveryPlanSnapshot Plan(WebhookMessage source) => WebhookDeliveryPlanSnapshot.Create(source.TenantId,
            source.Id, consumer.Id, WebhookProviderMode.Local, "1", "1", "selected", "1", expiry, expiry.AddDays(1),
            expiry.AddDays(2), expiry.AddDays(1), expiry.AddDays(1), now);
        var plan = Plan(message);
        var otherPlan = Plan(otherMessage);
        var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now, null, now);
        var lease = Guid.CreateVersion7();
        target.ClaimForDelivery("fixture", lease, now.AddSeconds(30), now);
        target.DeadLetter(lease, 1, now);
        var attempt = new WebhookDeliveryAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = message.TenantId,
            MessageId = message.Id,
            EndpointId = endpoint.Id,
            AttemptNumber = 1,
            ProcessingFence = 1,
            Outcome = WebhookDeliveryAttemptOutcome.Failed,
            ScheduledAt = now.UtcDateTime,
            SentAt = now.UtcDateTime,
            CompletedAt = now.UtcDateTime,
            CreatedAt = now.UtcDateTime
        };
        database.AddRange(consumer, endpoint, message, otherMessage, plan, otherPlan, target, attempt);
        await database.SaveChangesAsync();
        return new(attempt.Id, target.Id, message.Id, endpoint.Id, plan.Id, otherPlan.Id, expiry, endpoint.CircuitOpenedAt.Value);
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        private readonly DefaultHttpContext _context;
        internal IServiceProvider Services => _scope.ServiceProvider;

        internal Request(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            _context = new DefaultHttpContext { RequestServices = Services };
            _context.Request.Path = "/selected-webhook-retry";
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }

        internal async Task AuthenticateAsync(string token)
        {
            _accessor.HttpContext = _context;
            _context.Request.Headers.Authorization = "Bearer " + token;
            var result = await _context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            _context.User = result.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }
}
