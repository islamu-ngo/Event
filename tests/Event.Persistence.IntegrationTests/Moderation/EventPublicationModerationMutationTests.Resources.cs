using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain.Policies;
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
    public async Task Resource_publication_current_human_sanction_or_protection_leaves_no_state_stamp_or_audit_prefix(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedResourcePublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);

        var response = await PublishResourceAsync(fixture, original);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(EventResourceManagementFailureCodes.Forbidden);
        await Assert.That(await ResourcePublicationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Resource_qualified_absence_preserves_native_publish_withdraw_and_republish_with_exact_audits()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedResourcePublicationAsync(fixture, clock);
        await QualifyResourcePublicationAsync(fixture);
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);

        var published = await PublishResourceAsync(fixture, original);

        await Assert.That(published.IsSuccess).IsTrue().Because(published.Message);
        await Assert.That(published.Id).IsEqualTo(draft.Id);
        var current = await ResourcePublicationSnapshotAsync(fixture, draft.Id);
        await AssertResourcePublicationAsync(fixture, original, current, EventResourceAuditAction.Publish);
        var withdrawn = await fixture.Services.GetRequiredService<EventResourceManagementWorkflow>()
            .ChangeStateAsync(draft.Id, current.Stamp, EventResourceManagementAction.Unpublish, CancellationToken.None);
        await Assert.That(withdrawn.IsSuccess).IsTrue().Because(withdrawn.Message);
        var beforeRepublish = await ResourcePublicationSnapshotAsync(fixture, draft.Id);
        await Assert.That(beforeRepublish.StateId).IsEqualTo((int)EventResourcePublicationStateEnum.Withdrawn);

        var republished = await PublishResourceAsync(fixture, beforeRepublish);

        await Assert.That(republished.IsSuccess).IsTrue().Because(republished.Message);
        await AssertResourcePublicationAsync(fixture, beforeRepublish,
            await ResourcePublicationSnapshotAsync(fixture, draft.Id), EventResourceAuditAction.Republish);
        await Assert.That(await fixture.Context.EventResourceAuditEntries.CountAsync(row => row.EventResourceId == draft.Id
            && row.Action == EventResourceAuditAction.Publish)).IsEqualTo(1);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Resource_human_publish_restriction_blocks_republish_but_not_withdrawal_or_archive()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedResourcePublicationAsync(fixture, clock);
        var published = await PublishResourceAsync(fixture, draft);
        await Assert.That(published.IsSuccess).IsTrue().Because(published.Message);
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        var current = await ResourcePublicationSnapshotAsync(fixture, draft.Id);
        var workflow = fixture.Services.GetRequiredService<EventResourceManagementWorkflow>();

        var withdrawn = await workflow.ChangeStateAsync(draft.Id, current.Stamp,
            EventResourceManagementAction.Unpublish, CancellationToken.None);

        await Assert.That(withdrawn.IsSuccess).IsTrue().Because(withdrawn.Message);
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);
        await Assert.That(original.StateId).IsEqualTo((int)EventResourcePublicationStateEnum.Withdrawn);
        var rejected = await PublishResourceAsync(fixture, original);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(await ResourcePublicationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);

        var archived = await workflow.ChangeStateAsync(draft.Id, original.Stamp,
            EventResourceManagementAction.Archive, CancellationToken.None);

        await Assert.That(archived.IsSuccess).IsTrue().Because(archived.Message);
        await Assert.That((await ResourcePublicationSnapshotAsync(fixture, draft.Id)).StateId)
            .IsEqualTo((int)EventResourcePublicationStateEnum.Archived);
        await Assert.That(await fixture.Context.EventResourceAuditEntries.AnyAsync(row => row.EventResourceId == draft.Id
            && row.Action == EventResourceAuditAction.Republish)).IsFalse();
    }

    [Test]
    public async Task Resource_adopted_but_unavailable_human_publication_authority_does_not_publish_a_ready_resource()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedResourcePublicationAsync(fixture, clock);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);

        var response = await PublishResourceAsync(fixture, original);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(EventResourceManagementFailureCodes.Unavailable);
        await Assert.That(await ResourcePublicationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Resource_profile_support_retirement_does_not_erase_the_retained_human_publication_sanction()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedResourcePublicationAsync(fixture, clock);
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
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);

        var response = await PublishResourceAsync(fixture, original);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ResourcePublicationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
    }

    [Test]
    public async Task Resource_creator_sanction_does_not_replace_the_current_unrestricted_human_publisher()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedResourcePublicationAsync(fixture, clock);
        var author = NativeUser(Guid.CreateVersion7());
        fixture.Context.Users.Add(author);
        var resource = await fixture.Context.EventResources.SingleAsync(row => row.Id == draft.Id);
        resource.CreatedBy = author.Id;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, author.Id, clock.Now);
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);

        var response = await PublishResourceAsync(fixture, original);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await AssertResourcePublicationAsync(fixture, original,
            await ResourcePublicationSnapshotAsync(fixture, draft.Id), EventResourceAuditAction.Publish);
        await Assert.That((await fixture.Context.EventResources.AsNoTracking().SingleAsync(row => row.Id == draft.Id))
            .CreatedBy).IsEqualTo((Guid?)author.Id);
    }

    [Test]
    [Arguments("ordinary-permission")]
    [Arguments("parent-readiness")]
    [Arguments("protected-destination")]
    public async Task Resource_unrestricted_adoption_never_substitutes_for_native_publication_requirements(string missing)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedResourcePublicationAsync(fixture, clock, publishedParent: missing != "parent-readiness");
        await AdoptAsync(fixture, clock);
        if (missing == "ordinary-permission")
        {
            var other = NativeUser(Guid.CreateVersion7());
            var organizer = new Actor
            {
                Id = Guid.CreateVersion7(),
                UserId = other.Id,
                User = other,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Independent resource organizer" }
            };
            fixture.Context.Actors.Add(organizer);
            var parent = await fixture.Context.Events.SingleAsync(row => row.Id == draft.EventId);
            parent.ActorId = organizer.Id;
            parent.OrganizerActorId = organizer.Id;
            fixture.Context.PlatformUserRoles.RemoveRange(await fixture.Context.PlatformUserRoles
                .Where(row => row.UserId == fixture.UserId).ToListAsync());
            await fixture.Context.SaveChangesAsync();
        }
        else if (missing == "protected-destination")
        {
            var resource = await fixture.Context.EventResources.SingleAsync(row => row.Id == draft.Id);
            resource.SetExternalDestination("unreadable-protected-destination", 1, "https://resources.example.invalid",
                resource.ConcurrencyStamp, fixture.UserId, clock.Now.UtcDateTime);
            await fixture.Context.SaveChangesAsync();
        }
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);

        var response = await PublishResourceAsync(fixture, original);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ResourcePublicationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Resource_owning_commit_rolls_back_flushed_publication_stamp_and_audit_when_human_authority_changes(
        bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        var checkpoint = new AfterResourcePublicationFlush(clock, unavailable);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            ConfigureServices(services, () => privacy!, clock, [checkpoint]));
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var draft = await SeedResourcePublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var crossing = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, crossing);
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);
        checkpoint.ResourceId = draft.Id;
        checkpoint.Crossing = crossing;
        checkpoint.Armed = true;

        var response = await PublishResourceAsync(fixture, original);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(checkpoint.Observed).IsNotNull();
        var observed = checkpoint.Observed!;
        await Assert.That(observed.StateId).IsEqualTo((int)EventResourcePublicationStateEnum.Published);
        await Assert.That(observed.Stamp).IsNotEqualTo(original.Stamp);
        await Assert.That(observed.AuditCount).IsEqualTo(original.AuditCount + 1);
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await Assert.That(await ResourcePublicationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Resource_moderation_failure_is_not_disclosed_after_ordinary_publication_grants_expire(bool unavailable)
    {
        var clock = new PublicationClock();
        var externalProvider = new ExpiringResourcePublicationProvider(clock);
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            ConfigureServices(services, () => privacy, clock, []);
            services.AddSingleton<IEventResourceAuthorizationProvider>(externalProvider);
        });
        var draft = await SeedResourcePublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        if (unavailable)
            await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
        else
            await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);

        var parent = await fixture.Context.Events.SingleAsync(row => row.Id == draft.EventId);
        var otherHuman = NativeUser(Guid.CreateVersion7());
        var organizer = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            UserId = otherHuman.Id,
            Pii = new ActorPii { DisplayName = "Independent timed publication organizer" }
        };
        fixture.Context.AddRange(otherHuman, organizer);
        parent.ActorId = organizer.Id;
        parent.Actor = organizer;
        parent.OrganizerActorId = organizer.Id;
        parent.OrganizerActor = organizer;
        var expiresAt = clock.Now.AddMinutes(1);
        fixture.Context.EventRoleAssignments.Add(EventRoleAssignment.Create(fixture.TenantId, parent.Id,
            fixture.UserId, (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
            clock.Now.AddMinutes(-1).UtcDateTime, expiresAt.UtcDateTime, fixture.UserId));
        await fixture.Context.SaveChangesAsync();
        var original = await ResourcePublicationSnapshotAsync(fixture, draft.Id);
        externalProvider.ExpiresAt = expiresAt;
        externalProvider.Armed = true;

        var response = await PublishResourceAsync(fixture, original);

        await Assert.That(externalProvider.ObservedLiveStaffAuthority).IsTrue();
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(EventResourceManagementFailureCodes.NotFound);
        await Assert.That(await ResourcePublicationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    private sealed class ExpiringResourcePublicationProvider(PublicationClock clock) : IEventResourceAuthorizationProvider
    {
        internal bool Armed { get; set; }
        internal DateTimeOffset ExpiresAt { get; set; }
        internal bool ObservedLiveStaffAuthority { get; private set; }

        public Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Armed && input.Action == "publish")
            {
                Armed = false;
                ObservedLiveStaffAuthority = !input.Principal.ControlsOrganizer
                    && input.Principal.HasEventUpdate && input.Principal.HasEventPublish && input.Resource.DomainAllowed;
                clock.Now = ExpiresAt;
            }
            return Task.FromResult(EventResourceProviderDecision.Allow);
        }

        public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken token)
        {
            var decisions = new List<EventResourceProviderDecision>(inputs.Count);
            foreach (var input in inputs) decisions.Add(await CheckAsync(input, token));
            return decisions;
        }
    }

    private static async Task<ResourcePublicationSnapshot> SeedResourcePublicationAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock, bool publishedParent = true)
    {
        var settings = await fixture.Services.GetRequiredService<IEventResourceSettingsWriter>().ApplyAsync([
            new(null, GovernanceSettingKeys.EventResources.ExternalOrigins,
                EventResourceSettingMutationKind.SetValue, "[\"https://resources.example.invalid\"]")
        ], fixture.UserId);
        await Assert.That(settings.Success).IsTrue().Because(settings.FailureCode);
        var parent = await ReadyDraftAsync(fixture);
        if (publishedParent)
        {
            parent.Publish(clock.Now.UtcDateTime);
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();
        }
        var workflow = fixture.Services.GetRequiredService<EventResourceManagementWorkflow>();
        var resourceId = Guid.CreateVersion7();
        var created = await workflow.CreateAsync(parent.Id, resourceId, new EventResourceDraftDto
        {
            Title = "Native Human publication material",
            Kind = EventResourceKindEnum.GeneralDocument,
            DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly,
            DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink,
            AudienceRules = [new(EventResourceAudienceKindEnum.Public)]
        }, CancellationToken.None);
        await Assert.That(created.IsSuccess).IsTrue().Because(created.Message);
        var draft = await ResourcePublicationSnapshotAsync(fixture, resourceId);
        var configured = await workflow.ConfigureExternalDestinationAsync(resourceId, draft.Stamp,
            "https://resources.example.invalid/material", fixture.Services.GetRequiredService<IEventResourceDestinationProtector>(),
            CancellationToken.None);
        await Assert.That(configured.IsSuccess).IsTrue().Because(configured.Message);
        return await ResourcePublicationSnapshotAsync(fixture, resourceId);
    }

    private static async Task QualifyResourcePublicationAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var maintenanceId = Guid.CreateVersion7();
        var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), maintenanceId);
        fixture.Context.ServicePrincipals.Add(new ServicePrincipal
        {
            Id = maintenanceId,
            Code = $"resource-publication-{maintenanceId:N}",
            DisplayName = "Resource publication maintenance"
        });
        fixture.Context.ModerationHostBindings.Add(host);
        var tenant = await fixture.Context.Tenants.SingleAsync(row => row.Id == fixture.TenantId);
        fixture.Context.ModerationQualificationEpochs.Add(ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static Task<BaseCommandResponse<Guid>> PublishResourceAsync(
        EventVisitorCapabilitySqliteFixture fixture, ResourcePublicationSnapshot resource) =>
        fixture.Services.GetRequiredService<EventResourceManagementWorkflow>().ChangeStateAsync(resource.Id,
            resource.Stamp, EventResourceManagementAction.Publish, CancellationToken.None,
            fixture.Services.GetRequiredService<IEventResourceDestinationProtector>());

    private static async Task AssertResourcePublicationAsync(EventVisitorCapabilitySqliteFixture fixture,
        ResourcePublicationSnapshot before, ResourcePublicationSnapshot after, EventResourceAuditAction action)
    {
        await Assert.That(after.StateId).IsEqualTo((int)EventResourcePublicationStateEnum.Published);
        await Assert.That(after.Stamp).IsNotEqualTo(before.Stamp);
        await Assert.That(after.ParentStateId).IsEqualTo(before.ParentStateId);
        await Assert.That(after.ParentStamp).IsNotEqualTo(before.ParentStamp);
        await Assert.That(after.MaterialVersionId).IsEqualTo(before.MaterialVersionId);
        await Assert.That(after.Ciphertext).IsEqualTo(before.Ciphertext);
        await Assert.That(after.UpdatedBy).IsEqualTo((Guid?)fixture.UserId);
        await Assert.That(after.AuditCount).IsEqualTo(before.AuditCount + 1);
        var audit = await fixture.Context.EventResourceAuditEntries.AsNoTracking()
            .SingleAsync(row => row.EventResourceId == before.Id && row.Action == action);
        await Assert.That(audit.ResponsibleManagerUserId).IsEqualTo((Guid?)fixture.UserId);
        await Assert.That(audit.Outcome).IsEqualTo(EventResourceAuditOutcome.Succeeded);
        await Assert.That(audit.Reason).IsEqualTo(EventResourceAuditReason.OrganizerMutation);
        await Assert.That(audit.TenantId).IsEqualTo(fixture.TenantId);
    }

    private static Task<ResourcePublicationSnapshot> ResourcePublicationSnapshotAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid id)
    {
        fixture.Context.ChangeTracker.Clear();
        return ReadResourcePublicationSnapshotAsync(fixture.Context, id, CancellationToken.None);
    }

    private static async Task<ResourcePublicationSnapshot> ReadResourcePublicationSnapshotAsync(
        ExploreDbContext context, Guid id, CancellationToken token)
    {
        var resource = await context.EventResources.AsNoTracking().SingleAsync(row => row.Id == id, token);
        var parent = await context.Events.AsNoTracking().SingleAsync(row => row.Id == resource.EventId, token);
        var audits = await context.EventResourceAuditEntries.AsNoTracking()
            .Where(row => row.EventResourceId == id).ToListAsync(token);
        return new(resource.Id, resource.EventId, resource.PublicationStateId, resource.ConcurrencyStamp,
            parent.EventStatusId, parent.ConcurrencyStamp,
            resource.MaterialVersionId, resource.ExternalDestinationCiphertext, resource.UpdatedBy, resource.UpdatedAt,
            audits.Count, string.Join(",", audits.OrderBy(row => row.Id).Select(row => $"{row.Id:N}:{(int)row.Action}")));
    }

    private sealed record ResourcePublicationSnapshot(Guid Id, Guid EventId, int StateId, Guid Stamp,
        int ParentStateId, Guid ParentStamp,
        Guid? MaterialVersionId, string? Ciphertext, Guid? UpdatedBy, DateTime? UpdatedAt,
        int AuditCount, string AuditIdentity);

    private sealed class AfterResourcePublicationFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        internal Guid ResourceId { get; set; }
        internal DateTimeOffset Crossing { get; set; }
        internal bool Armed { get; set; }
        internal ResourcePublicationSnapshot? Observed { get; private set; }
        internal bool? ObservedPrivacyOwner { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context
                || !await context.EventResources.AsNoTracking().AnyAsync(row => row.Id == ResourceId
                    && row.PublicationStateId == (int)EventResourcePublicationStateEnum.Published, cancellationToken)
                || !await context.EventResourceAuditEntries.AsNoTracking().AnyAsync(row => row.EventResourceId == ResourceId
                    && row.Action == EventResourceAuditAction.Publish, cancellationToken))
                return result;
            Armed = false;
            Observed = await ReadResourcePublicationSnapshotAsync(context, ResourceId, cancellationToken);
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }
}
