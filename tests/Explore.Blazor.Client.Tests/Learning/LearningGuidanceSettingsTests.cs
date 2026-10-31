using AngleSharp.Html.Dom;
using Blazouter.Extensions;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Learning;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Routing;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Learning;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class LearningGuidanceSettingsTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly BlazorTestContext _ctx = new();
    private readonly ILearningClient _transport = Substitute.For<ILearningClient>();
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Guid _profileActorId = Guid.CreateVersion7();
    private readonly TaskCompletionSource _writeObserved = Signal();
    private ILearningClientService? _service;

    public LearningGuidanceSettingsTests()
    {
        _ctx.Services.AddBlazouter();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddScoped<ILearningClientService, LearningClientService>();
        _ctx.Services.AddSingleton(_transport);
        _ctx.SetAuthenticatedUser(_userId, "Tenant administrator");
        _transport.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Context()));
        _transport.UpdateLearningGuidancePolicyAsync(
                Arg.Any<UpdateLearningGuidancePolicyRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _writeObserved.TrySetResult();
                return Task.FromResult(Policy());
            });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments("")]
    [Arguments("/acme")]
    public async Task ActualRoutes_DedicatedLearningSettingsUsesTenantAdministrationAuthority(string pathBase)
    {
        var routes = Explore.Blazor.Client.Routes.CreateRouteCatalog();
        RouteConfigurationPathBase.Apply(routes, $"https://event.test{pathBase}/");
        var expectedPath = pathBase + "/settings/admin/learning";
        var matches = routes.Where(route => route.Path == expectedPath).ToList();

        // Fail as an assertion on the shipped catalogue, not a missing proposed component type.
        await Assert.That(matches.Count).IsEqualTo(1);
        var learning = matches.Single();
        await Assert.That(learning.Guards).Contains(typeof(TenantAdminRouteGuard));
        await Assert.That(learning.Component).IsNotNull();
        var tenantSettings = routes.Single(route => route.Path == pathBase + "/settings/admin");
        await Assert.That(learning.Component == tenantSettings.Component).IsFalse();
    }

    [Test]
    [Arguments("")]
    [Arguments("/acme")]
    public async Task MountedSettings_ClosedFieldsAndFourFamilyOverridesUseObservedGuidStamp(string pathBase)
    {
        var source = Policy();
        var cut = await MountSettings(source, pathBase);
        await EditSettings(cut);
        var requested = Signal<UpdateLearningGuidancePolicyRequest>();
        var response = Signal<HalResourceOfLearningGuidancePolicyDto>();
        var refreshing = Signal();
        var fresh = Signal<HalResourceOfLearningGuidancePolicyDto>();
        _transport.UpdateLearningGuidancePolicyAsync(
                Arg.Any<UpdateLearningGuidancePolicyRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult(call.ArgAt<UpdateLearningGuidancePolicyRequest>(0));
                return response.Task;
            });
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                refreshing.TrySetResult();
                return fresh.Task;
            });
        var service = Service();
        using var saved = Watch(cut, () => service.LastMutationApplied == true
            && cut.FindAll("button[type='submit']:not([disabled])").Count == 1);

        var submission = cut.Find("form").SubmitAsync();
        var request = await requested.Task.WaitAsync(Timeout);
        await Assert.That(request.ExpectedConcurrencyStamp).IsEqualTo(source.ConcurrencyStamp);
        await AssertDraft(request.Policy, enabled: true);
        await Assert.That(source.Policy.FamilyOverrides.Count).IsEqualTo(0);
        var settled = Policy();
        settled.Policy = Draft();
        response.SetResult(settled);
        await refreshing.Task.WaitAsync(Timeout);
        await Assert.That(service.LastMutationApplied == true).IsFalse();
        await Assert.That(submission.IsCompleted).IsFalse();

        fresh.SetResult(settled);
        await submission.WaitAsync(Timeout);
        await saved.Completion;
        await Assert.That(service.GuidancePolicy?.ConcurrencyStamp).IsEqualTo(settled.ConcurrencyStamp);
        await Assert.That(cut.FindAll($"a[href='{pathBase}/settings/admin']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("p[role='status']").Count).IsEqualTo(1);
        await AssertSettingsDraft(cut);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MountedSettings_MissingOrLockedEditHalDisablesFieldsAndCannotWrite(bool selfLinkOnly)
    {
        var source = Policy(editable: false);
        if (selfLinkOnly)
            source._links!["self"] = new() { Href = "/api/learning/guidance", Method = "GET" };
        var cut = await MountSettings(source);
        await Assert.That(cut.FindAll("[data-learning-state='policy-read-only']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("button[type='submit']").Count).IsEqualTo(0);
        await Assert.That(cut.Find("form > fieldset").HasAttribute("disabled")).IsTrue();

        // Dispatching a form submit directly must not bypass the absent server grant.
        await cut.Find("form").SubmitAsync();
        await Assert.That(_writeObserved.Task.IsCompleted).IsFalse();
        await Assert.That(Service().LastMutationApplied == true).IsFalse();
    }

    [Test]
    public async Task MountedSettings_HiddenOptionalGuidanceKeepsPendingNoticeUntilExplicitSourceBoundAcknowledgement()
    {
        var context = PanelContext();
        context.Preferences = new() { SuppressAutomaticTours = true, HideLearning = true, PinLearning = false };
        var policy = Policy();
        policy.Policy.AutomaticInvitations = false;
        policy.Policy.RequirementPolicy = LearningRequirementPolicy.TenantAdminOrientationAcknowledgement;
        var requests = new List<(string Key, LearningAcknowledgeRequirementRequest Body)>();
        var requested = Signal<(string Key, LearningAcknowledgeRequirementRequest Body)>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = (call.ArgAt<string>(0),
                    call.ArgAt<LearningAcknowledgeRequirementRequest>(1));
                requests.Add(request);
                requested.TrySetResult(request);
                return response.Task;
            });
        var cut = await MountSettings(policy, context: context);
        var service = Service();

        await Assert.That(requests.Count).IsEqualTo(0);
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(cut.FindAll("[data-learning-action='acknowledge-requirement']").Count).IsEqualTo(1);
        using var acknowledged = Watch(cut, () => service.LastMutationApplied == true
            && cut.FindAll("[data-learning-action='acknowledge-requirement']").Count == 0);
        var click = cut.Find("[data-learning-action='acknowledge-requirement']").ClickAsync(new());
        var submitted = await requested.Task.WaitAsync(Timeout);
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(submitted.Key).IsEqualTo("tenant-admin-orientation");
        await Assert.That(submitted.Body.MaterialVersion).IsEqualTo(1);
        await Assert.That(submitted.Body.ExpectedScopeStamp).IsEqualTo(context.ScopeStamp);
        await Assert.That(submitted.Body.ExpectedPolicySourceStamp).IsEqualTo(context.Requirement.PolicySourceStamp);
        await Assert.That(submitted.Body.ExpectedRevision).IsEqualTo(context.LearnerRevision);
        await Assert.That(submitted.Body.IntentId == Guid.Empty).IsFalse();
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(service.LastMutationApplied == true).IsFalse();

        var settled = Acknowledged(context);
        RespondWith(settled);
        response.SetResult(Receipt(settled));
        await click.WaitAsync(Timeout);
        await acknowledged.Completion;
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Satisfied);
        await Assert.That(service.Context?.Preferences.SuppressAutomaticTours).IsEqualTo(true);
        await Assert.That(service.Context?.Preferences.HideLearning).IsEqualTo(true);
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(requests.Single().Body.IntentId).IsEqualTo(submitted.Body.IntentId);
        await Assert.That(_writeObserved.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task MountedSettings_OrdinaryMemberReloadRemovesFormerAcknowledgementWithoutInvokingIt()
    {
        var context = PanelContext();
        var requested = Signal();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return Task.FromResult(Receipt(Acknowledged(context)));
            });
        var cut = await MountSettings(Policy(), context: context);
        await Assert.That(cut.FindAll("[data-learning-action='acknowledge-requirement']").Count).IsEqualTo(1);
        await Assert.That(requested.Task.IsCompleted).IsFalse();

        var member = PanelContext();
        member.Requirement = member.Requirement with { Status = LearningRequirementStatus.NotApplicable };
        member._links!.Remove("acknowledge-requirement");
        RespondWith(member);
        var service = Service();
        using var reloaded = Watch(cut, () => !service.IsBusy
            && ReferenceEquals(service.Context, member) && cut.FindAll("form").Count == 1);
        await cut.FindAll("button").Single(button => button.Closest("form") is null
            && button.GetAttribute("data-learning-action") != "acknowledge-requirement").ClickAsync(new());
        await reloaded.Completion;

        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.NotApplicable);
        await Assert.That(service.HasContextAction("acknowledge-requirement")).IsFalse();
        await Assert.That(cut.FindAll("[data-learning-action='acknowledge-requirement']").Count).IsEqualTo(0);
        await Assert.That(await service.AcknowledgeRequirementAsync(context.Requirement)).IsFalse();
        await Assert.That(requested.Task.IsCompleted).IsFalse();
        await Assert.That(service.LastMutationApplied == true).IsFalse();
    }

    [Test]
    public async Task MountedSettings_DisabledLearningStillAllowsAdministratorToRestoreGuidance()
    {
        var source = Policy();
        source.Policy.Enabled = false;
        var context = Context();
        context.Capabilities = new() { CanLearn = false };
        var cut = await MountSettings(source, context: context);
        await Assert.That(cut.Find("form > fieldset").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.Find("button[type='submit']").HasAttribute("disabled")).IsFalse();
        await Assert.That(((IHtmlInputElement)cut.Find("form > fieldset > label input")).IsChecked).IsFalse();
        await cut.Find("form > fieldset > label input").ChangeAsync(new() { Value = true });
        var requested = Signal<UpdateLearningGuidancePolicyRequest>();
        _transport.UpdateLearningGuidancePolicyAsync(
                Arg.Any<UpdateLearningGuidancePolicyRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult(call.ArgAt<UpdateLearningGuidancePolicyRequest>(0));
                return Task.FromResult<HalResourceOfLearningGuidancePolicyDto>(null!);
            });

        await cut.Find("form").SubmitAsync();
        await Assert.That((await requested.Task.WaitAsync(Timeout)).Policy.Enabled).IsTrue();
        await Assert.That(Service().LastMutationApplied == true).IsFalse();
    }

    [Test]
    [Arguments("conflict")]
    [Arguments("missing-response")]
    [Arguments("unchanged-stamp")]
    public async Task MountedSettings_UnconfirmedSaveRetainsDraftUntilExplicitReloadWithoutReplay(string outcome)
    {
        var source = Policy();
        var cut = await MountSettings(source);
        await EditSettings(cut);
        var writes = new List<UpdateLearningGuidancePolicyRequest>();
        _transport.UpdateLearningGuidancePolicyAsync(
                Arg.Any<UpdateLearningGuidancePolicyRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                writes.Add(call.ArgAt<UpdateLearningGuidancePolicyRequest>(0));
                return outcome == "conflict"
                    ? Task.FromException<HalResourceOfLearningGuidancePolicyDto>(
                        new ApiException("Conflict", 409, "", new Dictionary<string, IEnumerable<string>>(), null))
                    : Task.FromResult(outcome == "missing-response" ? null! : source);
            });
        var service = Service();
        using var rejected = Watch(cut, () => !service.IsBusy && cut.FindAll("[role='alert']").Count > 0);
        await cut.Find("form").SubmitAsync();
        await rejected.Completion;
        await AssertSettingsDraft(cut);
        await Assert.That(service.LastMutationApplied == true).IsFalse();
        await Assert.That(cut.FindAll("p[role='status']").Count).IsEqualTo(0);
        await Assert.That(cut.Find("button[type='submit']").HasAttribute("disabled")).IsTrue();
        await cut.Find("form").SubmitAsync();
        await Assert.That(writes.Count).IsEqualTo(1);

        var replacement = Policy();
        var reloadRequested = Signal();
        var response = Signal<HalResourceOfLearningGuidancePolicyDto>();
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reloadRequested.TrySetResult();
                return response.Task;
            });
        using var reloaded = Watch(cut, () => !service.IsBusy
            && service.GuidancePolicy?.ConcurrencyStamp == replacement.ConcurrencyStamp
            && cut.FindAll("form").Count == 1);
        // The only button outside the form is the shipped explicit discard-and-reload action.
        var reload = cut.FindAll("button").Single(button => button.Closest("form") is null).ClickAsync(new());
        await reloadRequested.Task.WaitAsync(Timeout);
        response.SetResult(replacement);
        await reload.WaitAsync(Timeout);
        await reloaded.Completion;
        await Assert.That(((IHtmlSelectElement)cut.Find("#learning-requirement-policy")).Value)
            .IsEqualTo(nameof(LearningRequirementPolicy.Optional));
        await Assert.That(((IHtmlSelectElement)cut.Find("#learning-invitation-audience")).Value)
            .IsEqualTo(nameof(LearningGuidanceAudience.ActiveMembers));
        await Assert.That(writes.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("")]
    [Arguments("/acme")]
    public async Task MountedPrivatePanel_ActualHubDestinationRespectsConfiguredApplicationBase(string pathBase)
    {
        var context = PanelContext();
        var cut = await MountPanel(context, pathBase);
        var href = cut.Find("[data-testid='private-learning-panel'] a").GetAttribute("href");
        await Assert.That(href).IsEqualTo(pathBase + "/learning");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        await Assert.That(navigation.ToAbsoluteUri(href!).AbsolutePath).IsEqualTo(pathBase + "/learning");
        await Assert.That(cut.FindAll("[data-learning-action='acknowledge-requirement']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task MountedPrivatePanel_SuppressionAndHiddenOptionalLearningDoNotAcknowledgePendingNotice()
    {
        var context = PanelContext();
        context.Preferences = new() { SuppressAutomaticTours = true, HideLearning = true, PinLearning = false };
        var requested = Signal<(string Key, LearningAcknowledgeRequirementRequest Body)>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult((call.ArgAt<string>(0),
                    call.ArgAt<LearningAcknowledgeRequirementRequest>(1)));
                return response.Task;
            });
        var cut = await MountPanel(context);
        await Assert.That(requested.Task.IsCompleted).IsFalse();
        await Assert.That(cut.FindAll("[data-testid='private-learning-panel'] a").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-learning-action='acknowledge-requirement']").Count).IsEqualTo(1);
        var service = Service();
        using var acknowledged = Watch(cut, () => service.LastMutationApplied == true
            && cut.FindAll("[data-learning-action='acknowledge-requirement']").Count == 0);
        var click = cut.Find("[data-learning-action='acknowledge-requirement']").ClickAsync(new());
        var request = await requested.Task.WaitAsync(Timeout);
        await Assert.That(request.Key).IsEqualTo("tenant-admin-orientation");
        await Assert.That(request.Body.MaterialVersion).IsEqualTo(1);
        await Assert.That(request.Body.ExpectedScopeStamp).IsEqualTo(context.ScopeStamp);
        await Assert.That(request.Body.ExpectedPolicySourceStamp).IsEqualTo(context.Requirement.PolicySourceStamp);
        await Assert.That(request.Body.ExpectedRevision).IsEqualTo(context.LearnerRevision);
        await Assert.That(request.Body.IntentId == Guid.Empty).IsFalse();
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(service.LastMutationApplied == true).IsFalse();
        var settled = Acknowledged(context);
        RespondWith(settled);
        response.SetResult(Receipt(settled));
        await click.WaitAsync(Timeout);
        await acknowledged.Completion;
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Satisfied);
        await Assert.That(service.Context?.Preferences.SuppressAutomaticTours).IsEqualTo(true);
        await Assert.That(service.Context?.Preferences.HideLearning).IsEqualTo(true);
    }

    [Test]
    [Arguments("member")]
    [Arguments("missing-hal")]
    [Arguments("unknown-material")]
    public async Task MountedPrivatePanel_OrdinaryMemberOrUnsupportedNoticeHasNoAcknowledgementAction(string reason)
    {
        var context = PanelContext();
        if (reason == "member")
            context.Requirement = context.Requirement with { Status = LearningRequirementStatus.NotApplicable };
        if (reason is "member" or "missing-hal")
            context._links!.Remove("acknowledge-requirement");
        if (reason == "unknown-material")
            context.Requirement = context.Requirement with { MaterialVersion = 2 };
        var requested = Signal();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return Task.FromResult(Receipt(Acknowledged(context)));
            });
        var cut = await MountPanel(context);

        await Assert.That(cut.FindAll("[data-learning-action='acknowledge-requirement']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-learning-action='preferences']").Count).IsEqualTo(3);
        await Assert.That(requested.Task.IsCompleted).IsFalse();
        await Assert.That(Service().LastMutationApplied == true).IsFalse();
    }

    [Test]
    [Arguments("suppress-automatic-tours", true, false, false)]
    [Arguments("hide-learning", false, true, false)]
    [Arguments("pin-learning", false, false, true)]
    public async Task MountedPrivatePanel_PreferenceActionsSubmitOnlyTheCurrentPrivatePreferenceIntent(
        string preference, bool suppress, bool hide, bool pin)
    {
        var context = PanelContext();
        var cut = await MountPanel(context);
        var requested = Signal<LearningUpdatePreferencesRequest>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        _transport.UpdateLearningPreferencesAsync(
                Arg.Any<LearningUpdatePreferencesRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult(call.ArgAt<LearningUpdatePreferencesRequest>(0));
                return response.Task;
            });
        var service = Service();
        using var applied = Watch(cut, () => service.LastMutationApplied == true
            && cut.FindAll("[data-learning-action='preferences']:not([disabled])").Count == 3);
        var click = cut.Find($"[data-learning-preference='{preference}']").ClickAsync(new());
        var request = await requested.Task.WaitAsync(Timeout);
        await Assert.That(request.ExpectedScopeStamp).IsEqualTo(context.ScopeStamp);
        await Assert.That(request.ExpectedRevision).IsEqualTo(context.LearnerRevision);
        await Assert.That(request.SuppressAutomaticTours).IsEqualTo(suppress);
        await Assert.That(request.HideLearning).IsEqualTo(hide);
        await Assert.That(request.PinLearning).IsEqualTo(pin);
        var settled = PanelContext();
        settled.LearnerRevision = 8;
        settled.Preferences = new() { SuppressAutomaticTours = suppress, HideLearning = hide, PinLearning = pin };
        RespondWith(settled);
        response.SetResult(Receipt(settled));
        await click.WaitAsync(Timeout);
        await applied.Completion;
        await Assert.That(service.Context?.Preferences.SuppressAutomaticTours).IsEqualTo(suppress);
        await Assert.That(service.Context?.Preferences.HideLearning).IsEqualTo(hide);
        await Assert.That(service.Context?.Preferences.PinLearning).IsEqualTo(pin);
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
    }

    [Test]
    [Arguments("missing-receipt")]
    [Arguments("pending-receipt")]
    [Arguments("pending-refresh")]
    public async Task MountedPrivatePanel_UnconfirmedAcknowledgementKeepsNoticeWithoutSuccessOrReplay(string outcome)
    {
        var context = PanelContext();
        var cut = await MountPanel(context);
        var submitted = new List<LearningAcknowledgeRequirementRequest>();
        var settled = Acknowledged(context);
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submitted.Add(call.ArgAt<LearningAcknowledgeRequirementRequest>(1));
                return Task.FromResult(outcome == "missing-receipt" ? null!
                    : Receipt(outcome == "pending-receipt" ? context : settled));
            });
        var service = Service();
        using var rejected = Watch(cut, () => !service.IsBusy && cut.FindAll("[role='alert']").Count > 0);
        await cut.Find("[data-learning-action='acknowledge-requirement']").ClickAsync(new());
        await rejected.Completion;
        await Assert.That(service.LastMutationApplied == true).IsFalse();
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(cut.FindAll("p[role='status']").Count).IsEqualTo(0);
        await Assert.That(cut.Find("[data-learning-action='acknowledge-requirement']").HasAttribute("disabled")).IsTrue();
        await cut.Find("[data-learning-action='acknowledge-requirement']").ClickAsync(new());
        await Assert.That(submitted.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("account")]
    [Arguments("actor")]
    [Arguments("dispose")]
    public async Task MountedPrivatePanel_LateExplicitAcknowledgementCannotRestoreFormerOwnerActions(string ownerChange)
    {
        var context = PanelContext();
        var cut = await MountPanel(context);
        var service = Service();
        var entered = Signal<CancellationToken>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        var submitted = new List<LearningAcknowledgeRequirementRequest>();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submitted.Add(call.ArgAt<LearningAcknowledgeRequirementRequest>(1));
                entered.TrySetResult(call.ArgAt<CancellationToken>(4));
                return response.Task;
            });
        var click = cut.Find("[data-learning-action='acknowledge-requirement']").ClickAsync(new());
        var token = await entered.Task.WaitAsync(Timeout);
        var cancelled = Signal();
        using var registration = token.Register(() => cancelled.TrySetResult());
        using var cleared = Watch(cut, () => cut.FindAll(
            "[data-learning-action='acknowledge-requirement'],[data-learning-action='preferences']").Count == 0);
        if (ownerChange == "dispose")
            await _ctx.DisposeComponentsAsync();
        else
        {
            await cut.InvokeAsync(() => ChangeOwner(service, ownerChange));
            await cleared.Completion;
        }
        await cancelled.Task.WaitAsync(Timeout);
        response.SetResult(Receipt(Acknowledged(context)));
        await click.WaitAsync(Timeout);

        await Assert.That(service.Context is null).IsTrue();
        await Assert.That(service.LastMutationApplied is null).IsTrue();
        await Assert.That(submitted.Count).IsEqualTo(1);
        await Assert.That(submitted.Single().ExpectedScopeStamp).IsEqualTo(context.ScopeStamp);
        if (ownerChange != "dispose")
            await Assert.That(cut.FindAll(
                "[data-learning-action='acknowledge-requirement'],[data-learning-action='preferences']").Count)
                .IsEqualTo(0);
    }

    [Test]
    public async Task MountedSettings_DisabledLearningCannotSubmitRequiredOrientation()
    {
        var cut = await MountSettings(Policy());
        await EditSettings(cut);
        await cut.FindAll("form > fieldset > label input[type='checkbox']")[0]
            .ChangeAsync(new() { Value = false });
        using var rejected = Watch(cut, () => cut.FindAll("#learning-settings-error[role='alert']").Count == 1);
        await cut.Find("form").SubmitAsync();
        await rejected.Completion;
        await Assert.That(_writeObserved.Task.IsCompleted).IsFalse();
        await Assert.That(Service().LastMutationApplied == true).IsFalse();
    }

    [Test]
    [Arguments("tenant", "read")]
    [Arguments("account", "read")]
    [Arguments("actor", "read")]
    [Arguments("dispose", "read")]
    [Arguments("tenant", "write")]
    [Arguments("account", "write")]
    [Arguments("actor", "write")]
    [Arguments("dispose", "write")]
    [Arguments("tenant", "refresh")]
    [Arguments("account", "refresh")]
    [Arguments("actor", "refresh")]
    [Arguments("dispose", "refresh")]
    public async Task MountedSettings_LatePolicyReadOrSaveCannotRestoreAnInvalidatedOwner(
        string ownerChange, string stage)
    {
        var original = Policy();
        var cut = await MountSettings(original);
        var service = Service();
        await Assert.That(service.GuidancePolicy is not null).IsTrue();

        var entered = Signal<CancellationToken>();
        var response = Signal<HalResourceOfLearningGuidancePolicyDto>();
        var writes = new List<UpdateLearningGuidancePolicyRequest>();
        var settled = Policy();
        settled.Policy = Draft();
        _transport.UpdateLearningGuidancePolicyAsync(
                Arg.Any<UpdateLearningGuidancePolicyRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                writes.Add(call.ArgAt<UpdateLearningGuidancePolicyRequest>(0));
                if (stage == "write")
                    entered.TrySetResult(call.ArgAt<CancellationToken>(3));
                return stage == "write" ? response.Task : Task.FromResult(settled);
            });
        if (stage is "read" or "refresh")
            _transport.GetLearningGuidancePolicyAsync(
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    entered.TrySetResult(call.ArgAt<CancellationToken>(2));
                    return response.Task;
                });

        using var cleared = Watch(cut, () => cut.FindAll("form").Count == 0);
        Task<bool>? mutation = null;
        Task operation = stage == "read"
            ? service.LoadGuidancePolicyAsync()
            : mutation = service.UpdateGuidancePolicyAsync(original, Draft());
        var token = await entered.Task.WaitAsync(Timeout);
        var cancelled = Signal();
        using var registration = token.Register(() => cancelled.TrySetResult());
        if (ownerChange == "dispose")
        {
            // Hand the queued facade notification back to the renderer before disposing
            // its mounted owner, as in the mounted practice lifecycle tests.
            await cut.InvokeAsync(() => { });
            await _ctx.DisposeComponentsAsync();
        }
        else
        {
            await cut.InvokeAsync(() => ChangeOwner(service, ownerChange));
            await cleared.Completion;
        }
        await cancelled.Task.WaitAsync(Timeout);
        await Assert.That(token.IsCancellationRequested).IsTrue();

        // The controlled transport ignores cancellation and returns the former owner's policy.
        response.SetResult(settled);
        await operation.WaitAsync(Timeout);
        if (mutation is not null)
            await Assert.That(await mutation).IsFalse();
        await Assert.That(service.GuidancePolicy is null).IsTrue();
        await Assert.That(service.LastMutationApplied is null).IsTrue();
        await Assert.That(service.HasGuidancePolicyAction("edit")).IsFalse();
        await Assert.That(await service.UpdateGuidancePolicyAsync(original, Draft())).IsFalse();
        await Assert.That(writes.Count).IsEqualTo(stage == "read" ? 0 : 1);
        if (ownerChange != "dispose")
            await Assert.That(cut.FindAll("form,button[type='submit']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task RealFacade_FormerPolicyDocumentCannotAuthorizeReplacement()
    {
        var service = Service();
        var original = Policy();
        var current = Policy();
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(original), Task.FromResult(current));
        await service.LoadGuidancePolicyAsync();
        await service.LoadGuidancePolicyAsync();

        await Assert.That(await service.UpdateGuidancePolicyAsync(original, Draft())).IsFalse();
        await Assert.That(service.GuidancePolicy?.ConcurrencyStamp).IsEqualTo(current.ConcurrencyStamp);
        await Assert.That(_writeObserved.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task RealFacade_MissingEditHalCannotWriteEvenWithAnAuthenticatedAdministrator()
    {
        var service = Service();
        var locked = Policy(editable: false);
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(locked));
        await service.LoadGuidancePolicyAsync();

        await Assert.That(service.HasGuidancePolicyAction("edit")).IsFalse();
        await Assert.That(await service.UpdateGuidancePolicyAsync(locked, Draft())).IsFalse();
        await Assert.That(service.GuidancePolicy?.ConcurrencyStamp).IsEqualTo(locked.ConcurrencyStamp);
        await Assert.That(_writeObserved.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task RealFacade_PolicyWriteBindsCurrentGuidStampAndWaitsForFreshAuthority()
    {
        var service = Service();
        var source = Policy();
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(source));
        await service.LoadGuidancePolicyAsync();
        var requested = Signal<UpdateLearningGuidancePolicyRequest>();
        var response = Signal<HalResourceOfLearningGuidancePolicyDto>();
        var refreshRequested = Signal();
        var refresh = Signal<HalResourceOfLearningGuidancePolicyDto>();
        _transport.UpdateLearningGuidancePolicyAsync(
                Arg.Any<UpdateLearningGuidancePolicyRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult(call.ArgAt<UpdateLearningGuidancePolicyRequest>(0));
                return response.Task;
            });
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                refreshRequested.TrySetResult();
                return refresh.Task;
            });

        var draft = Draft();
        var saving = service.UpdateGuidancePolicyAsync(source, draft);
        var request = await requested.Task.WaitAsync(Timeout);
        draft.Enabled = false;
        draft.FamilyOverrides.First().Enabled = false;
        await Assert.That(request.ExpectedConcurrencyStamp).IsEqualTo(source.ConcurrencyStamp);
        await AssertDraft(request.Policy);
        await Assert.That(source.Policy.Enabled).IsTrue();
        var settled = Policy();
        settled.Policy = Draft();
        response.SetResult(settled);
        await refreshRequested.Task.WaitAsync(Timeout);
        await Assert.That(saving.IsCompleted).IsFalse();
        await Assert.That(service.LastMutationApplied == true).IsFalse();

        refresh.SetResult(settled);
        await Assert.That(await saving.WaitAsync(Timeout)).IsTrue();
        await Assert.That(service.GuidancePolicy?.ConcurrencyStamp).IsEqualTo(settled.ConcurrencyStamp);
        await Assert.That(service.LastMutationApplied).IsEqualTo(true);
    }

    [Test]
    [Arguments("missing-response")]
    [Arguments("unchanged-stamp")]
    [Arguments("conflict")]
    [Arguments("unconfirmed-refresh")]
    public async Task RealFacade_UnconfirmedPolicyWriteNeverReportsSuccessOrAutomaticallyReplays(string outcome)
    {
        var service = Service();
        var source = Policy();
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(source));
        await service.LoadGuidancePolicyAsync();
        var requests = new List<UpdateLearningGuidancePolicyRequest>();
        var response = Signal<HalResourceOfLearningGuidancePolicyDto>();
        _transport.UpdateLearningGuidancePolicyAsync(
                Arg.Any<UpdateLearningGuidancePolicyRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requests.Add(call.ArgAt<UpdateLearningGuidancePolicyRequest>(0));
                return response.Task;
            });
        // A successful write envelope is not confirmation of the subsequently re-read policy.
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(source));
        var draft = Draft();
        var saving = service.UpdateGuidancePolicyAsync(source, draft);
        if (outcome == "conflict")
            response.SetException(new ApiException("Conflict", 409, "",
                new Dictionary<string, IEnumerable<string>>(), null));
        else
        {
            var receipt = Policy();
            receipt.Policy = Draft();
            response.SetResult(outcome == "missing-response" ? null!
                : outcome == "unchanged-stamp" ? source : receipt);
        }

        await Assert.That(await saving.WaitAsync(Timeout)).IsFalse();
        await Assert.That(service.LastMutationApplied == true).IsFalse();
        await AssertDraft(draft);
        await Assert.That(await service.UpdateGuidancePolicyAsync(source, draft)).IsFalse();
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(requests.Single().ExpectedConcurrencyStamp).IsEqualTo(source.ConcurrencyStamp);
    }

    [Test]
    public async Task RealFacade_ExplicitSelfAcknowledgementBindsOriginalRequirementAndWaitsForFreshEvidence()
    {
        var service = Service();
        var source = Context();
        RespondWith(source);
        await service.LoadContextAsync();
        var requested = Signal<(string Key, LearningAcknowledgeRequirementRequest Body)>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        var freshRequested = Signal();
        var fresh = Signal<HalResourceOfLearningContextDto>();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult((call.ArgAt<string>(0),
                    call.ArgAt<LearningAcknowledgeRequirementRequest>(1)));
                return response.Task;
            });
        _transport.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                freshRequested.TrySetResult();
                return fresh.Task;
            });

        await Assert.That(requested.Task.IsCompleted).IsFalse();
        var acknowledging = service.AcknowledgeRequirementAsync(source.Requirement);
        var request = await requested.Task.WaitAsync(Timeout);
        await Assert.That(request.Key).IsEqualTo(source.Requirement.RequirementKey);
        await Assert.That(request.Body.MaterialVersion).IsEqualTo(4);
        await Assert.That(request.Body.ExpectedPolicySourceStamp).IsEqualTo("guidance-settings-policy-source");
        await Assert.That(request.Body.ExpectedScopeStamp).IsEqualTo("guidance-settings-self-scope");
        await Assert.That(request.Body.ExpectedRevision).IsEqualTo(7);
        await Assert.That(request.Body.IntentId == Guid.Empty).IsFalse();
        var settled = Acknowledged(source);
        response.SetResult(Receipt(settled));
        await freshRequested.Task.WaitAsync(Timeout);
        await Assert.That(acknowledging.IsCompleted).IsFalse();
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(service.LastMutationApplied == true).IsFalse();

        fresh.SetResult(settled);
        await Assert.That(await acknowledging.WaitAsync(Timeout)).IsTrue();
        await Assert.That(service.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Satisfied);
        await Assert.That(service.Context?.Requirement.AcknowledgedAtUtc is not null).IsTrue();
    }

    [Test]
    [Arguments("member")]
    [Arguments("missing-hal")]
    [Arguments("requirement")]
    [Arguments("material")]
    [Arguments("policy")]
    [Arguments("satisfied")]
    [Arguments("actor")]
    public async Task RealFacade_NonCurrentOrUnadvertisedRequirementCannotBeAcknowledged(string mismatch)
    {
        var service = Service();
        var source = Context();
        var offered = source.Requirement;
        if (mismatch is "member" or "missing-hal")
            source._links!.Remove("acknowledge-requirement");
        if (mismatch == "member")
            source.Requirement = offered with { Status = LearningRequirementStatus.NotApplicable };
        if (mismatch == "satisfied")
            source.Requirement = offered with { Status = LearningRequirementStatus.Satisfied };
        if (mismatch == "actor")
        {
            var actor = new ManagedActorDto { ActorId = Guid.CreateVersion7(), ScopeId = _tenantId };
            _ctx.Services.GetRequiredService<UiShellState>()
                .TrySetActiveActor(actor.ActorId!.Value, [actor]);
            source.ContextKey = $"actor:{actor.ActorId:D}";
        }
        offered = mismatch switch
        {
            "requirement" => offered with { RequirementKey = "another-requirement" },
            "material" => offered with { MaterialVersion = 5 },
            "policy" => offered with { PolicySourceStamp = "another-policy-source" },
            _ => offered
        };
        RespondWith(source);
        await service.LoadContextAsync();
        var acknowledgement = Signal();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                acknowledgement.TrySetResult();
                return Task.FromResult(Receipt(Acknowledged(source)));
            });

        await Assert.That(await service.AcknowledgeRequirementAsync(offered)).IsFalse();
        await Assert.That(acknowledgement.Task.IsCompleted).IsFalse();
        await Assert.That(service.LastMutationApplied == true).IsFalse();
    }

    [Test]
    public async Task RealFacade_PreferencesRequireFreshMatchingStateRatherThanOnlyAWriteReceipt()
    {
        var service = Service();
        var source = Context();
        RespondWith(source);
        await service.LoadContextAsync();
        var requested = Signal<LearningUpdatePreferencesRequest>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        var freshRequested = Signal();
        var fresh = Signal<HalResourceOfLearningContextDto>();
        _transport.UpdateLearningPreferencesAsync(
                Arg.Any<LearningUpdatePreferencesRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult(call.ArgAt<LearningUpdatePreferencesRequest>(0));
                return response.Task;
            });
        _transport.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                freshRequested.TrySetResult();
                return fresh.Task;
            });
        var saving = service.UpdatePreferencesAsync(true, true, false);
        var request = await requested.Task.WaitAsync(Timeout);
        await Assert.That(request.ExpectedRevision).IsEqualTo(7);
        await Assert.That(request.ExpectedScopeStamp).IsEqualTo(source.ScopeStamp);
        await Assert.That(request.SuppressAutomaticTours).IsTrue();
        await Assert.That(request.HideLearning).IsTrue();
        await Assert.That(request.PinLearning).IsFalse();
        var settled = Context();
        settled.LearnerRevision = 8;
        settled.Preferences = new() { SuppressAutomaticTours = true, HideLearning = true, PinLearning = false };
        response.SetResult(Receipt(settled));
        await freshRequested.Task.WaitAsync(Timeout);
        await Assert.That(saving.IsCompleted).IsFalse();
        await Assert.That(service.LastMutationApplied == true).IsFalse();

        fresh.SetResult(source);
        await Assert.That(await saving.WaitAsync(Timeout)).IsFalse();
        await Assert.That(service.LastMutationApplied == true).IsFalse();
        await Assert.That(await service.UpdatePreferencesAsync(true, true, false)).IsFalse();
    }

    [Test]
    [Arguments("tenant", "acknowledgement", "write")]
    [Arguments("account", "acknowledgement", "write")]
    [Arguments("actor", "acknowledgement", "write")]
    [Arguments("dispose", "acknowledgement", "write")]
    [Arguments("tenant", "acknowledgement", "refresh")]
    [Arguments("account", "acknowledgement", "refresh")]
    [Arguments("actor", "acknowledgement", "refresh")]
    [Arguments("dispose", "acknowledgement", "refresh")]
    [Arguments("tenant", "preferences", "write")]
    [Arguments("account", "preferences", "write")]
    [Arguments("actor", "preferences", "write")]
    [Arguments("dispose", "preferences", "write")]
    [Arguments("tenant", "preferences", "refresh")]
    [Arguments("account", "preferences", "refresh")]
    [Arguments("actor", "preferences", "refresh")]
    [Arguments("dispose", "preferences", "refresh")]
    public async Task RealFacade_LatePrivateMutationCannotAcknowledgeOrPublishForAnotherOwner(
        string ownerChange, string action, string stage)
    {
        var service = Service();
        var source = Context();
        RespondWith(source);
        await service.LoadContextAsync();
        var settled = action == "acknowledgement" ? Acknowledged(source) : Context();
        if (action == "preferences")
        {
            settled.LearnerRevision = 8;
            settled.Preferences = new() { SuppressAutomaticTours = true, HideLearning = false, PinLearning = true };
        }
        var entered = Signal<CancellationToken>();
        var writeResponse = Signal<HalResourceOfLearningMutationReceiptDto>();
        var freshResponse = Signal<HalResourceOfLearningContextDto>();
        var submitted = new List<string>();
        _transport.LearningAcknowledgeRequirementAsync(
                Arg.Any<string>(), Arg.Any<LearningAcknowledgeRequirementRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submitted.Add("acknowledgement");
                if (stage == "write")
                    entered.TrySetResult(call.ArgAt<CancellationToken>(4));
                return stage == "write" ? writeResponse.Task : Task.FromResult(Receipt(settled));
            });
        _transport.UpdateLearningPreferencesAsync(
                Arg.Any<LearningUpdatePreferencesRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submitted.Add("preferences");
                if (stage == "write")
                    entered.TrySetResult(call.ArgAt<CancellationToken>(3));
                return stage == "write" ? writeResponse.Task : Task.FromResult(Receipt(settled));
            });
        _transport.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (stage == "refresh")
                    entered.TrySetResult(call.ArgAt<CancellationToken>(4));
                return freshResponse.Task;
            });

        var mutation = action == "acknowledgement"
            ? service.AcknowledgeRequirementAsync(source.Requirement)
            : service.UpdatePreferencesAsync(true, false, true);
        var token = await entered.Task.WaitAsync(Timeout);
        var cancelled = Signal();
        using var registration = token.Register(() => cancelled.TrySetResult());
        ChangeOwner(service, ownerChange);
        await cancelled.Task.WaitAsync(Timeout);
        writeResponse.TrySetResult(Receipt(settled));
        freshResponse.TrySetResult(settled);

        await Assert.That(await mutation.WaitAsync(Timeout)).IsFalse();
        await Assert.That(service.Context is null).IsTrue();
        await Assert.That(service.GuidancePolicy is null).IsTrue();
        await Assert.That(service.LastMutationApplied is null).IsTrue();
        await Assert.That(await service.AcknowledgeRequirementAsync(source.Requirement)).IsFalse();
        await Assert.That(await service.UpdatePreferencesAsync(true, false, true)).IsFalse();
        await Assert.That(submitted.Count).IsEqualTo(1);
        await Assert.That(submitted.Single()).IsEqualTo(action);
    }

    private HalResourceOfLearningContextDto PanelContext()
    {
        var context = Context();
        context.Requirement = context.Requirement with { MaterialVersion = 1 };
        return context;
    }

    private async Task<IRenderedComponent<CascadingValue<TenantContext>>> MountPanel(
        HalResourceOfLearningContextDto context, string pathBase = "")
    {
        _ctx.Services.AddSingleton<NavigationManager>(new SettingsNavigation($"http://localhost{pathBase}/"));
        var users = Substitute.For<IUserClient>();
        users.GetCurrentUserAsync().Returns(Task.FromResult(new HalResourceOfUserDto
        {
            Id = _userId, ActorId = _profileActorId
        }));
        _ctx.Services.AddSingleton(users);
        _ctx.Services.AddScoped<IUserService, UserService>();
        _ctx.Services.AddSingleton(Substitute.For<IUiShellClient>());
        _ctx.Services.AddScoped<IUiShellContextService, UiShellContextService>();
        var requested = Signal();
        var response = Signal<HalResourceOfLearningContextDto>();
        _transport.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return response.Task;
            });
        var service = Service();
        var cut = _ctx.RenderMudComponent<CascadingValue<TenantContext>>(parameters => parameters
            .Add(item => item.Name, "TenantContext")
            .Add(item => item.Value, new TenantContext { TenantId = _tenantId, IsAuthenticated = true })
            .AddChildContent<PrivateLearningPanel>(child => child
                .Add(panel => panel.ActorId, _profileActorId)
                .Add(panel => panel.TenantId, _tenantId)));
        using var rendered = Watch(cut, () => !service.IsBusy
            && cut.FindAll("[data-learning-action='reload']").Count == 1);
        await requested.Task.WaitAsync(Timeout);
        response.SetResult(context);
        await rendered.Completion;
        RespondWith(context);
        return cut;
    }

    private async Task<IRenderedComponent<CascadingValue<TenantContext>>> MountSettings(
        HalResourceOfLearningGuidancePolicyDto policy, string pathBase = "",
        HalResourceOfLearningContextDto? context = null)
    {
        var component = Explore.Blazor.Client.Routes.CreateRouteCatalog()
            .Single(route => route.Path == "/settings/admin/learning").Component
            ?? throw new InvalidOperationException("The shipped settings route has no component.");
        _ctx.Services.AddSingleton<NavigationManager>(new SettingsNavigation($"http://localhost{pathBase}/"));
        RespondWith(context ?? Context());
        var requested = Signal();
        var response = Signal<HalResourceOfLearningGuidancePolicyDto>();
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return response.Task;
            });
        var service = Service();
        var cut = _ctx.RenderMudComponent<CascadingValue<TenantContext>>(parameters => parameters
            .Add(item => item.Name, "TenantContext")
            .Add(item => item.Value, new TenantContext { TenantId = _tenantId, IsAuthenticated = true })
            .AddChildContent(builder =>
            {
                builder.OpenComponent(0, component);
                builder.CloseComponent();
            }));
        using var rendered = Watch(cut, () => !service.IsBusy && cut.FindAll("form").Count == 1);
        await requested.Task.WaitAsync(Timeout);
        response.SetResult(policy);
        await rendered.Completion;
        // Subsequent explicit reloads receive the same server document unless the case replaces it.
        _transport.GetLearningGuidancePolicyAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(policy));
        return cut;
    }

    private static async Task EditSettings(IRenderedComponent<CascadingValue<TenantContext>> cut)
    {
        await cut.FindAll("form > fieldset > label input[type='checkbox']")[0]
            .ChangeAsync(new() { Value = true });
        await cut.FindAll("form > fieldset > label input[type='checkbox']")[1]
            .ChangeAsync(new() { Value = false });
        await cut.Find("#learning-invitation-audience").ChangeAsync(new()
        {
            Value = nameof(LearningGuidanceAudience.TenantAdministrators)
        });
        await cut.Find("#learning-manual-audience").ChangeAsync(new()
        {
            Value = nameof(LearningGuidanceAudience.ActiveMembers)
        });
        await cut.Find("#learning-requirement-policy").ChangeAsync(new()
        {
            Value = nameof(LearningRequirementPolicy.TenantAdminOrientationAcknowledgement)
        });
        var families = Draft().FamilyOverrides.ToArray();
        for (var index = 0; index < families.Length; index++)
        {
            var family = families[index];
            await cut.FindAll(".learning-settings__family")[index].QuerySelectorAll("input[type='checkbox']")[0]
                .ChangeAsync(new() { Value = true });
            await cut.FindAll(".learning-settings__family")[index].QuerySelectorAll("input[type='checkbox']")[1]
                .ChangeAsync(new() { Value = family.Enabled });
            await cut.FindAll(".learning-settings__family")[index].QuerySelectorAll("input[type='checkbox']")[2]
                .ChangeAsync(new() { Value = family.AutomaticInvitations });
            await cut.Find($"#learning-family-invitation-{family.Family}").ChangeAsync(new()
            {
                Value = family.InvitationAudience?.ToString() ?? ""
            });
            await cut.Find($"#learning-family-manual-{family.Family}").ChangeAsync(new()
            {
                Value = family.ManualAccessAudience?.ToString() ?? ""
            });
        }
    }

    private static async Task AssertSettingsDraft(IRenderedComponent<CascadingValue<TenantContext>> cut)
    {
        var toggles = cut.FindAll("form > fieldset > label input[type='checkbox']");
        await Assert.That(((IHtmlInputElement)toggles[0]).IsChecked).IsTrue();
        await Assert.That(((IHtmlInputElement)toggles[1]).IsChecked).IsFalse();
        await Assert.That(((IHtmlSelectElement)cut.Find("#learning-invitation-audience")).Value)
            .IsEqualTo(nameof(LearningGuidanceAudience.TenantAdministrators));
        await Assert.That(((IHtmlSelectElement)cut.Find("#learning-manual-audience")).Value)
            .IsEqualTo(nameof(LearningGuidanceAudience.ActiveMembers));
        await Assert.That(((IHtmlSelectElement)cut.Find("#learning-requirement-policy")).Value)
            .IsEqualTo(nameof(LearningRequirementPolicy.TenantAdminOrientationAcknowledgement));
        await Assert.That(cut.FindAll(".learning-settings__family").Count).IsEqualTo(4);
        var families = Draft().FamilyOverrides.ToArray();
        for (var index = 0; index < families.Length; index++)
        {
            var controls = cut.FindAll(".learning-settings__family")[index].QuerySelectorAll("input[type='checkbox']");
            await Assert.That(((IHtmlInputElement)controls[0]).IsChecked).IsTrue();
            await Assert.That(((IHtmlInputElement)controls[1]).IsChecked).IsEqualTo(families[index].Enabled);
            await Assert.That(((IHtmlInputElement)controls[2]).IsChecked).IsEqualTo(families[index].AutomaticInvitations);
            await Assert.That(((IHtmlSelectElement)cut.Find(
                    $"#learning-family-invitation-{families[index].Family}")).Value ?? "")
                .IsEqualTo(families[index].InvitationAudience?.ToString() ?? "");
            await Assert.That(((IHtmlSelectElement)cut.Find(
                    $"#learning-family-manual-{families[index].Family}")).Value ?? "")
                .IsEqualTo(families[index].ManualAccessAudience?.ToString() ?? "");
        }
    }

    private static async Task AssertDraft(LearningGuidancePolicyPayload policy, bool enabled = true)
    {
        await Assert.That(policy.Enabled).IsEqualTo(enabled);
        await Assert.That(policy.AutomaticInvitations).IsFalse();
        await Assert.That(policy.InvitationAudience).IsEqualTo(LearningGuidanceAudience.TenantAdministrators);
        await Assert.That(policy.ManualAccessAudience).IsEqualTo(LearningGuidanceAudience.ActiveMembers);
        await Assert.That(policy.RequirementPolicy)
            .IsEqualTo(LearningRequirementPolicy.TenantAdminOrientationAcknowledgement);
        await Assert.That(policy.FamilyOverrides.Count).IsEqualTo(4);
        var shell = policy.FamilyOverrides.Single(item => item.Family == LearningTourFamily.Shell);
        await Assert.That(shell.Enabled).IsTrue();
        await Assert.That(shell.AutomaticInvitations).IsFalse();
        await Assert.That(shell.InvitationAudience).IsEqualTo(LearningGuidanceAudience.ActiveMembers);
        await Assert.That(shell.ManualAccessAudience).IsEqualTo(LearningGuidanceAudience.TenantAdministrators);
        var studio = policy.FamilyOverrides.Single(item => item.Family == LearningTourFamily.Studio);
        await Assert.That(studio.Enabled).IsFalse();
        await Assert.That(studio.AutomaticInvitations).IsTrue();
        await Assert.That(studio.InvitationAudience).IsEqualTo(LearningGuidanceAudience.TenantAdministrators);
        await Assert.That(studio.ManualAccessAudience).IsEqualTo(LearningGuidanceAudience.ActiveMembers);
        var events = policy.FamilyOverrides.Single(item => item.Family == LearningTourFamily.EventManagement);
        await Assert.That(events.Enabled).IsTrue();
        await Assert.That(events.AutomaticInvitations).IsTrue();
        await Assert.That(events.InvitationAudience is null && events.ManualAccessAudience is null).IsTrue();
        var ai = policy.FamilyOverrides.Single(item => item.Family == LearningTourFamily.NormalAi);
        await Assert.That(ai.Enabled).IsFalse();
        await Assert.That(ai.AutomaticInvitations).IsFalse();
        await Assert.That(ai.InvitationAudience is null && ai.ManualAccessAudience is null).IsTrue();
    }

    private ILearningClientService Service()
    {
        if (_service is not null)
            return _service;
        _ctx.Services.GetRequiredService<CurrentUserState>().NotifyUpdated(new UserDto { Id = _userId });
        _service = _ctx.Services.GetRequiredService<ILearningClientService>();
        _service.SetTenantContext(_tenantId);
        return _service;
    }

    private HalResourceOfLearningContextDto Context() => new()
    {
        TenantId = _tenantId,
        UserId = _userId,
        ContextKey = "self",
        ScopeStamp = "guidance-settings-self-scope",
        LearnerRevision = 7,
        ResourceRevision = 3,
        SpaceId = Guid.CreateVersion7(),
        SpaceStatus = LearningSpaceStatus.Active,
        Preferences = new() { SuppressAutomaticTours = false, HideLearning = false, PinLearning = false },
        Capabilities = new() { CanLearn = true },
        Requirement = new()
        {
            RequirementKey = "tenant-admin-orientation",
            MaterialVersion = 4,
            PolicySourceStamp = "guidance-settings-policy-source",
            Status = LearningRequirementStatus.Pending
        },
        Tours = [],
        Lessons = [],
        OwnedPractices = [],
        _links = new Dictionary<string, HalLink>
        {
            ["preferences"] = new() { Href = "/api/learning/preferences", Method = "PUT" },
            ["acknowledge-requirement"] = new() { Href = "/api/learning/requirements/tenant-admin-orientation", Method = "POST" }
        }
    };

    private void RespondWith(HalResourceOfLearningContextDto context) =>
        _transport.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(context));

    private static HalResourceOfLearningContextDto Acknowledged(HalResourceOfLearningContextDto source)
    {
        var context = new HalResourceOfLearningContextDto
        {
            TenantId = source.TenantId, UserId = source.UserId,
            ContextKey = source.ContextKey, ScopeStamp = source.ScopeStamp,
            LearnerRevision = source.LearnerRevision + 1, ResourceRevision = source.ResourceRevision,
            SpaceId = source.SpaceId, SpaceStatus = source.SpaceStatus, Preferences = source.Preferences,
            Capabilities = source.Capabilities,
            Requirement = source.Requirement with
            {
                Status = LearningRequirementStatus.Satisfied,
                AcknowledgedAtUtc = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)
            },
            Tours = [], Lessons = [], OwnedPractices = [],
            _links = new Dictionary<string, HalLink>()
        };
        return context;
    }

    private static HalResourceOfLearningMutationReceiptDto Receipt(HalResourceOfLearningContextDto context) => new()
    {
        Id = Guid.CreateVersion7(), ScopeStamp = context.ScopeStamp,
        ResourceRevision = context.ResourceRevision, LearnerRevision = context.LearnerRevision,
        State = new()
        {
            TenantId = context.TenantId, UserId = context.UserId, ContextKey = context.ContextKey,
            ScopeStamp = context.ScopeStamp, LearnerRevision = context.LearnerRevision,
            ResourceRevision = context.ResourceRevision, Preferences = context.Preferences,
            Requirement = context.Requirement
        }
    };

    private void ChangeOwner(ILearningClientService service, string change)
    {
        switch (change)
        {
            case "tenant":
                service.SetTenantContext(Guid.CreateVersion7());
                break;
            case "account":
                var replacement = Guid.CreateVersion7();
                _ctx.Services.GetRequiredService<CurrentUserState>()
                    .NotifyUpdated(new UserDto { Id = replacement });
                _ctx.SetAuthenticatedUser(replacement, "Replacement administrator");
                break;
            case "actor":
                var actor = new ManagedActorDto { ActorId = Guid.CreateVersion7(), ScopeId = _tenantId };
                _ctx.Services.GetRequiredService<UiShellState>()
                    .TrySetActiveActor(actor.ActorId!.Value, [actor]);
                break;
            case "dispose":
                service.Dispose();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private static LearningGuidancePolicyPayload Draft() => new()
    {
        Enabled = true,
        AutomaticInvitations = false,
        InvitationAudience = LearningGuidanceAudience.TenantAdministrators,
        ManualAccessAudience = LearningGuidanceAudience.ActiveMembers,
        RequirementPolicy = LearningRequirementPolicy.TenantAdminOrientationAcknowledgement,
        FamilyOverrides =
        [
            new() { Family = LearningTourFamily.Shell, Enabled = true, AutomaticInvitations = false,
                InvitationAudience = LearningGuidanceAudience.ActiveMembers,
                ManualAccessAudience = LearningGuidanceAudience.TenantAdministrators },
            new() { Family = LearningTourFamily.Studio, Enabled = false, AutomaticInvitations = true,
                InvitationAudience = LearningGuidanceAudience.TenantAdministrators,
                ManualAccessAudience = LearningGuidanceAudience.ActiveMembers },
            new() { Family = LearningTourFamily.EventManagement, Enabled = true, AutomaticInvitations = true },
            new() { Family = LearningTourFamily.NormalAi, Enabled = false, AutomaticInvitations = false }
        ]
    };

    private static HalResourceOfLearningGuidancePolicyDto Policy(bool editable = true) => new()
    {
        SchemaVersion = 1,
        DefaultsVersion = "guidance-test-defaults",
        ConcurrencyStamp = Guid.CreateVersion7(),
        Policy = new()
        {
            Enabled = true, AutomaticInvitations = true,
            InvitationAudience = LearningGuidanceAudience.ActiveMembers,
            ManualAccessAudience = LearningGuidanceAudience.TenantAdministrators,
            RequirementPolicy = LearningRequirementPolicy.Optional,
            FamilyOverrides = []
        },
        _links = editable
            ? new Dictionary<string, HalLink> { ["edit"] = new() { Href = "/api/learning/guidance", Method = "PUT" } }
            : new Dictionary<string, HalLink>()
    };

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static RenderSignal Watch<T>(IRenderedComponent<T> component, Func<bool> matches)
        where T : IComponent => new(
            handler => component.OnMarkupUpdated += handler,
            handler => component.OnMarkupUpdated -= handler, matches);

    private sealed class RenderSignal : IDisposable
    {
        private readonly Action<EventHandler> _unsubscribe;
        private readonly Func<bool> _matches;
        private readonly TaskCompletionSource _signal = Signal();

        public RenderSignal(Action<EventHandler> subscribe, Action<EventHandler> unsubscribe, Func<bool> matches)
        {
            _unsubscribe = unsubscribe;
            _matches = matches;
            subscribe(OnMarkupUpdated);
        }

        public Task Completion => _signal.Task.WaitAsync(Timeout);

        private void OnMarkupUpdated(object? sender, EventArgs args)
        {
            if (_matches())
                _signal.TrySetResult();
        }

        public void Dispose() => _unsubscribe(OnMarkupUpdated);
    }

    private sealed class SettingsNavigation : NavigationManager
    {
        public SettingsNavigation(string baseUri) => Initialize(baseUri, baseUri);

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }
}
