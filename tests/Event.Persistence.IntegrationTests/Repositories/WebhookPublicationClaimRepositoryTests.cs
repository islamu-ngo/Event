using System.Diagnostics;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Services.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookPublicationClaimRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime PreparedAt =
        new(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeRecoveryCommandDeniesUserOwnerDespiteTenantAdminStanding(bool abandon)
    {
        var admin = Substitute.For<IAdminContext>();
        await using var host = await EventVisitorCapabilitySqliteFixture.CreateAsync(
            services => services.AddScoped<IAdminContext>(_ => admin));
        admin.UserId.Returns(host.UserId);
        admin.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns((Guid?)host.UserId);
        admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        admin.IsTenantAdminAsync(host.TenantId, Arg.Any<CancellationToken>()).Returns(true);
        var context = host.Context;
        Guid ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            Pii = new UserPii { Email = "native-owner@example.test", FirstName = "Native", LastName = "Owner" }
        });
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = host.TenantId,
            Tenant = null!,
            UserId = ownerId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        var consumer = CreateConsumer(host.TenantId);
        consumer.ConsumerKind = WebhookConsumerKind.User;
        consumer.OwnerUserId = ownerId;
        var binding = CreateBinding(host.TenantId, consumer.Id);
        var message = CreateMessage(host.TenantId, consumer.Id, $"owner-recovery-{Guid.CreateVersion7():N}");
        var plan = CreatePlan(host.TenantId, message.Id, consumer.Id);
        var publication = CreatePublication(host.TenantId, message.Id, plan.Id, binding.Id,
            $"provider-{Guid.CreateVersion7():N}", $"transport-{Guid.CreateVersion7():N}");
        Guid token = Guid.CreateVersion7();
        publication.ClaimForPublishing("setup", token, PreparedAt.AddMinutes(3), PreparedAt.AddMinutes(1), 2);
        publication.MarkPublicationUnknown(token, publication.PublicationFence, "setup_unknown",
            null, PreparedAt.AddMinutes(4), PreparedAt.AddMinutes(2));
        publication.RequireManualReconciliation("setup_manual", null, PreparedAt.AddMinutes(3));
        context.AddRange(consumer, binding, message, plan, publication);
        await context.SaveChangesAsync();
        long version = publication.ConcurrencyVersion;
        int attempts = publication.Attempts.Count;
        int auditCount = await context.WebhookAuditEvents.CountAsync();
        context.ChangeTracker.Clear();

        async Task RecoverAsync(IServiceProvider services)
        {
            if (abandon)
                await services.GetRequiredService<ICommandHandler<AbandonWebhookProviderPublicationCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(new()
                    {
                        TenantId = host.TenantId,
                        PublicationId = publication.Id,
                        ActorUserId = host.UserId,
                        ExpectedConcurrencyVersion = version,
                        ReasonCode = "owner_recovery"
                    }, CancellationToken.None);
            else
                await services.GetRequiredService<ICommandHandler<ReconcileWebhookProviderPublicationCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(new()
                    {
                        TenantId = host.TenantId,
                        PublicationId = publication.Id,
                        ActorUserId = host.UserId,
                        ExpectedConcurrencyVersion = version,
                        ReasonCode = "owner_recovery",
                        ExternalProviderMessageId = "confirmed-provider-id"
                    }, CancellationToken.None);
        }

        await Assert.ThrowsAsync<AuthorizationException>(() => RecoverAsync(host.Services));
        var retained = await context.WebhookProviderPublications.AsNoTracking().SingleAsync(row => row.Id == publication.Id);
        await Assert.That(retained.Status).IsEqualTo(WebhookProviderPublicationStatus.ManualReconciliation);
        await Assert.That(retained.ConcurrencyVersion).IsEqualTo(version);
        await Assert.That(await context.WebhookProviderPublicationAttempts.CountAsync(row =>
            row.WebhookProviderPublicationId == publication.Id)).IsEqualTo(attempts);
        await Assert.That(await context.WebhookAuditEvents.CountAsync()).IsEqualTo(auditCount);

        var controlConsumer = await context.WebhookConsumers.SingleAsync(row => row.Id == consumer.Id);
        controlConsumer.ConsumerKind = WebhookConsumerKind.Tenant;
        controlConsumer.OwnerUserId = null;
        await context.SaveChangesAsync();
        await using var control = host.CreateScope();
        await RecoverAsync(control.ServiceProvider);
        var completed = await context.WebhookProviderPublications.AsNoTracking().SingleAsync(row => row.Id == publication.Id);
        await Assert.That(completed.Status).IsEqualTo(abandon
            ? WebhookProviderPublicationStatus.Abandoned : WebhookProviderPublicationStatus.ProviderQueued);
        await Assert.That(completed.ConcurrencyVersion).IsGreaterThan(version);
        await Assert.That(await context.WebhookAuditEvents.CountAsync()).IsEqualTo(auditCount + 1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task PublicationRecoveryResolvesOriginalUserOwnerAndRejectsForeignTenant(
        bool abandon, bool foreignTenant)
    {
        var scenario = await SeedAsync(publicationCount: 1, makeUnknown: true);
        Guid ownerId = Guid.CreateVersion7();
        await using (var writer = fixture.CreateDbContext())
        {
            var user = new User
            {
                Id = ownerId,
                Pii = new UserPii { Email = "publication-owner@example.test", FirstName = "Publication", LastName = "Owner" }
            };
            writer.Users.Add(user);
            writer.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = scenario.TenantId,
                Tenant = null!,
                UserId = ownerId,
                User = user,
                StatusId = (int)TenantUserStatusEnum.Active
            });
            var consumer = await writer.WebhookConsumers.SingleAsync(row => row.TenantId == scenario.TenantId);
            consumer.ConsumerKind = WebhookConsumerKind.User;
            consumer.OwnerUserId = ownerId;
            await writer.SaveChangesAsync();
        }
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(foreignTenant ? scenario.OtherTenantId : scenario.TenantId);
        await using var context = fixture.CreateDbContext();
        context.ClearTenantFilterBypass();
        context.TenantContext = tenant;
        var ownership = new WebhookOwnershipScopeResolver(
            tenant, Substitute.For<ICurrentUserService>(),
            Substitute.For<IInstanceBootstrapStateRepository>(),
            Substitute.For<IOrganizationRepository>(), Substitute.For<IGroupRepository>(),
            Substitute.For<ITenantUserRepository>(), new WebhookConsumerRepository(context),
            Substitute.For<IWebhookEndpointRepository>(), Substitute.For<IWebhookMessageRepository>(),
            Substitute.For<IWebhookDeliveryAttemptRepository>(),
            new WebhookProviderPublicationRepository(context));
        var resolver = new AuthorizationResourceContextResolver(
            webhookOwnershipScopeResolver: ownership, tenantContext: tenant);
        Guid publicationId = scenario.Publications.Single().Id;
        ISecureRequest command = abandon
            ? new AbandonWebhookProviderPublicationCommand { TenantId = tenant.TenantId, PublicationId = publicationId }
            : new ReconcileWebhookProviderPublicationCommand { TenantId = tenant.TenantId, PublicationId = publicationId };
        string action = abandon ? AuthorizationActions.Webhooks.AbandonPublication : AuthorizationActions.Webhooks.ReconcilePublication;
        if (foreignTenant)
        {
            await Assert.ThrowsAsync<AuthorizationException>(async () =>
                await resolver.ResolveAsync(command, ResourceKinds.Webhook, action,
                    publicationId.ToString("D"), command.AuthorizationFacts, CancellationToken.None));
        }
        else
        {
            var resolved = await resolver.ResolveAsync(command, ResourceKinds.Webhook, action,
                publicationId.ToString("D"), command.AuthorizationFacts, CancellationToken.None);
            await Assert.That(resolved.Facts).IsTypeOf<WebhookOwnershipAuthorizationFacts>();
            var facts = (WebhookOwnershipAuthorizationFacts)resolved.Facts!;
            await Assert.That(facts.Kind).IsEqualTo(WebhookConsumerKind.User);
            await Assert.That(facts.UserId).IsEqualTo(ownerId);
            await Assert.That(facts.TenantId).IsEqualTo(scenario.TenantId);
        }
    }

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
