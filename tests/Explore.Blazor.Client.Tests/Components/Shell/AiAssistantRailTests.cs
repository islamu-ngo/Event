using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Services.Shell;
using Explore.Blazor.Client.Tests;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Refit;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AiAssistantRailTests : IDisposable
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");

    private readonly BlazorTestContext _ctx = new();
    private readonly IAiAssistantClientService _clientService = Substitute.For<IAiAssistantClientService>();
    private readonly AiAssistantState _shellState = new();
    private readonly AiAssistantConversationState _conversationState = new();

    public AiAssistantRailTests()
    {
        _ctx.Services.AddSingleton(_clientService);
        _ctx.Services.AddSingleton(_shellState);
        _ctx.Services.AddSingleton(_conversationState);
        _ctx.Services.AddSingleton<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddSingleton<WorkspaceRouteClassifier>();
        _ctx.Services.AddSingleton<UiShellState>();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_WhenAssistantIsUnavailable_DoesNotRenderRailOrLoadConversations()
    {
        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        await Assert.That(cut.FindAll("[data-testid='shell-ai-rail']")).IsEmpty();
        await _clientService.DidNotReceive().GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DockHeader_AuthenticatedUser_OpensSelectedConversationInAiWorkspace()
    {
        var conversationId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(conversationId, "Event planning"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events?q=planning");

        var cut = _ctx.RenderMudComponent<AiAssistantRail>(parameters => parameters
            .Add(component => component.HostedInDock, true));
        await cut.Find("[data-testid='ai-rail-open-workspace']").ClickAsync(new MouseEventArgs());

        await Assert.That(navigation.Uri).EndsWith($"/ai/chats/{conversationId:D}");
        await Assert.That(_conversationState.ReturnWorkspace).IsEqualTo(WorkspaceKey.Events);
    }

    [Test]
    public async Task DockHeader_AnonymousUser_DoesNotOfferAiWorkspaceRoute()
    {
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(
                CreateConversationCollection([], canCreate: false)));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: true, isAuthenticated: false);

        var cut = _ctx.RenderMudComponent<AiAssistantRail>(parameters => parameters
            .Add(component => component.HostedInDock, true));

        await Assert.That(cut.FindAll("[data-testid='ai-rail-open-workspace']")).IsEmpty();
    }

    [Test]
    public async Task WorkspaceHost_RequestedConversation_SelectsRouteConversation()
    {
        var conversationId = Guid.CreateVersion7();
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(
                CreateConversationCollection(
                [
                    new() { Id = conversationId, Title = "Requested chat", Status = "Active" }
                ])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "Requested chat")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/ai/chats/{conversationId:D}");

        var cut = _ctx.RenderMudComponent<AiAssistantRail>(parameters => parameters
            .Add(component => component.HostedInWorkspace, true)
            .Add(component => component.ConversationId, conversationId));

        cut.WaitForAssertion(() =>
        {
            if (_conversationState.SelectedConversation?.Id != conversationId)
                throw new InvalidOperationException("Expected workspace host to select its route conversation.");
        });
        await Assert.That(cut.Find("[data-testid='shell-ai-rail']").ClassList).Contains("ai-rail--workspace");
        await _clientService.Received(1).GetConversationAsync(conversationId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WorkspaceHost_NewConversationRequest_CreatesOnceAndReplacesRoute()
    {
        var conversationId = Guid.CreateVersion7();
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])),
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection(
                [
                    new() { Id = conversationId, Title = "AI Assistant", Status = "Active" }
                ])));
        _clientService.CreateConversationAsync(Arg.Any<CreateAiConversationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, conversationId, "Created", null, [])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "AI Assistant")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/ai?new=true");

        var cut = _ctx.RenderMudComponent<AiAssistantRail>(parameters => parameters
            .Add(component => component.HostedInWorkspace, true)
            .Add(component => component.StartNewConversation, true));

        cut.WaitForAssertion(() =>
        {
            if (!navigation.Uri.EndsWith($"/ai/chats/{conversationId:D}", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected new conversation request to replace the workspace route.");
        });
        await Assert.That(navigation.Uri).EndsWith($"/ai/chats/{conversationId:D}");
        await _clientService.Received(1).CreateConversationAsync(
            Arg.Any<CreateAiConversationRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WorkspaceHost_WhenPolicyArrivesAfterRender_InitializesNewConversationRequest()
    {
        var conversationId = Guid.CreateVersion7();
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])),
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection(
                [
                    new() { Id = conversationId, Title = "AI Assistant", Status = "Active" }
                ])));
        _clientService.CreateConversationAsync(Arg.Any<CreateAiConversationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, conversationId, "Created", null, [])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "AI Assistant")));
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/ai?new=true");
        var cut = _ctx.RenderMudComponent<AiAssistantRail>(parameters => parameters
            .Add(component => component.HostedInWorkspace, true)
            .Add(component => component.StartNewConversation, true));

        await cut.InvokeAsync(() =>
            _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true));

        cut.WaitForAssertion(() =>
        {
            if (_conversationState.SelectedConversation?.Id != conversationId)
                throw new InvalidOperationException("Expected late AI policy to initialize the workspace request.");
        });
        await _clientService.Received(1).CreateConversationAsync(
            Arg.Any<CreateAiConversationRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WorkspaceHost_ReentrantPolicyChange_WaitsForCreateAffordanceBeforeProcessingNewRequest()
    {
        var conversationId = Guid.CreateVersion7();
        var initialCollection = new TaskCompletionSource<HalCollectionResourceOfAiConversationSummaryDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                initialCollection.Task,
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection(
                [
                    new() { Id = conversationId, Title = "AI Assistant", Status = "Active" }
                ])));
        _clientService.CreateConversationAsync(Arg.Any<CreateAiConversationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, conversationId, "Created", null, [])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "AI Assistant")));
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/ai?new=true");
        var cut = _ctx.RenderMudComponent<AiAssistantRail>(parameters => parameters
            .Add(component => component.HostedInWorkspace, true)
            .Add(component => component.StartNewConversation, true));

        await cut.InvokeAsync(() =>
            _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true));
        cut.WaitForAssertion(() =>
            _clientService.Received(1).GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()));

        await cut.InvokeAsync(_shellState.Open);
        initialCollection.SetResult(CreateConversationCollection([]));

        cut.WaitForAssertion(() =>
        {
            if (_conversationState.SelectedConversation?.Id != conversationId)
                throw new InvalidOperationException("Expected reentrant state changes to wait for the HAL create affordance.");
        });
        await _clientService.Received(1).CreateConversationAsync(
            Arg.Any<CreateAiConversationRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Open_WhenLatestConversationIsEmpty_LoadsExistingEmptyConversationWithoutCreatingAnother()
    {
        var conversationId = Guid.CreateVersion7();
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(
                CreateConversationCollection(
                [
                    new() { Id = conversationId, Title = "AI Assistant", Status = "Active", LastMessageSequence = 0 }
                ])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "AI Assistant")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        cut.WaitForAssertion(() =>
        {
            if (_conversationState.SelectedConversation?.Id != conversationId)
            {
                throw new InvalidOperationException("Expected rail to reuse the latest empty conversation.");
            }
        });

        await _clientService.Received(1).GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _clientService.Received(1).GetConversationAsync(conversationId, Arg.Any<CancellationToken>());
        await _clientService.DidNotReceive().CreateConversationAsync(
            Arg.Any<CreateAiConversationRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Open_WhenLatestConversationHasMessages_CreatesAndLoadsFreshConversation()
    {
        var previousConversationId = Guid.CreateVersion7();
        var freshConversationId = Guid.CreateVersion7();
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(
                    CreateConversationCollection(
                    [
                        new() { Id = previousConversationId, Title = "Event planning", Status = "Active", LastMessageSequence = 2 }
                    ])),
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(
                    CreateConversationCollection(
                    [
                        new() { Id = freshConversationId, Title = "AI Assistant", Status = "Active", LastMessageSequence = 0 },
                        new() { Id = previousConversationId, Title = "Event planning", Status = "Active", LastMessageSequence = 2 }
                    ])));
        _clientService.CreateConversationAsync(Arg.Any<CreateAiConversationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, freshConversationId, "Created", null, [])));
        _clientService.GetConversationAsync(freshConversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(freshConversationId, "AI Assistant")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        cut.WaitForAssertion(() =>
        {
            if (_conversationState.SelectedConversation?.Id != freshConversationId)
            {
                throw new InvalidOperationException("Expected rail open to select a fresh conversation.");
            }
        });

        await _clientService.Received(1).CreateConversationAsync(
            Arg.Is<CreateAiConversationRequestDto>(request => request.Title == "AI Assistant"),
            Arg.Any<CancellationToken>());
        await _clientService.Received(1).GetConversationAsync(freshConversationId, Arg.Any<CancellationToken>());
        await _clientService.DidNotReceive().GetConversationAsync(previousConversationId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Render_OnlyShowsProposalButtonsWhenHalLinksExist()
    {
        var conversationId = Guid.CreateVersion7();
        var linkedActionId = Guid.CreateVersion7();
        var staleActionId = Guid.CreateVersion7();
        _conversationState.SelectConversation(new HalResourceOfAiConversationDto
        {
            Id = conversationId,
            Title = "Proposal review",
            ProposedActions =
            [
                new ProposedActions2
                {
                    Id = linkedActionId,
                    Kind = "CreateEventDraft",
                    Status = "Proposed"
                },
                new ProposedActions2
                {
                    Id = staleActionId,
                    Kind = "CreateEventDraft",
                    Status = "Executed"
                }
            ]
        });
        var linkedAction = _conversationState.SelectedConversation!.ProposedActions!
            .Single(action => action.Id == linkedActionId);
        GeneratedHalLinkTestHelper.SetLinks(
            linkedAction,
            ("confirm-action", "/confirm", "POST"),
            ("reject-action", "/reject", "POST"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.ConfirmProposedActionAsync(conversationId, linkedActionId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, Guid.CreateVersion7(), "Confirmed", null, [])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(_conversationState.SelectedConversation));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='ai-rail-confirm-action']").Count != 1
                || cut.FindAll("[data-testid='ai-rail-reject-action']").Count != 1)
            {
                throw new InvalidOperationException("Expected only HAL-linked action buttons.");
            }
        });

        await cut.Find("[data-testid='ai-rail-confirm-action']").ClickAsync(new MouseEventArgs());

        await _clientService.Received(1).ConfirmProposedActionAsync(
            conversationId,
            linkedActionId,
            Arg.Is<string?>(value => value != null && value.StartsWith("blazor-ai-confirm-", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Render_WhenActionBelongsToAssistantMessage_RendersActionInlineAfterThatMessage()
    {
        var conversationId = Guid.CreateVersion7();
        var userMessageId = Guid.CreateVersion7();
        var assistantMessageId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(
            conversationId,
            "Inline proposal",
            [
                new Messages2 { Id = userMessageId, Role = "User", Sequence = 1, Content = "Create an event draft" },
                new Messages2 { Id = assistantMessageId, Role = "Assistant", Sequence = 2, Content = "I prepared a draft for review." }
            ],
            [
                new ProposedActions2
                {
                    Id = Guid.CreateVersion7(),
                    MessageId = assistantMessageId,
                    Kind = "CreateEventDraft",
                    Status = "Proposed",
                    PayloadJson = "{\"title\":\"Community Iftar\",\"description\":\"Plan the meal.\"}"
                }
            ]));
        var inlineAction = _conversationState.SelectedConversation!.ProposedActions!.Single();
        GeneratedHalLinkTestHelper.SetLinks(
            inlineAction,
            ("confirm-action", "/confirm", "POST"),
            ("reject-action", "/reject", "POST"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            var userIndex = markup.IndexOf("Create an event draft", StringComparison.Ordinal);
            var assistantIndex = markup.IndexOf("I prepared a draft for review.", StringComparison.Ordinal);
            var actionIndex = markup.IndexOf("Community Iftar", StringComparison.Ordinal);

            if (userIndex < 0 || assistantIndex < 0 || actionIndex < 0 || !(userIndex < assistantIndex && assistantIndex < actionIndex))
            {
                throw new InvalidOperationException("Expected proposal card to render inline after its assistant message.");
            }
        });
    }

    [Test]
    public async Task ReferenceSearch_SelectsAndRemovesInlineMentionReference()
    {
        var conversationId = Guid.CreateVersion7();
        var referenceId = Guid.CreateVersion7();
        _conversationState.SelectConversation(new HalResourceOfAiConversationDto { Id = conversationId, Title = "References" });
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SearchReferencesAsync("iftar", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HalResourceOfAiReferenceSearchResultDto>>(
            [
                HalLinkTestFactory.WithLinks(new HalResourceOfAiReferenceSearchResultDto
                {
                    Kind = "Event",
                    ReferenceId = referenceId,
                    DisplayName = "Community Iftar"
                }, new HalLinkTestLink("event", $"/api/events/{referenceId}", "GET"))
            ]));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(new ChangeEventArgs { Value = "@iftar" });

        cut.WaitForElement("[data-testid='ai-rail-reference-result']");

        await cut.Find("[data-testid='ai-rail-reference-result']").ClickAsync(new MouseEventArgs());
        await Assert.That(_conversationState.SelectedReferences.Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='ai-rail-prompt']").GetAttribute("value")).IsEqualTo("@Community Iftar ");
        await Assert.That(cut.Find("[data-testid='ai-rail-prompt-reference-token']").TextContent).IsEqualTo("@Community Iftar");
        await Assert.That(cut.Find("[data-testid='ai-rail-prompt']").GetAttribute("data-reference-mention-tokens"))
            .Contains("@Community Iftar");
        await Assert.That(cut.FindAll(".ai-rail__selected-reference-list")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='ai-reference-chip']")).IsEmpty();

        var deletion = await cut.InvokeAsync(() => cut.Instance.DeletePromptReferenceFromKeyboard(
            selectionStart: 1,
            selectionEnd: 1,
            key: "Backspace"));

        await Assert.That(deletion.Handled).IsTrue();
        await Assert.That(deletion.Text).IsEqualTo(string.Empty);
        await Assert.That(_conversationState.SelectedReferences).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='ai-rail-prompt-reference-token']")).IsEmpty();
    }

    [Test]
    public async Task ReferenceHighlights_EncodeDangerousReferenceDisplayNames()
    {
        var conversationId = Guid.CreateVersion7();
        var referenceId = Guid.CreateVersion7();
        var displayName = "<img src=x onerror=alert(1)> <script>alert(1)</script>";
        _conversationState.SelectConversation(new HalResourceOfAiConversationDto { Id = conversationId, Title = "References" });
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SearchReferencesAsync("xss", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HalResourceOfAiReferenceSearchResultDto>>(
            [
                HalLinkTestFactory.WithLinks(new HalResourceOfAiReferenceSearchResultDto
                {
                    Kind = "Event",
                    ReferenceId = referenceId,
                    DisplayName = displayName
                }, new HalLinkTestLink("event", $"/api/events/{referenceId}", "GET"))
            ]));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(new ChangeEventArgs { Value = "@xss" });
        cut.WaitForElement("[data-testid='ai-rail-reference-result']");
        await cut.InvokeAsync(() => cut.Find("[data-testid='ai-rail-reference-result']").Click());

        var token = cut.Find("[data-testid='ai-rail-prompt-reference-token']");
        await Assert.That(token.TextContent).IsEqualTo($"@{displayName}");
        await Assert.That(token.InnerHtml).Contains("&lt;img");
        await Assert.That(token.InnerHtml).Contains("&lt;script");
        await Assert.That(token.InnerHtml).DoesNotContain("<img");
        await Assert.That(token.InnerHtml).DoesNotContain("<script");
        await Assert.That(cut.FindAll("img")).IsEmpty();
        await Assert.That(cut.FindAll("script")).IsEmpty();
    }

    [Test]
    public async Task ReferenceSearch_WhenMentionTriggerIsDeleted_ClosesAutocomplete()
    {
        var conversationId = Guid.CreateVersion7();
        _conversationState.SelectConversation(new HalResourceOfAiConversationDto { Id = conversationId, Title = "References" });
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        var prompt = cut.Find("[data-testid='ai-rail-prompt']");

        await prompt.InputAsync(new ChangeEventArgs { Value = "@iftar" });
        cut.WaitForElement("[data-testid='ai-rail-reference-autocomplete']");

        await prompt.InputAsync(new ChangeEventArgs { Value = "iftar" });

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='ai-rail-reference-autocomplete']").Count != 0)
            {
                throw new InvalidOperationException("Expected deleting the @ mention trigger to close autocomplete.");
            }
        });
    }

    [Test]
    public async Task SendMessage_WhenReferenceIsSelected_PassesReferenceContext()
    {
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var referenceId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(conversationId, "Reference send"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SearchReferencesAsync("iftar", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HalResourceOfAiReferenceSearchResultDto>>(
            [
                HalLinkTestFactory.WithLinks(new HalResourceOfAiReferenceSearchResultDto
                {
                    Kind = "Event",
                    ReferenceId = referenceId,
                    DisplayName = "Community Iftar",
                    Summary = "Public evening program"
                }, new HalLinkTestLink("event", $"/api/events/{referenceId}", "GET"))
            ]));
        _clientService.SendMessageAsync(
                conversationId,
                "@Community Iftar",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
                Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, runId, "Sent", null, [])));
        _clientService.GetRunStatusAsync(conversationId, runId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AiRunStatusResult.Ok(new HalResourceOfAiRunDto { Status = "Succeeded" })));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "Reference send")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(new ChangeEventArgs { Value = "@iftar" });
        cut.WaitForElement("[data-testid='ai-rail-reference-result']");
        await cut.InvokeAsync(() => cut.Find("[data-testid='ai-rail-reference-result']").Click());
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        await _clientService.Received(1).SendMessageAsync(
            conversationId,
            "@Community Iftar",
            Arg.Any<string?>(),
            Arg.Is<string?>(value => value != null && value.StartsWith("blazor-ai-send-", StringComparison.Ordinal)),
            Arg.Is<string?>(value => value == "build"),
            Arg.Any<Guid?>(),
            Arg.Is<IReadOnlyList<AiMessageImageInputDto>?>(value => value != null && value.Count == 0),
            Arg.Is<IReadOnlyList<AiSelectedReferenceDto>?>(references =>
                references != null
                && references.Count == 1
                && references.Single().Kind == "Event"
                && references.Single().ReferenceId == referenceId
                && references.Single().DisplayName == "Community Iftar"
                && references.Single().Summary == "Public evening program"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NewConversation_WhenCreateSucceeds_LoadsCreatedConversation()
    {
        var actorId = Guid.CreateVersion7();
        var conversationId = Guid.CreateVersion7();
        _clientService.GetBootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiAssistantBootstrapDto?>(CreateBootstrap(actorId, "User", "Amina Yusuf")));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])),
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(
                    CreateConversationCollection(
                    [
                        new() { Id = conversationId, Title = "New AI plan", Status = "Active" }
                    ])));
        _clientService.CreateConversationAsync(Arg.Any<CreateAiConversationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, conversationId, "Created", null, [])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "New AI plan")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        cut.WaitForElement("[data-testid='ai-rail-new-conversation']");
        await cut.Find("[data-testid='ai-rail-new-conversation']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("New AI plan", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected created conversation to load into the rail.");
            }
        });
        await _clientService.Received(2).CreateConversationAsync(
            Arg.Is<CreateAiConversationRequestDto>(request => request.Title == "AI Assistant" && request.ActorId == actorId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessage_WhenConversationIsSelected_PropagatesGeneratedIdempotencyKeyAndReloadsConversation()
    {
        var actorId = Guid.CreateVersion7();
        var conversationId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(conversationId, "Send test"));
        _clientService.GetBootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiAssistantBootstrapDto?>(CreateBootstrap(actorId, "User", "Amina Yusuf")));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SendMessageAsync(
                conversationId,
                "Draft a khutbah event",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
                Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, Guid.CreateVersion7(), "Sent", null, [])));
        _clientService.GetRunStatusAsync(conversationId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AiRunStatusResult.Ok(new HalResourceOfAiRunDto
            {
                Status = "Succeeded"
            })));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(
                conversationId,
                "Send test",
                [
                    new Messages2 { Role = "Assistant", Sequence = 1, Content = "Draft created." }
                ])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        await cut.Find("[data-testid='ai-rail-prompt']").ChangeAsync(new ChangeEventArgs { Value = "Draft a khutbah event" });
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Draft created.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected sent message to reload conversation detail.");
            }
        });
        await _clientService.Received(1).SendMessageAsync(
            conversationId,
            "Draft a khutbah event",
            Arg.Any<string?>(),
            Arg.Is<string?>(value => value != null && value.StartsWith("blazor-ai-send-", StringComparison.Ordinal)),
            Arg.Is<string?>(value => value == "build"),
            Arg.Is<Guid?>(value => value == actorId),
            Arg.Is<IReadOnlyList<AiMessageImageInputDto>?>(value => value != null && value.Count == 0),
            Arg.Is<IReadOnlyList<AiSelectedReferenceDto>?>(value => value != null && value.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessage_WhenUnauthorized_RefreshesSessionAndRetriesWithSameIdempotencyKey()
    {
        var conversationId = Guid.CreateVersion7();
        var idempotencyKeys = new List<string?>();
        var bffAuthApi = _ctx.Services.GetRequiredService<IBffAuthApi>();
        var refreshResponse = Substitute.For<IApiResponse>();
        refreshResponse.IsSuccessStatusCode.Returns(true);
        refreshResponse.StatusCode.Returns(System.Net.HttpStatusCode.OK);
        bffAuthApi.RefreshSessionInternalAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(refreshResponse));

        _conversationState.SelectConversation(CreateConversation(conversationId, "Send auth retry"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SendMessageAsync(
                conversationId,
                "hello",
                Arg.Any<string?>(),
                Arg.Do<string?>(value => idempotencyKeys.Add(value)),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
                Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new AiAssistantCommandResult(
                    false,
                    null,
                    "The AI assistant message could not be sent.",
                    "unauthorized",
                    ["The AI assistant message could not be sent."])),
                Task.FromResult(new AiAssistantCommandResult(true, null, "Sent", null, [])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(
                conversationId,
                "Send auth retry",
                [new Messages2 { Role = "Assistant", Sequence = 1, Content = "Recovered response." }])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        await cut.Find("[data-testid='ai-rail-prompt']").ChangeAsync(new ChangeEventArgs { Value = "hello" });
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Recovered response.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected retry success to reload conversation detail.");
            }
        });
        await bffAuthApi.Received(1).RefreshSessionInternalAsync(Arg.Any<CancellationToken>());
        await _clientService.Received(2).SendMessageAsync(
            conversationId,
            "hello",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Is<string?>(value => value == "build"),
            Arg.Any<Guid?>(),
            Arg.Is<IReadOnlyList<AiMessageImageInputDto>?>(value => value != null && value.Count == 0),
            Arg.Is<IReadOnlyList<AiSelectedReferenceDto>?>(value => value != null && value.Count == 0),
            Arg.Any<CancellationToken>());
        await Assert.That(idempotencyKeys.Count).IsEqualTo(2);
        await Assert.That(idempotencyKeys[0]).IsEqualTo(idempotencyKeys[1]);
        await Assert.That(idempotencyKeys[0]).StartsWith("blazor-ai-send-");
    }

    [Test]
    public async Task Render_AddFilesAction_UsesMudFileUploadPicker()
    {
        var conversationId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(conversationId, "Image picker test"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        cut.WaitForElement("[data-testid='ai-rail-prompt']");

        await Assert.That(cut.FindComponents<MudFileUpload<IReadOnlyList<IBrowserFile>>>().Count).IsEqualTo(1);
        await Assert.That(cut.Find("input[type='file']").GetAttribute("accept"))
            .IsEqualTo(ImageUploadClientPolicy.DefaultAcceptedImageFormats);
        await Assert.That(cut.Markup).DoesNotContain("Browse files");
        await Assert.That(cut.Markup).DoesNotContain("mud-file-upload-files-default-template");
    }

    [Test]
    [Arguments("vector.svg", "image/svg+xml")]
    [Arguments("bitmap.bmp", "image/bmp")]
    [Arguments("modern.avif", "image/avif")]
    [Arguments("mismatch.jpg", "image/jpeg")]
    public async Task SelectImage_WhenTypeOrExtensionDoesNotMatchSafeRaster_RejectsBeforeAttachment(
        string fileName,
        string contentType)
    {
        byte[] pngBytes = ValidPng;
        var conversationId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(conversationId, "Unsafe image test"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(
            pngBytes,
            fileName,
            contentType: contentType));

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='ai-rail-image-chip']").Count != 0)
            {
                throw new InvalidOperationException("Unsafe or mismatched image reached the attachment list.");
            }
        });
    }

    [Test]
    public async Task SelectImage_WhenSafePrefixHasActiveTail_RejectsBeforeAttachmentOrNetwork()
    {
        byte[] activeTail = [.. ValidPng, .. "<html><script>alert(1)</script></html>"u8];
        var conversationId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(conversationId, "Active tail test"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(
            activeTail,
            "active-tail.png",
            contentType: "image/png"));

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='ai-rail-image-chip']").Count != 0)
            {
                throw new InvalidOperationException("Active-tail image reached the attachment list.");
            }
        });
        await _clientService.DidNotReceive().SendMessageAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<Guid?>(),
            Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
            Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessage_WhenImageIsSelected_ConvertsImageToPlainBase64()
    {
        var conversationId = Guid.CreateVersion7();
        byte[] imageBytes = ValidPng;
        var expectedBase64 = Convert.ToBase64String(imageBytes);
        _conversationState.SelectConversation(CreateConversation(conversationId, "Image send test"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SendMessageAsync(
                conversationId,
                "Describe this picture:",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
                Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, Guid.CreateVersion7(), "Sent", null, [])));
        _clientService.GetRunStatusAsync(conversationId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AiRunStatusResult.Ok(new HalResourceOfAiRunDto
            {
                Status = "Succeeded"
            })));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "Image send test")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(
            imageBytes,
            "csharp.png",
            contentType: "image/png"));
        await cut.Find("[data-testid='ai-rail-prompt']").ChangeAsync(new ChangeEventArgs { Value = "Describe this picture:" });

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("csharp.png", StringComparison.Ordinal)
                || cut.FindAll("[data-testid='ai-rail-image-chip']").Count != 1
                || cut.Markup.Contains("Browse files", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected only the selected image chip list to render before sending.");
            }
        });

        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        await _clientService.Received(1).SendMessageAsync(
            conversationId,
            "Describe this picture:",
            Arg.Any<string?>(),
            Arg.Is<string?>(value => value != null && value.StartsWith("blazor-ai-send-", StringComparison.Ordinal)),
            Arg.Is<string?>(value => value == "build"),
            Arg.Any<Guid?>(),
            Arg.Is<IReadOnlyList<AiMessageImageInputDto>?>(images =>
                images != null
                && images.Count == 1
                && images.Single().MediaType == "image/png"
                && images.Single().FileName == "csharp.png"
                && images.Single().SizeBytes == imageBytes.Length
                && images.Single().Data == expectedBase64
                && !images.Single().Data.StartsWith("data:", StringComparison.OrdinalIgnoreCase)),
            Arg.Is<IReadOnlyList<AiSelectedReferenceDto>?>(value => value != null && value.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Render_WhenBootstrapHasActors_DefaultsActorSelectorToCurrentUser()
    {
        var userActorId = Guid.CreateVersion7();
        var organizationActorId = Guid.CreateVersion7();
        var conversationId = Guid.CreateVersion7();
        _clientService.GetBootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiAssistantBootstrapDto?>(CreateBootstrap(
                (userActorId, "User", "Amina Yusuf"),
                (organizationActorId, "Organization", "ISLAMU Center"))));
        _conversationState.SelectConversation(CreateConversation(conversationId, "Actor test"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        var selector = cut.WaitForElement("[data-testid='ai-rail-actor-selector']");
        await Assert.That(selector.GetAttribute("value")).IsEqualTo(userActorId.ToString());
        await Assert.That(cut.Markup).Contains("User · Amina Yusuf");
        await Assert.That(cut.Markup).Contains("Organization · ISLAMU Center");
        await Assert.That(cut.Markup).Contains($"data-actor-type=\"User\"");
        await Assert.That(cut.Markup).Contains($"data-actor-display-name=\"Amina Yusuf\"");
    }

    [Test]
    public async Task ReferenceSearch_WhenMentionStarts_ShowsActorContextDefaultsAboveComposerCard()
    {
        var userActorId = Guid.CreateVersion7();
        var organizationActorId = Guid.CreateVersion7();
        var groupActorId = Guid.CreateVersion7();
        var conversationId = Guid.CreateVersion7();
        _clientService.GetBootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiAssistantBootstrapDto?>(CreateBootstrap(
                (userActorId, "User", "Amina Yusuf"),
                (organizationActorId, "Organization", "ISLAMU Center"),
                (groupActorId, "Group", "Sisters Study Circle"))));
        _conversationState.SelectConversation(CreateConversation(conversationId, "Reference defaults"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(new ChangeEventArgs { Value = "@" });

        cut.WaitForAssertion(() =>
        {
            var results = cut.FindAll("[data-testid='ai-rail-reference-result']");
            if (results.Count != 3
                || !cut.Markup.Contains("Amina Yusuf", StringComparison.Ordinal)
                || !cut.Markup.Contains("ISLAMU Center", StringComparison.Ordinal)
                || !cut.Markup.Contains("Sisters Study Circle", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected actor-context defaults for a bare mention trigger.");
            }
        });

        var autocomplete = cut.Find("[data-testid='ai-rail-reference-autocomplete']");
        await Assert.That(autocomplete.ParentElement?.ClassList.Contains("ai-rail__composer")).IsTrue();

        var autocompleteIndex = cut.Markup.IndexOf("data-testid=\"ai-rail-reference-autocomplete\"", StringComparison.Ordinal);
        var composerCardIndex = cut.Markup.IndexOf("ai-rail__composer-card", StringComparison.Ordinal);
        await Assert.That(autocompleteIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(composerCardIndex).IsGreaterThan(autocompleteIndex);

        var kindLabels = cut.FindAll(".ai-rail__reference-option-kind")
            .Select(element => element.TextContent)
            .ToList();
        await Assert.That(kindLabels).IsEquivalentTo(["User", "Org", "Group"]);
        await Assert.That(cut.Find("[data-testid='ai-rail-reference-result']").GetAttribute("id"))
            .IsEqualTo("ai-rail-reference-option-0");
        await _clientService.DidNotReceive().SearchReferencesAsync(
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessage_WhenActorContextReferenceIsSelected_SendsActorReferenceContext()
    {
        var userActorId = Guid.CreateVersion7();
        var groupActorId = Guid.CreateVersion7();
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        _clientService.GetBootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiAssistantBootstrapDto?>(CreateBootstrap(
                (userActorId, "User", "Amina Yusuf"),
                (groupActorId, "Group", "Sisters Study Circle"))));
        _conversationState.SelectConversation(CreateConversation(conversationId, "Actor reference send"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SendMessageAsync(
                conversationId,
                "@Sisters Study Circle",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
                Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, runId, "Sent", null, [])));
        _clientService.GetRunStatusAsync(conversationId, runId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AiRunStatusResult.Ok(new HalResourceOfAiRunDto { Status = "Succeeded" })));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "Actor reference send")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        await cut.Find("[data-testid='ai-rail-prompt']").InputAsync(new ChangeEventArgs { Value = "@" });
        cut.WaitForElement("[data-testid='ai-rail-reference-result']");
        var groupOption = cut.FindAll("[data-testid='ai-rail-reference-result']")
            .Single(element => element.TextContent.Contains("Sisters Study Circle", StringComparison.Ordinal));

        await groupOption.ClickAsync(new MouseEventArgs());
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        await _clientService.Received(1).SendMessageAsync(
            conversationId,
            "@Sisters Study Circle",
            Arg.Any<string?>(),
            Arg.Is<string?>(value => value != null && value.StartsWith("blazor-ai-send-", StringComparison.Ordinal)),
            Arg.Is<string?>(value => value == "build"),
            Arg.Any<Guid?>(),
            Arg.Is<IReadOnlyList<AiMessageImageInputDto>?>(value => value != null && value.Count == 0),
            Arg.Is<IReadOnlyList<AiSelectedReferenceDto>?>(references =>
                references != null
                && references.Count == 1
                && references.Single().Kind == "Actor"
                && references.Single().ReferenceId == groupActorId
                && references.Single().DisplayName == "Sisters Study Circle"
                && references.Single().Summary == "Actor context: Group"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessage_WhenSelectedActorExpiresAfterBootstrapRefresh_UsesRefreshedAuthorizedActor()
    {
        var userActorId = Guid.CreateVersion7();
        var organizationActorId = Guid.CreateVersion7();
        var existingConversationId = Guid.CreateVersion7();
        var newConversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(existingConversationId, "Existing actor context"));
        _clientService.GetBootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<HalResourceOfAiAssistantBootstrapDto?>(CreateBootstrap(
                    (userActorId, "User", "Amina Yusuf"),
                    (organizationActorId, "Organization", "ISLAMU Center"))),
                Task.FromResult<HalResourceOfAiAssistantBootstrapDto?>(CreateBootstrap(
                    userActorId,
                    "User",
                    "Amina Yusuf")));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])),
                Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection(
                [
                    new() { Id = newConversationId, Title = "Refreshed actor context", Status = "Active" }
                ])));
        _clientService.CreateConversationAsync(Arg.Any<CreateAiConversationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, newConversationId, "Created", null, [])));
        _clientService.GetConversationAsync(newConversationId, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(newConversationId, "Refreshed actor context")),
                Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(
                    newConversationId,
                    "Refreshed actor context",
                    [new Messages2 { Role = "Assistant", Sequence = 1, Content = "Ready under the refreshed actor." }])),
                Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(
                    newConversationId,
                    "Refreshed actor context",
                    [new Messages2 { Role = "Assistant", Sequence = 1, Content = "Ready under the refreshed actor." }])));
        _clientService.SendMessageAsync(
                newConversationId,
                "Continue with refreshed authority",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
                Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(true, runId, "Sent", null, [])));
        _clientService.GetRunStatusAsync(newConversationId, runId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AiRunStatusResult.Ok(new HalResourceOfAiRunDto
            {
                Status = "Succeeded"
            })));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        var selector = cut.WaitForElement("[data-testid='ai-rail-actor-selector']");
        await selector.ChangeAsync(new ChangeEventArgs { Value = organizationActorId.ToString() });
        await cut.Find("[data-testid='ai-rail-new-conversation']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var refreshedSelector = cut.Find("[data-testid='ai-rail-actor-selector']");
            if (refreshedSelector.GetAttribute("value") != userActorId.ToString()
                || cut.Markup.Contains("Organization · ISLAMU Center", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected stale actor selection to refresh to the remaining authorized actor.");
            }
        });

        await cut.Find("[data-testid='ai-rail-prompt']").ChangeAsync(new ChangeEventArgs
        {
            Value = "Continue with refreshed authority"
        });
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        await _clientService.Received(1).CreateConversationAsync(
            Arg.Is<CreateAiConversationRequestDto>(request => request.ActorId == organizationActorId),
            Arg.Any<CancellationToken>());
        await _clientService.Received(1).SendMessageAsync(
            newConversationId,
            "Continue with refreshed authority",
            Arg.Any<string?>(),
            Arg.Is<string?>(value => value != null && value.StartsWith("blazor-ai-send-", StringComparison.Ordinal)),
            Arg.Is<string?>(value => value == "build"),
            Arg.Is<Guid?>(value => value == userActorId),
            Arg.Is<IReadOnlyList<AiMessageImageInputDto>?>(value => value != null && value.Count == 0),
            Arg.Is<IReadOnlyList<AiSelectedReferenceDto>?>(value => value != null && value.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessage_WhenApiReturnsConflict_ReloadsConversationAndShowsProblemMessage()
    {
        var conversationId = Guid.CreateVersion7();
        _conversationState.SelectConversation(CreateConversation(conversationId, "Send conflict"));
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.SendMessageAsync(
                conversationId,
                "hello",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyList<AiMessageImageInputDto>?>(),
                Arg.Any<IReadOnlyList<AiSelectedReferenceDto>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(
                false,
                null,
                "AI conversation is not ready for a new message.",
                "conversation_not_active",
                ["AI conversation is not ready for a new message."])));
        _clientService.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfAiConversationDto?>(CreateConversation(conversationId, "Send conflict")));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        await cut.Find("[data-testid='ai-rail-prompt']").ChangeAsync(new ChangeEventArgs { Value = "hello" });
        await cut.Find("[data-testid='ai-rail-send']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("AI conversation is not ready for a new message.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected API problem detail to render in the rail.");
            }
        });
        await _clientService.Received(1).GetConversationAsync(conversationId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NewConversation_WhenCreateFails_ShowsSafeErrorMessage()
    {
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(CreateConversationCollection([])));
        _clientService.CreateConversationAsync(Arg.Any<CreateAiConversationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiAssistantCommandResult(false, null, "AI assistant is disabled.", "disabled", [])));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();
        cut.WaitForElement("[data-testid='ai-rail-new-conversation']");
        await cut.Find("[data-testid='ai-rail-new-conversation']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("AI assistant is disabled.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected safe create failure message.");
            }
        });
    }

    [Test]
    public async Task NewConversation_WhenCreateLinkMissing_HidesButtonAndDoesNotCallCreate()
    {
        _clientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(
                CreateConversationCollection([], canCreate: false)));
        _shellState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
        _shellState.Open();

        var cut = _ctx.RenderMudComponent<AiAssistantRail>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Conversation creation is not available for your account.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected missing HAL create link to render a safe empty state.");
            }
        });

        await Assert.That(cut.FindAll("[data-testid='ai-rail-new-conversation']")).IsEmpty();
        await _clientService.DidNotReceive().CreateConversationAsync(
            Arg.Any<CreateAiConversationRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    private static HalCollectionResourceOfAiConversationSummaryDto CreateConversationCollection(
        IReadOnlyList<HalResourceOfAiConversationSummaryDto> conversations,
        bool canCreate = true)
    {
        return new HalCollectionResourceOfAiConversationSummaryDto
        {
            _links = canCreate
                ? new Dictionary<string, HalLink>
                {
                    ["create"] = new() { Href = "/api/ai/assistant/conversations", Method = "POST" }
                }
                : new Dictionary<string, HalLink>(),
            _embedded = new HalCollectionEmbeddedOfAiConversationSummaryDto
            {
                Items = conversations.ToList()
            }
        };
    }

    private static HalResourceOfAiAssistantBootstrapDto CreateBootstrap(Guid actorId, string actorType, string actorDisplayName)
    {
        return CreateBootstrap((actorId, actorType, actorDisplayName));
    }

    private static HalResourceOfAiAssistantBootstrapDto CreateBootstrap(params (Guid ActorId, string ActorType, string ActorDisplayName)[] actors)
    {
        return new HalResourceOfAiAssistantBootstrapDto
        {
            ActorContexts = actors.Select(actor => new ActorContexts2
            {
                ActorId = actor.ActorId,
                ActorType = actor.ActorType,
                ActorDisplayName = actor.ActorDisplayName
            }).ToList()
        };
    }

    private static HalResourceOfAiConversationDto CreateConversation(
        Guid conversationId,
        string title,
        ICollection<Messages2>? messages = null,
        ICollection<ProposedActions2>? proposedActions = null)
    {
        return HalLinkTestFactory.WithLinks(new HalResourceOfAiConversationDto
        {
            Id = conversationId,
            Title = title,
            LastMessageSequence = messages?.Select(message => message.Sequence.GetValueOrDefault()).DefaultIfEmpty(0).Max() ?? 0,
            Messages = messages,
            ProposedActions = proposedActions
        }, new HalLinkTestLink("send-message", $"/api/ai/assistant/conversations/{conversationId}/messages", "POST"));
    }

}
