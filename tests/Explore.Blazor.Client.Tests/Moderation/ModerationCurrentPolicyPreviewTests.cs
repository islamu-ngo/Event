using Explore.Blazor.Client.Components.Moderation;

namespace Explore.Blazor.Client.Tests.Moderation;

public sealed class ModerationCurrentPolicyPreviewTests
{
    [Test]
    public async Task EmptyInstalledSlotDoesNotInventRetainedDefinitionsOrActions()
    {
        using var context = new BlazorTestContext();
        var component = context.Render<ModerationCurrentPolicyPreview>(parameters => parameters.Add(view => view.Policy, Policy()));

        await Assert.That(component.FindAll("[data-policy-profile], [data-policy-class], [data-policy-reason]")).IsEmpty();
        await Assert.That(component.Find("[data-policy-state]").GetAttribute("data-policy-state")).IsEqualTo("uninitialized");
        await Assert.That(component.FindAll("button, input, textarea, form, a")).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AllDefinitionsIncludingUnselectedProfilesRetainEveryPlanInBothModes(bool advanced)
    {
        using var context = new BlazorTestContext();
        var policy = Policy();
        policy.Authoring = Authoring();
        policy.State = "adopted";
        var component = context.Render<ModerationCurrentPolicyPreview>(parameters => parameters
            .Add(view => view.Policy, policy).Add(view => view.ShowAdvanced, advanced));

        await Assert.That(component.FindAll("[data-policy-class]").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("[data-policy-reason]").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("[data-policy-profile]").Count).IsEqualTo(2);
        await Assert.That(component.FindAll("[data-selected='False']").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("[data-plan-index]").Count).IsEqualTo(3);
        await Assert.That(component.FindAll("[data-dimension]").Count).IsEqualTo(39);
        await Assert.That(component.FindAll("button, input, textarea, form, a")).IsEmpty();
    }

    [Test]
    public async Task DisplayModePreservesNativeAndHistoricalCoordinatesWithoutMutatingSource()
    {
        using var context = new BlazorTestContext();
        var policy = Policy();
        policy.Authoring = Authoring();
        policy.State = "adopted";
        var original = System.Text.Json.JsonSerializer.Serialize(policy);
        var component = context.Render<ModerationCurrentPolicyPreview>(parameters => parameters.Add(view => view.Policy, policy));
        var plans = component.FindAll("[data-dimension]").Select(node => node.GetAttribute("data-mode")).ToArray();

        component.Render(parameters => parameters.Add(view => view.Policy, policy).Add(view => view.ShowAdvanced, true));

        await Assert.That(component.Find("[data-current-revision]").GetAttribute("data-current-revision")).IsEqualTo("7");
        await Assert.That(component.Find("[data-historical-revision]").GetAttribute("data-historical-revision")).IsEqualTo("6");
        await Assert.That(component.Find("[data-installed-inventory]").GetAttribute("data-installed-inventory")).IsEqualTo("9");
        await Assert.That(component.Find("[data-historical-inventory]").GetAttribute("data-historical-inventory")).IsEqualTo("3");
        await Assert.That(component.FindAll("[data-dimension]").Select(node => node.GetAttribute("data-mode"))).IsEquivalentTo(plans);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(policy)).IsEqualTo(original);
    }

    private static HalResourceOfModerationCurrentPolicyView Policy() => new()
    {
        PolicyId = Guid.CreateVersion7(), Scope = "tenant", State = "uninitialized",
        ExpectedRevision = 7, InstalledInventoryRevision = 9, Authoring = null
    };

    private static ModerationCurrentPolicyAuthoringState Authoring()
    {
        var owner = Guid.CreateVersion7();
        ModerationRuleReference Rule(string code) => new() { OwnerId = owner, Code = code, Revision = 2 };
        var classRule = Rule("integrity.class");
        var reason = Rule("integrity.reason");
        var selected = Rule("selected.profile");
        var unselected = Rule("unselected.profile");
        return new()
        {
            HistoricalExpectedRevision = 6, InventoryRevision = 3,
            Classes = [new() { Reference = classRule, Meaning = "Retained class meaning." }],
            Reasons = [new() { Reference = reason, Class = classRule, Meaning = "Retained reason meaning." }],
            SelectedProfiles = [selected],
            Profiles =
            [
                new()
                {
                    Definition = new() { Id = Guid.CreateVersion7(), Rule = selected, Reason = reason },
                    Consequences = [Plan(), Plan() with { Content = ModerationContentMode.PendingPublicationReview }]
                },
                new()
                {
                    Definition = new() { Id = Guid.CreateVersion7(), Rule = unselected, Reason = reason },
                    Consequences = [Plan()]
                }
            ]
        };
    }

    private static ModerationConsequenceModes Plan() => new()
    {
        Content = ModerationContentMode.PreserveSafePublished, Management = ModerationManagementMode.CurrentAuthority,
        Resources = ModerationResourceReadMode.CurrentAudience, NewAdmission = ModerationNewAdmissionMode.CurrentEligibility,
        ExistingAdmission = ModerationExistingAdmissionMode.CurrentEligibility, PrivateDisclosure = ModerationPrivateDisclosureMode.CurrentAudience,
        Settlement = ModerationSettlementMode.NoBookingChange, Capacity = ModerationCapacityMode.NoSeatChange,
        Credentials = ModerationCredentialMode.CurrentCredentialLifecycle, Notifications = ModerationNotificationMode.DurableSafeSubjectAndAdmin,
        Recognition = ModerationRecognitionMode.NoCaptureFromThisControl, PendingWork = ModerationPendingWorkMode.RecheckCurrentEffectAtUseAndDispatch,
        Restoration = ModerationRestorationMode.RecomputeCurrentRightsOnly
    };
}
