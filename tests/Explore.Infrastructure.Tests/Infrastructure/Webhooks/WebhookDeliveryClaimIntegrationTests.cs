using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[NotInParallel("WebhookDeliveryClaimPostgreSql")]
public sealed class WebhookDeliveryClaimIntegrationTests : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("webhook_claim_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
    }

    [Test]
    public async Task ClaimDue_WithRetryingExecutionStrategy_ClaimsCanonicalTargetAsRetriableUnit()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1));
        var request = CreateClaimRequest(target, now, TimeSpan.FromMinutes(2));
        var limits = CreateClaimLimits(target);

        await using var context = CreateDbContext(enableRetryOnFailure: true);
        var claims = await new WebhookLocalTargetRepository(context)
            .ClaimDueAsync(request, limits, CancellationToken.None);

        await Assert.That(claims.Count).IsEqualTo(1);
        await Assert.That(claims[0].Target.Id).IsEqualTo(target.Id);
        await Assert.That(claims[0].DeliveryFence).IsEqualTo(1);
        await Assert.That(claims[0].Message.Id).IsEqualTo(target.WebhookMessageId);
    }

    [Test]
    public async Task ClaimDue_WhenWorkersCompete_ProducesOneFencedOwner()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1));
        var request = CreateClaimRequest(target, now, TimeSpan.FromMinutes(2));
        var limits = CreateClaimLimits(target);

        var workerClaims = await Task.WhenAll(
            ClaimAsync(request, limits),
            ClaimAsync(request, limits));
        var winningClaim = workerClaims.SelectMany(claims => claims).Single();

        await Assert.That(workerClaims.Count(claims => claims.Count == 1)).IsEqualTo(1);
        await Assert.That(workerClaims.Count(claims => claims.Count == 0)).IsEqualTo(1);
        await using var settlementContext = CreateDbContext();
        var repository = new WebhookLocalTargetRepository(settlementContext);
        var staleLease = await repository.GetActiveClaimAsync(
            target.TenantId,
            target.Id,
            Guid.CreateVersion7(),
            winningClaim.DeliveryFence,
            now,
            CancellationToken.None);
        var staleFence = await repository.GetActiveClaimAsync(
            target.TenantId,
            target.Id,
            winningClaim.LeaseToken,
            winningClaim.DeliveryFence + 1,
            now,
            CancellationToken.None);
        var owner = await repository.GetActiveClaimAsync(
            target.TenantId,
            target.Id,
            winningClaim.LeaseToken,
            winningClaim.DeliveryFence,
            now,
            CancellationToken.None);

        await Assert.That(staleLease).IsNull();
        await Assert.That(staleFence).IsNull();
        await Assert.That(owner).IsNotNull();
    }

    [Test]
    public async Task GetActiveClaimAsync_WhenEndpointIsArchivedAfterClaim_ReturnsNull()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1));
        var claim = (await ClaimAsync(
            CreateClaimRequest(target, now, TimeSpan.FromMinutes(2)),
            CreateClaimLimits(target))).Single();

        await using (var archiveContext = CreateDbContext())
        {
            await new WebhookEndpointRepository(archiveContext).ArchiveAsync(
                target.TenantId,
                target.WebhookEndpointId,
                now.UtcDateTime,
                CancellationToken.None);
        }

        await using var workerContext = CreateDbContext();
        var activeClaim = await new WebhookLocalTargetRepository(workerContext).GetActiveClaimAsync(
            target.TenantId,
            target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            now,
            CancellationToken.None);

        await Assert.That(activeClaim).IsNull();
    }

    [Test]
    public async Task RecoverExpiredClaim_AppendsFailureEvidenceAndReschedulesTarget()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1));
        var request = CreateClaimRequest(target, now, TimeSpan.FromSeconds(1));
        var limits = CreateClaimLimits(target);
        var claim = (await ClaimAsync(request, limits)).Single();

        await using (var recoveryContext = CreateDbContext(enableRetryOnFailure: true))
        {
            var recovered = await new WebhookLocalTargetRepository(recoveryContext)
                .RecoverExpiredClaimsAsync(
                    now.AddSeconds(2),
                    "processing_lease_expired",
                    10,
                    CancellationToken.None);
            await Assert.That(recovered).IsEqualTo(1);
        }

        await using var verificationContext = CreateDbContext();
        var recoveredTarget = await verificationContext.WebhookLocalTargetSnapshots
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == target.Id);
        var evidence = await verificationContext.WebhookDeliveryAttempts
            .AsNoTracking()
            .SingleAsync(candidate =>
                candidate.TenantId == target.TenantId &&
                candidate.MessageId == target.WebhookMessageId &&
                candidate.EndpointId == target.WebhookEndpointId);
        await Assert.That(recoveredTarget.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.RetryDue);
        await Assert.That(recoveredTarget.ProcessingLeaseToken).IsNull();
        await Assert.That(evidence.Outcome).IsEqualTo(WebhookDeliveryAttemptOutcome.Failed);
        await Assert.That(evidence.AttemptNumber).IsEqualTo(checked((int)claim.DeliveryFence));
        await Assert.That(evidence.FailureCategory).IsEqualTo("processing_lease_expired");
    }

    [Test]
    public async Task RecoverExpiredClaim_WhenRetryBudgetIsExhausted_DeadLettersTarget()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1), maxAttempts: 1);
        var request = CreateClaimRequest(target, now, TimeSpan.FromSeconds(1));
        var limits = CreateClaimLimits(target);
        await ClaimAsync(request, limits);

        await using (var recoveryContext = CreateDbContext())
        {
            await new WebhookLocalTargetRepository(recoveryContext)
                .RecoverExpiredClaimsAsync(
                    now.AddSeconds(2),
                    "processing_lease_expired",
                    10,
                    CancellationToken.None);
        }

        await using var verificationContext = CreateDbContext();
        var recoveredTarget = await verificationContext.WebhookLocalTargetSnapshots
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == target.Id);
        await Assert.That(recoveredTarget.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.DeadLettered);
    }

    [Test]
    public async Task RecordFailure_WhenClaimLeaseIsStale_DoesNotMutateEndpoint()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1));
        var request = CreateClaimRequest(target, now, TimeSpan.FromSeconds(1));
        var limits = CreateClaimLimits(target);
        var claim = (await ClaimAsync(request, limits)).Single();

        await using (var recoveryContext = CreateDbContext())
        {
            await new WebhookLocalTargetRepository(recoveryContext)
                .RecoverExpiredClaimsAsync(
                    now.AddSeconds(2),
                    "processing_lease_expired",
                    10,
                    CancellationToken.None);
        }

        await using (var staleWorkerContext = CreateDbContext())
        {
            await new WebhookEndpointRepository(staleWorkerContext).RecordFailureAsync(
                target.TenantId,
                target.WebhookEndpointId,
                target.Id,
                claim.LeaseToken,
                claim.DeliveryFence,
                now.AddSeconds(2).UtcDateTime,
                "http_non_success",
                2,
                CancellationToken.None);
        }

        await using var verificationContext = CreateDbContext();
        var endpoint = await verificationContext.WebhookEndpoints
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == target.WebhookEndpointId);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(0);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
    }

    [Test]
    public async Task RecordFailure_RequiresCurrentTokenAndFence_AndAutoPausesExactlyOnce()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1));
        var claim = (await ClaimAsync(
            CreateClaimRequest(target, now, TimeSpan.FromMinutes(2)),
            CreateClaimLimits(target))).Single();
        await using var context = CreateDbContext();
        var repository = new WebhookEndpointRepository(context);

        var wrongToken = await repository.RecordFailureAsync(
            target.TenantId,
            target.WebhookEndpointId,
            target.Id,
            Guid.CreateVersion7(),
            claim.DeliveryFence,
            now.UtcDateTime,
            "http_non_success",
            2,
            CancellationToken.None);
        var wrongFence = await repository.RecordFailureAsync(
            target.TenantId,
            target.WebhookEndpointId,
            target.Id,
            claim.LeaseToken,
            claim.DeliveryFence + 1,
            now.UtcDateTime,
            "http_non_success",
            2,
            CancellationToken.None);
        var firstOwnedFailure = await repository.RecordFailureAsync(
            target.TenantId,
            target.WebhookEndpointId,
            target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            now.UtcDateTime,
            "http_non_success",
            2,
            CancellationToken.None);
        var autoPause = await repository.RecordFailureAsync(
            target.TenantId,
            target.WebhookEndpointId,
            target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            now.AddSeconds(1).UtcDateTime,
            "http_non_success",
            2,
            CancellationToken.None);
        var afterAutoPause = await repository.RecordFailureAsync(
            target.TenantId,
            target.WebhookEndpointId,
            target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            now.AddSeconds(2).UtcDateTime,
            "http_non_success",
            2,
            CancellationToken.None);

        var endpoint = await context.WebhookEndpoints
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == target.WebhookEndpointId);
        await Assert.That(wrongToken).IsEqualTo(new WebhookEndpointFailureState(0, false));
        await Assert.That(wrongFence).IsEqualTo(new WebhookEndpointFailureState(0, false));
        await Assert.That(firstOwnedFailure).IsEqualTo(new WebhookEndpointFailureState(1, false));
        await Assert.That(autoPause).IsEqualTo(new WebhookEndpointFailureState(2, true, true));
        await Assert.That(afterAutoPause).IsEqualTo(new WebhookEndpointFailureState(2, true));
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(2);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.AutoPaused);
    }

    [Test]
    public async Task EndpointCircuit_AutoPauseAndTenantScopedResumeResetState()
    {
        await ResetDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var target = await SeedTargetAsync(now.AddSeconds(-1));
        var actorUserId = Guid.CreateVersion7();
        var claim = (await ClaimAsync(
            CreateClaimRequest(target, now, TimeSpan.FromMinutes(2)),
            CreateClaimLimits(target))).Single();
        await using var context = CreateDbContext();
        var repository = new WebhookEndpointRepository(context);

        var firstFailure = await repository.RecordFailureAsync(
            target.TenantId,
            target.WebhookEndpointId,
            target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            now.UtcDateTime,
            "http_non_success",
            2,
            CancellationToken.None);
        var secondFailure = await repository.RecordFailureAsync(
            target.TenantId,
            target.WebhookEndpointId,
            target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            now.AddSeconds(1).UtcDateTime,
            "http_non_success",
            2,
            CancellationToken.None);
        var wrongTenantResume = await repository.TryResumeAsync(
            Guid.CreateVersion7(),
            target.WebhookEndpointId,
            2,
            now.AddSeconds(2).UtcDateTime,
            actorUserId,
            CancellationToken.None);
        var resumed = await repository.TryResumeAsync(
            target.TenantId,
            target.WebhookEndpointId,
            2,
            now.AddSeconds(2).UtcDateTime,
            actorUserId,
            CancellationToken.None);

        var endpoint = await context.WebhookEndpoints
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == target.WebhookEndpointId);
        await Assert.That(firstFailure).IsEqualTo(new WebhookEndpointFailureState(1, false));
        await Assert.That(secondFailure).IsEqualTo(new WebhookEndpointFailureState(2, true, true));
        await Assert.That(wrongTenantResume).IsFalse();
        await Assert.That(resumed).IsTrue();
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(0);
        await Assert.That(endpoint.CircuitOpenedAt).IsNull();
        await Assert.That(endpoint.AutoPausedAt).IsNull();
        await Assert.That(endpoint.AutoPauseReason).IsNull();
        await Assert.That(endpoint.LastResumedBy).IsEqualTo(actorUserId);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<WebhookLocalTargetClaim>> ClaimAsync(
        WebhookLocalTargetClaimRequest request,
        IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> limits)
    {
        await using var context = CreateDbContext();
        return await new WebhookLocalTargetRepository(context)
            .ClaimDueAsync(request, limits, CancellationToken.None);
    }

    private static WebhookLocalTargetClaimRequest CreateClaimRequest(
        WebhookLocalTargetSnapshot target,
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration) =>
        new(
            BatchSize: 1,
            CandidateBatchSize: 10,
            GlobalInFlightLimit: 10,
            TenantOrder: [target.TenantId],
            ClaimedAtUtc: claimedAtUtc,
            LeaseDuration: leaseDuration);

    private static Dictionary<Guid, WebhookDeliveryClaimLimits> CreateClaimLimits(
        WebhookLocalTargetSnapshot target) =>
        new()
        {
            [target.TenantId] = new(10, 1, 1)
        };

    private async Task<WebhookLocalTargetSnapshot> SeedTargetAsync(
        DateTimeOffset capturedAtUtc,
        int maxAttempts = 8)
    {
        await using var context = CreateDbContext();
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var endpointId = Guid.CreateVersion7();
        var tenant = new Tenant
        {
            Id = tenantId,
            FullName = "Webhook Target Claim Test Tenant",
            Slug = $"webhook-target-claim-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = capturedAtUtc.UtcDateTime
        };
        var consumer = new WebhookConsumer
        {
            Id = consumerId,
            TenantId = tenantId,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = "Target claim integration consumer",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 1,
            CreatedAt = capturedAtUtc.UtcDateTime
        };
        var endpoint = new WebhookEndpoint
        {
            Id = endpointId,
            TenantId = tenantId,
            ConsumerId = consumerId,
            Url = "https://hooks.example.test/target",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "target-claim-integration-secret",
            SecretVersion = 1,
            SecretActivatedAt = capturedAtUtc.AddMinutes(-1).UtcDateTime,
            ConfigurationVersion = 1,
            MaxAttempts = maxAttempts,
            TimeoutSeconds = 15,
            CreatedAt = capturedAtUtc.UtcDateTime
        };
        var message = WebhookMessage.Create(
            tenantId,
            "event.published",
            $"target-claim-{Guid.CreateVersion7():N}",
            "Event",
            Guid.CreateVersion7(),
            consumerId,
            "{\"type\":\"event.published\"}"u8,
            "application/json",
            "utf-8",
            capturedAtUtc.UtcDateTime,
            capturedAtUtc.AddDays(14).UtcDateTime,
            capturedAtUtc.UtcDateTime);
        var plan = WebhookDeliveryPlanSnapshot.Create(
            tenantId,
            message.Id,
            consumerId,
            WebhookProviderMode.Local,
            "consumer-v1",
            "contract-v1",
            "standard",
            "retention-v1",
            capturedAtUtc.AddDays(14),
            capturedAtUtc.AddDays(30),
            capturedAtUtc.AddDays(90),
            capturedAtUtc.AddDays(90),
            capturedAtUtc.AddDays(30),
            capturedAtUtc);
        var target = WebhookLocalTargetSnapshot.Create(
            plan,
            endpoint,
            endpoint.ConfigurationVersion,
            capturedAtUtc.AddMinutes(-1),
            null,
            capturedAtUtc);

        context.AddRange(tenant, consumer, endpoint, message, plan, target);
        await context.SaveChangesAsync();
        return target;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
    }

    private ExploreDbContext CreateDbContext(bool enableRetryOnFailure = false)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ExploreDbContext>();
        if (enableRetryOnFailure)
        {
            optionsBuilder.UseNpgsql(
                _container.GetConnectionString(),
                npgsql => npgsql.EnableRetryOnFailure());
        }
        else
        {
            optionsBuilder.UseNpgsql(_container.GetConnectionString());
        }

        var context = new ExploreDbContext(optionsBuilder
            .UseSnakeCaseNamingConvention()
            .Options);
        context.EnableTenantFilterBypass("Webhook Local target claim integration test.");
        return context;
    }
}
