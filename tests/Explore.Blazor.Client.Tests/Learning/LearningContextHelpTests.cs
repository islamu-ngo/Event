using AngleSharp.Dom;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Learning;
using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Contracts.Services.SupportAccess;
using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Services.Learning;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.RenderTree;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class LearningContextHelpTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly BlazorTestContext _ctx = new();
    private readonly ILearningClientService _learning = Substitute.For<ILearningClientService>();
    private readonly TaskCompletionSource _privateRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Guid _tenantId = Guid.CreateVersion7();

    public LearningContextHelpTests()
    {
        _ctx.JSInterop.SetupVoid("mudThemeProvider.stopWatchingDarkMode").SetVoidResult();
        _ctx.AddShellStateMocks();
        NavMenuTestServices.Register(_ctx);
        _ctx.Services.AddScoped<MainContentAppearanceState>();
        _ctx.Services.AddScoped<AiAssistantConversationState>();
        var ai = Substitute.For<IAiAssistantClientService>();
        ai.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(new()
            {
                _embedded = new() { Items = [] }
            }));
        _ctx.Services.AddSingleton(ai);
        var preferences = Substitute.For<IShellPreferencesService>();
        preferences.LoadAsync(Arg.Any<UiShellContextDto>(), Arg.Any<CancellationToken>())
            .Returns(new ShellPreferenceState(WorkspaceKey.Events.Value, null, "/settings/personal"));
        _ctx.Services.AddSingleton(preferences);
        var support = Substitute.For<ISupportAccessClientService>();
        support.RefreshAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _ctx.Services.AddSingleton(support);
        _ctx.Services.AddSingleton(Substitute.For<IAnalyticsInterop>());
        _ctx.Services.AddSingleton(Substitute.For<ICookieConsentInterop>());
        _ctx.Services.AddSingleton(new CookieConsentStateService());
        _learning.LoadContextAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _privateRead.TrySetResult();
            return Task.CompletedTask;
        });
        _learning.LoadPracticeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _privateRead.TrySetResult();
            return Task.CompletedTask;
        });
        _ctx.Services.AddSingleton(_learning);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments(false, "anonymous")]
    [Arguments(true, "anonymous")]
    [Arguments(false, "authenticated")]
    [Arguments(true, "authenticated")]
    [Arguments(false, "suppressed")]
    [Arguments(true, "suppressed")]
    public async Task ActualShell_PersistentHelpIsDiscoverableWithoutOpeningUserMenu(
        bool fullLayout, string audience)
    {
        if (audience == "anonymous")
            _ctx.SetAnonymousUser();
        else
            _ctx.SetAuthenticatedUser(_userId, "Learner");

        if (audience == "suppressed")
        {
            _learning.Scope.Returns(new LearningClientScope(_tenantId, _userId, "self"));
            _learning.Context.Returns(new HalResourceOfLearningContextDto
            {
                TenantId = _tenantId,
                UserId = _userId,
                ContextKey = "self",
                Preferences = new() { HideLearning = true, PinLearning = false },
                Tours = [],
                Lessons = [],
                OwnedPractices = [],
                _links = new Dictionary<string, HalLink>()
            });
        }

        _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/events");
        var layout = fullLayout
            ? _ctx.Render<MainLayout>(parameters =>
                parameters.Add(component => component.Body,
                    (RenderFragment)(builder => builder.AddContent(0, "Public event content"))))
            : null;
        var navMenu = fullLayout ? null : _ctx.RenderMudComponent<NavMenu>();
        var shellElements = layout is not null
            ? layout.FindAll("a,button,summary")
            : navMenu!.FindAll("a,button,summary");
        var shellLearningElements = layout is not null
            ? layout.FindAll("[data-testid='learning-practice'],[data-learning-action]")
            : navMenu!.FindAll("[data-testid='learning-practice'],[data-learning-action]");

        await Assert.That(HelpActions(shellElements).Count()).IsGreaterThan(0);
        await Assert.That(shellLearningElements.Count)
            .IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task PublicPrerender_PreservesHelpWithoutReadingPrivateLearning(
        bool fullLayout, bool authenticated)
    {
        if (authenticated)
            _ctx.SetAuthenticatedUser(_userId, "Learner");
        else
            _ctx.SetAnonymousUser();
        _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/events");
        await using var renderer = new StaticHelpHtmlRenderer(
            _ctx.Services, _ctx.Services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = renderer.BeginRenderingComponent(
                fullLayout ? typeof(MainLayout) : typeof(NavMenu), ParameterView.Empty);
            await output.QuiescenceTask.WaitAsync(Timeout);
            return output.ToHtmlString();
        });
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        // Assert the privacy boundary separately so missing Help cannot mask a private read.
        await Assert.That(_privateRead.Task.IsCompleted).IsFalse();
        await Assert.That(document.QuerySelectorAll(
            "[data-testid='learning-practice'],[data-learning-action],a[href*='/learning/demo/']").Length)
            .IsEqualTo(0);
        await Assert.That(HelpActions(document.QuerySelectorAll("a,button,summary")).Count())
            .IsGreaterThan(0);
    }

    [Test]
    [Arguments("anonymous")]
    [Arguments("ordinary-user")]
    [Arguments("personal-organizer")]
    [Arguments("organization-collaborator")]
    [Arguments("organization-admin")]
    [Arguments("group-admin")]
    [Arguments("tenant-admin")]
    [Arguments("instance-admin")]
    public async Task ActualHelp_NativeDisclosureRetainsGoalsAcrossEightAudiences(string audience)
    {
        if (audience == "anonymous")
            _ctx.SetAnonymousUser();
        else
            _ctx.SetAuthenticatedUser(_userId, "Learner");
        var scopeId = Guid.CreateVersion7();
        var scope = audience switch
        {
            "organization-admin" => "Organization",
            "group-admin" => "Group",
            "tenant-admin" => "Tenant",
            "instance-admin" => "Instance",
            _ => null
        };
        var scopes = scope is null ? Array.Empty<SettingsScopeDto>() :
            [new SettingsScopeDto { Scope = scope, ScopeId = scopeId, DisplayName = scope }];
        var cut = _ctx.Render<LearningContextHelp>(parameters =>
            parameters.Add(component => component.AuthorizedSettingsScopes, scopes));
        var disclosure = cut.Find("details[data-testid='learning-context-help']");

        await Assert.That(disclosure.Children[0].LocalName).IsEqualTo("summary");
        await Assert.That(HelpActions(cut.FindAll("a,button,summary")).Count()).IsEqualTo(1);
        await Assert.That(VisibleControls(cut.FindAll("a")).Count()).IsEqualTo(0);
        await Assert.That(_privateRead.Task.IsCompleted).IsFalse();

        // bUnit does not execute the browser's native summary default action.
        // Exercise both actual disclosure DOM states without claiming browser input coverage.
        disclosure.SetAttribute("open", "");
        await Assert.That(cut.FindAll(".learning-help__goals dt").Count)
            .IsEqualTo(audience == "anonymous" ? 2 : scope is null ? 6 : 7);
        await Assert.That(VisibleControls(cut.FindAll("a")).Count())
            .IsEqualTo(audience == "anonymous" ? 2 : scope is null ? 2 : 3);
        var hrefs = cut.FindAll("a").Select(link => link.GetAttribute("href")).ToArray();
        await Assert.That(hrefs.Contains("/auth/login")).IsEqualTo(audience == "anonymous");
        await Assert.That(hrefs.Contains("/learning")).IsEqualTo(audience != "anonymous");
        await Assert.That(hrefs.Any(href => href?.Contains("/settings/", StringComparison.Ordinal) == true))
            .IsEqualTo(scope is not null);
        if (scope is not null)
        {
            var expected = scope switch
            {
                "Instance" => "/settings/instance",
                "Tenant" => "/settings/admin",
                "Organization" => $"/settings/organization/{scopeId:D}",
                _ => $"/settings/group/{scopeId:D}"
            };
            await Assert.That(hrefs.Contains(expected)).IsTrue();
        }
        await Assert.That(cut.FindAll(
            "[data-testid='private-learning-panel'],[data-testid='learning-practice'],[data-learning-action]").Count)
            .IsEqualTo(0);
        disclosure.RemoveAttribute("open");
        await Assert.That(VisibleControls(cut.FindAll("a")).Count()).IsEqualTo(0);
        await Assert.That(HelpActions(cut.FindAll("a,button,summary")).Count()).IsEqualTo(1);
        await Assert.That(_privateRead.Task.IsCompleted).IsFalse();
    }

    [Test]
    [Arguments("")]
    [Arguments("/acme")]
    public async Task ActualHelp_BrowseDestinationIsEventCatalogueUnderApplicationBase(string pathBase)
    {
        _ctx.Services.AddSingleton<NavigationManager>(new HelpNavigation($"http://localhost{pathBase}/"));
        _ctx.SetAnonymousUser();
        var cut = _ctx.Render<LearningContextHelp>();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var browse = cut.Find(".learning-help__goals dd a");

        await Assert.That(browse.GetAttribute("href")).IsEqualTo(pathBase + "/events");
        await cut.InvokeAsync(() => navigation.NavigateTo(browse.GetAttribute("href")!));
        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo(pathBase + "/events");
        await Assert.That(cut.Find("a[data-enhance-nav='false']").GetAttribute("href"))
            .IsEqualTo(pathBase + "/auth/login");
        await Assert.That(_privateRead.Task.IsCompleted).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualHelp_StaticRendererNeverPublishesInteractiveAdminScopes(bool authenticated)
    {
        if (authenticated)
            _ctx.SetAuthenticatedUser(_userId, "Learner");
        else
            _ctx.SetAnonymousUser();
        SettingsScopeDto[] scopes =
        [
            new() { Scope = "Instance", DisplayName = "Instance" },
            new() { Scope = "Tenant", ScopeId = _tenantId, DisplayName = "Directory" },
            new() { Scope = "Organization", ScopeId = Guid.CreateVersion7(), DisplayName = "Organization" },
            new() { Scope = "Group", ScopeId = Guid.CreateVersion7(), DisplayName = "Group" }
        ];
        await using var renderer = new StaticHelpHtmlRenderer(
            _ctx.Services, _ctx.Services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = renderer.BeginRenderingComponent(typeof(LearningContextHelp),
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(LearningContextHelp.AuthorizedSettingsScopes)] = scopes
                }));
            await output.QuiescenceTask.WaitAsync(Timeout);
            return output.ToHtmlString();
        });
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        await Assert.That(HelpActions(document.QuerySelectorAll("a,button,summary")).Count()).IsEqualTo(1);
        await Assert.That(document.QuerySelectorAll("a[href*='/settings/']").Length).IsEqualTo(0);
        await Assert.That(document.QuerySelectorAll(".learning-help__goals dt").Length)
            .IsEqualTo(authenticated ? 6 : 2);
        await Assert.That(_privateRead.Task.IsCompleted).IsFalse();
    }

    [Test]
    [Arguments("suppressed")]
    [Arguments("disabled")]
    public async Task ActualHelp_RemainsStaticAndAvailableWhenPrivateGuidanceIsUnavailable(string state)
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        var context = PrivateContext();
        context.Preferences = new()
        {
            SuppressAutomaticTours = state == "suppressed",
            HideLearning = true
        };
        _learning.Context.Returns(context);
        _learning.GuidancePolicy.Returns(new HalResourceOfLearningGuidancePolicyDto
        {
            Policy = new() { Enabled = state != "disabled", FamilyOverrides = [] }
        });
        var cut = _ctx.Render<LearningContextHelp>();

        await Assert.That(HelpActions(cut.FindAll("a,button,summary")).Count()).IsEqualTo(1);
        cut.Find("details").SetAttribute("open", "");
        await Assert.That(cut.FindAll(".learning-help__goals dt").Count).IsEqualTo(6);
        await Assert.That(cut.FindAll("a[href='/learning']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-learning-action],[data-testid='learning-practice']").Count)
            .IsEqualTo(0);
        await Assert.That(_privateRead.Task.IsCompleted).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualMainLayout_ForwardsOnlyServerAdvertisedSettingsScopesToHelp(bool grants)
    {
        var shell = Substitute.For<IUiShellContextService>();
        var requested = Signal();
        var response = Signal<UiShellContextDto?>();
        shell.GetContextAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            requested.TrySetResult();
            return response.Task;
        });
        shell.GetCachedContextAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            requested.TrySetResult();
            return response.Task;
        });
        _ctx.Services.AddSingleton(shell);
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/events");
        var cut = _ctx.Render<MainLayout>(parameters => parameters.Add(component => component.Body,
            (RenderFragment)(builder => builder.AddContent(0, "Public event content"))));
        await requested.Task.WaitAsync(Timeout);
        await RenderAfter(cut,
            () => cut.FindAll("[data-testid='learning-context-help']").Count == 1
                && cut.FindAll("[data-testid='learning-context-help'] a[href*='/settings/']").Count
                    == (grants ? 4 : 0),
            () => response.SetResult(new UiShellContextDto
            {
                TenantId = _tenantId,
                SettingsScopes = grants ?
                [
                    new() { Scope = "Instance", DisplayName = "Instance" },
                    new() { Scope = "Tenant", ScopeId = _tenantId, DisplayName = "Directory" },
                    new() { Scope = "Organization", ScopeId = Guid.CreateVersion7(), DisplayName = "Organization" },
                    new() { Scope = "Group", ScopeId = Guid.CreateVersion7(), DisplayName = "Group" }
                ] : []
            }));

        await Assert.That(cut.FindAll("[data-testid='learning-context-help'] a[href*='/settings/']").Count)
            .IsEqualTo(grants ? 4 : 0);
        await Assert.That(HelpActions(cut.FindAll("a,button,summary")).Any()).IsTrue();
        await Assert.That(_privateRead.Task.IsCompleted).IsFalse();
    }

    [Test]
    [Arguments(false, "rejected")]
    [Arguments(true, "rejected")]
    [Arguments(false, "conflict")]
    [Arguments(true, "conflict")]
    [Arguments(false, "settled")]
    [Arguments(true, "settled")]
    public async Task RealFacade_PrivateHelpMutationRequiresReceiptAndFreshCurrentState(
        bool acknowledge, string outcome)
    {
        var transport = Substitute.For<ILearningClient>();
        using var facade = CreateFacade(transport);
        var source = PrivateContext();
        var settled = PrivateContext(settled: true);
        var conflicting = PrivateContext();
        conflicting.LearnerRevision = settled.LearnerRevision;
        var requested = Signal();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        var refreshRequested = Signal();
        var refresh = Signal<HalResourceOfLearningContextDto>();
        var reads = 0;
        var writes = 0;
        transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (++reads == 1)
                    return Task.FromResult(source);
                refreshRequested.TrySetResult();
                return refresh.Task;
            });
        ConfigureMutation(transport, acknowledge, requested, response, () => writes++);
        await facade.LoadContextAsync().WaitAsync(Timeout);
        await Assert.That(requested.Task.IsCompleted).IsFalse();

        var mutation = Mutate(facade, acknowledge, source);
        await requested.Task.WaitAsync(Timeout);
        await Assert.That(facade.IsSaving).IsTrue();
        await Assert.That(facade.LastMutationApplied is null).IsTrue();

        response.SetResult(PrivateReceipt(outcome == "rejected" ? source : settled));
        if (outcome != "rejected")
        {
            await refreshRequested.Task.WaitAsync(Timeout);
            await Assert.That(mutation.IsCompleted).IsFalse();
            await Assert.That(facade.LastMutationApplied is null).IsTrue();
            refresh.SetResult(outcome == "conflict" ? conflicting : settled);
        }

        await Assert.That(await mutation.WaitAsync(Timeout)).IsEqualTo(outcome == "settled");
        await Assert.That(facade.IsSaving).IsFalse();
        await Assert.That(facade.Failure).IsEqualTo(outcome switch
        {
            "rejected" => LearningClientFailure.SaveNotApplied,
            "conflict" => LearningClientFailure.Conflict,
            _ => LearningClientFailure.None
        });
        await Assert.That(facade.LastMutationApplied == true).IsEqualTo(outcome == "settled");
        await Assert.That(facade.Context?.LearnerRevision)
            .IsEqualTo(outcome == "settled" ? settled.LearnerRevision : source.LearnerRevision);
        await Assert.That(reads).IsEqualTo(outcome == "rejected" ? 1 : 2);
        await Assert.That(writes).IsEqualTo(1);
        if (outcome != "settled")
        {
            await Assert.That(await Mutate(facade, acknowledge, source).WaitAsync(Timeout)).IsFalse();
            await Assert.That(reads).IsEqualTo(outcome == "rejected" ? 1 : 2);
            await Assert.That(writes).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RealFacade_PrivateHelpMutationWithoutHalGrantDoesNotWrite(bool acknowledge)
    {
        var transport = Substitute.For<ILearningClient>();
        using var facade = CreateFacade(transport);
        var source = PrivateContext();
        source._links = new Dictionary<string, HalLink>();
        transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(source));
        var requested = Signal();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        ConfigureMutation(transport, acknowledge, requested, response);
        await facade.LoadContextAsync().WaitAsync(Timeout);

        await Assert.That(await Mutate(facade, acknowledge, source).WaitAsync(Timeout)).IsFalse();
        await Assert.That(requested.Task.IsCompleted).IsFalse();
        await Assert.That(facade.LastMutationApplied is null).IsTrue();
        await Assert.That(facade.IsSaving).IsFalse();
    }

    [Test]
    [Arguments("key")]
    [Arguments("material")]
    [Arguments("policy")]
    [Arguments("satisfied")]
    public async Task RealFacade_ExplicitAcknowledgementRejectsStaleOrDifferentRequirement(string mismatch)
    {
        var transport = Substitute.For<ILearningClient>();
        using var facade = CreateFacade(transport);
        var source = PrivateContext();
        var selected = new LearningRequirementDto
        {
            RequirementKey = mismatch == "key" ? "different-requirement" : source.Requirement.RequirementKey,
            MaterialVersion = mismatch == "material" ? 2 : source.Requirement.MaterialVersion,
            PolicySourceStamp = mismatch == "policy" ? "different-policy" : source.Requirement.PolicySourceStamp,
            Status = mismatch == "satisfied" ? LearningRequirementStatus.Satisfied : LearningRequirementStatus.Pending
        };
        transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(source));
        var requested = Signal();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        ConfigureMutation(transport, true, requested, response);
        await facade.LoadContextAsync().WaitAsync(Timeout);

        await Assert.That(await facade.AcknowledgeRequirementAsync(selected).WaitAsync(Timeout)).IsFalse();
        await Assert.That(requested.Task.IsCompleted).IsFalse();
        await Assert.That(facade.Context?.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(facade.LastMutationApplied is null).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RealFacade_SettlementNotificationOwnerChangeCannotReturnSuccess(bool acknowledge)
    {
        var transport = Substitute.For<ILearningClient>();
        using var facade = CreateFacade(transport);
        var source = PrivateContext();
        var settled = PrivateContext(settled: true);
        var reads = 0;
        transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(++reads == 1 ? source : settled));
        var requested = Signal();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        ConfigureMutation(transport, acknowledge, requested, response);
        await facade.LoadContextAsync().WaitAsync(Timeout);
        var invalidated = false;
        facade.Changed += () =>
        {
            if (facade.LastMutationApplied != true)
                return;
            invalidated = true;
            facade.SetTenantContext(Guid.CreateVersion7());
        };

        var mutation = Mutate(facade, acknowledge, source);
        await requested.Task.WaitAsync(Timeout);
        response.SetResult(PrivateReceipt(settled));

        await Assert.That(await mutation.WaitAsync(Timeout)).IsFalse();
        await Assert.That(invalidated).IsTrue();
        await Assert.That(facade.Context is null).IsTrue();
        await Assert.That(facade.Scope is null).IsTrue();
        await Assert.That(facade.LastMutationApplied is null).IsTrue();
    }

    [Test]
    [Arguments(false, false, "tenant")]
    [Arguments(true, false, "tenant")]
    [Arguments(false, true, "tenant")]
    [Arguments(true, true, "tenant")]
    [Arguments(false, false, "account")]
    [Arguments(true, false, "account")]
    [Arguments(false, true, "account")]
    [Arguments(true, true, "account")]
    [Arguments(false, false, "actor")]
    [Arguments(true, false, "actor")]
    [Arguments(false, true, "actor")]
    [Arguments(true, true, "actor")]
    [Arguments(false, false, "dispose")]
    [Arguments(true, false, "dispose")]
    [Arguments(false, true, "dispose")]
    [Arguments(true, true, "dispose")]
    public async Task RealFacade_PrivateHelpLateMutationCannotCrossOwnerOrDisposal(
        bool acknowledge, bool duringRefresh, string change)
    {
        var transport = Substitute.For<ILearningClient>();
        using var facade = CreateFacade(transport);
        var source = PrivateContext();
        var settled = PrivateContext(settled: true);
        var requested = Signal();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        var refreshRequested = Signal();
        var refresh = Signal<HalResourceOfLearningContextDto>();
        var reads = 0;
        transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (++reads == 1)
                    return Task.FromResult(source);
                refreshRequested.TrySetResult();
                return refresh.Task;
            });
        ConfigureMutation(transport, acknowledge, requested, response);
        await facade.LoadContextAsync().WaitAsync(Timeout);
        var mutation = Mutate(facade, acknowledge, source);
        await requested.Task.WaitAsync(Timeout);
        if (duringRefresh)
        {
            response.SetResult(PrivateReceipt(settled));
            await refreshRequested.Task.WaitAsync(Timeout);
        }

        InvalidateOwner(facade, change);
        await Assert.That(facade.Scope is null).IsTrue();
        await Assert.That(facade.Context is null).IsTrue();
        await Assert.That(facade.GuidancePolicy is null).IsTrue();
        // Deliver the old owner's data even though invalidation cancelled its transport.
        if (!duringRefresh)
            response.SetResult(PrivateReceipt(settled));
        refresh.SetResult(settled);

        await Assert.That(await mutation.WaitAsync(Timeout)).IsFalse();
        await Assert.That(facade.LastMutationApplied is null).IsTrue();
        await Assert.That(facade.Context is null).IsTrue();
        await Assert.That(facade.Scope is null).IsTrue();
        await Assert.That(reads).IsEqualTo(duringRefresh ? 2 : 1);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("account")]
    [Arguments("actor")]
    [Arguments("dispose")]
    public async Task RealFacade_PrivateHelpLateReadCannotRestoreInvalidatedOwner(string change)
    {
        var transport = Substitute.For<ILearningClient>();
        using var facade = CreateFacade(transport);
        var requested = Signal();
        var response = Signal<HalResourceOfLearningContextDto>();
        transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return response.Task;
            });
        var load = facade.LoadContextAsync();
        await requested.Task.WaitAsync(Timeout);

        InvalidateOwner(facade, change);
        response.SetResult(PrivateContext());
        await load.WaitAsync(Timeout);

        await Assert.That(facade.Context is null).IsTrue();
        await Assert.That(facade.Scope is null).IsTrue();
        await Assert.That(facade.GuidancePolicy is null).IsTrue();
        await Assert.That(facade.LastMutationApplied is null).IsTrue();
    }

    [Test]
    [Arguments("anonymous")]
    [Arguments("personal")]
    [Arguments("organization")]
    [Arguments("group")]
    [Arguments("unauthorized")]
    [Arguments("foreign-tenant")]
    [Arguments("unselected-actor")]
    [Arguments("instance")]
    public async Task ActualPrivatePanel_AdmitsOnlyCurrentSelectedOwner(string audience)
    {
        var fixture = ConfigurePanel(audience);
        var admitted = audience is "personal" or "organization" or "group";
        if (!admitted)
            fixture.UserResponse.SetResult(fixture.User);
        var cut = RenderPanel(fixture);
        if (admitted)
        {
            await fixture.UserRequested.Task.WaitAsync(Timeout);
            await RenderAfter(cut,
                () => cut.FindAll("[data-testid='private-learning-panel'][aria-busy='false'] dl").Count == 1,
                () => fixture.UserResponse.SetResult(fixture.User));
        }

        await Assert.That(fixture.PrivateRequested.Task.IsCompleted).IsEqualTo(admitted);
        await Assert.That(cut.FindAll(".private-learning__preferences").Count).IsEqualTo(admitted ? 1 : 0);
        await Assert.That(cut.FindAll(".private-learning__actions button").Count).IsEqualTo(admitted ? 3 : 0);
        await Assert.That(cut.FindAll(".private-learning__notice button").Count)
            .IsEqualTo(audience == "personal" ? 1 : 0);
        if (admitted)
        {
            await Assert.That(fixture.Facade.Scope?.ContextKey).IsEqualTo(fixture.Context.ContextKey);
            await Assert.That(fixture.Facade.Scope?.UserId).IsEqualTo(_userId);
            await Assert.That(fixture.Facade.Scope?.TenantId).IsEqualTo(_tenantId);
        }
        else
        {
            await Assert.That(fixture.Facade.Scope is null).IsTrue();
            await Assert.That(fixture.Facade.Context is null).IsTrue();
        }
        if (audience is "anonymous" or "instance")
            await Assert.That(fixture.UserRequested.Task.IsCompleted).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualPrivatePanel_StaticRendererNeverReadsIdentityOrPrivateLearning(bool authenticated)
    {
        var fixture = ConfigurePanel(authenticated ? "personal" : "anonymous");
        await using var renderer = new StaticHelpHtmlRenderer(
            _ctx.Services, _ctx.Services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = renderer.BeginRenderingComponent(typeof(PrivateLearningPanel),
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(PrivateLearningPanel.ActorId)] = fixture.ActorId,
                    [nameof(PrivateLearningPanel.TenantId)] = _tenantId
                }));
            await output.QuiescenceTask.WaitAsync(Timeout);
            return output.ToHtmlString();
        });
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        await Assert.That(fixture.UserRequested.Task.IsCompleted).IsFalse();
        await Assert.That(fixture.PrivateRequested.Task.IsCompleted).IsFalse();
        await Assert.That(document.QuerySelectorAll("[data-testid='private-learning-placeholder']").Length)
            .IsEqualTo(1);
        await Assert.That(document.QuerySelectorAll(
            "[data-testid='private-learning-panel'],.private-learning__preferences,button,a[href*='learning']").Length)
            .IsEqualTo(0);
    }

    [Test]
    public async Task ActualPrivatePanel_NamedTenantCascadeAdmitsCurrentPersonalOwner()
    {
        var fixture = ConfigurePanel("personal");
        var cut = _ctx.Render<CascadingValue<TenantContext>>(parameters => parameters
            .Add(component => component.Name, "TenantContext")
            .Add(component => component.Value, new TenantContext { TenantId = _tenantId })
            .AddChildContent<PrivateLearningPanel>(child =>
                child.Add(component => component.ActorId, fixture.ActorId)));
        await fixture.UserRequested.Task.WaitAsync(Timeout);
        await RenderAfter(cut, () => cut.FindAll(".private-learning__preferences").Count == 1,
            () => fixture.UserResponse.SetResult(fixture.User));

        await Assert.That(fixture.PrivateRequested.Task.IsCompleted).IsTrue();
        await Assert.That(fixture.Facade.Scope?.TenantId).IsEqualTo(_tenantId);
        await Assert.That(fixture.Facade.Scope?.ContextKey).IsEqualTo("self");
    }

    [Test]
    [Arguments("user")]
    [Arguments("tenant")]
    [Arguments("context")]
    public async Task ActualPrivatePanel_ForeignLearningResponseNeverBecomesVisible(string mismatch)
    {
        var fixture = ConfigurePanel("personal");
        switch (mismatch)
        {
            case "user":
                fixture.Context.UserId = Guid.CreateVersion7();
                break;
            case "tenant":
                fixture.Context.TenantId = Guid.CreateVersion7();
                break;
            case "context":
                fixture.Context.ContextKey = $"actor:{Guid.CreateVersion7():D}";
                break;
        }
        var cut = RenderPanel(fixture);
        await fixture.UserRequested.Task.WaitAsync(Timeout);
        await RenderAfter(cut, () => cut.FindAll("[role='alert']").Count == 1,
            () => fixture.UserResponse.SetResult(fixture.User));

        await Assert.That(fixture.PrivateRequested.Task.IsCompleted).IsTrue();
        await Assert.That(fixture.Facade.Context is null).IsTrue();
        await Assert.That(cut.FindAll(
            ".private-learning__preferences,.private-learning__actions,.private-learning__notice,a").Count)
            .IsEqualTo(0);
    }

    [Test]
    [Arguments("")]
    [Arguments("/acme")]
    public async Task ActualPrivatePanel_LearningDestinationUsesConfiguredApplicationBase(string pathBase)
    {
        var fixture = ConfigurePanel("personal", pathBase);
        var cut = RenderPanel(fixture);
        await fixture.UserRequested.Task.WaitAsync(Timeout);
        await RenderAfter(cut,
            () => cut.FindAll("[data-testid='private-learning-panel'][aria-busy='false'] a").Count == 1,
            () => fixture.UserResponse.SetResult(fixture.User));
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var link = cut.Find("[data-testid='private-learning-panel'] a");

        await Assert.That(navigation.ToAbsoluteUri(link.GetAttribute("href")!).AbsolutePath)
            .IsEqualTo(pathBase + "/learning");
        await cut.InvokeAsync(() => navigation.NavigateTo(link.GetAttribute("href")!));
        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo(pathBase + "/learning");
    }

    [Test]
    [Arguments(false, "rejected")]
    [Arguments(true, "rejected")]
    [Arguments(false, "conflict")]
    [Arguments(true, "conflict")]
    [Arguments(false, "settled")]
    [Arguments(true, "settled")]
    public async Task ActualPrivatePanel_ExplicitActionsDisplayOnlyConfirmedSettlement(
        bool acknowledge, string outcome)
    {
        var fixture = ConfigurePanel("personal");
        var cut = RenderPanel(fixture);
        await fixture.UserRequested.Task.WaitAsync(Timeout);
        await RenderAfter(cut, () => cut.FindAll(".private-learning__preferences").Count == 1,
            () => fixture.UserResponse.SetResult(fixture.User));
        var settled = PrivateContext(settled: true);
        if (!acknowledge)
        {
            settled.Preferences = new() { SuppressAutomaticTours = true, HideLearning = false, PinLearning = false };
            settled.Requirement = fixture.Context.Requirement;
        }
        var conflicting = PrivateContext();
        conflicting.LearnerRevision = settled.LearnerRevision;
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        var requested = Signal();
        var refreshRequested = Signal();
        var refresh = Signal<HalResourceOfLearningContextDto>();
        fixture.Transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                refreshRequested.TrySetResult();
                return refresh.Task;
            });
        ConfigureMutation(fixture.Transport, acknowledge, requested, response);
        var selector = acknowledge ? ".private-learning__notice button" : ".private-learning__actions button";
        var action = cut.FindAll(selector)[0].ClickAsync(new());
        await requested.Task.WaitAsync(Timeout);
        await Assert.That(fixture.Facade.LastMutationApplied is null).IsTrue();
        await Assert.That(fixture.Facade.IsSaving).IsTrue();
        response.SetResult(PrivateReceipt(outcome == "rejected" ? fixture.Context : settled));
        if (outcome != "rejected")
        {
            await refreshRequested.Task.WaitAsync(Timeout);
            await Assert.That(fixture.Facade.LastMutationApplied is null).IsTrue();
            refresh.SetResult(outcome == "conflict" ? conflicting : settled);
        }
        await action.WaitAsync(Timeout);

        await Assert.That(fixture.Facade.LastMutationApplied == true).IsEqualTo(outcome == "settled");
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(outcome == "settled" ? 0 : 1);
        await Assert.That(cut.FindAll(".private-learning__notice button").Count)
            .IsEqualTo(acknowledge && outcome == "settled" ? 0 : 1);
        await Assert.That(cut.FindAll(".private-learning__actions button[disabled]").Count)
            .IsEqualTo(outcome == "settled" ? 0 : 3);
        if (outcome != "settled")
            await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("account")]
    [Arguments("actor")]
    [Arguments("dispose")]
    public async Task ActualPrivatePanel_LatePreferenceReceiptCannotRestoreDepartedOwner(string change)
    {
        var fixture = ConfigurePanel("personal");
        var cut = RenderPanel(fixture);
        await fixture.UserRequested.Task.WaitAsync(Timeout);
        await RenderAfter(cut, () => cut.FindAll(".private-learning__preferences").Count == 1,
            () => fixture.UserResponse.SetResult(fixture.User));
        var replacementUser = Signal<HalResourceOfUserDto>();
        fixture.Users.GetCurrentUserResourceAsync().Returns(replacementUser.Task);
        var requested = Signal<CancellationToken>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        fixture.Transport.UpdateLearningPreferencesAsync(Arg.Any<LearningUpdatePreferencesRequest>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult(call.ArgAt<CancellationToken>(3));
                return response.Task;
            });
        var action = cut.Find(".private-learning__actions button").ClickAsync(new());
        var transportToken = await requested.Task.WaitAsync(Timeout);
        var cancelled = Signal();
        using var cancellationRegistration = transportToken.Register(() => cancelled.TrySetResult());

        if (change == "dispose")
        {
            await _ctx.DisposeComponentsAsync();
        }
        else
        {
            await RenderAfter(cut,
                () => cut.FindAll("[data-testid='private-learning-panel']").Count == 0,
                () =>
                {
                    if (change == "tenant")
                        cut.Render(parameters => parameters.Add(component => component.TenantId, Guid.CreateVersion7()));
                    else
                        InvalidateOwner(fixture.Facade, change);
                });
        }
        await cancelled.Task.WaitAsync(Timeout);
        await Assert.That(transportToken.IsCancellationRequested).IsTrue();
        var settled = PrivateContext(settled: true);
        settled.Preferences = new() { SuppressAutomaticTours = true, HideLearning = false, PinLearning = false };
        response.SetResult(PrivateReceipt(settled));
        await action.WaitAsync(Timeout);

        await Assert.That(fixture.Facade.Context is null).IsTrue();
        await Assert.That(fixture.Facade.Scope is null).IsTrue();
        await Assert.That(fixture.Facade.LastMutationApplied is null).IsTrue();
        if (change != "dispose")
            await Assert.That(cut.FindAll(
                "[data-testid='private-learning-panel'],.private-learning__preferences,[role='status']").Count)
                .IsEqualTo(0);
    }

    private PanelFixture ConfigurePanel(string audience, string pathBase = "")
    {
        var users = Substitute.For<IUserService>();
        var shellContext = Substitute.For<IUiShellContextService>();
        var userResponse = Signal<HalResourceOfUserDto>();
        var userRequested = Signal();
        users.GetCurrentUserResourceAsync().Returns(_ =>
        {
            userRequested.TrySetResult();
            return userResponse.Task;
        });
        var personalActor = Guid.CreateVersion7();
        var actor = audience is "personal" or "anonymous" or "instance"
            ? personalActor : Guid.CreateVersion7();
        var managed = new ManagedActorDto
        {
            ActorId = actor,
            ActorType = audience == "group" ? "Group" : "Organization"
        };
        shellContext.GetContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UiShellContextDto?>(new()
            {
                TenantId = audience == "foreign-tenant" ? Guid.CreateVersion7() : _tenantId,
                ManagedActors = audience == "unauthorized" ? [] : [managed]
            }));
        _ctx.Services.AddSingleton(users);
        _ctx.Services.AddSingleton(shellContext);
        _ctx.Services.AddSingleton<NavigationManager>(new HelpNavigation($"http://localhost{pathBase}/"));
        var transport = Substitute.For<ILearningClient>();
        var context = PrivateContext();
        context.ContextKey = audience is "personal" or "anonymous" or "instance" ? "self" : $"actor:{actor:D}";
        if (context.ContextKey != "self")
        {
            var links = context._links
                ?? throw new InvalidOperationException("Private learning fixture requires HAL links.");
            links.Remove("acknowledge-requirement");
        }
        var privateRequested = Signal();
        transport.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                privateRequested.TrySetResult();
                return Task.FromResult(context);
            });
        var facade = CreateFacade(transport);
        if (audience is "organization" or "group" or "unauthorized" or "foreign-tenant")
            _ctx.Services.GetRequiredService<UiShellState>().ReconcileActiveActors([managed], actor);
        if (audience == "anonymous")
            _ctx.SetAnonymousUser();
        return new(transport, users, facade, context, actor, audience == "instance" ? null : _tenantId,
            new HalResourceOfUserDto { Id = _userId, ActorId = personalActor },
            userResponse, userRequested, privateRequested);
    }

    private IRenderedComponent<PrivateLearningPanel> RenderPanel(PanelFixture fixture) =>
        _ctx.Render<PrivateLearningPanel>(parameters => parameters
            .Add(component => component.ActorId, fixture.ActorId)
            .Add(component => component.TenantId, fixture.TenantId));

    private static async Task RenderAfter<T>(
        IRenderedComponent<T> component, Func<bool> matches, Action trigger) where T : IComponent
    {
        var rendered = Signal();
        void OnMarkupUpdated(object? sender, EventArgs args)
        {
            if (matches())
                rendered.TrySetResult();
        }
        component.OnMarkupUpdated += OnMarkupUpdated;
        try
        {
            await component.InvokeAsync(trigger);
            await rendered.Task.WaitAsync(Timeout);
        }
        finally
        {
            component.OnMarkupUpdated -= OnMarkupUpdated;
        }
    }

    private sealed record PanelFixture(
        ILearningClient Transport, IUserService Users, LearningClientService Facade,
        HalResourceOfLearningContextDto Context,
        Guid ActorId, Guid? TenantId, HalResourceOfUserDto User,
        TaskCompletionSource<HalResourceOfUserDto> UserResponse,
        TaskCompletionSource UserRequested, TaskCompletionSource PrivateRequested);

    private sealed class HelpNavigation : NavigationManager
    {
        public HelpNavigation(string baseUri) => Initialize(baseUri, baseUri);

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }

    }

    private LearningClientService CreateFacade(ILearningClient transport)
    {
        _ctx.Services.AddSingleton(transport);
        _ctx.Services.AddScoped<LearningClientService>();
        _ctx.Services.AddScoped<ILearningClientService>(services =>
            services.GetRequiredService<LearningClientService>());
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        _ctx.Services.GetRequiredService<CurrentUserState>().NotifyUpdated(new UserDto { Id = _userId });
        var facade = _ctx.Services.GetRequiredService<LearningClientService>();
        facade.SetTenantContext(_tenantId);
        return facade;
    }

    private void InvalidateOwner(LearningClientService facade, string change)
    {
        switch (change)
        {
            case "tenant":
                facade.SetTenantContext(Guid.CreateVersion7());
                break;
            case "account":
                _ctx.SetAuthenticatedUser(Guid.CreateVersion7(), "Learner");
                break;
            case "actor":
                var actorId = Guid.CreateVersion7();
                _ctx.Services.GetRequiredService<UiShellState>().ReconcileActiveActors(
                    [new ManagedActorDto { ActorId = actorId }], actorId);
                break;
            case "dispose":
                facade.Dispose();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private static Task<bool> Mutate(
        LearningClientService facade, bool acknowledge, HalResourceOfLearningContextDto source) =>
        acknowledge
            ? facade.AcknowledgeRequirementAsync(source.Requirement)
            : facade.UpdatePreferencesAsync(true, true, true);

    private static void ConfigureMutation(
        ILearningClient transport, bool acknowledge, TaskCompletionSource requested,
        TaskCompletionSource<HalResourceOfLearningMutationReceiptDto> response,
        Action? onWrite = null)
    {
        if (acknowledge)
        {
            transport.LearningAcknowledgeRequirementAsync(Arg.Any<string>(),
                    Arg.Any<LearningAcknowledgeRequirementRequest>(),
                    cancellationToken: Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    onWrite?.Invoke();
                    requested.TrySetResult();
                    return response.Task;
                });
        }
        else
        {
            transport.UpdateLearningPreferencesAsync(Arg.Any<LearningUpdatePreferencesRequest>(),
                    cancellationToken: Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    onWrite?.Invoke();
                    requested.TrySetResult();
                    return response.Task;
                });
        }
    }

    private HalResourceOfLearningContextDto PrivateContext(bool settled = false) => new()
    {
        TenantId = _tenantId,
        UserId = _userId,
        ContextKey = "self",
        ScopeStamp = "private-help-self",
        ResourceRevision = 7,
        LearnerRevision = settled ? 3 : 2,
        Preferences = new()
        {
            SuppressAutomaticTours = settled, HideLearning = settled, PinLearning = settled
        },
        Requirement = new()
        {
            RequirementKey = "tenant-admin-orientation",
            MaterialVersion = 1,
            PolicySourceStamp = "private-help-policy-1",
            Status = settled ? LearningRequirementStatus.Satisfied : LearningRequirementStatus.Pending,
            AcknowledgedAtUtc = settled ? new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero) : null
        },
        Capabilities = new(),
        Tours = [],
        Lessons = [],
        OwnedPractices = [],
        _links = new Dictionary<string, HalLink>
        {
            ["preferences"] = new() { Href = "/api/learning/preferences", Method = "PUT" },
            ["acknowledge-requirement"] = new()
            {
                Href = "/api/learning/requirements/tenant-admin-orientation/acknowledgement", Method = "POST"
            }
        }
    };

    private static HalResourceOfLearningMutationReceiptDto PrivateReceipt(
        HalResourceOfLearningContextDto state) => new()
    {
        ScopeStamp = state.ScopeStamp,
        LearnerRevision = state.LearnerRevision,
        ResourceRevision = state.ResourceRevision,
        State = new()
        {
            TenantId = state.TenantId, UserId = state.UserId,
            Preferences = state.Preferences, Requirement = state.Requirement
        }
    };

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IEnumerable<IElement> VisibleControls(IEnumerable<IElement> elements) =>
        elements.Where(element =>
            element.Closest("[hidden],[aria-hidden='true'],[inert]") is null
            && (element.Closest("details:not([open])") is not { } disclosure
                || element.LocalName == "summary" && element.ParentElement == disclosure)
            && !element.HasAttribute("disabled")
            && element.GetAttribute("aria-disabled") != "true"
            && (element.LocalName is "button" or "summary"
                || !string.IsNullOrWhiteSpace(element.GetAttribute("href"))));

    private static IEnumerable<IElement> HelpActions(IEnumerable<IElement> elements) =>
        VisibleControls(elements).Where(element =>
            (element.GetAttribute("aria-label") ?? element.GetAttribute("title") ?? element.TextContent)
                .Contains("Help", StringComparison.OrdinalIgnoreCase));

    private sealed class StaticHelpHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(
            Type componentType, int? parentComponentId, IComponentActivator componentActivator,
            IComponentRenderMode renderMode)
        {
            if (renderMode is InteractiveAutoRenderMode)
                return componentActivator.CreateInstance(componentType);

            return base.ResolveComponentForRenderMode(
                componentType, parentComponentId, componentActivator, renderMode);
        }
    }
}
