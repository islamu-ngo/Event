using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Blazouter.Extensions;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Learning;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Components.Shell.AiAssistant;
using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Explore.Blazor.Client.Pages.Learning;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Services.Learning;
using Explore.Blazor.Client.Services.Shell;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class DemoAiConversationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly BlazorTestContext _ctx = new();
    private readonly ILearningClient _learningClient = Substitute.For<ILearningClient>();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly Guid _practiceId = Guid.CreateVersion7();
    private HttpClient? _demoHttpClient;

    public void Dispose()
    {
        try
        {
            _ctx.Dispose();
        }
        finally
        {
            _demoHttpClient?.Dispose();
        }
    }

    [Test]
    public async Task Result_WithoutHostDestination_DoesNotEmitNavigationHref()
    {
        var action = CreateExecutedAction();
        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Action, action));

        await Assert.That(cut.FindAll("[data-testid='ai-action-result']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-action-result'] a[href]")).IsEmpty();
    }

    [Test]
    public async Task Result_WithoutHostDestination_DefaultClickDoesNotLeaveLearning()
    {
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/learning");
        var originalUri = navigation.Uri;
        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Action, CreateExecutedAction()));

        await Assert.That(cut.FindAll("[data-testid='ai-action-result']").Count).IsEqualTo(1);
        var linkCount = cut.FindAll("[data-testid='ai-action-result'] a").Count;
        for (var index = 0; index < linkCount; index++)
        {
            await cut.FindAll("[data-testid='ai-action-result'] a")[index]
                .ClickAsync(new MouseEventArgs());

            await Assert.That(navigation.Uri).IsEqualTo(originalUri);
        }
    }

    [Test]
    [Arguments("input[type='file']")]
    [Arguments("[data-testid='ai-rail-actor-selector']")]
    [Arguments("button[aria-label='Add references']")]
    [Arguments("button[aria-label='AI capabilities']")]
    public async Task Composer_WithoutHostMode_DoesNotExposeLiveControl(string selector)
    {
        var cut = _ctx.RenderMudComponent<AiConversationComposer>();

        await Assert.That(cut.FindAll("[data-testid='ai-rail-prompt']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(selector)).IsEmpty();
    }

    [Test]
    public async Task LiveRail_ConfirmDisplayedAction_ReachesHttpWithDisplayedVersionAndIdempotency()
    {
        var conversationId = Guid.CreateVersion7();
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Version = new string('a', 64),
            Kind = "CreateEventDraft",
            Status = "Proposed"
        };
        GeneratedHalLinkTestHelper.SetLinks(action,
            ("confirm-action", "/confirm", "POST"),
            ("reject-action", "/reject", "POST"));
        var conversation = new HalResourceOfAiConversationDto
        {
            Id = conversationId,
            ProposedActions = [action]
        };
        var conversationState = new AiAssistantConversationState();
        conversationState.SelectConversation(conversation);
        var shellState = new AiAssistantState();
        shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true,
            allowAnonymousAccess: false, isAuthenticated: true);
        shellState.Open();
        var expectedPath =
            $"/api/ai/assistant/conversations/{conversationId}/proposed-actions/{action.Id}/confirm";
        using var handler = new LiveConfirmationHttpHandler(expectedPath);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://event.test/") };
        _ctx.Services.AddSingleton<IAiAssistantClientService>(new AiAssistantClientService(
            new AiAssistantClient(http), NullLogger<AiAssistantClientService>.Instance));
        _ctx.Services.AddSingleton(shellState);
        _ctx.Services.AddSingleton(conversationState);
        _ctx.Services.AddSingleton<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddSingleton<WorkspaceRouteClassifier>();
        _ctx.Services.AddSingleton<UiShellState>();
        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        await Assert.That(cut.FindComponent<AiConversationTimeline>().Instance.Conversation)
            .IsSameReferenceAs(conversation);
        await Assert.That(cut.FindComponent<AiProposedActionCard>().Instance.Action)
            .IsSameReferenceAs(action);
        var confirmation = handler.Confirmation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cut.Find("[data-testid='ai-rail-confirm-action']").ClickAsync(new MouseEventArgs());
        var sent = await confirmation;

        await Assert.That(sent.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(sent.Path).IsEqualTo(expectedPath);
        using var body = JsonDocument.Parse(sent.Body);
        await Assert.That(body.RootElement.GetProperty("expectedVersion").GetString())
            .IsEqualTo(action.Version);
        const string prefix = "blazor-ai-confirm-";
        await Assert.That(sent.IdempotencyKey).IsNotNull();
        await Assert.That(sent.IdempotencyKey!).StartsWith(prefix);
        await Assert.That(Guid.TryParse(sent.IdempotencyKey![prefix.Length..], out _)).IsTrue();
        await Assert.That(conversationState.ErrorMessage).IsNotNull();
        await Assert.That(handler.ConfirmationCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("accept-example", "example-accepted")]
    [Arguments("reject-example", "example-rejected")]
    public async Task MountedDemo_AuthoredReviewUsesLearningReceiptsAndPrivateResultNavigation(
        string choice, string resultKey)
    {
        using var outbound = new LiveConfirmationHttpHandler(null, allowReads: false);
        var context = Context();
        var practice = Practice("choose", 3, 2);
        var cut = await MountDemoPageAsync(context, practice, outbound);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var intents = new HashSet<Guid>();

        await Assert.That(cut.FindComponent<DemoAiConversation>().Instance.Practice)
            .IsSameReferenceAs(practice);
        await Assert.That(cut.FindComponent<AiConversationComposer>().Instance.Mode)
            .IsEqualTo(AiPresentationMode.Simulation);
        await Assert.That(cut.FindAll("[data-testid='shell-ai-rail']")).IsEmpty();
        await Assert.That(cut.FindAll("input[type='file'],[data-testid='ai-rail-actor-selector']"))
            .IsEmpty();

        foreach (var action in new[] { LearningPracticeAction.Choose, LearningPracticeAction.Confirm })
        {
            var nextRevision = practice.Revision + 1;
            var nextLearnerRevision = context.LearnerRevision + 1;
            var nextContext = Context(nextLearnerRevision);
            var nextPractice = Practice(action == LearningPracticeAction.Choose ? "confirm" : "result",
                nextRevision, nextLearnerRevision, choice);
            var requested = Signal<LearningApplyPracticeActionRequest>();
            var response = Signal<HalResourceOfLearningMutationReceiptDto>();
            _learningClient.ApplyLearningPracticeActionAsync(
                    Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                    Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    requested.TrySetResult(call.ArgAt<LearningApplyPracticeActionRequest>(0));
                    return response.Task;
                });
            RespondWith(nextContext, nextPractice);
            using var rendered = Watch(cut, () => cut.FindAll(
                $"[data-practice-revision='{nextRevision}']").Count == 1);
            var selector = action == LearningPracticeAction.Choose && choice == "reject-example"
                ? "[data-testid='ai-rail-reject-action']"
                : "[data-testid='ai-rail-confirm-action']";

            var click = cut.Find(selector).ClickAsync(new MouseEventArgs());
            var sent = await requested.Task.WaitAsync(Timeout);

            await Assert.That(sent.Action).IsEqualTo(action);
            await Assert.That(sent.ChoiceKey).IsEqualTo(
                action == LearningPracticeAction.Choose ? choice : null);
            await Assert.That(sent.PracticeId).IsEqualTo(_practiceId);
            await Assert.That(sent.RecipeKey).IsEqualTo("ai-example-review");
            await Assert.That(sent.MaterialVersion).IsEqualTo(1);
            await Assert.That(sent.ContextKey).IsEqualTo("self");
            await Assert.That(sent.ExpectedScopeStamp).IsEqualTo(context.ScopeStamp);
            await Assert.That(sent.ExpectedRevision).IsEqualTo(context.LearnerRevision);
            await Assert.That(sent.ExpectedPracticeRevision).IsEqualTo(practice.Revision);
            await Assert.That(sent.IntentId == Guid.Empty).IsFalse();
            await Assert.That(intents.Add(sent.IntentId)).IsTrue();
            await Assert.That(cut.Find("[data-testid='learning-practice']")
                .GetAttribute("data-practice-revision"))
                .IsEqualTo(practice.Revision.ToString(CultureInfo.InvariantCulture));
            await Assert.That(cut.FindAll("[data-testid='ai-action-result']")).IsEmpty();

            response.SetResult(Receipt(nextContext, nextPractice));
            await click.WaitAsync(Timeout);
            await rendered.Completion;
            context = nextContext;
            practice = nextPractice;
        }

        var timeline = cut.FindComponent<AiConversationTimeline>();
        var proposal = cut.FindComponent<AiProposedActionCard>();
        var result = cut.FindComponent<AiActionResultCard>();
        await Assert.That(timeline.Instance.Mode).IsEqualTo(AiPresentationMode.Simulation);
        await Assert.That(proposal.Instance.Action).IsSameReferenceAs(
            timeline.Instance.Conversation!.ProposedActions!.Single());
        await Assert.That(result.Instance.Action).IsSameReferenceAs(proposal.Instance.Action);
        await Assert.That(result.Instance.Action!.ResultResourceId is null).IsTrue();
        await Assert.That(AiAssistantConversationState.CanConfirm(proposal.Instance.Action!)).IsFalse();
        await Assert.That(AiAssistantConversationState.CanReject(proposal.Instance.Action!)).IsFalse();
        var expectedDestination = $"/learning/demo/{_practiceId}#demo-ai-result-{_practiceId:N}";
        await Assert.That(result.Instance.Presentation!.Destination)
            .IsEqualTo(expectedDestination);
        await Assert.That(_ctx.Services.GetRequiredService<ILearningClientService>()
            .Practice!.Simulation.Receipt!.ResultKey).IsEqualTo(resultKey);
        await Assert.That(cut.FindAll("[data-testid='ai-rail-confirm-action']")).IsEmpty();
        await Assert.That(cut.FindAll("a[href^='/events'],a[href^='/ai'],a[href^='/studio']"))
            .IsEmpty();
        var link = result.Find("a[href]");
        await Assert.That(link.GetAttribute("href")).IsEqualTo(expectedDestination);
        await link.ClickAsync(new MouseEventArgs());
        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo(new Uri(originalUri).AbsolutePath);
        await Assert.That(new Uri(navigation.Uri).Fragment).IsEqualTo($"#demo-ai-result-{_practiceId:N}");
        await Assert.That(outbound.Requests).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MountedDemo_PathBaseResultHrefAndDefaultClickStayInPrivatePractice(bool click)
    {
        using var outbound = new LiveConfirmationHttpHandler(null, allowReads: false);
        var navigation = new PathBaseNavigation();
        _ctx.Services.AddSingleton<NavigationManager>(navigation);
        var cut = await MountDemoPageAsync(Context(4),
            Practice("result", 5, 4, "accept-example"), outbound);
        await Assert.That(cut.FindComponent<DemoAiConversation>().Instance.Practice)
            .IsSameReferenceAs(_ctx.Services.GetRequiredService<ILearningClientService>().Practice);
        var result = cut.FindComponent<AiActionResultCard>();
        var link = result.Find("[data-testid='ai-result-event-link']");
        var expectedPath = $"/acme/learning/demo/{_practiceId}";
        var fragment = $"#demo-ai-result-{_practiceId:N}";

        if (click)
            await link.ClickAsync(new MouseEventArgs());

        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo(expectedPath);
        if (click)
            await Assert.That(new Uri(navigation.Uri).Fragment).IsEqualTo(fragment);
        await Assert.That(link.GetAttribute("href")).IsEqualTo(expectedPath + fragment);
        await Assert.That(result.Instance.Presentation!.Destination).IsEqualTo(expectedPath + fragment);
        await Assert.That(outbound.Requests).IsEmpty();
    }

    [Test]
    public async Task MountedDemo_UntrustedPromptIsEphemeralEscapedAndNeverSentToNormalAi()
    {
        using var outbound = new LiveConfirmationHttpHandler(null, allowReads: false);
        var cut = await MountDemoPageAsync(Context(), Practice("choose", 3, 2), outbound);
        const string input = "<img src='https://untrusted.invalid/x' onerror='alert(1)'><script>alert(2)</script>";
        var writes = new List<LearningApplyPracticeActionRequest>();
        _learningClient.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                writes.Add(call.ArgAt<LearningApplyPracticeActionRequest>(0));
                return Task.FromException<HalResourceOfLearningMutationReceiptDto>(
                    new InvalidOperationException("Free text is not a learning mutation."));
            });

        var status = cut.Find("[data-testid='demo-ai-conversation'] [role='status']").TextContent;
        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(new ChangeEventArgs { Value = input });
        await Assert.That(cut.FindComponent<AiConversationComposer>().Instance.PromptText).IsEqualTo(input);
        await Assert.That(cut.Find("[data-testid='ai-rail-prompt']").GetAttribute("value")).IsEqualTo(input);
        await Assert.That(cut.FindAll("script,img[src='https://untrusted.invalid/x'],[onerror]"))
            .IsEmpty();

        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindComponent<AiConversationComposer>().Instance.PromptText).IsEqualTo("");
        await Assert.That(cut.Find("[data-testid='demo-ai-conversation'] [role='status']").TextContent == status)
            .IsFalse();
        await Assert.That(cut.FindAll("[data-testid='ai-rail-confirm-action']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-rail-reject-action']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-rail-message']")
            .Any(message => message.TextContent.Contains(input, StringComparison.Ordinal))).IsFalse();
        await Assert.That(writes).IsEmpty();
        await Assert.That(outbound.Requests).IsEmpty();

        using var cleared = Watch(cut, () =>
            cut.FindAll("[data-testid='learning-practice']").Count == 0);
        await cut.InvokeAsync(() => _ctx.Services.GetRequiredService<ILearningClientService>()
            .SetTenantContext(Guid.CreateVersion7()));
        await cleared.Completion;

        await Assert.That(cut.FindAll("[data-testid='ai-rail-message']")
            .Any(message => message.TextContent.Contains(input, StringComparison.Ordinal))).IsFalse();
        await Assert.That(outbound.Requests).IsEmpty();
    }

    [Test]
    [Arguments("accept-example")]
    [Arguments("reject-example")]
    public async Task MountedDemo_ComposerSelectsOnlyAuthoredLearningChoice(string choice)
    {
        using var outbound = new LiveConfirmationHttpHandler(null, allowReads: false);
        var cut = await MountDemoPageAsync(Context(), Practice("choose", 3, 2), outbound);
        var nextContext = Context(3);
        var nextPractice = Practice("confirm", 4, 3, choice);
        var requested = Signal<LearningApplyPracticeActionRequest>();
        _learningClient.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested.TrySetResult(call.ArgAt<LearningApplyPracticeActionRequest>(0));
                return Task.FromResult(Receipt(nextContext, nextPractice));
            });
        RespondWith(nextContext, nextPractice);
        using var rendered = Watch(cut, () =>
            cut.FindAll("[data-practice-revision='4']").Count == 1);

        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(new ChangeEventArgs { Value = choice });
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());
        var sent = await requested.Task.WaitAsync(Timeout);
        await rendered.Completion;

        await Assert.That(sent.Action).IsEqualTo(LearningPracticeAction.Choose);
        await Assert.That(sent.ChoiceKey).IsEqualTo(choice);
        await Assert.That(sent.ExpectedPracticeRevision).IsEqualTo(3);
        await Assert.That(sent.ContextKey).IsEqualTo("self");
        await Assert.That(sent.ExpectedScopeStamp).IsEqualTo("demo-self-scope");
        await Assert.That(cut.FindComponent<AiConversationComposer>().Instance.PromptText).IsEqualTo("");
        await Assert.That(cut.FindAll("[data-testid='ai-rail-confirm-action']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-action-result']")).IsEmpty();
        await Assert.That(outbound.Requests).IsEmpty();
    }

    [Test]
    public async Task MountedDemo_CapabilityFlagsCannotReplaceAbsentLearningHal()
    {
        using var outbound = new LiveConfirmationHttpHandler(null, allowReads: false);
        var practice = Practice("choose", 3, 2);
        practice._links = new Dictionary<string, HalLink>();
        var cut = await MountDemoPageAsync(Context(), practice, outbound);
        var writes = new List<LearningApplyPracticeActionRequest>();
        _learningClient.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                writes.Add(call.ArgAt<LearningApplyPracticeActionRequest>(0));
                return Task.FromException<HalResourceOfLearningMutationReceiptDto>(
                    new InvalidOperationException("No current learning HAL grant."));
            });

        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(
            new ChangeEventArgs { Value = "accept-example" });
        var proposal = cut.FindComponent<AiProposedActionCard>().Instance;
        await cut.InvokeAsync(() => proposal.OnConfirm.InvokeAsync(proposal.Action!));
        await cut.InvokeAsync(() => proposal.OnReject.InvokeAsync(proposal.Action!));

        await Assert.That(cut.FindComponent<AiConversationComposer>().Instance.IsSendDisabled).IsTrue();
        await Assert.That(cut.FindAll(
            "[data-testid='ai-rail-confirm-action'],[data-testid='ai-rail-reject-action']")).IsEmpty();
        await Assert.That(writes).IsEmpty();
        await Assert.That(outbound.Requests).IsEmpty();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("actor")]
    [Arguments("dispose")]
    public async Task MountedDemo_RetainedResultCallbackCannotNavigateAfterScopeOrDisposal(string scopeChange)
    {
        using var outbound = new LiveConfirmationHttpHandler(null, allowReads: false);
        var cut = await MountDemoPageAsync(Context(4),
            Practice("result", 5, 4, "accept-example"), outbound);
        var result = cut.FindComponent<AiActionResultCard>().Instance;
        var retainedCallback = result.OnOpenResult;
        var dispatcher = _ctx.Renderer.Dispatcher;
        var displayedResult = result.Presentation!;
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var replacement = Context(4);
        replacement.OwnedPractices = [];
        var actor = new ManagedActorDto { ActorId = Guid.CreateVersion7(), ScopeId = _tenantId };
        if (scopeChange == "tenant")
            replacement.TenantId = Guid.CreateVersion7();
        if (scopeChange == "actor")
            replacement.ContextKey = $"actor:{actor.ActorId:D}";
        RespondWith(replacement, Practice("result", 5, 4, "accept-example"));
        using var cleared = Watch(cut, () =>
            cut.FindAll("[data-testid='learning-practice']").Count == 0);

        if (scopeChange == "dispose")
            await _ctx.DisposeComponentsAsync();
        else
        {
            await cut.InvokeAsync(() =>
            {
                if (scopeChange == "tenant")
                    learning.SetTenantContext(replacement.TenantId);
                else
                    _ctx.Services.GetRequiredService<UiShellState>()
                        .TrySetActiveActor(actor.ActorId!.Value, [actor]);
            });
            await cleared.Completion;
        }
        await dispatcher.InvokeAsync(() => retainedCallback.InvokeAsync(displayedResult))
            .WaitAsync(Timeout);

        await Assert.That(navigation.Uri).IsEqualTo(originalUri);
        await Assert.That(learning.Practice is null).IsTrue();
        await Assert.That(outbound.Requests).IsEmpty();
    }

    [Test]
    [Arguments("tenant", "confirm")]
    [Arguments("actor", "confirm")]
    [Arguments("dispose", "confirm")]
    [Arguments("tenant", "result-read")]
    [Arguments("actor", "result-read")]
    [Arguments("dispose", "result-read")]
    public async Task MountedDemo_DelayedConfirmationCannotPublishResultAcrossScopeOrDisposal(
        string scopeChange, string stage)
    {
        using var outbound = new LiveConfirmationHttpHandler(null, allowReads: false);
        var cut = await MountDemoPageAsync(Context(3),
            Practice("confirm", 4, 3, "accept-example"), outbound);
        var learning = _ctx.Services.GetRequiredService<ILearningClientService>();
        var settledContext = Context(4);
        var settledPractice = Practice("result", 5, 4, "accept-example");
        var entered = Signal<CancellationToken>();
        var response = Signal<HalResourceOfLearningMutationReceiptDto>();
        var resultResponse = Signal<HalResourceOfLearningPracticeDto>();
        var requests = new List<LearningApplyPracticeActionRequest>();
        RespondWith(settledContext, settledPractice);
        _learningClient.ApplyLearningPracticeActionAsync(
                Arg.Any<LearningApplyPracticeActionRequest>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requests.Add(call.ArgAt<LearningApplyPracticeActionRequest>(0));
                if (stage == "confirm")
                {
                    entered.TrySetResult(call.ArgAt<CancellationToken>(3));
                    return response.Task;
                }
                return Task.FromResult(Receipt(settledContext, settledPractice));
            });
        if (stage == "result-read")
        {
            _learningClient.GetLearningPracticeAsync(
                    Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    entered.TrySetResult(call.ArgAt<CancellationToken>(3));
                    return resultResponse.Task;
                });
        }
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var displayedProposal = cut.FindComponent<AiProposedActionCard>().Instance;
        var retainedConfirmation = displayedProposal.OnConfirm;
        var dispatcher = _ctx.Renderer.Dispatcher;
        var displayedAction = displayedProposal.Action!;
        var click = cut.Find("[data-testid='ai-rail-confirm-action']").ClickAsync(new MouseEventArgs());
        var token = await entered.Task.WaitAsync(Timeout);
        var cancelled = Signal();
        using var registration = token.Register(() => cancelled.TrySetResult());
        using var cleared = Watch(cut, () =>
            cut.FindAll("[data-testid='learning-practice']").Count == 0);
        var replacement = Context(4);
        replacement.OwnedPractices = [];
        var actor = new ManagedActorDto { ActorId = Guid.CreateVersion7(), ScopeId = _tenantId };
        if (scopeChange == "tenant")
            replacement.TenantId = Guid.CreateVersion7();
        if (scopeChange == "actor")
            replacement.ContextKey = $"actor:{actor.ActorId:D}";
        RespondWith(replacement, settledPractice);

        if (scopeChange == "dispose")
            await _ctx.DisposeComponentsAsync();
        else
        {
            await cut.InvokeAsync(() =>
            {
                if (scopeChange == "tenant")
                    learning.SetTenantContext(replacement.TenantId);
                else
                    _ctx.Services.GetRequiredService<UiShellState>()
                        .TrySetActiveActor(actor.ActorId!.Value, [actor]);
            });
            await cleared.Completion;
        }
        await cancelled.Task.WaitAsync(Timeout);
        // The transport intentionally ignores cancellation and delivers the old owner's data.
        response.TrySetResult(Receipt(settledContext, settledPractice));
        resultResponse.TrySetResult(settledPractice);
        await click.WaitAsync(Timeout);
        await dispatcher.InvokeAsync(() => retainedConfirmation.InvokeAsync(displayedAction))
            .WaitAsync(Timeout);

        await Assert.That(token.IsCancellationRequested).IsTrue();
        await Assert.That(learning.Practice is null).IsTrue();
        await Assert.That(learning.LastMutationApplied is null).IsTrue();
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(requests[0].ExpectedScopeStamp).IsEqualTo("demo-self-scope");
        await Assert.That(requests[0].ContextKey).IsEqualTo("self");
        await Assert.That(navigation.Uri).IsEqualTo(originalUri);
        if (scopeChange != "dispose")
            await Assert.That(cut.FindAll(
                "[data-testid='ai-action-result'],[data-testid='ai-rail-confirm-action']")).IsEmpty();
        await Assert.That(outbound.Requests).IsEmpty();
    }

    private async Task<IRenderedComponent<CascadingValue<TenantContext>>> MountDemoPageAsync(
        HalResourceOfLearningContextDto context, HalResourceOfLearningPracticeDto practice,
        LiveConfirmationHttpHandler outbound)
    {
        _ctx.SetAuthenticatedUser(_userId, "Learner");
        _ctx.Services.AddBlazouter();
        _ctx.Services.AddSingleton<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddSingleton<WorkspaceRouteClassifier>();
        _ctx.Services.AddSingleton<UiShellState>();
        _ctx.Services.AddSingleton(_learningClient);
        _ctx.Services.AddScoped<ILearningClientService, LearningClientService>();
        _demoHttpClient = new HttpClient(outbound, disposeHandler: false)
        {
            BaseAddress = new Uri("https://event.test/")
        };
        _ctx.Services.AddSingleton(_demoHttpClient);
        _ctx.Services.AddSingleton<IAiAssistantClientService>(new AiAssistantClientService(
            new AiAssistantClient(_demoHttpClient), NullLogger<AiAssistantClientService>.Instance));
        _ctx.Services.AddSingleton(new AiAssistantState());
        _ctx.Services.AddSingleton(new AiAssistantConversationState());
        _ctx.Services.GetRequiredService<CurrentUserState>().NotifyUpdated(new UserDto { Id = _userId });
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"learning/demo/{_practiceId}");
        RespondWith(context, practice);
        var initialContext = Signal<HalResourceOfLearningContextDto>();
        _learningClient.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(initialContext.Task);
        var cut = _ctx.RenderMudComponent<CascadingValue<TenantContext>>(parameters => parameters
            .Add(component => component.Name, "TenantContext")
            .Add(component => component.Value, new TenantContext
            {
                TenantId = _tenantId,
                IsAuthenticated = true
            })
            .AddChildContent<LearningPractice>(child => child
                .Add(component => component.PracticeId, _practiceId)));
        using var rendered = Watch(cut, () =>
            cut.FindAll("[data-testid='learning-practice']").Count == 1);
        initialContext.SetResult(context);
        await rendered.Completion;
        RespondWith(context, practice);
        return cut;
    }

    private void RespondWith(HalResourceOfLearningContextDto context, HalResourceOfLearningPracticeDto practice)
    {
        _learningClient.GetLearningContextAsync(
                Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(context));
        _learningClient.GetLearningPracticeAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(practice));
    }

    private HalResourceOfLearningContextDto Context(long learnerRevision = 2) => new()
    {
        TenantId = _tenantId,
        UserId = _userId,
        ContextKey = "self",
        ScopeStamp = "demo-self-scope",
        ResourceRevision = 7,
        LearnerRevision = learnerRevision,
        SpaceId = Guid.CreateVersion7(),
        SpaceStatus = LearningSpaceStatus.Active,
        Preferences = new(),
        Capabilities = new() { CanLearn = true, CanSimulate = true },
        Tours = [],
        Lessons =
        [
            new()
            {
                PackKey = "learning-core", RecipeKey = "ai-example-review", MaterialVersion = 1,
                Choices =
                [
                    new() { Key = "accept-example", Slot = LearningPracticeChoiceSlot.Primary, Value = 1 },
                    new() { Key = "reject-example", Slot = LearningPracticeChoiceSlot.Secondary, Value = 0 }
                ],
                Transitions =
                [
                    new() { FromStepKey = "choose", Action = LearningPracticeAction.Choose,
                        ChoiceKey = "accept-example", ToStepKey = "confirm" },
                    new() { FromStepKey = "choose", Action = LearningPracticeAction.Choose,
                        ChoiceKey = "reject-example", ToStepKey = "confirm" },
                    new() { FromStepKey = "confirm", Action = LearningPracticeAction.Confirm,
                        ToStepKey = "result" }
                ]
            }
        ],
        OwnedPractices =
        [
            new() { Id = _practiceId, ContextKey = "self", RecipeKey = "ai-example-review",
                MaterialVersion = 1, Revision = learnerRevision + 1, PreviewAvailable = true }
        ],
        _links = new Dictionary<string, HalLink>
        {
            ["self"] = new() { Href = "/api/learning/context", Method = "GET" }
        }
    };

    private HalResourceOfLearningPracticeDto Practice(
        string step, long revision, long learnerRevision, string? choice = null) => new()
        {
            Id = _practiceId,
            ContextKey = "self",
            ScopeStamp = "demo-self-scope",
            RecipeKey = "ai-example-review",
            MaterialVersion = 1,
            Revision = revision,
            LearnerRevision = learnerRevision,
            CanSimulate = true,
            Simulation = new()
            {
                RecipeKey = "ai-example-review",
                MaterialVersion = 1,
                StepKey = step,
                Step = step == "result" ? 2 : step == "confirm" ? 1 : 0,
                ChoiceSlot = choice == "accept-example" ? LearningPracticeChoiceSlot.Primary
                : choice == "reject-example" ? LearningPracticeChoiceSlot.Secondary : LearningPracticeChoiceSlot.None,
                Value = choice == "accept-example" ? 1 : 0,
                ChoiceKey = choice,
                IsConfirmed = step == "result",
                Status = step == "result" ? LearningSimulationStatus.Simulated
                : step == "confirm" ? LearningSimulationStatus.AwaitingConfirmation : LearningSimulationStatus.Choosing,
                Receipt = step == "result" ? new()
                {
                    RecipeKey = "ai-example-review",
                    MaterialVersion = 1,
                    ChoiceKey = choice!,
                    ResultKey = choice == "accept-example" ? "example-accepted" : "example-rejected",
                    IsSimulation = true,
                    ProductionEffects = false
                } : null
            },
            _links = new Dictionary<string, HalLink>
            {
                [step == "choose" ? "simulate-choice" : step == "confirm" ? "simulate-confirm" : "reset-simulation"] =
                new() { Href = "/api/learning/practices/actions", Method = "POST" }
            }
        };

    private HalResourceOfLearningMutationReceiptDto Receipt(
        HalResourceOfLearningContextDto context, HalResourceOfLearningPracticeDto practice) => new()
        {
            Id = _practiceId,
            ScopeStamp = context.ScopeStamp,
            ResourceRevision = context.ResourceRevision,
            LearnerRevision = context.LearnerRevision,
            State = new() { TenantId = context.TenantId, UserId = context.UserId },
            Practice = new()
            {
                Id = practice.Id,
                ScopeStamp = practice.ScopeStamp,
                ContextKey = practice.ContextKey,
                RecipeKey = practice.RecipeKey,
                MaterialVersion = practice.MaterialVersion,
                Revision = practice.Revision,
                LearnerRevision = practice.LearnerRevision,
                Simulation = practice.Simulation
            },
            SimulationStatus = practice.Simulation.Status
        };

    private static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    private static ProposedActions2 CreateExecutedAction() => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = "CreateEventDraft",
        Status = "Executed",
        ResultResourceId = Guid.CreateVersion7()
    };

    private sealed class PathBaseNavigation : NavigationManager
    {
        public PathBaseNavigation() => Initialize("https://event.test/acme/", "https://event.test/acme/");

        protected override void NavigateToCore(string uri, bool forceLoad) => Navigate(uri);

        protected override void NavigateToCore(string uri, NavigationOptions options) => Navigate(uri);

        private void Navigate(string uri)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }

    private sealed record ConfirmationRequest(
        HttpMethod Method, string Path, string? IdempotencyKey, string Body);

    private sealed class LiveConfirmationHttpHandler(string? confirmationPath, bool allowReads = true)
        : HttpMessageHandler
    {
        public TaskCompletionSource<ConfirmationRequest> Confirmation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConfirmationCount { get; private set; }
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            if (allowReads && request.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, path.EndsWith("/conversations", StringComparison.Ordinal)
                    ? """{"_embedded":{"items":[]}}"""
                    : "{}");
            }

            if (request.Method != HttpMethod.Post || path != confirmationPath)
                throw new InvalidOperationException("Unexpected outbound AI mutation.");

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ConfirmationCount++;
            Confirmation.TrySetResult(new ConfirmationRequest(request.Method, path,
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                body));
            return JsonResponse(HttpStatusCode.Conflict,
                """{"status":409,"code":"proposed_action_version_changed"}""");
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
