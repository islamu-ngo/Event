using System.Collections.Immutable;
using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.Settings;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventPublicationModerationMutationTests
{
    [Test]
    public async Task External_form_import_does_not_flush_or_discard_pending_caller_changes()
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, new PublicationClock());
        var parent = await ReadyDraftAsync(fixture);
        var originalTitle = parent.Title;
        parent.Title = "Unsaved external import caller change";
        var handler = ActivatorUtilities.CreateInstance<Explore.Application.Features.RegistrationProviders.Commands.ImportExternalRegistrationProviderFormVersionCommandHandler>(fixture.Services);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(
            new Explore.Application.Features.RegistrationProviders.Commands.ImportExternalRegistrationProviderFormVersionCommand(
                fixture.TenantId, parent.Id, Guid.CreateVersion7(),
                new Explore.Application.DTOs.RegistrationProviders.ImportExternalRegistrationProviderFormVersionRequestDto
                {
                    Key = "external-import",
                    Name = "External import",
                    ProviderSurveyId = "survey"
                }), CancellationToken.None));

        await Assert.That(fixture.Context.Entry(parent).State).IsEqualTo(EntityState.Modified);
        await Assert.That(parent.Title).IsEqualTo("Unsaved external import caller change");
        await Assert.That(await fixture.Context.Events.AsNoTracking().Where(row => row.Id == parent.Id)
            .Select(row => row.Title).SingleAsync()).IsEqualTo(originalTitle);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Form_publication_does_not_flush_or_discard_pending_caller_changes()
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, new PublicationClock());
        var parent = await ReadyDraftAsync(fixture);
        var originalTitle = parent.Title;
        parent.Title = "Unsaved form authoring caller change";
        var service = fixture.Services.GetRequiredService<Explore.Application.Services.Registration.RegistrationFormAuthoringCommandService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(
            new Explore.Application.Features.RegistrationForms.Requests.Commands.PublishRegistrationFormVersionCommand(
                parent.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None));

        await Assert.That(fixture.Context.Entry(parent).State).IsEqualTo(EntityState.Modified);
        await Assert.That(parent.Title).IsEqualTo("Unsaved form authoring caller change");
        await Assert.That(await fixture.Context.Events.AsNoTracking().Where(row => row.Id == parent.Id)
            .Select(row => row.Title).SingleAsync()).IsEqualTo(originalTitle);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Add_on_publication_does_not_flush_or_discard_pending_caller_changes()
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, new PublicationClock());
        var parent = await ReadyDraftAsync(fixture);
        var originalTitle = parent.Title;
        parent.Title = "Unsaved add-on caller change";
        var handler = ActivatorUtilities.CreateInstance<Explore.Application.Features.EventAddOns.Handlers.Commands.PublishEventAddOnCatalogCommandHandler>(fixture.Services);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(
            new Explore.Application.Features.EventAddOns.Requests.Commands.PublishEventAddOnCatalogCommand(
                parent.Id, DateTime.UtcNow), CancellationToken.None));

        await Assert.That(fixture.Context.Entry(parent).State).IsEqualTo(EntityState.Modified);
        await Assert.That(parent.Title).IsEqualTo("Unsaved add-on caller change");
        await Assert.That(await fixture.Context.Events.AsNoTracking().Where(row => row.Id == parent.Id)
            .Select(row => row.Title).SingleAsync()).IsEqualTo(originalTitle);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Promotion_publication_does_not_translate_or_discard_pending_caller_changes()
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, new PublicationClock());
        var parent = await ReadyDraftAsync(fixture);
        var originalTitle = parent.Title;
        parent.Title = "Unsaved caller change";
        var handler = ActivatorUtilities.CreateInstance<Explore.Application.Features.Promotions.Handlers.Commands.PublishPromotionCommandHandler>(fixture.Services);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(
            new Explore.Application.Features.Promotions.Requests.Commands.PublishPromotionCommand(
                parent.Id, Guid.CreateVersion7(), "SAVE10"), CancellationToken.None));

        await Assert.That(fixture.Context.Entry(parent).State).IsEqualTo(EntityState.Modified);
        await Assert.That(parent.Title).IsEqualTo("Unsaved caller change");
        await Assert.That(await fixture.Context.Events.AsNoTracking().Where(row => row.Id == parent.Id)
            .Select(row => row.Title).SingleAsync()).IsEqualTo(originalTitle);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Already_published_session_cannot_bypass_current_caller_publication_restriction()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        parent.Publish(clock.Now.UtcDateTime);
        await fixture.Context.SaveChangesAsync();
        var session = await fixture.Context.EventSessions.AsNoTracking().SingleAsync(row => row.EventId == parent.Id);
        var originalStamp = session.ConcurrencyStamp;
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        var handler = ActivatorUtilities.CreateInstance<Explore.Application.Features.EventSessions.Handlers.Commands.PublishEventSessionCommandHandler>(fixture.Services);

        var response = await handler.ExecuteAsync(new Explore.Application.Features.EventSessions.Requests.Commands.PublishEventSessionCommand
        {
            Id = session.Id,
            Request = new Explore.Application.DTOs.EventSession.PublishEventSessionRequestDto
            { ExpectedConcurrencyStamp = originalStamp }
        }, CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(session.Id);
        await Assert.That(response.FailureCode).IsEqualTo("event_session_publish_restricted");
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
        await Assert.That(persisted.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Published);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(originalStamp);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(EventPublicationMode.Ordinary)]
    [Arguments(EventPublicationMode.PrivilegedApproval)]
    public async Task AbsentHumanPublishAdoptionPreservesReadyEventPublication(EventPublicationMode mode)
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, new PublicationClock());
        var draft = await ReadyDraftAsync(fixture);

        var response = await PublishAsync(fixture, draft, mode);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.Events.SingleAsync(value => value.Id == draft.Id);
        await Assert.That(persisted.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(await fixture.Context.OutboxMessages.CountAsync(value => value.AggregateId == draft.Id))
            .IsGreaterThan(0);
    }

    [Test]
    [Arguments(EventPublicationMode.Ordinary, false, false)]
    [Arguments(EventPublicationMode.Ordinary, true, false)]
    [Arguments(EventPublicationMode.Ordinary, false, true)]
    [Arguments(EventPublicationMode.Ordinary, true, true)]
    [Arguments(EventPublicationMode.PrivilegedApproval, false, false)]
    [Arguments(EventPublicationMode.PrivilegedApproval, true, false)]
    [Arguments(EventPublicationMode.PrivilegedApproval, false, true)]
    [Arguments(EventPublicationMode.PrivilegedApproval, true, true)]
    public async Task CurrentCallerRestrictionAtContainingAuthorityBlocksBothPublicationModes(
        EventPublicationMode mode, bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);
        if (mode == EventPublicationMode.PrivilegedApproval) await RequireApprovalAsync(fixture);
        var entered = false;
        var blocked = await fixture.Services.GetRequiredService<IEventPublicationMutationBoundary>()
            .ExecuteAsync(draft.Id, _ =>
            {
                entered = true;
                return Task.FromResult(BaseCommandResponse.Success(draft.Id));
            }, CancellationToken.None);

        var response = await PublishAsync(fixture, draft, mode);

        await Assert.That(entered).IsFalse();
        await Assert.That(blocked.FailureCode).IsEqualTo("event_publish_restricted");
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_publish_restricted");
        await AssertUnpublishedAsync(fixture, draft);
    }

    [Test]
    [Arguments(EventPublicationMode.Ordinary)]
    [Arguments(EventPublicationMode.PrivilegedApproval)]
    public async Task RetiringProfileSupportDoesNotEraseCanonicalCallerSanction(EventPublicationMode mode)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        var declared = ModerationPolicyPublicationProjection.RestoreHistory(adoption.Publication)!.Proposed;
        var retired = await PublishPolicyAsync(fixture, clock, adoption.Host, declared with
        {
            PublicationId = Guid.CreateVersion7(),
            ExpectedRevision = adoption.Publication.Revision,
            SupportedProfiles = []
        }, adoption.Publication.Id);
        await Assert.That(retired.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);

        var response = await PublishAsync(fixture, draft, mode);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_publish_restricted");
        await AssertUnpublishedAsync(fixture, draft);
    }

    [Test]
    [Arguments(EventPublicationMode.Ordinary)]
    [Arguments(EventPublicationMode.PrivilegedApproval)]
    public async Task AdoptedButUnavailableAuthorityBlocksBeforeNativeCallback(EventPublicationMode mode)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await ReadyDraftAsync(fixture);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
        var entered = false;
        var boundary = fixture.Services.GetRequiredService<IEventPublicationMutationBoundary>();

        var blocked = await boundary.ExecuteAsync(draft.Id, _ =>
        {
            entered = true;
            return Task.FromResult(BaseCommandResponse.Success(draft.Id));
        }, CancellationToken.None);
        var response = await PublishAsync(fixture, draft, mode);

        await Assert.That(entered).IsFalse();
        await Assert.That(blocked.IsSuccess).IsFalse();
        await Assert.That(blocked.FailureCode).IsEqualTo("event_publish_authority_unavailable");
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_publish_authority_unavailable");
        await AssertUnpublishedAsync(fixture, draft);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnrestrictedAdoptionDoesNotBypassOrdinaryApprovalOrEitherModesReadiness(bool adopt)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var ready = await ReadyDraftAsync(fixture);
        if (adopt) await AdoptAsync(fixture, clock);
        var unready = await fixture.SeedEventAsync();
        unready.RecalculateScheduleSummaryFromSessions();
        fixture.Context.Events.Update(unready);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        foreach (var mode in new[] { EventPublicationMode.Ordinary, EventPublicationMode.PrivilegedApproval })
        {
            var rejected = await PublishAsync(fixture, unready, mode);
            await Assert.That(rejected.IsSuccess).IsFalse();
            await Assert.That(rejected.FailureCode).IsEqualTo(EventPublicationExecutor.ReadinessFailedCode);
            await AssertUnpublishedAsync(fixture, unready);
        }
        await RequireApprovalAsync(fixture);

        var ordinary = await PublishAsync(fixture, ready, EventPublicationMode.Ordinary);
        await Assert.That(ordinary.IsSuccess).IsFalse();
        await Assert.That(ordinary.FailureCode).IsEqualTo(EventPublicationExecutor.ApprovalRequiredCode);
        await AssertUnpublishedAsync(fixture, ready);

        var privileged = await PublishAsync(fixture, ready, EventPublicationMode.PrivilegedApproval);
        await Assert.That(privileged.IsSuccess).IsTrue().Because(privileged.Message);

        var approvalReadiness = await PublishAsync(fixture, unready, EventPublicationMode.PrivilegedApproval);
        await Assert.That(approvalReadiness.IsSuccess).IsFalse();
        await Assert.That(approvalReadiness.FailureCode).IsEqualTo(EventPublicationExecutor.ReadinessFailedCode);
        await AssertUnpublishedAsync(fixture, unready);
    }

    [Test]
    [Arguments(EventPublicationMode.Ordinary)]
    [Arguments(EventPublicationMode.PrivilegedApproval)]
    public async Task SanctionOnPublisherDoesNotSubstituteForTheUnrestrictedCurrentCaller(EventPublicationMode mode)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock);
        var publisher = NativeUser(Guid.CreateVersion7());
        var publisherActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = publisher.Id,
            User = publisher,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Different event publisher" }
        };
        fixture.Context.Actors.Add(publisherActor);
        var tracked = await fixture.Context.Events.SingleAsync(row => row.Id == draft.Id);
        tracked.ActorId = publisherActor.Id;
        tracked.OrganizerActorId = publisherActor.Id;
        tracked.SubmittedByUserId = publisher.Id;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        draft = await fixture.Context.Events.SingleAsync(row => row.Id == draft.Id);
        await StoreRestrictionAsync(fixture, adoption, publisher.Id, clock.Now);

        var response = await PublishAsync(fixture, draft, mode);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        fixture.Context.ChangeTracker.Clear();
        await Assert.That((await fixture.Context.Events.SingleAsync(row => row.Id == draft.Id)).EventStatusId)
            .IsEqualTo((int)EventStatusEnum.Published);
    }

    [Test]
    [Arguments(EventPublicationMode.Ordinary, false)]
    [Arguments(EventPublicationMode.Ordinary, true)]
    [Arguments(EventPublicationMode.PrivilegedApproval, false)]
    [Arguments(EventPublicationMode.PrivilegedApproval, true)]
    public async Task CommitRecheckRollsBackExecutorWritesWhenAuthorityBecomesUnavailableOrSanctionStarts(
        EventPublicationMode mode, bool unavailable)
    {
        var clock = new PublicationClock();
        var checkpoint = new AfterPublicationFlush(clock, unavailable);
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock, checkpoint);
        var draft = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock);
        var startsAt = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, startsAt);
        checkpoint.EventId = draft.Id;
        checkpoint.Crossing = startsAt;
        checkpoint.Armed = true;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await PublishAsync(fixture, draft, mode));

        await Assert.That(checkpoint.ObservedPublishedEventAndNotification).IsTrue();
        await AssertUnpublishedAsync(fixture, draft);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OwningCommitRecheckRollsBackEventAndBothOutboxesIncludingColocatedPrivacyOwner(
        bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            ConfigureServices(services, () => privacy!, clock, []));
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var draft = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock);
        var startsAt = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, startsAt);
        var boundary = fixture.Services.GetRequiredService<IEventPublicationMutationBoundary>();
        bool? observedPrivacyOwner = null;
        var observedBothOutboxes = false;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await boundary.ExecuteAsync(draft.Id, async token =>
            {
                observedPrivacyOwner = fixture.Context.IdentityFenceOwnsTransaction;
                var tracked = await fixture.Context.Events.SingleAsync(row => row.Id == draft.Id, token);
                tracked.Publish(clock.Now.UtcDateTime);
                fixture.Context.OutboxMessages.Add(EventPublishedOutboxMessageFactory.CreateNotificationFanoutOutboxMessage(
                    Guid.CreateVersion7(), tracked, clock.Now));
                fixture.Context.PdsSyncOutbox.Add(new PdsSyncOutbox
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = fixture.TenantId,
                    UserId = fixture.UserId,
                    Did = "did:plc:publication-boundary",
                    Collection = "community.lexicon.calendar.event",
                    RecordKey = Guid.CreateVersion7().ToString("N"),
                    Operation = PdsSyncOperation.Create,
                    Payload = "{\"name\":\"Human publication boundary\"}",
                    PayloadHash = new string('a', 64),
                    IdempotencyKey = $"event:{draft.Id:N}:create",
                    PdsHost = "https://pds.example.test",
                    SourceEntityType = "Event",
                    SourceEntityId = draft.Id,
                    SourceVersion = tracked.ConcurrencyStamp,
                    Status = PdsSyncStatus.Pending,
                    CreatedAt = clock.Now.UtcDateTime,
                    MaxRetries = 3
                });
                await fixture.Context.SaveChangesAsync(token);
                observedBothOutboxes = await fixture.Context.OutboxMessages.AnyAsync(row => row.AggregateId == draft.Id, token)
                    && await fixture.Context.PdsSyncOutbox.AnyAsync(row => row.SourceEntityId == draft.Id, token);
                if (unavailable)
                    await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(token);
                else
                    clock.Now = startsAt;
                return BaseCommandResponse.Success(draft.Id);
            }, CancellationToken.None));

        await Assert.That(observedBothOutboxes).IsTrue();
        await Assert.That(observedPrivacyOwner).IsEqualTo((bool?)colocated);
        await AssertUnpublishedAsync(fixture, draft);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private static Task<EventVisitorCapabilitySqliteFixture> CreateFixtureAsync(
        ModerationNoticePrivacyFixture privacy, PublicationClock clock, params IInterceptor[] interceptors) =>
        EventVisitorCapabilitySqliteFixture.CreateAsync(services => ConfigureServices(services, () => privacy, clock, interceptors));

    private static void ConfigureServices(IServiceCollection services, Func<ModerationNoticePrivacyFixture> privacy,
        PublicationClock clock, IInterceptor[] interceptors)
    {
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<IPrivacyIdentityFenceAuthority>(provider =>
            privacy().Authority(provider.GetRequiredService<ExploreDbContext>(), clock));
        services.AddScoped<IModerationRecognitionContinuityReader>(provider =>
            ModerationRecognitionResourceFixture.Reader(provider.GetRequiredService<ExploreDbContext>()));
        services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(interceptors));
    }

    private static async Task AssertUnpublishedAsync(EventVisitorCapabilitySqliteFixture fixture,
        Explore.Domain.Event draft)
    {
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.Events.SingleAsync(value => value.Id == draft.Id);
        await Assert.That(persisted.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(draft.ConcurrencyStamp);
        await Assert.That(await fixture.Context.OutboxMessages.AnyAsync(value => value.AggregateId == draft.Id)).IsFalse();
        await Assert.That(await fixture.Context.PdsSyncOutbox.AnyAsync(value => value.SourceEntityId == draft.Id)).IsFalse();
    }

    private static async Task RequireApprovalAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var writer = fixture.Services.GetRequiredService<IPublicationPolicyMutationBoundary>();
        var result = await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            token => writer.ApplyTenantAsync(new PublicationPolicyTenantMutationRequest(fixture.TenantId,
                fixture.ActorId, DateTime.UtcNow,
                [new PublicationPolicySettingMutation(GovernanceSettingKeys.Events.RequireApproval,
                    PublicationPolicyMutationKind.Set, SettingValueSerializer.Serialize(true), fixture.TenantId, null)],
                PublicationPolicyLockedSystemBehavior.Reject), token), CancellationToken.None);
        await Assert.That(result.Success).IsTrue().Because(result.Message);
        fixture.Services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache(SettingScope.Tenant, fixture.TenantId);
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<Adoption> AdoptAsync(EventVisitorCapabilitySqliteFixture fixture,
        PublicationClock clock, bool instanceScope = false)
    {
        var context = fixture.Context;
        context.ChangeTracker.Clear();
        var maintenanceId = Guid.CreateVersion7();
        var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), maintenanceId);
        var role = await context.Roles.SingleAsync(row => row.MasterCode == "platform.admin"
            && row.RoleScopeId == (int)RoleScopeEnum.Platform);
        context.ServicePrincipals.Add(new ServicePrincipal
        {
            Id = maintenanceId,
            Code = $"publish-maintenance-{maintenanceId:N}",
            DisplayName = "Publication maintenance"
        });
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = role.Id,
            Role = role
        });
        context.ModerationHostBindings.Add(host);
        var tenant = await context.Tenants.SingleAsync(row => row.Id == fixture.TenantId);
        context.ModerationQualificationEpochs.Add(ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId),
            host.SolutionId), host.InstanceId);
        var scope = instanceScope ? instance : AuthorityScope.Tenant(instance, fixture.TenantId);
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
        var response = reference.Responses[0];
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(scope.Id, code, 1);
        var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("human-publish"),
            Rule("human-publish-reason"), reference.Id, reference.Revision, response.Grade,
            [ModerationScopeKind.Tenant], [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(),
            reference.Bundle, reference.ProtectionPurpose, [], new(response.Timing.Kind, response.Timing.Duration),
            response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
        var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), scope,
            ModerationReferenceCatalogue.Revision, inventory.Revision, inventory.Fingerprint,
            ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            [new(Rule("human-publish-class"), "Human publication restriction.", reference.Ground, reference.Basis)],
            [new(profile.Reason, "Native Human publication restriction reason.", Rule("human-publish-class"))],
            [profile], [profile.Rule]);
        var published = await PublishPolicyAsync(fixture, clock, host, proposed);
        await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        return new(host, instance, scope, published.Publication!);
    }

    private static async Task<ModerationPolicyPublicationResult> PublishPolicyAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock, ModerationHostBinding host,
        ModerationPolicyProposedState proposed, Guid? previous = null)
    {
        var epoch = await fixture.Context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var writer = new ModerationPolicyPublicationRepository(fixture.Context,
            fixture.Services.GetRequiredService<IAdminContext>(), fixture.Services.GetRequiredService<IMachinePrincipalAccessor>(),
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory()), null, clock);
        return await writer.PublishAsync(new(Guid.CreateVersion7(), host.Id, epoch.Id, epoch.Revision,
            ModerationPolicyPublicationProjection.WriterGeneration, previous, proposed));
    }

    private static async Task StoreRestrictionAsync(EventVisitorCapabilitySqliteFixture fixture, Adoption adoption,
        Guid subjectId, DateTimeOffset startsAt, bool protective = false)
    {
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, subjectId, adoption.Instance);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), adoption.Instance);
        var controller = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), adoption.Instance);
        var now = startsAt.AddMinutes(-1);
        var scope = adoption.Scope;
        var profile = adoption.Publication.Profiles.Single();
        var rule = ModerationRuleReference.Create(scope.Id, profile.RuleCode, profile.RuleRevision);
        var reason = ModerationRuleReference.Create(scope.Id, profile.ReasonCode, profile.ReasonRevision);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        fixture.Context.ModerationTrustDomainId = adoption.Host.TrustDomainId;
        fixture.Context.Users.AddRange(NativeUser(issuer.Id), NativeUser(controller.Id));
        if (protective)
        {
            var measure = ModerationProtectiveMeasure.Create(Guid.CreateVersion7(), subject, issuer, scope, scope,
                [ModerationCapability.Publish], "Bounded native publication protection", startsAt,
                startsAt.AddHours(1), startsAt.AddMinutes(30));
            measure.ValidatePersistedState();
            fixture.Context.Add(measure);
        }
        else
        {
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), now);
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1, scope, null, null,
                rule, rule, reason, reason, controller,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, now),
                "Canonical retained Human publication finding.", now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                scope, scope, rule, [ModerationCapability.Publish], startsAt, startsAt.AddHours(1), null, 1, now);
            decision.RecordResponse(decision.Revision, now);
            decision.AcceptFinding(finding.Id, decision.Revision, now);
            decision.Approve(controller, scope, decision.Revision, now);
            var sanction = decision.Activate(Guid.CreateVersion7(), decision.Revision, now);
            decision.ValidatePersistedState();
            sanction.ValidatePersistedState();
            fixture.Context.AddRange(allegation, finding, decision, sanction);
        }
        await fixture.Context.SaveChangesAsync();
        await transaction.CommitAsync();
        fixture.Context.ModerationTrustDomainId = null;
        fixture.Context.ChangeTracker.Clear();
    }

    private static User NativeUser(Guid id) => new()
    {
        Id = id,
        Pii = new UserPii { Email = $"{id:N}@example.invalid", FirstName = "Publication", LastName = "Publisher" }
    };

    private sealed record Adoption(ModerationHostBinding Host, AuthorityScope Instance,
        AuthorityScope Scope, ModerationPolicyPublication Publication);

    private sealed class PublicationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AfterPublicationFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        public Guid EventId { get; set; }
        public DateTimeOffset Crossing { get; set; }
        public bool Armed { get; set; }
        public bool ObservedPublishedEventAndNotification { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            if (!await context.OutboxMessages.AsNoTracking().AnyAsync(row => row.AggregateId == EventId, cancellationToken))
                return result;
            Armed = false;
            ObservedPublishedEventAndNotification = await context.Events.AsNoTracking().AnyAsync(row => row.Id == EventId
                && row.EventStatusId == (int)EventStatusEnum.Published, cancellationToken);
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }

    private static Task<BaseCommandResponse<Guid>> PublishAsync(
        EventVisitorCapabilitySqliteFixture fixture, Explore.Domain.Event draft, EventPublicationMode mode) =>
        fixture.Services.GetRequiredService<EventPublicationExecutor>().ExecuteAsync(draft.Id,
            new PublishEventRequestDto { ExpectedConcurrencyStamp = draft.ConcurrencyStamp }, mode,
            CancellationToken.None);

    private static async Task<Explore.Domain.Event> ReadyDraftAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var draft = await fixture.SeedEventAsync();
        var tracked = await fixture.Context.Events.SingleAsync(value => value.Id == draft.Id);
        var session = new Explore.Domain.EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = tracked.Id,
            Event = tracked,
            TenantId = fixture.TenantId,
            Tenant = null!,
            Title = "Human publish regression session",
            SortOrder = 1,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var start = DateTimeOffset.UtcNow.AddDays(7);
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC",
            new EventScheduleProjectionCalculator());
        tracked.Sessions.Add(session);
        tracked.RecalculateScheduleSummaryFromSessions();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return await fixture.Context.Events.SingleAsync(value => value.Id == draft.Id);
    }
}
