using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventAddOns.Handlers.Commands;
using Explore.Application.Features.EventAddOns.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventPublicationModerationMutationTests
{
    [Test]
    public async Task QualifiedHostWithoutHumanAdoptionPublishesAddOnItemsAndRetiresPriorCatalog()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var prior = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, published: true);
        var draft = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, version: 2);
        await SeedQualifiedTicketPublicationHostAsync(fixture);

        var response = await PublishAddOnCatalogAsync(fixture, parent.Id, clock);

        await AssertPublishedAddOnResponseAsync(response, parent.Id, draft);
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var repository = freshScope.ServiceProvider.GetRequiredService<IEventAddOnRepository>();
        var published = await repository.GetPublishedCatalogAsync(fixture.TenantId, parent.Id, CancellationToken.None);
        await Assert.That(published).IsNotNull();
        await Assert.That(published!.Id).IsEqualTo(draft.Id);
        await Assert.That(published.PublishedAt).IsEqualTo((DateTime?)clock.Now.UtcDateTime);
        await Assert.That(published.ConcurrencyStamp).IsNotEqualTo(draft.ConcurrencyStamp);
        var retired = await context.EventAddOnCatalogVersions.AsNoTracking().SingleAsync(row => row.Id == prior.Id);
        await Assert.That(retired.RetiredAt).IsEqualTo((DateTime?)clock.Now.UtcDateTime);
        await Assert.That(retired.PublishedAt).IsEqualTo(prior.PublishedAt);
        await Assert.That(retired.ConcurrencyStamp).IsNotEqualTo(prior.ConcurrencyStamp);
        await Assert.That(await context.EventTicketCatalogVersions.AnyAsync(row => row.EventId == parent.Id)).IsFalse();
        await AssertUnpublishedAsync(fixture, parent);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CurrentHumanRestrictionBlocksAddOnCallbackBeforeEmptyCatalogValidationAndPriorRetirement(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var prior = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, published: true);
        var draft = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, version: 2, withItem: false);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);

        var response = await PublishAddOnCatalogAsync(fixture, parent.Id, clock);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_add_on_publish_restricted");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        await Assert.That(response.Catalog).IsNull();
        await AssertAddOnPublicationUnchangedAsync(fixture, draft, prior);
    }

    [Test]
    public async Task AdoptedUnknownHumanAuthorityBlocksAddOnCallbackBeforeEmptyCatalogDomainValidation()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var prior = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, published: true);
        var draft = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, version: 2, withItem: false);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();

        var response = await PublishAddOnCatalogAsync(fixture, parent.Id, clock);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_add_on_publish_authority_unavailable");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        await Assert.That(response.Catalog).IsNull();
        await AssertAddOnPublicationUnchangedAsync(fixture, draft, prior);
    }

    [Test]
    public async Task AnotherHumansRestrictionDoesNotReplaceTheCurrentAddOnPublishersAuthority()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var otherHuman = NativeUser(Guid.CreateVersion7());
        fixture.Context.Users.Add(otherHuman);
        await fixture.Context.SaveChangesAsync();
        await StoreRestrictionAsync(fixture, adoption, otherHuman.Id, clock.Now);

        var response = await PublishAddOnCatalogAsync(fixture, parent.Id, clock);

        await AssertPublishedAddOnResponseAsync(response, parent.Id, draft);
        await using var freshScope = fixture.CreateScope();
        var repository = freshScope.ServiceProvider.GetRequiredService<IEventAddOnRepository>();
        var published = await repository.GetPublishedCatalogAsync(fixture.TenantId, parent.Id, CancellationToken.None);
        await Assert.That(published).IsNotNull();
        await Assert.That(published!.Id).IsEqualTo(draft.Id);
        await Assert.That(published.PublishedAt).IsEqualTo((DateTime?)clock.Now.UtcDateTime);
        await AssertUnpublishedAsync(fixture, parent);
    }

    [Test]
    [Arguments("wrong_tenant")]
    [Arguments("other_owner")]
    [Arguments("missing_event")]
    [Arguments("missing_draft")]
    public async Task UnrestrictedHumanAdoptionPreservesNativeAddOnManagementDenials(string denial)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = denial == "wrong_tenant"
            ? await SeedForeignAddOnPublicationEventAsync(fixture, clock)
            : await ReadyDraftAsync(fixture);
        var draft = denial == "missing_draft"
            ? null
            : await SeedPublicationAddOnCatalogAsync(fixture, parent, clock);
        if (denial == "other_owner")
        {
            var otherHuman = NativeUser(Guid.CreateVersion7());
            var otherActor = new Actor
            {
                Id = Guid.CreateVersion7(), UserId = otherHuman.Id, User = otherHuman,
                ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "Other add-on owner" }
            };
            fixture.Context.Actors.Add(otherActor);
            var tracked = await fixture.Context.Events.SingleAsync(row => row.Id == parent.Id);
            tracked.ActorId = otherActor.Id;
            tracked.OrganizerActorId = otherActor.Id;
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();
        }
        await AdoptAsync(fixture, clock);
        var eventId = denial == "missing_event" ? Guid.CreateVersion7() : parent.Id;

        var response = await PublishAddOnCatalogAsync(fixture, eventId, clock);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_add_on_management_unavailable");
        await Assert.That(response.Id).IsEqualTo(eventId);
        await Assert.That(response.Catalog).IsNull();
        if (draft is not null) await AssertAddOnPublicationUnchangedAsync(fixture, draft);
        else
        {
            await using var freshScope = fixture.CreateScope();
            var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await context.EventAddOnCatalogVersions.AnyAsync(row => row.EventId == parent.Id)).IsFalse();
        }
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    [Test]
    public async Task EmptyAddOnCatalogDomainRejectionEscapesAndRollsBackPriorRetirement()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var prior = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, published: true);
        var draft = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, version: 2, withItem: false);
        await AdoptAsync(fixture, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            _ = await PublishAddOnCatalogAsync(fixture, parent.Id, clock));

        await AssertAddOnPublicationUnchangedAsync(fixture, draft, prior);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AddOnOwningCommitRecheckRollsBackFlushedPublicationRetirementAndBothStamps(
        bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        var checkpoint = new AfterAddOnPublicationFlush(clock, unavailable);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            ConfigureServices(services, () => privacy!, clock, [checkpoint]));
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var parent = await ReadyDraftAsync(fixture);
        var prior = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, published: true);
        var draft = await SeedPublicationAddOnCatalogAsync(fixture, parent, clock, version: 2);
        var adoption = await AdoptAsync(fixture, clock);
        var startsAt = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, startsAt);
        checkpoint.DraftId = draft.Id;
        checkpoint.PriorId = prior.Id;
        checkpoint.DraftStamp = draft.ConcurrencyStamp;
        checkpoint.PriorStamp = prior.ConcurrencyStamp;
        checkpoint.Crossing = startsAt;
        checkpoint.Armed = true;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await PublishAddOnCatalogAsync(fixture, parent.Id, clock));

        await Assert.That(checkpoint.ObservedPublishedDraftAndRetiredPrior).IsTrue();
        await Assert.That(checkpoint.ObservedBothChangedStamps).IsTrue();
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await AssertAddOnPublicationUnchangedAsync(fixture, draft, prior);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    // Direct concrete-handler calls exercise native access and transactions, not decorated authorization.
    private static Task<EventAddOnPublicationResponse> PublishAddOnCatalogAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid eventId, PublicationClock clock) =>
        ActivatorUtilities.CreateInstance<PublishEventAddOnCatalogCommandHandler>(fixture.Services)
            .ExecuteAsync(new PublishEventAddOnCatalogCommand(eventId, clock.Now.UtcDateTime), CancellationToken.None);

    private static async Task<EventAddOnCatalogVersion> SeedPublicationAddOnCatalogAsync(
        EventVisitorCapabilitySqliteFixture fixture, Explore.Domain.Event parent, PublicationClock clock,
        int version = 1, bool published = false, bool withItem = true)
    {
        var catalog = EventAddOnCatalogVersion.Create(parent.TenantId, parent.Id, "USD", version);
        if (withItem)
            catalog.AddItem(EventAddOnCatalogItem.Create(Guid.CreateVersion7(), parent.TenantId, catalog.Id,
                "Native publication meal", "Meal at the venue", Money.Create(250, "USD"), 10,
                "Collect at the venue", "Refunds through organizer support"));
        if (published) catalog.Publish(clock.Now.UtcDateTime.AddHours(-1));
        await fixture.Services.GetRequiredService<IEventAddOnRepository>().AddCatalogAsync(catalog, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return catalog;
    }

    private static async Task<Explore.Domain.Event> SeedForeignAddOnPublicationEventAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock)
    {
        var tenantId = Guid.CreateVersion7();
        fixture.Context.Tenants.Add(new Tenant
        {
            Id = tenantId, FullName = "Other add-on tenant", Slug = $"add-on-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        });
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(), Title = "Foreign add-on event", TenantId = tenantId, Tenant = null!,
            ActorId = fixture.ActorId, Actor = null!, OrganizerActorId = fixture.ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
            CreatedAt = clock.Now.UtcDateTime
        };
        parent.ParticipationConfiguration = EventParticipationConfiguration.Create(parent.Id, tenantId,
            (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
            (int)IdentityAccessModeEnum.AccountRequired, null, clock.Now.UtcDateTime);
        fixture.Context.Events.Add(parent);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return parent;
    }

    private static async Task AssertPublishedAddOnResponseAsync(EventAddOnPublicationResponse response,
        Guid eventId, EventAddOnCatalogVersion draft)
    {
        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.Catalog).IsNotNull();
        var catalog = response.Catalog!;
        await Assert.That(catalog.Id).IsEqualTo((Guid?)draft.Id);
        await Assert.That(catalog.EventId).IsEqualTo(eventId);
        await Assert.That(catalog.VersionNumber).IsEqualTo((int?)draft.VersionNumber);
        await Assert.That(catalog.CurrencyCode).IsEqualTo("USD");
        await Assert.That(catalog.CanManage).IsTrue();
        await Assert.That(catalog.CanRetire).IsTrue();
        await Assert.That(catalog.CanPublish).IsFalse();
        var item = catalog.Items.Single();
        await Assert.That(item.Id).IsEqualTo(draft.Items.Single().Id);
        await Assert.That(item.UnitPriceMinor).IsEqualTo(250L);
        await Assert.That(item.IsAvailable).IsTrue();
        await Assert.That(item.MaximumSelectableQuantity).IsEqualTo(10);
    }

    private static async Task AssertAddOnPublicationUnchangedAsync(EventVisitorCapabilitySqliteFixture fixture,
        EventAddOnCatalogVersion draft, EventAddOnCatalogVersion? prior = null)
    {
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        // Explicit foreign-tenant snapshots prove denial did not mutate the requested tenant's catalog.
        var persisted = await context.EventAddOnCatalogVersions.IgnoreQueryFilters(["Tenant"]).AsNoTracking()
            .Include(row => row.Items).SingleAsync(row => row.TenantId == draft.TenantId && row.Id == draft.Id);
        await Assert.That(persisted.PublishedAt).IsNull();
        await Assert.That(persisted.RetiredAt).IsNull();
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(draft.ConcurrencyStamp);
        await Assert.That(persisted.Items.Count).IsEqualTo(draft.Items.Count);
        if (prior is not null)
        {
            var persistedPrior = await context.EventAddOnCatalogVersions.AsNoTracking().SingleAsync(row => row.Id == prior.Id);
            await Assert.That(persistedPrior.PublishedAt).IsEqualTo(prior.PublishedAt);
            await Assert.That(persistedPrior.RetiredAt).IsNull();
            await Assert.That(persistedPrior.ConcurrencyStamp).IsEqualTo(prior.ConcurrencyStamp);
        }
        await Assert.That(await context.OutboxMessages.AnyAsync(row => row.AggregateId == draft.EventId)).IsFalse();
        await Assert.That(await context.PdsSyncOutbox.AnyAsync(row => row.SourceEntityId == draft.EventId)).IsFalse();
    }

    private sealed class AfterAddOnPublicationFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        public Guid DraftId { get; set; }
        public Guid PriorId { get; set; }
        public Guid DraftStamp { get; set; }
        public Guid PriorStamp { get; set; }
        public DateTimeOffset Crossing { get; set; }
        public bool Armed { get; set; }
        public bool ObservedPublishedDraftAndRetiredPrior { get; private set; }
        public bool ObservedBothChangedStamps { get; private set; }
        public bool? ObservedPrivacyOwner { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            var draft = await context.EventAddOnCatalogVersions.AsNoTracking()
                .SingleAsync(row => row.Id == DraftId, cancellationToken);
            if (draft.PublishedAt is null) return result;
            var prior = await context.EventAddOnCatalogVersions.AsNoTracking()
                .SingleAsync(row => row.Id == PriorId, cancellationToken);
            Armed = false;
            ObservedPublishedDraftAndRetiredPrior = draft.RetiredAt is null && prior.RetiredAt is not null;
            ObservedBothChangedStamps = draft.ConcurrencyStamp != DraftStamp && prior.ConcurrencyStamp != PriorStamp;
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }
}
