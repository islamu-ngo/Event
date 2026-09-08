using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class EventAuthorityRulesTests
{
    [Test]
    public async Task Resolve_RecognizedPublisherWithoutOrganizer_GrantsListingAuthorityOnly()
    {
        var authority = EventAuthorityRules.Resolve(
            (int)EventProvenanceTypeEnum.CommunityReported,
            Guid.CreateVersion7(),
            organizerActorId: null);

        await Assert.That(authority.HasListingAuthority).IsTrue();
        await Assert.That(authority.HasParticipationManagementAuthority).IsFalse();
        await Assert.That(authority.HasDataCollectionAuthority).IsFalse();
        await Assert.That(authority.HasCommercialAuthority).IsFalse();
    }

    [Test]
    public async Task Resolve_RecognizedPublisherWithOrganizer_GrantsOrganizerAuthorities()
    {
        var authority = EventAuthorityRules.Resolve(
            (int)EventProvenanceTypeEnum.OrganizerCreated,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await Assert.That(authority.HasListingAuthority).IsTrue();
        await Assert.That(authority.HasParticipationManagementAuthority).IsTrue();
        await Assert.That(authority.HasDataCollectionAuthority).IsTrue();
        await Assert.That(authority.HasCommercialAuthority).IsTrue();
    }

    [Test]
    public async Task Resolve_UnknownProvenance_FailsClosed()
    {
        var authority = EventAuthorityRules.Resolve(999, Guid.CreateVersion7(), Guid.CreateVersion7());

        await Assert.That(authority.HasListingAuthority).IsFalse();
        await Assert.That(authority.HasParticipationManagementAuthority).IsFalse();
        await Assert.That(authority.HasDataCollectionAuthority).IsFalse();
        await Assert.That(authority.HasCommercialAuthority).IsFalse();
    }

    [Test]
    public async Task Resolve_EmptyPublishingActor_FailsClosed()
    {
        var authority = EventAuthorityRules.Resolve(
            (int)EventProvenanceTypeEnum.TenantCurated,
            Guid.Empty,
            Guid.CreateVersion7());

        await Assert.That(authority.HasListingAuthority).IsFalse();
        await Assert.That(authority.HasParticipationManagementAuthority).IsFalse();
        await Assert.That(authority.HasDataCollectionAuthority).IsFalse();
        await Assert.That(authority.HasCommercialAuthority).IsFalse();
    }

    [Test]
    [Arguments((int)ParticipationHandlingModeEnum.InformationOnly, false)]
    [Arguments((int)ParticipationHandlingModeEnum.WalkIn, false)]
    [Arguments((int)ParticipationHandlingModeEnum.ExternalManaged, false)]
    [Arguments((int)ParticipationHandlingModeEnum.PlatformManaged, true)]
    [Arguments(999, false)]
    public async Task IsNativeWorkflowAllowed_PinsFailClosedModeMatrix(int modeId, bool expected)
    {
        var result = EventAuthorityRules.IsNativeWorkflowAllowed(modeId);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments((int)ParticipationHandlingModeEnum.InformationOnly, (int)EventPublicActionKindEnum.ExternalRegistration, false)]
    [Arguments((int)ParticipationHandlingModeEnum.InformationOnly, (int)EventPublicActionKindEnum.OptionalQuestionnaire, false)]
    [Arguments((int)ParticipationHandlingModeEnum.WalkIn, (int)EventPublicActionKindEnum.ExternalRegistration, false)]
    [Arguments((int)ParticipationHandlingModeEnum.WalkIn, (int)EventPublicActionKindEnum.OptionalQuestionnaire, true)]
    [Arguments((int)ParticipationHandlingModeEnum.ExternalManaged, (int)EventPublicActionKindEnum.ExternalRegistration, true)]
    [Arguments((int)ParticipationHandlingModeEnum.ExternalManaged, (int)EventPublicActionKindEnum.OptionalQuestionnaire, true)]
    [Arguments((int)ParticipationHandlingModeEnum.PlatformManaged, (int)EventPublicActionKindEnum.ExternalRegistration, false)]
    [Arguments((int)ParticipationHandlingModeEnum.PlatformManaged, (int)EventPublicActionKindEnum.OptionalQuestionnaire, true)]
    public async Task IsPublicActionAllowed_PinsParticipationActionMatrix(
        int modeId,
        int actionKindId,
        bool expected)
    {
        var result = EventAuthorityRules.IsPublicActionAllowed(modeId, actionKindId);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task IsPublicActionAllowed_UnrelatedRecognizedActionsRemainAllowed()
    {
        ParticipationHandlingModeEnum[] modes =
        [
            ParticipationHandlingModeEnum.InformationOnly,
            ParticipationHandlingModeEnum.WalkIn,
            ParticipationHandlingModeEnum.ExternalManaged,
            ParticipationHandlingModeEnum.PlatformManaged
        ];
        EventPublicActionKindEnum[] unrelatedActions =
        [
            EventPublicActionKindEnum.OriginalSource,
            EventPublicActionKindEnum.ExternalEventPage,
            EventPublicActionKindEnum.Livestream,
            EventPublicActionKindEnum.OrganizerContact
        ];

        foreach (var mode in modes)
        {
            foreach (var action in unrelatedActions)
            {
                await Assert.That(EventAuthorityRules.IsPublicActionAllowed((int)mode, (int)action)).IsTrue();
            }
        }
    }

    [Test]
    [Arguments(999, (int)EventPublicActionKindEnum.OptionalQuestionnaire)]
    [Arguments((int)ParticipationHandlingModeEnum.ExternalManaged, 999)]
    public async Task IsPublicActionAllowed_UnknownIdsFailClosed(int modeId, int actionKindId)
    {
        await Assert.That(EventAuthorityRules.IsPublicActionAllowed(modeId, actionKindId)).IsFalse();
    }
}
