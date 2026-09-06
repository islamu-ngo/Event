// ABOUTME: PostgreSQL integration tests for atomic provider-publication claims and fenced completion.
// ABOUTME: Verifies entity-returning claims, bounded concurrency, tenant isolation, and immutable uniqueness.

using System.Diagnostics;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookPublicationClaimRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime PreparedAt =
        new(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ConcurrentDueClaims_ReturnEachEntityOnceAndHonorBatchLimit()
    {
        var scenario = await SeedAsync(publicationCount: 3);
        var baseline = new PersistenceQueryBaselineInterceptor();
        await using var firstContext = fixture.CreateDbContext(baseline);
        await using var secondContext = fixture.CreateDbContext(baseline);
        var request = new WebhookProviderPublicationClaimRequest(
            BatchSize: 2,
            LeaseOwner: "publication-worker",
            ClaimedAt: PreparedAt.AddMinutes(1),
            LeaseDuration: TimeSpan.FromMinutes(2),
            MaxAutomaticAttempts: 3);

        var elapsed = Stopwatch.StartNew();
        var results = await Task.WhenAll(
            new WebhookProviderPublicationRepository(firstContext)
                .ClaimDueAsync(request, CancellationToken.None),
            new WebhookProviderPublicationRepository(secondContext)
                .ClaimDueAsync(request, CancellationToken.None));
        elapsed.Stop();
        var claims = results.SelectMany(result => result).ToArray();
        PersistenceQueryBaselineEvidence.Record(baseline
            .Snapshot("webhook_concurrent_due_claims", claims.Length, elapsed.Elapsed));

        await Assert.That(results[0].Count).IsLessThanOrEqualTo(2);
        await Assert.That(results[1].Count).IsLessThanOrEqualTo(2);
        await Assert.That(claims.Length).IsEqualTo(3);
        await Assert.That(claims.Select(claim => claim.Publication.Id).Distinct().Count()).IsEqualTo(3);
        await Assert.That(claims.All(claim => claim.Publication.Status == WebhookProviderPublicationStatus.Publishing))
            .IsTrue();
        await Assert.That(claims.All(claim => claim.PublicationFence == 1)).IsTrue();
        await Assert.That(claims.All(claim => claim.Publication.Attempts.Last().Outcome ==
            WebhookProviderPublicationAttemptOutcome.PublishingStarted)).IsTrue();

        await using var verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.WebhookProviderPublications
                .CountAsync(publication => publication.TenantId == scenario.TenantId &&
                    publication.StatusId == (int)WebhookProviderPublicationStatus.Publishing))
            .IsEqualTo(3);
        await Assert.That(await verificationContext.WebhookProviderPublicationAttempts
                .CountAsync(attempt => attempt.TenantId == scenario.TenantId))
            .IsEqualTo(3);
    }

    [Test]
    public async Task ClaimDue_WithRetryingExecutionStrategy_ClaimsPublicationAsRetriableUnit()
    {
        var scenario = await SeedAsync(publicationCount: 1);
        await using var context = CreateRetryingDbContext();

        var claims = await new WebhookProviderPublicationRepository(context).ClaimDueAsync(
            new WebhookProviderPublicationClaimRequest(
                BatchSize: 1,
                LeaseOwner: "publication-retrying-worker",
                ClaimedAt: PreparedAt.AddMinutes(1),
                LeaseDuration: TimeSpan.FromMinutes(2),
                MaxAutomaticAttempts: 3),
            CancellationToken.None);

        await Assert.That(claims.Count).IsEqualTo(1);
        await Assert.That(claims[0].Publication.Id).IsEqualTo(scenario.Publications.Single().Id);
        await Assert.That(claims[0].PublicationFence).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentUnknownClaims_ReturnOneEntityAndAppendReconciliationEvidence()
    {
        var scenario = await SeedAsync(publicationCount: 1, makeUnknown: true);
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var request = new WebhookProviderPublicationClaimRequest(
            BatchSize: 1,
            LeaseOwner: "reconciliation-worker",
            ClaimedAt: PreparedAt.AddMinutes(5),
            LeaseDuration: TimeSpan.FromMinutes(2),
            MaxAutomaticAttempts: 1);

        var results = await Task.WhenAll(
            new WebhookProviderPublicationRepository(firstContext)
                .ClaimUnknownAsync(request, CancellationToken.None),
            new WebhookProviderPublicationRepository(secondContext)
                .ClaimUnknownAsync(request, CancellationToken.None));
        var claims = results.SelectMany(result => result).ToArray();

        await Assert.That(claims.Length).IsEqualTo(1);
        await Assert.That(claims[0].Publication.Id).IsEqualTo(scenario.Publications.Single().Id);
        await Assert.That(claims[0].Publication.Status)
            .IsEqualTo(WebhookProviderPublicationStatus.PublicationUnknown);
        await Assert.That(claims[0].Publication.AutomaticReconciliationAttemptCount).IsEqualTo(1);
        await Assert.That(claims[0].Publication.Attempts.Last().Outcome)
            .IsEqualTo(WebhookProviderPublicationAttemptOutcome.AutomaticReconciliationStarted);

        await using var verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.WebhookProviderPublicationAttempts
                .CountAsync(attempt => attempt.WebhookProviderPublicationId == scenario.Publications.Single().Id))
            .IsEqualTo(3);
    }

    [Test]
    public async Task ConcurrentCompletion_RejectsStaleVersionAndWrongTenantLookup()
    {
        var scenario = await SeedAsync(publicationCount: 1);
        var publicationId = scenario.Publications.Single().Id;
        WebhookProviderPublicationClaim claim;
        await using (var claimContext = fixture.CreateDbContext())
        {
            var claims = await new WebhookProviderPublicationRepository(claimContext)
                .ClaimDueAsync(
                    new WebhookProviderPublicationClaimRequest(
                        BatchSize: 1,
                        LeaseOwner: "publication-worker",
                        ClaimedAt: PreparedAt.AddMinutes(1),
                        LeaseDuration: TimeSpan.FromMinutes(5),
                        MaxAutomaticAttempts: 2),
                    CancellationToken.None);
            claim = claims.Single();
        }

        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var firstRepository = new WebhookProviderPublicationRepository(firstContext);
        var secondRepository = new WebhookProviderPublicationRepository(secondContext);
        var first = await firstRepository.GetActiveClaimAsync(
            scenario.TenantId,
            publicationId,
            claim.LeaseToken,
            claim.PublicationFence,
            PreparedAt.AddMinutes(2),
            CancellationToken.None);
        var second = await secondRepository.GetActiveClaimAsync(
            scenario.TenantId,
            publicationId,
            claim.LeaseToken,
            claim.PublicationFence,
            PreparedAt.AddMinutes(2),
            CancellationToken.None);
        var wrongTenant = await secondRepository.GetActiveClaimAsync(
            scenario.OtherTenantId,
            publicationId,
            claim.LeaseToken,
            claim.PublicationFence,
            PreparedAt.AddMinutes(2),
            CancellationToken.None);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(wrongTenant).IsNull();

        first!.MarkProviderQueued(
            claim.LeaseToken,
            claim.PublicationFence,
            "provider-message-first",
            PreparedAt.AddMinutes(3));
        second!.MarkProviderQueued(
            claim.LeaseToken,
            claim.PublicationFence,
            "provider-message-stale",
            PreparedAt.AddMinutes(3));
        await firstRepository.UpdateAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<WebhookProviderPublicationConcurrencyException>(async () =>
            await secondRepository.UpdateAsync(second, CancellationToken.None));

        await using var verificationContext = fixture.CreateDbContext();
        var stored = await verificationContext.WebhookProviderPublications
            .AsNoTracking()
            .SingleAsync(publication => publication.Id == publicationId);
        await Assert.That(stored.StatusId).IsEqualTo((int)WebhookProviderPublicationStatus.ProviderQueued);
        await Assert.That(stored.ExternalProviderMessageId).IsEqualTo("provider-message-first");
        await Assert.That(await verificationContext.WebhookProviderPublicationAttempts
                .CountAsync(attempt => attempt.WebhookProviderPublicationId == publicationId))
            .IsEqualTo(2);
    }

    [Test]
    public async Task DuplicateMessageProviderBinding_WithFreshIdentity_IsRejected()
    {
        var scenario = await SeedAsync(publicationCount: 1);
        var existing = scenario.Publications.Single();
        await using var context = fixture.CreateDbContext();
        var repository = new WebhookProviderPublicationRepository(context);
        var duplicate = CreatePublication(
            scenario.TenantId,
            existing.WebhookMessageId,
            existing.DeliveryPlanId,
            scenario.BindingId,
            "fresh-provider-event",
            "fresh-idempotency-key");

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await repository.CreateAsync(duplicate, CancellationToken.None));

        await using var verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.WebhookProviderPublications
                .CountAsync(publication => publication.TenantId == scenario.TenantId))
            .IsEqualTo(1);
    }

    [Test]
    public async Task ListByTenant_FiltersMessageAndStatusWithoutCrossTenantLeakage()
    {
        var scenario = await SeedAsync(publicationCount: 2, makeUnknown: true);
        var selected = scenario.Publications[0];
        await using var context = fixture.CreateDbContext();
        var repository = new WebhookProviderPublicationRepository(context);

        var result = await repository.ListByTenantAsync(
            scenario.TenantId,
            selected.WebhookMessageId,
            null,
            (int)WebhookProviderPublicationStatus.PublicationUnknown,
            10,
            CancellationToken.None);
        var otherTenantResult = await repository.ListByTenantAsync(
            scenario.OtherTenantId,
            selected.WebhookMessageId,
            null,
            (int)WebhookProviderPublicationStatus.PublicationUnknown,
            10,
            CancellationToken.None);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Id).IsEqualTo(selected.Id);
        await Assert.That(result[0].WebhookDeliveryPlanSnapshot).IsNotNull();
        await Assert.That(result[0].Attempts.Count).IsEqualTo(2);
        await Assert.That(otherTenantResult).IsEmpty();
    }

    private async Task<SeededScenario> SeedAsync(int publicationCount, bool makeUnknown = false)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("publication-claim");
        var otherTenant = CreateTenant("publication-other");
        var consumer = CreateConsumer(tenant.Id);
        var binding = CreateBinding(tenant.Id, consumer.Id);
        context.Tenants.AddRange(tenant, otherTenant);
        context.WebhookConsumers.Add(consumer);
        context.WebhookConsumerProviderBindings.Add(binding);

        var publications = new List<SeededPublication>(publicationCount);
        for (var index = 0; index < publicationCount; index++)
        {
            var message = CreateMessage(tenant.Id, consumer.Id, $"event-{index}");
            var plan = CreatePlan(tenant.Id, message.Id, consumer.Id);
            var publication = CreatePublication(
                tenant.Id,
                message.Id,
                plan.Id,
                binding.Id,
                $"provider-event-{index}",
                $"idempotency-{index}");
            if (makeUnknown)
            {
                var leaseToken = Guid.CreateVersion7();
                publication.ClaimForPublishing(
                    "seed-worker",
                    leaseToken,
                    PreparedAt.AddMinutes(3),
                    PreparedAt.AddMinutes(1),
                    maxAutomaticPublicationAttempts: 2);
                publication.MarkPublicationUnknown(
                    leaseToken,
                    publication.PublicationFence,
                    "acceptance_timeout",
                    null,
                    PreparedAt.AddMinutes(4),
                    PreparedAt.AddMinutes(2));
            }

            context.WebhookMessages.Add(message);
            context.WebhookDeliveryPlanSnapshots.Add(plan);
            context.WebhookProviderPublications.Add(publication);
            publications.Add(new SeededPublication(publication.Id, message.Id, plan.Id));
        }

        await context.SaveChangesAsync();
        return new SeededScenario(tenant.Id, otherTenant.Id, binding.Id, publications);
    }

    private static WebhookProviderPublication CreatePublication(
        Guid tenantId,
        Guid messageId,
        Guid planId,
        Guid bindingId,
        string providerEventId,
        string idempotencyKey) =>
        WebhookProviderPublication.Create(
            tenantId,
            messageId,
            planId,
            WebhookProviderKind.Svix,
            bindingId,
            "1.84.0",
            providerEventId,
            idempotencyKey,
            $"sha256:{new string('a', 64)}",
            "consumer-application-uid",
            "provider-application-id",
            "managed-eu",
            "secret:webhook-provider",
            "credential-v1",
            WebhookProviderMode.Svix,
            "provider-config-v1",
            eventContractVersion: 1,
            "retention-v1",
            PreparedAt.AddDays(7),
            PreparedAt.AddDays(30),
            PreparedAt.AddHours(12),
            PreparedAt);

    private static WebhookMessage CreateMessage(Guid tenantId, Guid consumerId, string eventId) =>
        WebhookMessage.Create(
            tenantId,
            "event.updated",
            eventId,
            "event",
            Guid.CreateVersion7(),
            consumerId,
            Encoding.UTF8.GetBytes($"{{\"id\":\"{eventId}\"}}"),
            "application/json",
            "utf-8",
            PreparedAt.AddMinutes(-1),
            PreparedAt.AddDays(7),
            PreparedAt);

    private static WebhookDeliveryPlanSnapshot CreatePlan(
        Guid tenantId,
        Guid messageId,
        Guid consumerId) =>
        WebhookDeliveryPlanSnapshot.Create(
            tenantId,
            messageId,
            consumerId,
            WebhookProviderMode.Svix,
            "configuration-v1",
            "contract-v1",
            "default",
            "retention-v1",
            new DateTimeOffset(PreparedAt.AddDays(7)),
            new DateTimeOffset(PreparedAt.AddDays(30)),
            new DateTimeOffset(PreparedAt.AddDays(90)),
            new DateTimeOffset(PreparedAt.AddDays(90)),
            new DateTimeOffset(PreparedAt.AddDays(30)),
            new DateTimeOffset(PreparedAt));

    private static WebhookConsumerProviderBinding CreateBinding(Guid tenantId, Guid consumerId)
    {
        var profile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            "1.84.0",
            WebhookProviderCapability.EndpointManagement,
            "svix-1.84.0-v1",
            new DateTimeOffset(PreparedAt));

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
        Name = $"Publication Consumer {Guid.NewGuid():N}",
        Status = WebhookConsumerStatus.Active,
        ProviderMode = WebhookProviderMode.Svix,
        CreatedAt = PreparedAt
    };

    private static Tenant CreateTenant(string slugPrefix) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Webhook Publication Test Tenant",
        Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = PreparedAt
    };

    private ExploreDbContext CreateRetryingDbContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Retry-enabled provider publication claim integration test.");
        return context;
    }

    private sealed record SeededScenario(
        Guid TenantId,
        Guid OtherTenantId,
        Guid BindingId,
        IReadOnlyList<SeededPublication> Publications);

    private sealed record SeededPublication(Guid Id, Guid WebhookMessageId, Guid DeliveryPlanId);
}
