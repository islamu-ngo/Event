using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookMaterializationAtomicityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime MaterializedAt =
        new(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task MaterializeAsync_NewPlan_CommitsMessagePlanLocalTargetAndPublicationTogether()
    {
        var scenario = await SeedAuthorityAsync();
        await using var context = fixture.CreateDbContext();
        var materializer = new WebhookDeliveryPlanMaterializer(context, new EfCoreUnitOfWork(context));

        var materialization = CreateMaterialization(scenario);
        var result = await materializer.MaterializeAsync(
            materialization,
            CancellationToken.None);

        await Assert.That(result.Created).IsTrue();
        await Assert.That(result.Message.Id).IsEqualTo(materialization.Message.Id);
        await Assert.That(result.Message.Id.Version).IsEqualTo(7);
        await Assert.That(result.DeliveryPlan.WebhookMessageId).IsEqualTo(result.Message.Id);
        await Assert.That(await context.WebhookMessages.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookDeliveryPlanSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookLocalTargetSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookProviderPublications.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task MaterializeAsync_WithinExistingUnitOfWork_JoinsAmbientTransaction()
    {
        var scenario = await SeedAuthorityAsync();
        await using var context = fixture.CreateDbContext();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var materializer = new WebhookDeliveryPlanMaterializer(context, unitOfWork);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            token => materializer.MaterializeAsync(CreateMaterialization(scenario), token),
            CancellationToken.None);

        await Assert.That(result.Created).IsTrue();
        await Assert.That(await context.WebhookMessages.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookDeliveryPlanSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookLocalTargetSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookProviderPublications.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task MaterializeAsync_WhenAnyTargetViolatesUniqueness_RollsBackEveryRow()
    {
        var scenario = await SeedAuthorityAsync();
        await using var context = fixture.CreateDbContext();
        var materializer = new WebhookDeliveryPlanMaterializer(context, new EfCoreUnitOfWork(context));

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await materializer.MaterializeAsync(
                CreateMaterialization(scenario, duplicateProviderPublication: true),
                CancellationToken.None));

        await Assert.That(await context.WebhookMessages.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.WebhookDeliveryPlanSnapshots.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.WebhookLocalTargetSnapshots.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.WebhookProviderPublications.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task MaterializeAsync_ReplayedSameIdentityAndHash_ReturnsFrozenExistingPlan()
    {
        var scenario = await SeedAuthorityAsync();
        await using var context = fixture.CreateDbContext();
        var materializer = new WebhookDeliveryPlanMaterializer(context, new EfCoreUnitOfWork(context));

        var first = await materializer.MaterializeAsync(
            CreateMaterialization(scenario),
            CancellationToken.None);
        var replay = await materializer.MaterializeAsync(
            CreateMaterialization(scenario),
            CancellationToken.None);

        await Assert.That(first.Created).IsTrue();
        await Assert.That(replay.Created).IsFalse();
        await Assert.That(replay.Message.Id).IsEqualTo(first.Message.Id);
        await Assert.That(replay.DeliveryPlan.Id).IsEqualTo(first.DeliveryPlan.Id);
        await Assert.That(await context.WebhookMessages.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookDeliveryPlanSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookLocalTargetSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookProviderPublications.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task MaterializeAsync_ReplayedIdentityWithChangedBytes_ConflictsWithoutMutation()
    {
        var scenario = await SeedAuthorityAsync();
        await using var context = fixture.CreateDbContext();
        var materializer = new WebhookDeliveryPlanMaterializer(context, new EfCoreUnitOfWork(context));
        var original = await materializer.MaterializeAsync(
            CreateMaterialization(scenario),
            CancellationToken.None);

        await Assert.ThrowsAsync<WebhookMaterializationConflictException>(async () =>
            await materializer.MaterializeAsync(
                CreateMaterialization(scenario, payloadSuffix: "-changed"),
                CancellationToken.None));

        var storedMessage = await context.WebhookMessages.AsNoTracking().SingleAsync();
        await Assert.That(storedMessage.Id).IsEqualTo(original.Message.Id);
        await Assert.That(Encoding.UTF8.GetString(storedMessage.GetPayloadBytes()!))
            .IsEqualTo("{\"event\":\"materialized\"}");
        await Assert.That(await context.WebhookMessages.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookDeliveryPlanSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookLocalTargetSnapshots.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.WebhookProviderPublications.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task MaterializeAsync_AfterEndpointUpdate_FreezesOldAndNewTargetVersions()
    {
        var scenario = await SeedAuthorityAsync();
        await using var context = fixture.CreateDbContext();
        var materializer = new WebhookDeliveryPlanMaterializer(context, new EfCoreUnitOfWork(context));
        var original = await materializer.MaterializeAsync(
            CreateMaterialization(scenario),
            CancellationToken.None);

        var endpoint = await context.WebhookEndpoints.SingleAsync();
        endpoint.UpdateConfiguration(
            "https://webhook-v2.example.test/events",
            "Version two",
            maxAttempts: 12,
            timeoutSeconds: 20,
            rateLimitPerMinute: 240,
            MaterializedAt.AddMinutes(1));
        await context.SaveChangesAsync();

        var nextScenario = scenario with
        {
            Endpoint = endpoint,
            AggregateId = Guid.CreateVersion7(),
            EventId = $"domain-event-{Guid.NewGuid():N}"
        };
        var next = await materializer.MaterializeAsync(
            CreateMaterialization(nextScenario),
            CancellationToken.None);

        var targets = await context.WebhookLocalTargetSnapshots
            .AsNoTracking()
            .OrderBy(target => target.CapturedAtUtc)
            .ThenBy(target => target.Id)
            .ToListAsync();
        var originalTarget = targets.Single(target => target.WebhookMessageId == original.Message.Id);
        var newTarget = targets.Single(target => target.WebhookMessageId == next.Message.Id);

        await Assert.That(originalTarget.EndpointConfigurationVersion).IsEqualTo(1);
        await Assert.That(originalTarget.DestinationUrl).IsEqualTo("https://webhook.example.test/events");
        await Assert.That(originalTarget.MaxAttempts).IsEqualTo(8);
        await Assert.That(newTarget.EndpointConfigurationVersion).IsEqualTo(2);
        await Assert.That(newTarget.DestinationUrl).IsEqualTo("https://webhook-v2.example.test/events");
        await Assert.That(newTarget.MaxAttempts).IsEqualTo(12);
    }

    [Test]
    public async Task GetEligiblePendingTargetsForUpdateAsync_ExcludesAttemptedWorkAndPersistsExplicitMigration()
    {
        var scenario = await SeedAuthorityAsync();
        await using var context = fixture.CreateDbContext();
        var materializer = new WebhookDeliveryPlanMaterializer(context, new EfCoreUnitOfWork(context));
        var original = await materializer.MaterializeAsync(
            CreateMaterialization(scenario),
            CancellationToken.None);

        var attemptedScenario = scenario with
        {
            AggregateId = Guid.CreateVersion7(),
            EventId = $"domain-event-{Guid.NewGuid():N}"
        };
        var attemptedMaterialization = await materializer.MaterializeAsync(
            CreateMaterialization(attemptedScenario),
            CancellationToken.None);
        context.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = scenario.TenantId,
            MessageId = attemptedMaterialization.Message.Id,
            EndpointId = scenario.Endpoint.Id,
            AttemptNumber = 1,
            Outcome = WebhookDeliveryAttemptOutcome.Failed,
            ScheduledAt = MaterializedAt,
            CompletedAt = MaterializedAt,
            ProcessingFence = 1,
            FailureCategory = "transport_failure",
            CreatedAt = MaterializedAt
        });

        var endpoint = await context.WebhookEndpoints.SingleAsync();
        endpoint.UpdateConfiguration(
            "https://migrated.example.test/events",
            endpoint.Description,
            maxAttempts: 10,
            timeoutSeconds: 25,
            rateLimitPerMinute: 180,
            MaterializedAt.AddMinutes(2));
        await context.SaveChangesAsync();

        var repository = new WebhookEndpointRepository(context);
        var eligibleTargets = await repository.GetEligiblePendingTargetsForUpdateAsync(
            scenario.TenantId,
            endpoint.Id,
            CancellationToken.None);

        await Assert.That(eligibleTargets.Count).IsEqualTo(1);
        await Assert.That(eligibleTargets.Single().WebhookMessageId).IsEqualTo(original.Message.Id);

        eligibleTargets.Single().MigratePendingConfiguration(
            endpoint,
            new DateTimeOffset(MaterializedAt.AddMinutes(2)));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var targets = await context.WebhookLocalTargetSnapshots
            .AsNoTracking()
            .OrderBy(target => target.WebhookMessageId)
            .ToListAsync();
        var migrated = targets.Single(target => target.WebhookMessageId == original.Message.Id);
        var attempted = targets.Single(target =>
            target.WebhookMessageId == attemptedMaterialization.Message.Id);

        await Assert.That(migrated.EndpointConfigurationVersion).IsEqualTo(2);
        await Assert.That(migrated.DestinationUrl).IsEqualTo("https://migrated.example.test/events");
        await Assert.That(migrated.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
        await Assert.That(attempted.EndpointConfigurationVersion).IsEqualTo(1);
        await Assert.That(attempted.DestinationUrl).IsEqualTo("https://webhook.example.test/events");
    }

    [Test]
    public async Task UpdateEndpointConfiguration_ConcurrentWritersRejectStaleConfigurationVersion()
    {
        var scenario = await SeedAuthorityAsync();
        await using var winningContext = fixture.CreateDbContext();
        await using var staleContext = fixture.CreateDbContext();
        var winningEndpoint = await winningContext.WebhookEndpoints
            .SingleAsync(endpoint => endpoint.Id == scenario.Endpoint.Id);
        var staleEndpoint = await staleContext.WebhookEndpoints
            .SingleAsync(endpoint => endpoint.Id == scenario.Endpoint.Id);

        winningEndpoint.UpdateConfiguration(
            "https://winner.example.test/events",
            "Winning configuration",
            maxAttempts: 10,
            timeoutSeconds: 20,
            rateLimitPerMinute: 180,
            MaterializedAt.AddMinutes(3));
        staleEndpoint.UpdateConfiguration(
            "https://stale.example.test/events",
            "Stale configuration",
            maxAttempts: 12,
            timeoutSeconds: 25,
            rateLimitPerMinute: 240,
            MaterializedAt.AddMinutes(4));

        await winningContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await staleContext.SaveChangesAsync());

        await using var verificationContext = fixture.CreateDbContext();
        var persisted = await verificationContext.WebhookEndpoints
            .AsNoTracking()
            .SingleAsync(endpoint => endpoint.Id == scenario.Endpoint.Id);
        await Assert.That(persisted.ConfigurationVersion).IsEqualTo(2);
        await Assert.That(persisted.Url).IsEqualTo("https://winner.example.test/events");
        await Assert.That(persisted.Description).IsEqualTo("Winning configuration");
    }

    [Test]
    public async Task ChangeConsumerProviderMode_ConcurrentWritersRejectStaleConfigurationVersion()
    {
        var scenario = await SeedAuthorityAsync();
        await using var winningContext = fixture.CreateDbContext();
        await using var staleContext = fixture.CreateDbContext();
        var winningConsumer = await winningContext.WebhookConsumers
            .SingleAsync(consumer => consumer.Id == scenario.ConsumerId);
        var staleConsumer = await staleContext.WebhookConsumers
            .SingleAsync(consumer => consumer.Id == scenario.ConsumerId);

        winningConsumer.ChangeProviderMode(WebhookProviderMode.DryRun, MaterializedAt.AddMinutes(3));
        staleConsumer.ChangeProviderMode(WebhookProviderMode.Disabled, MaterializedAt.AddMinutes(4));

        await winningContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await staleContext.SaveChangesAsync());

        await using var verificationContext = fixture.CreateDbContext();
        var persisted = await verificationContext.WebhookConsumers
            .AsNoTracking()
            .SingleAsync(consumer => consumer.Id == scenario.ConsumerId);
        await Assert.That(persisted.ConfigurationVersion).IsEqualTo(2);
        await Assert.That(persisted.ProviderMode).IsEqualTo(WebhookProviderMode.DryRun);
    }

    private async Task<SeededAuthority> SeedAuthorityAsync()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant();
        var consumer = CreateConsumer(tenant.Id);
        var endpoint = CreateEndpoint(tenant.Id, consumer.Id);
        var binding = CreateBinding(tenant.Id, consumer.Id);
        context.Tenants.Add(tenant);
        context.WebhookConsumers.Add(consumer);
        context.WebhookEndpoints.Add(endpoint);
        context.WebhookConsumerProviderBindings.Add(binding);
        await context.SaveChangesAsync();

        return new SeededAuthority(
            tenant.Id,
            consumer.Id,
            endpoint,
            binding.Id,
            Guid.CreateVersion7(),
            $"domain-event-{Guid.NewGuid():N}");
    }

    private static WebhookDeliveryMaterialization CreateMaterialization(
        SeededAuthority authority,
        string payloadSuffix = "",
        bool duplicateProviderPublication = false)
    {
        var message = WebhookMessage.Create(
            authority.TenantId,
            "event.materialized",
            authority.EventId,
            "event",
            authority.AggregateId,
            authority.ConsumerId,
            Encoding.UTF8.GetBytes($"{{\"event\":\"materialized{payloadSuffix}\"}}"),
            "application/json",
            "utf-8",
            MaterializedAt.AddMinutes(-1),
            MaterializedAt.AddDays(7),
            MaterializedAt);
        var plan = WebhookDeliveryPlanSnapshot.Create(
            authority.TenantId,
            message.Id,
            authority.ConsumerId,
            WebhookProviderMode.Composite,
            "configuration-v1",
            "contract-v1",
            "default",
            "retention-v1",
            new DateTimeOffset(message.PayloadRetentionUntil),
            new DateTimeOffset(MaterializedAt.AddDays(30)),
            new DateTimeOffset(MaterializedAt.AddDays(90)),
            new DateTimeOffset(MaterializedAt.AddDays(90)),
            new DateTimeOffset(MaterializedAt.AddDays(30)),
            new DateTimeOffset(MaterializedAt));
        var localTarget = WebhookLocalTargetSnapshot.Create(
            plan,
            authority.Endpoint,
            authority.Endpoint.ConfigurationVersion,
            new DateTimeOffset(MaterializedAt.AddDays(-1)),
            new DateTimeOffset(MaterializedAt.AddDays(30)),
            new DateTimeOffset(MaterializedAt));
        var publication = CreatePublication(authority, message, plan, "primary");
        var publications = duplicateProviderPublication
            ? new[] { publication, CreatePublication(authority, message, plan, "duplicate") }
            : [publication];

        return new WebhookDeliveryMaterialization(
            message,
            plan,
            [localTarget],
            publications);
    }

    private static WebhookProviderPublication CreatePublication(
        SeededAuthority authority,
        WebhookMessage message,
        WebhookDeliveryPlanSnapshot plan,
        string identitySuffix) =>
        WebhookProviderPublication.Create(
            authority.TenantId,
            message.Id,
            plan.Id,
            WebhookProviderKind.Svix,
            authority.BindingId,
            "1.84.0",
            $"{message.Id:D}-{identitySuffix}",
            $"{message.Id:N}:{authority.BindingId:N}:{identitySuffix}",
            message.PayloadHash,
            "consumer-application-uid",
            "provider-application-id",
            "managed-eu",
            "secret:webhook-provider",
            "credential-v1",
            WebhookProviderMode.Composite,
            "provider-config-v1",
            eventContractVersion: 1,
            "retention-v1",
            message.PayloadRetentionUntil,
            MaterializedAt.AddDays(30),
            MaterializedAt.AddHours(12),
            MaterializedAt);

    private static WebhookEndpoint CreateEndpoint(Guid tenantId, Guid consumerId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        ConsumerId = consumerId,
        Url = "https://webhook.example.test/events",
        Status = WebhookEndpointStatus.Active,
        SecretRef = "secret:webhook-endpoint",
        SecretVersion = 3,
        ConfigurationVersion = 1,
        MaxAttempts = 8,
        TimeoutSeconds = 15,
        RateLimitPerMinute = 120,
        CreatedAt = MaterializedAt.AddDays(-1)
    };

    private static WebhookConsumerProviderBinding CreateBinding(Guid tenantId, Guid consumerId)
    {
        var profile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            "1.84.0",
            WebhookProviderCapability.EndpointManagement,
            "svix-1.84.0-v1",
            new DateTimeOffset(MaterializedAt));
        return WebhookConsumerProviderBinding.CreatePending(
            tenantId,
            consumerId,
            Guid.CreateVersion7(),
            "managed-eu",
            profile,
            WebhookProviderCapability.EndpointManagement);
    }

    private static WebhookConsumer CreateConsumer(Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        ConsumerKind = WebhookConsumerKind.Tenant,
        Name = $"Materialization Consumer {Guid.NewGuid():N}",
        Status = WebhookConsumerStatus.Active,
        ProviderMode = WebhookProviderMode.Composite,
        ConfigurationVersion = 1,
        CreatedAt = MaterializedAt.AddDays(-1)
    };

    private static Tenant CreateTenant() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Webhook Materialization Test Tenant",
        Slug = $"webhook-materialization-{Guid.NewGuid():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = MaterializedAt.AddDays(-1)
    };

    private sealed record SeededAuthority(
        Guid TenantId,
        Guid ConsumerId,
        WebhookEndpoint Endpoint,
        Guid BindingId,
        Guid AggregateId,
        string EventId);
}
