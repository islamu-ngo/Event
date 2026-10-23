using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
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
    public async Task Registration_form_qualified_absence_or_unrestricted_adoption_pins_the_native_schema_bundle(bool adopted)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedRegistrationPublicationAsync(fixture, clock);
        if (adopted) await AdoptAsync(fixture, clock);
        else await QualifyRegistrationPublicationAsync(fixture);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var expected = new FormSchemaArtifactGenerator().Generate(draft);

        var response = await PublishRegistrationAsync(fixture, draft);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await AssertRegistrationPublishedAsync(await RegistrationSnapshotAsync(fixture, draft.Id),
            draft, expected, fixture.UserId, clock.Now.UtcDateTime);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Registration_form_moderation_neither_grants_ordinary_permission_nor_bypasses_native_preflight(bool adopted)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedRegistrationPublicationAsync(fixture, clock, withField: false, grantOwner: false);
        if (adopted) await AdoptAsync(fixture, clock);
        else await QualifyRegistrationPublicationAsync(fixture);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await RegistrationSnapshotAsync(fixture, draft.Id);

        await Assert.ThrowsAsync<AuthorizationException>(async () =>
            _ = await PublishRegistrationAsync(fixture, draft));
        await Assert.That(await RegistrationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);

        await GrantRegistrationPublisherAsync(fixture, draft.EventId);
        var response = await PublishRegistrationAsync(fixture, draft);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.FailureCode).IsEqualTo("registration_form_preflight_failed");
        await Assert.That(await RegistrationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Registration_form_current_human_sanction_or_protection_blocks_before_native_preflight(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedRegistrationPublicationAsync(fixture, clock, withField: false);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await RegistrationSnapshotAsync(fixture, draft.Id);

        var response = await PublishRegistrationAsync(fixture, draft);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.FailureCode).IsEqualTo("registration_form_publish_restricted");
        await Assert.That(await RegistrationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Registration_form_adopted_unknown_authority_fails_closed_before_native_preflight()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedRegistrationPublicationAsync(fixture, clock, withField: false);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await RegistrationSnapshotAsync(fixture, draft.Id);

        var response = await PublishRegistrationAsync(fixture, draft);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.FailureCode).IsEqualTo("registration_form_publish_authority_unavailable");
        await Assert.That(await RegistrationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
    }

    [Test]
    public async Task Registration_form_original_author_does_not_substitute_for_the_current_human_publisher()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedRegistrationPublicationAsync(fixture, clock);
        var author = NativeUser(Guid.CreateVersion7());
        fixture.Context.Users.Add(author);
        var tracked = await fixture.Context.RegistrationFormVersions.SingleAsync(row => row.Id == draft.Id);
        tracked.CreatedBy = author.Id;
        var form = await fixture.Context.RegistrationForms.SingleAsync(row => row.Id == draft.RegistrationFormId);
        form.CreatedBy = author.Id;
        await fixture.Context.SaveChangesAsync();
        draft = tracked;
        fixture.Context.ChangeTracker.Clear();
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, author.Id, clock.Now);
        await RemoveRegistrationPublisherAdminAsync(fixture);

        var response = await PublishRegistrationAsync(fixture, draft);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        var published = await RegistrationSnapshotAsync(fixture, draft.Id);
        await Assert.That(published.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Published);
        await Assert.That(published.UpdatedBy).IsEqualTo((Guid?)fixture.UserId);
        await Assert.That((await fixture.Context.RegistrationFormVersions.AsNoTracking()
            .SingleAsync(row => row.Id == draft.Id)).CreatedBy).IsEqualTo((Guid?)author.Id);

        var nextDraft = await SeedRegistrationPublicationAsync(fixture, clock);
        var original = await RegistrationSnapshotAsync(fixture, nextDraft.Id);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        var blocked = await PublishRegistrationAsync(fixture, nextDraft);

        await Assert.That(blocked.IsSuccess).IsFalse();
        await Assert.That(blocked.Id).IsEqualTo(nextDraft.Id);
        await Assert.That(blocked.FailureCode).IsEqualTo("registration_form_publish_restricted");
        await Assert.That(await RegistrationSnapshotAsync(fixture, nextDraft.Id)).IsEqualTo(original);
    }

    [Test]
    public async Task Registration_form_unrestricted_adoption_does_not_rebind_a_version_to_another_event_or_form()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedRegistrationPublicationAsync(fixture, clock);
        var other = await SeedRegistrationPublicationAsync(fixture, clock);
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await RegistrationSnapshotAsync(fixture, draft.Id);
        var otherOriginal = await RegistrationSnapshotAsync(fixture, other.Id);
        var service = fixture.Services.GetRequiredService<RegistrationFormAuthoringCommandService>();

        foreach (var request in new[]
        {
            new PublishRegistrationFormVersionCommand(other.EventId, draft.RegistrationFormId, draft.Id, draft.ConcurrencyStamp),
            new PublishRegistrationFormVersionCommand(draft.EventId, other.RegistrationFormId, draft.Id, draft.ConcurrencyStamp)
        })
        {
            await Assert.ThrowsAsync<NotFoundException>(async () =>
                _ = await service.PublishAsync(request, CancellationToken.None));
            await Assert.That(await RegistrationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
            await Assert.That(await RegistrationSnapshotAsync(fixture, other.Id)).IsEqualTo(otherOriginal);
            await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        }
    }

    [Test]
    public async Task Registration_form_profile_support_retirement_preserves_the_canonical_publish_restriction()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedRegistrationPublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        var declared = ModerationPolicyPublicationProjection.RestoreHistory(adoption.Publication)!.Proposed;
        var retired = await PublishPolicyAsync(fixture, clock, adoption.Host, declared with
        {
            PublicationId = Guid.CreateVersion7(), ExpectedRevision = adoption.Publication.Revision,
            SupportedProfiles = []
        }, adoption.Publication.Id);
        await Assert.That(retired.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await RegistrationSnapshotAsync(fixture, draft.Id);

        var response = await PublishRegistrationAsync(fixture, draft);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(draft.Id);
        await Assert.That(response.FailureCode).IsEqualTo("registration_form_publish_restricted");
        await Assert.That(await RegistrationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Registration_form_owning_commit_rolls_back_the_entire_flushed_bundle_on_restriction_or_unknown_authority(
        bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        var checkpoint = new AfterRegistrationPublicationFlush(clock, unavailable);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            ConfigureServices(services, () => privacy!, clock, [checkpoint]));
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var draft = await SeedRegistrationPublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var crossing = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, crossing);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await RegistrationSnapshotAsync(fixture, draft.Id);
        var expected = new FormSchemaArtifactGenerator().Generate(draft);
        var publicationUtc = clock.Now.UtcDateTime;
        checkpoint.VersionId = draft.Id;
        checkpoint.Crossing = crossing;
        checkpoint.Armed = true;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await PublishRegistrationAsync(fixture, draft));

        await Assert.That(checkpoint.Observed).IsNotNull();
        await AssertRegistrationPublishedAsync(checkpoint.Observed!, draft, expected, fixture.UserId, publicationUtc);
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await Assert.That(await RegistrationSnapshotAsync(fixture, draft.Id)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private static async Task<RegistrationFormVersion> SeedRegistrationPublicationAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock, bool withField = true, bool grantOwner = true)
    {
        var parent = await ReadyDraftAsync(fixture);
        if (!grantOwner)
        {
            // Without an event role, the caller must not inherit publication authority as organizer either.
            var organizer = NativeUser(Guid.CreateVersion7());
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                UserId = organizer.Id, Pii = new ActorPii { DisplayName = "Independent form organizer" },
                CreatedAt = clock.Now.UtcDateTime
            };
            fixture.Context.Users.Add(organizer);
            fixture.Context.Actors.Add(actor);
            parent.ActorId = actor.Id;
            parent.Actor = actor;
            parent.OrganizerActorId = actor.Id;
            parent.OrganizerActor = actor;
        }
        var form = RegistrationForm.Create(fixture.TenantId, parent.Id, "platform.registration",
            "publication", "Publication registration details", clock.Now.UtcDateTime);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, clock.Now.UtcDateTime);
        form.CreatedBy = fixture.UserId;
        version.CreatedBy = fixture.UserId;
        if (withField)
        {
            var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", clock.Now.UtcDateTime);
            version.AddSection(section);
            version.AddField(section, RegistrationFormField.Create(Guid.CreateVersion7(), section, 1,
                "platform.registration", "email", "Email", RegistrationFieldTypeEnum.Email, 1,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, clock.Now.UtcDateTime));
        }
        form.AddVersion(version);
        fixture.Context.RegistrationForms.Add(form);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        if (grantOwner) await GrantRegistrationPublisherAsync(fixture, parent.Id);
        return version;
    }

    private static async Task GrantRegistrationPublisherAsync(EventVisitorCapabilitySqliteFixture fixture, Guid eventId)
    {
        await Assert.That(await fixture.Context.RolePermissions.AnyAsync(row => row.RoleId == (int)RoleEnum.EventOwner
            && row.Permission.MasterCode == PermissionCodes.EventRegistrationManage && row.Permission.IsActive)).IsTrue();
        fixture.Context.EventRoleAssignments.Add(EventRoleAssignment.Create(fixture.TenantId, eventId, fixture.UserId,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UnixEpoch, null, fixture.UserId));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task QualifyRegistrationPublicationAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var maintenanceId = Guid.CreateVersion7();
        var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), maintenanceId);
        fixture.Context.ServicePrincipals.Add(new ServicePrincipal
        {
            Id = maintenanceId, Code = $"form-publication-{maintenanceId:N}", DisplayName = "Form publication maintenance"
        });
        fixture.Context.ModerationHostBindings.Add(host);
        var tenant = await fixture.Context.Tenants.SingleAsync(row => row.Id == fixture.TenantId);
        fixture.Context.ModerationQualificationEpochs.Add(ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task RemoveRegistrationPublisherAdminAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        // Policy setup needs administration; native form publication must use the real event permission instead.
        fixture.Context.PlatformUserRoles.RemoveRange(await fixture.Context.PlatformUserRoles
            .Where(row => row.UserId == fixture.UserId).ToListAsync());
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static Task<BaseCommandResponse<Guid>> PublishRegistrationAsync(
        EventVisitorCapabilitySqliteFixture fixture, RegistrationFormVersion version) =>
        fixture.ExecuteCommandAsync<PublishRegistrationFormVersionCommand, BaseCommandResponse<Guid>>(
            new(version.EventId, version.RegistrationFormId, version.Id, version.ConcurrencyStamp));

    private static async Task<RegistrationPublicationSnapshot> RegistrationSnapshotAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid versionId)
    {
        fixture.Context.ChangeTracker.Clear();
        return RegistrationPublicationSnapshot.From(await fixture.Context.RegistrationFormVersions.AsNoTracking()
            .SingleAsync(row => row.Id == versionId));
    }

    private static async Task AssertRegistrationPublishedAsync(RegistrationPublicationSnapshot actual,
        RegistrationFormVersion draft, FormSchemaArtifactBundle expected, Guid userId, DateTime publishedAt)
    {
        await Assert.That(actual.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Published);
        await Assert.That(actual.SchemaHash).IsEqualTo(expected.SchemaHash);
        await Assert.That(actual.Data).IsEqualTo(expected.DataSchemaJson);
        await Assert.That(actual.Ui).IsEqualTo(expected.UiSchemaJson);
        await Assert.That(actual.Logic).IsEqualTo(expected.LogicSchemaJson);
        await Assert.That(actual.Mapping).IsEqualTo(expected.MappingArtifactJson);
        await Assert.That(actual.PublishedAt).IsEqualTo((DateTime?)publishedAt);
        await Assert.That(actual.UpdatedBy).IsEqualTo((Guid?)userId);
        await Assert.That(actual.Stamp).IsNotEqualTo(draft.ConcurrencyStamp);
    }

    private sealed record RegistrationPublicationSnapshot(int StatusId, Guid Stamp, string? SchemaHash,
        string? Data, string? Ui, string? Logic, string? Mapping, DateTime? PublishedAt,
        Guid? UpdatedBy, DateTime? UpdatedAt)
    {
        internal static RegistrationPublicationSnapshot From(RegistrationFormVersion version) =>
            new(version.StatusId, version.ConcurrencyStamp, version.SchemaHash, version.DataSchemaArtifact,
                version.UiSchemaArtifact, version.LogicSchemaArtifact, version.MappingArtifact, version.PublishedAt,
                version.UpdatedBy, version.UpdatedAt);
    }

    private sealed class AfterRegistrationPublicationFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        internal Guid VersionId { get; set; }
        internal DateTimeOffset Crossing { get; set; }
        internal bool Armed { get; set; }
        internal RegistrationPublicationSnapshot? Observed { get; private set; }
        internal bool? ObservedPrivacyOwner { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            var version = await context.RegistrationFormVersions.AsNoTracking().SingleOrDefaultAsync(
                row => row.Id == VersionId && row.StatusId == (int)RegistrationFormStatusEnum.Published, cancellationToken);
            if (version is null) return result;
            Armed = false;
            Observed = RegistrationPublicationSnapshot.From(version);
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }
}
