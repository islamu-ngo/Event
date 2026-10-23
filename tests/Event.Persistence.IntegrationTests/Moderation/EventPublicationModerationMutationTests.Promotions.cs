using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Exceptions;
using Explore.Application.Features.Promotions;
using Explore.Application.Features.Promotions.Handlers.Commands;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventPublicationModerationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task QualifiedHostWithoutOrWithUnrestrictedHumanAdoptionPublishesPromotion(bool adopted)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreatePromotionPublicationFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedPublicationPromotionAsync(fixture, parent.Id, clock);
        if (adopted) await AdoptAsync(fixture, clock);
        else await SeedQualifiedTicketPublicationHostAsync(fixture);

        var response = await PublishPromotionAsync(fixture, parent.Id, draft.Id);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.Promotion).IsNotNull();
        await Assert.That(response.Promotion!.DefinitionId).IsEqualTo(draft.Id);
        await Assert.That(response.Promotion.EventId).IsEqualTo(parent.Id);
        await Assert.That(response.Promotion.StatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Published);
        await Assert.That(response.Promotion.PromotionCodeDisplayLabel).IsEqualTo("****LICATION");
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.PromotionDefinitions.AsNoTracking().SingleAsync(row => row.Id == draft.Id);
        await Assert.That(persisted.PromotionDefinitionStatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Published);
        await Assert.That(persisted.PublishedAtUtc).IsEqualTo((DateTime?)clock.Now.UtcDateTime);
        var code = await context.PromotionCodes.AsNoTracking().SingleAsync(row => row.PromotionDefinitionVersionId == draft.Id);
        await Assert.That(code.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(code.ScopeMetadata).IsEqualTo(draft.ScopeMetadata);
        await Assert.That(code.DisplayLabel).IsEqualTo("****LICATION");
        await Assert.That((await context.Events.SingleAsync(row => row.Id == parent.Id)).EventStatusId)
            .IsEqualTo((int)EventStatusEnum.Draft);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CurrentHumanRestrictionBlocksDraftPromotionPublication(bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedPublicationPromotionAsync(fixture, parent.Id, clock);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);

        var response = await PublishPromotionAsync(fixture, parent.Id, draft.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("promotion_publish_restricted");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        await Assert.That(response.Promotion).IsNull();
        await AssertPublicationPromotionUnchangedAsync(fixture, draft);
    }

    [Test]
    public async Task AdoptedUnavailableHumanAuthorityBlocksPromotionBeforePublication()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreatePromotionPublicationFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedPublicationPromotionAsync(fixture, parent.Id, clock);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();

        var response = await PublishPromotionAsync(fixture, parent.Id, draft.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("promotion_publish_authority_unavailable");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        await Assert.That(response.Promotion).IsNull();
        await AssertPublicationPromotionUnchangedAsync(fixture, draft);
    }

    [Test]
    public async Task CurrentHumanRestrictionPrecedesMissingPromotionDefinitionLookup()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);

        var response = await PublishPromotionAsync(fixture, parent.Id, Guid.CreateVersion7());

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("promotion_publish_restricted");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        await Assert.That(response.Promotion).IsNull();
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await context.PromotionDefinitions.AnyAsync()).IsFalse();
        await Assert.That(await context.PromotionCodes.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnrestrictedHumanAdoptionDoesNotPublishAnotherEventOrTenantsPromotion(bool otherTenant)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreatePromotionPublicationFixtureAsync(privacy, clock);
        var requestedEvent = await ReadyDraftAsync(fixture);
        var draft = otherTenant
            ? await SeedForeignPublicationPromotionAsync(fixture, clock)
            : await SeedPublicationPromotionAsync(fixture, (await ReadyDraftAsync(fixture)).Id, clock);
        await AdoptAsync(fixture, clock);

        var eventId = otherTenant ? draft.ScopeMetadata.EventId : requestedEvent.Id;
        var response = await PublishPromotionAsync(fixture, eventId, draft.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("promotion_management_not_found");
        await Assert.That(response.Id).IsEqualTo(Guid.Empty);
        await Assert.That(response.Promotion).IsNull();
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.PromotionDefinitions.IgnoreQueryFilters(["Tenant"]).AsNoTracking()
            .SingleAsync(row => row.TenantId == draft.TenantId && row.Id == draft.Id);
        await Assert.That(persisted.PromotionDefinitionStatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Draft);
        await Assert.That(persisted.PublishedAtUtc).IsNull();
        await Assert.That(await context.PromotionCodes.IgnoreQueryFilters(["Tenant"])
            .AnyAsync(row => row.TenantId == draft.TenantId && row.PromotionDefinitionVersionId == draft.Id)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DigestProviderFailureAfterDefinitionPublishLeavesNoDurablePromotionOrCode(bool argumentFailure)
    {
        var clock = new PublicationClock();
        var secrets = new PublicationPromotionSecrets(argumentFailure);
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreatePromotionPublicationFixtureAsync(privacy, clock, secrets);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedPublicationPromotionAsync(fixture, parent.Id, clock);
        await AdoptAsync(fixture, clock);
        secrets.ObservePublishedDefinition = () => fixture.Context.ChangeTracker.Entries<PromotionDefinition>()
            .Any(entry => entry.Entity.Id == draft.Id
                && entry.Entity.PromotionDefinitionStatusId == (int)PromotionDefinitionStatusEnum.Published);

        var response = await PublishPromotionAsync(fixture, parent.Id, draft.Id);

        await Assert.That(secrets.ObservedPublishedDefinition).IsTrue();
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("promotion_management_validation_failed");
        await Assert.That(response.Promotion).IsNull();
        await AssertPublicationPromotionUnchangedAsync(fixture, draft);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OwningCommitRecheckRollsBackPublishedPromotionAndCode(bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        var checkpoint = new AfterPromotionPublicationFlush(clock, unavailable);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            ConfigureServices(services, () => privacy!, clock, [checkpoint]);
            services.AddSingleton<ISecretResolver>(new PublicationPromotionSecrets());
        });
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedPublicationPromotionAsync(fixture, parent.Id, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var startsAt = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, startsAt);
        checkpoint.DefinitionId = draft.Id;
        checkpoint.Crossing = startsAt;
        checkpoint.Armed = true;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await PublishPromotionAsync(fixture, parent.Id, draft.Id));

        await Assert.That(checkpoint.ObservedPublishedDefinitionAndCode).IsTrue();
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await AssertPublicationPromotionUnchangedAsync(fixture, draft);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private static Task<EventVisitorCapabilitySqliteFixture> CreatePromotionPublicationFixtureAsync(
        ModerationNoticePrivacyFixture privacy, PublicationClock clock, PublicationPromotionSecrets? secrets = null) =>
        EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            ConfigureServices(services, () => privacy, clock, []);
            services.AddSingleton<ISecretResolver>(secrets ?? new PublicationPromotionSecrets());
        });

    // Direct concrete-handler calls test native mutation; ordinary decorated commerce authorization is separate.
    private static Task<PromotionManagementCommandResponseDto> PublishPromotionAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid eventId, Guid definitionId) =>
        fixture.Services.GetRequiredService<PublishPromotionCommandHandler>()
            .ExecuteAsync(new PublishPromotionCommand(eventId, definitionId, "NATIVE-PUBLICATION"), CancellationToken.None);

    private static async Task<PromotionDefinition> SeedPublicationPromotionAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid eventId, PublicationClock clock)
    {
        var catalog = await SeedDraftTicketCatalogAsync(fixture, eventId);
        var definition = PromotionDefinition.CreateDraft(
            PromotionScopeMetadata.Create(fixture.TenantId, eventId, catalog.Id, catalog.VersionNumber, "USD"),
            "Native publication promotion", PromotionEligibility.AllTickets(),
            PromotionDiscountRule.FixedMinor("USD", 100, null), clock.Now.UtcDateTime.AddDays(-1),
            clock.Now.UtcDateTime.AddDays(1), 10, 1);
        var repository = fixture.Services.GetRequiredService<IPromotionManagementRepository>();
        await repository.AddDefinitionAsync(definition, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return definition;
    }

    private static async Task<PromotionDefinition> SeedForeignPublicationPromotionAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock)
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        fixture.Context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            FullName = "Other promotion owner",
            Slug = $"promotion-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        fixture.Context.Events.Add(new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = "Other tenant promotion event",
            TenantId = tenantId,
            Tenant = null!,
            ActorId = fixture.ActorId,
            Actor = null!,
            OrganizerActorId = fixture.ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            CreatedAt = clock.Now.UtcDateTime
        });
        var catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        fixture.Context.EventTicketCatalogVersions.Add(catalog);
        var definition = PromotionDefinition.CreateDraft(
            PromotionScopeMetadata.Create(tenantId, eventId, catalog.Id, 1, "USD"),
            "Other tenant promotion", PromotionEligibility.AllTickets(), PromotionDiscountRule.FixedMinor("USD", 100, null),
            clock.Now.UtcDateTime.AddDays(-1), clock.Now.UtcDateTime.AddDays(1), 10, 1);
        var repository = fixture.Services.GetRequiredService<IPromotionManagementRepository>();
        await repository.AddDefinitionAsync(definition, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return definition;
    }

    private static async Task AssertPublicationPromotionUnchangedAsync(
        EventVisitorCapabilitySqliteFixture fixture, PromotionDefinition draft)
    {
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.PromotionDefinitions.AsNoTracking().SingleAsync(row => row.Id == draft.Id);
        await Assert.That(persisted.PromotionDefinitionStatusId).IsEqualTo((int)PromotionDefinitionStatusEnum.Draft);
        await Assert.That(persisted.PublishedAtUtc).IsNull();
        await Assert.That(await context.PromotionCodes.AnyAsync(row => row.PromotionDefinitionVersionId == draft.Id)).IsFalse();
        await Assert.That((await context.Events.SingleAsync(row => row.Id == draft.ScopeMetadata.EventId)).EventStatusId)
            .IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(await context.OutboxMessages.AnyAsync(row => row.AggregateId == draft.ScopeMetadata.EventId)).IsFalse();
        await Assert.That(await context.PdsSyncOutbox.AnyAsync(row => row.SourceEntityId == draft.ScopeMetadata.EventId)).IsFalse();
    }

    private sealed class AfterPromotionPublicationFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        public Guid DefinitionId { get; set; }
        public DateTimeOffset Crossing { get; set; }
        public bool Armed { get; set; }
        public bool ObservedPublishedDefinitionAndCode { get; private set; }
        public bool? ObservedPrivacyOwner { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            if (!await context.PromotionCodes.AsNoTracking()
                    .AnyAsync(row => row.PromotionDefinitionVersionId == DefinitionId, cancellationToken)) return result;
            Armed = false;
            ObservedPublishedDefinitionAndCode = await context.PromotionDefinitions.AsNoTracking()
                .AnyAsync(row => row.Id == DefinitionId
                    && row.PromotionDefinitionStatusId == (int)PromotionDefinitionStatusEnum.Published
                    && row.PublishedAtUtc != null, cancellationToken);
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }

    private sealed class PublicationPromotionSecrets(bool? argumentFailure = null) : ISecretResolver
    {
        private readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public Func<bool>? ObservePublishedDefinition { get; set; }
        public bool ObservedPublishedDefinition { get; private set; }

        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId,
            CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId,
            string qualifier, CancellationToken cancellationToken = default)
        {
            if (settingKey != SecretDefinitionRegistry.Keys.Promotions.CodeLookupHmacKey
                || scope != SecretScope.Instance || scopeId is not null || qualifier != "v1")
                return Task.FromResult(SecretResolutionResult.Unconfigured);
            ObservedPublishedDefinition = ObservePublishedDefinition?.Invoke() == true;
            if (argumentFailure == true) throw new ArgumentException("Native digest provider rejected publication input.");
            if (argumentFailure == false) throw new InvalidOperationException("Native digest provider is unavailable.");
            return Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, _key,
                SecretSourceType.EnvironmentVariable, scope, scopeId, DateTime.UtcNow)));
        }

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId,
            CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
