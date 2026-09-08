using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class ParticipationRequirementAttachmentTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Create_InitializesAStrongConcurrencyStamp()
    {
        Fixture fixture = CreateFixture(
            ParticipationHandlingModeEnum.InformationOnly,
            RegistrationRequirementCriticalityEnum.Informational,
            nativeChannel: true);

        await Assert.That(fixture.Configuration.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
        await Assert.That(fixture.Configuration.ConcurrencyStamp.Version).IsEqualTo(7);
    }

    [Test]
    [Arguments(ParticipationHandlingModeEnum.InformationOnly, RegistrationRequirementCriticalityEnum.Informational, false)]
    [Arguments(ParticipationHandlingModeEnum.WalkIn, RegistrationRequirementCriticalityEnum.Informational, true)]
    [Arguments(ParticipationHandlingModeEnum.ExternalManaged, RegistrationRequirementCriticalityEnum.Optional, false)]
    [Arguments(ParticipationHandlingModeEnum.PlatformManaged, RegistrationRequirementCriticalityEnum.Required, false)]
    public async Task AttachRequirement_AcceptsTheSupportedMatrix(
        ParticipationHandlingModeEnum mode,
        RegistrationRequirementCriticalityEnum criticality,
        bool standalone)
    {
        Fixture fixture = CreateFixture(mode, criticality, nativeChannel: mode != ParticipationHandlingModeEnum.ExternalManaged);
        RegistrationFormVersion? version = standalone ? PublishedVersion(fixture.TenantId, fixture.EventId) : null;
        Guid previousStamp = fixture.Configuration.ConcurrencyStamp;

        ParticipationRequirementAttachment attachment = fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, version, standalone, Now.AddMinutes(1));

        await Assert.That(attachment.RegistrationRequirementId).IsEqualTo(fixture.Requirement.Id);
        await Assert.That(attachment.IsStandaloneQuestionnaire).IsEqualTo(standalone);
        await Assert.That(fixture.Configuration.ConcurrencyStamp).IsNotEqualTo(previousStamp);
    }

    [Test]
    [Arguments(ParticipationHandlingModeEnum.InformationOnly, RegistrationRequirementCriticalityEnum.Required, true)]
    [Arguments(ParticipationHandlingModeEnum.WalkIn, RegistrationRequirementCriticalityEnum.Optional, true)]
    [Arguments(ParticipationHandlingModeEnum.ExternalManaged, RegistrationRequirementCriticalityEnum.Required, false)]
    [Arguments(ParticipationHandlingModeEnum.ExternalManaged, RegistrationRequirementCriticalityEnum.Optional, true)]
    public async Task AttachRequirement_RejectsInvalidModeAndChannelCombinations(
        ParticipationHandlingModeEnum mode,
        RegistrationRequirementCriticalityEnum criticality,
        bool nativeChannel)
    {
        Fixture fixture = CreateFixture(mode, criticality, nativeChannel);

        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, null, false, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AttachRequirement_RejectsForeignDeletedDuplicateAndSecondStandalone()
    {
        Fixture fixture = CreateFixture(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCriticalityEnum.Informational,
            nativeChannel: true);
        RegistrationFormVersion published = PublishedVersion(fixture.TenantId, fixture.EventId);
        fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, published, true, Now.AddMinutes(1));

        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, published, false, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();

        RegistrationRequirement second = Requirement(fixture.Workflow, 2,
            RegistrationRequirementCriticalityEnum.Informational);
        second.AddChannel(RegistrationChannel.Create(second, 1, true, null, Now));
        fixture.Workflow.AddRequirement(second);
        RegistrationFormVersion secondPublished = PublishedVersion(fixture.TenantId, fixture.EventId);
        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, second, secondPublished, true, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();

        RegistrationWorkflow foreignWorkflow = RegistrationWorkflow.Create(
            fixture.TenantId, Guid.CreateVersion7(), "registration", Now);
        RegistrationRequirement foreign = Requirement(foreignWorkflow, 1,
            RegistrationRequirementCriticalityEnum.Informational);
        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), foreignWorkflow, foreign, null, false, Now.AddMinutes(2)))
            .Throws<ArgumentException>();

        RegistrationRequirement deleted = Requirement(fixture.Workflow, 3,
            RegistrationRequirementCriticalityEnum.Informational);
        fixture.Workflow.AddRequirement(deleted);
        fixture.Workflow.RemoveRequirement(deleted, Now.AddMinutes(1));
        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, deleted, null, false, Now.AddMinutes(2)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AttachRequirement_RejectsCrossTenantAndInvalidFormVersionOwnership()
    {
        Fixture fixture = CreateFixture(
            ParticipationHandlingModeEnum.WalkIn,
            RegistrationRequirementCriticalityEnum.Informational,
            nativeChannel: true);
        RegistrationWorkflow foreignTenantWorkflow = RegistrationWorkflow.Create(
            Guid.CreateVersion7(), fixture.EventId, "registration", Now);
        RegistrationRequirement foreignTenantRequirement = Requirement(
            foreignTenantWorkflow, 1, RegistrationRequirementCriticalityEnum.Informational);
        foreignTenantRequirement.AddChannel(
            RegistrationChannel.Create(foreignTenantRequirement, 1, true, null, Now));
        foreignTenantWorkflow.AddRequirement(foreignTenantRequirement);

        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), foreignTenantWorkflow, foreignTenantRequirement, null, false, Now.AddMinutes(1)))
            .Throws<ArgumentException>();

        RegistrationFormVersion foreignEventVersion = PublishedVersion(fixture.TenantId, Guid.CreateVersion7());
        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, foreignEventVersion, true, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();

        RegistrationFormVersion deletedVersion = PublishedVersion(fixture.TenantId, fixture.EventId);
        deletedVersion.IsDeleted = true;
        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, deletedVersion, true, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();

        RegistrationFormVersion incompleteVersion = PublishedVersion(fixture.TenantId, fixture.EventId);
        typeof(RegistrationFormVersion).GetProperty(nameof(RegistrationFormVersion.MappingArtifact))!
            .SetValue(incompleteVersion, " ");
        await Assert.That(() => fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, incompleteVersion, true, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DetachRequirement_IsIdempotentAndLeavesTheRequirementUntouched()
    {
        Fixture fixture = CreateFixture(
            ParticipationHandlingModeEnum.PlatformManaged,
            RegistrationRequirementCriticalityEnum.Required,
            nativeChannel: true);
        ParticipationRequirementAttachment attachment = fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, null, false, Now.AddMinutes(1));

        bool first = fixture.Configuration.DetachRequirement(fixture.Requirement.Id, Now.AddMinutes(2));
        Guid detachedStamp = fixture.Configuration.ConcurrencyStamp;
        bool second = fixture.Configuration.DetachRequirement(fixture.Requirement.Id, Now.AddMinutes(3));

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
        await Assert.That(attachment.IsDeleted).IsTrue();
        await Assert.That(fixture.Requirement.IsDeleted).IsFalse();
        await Assert.That(fixture.Configuration.ConcurrencyStamp).IsEqualTo(detachedStamp);
    }

    [Test]
    public async Task Reconfigure_RejectsAHandlingModeThatInvalidatesAnActiveAttachment()
    {
        Fixture fixture = CreateFixture(
            ParticipationHandlingModeEnum.PlatformManaged,
            RegistrationRequirementCriticalityEnum.Required,
            nativeChannel: true);
        fixture.Configuration.AttachRequirement(
            Guid.CreateVersion7(), fixture.Workflow, fixture.Requirement, null, false, Now.AddMinutes(1));
        Guid attachedStamp = fixture.Configuration.ConcurrencyStamp;

        await Assert.That(() => fixture.Configuration.Reconfigure(
            (int)ParticipationHandlingModeEnum.WalkIn,
            (int)AdvanceRegistrationObligationEnum.NotApplicable,
            null,
            null)).Throws<InvalidOperationException>();

        await Assert.That(fixture.Configuration.ParticipationHandlingModeId)
            .IsEqualTo((int)ParticipationHandlingModeEnum.PlatformManaged);
        await Assert.That(fixture.Configuration.ConcurrencyStamp).IsEqualTo(attachedStamp);
    }

    private static Fixture CreateFixture(
        ParticipationHandlingModeEnum mode,
        RegistrationRequirementCriticalityEnum criticality,
        bool nativeChannel)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventParticipationConfiguration configuration = EventParticipationConfiguration.Create(
            eventId, tenantId, (int)mode,
            mode == ParticipationHandlingModeEnum.ExternalManaged
                ? (int)AdvanceRegistrationObligationEnum.Optional
                : mode == ParticipationHandlingModeEnum.PlatformManaged
                    ? (int)AdvanceRegistrationObligationEnum.Required
                    : (int)AdvanceRegistrationObligationEnum.NotApplicable,
            mode == ParticipationHandlingModeEnum.PlatformManaged ? (int)IdentityAccessModeEnum.AccountRequired : null,
            null,
            Now);
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "registration", Now);
        RegistrationRequirement requirement = Requirement(workflow, 1, criticality);
        requirement.AddChannel(RegistrationChannel.Create(
            requirement,
            1,
            nativeChannel,
            nativeChannel ? null : Guid.CreateVersion7(),
            Now));
        workflow.AddRequirement(requirement);
        return new(configuration, workflow, requirement, tenantId, eventId);
    }

    private static RegistrationRequirement Requirement(
        RegistrationWorkflow workflow,
        int ordinal,
        RegistrationRequirementCriticalityEnum criticality) =>
        RegistrationRequirement.Create(
            workflow,
            ordinal,
            criticality,
            criticality != RegistrationRequirementCriticalityEnum.Required,
            criticality switch
            {
                RegistrationRequirementCriticalityEnum.Required => RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
                RegistrationRequirementCriticalityEnum.Optional => RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
                _ => RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect
            },
            RegistrationAnswerSyncModeEnum.NONE,
            RegistrationRequirementSubjectTypeEnum.AllOrders,
            null,
            Now);

    private static RegistrationFormVersion PublishedVersion(Guid tenantId, Guid eventId)
    {
        RegistrationForm form = RegistrationForm.Create(
            tenantId, eventId, "platform.registration", Guid.CreateVersion7().ToString("N"), "Questionnaire", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        RegistrationFormSection section = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, 1, "Questions", Now);
        version.AddSection(section);
        version.PinGeneratedSchemaBundle(
            $"{{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"{version.Id:D}\",\"version\":1,\"languageTag\":\"en\",\"data\":{{}},\"ui\":{{}},\"logic\":{{}},\"mapping\":{{}}}}",
            Now.AddMinutes(1));
        return version;
    }

    private sealed record Fixture(
        EventParticipationConfiguration Configuration,
        RegistrationWorkflow Workflow,
        RegistrationRequirement Requirement,
        Guid TenantId,
        Guid EventId);
}
