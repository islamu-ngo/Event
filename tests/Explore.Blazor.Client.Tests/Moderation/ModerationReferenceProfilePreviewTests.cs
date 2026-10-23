using System.Text.Json;
using Explore.Blazor.Client.Components.Moderation;

namespace Explore.Blazor.Client.Tests.Moderation;

public sealed class ModerationReferenceProfilePreviewTests
{
    private static readonly string[] Dimensions =
    [
        "content", "management", "resources", "newAdmission", "existingAdmission", "privateDisclosure",
        "settlement", "capacity", "credentials", "notifications", "recognition", "pendingWork", "restoration"
    ];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothPresentationsRetainEveryRecipeDimensionWithoutMutationActions(bool advanced)
    {
        using var context = new BlazorTestContext();
        var component = context.Render<ModerationReferenceProfilePreview>(parameters => parameters
            .Add(view => view.Profile, Profile()).Add(view => view.ShowAdvanced, advanced));

        await Assert.That(component.FindAll("[data-dimension]").Select(node => node.GetAttribute("data-dimension")!))
            .IsEquivalentTo(Dimensions);
        await Assert.That(component.FindAll("button, input, textarea, form")).IsEmpty();
        await Assert.That(component.Find("[data-dimension='recognition']").GetAttribute("data-mode"))
            .IsEqualTo(nameof(ModerationRecognitionMode.NoCaptureFromThisControl));
    }

    [Test]
    public async Task PresentationChangeDoesNotChangeTheReferencedPolicyMeaning()
    {
        using var context = new BlazorTestContext();
        var profile = Profile();
        string original = JsonSerializer.Serialize(profile);
        var component = context.Render<ModerationReferenceProfilePreview>(parameters => parameters
            .Add(view => view.Profile, profile).Add(view => view.ShowAdvanced, false));
        var modes = component.FindAll("[data-dimension]").Select(node => node.GetAttribute("data-mode")).ToArray();

        component.Render(parameters => parameters.Add(view => view.Profile, profile).Add(view => view.ShowAdvanced, true));

        await Assert.That(component.FindAll("[data-dimension]").Select(node => node.GetAttribute("data-mode")))
            .IsEquivalentTo(modes);
        await Assert.That(JsonSerializer.Serialize(profile)).IsEqualTo(original);
    }

    [Test]
    public async Task BaseAndAdditivePlansRemainSeparateInsteadOfFlatteningTheirEffects()
    {
        using var context = new BlazorTestContext();
        var profile = Profile();
        var basePlan = profile.ConsequencePlans!.Single();
        profile = profile with
        {
            ConsequencePlans =
            [
                basePlan,
                basePlan with { Content = ModerationContentMode.PendingPublicationReview }
            ]
        };
        var component = context.Render<ModerationReferenceProfilePreview>(parameters => parameters.Add(view => view.Profile, profile));

        await Assert.That(component.FindAll("[data-plan-index]").Count).IsEqualTo(2);
        await Assert.That(component.Find("[data-plan-index='0']").QuerySelectorAll("[data-dimension]").Length).IsEqualTo(13);
        await Assert.That(component.Find("[data-plan-index='1']").QuerySelectorAll("[data-dimension]").Length).IsEqualTo(13);
        await Assert.That(component.Find("[data-plan-index='1'] [data-dimension='content']").GetAttribute("data-mode"))
            .IsEqualTo(nameof(ModerationContentMode.PendingPublicationReview));
    }

    private static ModerationReferenceProfile Profile() => new()
    {
        Id = ModerationReferenceProfileId.CFG09, Code = "CFG09", Revision = 1,
        Basis = ModerationReferenceBasis.VerifiedUnsafeObject, Ground = ModerationReferenceGround.VerifiedUnsafeResource,
        Bundle = ModerationConsequenceBundleKind.ObjectQuarantine, Recognition = ModerationReferenceRecognition.Never,
        ScopeKinds = [ModerationScopeKind.Resource], SubjectKinds = [ModerationSubjectKind.Content],
        Prerequisites = [ModerationReferencePrerequisite.ExactResourceAudienceAndVersion],
        Effects =
        [
            new() { Kind = ModerationReferenceEffectKind.UnsafeResourceVersion, Mode = ModerationReferenceEffectMode.Quarantine,
                Target = ModerationReferenceTarget.SelectedResourceVersion, Execution = ModerationReferenceExecution.NativeOwner }
        ],
        ConsequencePlans =
        [
            new()
            {
                Content = ModerationContentMode.QuarantineNamedVersion, Management = ModerationManagementMode.CurrentAuthority,
                Resources = ModerationResourceReadMode.QuarantineNamedVersionSafeStatusOnly,
                NewAdmission = ModerationNewAdmissionMode.CurrentEligibility, ExistingAdmission = ModerationExistingAdmissionMode.CurrentEligibility,
                PrivateDisclosure = ModerationPrivateDisclosureMode.MaskQuarantinedEmbeddedDetails, Settlement = ModerationSettlementMode.NoBookingChange,
                Capacity = ModerationCapacityMode.NoSeatChange, Credentials = ModerationCredentialMode.CurrentCredentialLifecycle,
                Notifications = ModerationNotificationMode.DurableSafeSubjectAndAdmin, Recognition = ModerationRecognitionMode.NoCaptureFromThisControl,
                PendingWork = ModerationPendingWorkMode.OwnedObjectReviewNeverAutomaticRelease, Restoration = ModerationRestorationMode.AuthorizedObjectDecision
            }
        ]
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothPresentationsRetainEverySafeguardIncludingNonRestorationAndComposition(bool advanced)
    {
        using var context = new BlazorTestContext();
        var profile = Profile();
        var basePlan = profile.ConsequencePlans!.Single();
        profile = profile with
        {
            ConsequencePlans =
            [
                basePlan with
                {
                    Safeguards = new()
                    {
                        OrdinaryPermissionRequired = true, PreserveIndependentPeopleRolesAndOrigin = true,
                        RecheckProhibitedEffectsAtUseAndDispatch = true, PreserveProtectedRemedies = true,
                        PreserveQualifiedSettlementRoutes = true, RecomputeRemainingContributions = true,
                        AutomaticHistoricalRestoration = false, CanRecallDisclosedCopies = false,
                        DeduplicateByOwningTargetTransition = true, IrreversibleCauseConflictRequiresOwningResolution = true,
                        PreserveEachContributionScopeAndInterval = true, PreserveBothProtectivePurposes = true,
                        ContinuityOnlyForActuallyDeniedEssentialOperations = true, RescheduleRequiresNewAuthorityToExtend = true,
                        Operations = ModerationOperationComposition.SameTargetDenialThenReviewThenCurrentPermission,
                        Resources = ModerationResourceComposition.NamedVersionQuarantineOrReadDenialMasksRelease,
                        Participants = ModerationParticipantComposition.SameParticipantWindowRevocationThenHoldThenCurrentEligibility
                    }
                }
            ]
        };
        var component = context.Render<ModerationReferenceProfilePreview>(parameters => parameters
            .Add(view => view.Profile, profile).Add(view => view.ShowAdvanced, advanced));

        await Assert.That(component.FindAll("[data-safeguard]").Count).IsEqualTo(17);
        await Assert.That(component.Find("[data-safeguard='automaticHistoricalRestoration']").GetAttribute("data-value"))
            .IsEqualTo(bool.FalseString);
        await Assert.That(component.Find("[data-safeguard='canRecallDisclosedCopies']").GetAttribute("data-value"))
            .IsEqualTo(bool.FalseString);
        await Assert.That(component.Find("[data-safeguard='participants']").GetAttribute("data-value"))
            .IsEqualTo(nameof(ModerationParticipantComposition.SameParticipantWindowRevocationThenHoldThenCurrentEligibility));
    }
}
