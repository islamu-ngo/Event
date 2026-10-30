using System.Globalization;
using System.Security.Claims;
using Blazouter.Extensions;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages.Learning;
using Explore.Blazor.Client.Routing;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Learning;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.RenderTree;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class LearningPracticePresentationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly BlazorTestContext _ctx = new();
    private readonly ILearningClient _learning = Substitute.For<ILearningClient>();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly Guid _practiceId = Guid.CreateVersion7();
    private readonly TaskCompletionSource _contextRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Guid> _practiceRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LearningPracticePresentationTests()
    {
        _ctx.Services.AddBlazouter();
        _ctx.Services.AddScoped<AuthenticatedRouteGuard>();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddScoped<ILearningClientService, LearningClientService>();
        _ctx.Services.AddSingleton(_learning);
        _learning.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _contextRequested.TrySetResult();
                return Task.FromResult(Context());
            });
        _learning.GetLearningPracticeAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _practiceRequested.TrySetResult(call.ArgAt<Guid>(0));
                return Task.FromResult(Practice());
            });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments("")]
    [Arguments("/acme")]
    public async Task ActualRoutes_AuthenticatedHub_RendersOwnedPrivatePractice(string pathBase)
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        var navigation = ConfigureNavigation(pathBase);
        var router = await MountAsync(navigation, "/learning");
        using var rendered = Watch(router, () =>
            router.FindAll("[data-testid='learning-hub']").Count == 1 &&
            router.FindAll($"a[href='{pathBase}/learning/demo/{_practiceId}']").Count == 1);

        await router.InvokeAsync(() => navigation.NavigateAsync(pathBase + "/learning"));
        await rendered.Completion;

        await Assert.That(router.FindAll("[data-testid='learning-hub']").Count).IsEqualTo(1);
        await Assert.That(router.FindAll($"a[href='{pathBase}/learning/demo/{_practiceId}']").Count)
            .IsEqualTo(1);
        await Assert.That(router.FindAll("[data-learning-action='clear-practice']").Count).IsEqualTo(0);
        await Assert.That(router.FindAll("[data-learning-action='remove-space']").Count).IsEqualTo(0);
        await Assert.That(_contextRequested.Task.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("/acme")]
    public async Task ActualRoutes_AuthenticatedPractice_RendersSyntheticStateWithoutUnadvertisedActions(string pathBase)
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        var navigation = ConfigureNavigation(pathBase);
        var router = await MountAsync(navigation, "/learning/demo/:practiceId");
        using var rendered = Watch(router, () =>
            router.FindAll("[data-testid='learning-practice'][data-practice-id]").Count == 1);

        await router.InvokeAsync(() => navigation.NavigateAsync($"{pathBase}/learning/demo/{_practiceId}"));
        await rendered.Completion;

        var surface = router.Find("[data-testid='learning-practice']");
        await Assert.That(surface.GetAttribute("data-practice-id")).IsEqualTo(_practiceId.ToString());
        await Assert.That(surface.GetAttribute("data-recipe-key")).IsEqualTo("event-details");
        await Assert.That(surface.GetAttribute("data-practice-revision")).IsEqualTo("3");
        await Assert.That(surface.GetAttribute("data-simulation-step")).IsEqualTo("0");
        await Assert.That(router.FindAll("[data-learning-action='clear-practice']").Count).IsEqualTo(0);
        await Assert.That(router.FindAll("[data-learning-action='apply-practice-action']").Count).IsEqualTo(0);
        await Assert.That(await _practiceRequested.Task.WaitAsync(Timeout)).IsEqualTo(_practiceId);
    }

    [Test]
    [Arguments("", false)]
    [Arguments("", true)]
    [Arguments("/acme", false)]
    [Arguments("/acme", true)]
    public async Task ActualRoutes_AnonymousLearningEntry_RedirectsWithoutReadingPrivateState(
        string pathBase, bool practice)
    {
        _ctx.SetAnonymousUser();
        var navigation = ConfigureNavigation(pathBase);
        var template = practice ? "/learning/demo/:practiceId" : "/learning";
        var destination = practice ? $"/learning/demo/{_practiceId}" : "/learning";
        var router = await MountAsync(navigation, template);

        await router.InvokeAsync(() => navigation.NavigateAsync(pathBase + destination));
        var login = await navigation.WaitForLoginAsync();

        await Assert.That(new Uri(login).AbsolutePath.EndsWith("/login", StringComparison.Ordinal)).IsTrue();
        await Assert.That(new Uri(login).Query)
            .IsEqualTo("?returnUrl=" + Uri.EscapeDataString(pathBase + destination));
        await Assert.That(router.FindAll("[data-testid='learning-hub'],[data-testid='learning-practice']").Count)
            .IsEqualTo(0);
        await Assert.That(_contextRequested.Task.IsCompleted).IsFalse();
        await Assert.That(_practiceRequested.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task ActualRoutes_PendingAuthentication_DoesNotExposePrivateStateBeforeAuthenticatedRender()
    {
        var authentication = new PendingAuthenticationState();
        _ctx.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var response = new TaskCompletionSource<HalResourceOfLearningContextDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _learning.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _contextRequested.TrySetResult();
                return response.Task;
            });
        var navigation = ConfigureNavigation("");
        var router = await MountAsync(navigation, "/learning");
        using var rendered = Watch(router, () =>
            router.FindAll($"a[href='/learning/demo/{_practiceId}']").Count == 1);

        var authenticationRequested = authentication.AuthenticationRequested.Task;
        var navigationTask = router.InvokeAsync(() => navigation.NavigateAsync("/learning"));
        await authenticationRequested.WaitAsync(Timeout);

        await Assert.That(_contextRequested.Task.IsCompleted).IsFalse();
        await Assert.That(_practiceRequested.Task.IsCompleted).IsFalse();
        await Assert.That(router.FindAll($"a[href='/learning/demo/{_practiceId}']").Count).IsEqualTo(0);
        await Assert.That(router.FindAll("[data-learning-action]").Count).IsEqualTo(0);

        await router.InvokeAsync(() => authentication.Authenticate(_userId));
        await navigationTask.WaitAsync(Timeout);
        await _contextRequested.Task.WaitAsync(Timeout);
        await router.InvokeAsync(() => response.SetResult(Context()));
        await rendered.Completion;

        await Assert.That(router.FindAll($"a[href='/learning/demo/{_practiceId}']").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("event-details", "in-person", "preview-in-person")]
    [Arguments("sessions-program", "single-session", "preview-single-session")]
    [Arguments("ticket-capacity", "capacity-12", "preview-capacity-12")]
    [Arguments("attendees-team", "attendee", "preview-attendee")]
    [Arguments("publication-lifecycle", "keep-draft", "preview-draft")]
    public async Task MountedPractice_ServerSnapshotsAdvanceOnlyAfterReceiptAndFreshHal(
        string recipe, string choice, string resultKey)
    {
        var context = RecipeContext(recipe, choice);
        var practice = RecipePractice(recipe, "choose", 3, 2);
        var cut = await MountPracticeAsync(context, practice);
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var intents = new HashSet<Guid>();

        foreach (var (action, step, revision, learnerRevision) in new[]
        {
            (LearningPracticeAction.Choose, "confirm", 4L, 3L),
            (LearningPracticeAction.Confirm, "result", 5L, 4L),
            (LearningPracticeAction.Reset, "choose", 6L, 5L),
            (LearningPracticeAction.Clear, "cleared", 7L, 6L)
        })
        {
            var requested = Signal<LearningApplyPracticeActionRequest>();
            var response = Signal<HalResourceOfLearningMutationReceiptDto>();
            _learning.ApplyLearningPracticeActionAsync(
                    Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                    Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    requested.TrySetResult(call.ArgAt<LearningApplyPracticeActionRequest>(0));
                    return response.Task;
                });
            var nextPractice = RecipePractice(recipe, step, revision, learnerRevision, choice, resultKey);
            var nextContext = RecipeContext(recipe, choice, learnerRevision);
            if (action == LearningPracticeAction.Clear)
                nextContext.OwnedPractices = [];
            RespondWith(nextContext, nextPractice);
            using var rendered = Watch(cut, () => action == LearningPracticeAction.Clear
                ? cut.FindAll("[data-learning-state='cleared']").Count == 1
                : cut.FindAll($"[data-practice-revision='{revision}']").Count == 1);

            if (action == LearningPracticeAction.Clear)
                await cut.Find("[data-learning-action='clear-practice']").ClickAsync(new());
            var selector = action == LearningPracticeAction.Clear
                ? "#learning-clear-confirmation [data-learning-action='clear-practice']"
                : $"[data-practice-action='{action.ToString().ToLowerInvariant()}']";
            var click = cut.Find(selector).ClickAsync(new());
            var request = await requested.Task.WaitAsync(Timeout);
            await cut.InvokeAsync(() => { });

            await Assert.That(request.ExpectedScopeStamp).IsEqualTo(context.ScopeStamp);
            await Assert.That(request.ExpectedRevision).IsEqualTo(learnerRevision - 1);
            await Assert.That(request.ExpectedPracticeRevision).IsEqualTo(revision - 1);
            await Assert.That(request.PracticeId).IsEqualTo(_practiceId);
            await Assert.That(request.ContextKey).IsEqualTo("self");
            await Assert.That(request.RecipeKey).IsEqualTo(recipe);
            await Assert.That(request.MaterialVersion).IsEqualTo(1);
            await Assert.That(request.Action).IsEqualTo(action);
            await Assert.That(request.ChoiceKey).IsEqualTo(action == LearningPracticeAction.Choose ? choice : null);
            await Assert.That(request.IntentId == Guid.Empty).IsFalse();
            await Assert.That(intents.Add(request.IntentId)).IsTrue();
            await Assert.That(learning.Scope).IsEqualTo(new LearningClientScope(_tenantId, _userId, "self"));
            await Assert.That(cut.Find("[data-testid='learning-practice']")
                .GetAttribute("data-practice-revision"))
                .IsEqualTo((revision - 1).ToString(CultureInfo.InvariantCulture));
            await Assert.That(cut.Find("[data-testid='learning-practice']")
                .GetAttribute("aria-busy")).IsEqualTo("true");

            response.SetResult(Receipt(nextContext,
                action == LearningPracticeAction.Clear ? null : nextPractice));
            await click.WaitAsync(Timeout);
            await rendered.Completion;

            await Assert.That(learning.LastMutationApplied).IsEqualTo(true);
            if (action == LearningPracticeAction.Confirm)
                await Assert.That(cut.FindAll($"[data-simulation-result='{resultKey}']").Count).IsEqualTo(1);
            if (action == LearningPracticeAction.Reset)
                await Assert.That(cut.FindAll("[data-simulation-result]").Count).IsEqualTo(0);
            if (action == LearningPracticeAction.Clear)
            {
                await Assert.That(learning.Practice is null).IsTrue();
                await Assert.That(cut.FindAll("[data-testid='learning-practice']").Count).IsEqualTo(0);
            }
            context = nextContext;
        }
    }

    [Test]
    [Arguments("null")]
    [Arguments("missing-state")]
    [Arguments("wrong-scope")]
    [Arguments("unapplied")]
    [Arguments("unchanged-revision")]
    public async Task MountedPractice_UnconfirmedReceiptRetainsStateAndRequiresReload(string malformed)
    {
        var context = RecipeContext("event-details", "in-person");
        var practice = RecipePractice("event-details", "choose", 3, 2);
        var cut = await MountPracticeAsync(context, practice);
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var receipt = Receipt(RecipeContext("event-details", "in-person", 3),
            RecipePractice("event-details", "confirm", 4, 3, "in-person"));
        switch (malformed)
        {
            case "missing-state": receipt.State = null; break;
            case "wrong-scope": receipt.ScopeStamp = "different-selected-scope"; break;
            case "unapplied": receipt.Practice = null; break;
            case "unchanged-revision": receipt.LearnerRevision = 2; break;
        }
        _learning.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(malformed == "null" ? null! : receipt));
        using var rendered = Watch(cut, () => cut.FindAll("[role='alert']").Count > 0);

        await cut.Find("[data-practice-action='choose']").ClickAsync(new());
        await rendered.Completion;

        await Assert.That(learning.Failure).IsEqualTo(LearningClientFailure.SaveNotApplied);
        await Assert.That(learning.LastMutationApplied).IsEqualTo(false);
        await Assert.That(learning.CanSubmit).IsFalse();
        await Assert.That(ReferenceEquals(learning.Practice, practice)).IsTrue();
        await Assert.That(cut.Find("[data-testid='learning-practice']")
            .GetAttribute("data-practice-revision")).IsEqualTo("3");
        await Assert.That(cut.FindAll("[data-simulation-result]").Count).IsEqualTo(0);
        await Assert.That(cut.Find("[data-practice-action='choose']").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    [Arguments("removed")]
    [Arguments("absent")]
    [Arguments("foreign")]
    public async Task MountedPractice_UnavailableOwnershipNeverReadsOrResurrectsPractice(string unavailable)
    {
        var context = RecipeContext("event-details", "in-person");
        if (unavailable == "removed")
            context.SpaceStatus = LearningSpaceStatus.Removed;
        if (unavailable == "absent")
            context.OwnedPractices = [];
        if (unavailable == "foreign")
            context.OwnedPractices = [context.OwnedPractices!.Single() with
            {
                ContextKey = $"actor:{Guid.CreateVersion7():D}"
            }];
        var cut = await MountPracticeAsync(context, Practice(), available: false);
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var write = Signal<LearningApplyPracticeActionRequest>();
        _learning.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                write.TrySetResult(call.ArgAt<LearningApplyPracticeActionRequest>(0));
                return Task.FromResult(new HalResourceOfLearningMutationReceiptDto());
            });

        await Assert.That(_practiceRequested.Task.IsCompleted).IsFalse();
        await Assert.That(cut.FindAll("[data-testid='learning-practice'],[data-learning-action]").Count)
            .IsEqualTo(0);
        await Assert.That(await learning.ApplyPracticeActionAsync(LearningPracticeAction.Confirm)).IsFalse();
        await Assert.That(write.Task.IsCompleted).IsFalse();
        await Assert.That(learning.Practice is null).IsTrue();
    }

    [Test]
    public async Task MountedHub_MisleadingCapabilitiesDoNotGrantHalActions()
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        ConfigureNavigation("");
        var context = RecipeContext("event-details", "in-person");
        context._links = new Dictionary<string, HalLink>();
        var response = Signal<HalResourceOfLearningContextDto>();
        _learning.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(response.Task);
        var cut = _ctx.RenderMudComponent<LearningHub>();
        using var rendered = Watch(cut, () =>
            cut.FindAll($"a[href='/learning/demo/{_practiceId}']").Count == 1);

        response.SetResult(context);
        await rendered.Completion;

        await Assert.That(context.Capabilities.CanSimulate).IsEqualTo(true);
        await Assert.That(context.Capabilities.CanClearPractice).IsEqualTo(true);
        await Assert.That(context.Capabilities.CanManageSpace).IsEqualTo(true);
        await Assert.That(cut.FindAll("[data-learning-action]").Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MountedPractice_HalRelationsOwnAffordancesDespiteOppositeCapabilityFlags(bool advertised)
    {
        var practice = RecipePractice("event-details", "choose", 3, 2);
        practice.CanSimulate = !advertised;
        practice.CanClearPractice = !advertised;
        if (!advertised)
            practice._links = new Dictionary<string, HalLink>();
        var cut = await MountPracticeAsync(RecipeContext("event-details", "in-person"), practice);

        await Assert.That(cut.FindAll("[data-practice-action='choose']").Count).IsEqualTo(advertised ? 1 : 0);
        await Assert.That(cut.FindAll("[data-learning-action='clear-practice']").Count)
            .IsEqualTo(advertised ? 1 : 0);
        await Assert.That(cut.FindAll("[data-practice-action='confirm'],[data-practice-action='reset']").Count)
            .IsEqualTo(0);
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Arguments(true, false)]
    public async Task MountedPractice_UsesCanonicalSelectedActorAndServerPreviewEligibility(
        bool actorSelected, bool previewAvailable)
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        ConfigureNavigation("");
        var actor = new ManagedActorDto { ActorId = Guid.CreateVersion7(), ScopeId = _tenantId };
        var shell = _ctx.Services.GetRequiredService<UiShellState>();
        if (actorSelected)
            await Assert.That(shell.TrySetActiveActor(actor.ActorId!.Value, [actor])).IsTrue();
        var contextKey = actorSelected ? $"actor:{actor.ActorId:D}" : "self";
        var context = RecipeContext("event-details", "in-person");
        context.ContextKey = contextKey;
        context.OwnedPractices = [context.OwnedPractices!.Single() with
        {
            ContextKey = contextKey, PreviewAvailable = previewAvailable
        }];
        var practice = RecipePractice("event-details", "choose", 3, 2);
        practice.ContextKey = contextKey;
        string? requestedContext = null;
        var response = Signal<HalResourceOfLearningContextDto>();
        _learning.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requestedContext = call.ArgAt<string?>(0);
                return response.Task;
            });
        var cut = RenderPractice();
        using var rendered = Watch(cut, () => previewAvailable
            ? cut.FindAll("[data-testid='learning-practice']").Count == 1
            : cut.FindAll("[role='alert']").Count > 0);
        _learning.GetLearningPracticeAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _practiceRequested.TrySetResult(call.ArgAt<Guid>(0));
                return Task.FromResult(practice);
            });

        response.SetResult(context);
        await rendered.Completion;

        await Assert.That(requestedContext).IsEqualTo(contextKey);
        await Assert.That(_practiceRequested.Task.IsCompleted).IsEqualTo(previewAvailable);
        await Assert.That(cut.FindAll("[data-testid='learning-practice']").Count)
            .IsEqualTo(previewAvailable ? 1 : 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StaticHtmlRenderer_DoesNotReadOrRenderPrivateLearning(bool practice)
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        ConfigureNavigation("");
        await using var renderer = new StaticLearningHtmlRenderer(
            _ctx.Services, _ctx.Services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var componentType = practice ? typeof(LearningPractice) : typeof(LearningHub);
            var parameters = practice
                ? ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(LearningPractice.PracticeId)] = _practiceId
                })
                : ParameterView.Empty;
            var output = renderer.BeginRenderingComponent(componentType, parameters);
            await output.QuiescenceTask;
            return output.ToHtmlString();
        });
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        await Assert.That(document.QuerySelectorAll(
            "[data-testid='learning-practice'],[data-learning-action],a[href*='/learning/demo/']").Length)
            .IsEqualTo(0);
        await Assert.That(_contextRequested.Task.IsCompleted).IsFalse();
        await Assert.That(_practiceRequested.Task.IsCompleted).IsFalse();
    }

    private static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledMutation_AfterTransportEntryRequiresExplicitReload(bool transportReturnsReceipt)
    {
        var context = RecipeContext("event-details", "in-person");
        var practice = RecipePractice("event-details", "choose", 3, 2);
        var cut = await MountPracticeAsync(context, practice);
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var entered = Signal<CancellationToken>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        _learning.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                entered.TrySetResult(call.ArgAt<CancellationToken>(3));
                return response.Task;
            });
        using var cancellation = new CancellationTokenSource();
        var mutation = learning.ApplyPracticeActionAsync(
            LearningPracticeAction.Choose, "in-person", cancellation.Token);
        var transportToken = await entered.Task.WaitAsync(Timeout);

        cancellation.Cancel();
        if (transportReturnsReceipt)
        {
            var advancedContext = RecipeContext("event-details", "in-person", 3);
            var advancedPractice = RecipePractice("event-details", "confirm", 4, 3, "in-person");
            response.SetResult(Receipt(advancedContext, advancedPractice));
        }
        else
            response.SetException(new OperationCanceledException(transportToken));
        await Assert.That(await mutation.WaitAsync(Timeout)).IsFalse();
        await cut.InvokeAsync(() => { });

        await Assert.That(learning.CanSubmit).IsFalse();
        await Assert.That(learning.LastMutationApplied is not true).IsTrue();
        await Assert.That(cut.FindAll("[data-practice-action]")
            .All(element => element.HasAttribute("disabled"))).IsTrue();
    }

    [Test]
    public async Task MutationCompletion_ScopeChangeInsideChangedCannotReturnStaleSuccess()
    {
        await MountPracticeAsync(RecipeContext("event-details", "in-person"),
            RecipePractice("event-details", "choose", 3, 2));
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var context = RecipeContext("event-details", "in-person", 3);
        var practice = RecipePractice("event-details", "confirm", 4, 3, "in-person");
        RespondWith(context, practice);
        _learning.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Receipt(context, practice)));
        var invalidated = Signal<bool>();
        var nextTenant = new TenantContext { TenantId = Guid.CreateVersion7(), IsAuthenticated = true };
        void OnChanged()
        {
            if (learning.LastMutationApplied == true)
            {
                learning.SetTenantContext(nextTenant.TenantId);
                invalidated.TrySetResult(true);
            }
        }
        learning.Changed += OnChanged;
        try
        {
            bool applied = await learning.ApplyPracticeActionAsync(
                LearningPracticeAction.Choose, "in-person").WaitAsync(Timeout);

            await invalidated.Task.WaitAsync(Timeout);
            await Assert.That(applied).IsFalse();
            await Assert.That(learning.Practice is null).IsTrue();
            await Assert.That(learning.LastMutationApplied is null).IsTrue();
        }
        finally
        {
            learning.Changed -= OnChanged;
        }
    }

    [Test]
    [Arguments("context-read", "account")]
    [Arguments("context-read", "tenant")]
    [Arguments("context-read", "actor")]
    [Arguments("context-read", "dispose")]
    [Arguments("practice-read", "account")]
    [Arguments("practice-read", "tenant")]
    [Arguments("practice-read", "actor")]
    [Arguments("practice-read", "dispose")]
    [Arguments("mutation", "account")]
    [Arguments("mutation", "tenant")]
    [Arguments("mutation", "actor")]
    [Arguments("mutation", "dispose")]
    [Arguments("context-refresh", "account")]
    [Arguments("context-refresh", "tenant")]
    [Arguments("context-refresh", "actor")]
    [Arguments("context-refresh", "dispose")]
    [Arguments("practice-refresh", "account")]
    [Arguments("practice-refresh", "tenant")]
    [Arguments("practice-refresh", "actor")]
    [Arguments("practice-refresh", "dispose")]
    public async Task MountedPractice_IgnoredCancellationCannotPublishPreviousScope(
        string stage, string scopeChange)
    {
        var context = RecipeContext("event-details", "in-person");
        var practice = RecipePractice("event-details", "choose", 3, 2);
        var cut = await MountPracticeAsync(context, practice);
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var entered = Signal<CancellationToken>();
        var contextResponse = Signal<HalResourceOfLearningContextDto>();
        var practiceResponse = Signal<HalResourceOfLearningPracticeDto>();
        var mutationResponse = Signal<HalResourceOfLearningMutationReceiptDto>();
        var settledContext = RecipeContext("event-details", "in-person", 3);
        var settledPractice = RecipePractice("event-details", "confirm", 4, 3, "in-person");
        RespondWith(settledContext, settledPractice);
        _learning.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (stage == "mutation")
                {
                    entered.TrySetResult(call.ArgAt<CancellationToken>(3));
                    return mutationResponse.Task;
                }
                return Task.FromResult(Receipt(settledContext, settledPractice));
            });
        if (stage is "context-read" or "context-refresh")
            _learning.GetLearningContextAsync(
                    Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                    Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    entered.TrySetResult(call.ArgAt<CancellationToken>(4));
                    return contextResponse.Task;
                });
        if (stage is "practice-read" or "practice-refresh")
            _learning.GetLearningPracticeAsync(
                    Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    entered.TrySetResult(call.ArgAt<CancellationToken>(3));
                    return practiceResponse.Task;
                });

        using var cleared = Watch(cut, () =>
            cut.FindAll("[data-testid='learning-practice']").Count == 0);
        Task<bool>? mutation = null;
        Task operation;
        if (stage is "context-read" or "practice-read")
            operation = learning.LoadPracticeAsync(_practiceId);
        else
            operation = mutation = learning.ApplyPracticeActionAsync(LearningPracticeAction.Choose, "in-person");
        var transportToken = await entered.Task.WaitAsync(Timeout);
        var disposed = Signal();
        using var cancellationRegistration = transportToken.Register(() => disposed.TrySetResult());
        var replacement = RecipeContext("event-details", "in-person");
        replacement.OwnedPractices = [];
        var nextUser = new UserDto { Id = Guid.CreateVersion7() };
        var nextTenant = new TenantContext { TenantId = Guid.CreateVersion7(), IsAuthenticated = true };
        var nextActor = new ManagedActorDto { ActorId = Guid.CreateVersion7(), ScopeId = _tenantId };
        if (scopeChange == "account")
            replacement.UserId = nextUser.Id!.Value;
        if (scopeChange == "tenant")
            replacement.TenantId = nextTenant.TenantId!.Value;
        if (scopeChange == "actor")
            replacement.ContextKey = $"actor:{nextActor.ActorId:D}";
        RespondWith(replacement, settledPractice);

        await cut.InvokeAsync(() =>
        {
            switch (scopeChange)
            {
                case "account":
                    _ctx.Services.GetRequiredService<CurrentUserState>().NotifyUpdated(nextUser);
                    _ctx.SetAuthenticatedUser(nextUser.Id!.Value, "Replacement learner");
                    break;
                case "tenant": learning.SetTenantContext(nextTenant.TenantId); break;
                case "actor":
                    _ctx.Services.GetRequiredService<UiShellState>()
                        .TrySetActiveActor(nextActor.ActorId!.Value, [nextActor]);
                    break;
                case "dispose": break;
            }
        });
        if (scopeChange == "dispose")
            await _ctx.DisposeComponentsAsync();
        else
            await cleared.Completion;
        await disposed.Task.WaitAsync(Timeout);
        await Assert.That(transportToken.IsCancellationRequested).IsTrue();

        // These substitutes deliberately ignore the cancelled token and deliver old private data.
        contextResponse.TrySetResult(settledContext);
        practiceResponse.TrySetResult(settledPractice);
        mutationResponse.TrySetResult(Receipt(settledContext, settledPractice));
        await operation.WaitAsync(Timeout);
        if (mutation is not null)
            await Assert.That(await mutation).IsFalse();
        await Assert.That(learning.Practice is null).IsTrue();
        await Assert.That(learning.LastMutationApplied is null).IsTrue();
        await Assert.That(learning.OwnedPractices.Any(item => item.Id == _practiceId)).IsFalse();
        if (learning.Context is { } current)
        {
            await Assert.That(current.TenantId).IsEqualTo(replacement.TenantId);
            await Assert.That(current.UserId).IsEqualTo(replacement.UserId);
            await Assert.That(current.ContextKey).IsEqualTo(replacement.ContextKey);
        }
        if (scopeChange != "dispose")
            await Assert.That(cut.FindAll("[data-testid='learning-practice'],[data-simulation-result]").Count)
                .IsEqualTo(0);
        else
            await Assert.That(learning.Context is null).IsTrue();
    }

    private IRenderedComponent<CascadingValue<TenantContext>> RenderPractice() =>
        _ctx.RenderMudComponent<CascadingValue<TenantContext>>(parameters => parameters
            .Add(component => component.Name, "TenantContext")
            .Add(component => component.Value, new TenantContext
            {
                TenantId = _tenantId, IsAuthenticated = true
            })
            .AddChildContent<LearningPractice>(child =>
                child.Add(component => component.PracticeId, _practiceId)));

    private async Task<IRenderedComponent<CascadingValue<TenantContext>>> MountPracticeAsync(
        HalResourceOfLearningContextDto context, HalResourceOfLearningPracticeDto practice,
        bool available = true)
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        ConfigureNavigation("");
        _ctx.Services.GetRequiredService<CurrentUserState>().NotifyUpdated(new UserDto { Id = _userId });
        RespondWith(context, practice);
        var response = Signal<HalResourceOfLearningContextDto>();
        _learning.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _contextRequested.TrySetResult();
                return response.Task;
            });
        var cut = RenderPractice();
        using var rendered = Watch(cut, () => available
            ? cut.FindAll("[data-testid='learning-practice']").Count == 1
            : cut.FindAll("[role='alert']").Count > 0);
        response.SetResult(context);
        await rendered.Completion;
        RespondWith(context, practice);
        return cut;
    }

    private void RespondWith(HalResourceOfLearningContextDto context, HalResourceOfLearningPracticeDto practice)
    {
        _learning.GetLearningContextAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(context));
        _learning.GetLearningPracticeAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _practiceRequested.TrySetResult(call.ArgAt<Guid>(0));
                return Task.FromResult(practice);
            });
    }

    private HalResourceOfLearningContextDto RecipeContext(string recipe, string choice, long learnerRevision = 2)
    {
        var context = Context();
        context.LearnerRevision = learnerRevision;
        context.Lessons =
        [
            new()
            {
                PackKey = "learning-core", RecipeKey = recipe, MaterialVersion = 1,
                Choices = [new() { Key = choice }],
                Transitions =
                [
                    new() { FromStepKey = "choose", Action = LearningPracticeAction.Choose,
                        ChoiceKey = choice, ToStepKey = "confirm" },
                    new() { FromStepKey = "confirm", Action = LearningPracticeAction.Confirm, ToStepKey = "result" },
                    new() { FromStepKey = "result", Action = LearningPracticeAction.Reset, ToStepKey = "choose" }
                ]
            }
        ];
        context.OwnedPractices = [context.OwnedPractices!.Single() with
        {
            RecipeKey = recipe, Revision = learnerRevision + 1
        }];
        return context;
    }

    private HalResourceOfLearningPracticeDto RecipePractice(
        string recipe, string step, long revision, long learnerRevision,
        string? choice = null, string? resultKey = null)
    {
        var practice = Practice();
        practice.RecipeKey = recipe;
        practice.Revision = revision;
        practice.LearnerRevision = learnerRevision;
        practice.Simulation = new()
        {
            RecipeKey = recipe, MaterialVersion = 1, StepKey = step,
            Step = step == "result" ? 2 : step == "confirm" ? 1 : 0,
            ChoiceSlot = step is "confirm" or "result"
                ? LearningPracticeChoiceSlot.Primary : LearningPracticeChoiceSlot.None,
            Value = step is "confirm" or "result"
                ? recipe == "ticket-capacity" ? 12 : recipe == "publication-lifecycle" ? 0 : 1
                : 0,
            Status = step == "result" ? LearningSimulationStatus.Simulated
                : step == "confirm" ? LearningSimulationStatus.AwaitingConfirmation : LearningSimulationStatus.Choosing,
            ChoiceKey = step is "confirm" or "result" ? choice : null,
            IsConfirmed = step == "result",
            Receipt = step == "result" ? new()
            {
                RecipeKey = recipe, MaterialVersion = 1, ChoiceKey = choice!,
                ResultKey = resultKey!, IsSimulation = true, ProductionEffects = false
            } : null
        };
        practice._links = new Dictionary<string, HalLink>
        {
            [step == "choose" ? "simulate-choice" : step == "confirm" ? "simulate-confirm" : "reset-simulation"] =
                new() { Href = "/api/learning/practices/actions", Method = "POST" },
            ["clear-practice"] = new() { Href = "/api/learning/practices/actions", Method = "POST" }
        };
        return practice;
    }

    private HalResourceOfLearningMutationReceiptDto Receipt(
        HalResourceOfLearningContextDto context, HalResourceOfLearningPracticeDto? practice) => new()
    {
        Id = _practiceId, ScopeStamp = context.ScopeStamp,
        ResourceRevision = context.ResourceRevision, LearnerRevision = context.LearnerRevision,
        State = new() { TenantId = context.TenantId, UserId = context.UserId },
        Practice = practice is null ? null : new()
        {
            Id = practice.Id, ScopeStamp = practice.ScopeStamp, ContextKey = practice.ContextKey,
            RecipeKey = practice.RecipeKey, MaterialVersion = practice.MaterialVersion,
            Revision = practice.Revision, LearnerRevision = practice.LearnerRevision, Simulation = practice.Simulation
        },
        SimulationStatus = practice?.Simulation.Status ?? LearningSimulationStatus.Cleared
    };

    private RoutedNavigation ConfigureNavigation(string pathBase)
    {
        var navigation = new RoutedNavigation($"http://localhost{pathBase}/");
        _ctx.Services.AddSingleton<NavigationManager>(navigation);
        return navigation;
    }

    private async Task<IRenderedComponent<Blazouter.Components.Router>> MountAsync(
        RoutedNavigation navigation, string template)
    {
        var routes = Explore.Blazor.Client.Routes.CreateRouteCatalog()
            .Where(route => route.Path == "/learning" ||
                route.Path.StartsWith("/learning/", StringComparison.Ordinal))
            .ToList();
        RouteConfigurationPathBase.Apply(routes, navigation.BaseUri);
        var router = _ctx.RenderMudComponent<Blazouter.Components.Router>(parameters =>
            parameters.Add(component => component.Routes, routes));

        // Fail on the absent shipped route before an event wait can disguise it as a timeout.
        var expectedPath = new Uri(navigation.BaseUri).AbsolutePath.TrimEnd('/') + template;
        await Assert.That(routes.Select(route => route.Path)).Contains(expectedPath);
        var route = routes.Single(route => route.Path == expectedPath);
        await Assert.That(route.Guards).Contains(typeof(AuthenticatedRouteGuard));
        return router;
    }

    private HalResourceOfLearningContextDto Context() => new()
    {
        TenantId = _tenantId,
        UserId = _userId,
        ScopeStamp = "learning-presentation-self-1",
        ContextKey = "self",
        ResourceRevision = 7,
        LearnerRevision = 2,
        SpaceId = Guid.CreateVersion7(),
        SpaceStatus = LearningSpaceStatus.Active,
        Preferences = new() { HideLearning = false, PinLearning = false },
        Capabilities = new() { CanLearn = true, CanSimulate = true, CanClearPractice = true, CanManageSpace = true },
        Tours = [],
        Lessons = [],
        OwnedPractices =
        [
            new()
            {
                Id = _practiceId, ContextKey = "self", RecipeKey = "event-details",
                MaterialVersion = 1, Revision = 3, PreviewAvailable = true
            }
        ],
        _links = new Dictionary<string, HalLink>
        {
            ["self"] = new() { Href = "/api/learning/context", Method = "GET" }
        }
    };

    private HalResourceOfLearningPracticeDto Practice() => new()
    {
        Id = _practiceId,
        ScopeStamp = "learning-presentation-self-1",
        ContextKey = "self",
        RecipeKey = "event-details",
        MaterialVersion = 1,
        Revision = 3,
        LearnerRevision = 2,
        CanSimulate = true,
        CanClearPractice = true,
        Simulation = new()
        {
            RecipeKey = "event-details", MaterialVersion = 1, StepKey = "initial",
            Step = 0, Value = 0, IsConfirmed = false
        },
        _links = new Dictionary<string, HalLink>
        {
            ["self"] = new() { Href = $"/api/learning/practices/{_practiceId}", Method = "GET" }
        }
    };

    private sealed class PendingAuthenticationState : AuthenticationStateProvider
    {
        private readonly TaskCompletionSource<AuthenticationState> _state =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AuthenticationRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            AuthenticationRequested.TrySetResult();
            return _state.Task;
        }

        public void Authenticate(Guid userId)
        {
            _state.SetResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "LearningPresentationTests"))));
            NotifyAuthenticationStateChanged(_state.Task);
        }
    }

    private sealed class StaticLearningHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
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

    private static RenderSignal Watch<T>(IRenderedComponent<T> component, Func<bool> matches)
        where T : IComponent => new(
            handler => component.OnMarkupUpdated += handler,
            handler => component.OnMarkupUpdated -= handler, matches);

    private sealed class RenderSignal : IDisposable
    {
        private readonly Action<EventHandler> _unsubscribe;
        private readonly Func<bool> _matches;
        private readonly TaskCompletionSource _signal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            {
                _signal.TrySetResult();
            }
        }

        public void Dispose() => _unsubscribe(OnMarkupUpdated);
    }

    private sealed class RoutedNavigation : NavigationManager
    {
        public RoutedNavigation(string baseUri) => Initialize(baseUri, baseUri);

        public TaskCompletionSource<string> LoginRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<Exception> NavigationFailed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void NavigateToCore(string uri, bool forceLoad) => _ = RedirectAsync(uri);

        protected override void NavigateToCore(string uri, NavigationOptions options) => _ = RedirectAsync(uri);

        private async Task RedirectAsync(string uri)
        {
            try { await NavigateAsync(uri); }
            catch (Exception exception) { NavigationFailed.TrySetResult(exception); }
        }

        public async Task<string> WaitForLoginAsync()
        {
            var completed = await Task.WhenAny(LoginRequested.Task, NavigationFailed.Task).WaitAsync(Timeout);
            if (completed == NavigationFailed.Task)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(await NavigationFailed.Task).Throw();
            return await LoginRequested.Task;
        }

        protected override void SetNavigationLockState(bool value) { }

        protected override void HandleLocationChangingHandlerException(
            Exception exception, LocationChangingContext context)
            => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();

        public async Task NavigateAsync(string uri)
        {
            var destination = ToAbsoluteUri(uri).AbsoluteUri;
            if (await NotifyLocationChangingAsync(destination, null, false))
            {
                Uri = destination;
                NotifyLocationChanged(false);
                if (new Uri(destination).AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                {
                    LoginRequested.TrySetResult(destination);
                }
            }
        }
    }
}
