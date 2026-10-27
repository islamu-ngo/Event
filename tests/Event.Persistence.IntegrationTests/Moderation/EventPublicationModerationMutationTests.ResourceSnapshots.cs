using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventPublicationModerationMutationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Resource_publish_batch_uses_current_Human_Tenant_restrictions_without_restricting_archive(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);
        var first = new EventResourceAuthorityRequest(fixture.TenantId, Guid.CreateVersion7(), fixture.UserId, false, "publish");
        var second = first with { ResourceId = Guid.CreateVersion7() };

        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var publication = await ReadHumanPublicationForResourceSnapshotAsync(fixture, clock.Now, token);
            var resources = await fixture.Services.GetRequiredService<IEventResourceModerationSnapshotReader>()
                .ReadBatchAsync([first, first with { Action = "archive" }, second], clock.Now, token);

            await Assert.That(publication.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
            await Assert.That(resources.Count).IsEqualTo(3);
            await Assert.That(resources[0].IsAdopted).IsTrue();
            await Assert.That(resources[0].Outcome).IsEqualTo(publication.Outcome);
            await Assert.That(resources[0].Subject!.Id).IsEqualTo(fixture.UserId);
            await Assert.That(resources[0].Subject!.Kind).IsEqualTo(ModerationSubjectKind.Human);
            await Assert.That(resources[0].CurrentUntil).IsEqualTo(publication.CurrentUntil);
            await Assert.That(resources[0].PermitsNativeEvaluationAt(clock.Now)).IsFalse();
            await Assert.That(resources[2]).IsEqualTo(resources[0]);
            await Assert.That(resources[1]).IsEqualTo(EventResourceModerationFacts.NotAdopted);
            await Assert.That(resources[1].PermitsNativeEvaluationAt(clock.Now)).IsTrue();
            return true;
        });

        await using var human = PublicationHuman(fixture);
        await human.InitializeAsync();
        await human.PrepareAsync();
        var response = await PublishAsync(human, draft, EventPublicationMode.Ordinary);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("event_publish_restricted");
        await AssertUnpublishedAsync(fixture, draft);
    }

    [Test]
    public async Task Qualified_Human_publication_nonadoption_retains_generation_without_granting_native_resource_permissions()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var parent = await ReadyDraftAsync(fixture);
        var adoption = await AdoptAsync(fixture, clock);
        await SelectServiceOnlyPublicationForResourceSnapshotAsync(fixture, clock, adoption);
        var outsider = NativeUser(Guid.CreateVersion7());
        var resourceId = Guid.CreateVersion7();
        var resource = EventResource.CreateDraft(resourceId, fixture.TenantId, parent.Id, null,
            new EventResourceMetadata
            {
                Title = "Native publication authority remains independent",
                Kind = (EventResourceKindEnum)1,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(fixture.TenantId, parent.Id, resourceId,
                EventResourceAudienceKindEnum.Organizer)], fixture.UserId, clock.Now.UtcDateTime);
        fixture.Context.Users.Add(outsider);
        fixture.Context.EventResources.Add(resource);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var ownerRequest = new EventResourceAuthorityRequest(fixture.TenantId, resourceId, fixture.UserId, false, "publish");

        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var native = fixture.Services.GetRequiredService<IEventResourceAuthoritySnapshotReader>();
            var owner = await native.ReadAsync(ownerRequest, clock.Now, token);
            var unprivileged = await native.ReadAsync(ownerRequest with { SubjectUserId = outsider.Id }, clock.Now, token);
            var publication = await ReadHumanPublicationForResourceSnapshotAsync(fixture, clock.Now, token);

            await Assert.That(publication.IsAdopted).IsFalse();
            await Assert.That(owner).IsNotNull();
            await Assert.That(unprivileged).IsNotNull();
            await Assert.That(owner!.Moderation.IsAdopted).IsFalse();
            await Assert.That(owner.Moderation.Outcome).IsEqualTo(publication.Outcome);
            await Assert.That(owner.Moderation.Generation).IsNotEqualTo(EventResourceModerationFacts.NotAdopted.Generation);
            await Assert.That(owner.Moderation.PermitsNativeEvaluationAt(clock.Now)).IsTrue();
            await Assert.That(unprivileged!.Moderation.IsAdopted).IsFalse();
            await Assert.That(unprivileged.Moderation.PermitsNativeEvaluationAt(clock.Now)).IsTrue();
            await Assert.That(owner.Management.OrganizerControl.IsEffectiveAt(clock.Now)).IsTrue();
            await Assert.That(unprivileged.Management.OrganizerControl.IsEffectiveAt(clock.Now)).IsFalse();
            await Assert.That(unprivileged.Management.Permissions.Count).IsEqualTo(0);
            return true;
        });
    }

    [Test]
    [Arguments("maintenance")]
    [Arguments("subject")]
    [Arguments("epoch")]
    [Arguments("history")]
    public async Task Resource_publication_fails_closed_for_unqualified_host_subject_epoch_or_policy_history(string unavailable)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var adoption = await AdoptAsync(fixture, clock);
        switch (unavailable)
        {
            case "maintenance":
                await fixture.Context.ServicePrincipals.Where(row => row.Id == adoption.Host.MaintenanceServicePrincipalId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                break;
            case "epoch":
                await fixture.Context.ModerationQualificationEpochs.ExecuteDeleteAsync();
                break;
            case "history":
                await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
                break;
        }
        var request = new EventResourceAuthorityRequest(fixture.TenantId, Guid.CreateVersion7(),
            unavailable == "subject" ? Guid.CreateVersion7() : fixture.UserId, false, "publish");

        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var publication = await new EventPublicationModerationSnapshotReader(fixture.Context,
                fixture.Services.GetRequiredService<IModerationRecognitionContinuityReader>())
                .ReadAsync(request.SubjectUserId!.Value, clock.Now, token);
            var resources = await fixture.Services.GetRequiredService<IEventResourceModerationSnapshotReader>()
                .ReadBatchAsync([request, request with { Action = "archive" }], clock.Now, token);

            await Assert.That(publication.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
            await Assert.That(resources[0].IsAdopted).IsTrue();
            await Assert.That(resources[0].Outcome).IsEqualTo(publication.Outcome);
            await Assert.That(resources[0].PermitsNativeEvaluationAt(clock.Now)).IsFalse();
            await Assert.That(resources[1]).IsEqualTo(EventResourceModerationFacts.NotAdopted);
            return true;
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Resource_publication_retains_scheduled_restriction_deadline_at_the_actual_Tenant(bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var startsAt = clock.Now.AddMinutes(1);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, startsAt, protective);
        var request = new EventResourceAuthorityRequest(fixture.TenantId, Guid.CreateVersion7(), fixture.UserId, false, "publish");

        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var reader = fixture.Services.GetRequiredService<IEventResourceModerationSnapshotReader>();
            var publication = await ReadHumanPublicationForResourceSnapshotAsync(fixture, clock.Now, token);
            var before = await reader.ReadAsync(request, clock.Now, token);
            var effective = await reader.ReadAsync(request, startsAt, token);

            await Assert.That(before.IsAdopted).IsTrue();
            await Assert.That(before.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
            await Assert.That(before.CurrentUntil).IsEqualTo((DateTimeOffset?)startsAt);
            await Assert.That(before.CurrentUntil).IsEqualTo(publication.CurrentUntil);
            await Assert.That(before.PermitsNativeEvaluationAt(startsAt.AddTicks(-1))).IsTrue();
            await Assert.That(before.PermitsNativeEvaluationAt(startsAt)).IsFalse();
            await Assert.That(effective.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
            await Assert.That(effective.CurrentUntil).IsEqualTo((DateTimeOffset?)startsAt.AddHours(1));
            await Assert.That(effective.PermitsNativeEvaluationAt(startsAt)).IsFalse();
            return true;
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Resource_publication_generation_changes_on_epoch_advance_even_when_native_publication_generation_does_not(
        bool adopted)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var adoption = await AdoptAsync(fixture, clock);
        if (!adopted) await SelectServiceOnlyPublicationForResourceSnapshotAsync(fixture, clock, adoption);
        var request = new EventResourceAuthorityRequest(fixture.TenantId, Guid.CreateVersion7(), fixture.UserId, false, "publish");

        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var reader = fixture.Services.GetRequiredService<IEventResourceModerationSnapshotReader>();
            var publicationBefore = await ReadHumanPublicationForResourceSnapshotAsync(fixture, clock.Now, token);
            var before = await reader.ReadAsync(request, clock.Now, token);
            await Assert.That(await reader.ReadAsync(request, clock.Now, token)).IsEqualTo(before);
            var epoch = await fixture.Context.ModerationQualificationEpochs.SingleAsync(token);
            epoch.Advance(epoch.Revision);
            await fixture.Context.SaveChangesAsync(token);
            var publicationAfter = await ReadHumanPublicationForResourceSnapshotAsync(fixture, clock.Now, token);
            var after = await reader.ReadAsync(request, clock.Now, token);

            await Assert.That(before.IsAdopted).IsEqualTo(adopted);
            await Assert.That(after.IsAdopted).IsEqualTo(adopted);
            await Assert.That(before.PermitsNativeEvaluationAt(clock.Now)).IsTrue();
            await Assert.That(after.PermitsNativeEvaluationAt(clock.Now)).IsTrue();
            await Assert.That(publicationAfter.Generation).IsEqualTo(publicationBefore.Generation);
            await Assert.That(publicationAfter == (publicationBefore with { QualificationRevision = epoch.Revision })).IsTrue();
            await Assert.That(after.Generation).IsNotEqualTo(before.Generation);
            return true;
        });
    }

    [Test]
    public async Task Resource_publication_without_the_native_serializable_transaction_is_unavailable_not_nonadoption()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        await AdoptAsync(fixture, clock);
        var request = new EventResourceAuthorityRequest(fixture.TenantId, Guid.CreateVersion7(), fixture.UserId, false, "publish");

        var facts = await fixture.Services.GetRequiredService<IEventResourceModerationSnapshotReader>()
            .ReadBatchAsync([request, request with { Action = "archive" }], clock.Now, CancellationToken.None);

        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(facts[0].IsAdopted).IsTrue();
        await Assert.That(facts[0].Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
        await Assert.That(facts[0].PermitsNativeEvaluationAt(clock.Now)).IsFalse();
        await Assert.That(facts[1]).IsEqualTo(EventResourceModerationFacts.NotAdopted);
    }

    private static Task<EventPublicationModerationFacts> ReadHumanPublicationForResourceSnapshotAsync(
        EventVisitorCapabilitySqliteFixture fixture, DateTimeOffset at, CancellationToken token) =>
        new EventPublicationModerationSnapshotReader(fixture.Context,
            fixture.Services.GetRequiredService<IModerationRecognitionContinuityReader>())
            .ReadAsync(fixture.UserId, at, token);

    private static async Task SelectServiceOnlyPublicationForResourceSnapshotAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock, Adoption adoption)
    {
        var declared = ModerationPolicyPublicationProjection.RestoreHistory(adoption.Publication)!.Proposed;
        var result = await PublishPolicyAsync(fixture, clock, adoption.Host, declared with
        {
            PublicationId = Guid.CreateVersion7(),
            ExpectedRevision = adoption.Publication.Revision,
            Profiles = [declared.Profiles.Single() with { SubjectKinds = [ModerationSubjectKind.Service] }],
            SupportedProfiles = []
        }, adoption.Publication.Id);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        fixture.Context.ChangeTracker.Clear();
    }
}
