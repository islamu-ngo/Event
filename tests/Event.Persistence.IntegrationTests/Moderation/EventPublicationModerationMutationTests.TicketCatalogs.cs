using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.EventTicketing.Handlers.Commands;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Features.EventTicketing.Services;
using Explore.Application.Responses;
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
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CurrentHumanPublicationRestrictionBlocksActualDraftTicketCatalogPublication(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedDraftTicketCatalogAsync(fixture, parent.Id);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_ticketing_publish_restricted");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        await AssertTicketCatalogUnchangedAsync(fixture, draft);
    }

    [Test]
    public async Task CurrentHumanRestrictionPrecedesMissingDraftCatalogLookup()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_ticketing_publish_restricted");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await context.EventTicketCatalogVersions.AnyAsync(row => row.EventId == parent.Id)).IsFalse();
        await Assert.That(await context.AdmissionTargets.AnyAsync(row => row.EventId == parent.Id)).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AbsentOrUnrestrictedHumanAdoptionPreservesActualDraftCatalogPublication(
        bool hostInstalled, bool adopted)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedDraftTicketCatalogAsync(fixture, parent.Id);
        if (adopted) await AdoptAsync(fixture, clock);
        else if (hostInstalled) await SeedQualifiedTicketPublicationHostAsync(fixture);

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await AssertTicketCatalogPublishedAsync(fixture, draft);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdoptedUnavailableHumanAuthorityPrecedesEvenMissingDraftCatalog(bool hasDraft)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = hasDraft ? await SeedDraftTicketCatalogAsync(fixture, parent.Id) : null;
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_ticketing_publish_authority_unavailable");
        await Assert.That(response.Id).IsEqualTo(parent.Id);
        if (draft is not null) await AssertTicketCatalogUnchangedAsync(fixture, draft);
        else
        {
            await using var freshScope = fixture.CreateScope();
            var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await context.EventTicketCatalogVersions.AnyAsync(row => row.EventId == parent.Id)).IsFalse();
            await Assert.That(await context.AdmissionTargets.AnyAsync(row => row.EventId == parent.Id)).IsFalse();
        }
    }

    [Test]
    public async Task AnotherHumansPublicationRestrictionDoesNotControlCurrentCatalogPublisher()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedDraftTicketCatalogAsync(fixture, parent.Id);
        var adoption = await AdoptAsync(fixture, clock);
        var otherHuman = NativeUser(Guid.CreateVersion7());
        fixture.Context.Users.Add(otherHuman);
        await fixture.Context.SaveChangesAsync();
        await StoreRestrictionAsync(fixture, adoption, otherHuman.Id, clock.Now);

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await AssertTicketCatalogPublishedAsync(fixture, draft);
    }

    [Test]
    public async Task UnrestrictedHumanAdoptionDoesNotBypassPaidCatalogPreflight()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var draft = await SeedDraftTicketCatalogAsync(fixture, parent.Id, paid: true);
        await AdoptAsync(fixture, clock);
        var preflight = await fixture.Services.GetRequiredService<PaidEventPublicationPreflightService>()
            .AssessAsync(parent.Id, CancellationToken.None);
        await Assert.That(preflight.IsPaidCatalog).IsTrue();
        await Assert.That(preflight.IsReady).IsFalse();
        await Assert.That(preflight.Blockers.Single().Code).IsEqualTo("tenant_directory_operator_identity_unavailable");

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_ticketing_validation_failed");
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.Errors!.Single())
            .IsEqualTo("Tenant directory operator identity is unavailable for paid commerce.");
        await AssertTicketCatalogUnchangedAsync(fixture, draft);
    }

    [Test]
    public async Task UnrestrictedHumanAdoptionDoesNotPublishEntitlementToDeletedSession()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var session = await fixture.Context.EventSessions.SingleAsync(row => row.EventId == parent.Id);
        var draft = await SeedDraftTicketCatalogAsync(fixture, parent.Id, session: session);
        await AdoptAsync(fixture, clock);
        var target = await fixture.Context.EventSessions.SingleAsync(row => row.Id == session.Id);
        target.IsDeleted = true;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_ticketing_validation_failed");
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.Errors!.Single())
            .IsEqualTo("Ticket entitlement targets must be active and belong to the catalog event and tenant.");
        await AssertTicketCatalogUnchangedAsync(fixture, draft);
    }

    [Test]
    public async Task UnrestrictedHumanAdoptionDoesNotPublishCatalogWithoutAdmissionSchedule()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await fixture.SeedEventAsync();
        var draft = await SeedDraftTicketCatalogAsync(fixture, parent.Id);
        await AdoptAsync(fixture, clock);

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_ticketing_validation_failed");
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.Errors!.Single())
            .IsEqualTo("Admission targets require complete UTC schedule bounds for every session in their entitlement scope.");
        await AssertTicketCatalogUnchangedAsync(fixture, draft);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FutureHumanRestrictionRollsBackBothCatalogFlushesAndAdmissionMaterialization(bool colocated)
    {
        var clock = new PublicationClock();
        var checkpoint = new AfterTicketCatalogPublicationFlush(clock);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            ConfigureServices(services, () => privacy!, clock, [checkpoint]));
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var parent = await ReadyDraftAsync(fixture);
        var prior = await SeedDraftTicketCatalogAsync(fixture, parent.Id);
        var trackedPrior = await fixture.Context.EventTicketCatalogVersions.Include(row => row.TicketTypes)
            .ThenInclude(row => row.Entitlements).SingleAsync(row => row.Id == prior.Id);
        trackedPrior.Publish();
        await fixture.Context.SaveChangesAsync();
        var priorStamp = trackedPrior.ConcurrencyStamp;
        fixture.Context.ChangeTracker.Clear();
        var draft = await SeedDraftTicketCatalogAsync(fixture, parent.Id, version: 2);
        var adoption = await AdoptAsync(fixture, clock);
        var startsAt = clock.Now.AddMinutes(1);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, startsAt);
        checkpoint.EventId = parent.Id;
        checkpoint.DraftId = draft.Id;
        checkpoint.PriorId = prior.Id;
        checkpoint.Crossing = startsAt;
        checkpoint.Armed = true;

        var response = await PublishTicketCatalogAsync(fixture, parent.Id);

        await Assert.That(checkpoint.ObservedRetirementAndAdmissionBeforePublish).IsTrue();
        await Assert.That(checkpoint.ObservedPublishedDraftAndRetiredPrior).IsTrue();
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_ticketing_concurrency_conflict");
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await AssertTicketCatalogUnchangedAsync(fixture, draft);
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persistedPrior = await context.EventTicketCatalogVersions.AsNoTracking().SingleAsync(row => row.Id == prior.Id);
        await Assert.That(persistedPrior.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Published);
        await Assert.That(persistedPrior.ConcurrencyStamp).IsEqualTo(priorStamp);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private static async Task<EventTicketCatalogVersion> SeedDraftTicketCatalogAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid eventId, int version = 1,
        bool paid = false, EventSession? session = null)
    {
        var catalog = EventTicketCatalogVersion.Create(fixture.TenantId, eventId, "USD", version);
        var pool = EventCapacityPool.Create(fixture.TenantId, eventId, $"Publication allocation {version}", 10, 900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
        var ticket = EventTicketType.Create(Guid.CreateVersion7(), fixture.TenantId, catalog.Id,
            "Native admission", "USD", paid ? TicketPricingModeEnum.Fixed : TicketPricingModeEnum.Free,
            paid ? Money.Create(1000, "USD") : null, null, null,
            ParticipantDataCollectionModeEnum.None, pool.Id, null, null, false, false,
            null, null, null, null);
        catalog.AddTicketType(ticket, pool);
        catalog.AddEntitlement(ticket, session is null
            ? TicketTypeEntitlement.CreateForEvent(ticket.Id, fixture.TenantId, eventId, 1)
            : TicketTypeEntitlement.CreateForEventSession(ticket.Id, session, 1, EntitlementSelectionRuleEnum.FixedSelection));
        if (paid) catalog.UpdateCommercialDisclosures("Native merchant", "Refunds by support", "support@example.invalid");
        catalog.ValidateForPublication();
        fixture.Context.AddRange(catalog, pool);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return catalog;
    }

    private static async Task SeedQualifiedTicketPublicationHostAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var maintenanceId = Guid.CreateVersion7();
        var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), maintenanceId);
        fixture.Context.ServicePrincipals.Add(new ServicePrincipal
        {
            Id = maintenanceId,
            Code = $"ticket-publication-{maintenanceId:N}",
            DisplayName = "Ticket publication maintenance"
        });
        fixture.Context.ModerationHostBindings.Add(host);
        var tenant = await fixture.Context.Tenants.SingleAsync(row => row.Id == fixture.TenantId);
        fixture.Context.ModerationQualificationEpochs.Add(ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static Task<BaseCommandResponse<Guid>> PublishTicketCatalogAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid eventId) =>
        ActivatorUtilities.CreateInstance<PublishEventTicketCatalogCommandHandler>(fixture.Services)
            .ExecuteAsync(new PublishEventTicketCatalogCommand { EventId = eventId }, CancellationToken.None);

    private static async Task AssertTicketCatalogUnchangedAsync(
        EventVisitorCapabilitySqliteFixture fixture, EventTicketCatalogVersion draft)
    {
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.EventTicketCatalogVersions.AsNoTracking().SingleAsync(row => row.Id == draft.Id);
        await Assert.That(persisted.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Draft);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(draft.ConcurrencyStamp);
        await Assert.That((await context.Events.SingleAsync(row => row.Id == draft.EventId)).EventStatusId)
            .IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(await context.AdmissionTargets.AnyAsync(row => row.EventId == draft.EventId)).IsFalse();
        await Assert.That(await context.AdmissionCheckInPolicies.AnyAsync()).IsFalse();
        await Assert.That(await context.OutboxMessages.AnyAsync(row => row.AggregateId == draft.EventId)).IsFalse();
        await Assert.That(await context.PdsSyncOutbox.AnyAsync(row => row.SourceEntityId == draft.EventId)).IsFalse();
    }

    private static async Task AssertTicketCatalogPublishedAsync(
        EventVisitorCapabilitySqliteFixture fixture, EventTicketCatalogVersion draft)
    {
        await using var freshScope = fixture.CreateScope();
        var context = freshScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persisted = await context.EventTicketCatalogVersions.AsNoTracking().Include(row => row.TicketTypes)
            .ThenInclude(row => row.Entitlements).SingleAsync(row => row.Id == draft.Id);
        await Assert.That(persisted.TicketCatalogStatusId).IsEqualTo((int)TicketCatalogStatusEnum.Published);
        await Assert.That(persisted.TicketTypes.Count).IsEqualTo(1);
        await Assert.That(persisted.TicketTypes.Single().Entitlements.Single().TargetEventId).IsEqualTo(draft.EventId);
        var target = await context.AdmissionTargets.SingleAsync(row => row.EventId == draft.EventId);
        await Assert.That(await context.AdmissionCheckInPolicies.CountAsync(row => row.AdmissionTargetId == target.Id)).IsEqualTo(1);
        await Assert.That((await context.Events.SingleAsync(row => row.Id == draft.EventId)).EventStatusId)
            .IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(await context.OutboxMessages.AnyAsync(row => row.AggregateId == draft.EventId)).IsFalse();
        await Assert.That(await context.PdsSyncOutbox.AnyAsync(row => row.SourceEntityId == draft.EventId)).IsFalse();
    }

    private sealed class AfterTicketCatalogPublicationFlush(PublicationClock clock) : SaveChangesInterceptor
    {
        public Guid EventId { get; set; }
        public Guid DraftId { get; set; }
        public Guid PriorId { get; set; }
        public DateTimeOffset Crossing { get; set; }
        public bool Armed { get; set; }
        public bool ObservedRetirementAndAdmissionBeforePublish { get; private set; }
        public bool ObservedPublishedDraftAndRetiredPrior { get; private set; }
        public bool? ObservedPrivacyOwner { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            var draft = await context.EventTicketCatalogVersions.AsNoTracking()
                .SingleAsync(row => row.Id == DraftId, cancellationToken);
            var prior = await context.EventTicketCatalogVersions.AsNoTracking()
                .SingleAsync(row => row.Id == PriorId, cancellationToken);
            var admissionPersisted = await context.AdmissionTargets.AnyAsync(row => row.EventId == EventId, cancellationToken)
                && await context.AdmissionCheckInPolicies.AnyAsync(row => context.AdmissionTargets.Any(
                    target => target.EventId == EventId && target.Id == row.AdmissionTargetId), cancellationToken);
            if (draft.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Draft)
            {
                ObservedRetirementAndAdmissionBeforePublish = prior.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Retired
                    && admissionPersisted;
                return result;
            }

            Armed = false;
            ObservedPublishedDraftAndRetiredPrior = draft.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published
                && prior.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Retired && admissionPersisted;
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            clock.Now = Crossing;
            return result;
        }
    }
}
