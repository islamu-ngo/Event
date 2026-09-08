// ABOUTME: Verifies anonymous challenge issue eligibility through real event and shared visitor policy facts.
// ABOUTME: Rejects wrong tenant, nonpublic lifecycle, deleted participation and account-required modes without mocks.

using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.RegistrationOrders;

public sealed class AnonymousRegistrationChallengeIssuePolicyTests
{
    [Test]
    [Arguments(VisitorAccessMode.FullRegistrationAndAuth, true)]
    [Arguments(VisitorAccessMode.AnonymousOnly, true)]
    [Arguments(VisitorAccessMode.DirectoryListingOnly, false)]
    public async Task SharedVisitorAuthorityControlsNativeIssuance(VisitorAccessMode mode, bool expected)
    {
        var target = Event();
        var capability = VisitorAccessCapabilityResolver.EvaluateProposedState(new VisitorAccessPolicyState(mode, []));
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, target.TenantId, capability)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(EventStatusEnum.Draft)]
    [Arguments(EventStatusEnum.Cancelled)]
    [Arguments(EventStatusEnum.Archived)]
    [Arguments(EventStatusEnum.Moderated)]
    public async Task NonpublicLifecycleCannotAcquireChallenge(EventStatusEnum status)
    {
        var target = Event(status);
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, target.TenantId, Capability())).IsFalse();
    }

    [Test]
    public async Task WrongTenantPrivateDeletedAndMissingParticipationFailClosed()
    {
        var target = Event();
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, Guid.CreateVersion7(), Capability())).IsFalse();
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(null, target.TenantId, Capability())).IsFalse();
        target.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, target.TenantId, Capability())).IsFalse();
        target.VisibilityTypeId = (int)VisibilityTypeEnum.Public;
        target.IsDeleted = true;
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, target.TenantId, Capability())).IsFalse();
        target.IsDeleted = false;
        target.ParticipationConfiguration!.IsDeleted = true;
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, target.TenantId, Capability())).IsFalse();
        target.ParticipationConfiguration = null;
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, target.TenantId, Capability())).IsFalse();
    }

    [Test]
    [Arguments(IdentityAccessModeEnum.AccountRequired, false)]
    [Arguments(IdentityAccessModeEnum.GuestAllowed, true)]
    [Arguments(IdentityAccessModeEnum.CapabilityTokenAllowed, true)]
    public async Task ParticipationIdentityAuthorityIsPreserved(IdentityAccessModeEnum mode, bool expected)
    {
        var target = Event(identity: mode);
        await Assert.That(AnonymousRegistrationChallengeIssuePolicy.CanIssue(target, target.TenantId, Capability())).IsEqualTo(expected);
    }

    private static VisitorAccessCapability Capability() => VisitorAccessCapabilityResolver.EvaluateProposedState(
        new VisitorAccessPolicyState(VisitorAccessMode.AnonymousOnly, []));

    private static Explore.Domain.Event Event(EventStatusEnum status = EventStatusEnum.Published,
        IdentityAccessModeEnum identity = IdentityAccessModeEnum.GuestAllowed)
    {
        var target = new Explore.Domain.Event(status)
        {
            Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Tenant = null!, Title = "Public event",
            Actor = null!, EventFormat = null!, EventStatus = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!
        };
        target.ParticipationConfiguration = EventParticipationConfiguration.Create(target.Id, target.TenantId,
            (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
            (int)identity, identity switch
            {
                IdentityAccessModeEnum.AccountRequired => null,
                IdentityAccessModeEnum.CapabilityTokenAllowed => GuestRecoveryPolicyEnum.CapabilityLinkOnly,
                _ => GuestRecoveryPolicyEnum.EmailOptional
            },
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc));
        return target;
    }
}
