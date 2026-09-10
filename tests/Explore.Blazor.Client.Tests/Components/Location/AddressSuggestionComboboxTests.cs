using AngleSharp.Dom;
using Explore.Blazor.Client.Components.Locations;
using Explore.Blazor.Client.Contracts.Services.Accessibility;

namespace Explore.Blazor.Client.Tests.Components.Location;

public sealed class AddressSuggestionComboboxTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly StubAddressSuggestionService _service = new();
    private readonly IAccessibilityAnnouncerService _announcer;
    private readonly IAccessibilityFocusService _focus;

    public AddressSuggestionComboboxTests()
    {
        _ctx.Services.AddSingleton<IAddressSuggestionService>(_service);
        _announcer = _ctx.Services.GetRequiredService<IAccessibilityAnnouncerService>();
        _focus = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();
    }

    [Test]
    public async Task InitialRender_UsesNativeCollapsedComboboxSemantics()
    {
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        IElement input = cut.Find("[data-testid='address-suggestion-input']");

        await Assert.That(input.GetAttribute("role")).IsEqualTo("combobox");
        await Assert.That(input.GetAttribute("aria-autocomplete")).IsEqualTo("list");
        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("false");
        await Assert.That(input.HasAttribute("aria-controls")).IsFalse();
        await Assert.That(input.HasAttribute("aria-activedescendant")).IsFalse();
        await Assert.That(input.GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(cut.FindAll("[role='listbox']")).IsEmpty();
        await Assert.That(_service.Calls).IsEmpty();
        await Assert.That(
                _ctx.JSInterop.Invocations.Count(invocation =>
                    invocation.Identifier == "import"))
            .IsEqualTo(1);
    }

    [Test]
    public async Task Search_RendersSemanticOptionsAndKeyboardSelection()
    {
        HalResourceOfAddressSuggestionDto first = Suggestion(
            "Community Hall",
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate);
        HalResourceOfAddressSuggestionDto second = Suggestion(
            "Organization Centre",
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.OrganizationScoped);
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([first, second]);
        HalResourceOfAddressSuggestionDto? selected = null;
        IRenderedComponent<AddressSuggestionCombobox> cut = Render(
            option => selected = option);
        IElement input = cut.Find("[data-testid='address-suggestion-input']");

        await input.InputAsync(new ChangeEventArgs { Value = "centre" });

        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("true");
        await Assert.That(input.GetAttribute("aria-controls"))
            .IsEqualTo("address-suggestion-listbox");
        var options = cut.FindAll("[role='option']");
        await Assert.That(options).Count().IsEqualTo(2);
        await Assert.That(options[0].QuerySelectorAll("bdi[dir='auto']").Length)
            .IsEqualTo(3);
        await Assert.That(options[0].TextContent).Contains("Community Hall");
        await Assert.That(options[0].TextContent).Contains("Local");
        await Assert.That(options[0].TextContent).Contains("Mine");
        await Assert.That(options[1].TextContent).Contains("Provider");
        await Assert.That(options[1].TextContent).Contains("Organization");

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        await Assert.That(input.GetAttribute("aria-activedescendant"))
            .IsEqualTo("address-suggestion-option-1");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        await Assert.That(input.GetAttribute("aria-activedescendant"))
            .IsEqualTo("address-suggestion-option-0");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await Assert.That(input.GetAttribute("aria-activedescendant"))
            .IsEqualTo("address-suggestion-option-1");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        await Assert.That(input.GetAttribute("aria-activedescendant"))
            .IsEqualTo("address-suggestion-option-0");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(selected).IsSameReferenceAs(first);
        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("false");
        await _announcer.Received(1)
            .AnnouncePoliteAsync("2 saved addresses found.");
        await _focus.Received(1)
            .FocusByIdAsync("address-suggestion-input", true);
    }

    [Test]
    public async Task ProviderSuggestionRendersRequiredAttribution()
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion(
            "Provider Hall",
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.CreatorPrivate);
        suggestion.Attribution = "OpenStreetMap contributors";
        suggestion.SelectionToken = "opaque-selection-token";
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>(
                [suggestion]);

        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        await cut.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = "provider" });

        await Assert.That(cut.Markup).Contains("OpenStreetMap contributors");
    }

    [Test]
    public async Task ComposingKeys_DoNotNavigateOrSelectResults()
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion(
            "Composed result",
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate);
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([suggestion]);
        var selections = new List<HalResourceOfAddressSuggestionDto>();
        IRenderedComponent<AddressSuggestionCombobox> cut = Render(selections.Add);
        IElement input = cut.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "composed" });

        await input.KeyDownAsync(
            new KeyboardEventArgs { Key = "ArrowDown", IsComposing = true });
        await input.KeyDownAsync(
            new KeyboardEventArgs { Key = "Enter", IsComposing = true });

        await Assert.That(input.HasAttribute("aria-activedescendant")).IsFalse();
        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("true");
        await Assert.That(selections).IsEmpty();
    }

    [Test]
    public async Task Blur_CollapsesResultsWithoutSelecting()
    {
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>(
                [Suggestion(
                    "Result",
                    LocationAddressSourceEnum.Manual,
                    LocationAddressVisibilityEnum.CreatorPrivate)]);
        var selections = new List<HalResourceOfAddressSuggestionDto>();
        IRenderedComponent<AddressSuggestionCombobox> cut = Render(selections.Add);
        IElement input = cut.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "result" });

        await input.TriggerEventAsync("onblur", new FocusEventArgs());

        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("false");
        await Assert.That(selections).IsEmpty();
    }

    [Test]
    public async Task SupersededSearch_IsCancelledAndCannotReplaceLatestResults()
    {
        var first = PendingSearch.Create();
        var second = PendingSearch.Create();
        _service.Search = (text, _, _, cancellationToken) =>
            text == "first"
                ? first.Execute(cancellationToken)
                : second.Execute(cancellationToken);
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        IElement input = cut.Find("[data-testid='address-suggestion-input']");

        Task firstInput = input.InputAsync(new ChangeEventArgs { Value = "first" });
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task secondInput = input.InputAsync(new ChangeEventArgs { Value = "second" });
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await Assert.That(first.Token.IsCancellationRequested).IsTrue();
        second.Completion.SetResult(
            [Suggestion(
                "Latest result",
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.TenantApproved)]);
        await secondInput.WaitAsync(TimeSpan.FromSeconds(3));
        first.Completion.TrySetResult(
            [Suggestion(
                "Stale result",
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.CreatorPrivate)]);
        await firstInput.WaitAsync(TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Latest result");
        await Assert.That(cut.Markup).DoesNotContain("Stale result");
    }

    [Test]
    public async Task EscapeAndTab_CollapseWithoutSelecting()
    {
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>(
                [Suggestion(
                    "Result",
                    LocationAddressSourceEnum.Manual,
                    LocationAddressVisibilityEnum.CreatorPrivate)]);
        var selections = new List<HalResourceOfAddressSuggestionDto>();
        IRenderedComponent<AddressSuggestionCombobox> cut = Render(selections.Add);
        IElement input = cut.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "result" });

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("false");
        await _focus.Received(1)
            .FocusByIdAsync("address-suggestion-input", true);
        await input.InputAsync(new ChangeEventArgs { Value = "result" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });

        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("false");
        await Assert.That(selections).IsEmpty();
        await _focus.Received(1)
            .FocusByIdAsync("address-suggestion-input", true);
    }

    [Test]
    public async Task SearchFailure_RendersBoundedAlertWithoutEchoingQuery()
    {
        const string QuerySentinel = "query-must-not-appear";
        _service.Search = (_, _, _, _) =>
            Task.FromException<IReadOnlyList<HalResourceOfAddressSuggestionDto>>(
                new HttpRequestException("Synthetic transport failure."));
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();

        await cut.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = QuerySentinel });

        IElement alert = cut.Find(".address-suggestion__error");
        await Assert.That(alert.TextContent).IsEqualTo("Address suggestions are unavailable.");
        await Assert.That(alert.TextContent).DoesNotContain(QuerySentinel);
        await Assert.That(cut.Find("[data-testid='address-suggestion-input']")
            .GetAttribute("aria-expanded")).IsEqualTo("false");
        await _announcer.Received(1)
            .AnnounceAssertiveAsync("Address suggestions are unavailable.");
    }

    [Test]
    public async Task EmptySearch_AnnouncesNoResultsWithoutEchoingTheQuery()
    {
        const string QuerySentinel = "private-query-sentinel";
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([]);
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();

        await cut.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = QuerySentinel });

        IElement status = cut.Find(".address-suggestion__status");
        await Assert.That(status.TextContent).IsEqualTo("No saved addresses found.");
        await Assert.That(status.TextContent).DoesNotContain(QuerySentinel);
        await _announcer.Received(1)
            .AnnouncePoliteAsync("No saved addresses found.");
    }

    [Test]
    public async Task PointerSelection_UpdatesValueClosesPopupAndRestoresInputFocus()
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion(
            "Pointer result",
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate);
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([suggestion]);
        HalResourceOfAddressSuggestionDto? selected = null;
        IRenderedComponent<AddressSuggestionCombobox> cut = Render(
            option => selected = option);

        await cut.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = "pointer" });
        cut.Find("[role='option']").Click();

        await Assert.That(selected).IsSameReferenceAs(suggestion);
        await Assert.That(cut.Find("[data-testid='address-suggestion-input']")
            .GetAttribute("value")).IsEqualTo("Pointer result");
        await Assert.That(cut.FindAll("[role='listbox']")).IsEmpty();
        await _focus.Received(1)
            .FocusByIdAsync("address-suggestion-input", true);
    }

    [Test]
    public async Task ApprovalAffordance_FollowsOnlyAdvertisedHalLink()
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion(
            "Governed result",
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.OrganizationScoped,
            canApprove: true);
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([suggestion]);
        HalLink advertisedLink = suggestion._links!["approve-tenant-address"];
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        IElement input = cut.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "governed" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("[data-testid='address-suggestion-approve']").Click();

        await Assert.That(_service.Approvals).HasSingleItem();
        var approval = _service.Approvals[0];
        await Assert.That(approval.Suggestion).IsSameReferenceAs(suggestion);
        await Assert.That(approval.Link).IsSameReferenceAs(advertisedLink);
        await Assert.That(cut.Find(".address-suggestion__status").TextContent)
            .IsEqualTo("Address approved for tenant reuse.");
        await _announcer.Received(1)
            .AnnouncePoliteAsync("Address approved for tenant reuse.");
    }

    [Test]
    public async Task MissingApprovalRelation_OmitsApprovalControl()
    {
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>(
                [Suggestion(
                    "Private result",
                    LocationAddressSourceEnum.Manual,
                    LocationAddressVisibilityEnum.CreatorPrivate)]);
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        IElement input = cut.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "private" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(cut.FindAll("[data-testid='address-suggestion-approve']"))
            .IsEmpty();
        await Assert.That(_service.Approvals).IsEmpty();
    }

    [Test]
    public async Task DisposeAsync_CancelsPendingSearch()
    {
        var pending = PendingSearch.Create();
        _service.Search = (_, _, _, cancellationToken) =>
            pending.Execute(cancellationToken);
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();

        Task input = cut.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = "pending" });
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cut.Instance.DisposeAsync();

        await Assert.That(pending.Token.IsCancellationRequested).IsTrue();
        pending.Completion.TrySetCanceled(pending.Token);
        await input.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task DisposeAsync_CancelsApprovalAndBlocksPostDisposalSuccess()
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion(
            "Governed result",
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.OrganizationScoped,
            canApprove: true);
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([suggestion]);
        var pending = PendingApproval.Create();
        _service.Approve = pending.Execute;
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        IElement input = cut.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "governed" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Task click = cut.Find("[data-testid='address-suggestion-approve']")
            .ClickAsync(new MouseEventArgs());
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        IElement approvingButton =
            cut.Find("[data-testid='address-suggestion-approve']");
        await Assert.That(approvingButton.HasAttribute("disabled")).IsTrue();
        await Assert.That(approvingButton.GetAttribute("aria-busy")).IsEqualTo("true");
        await Assert.That(approvingButton.TextContent).Contains("Approving address");

        await cut.Instance.DisposeAsync();
        await Assert.That(pending.Token.IsCancellationRequested).IsTrue();
        pending.Completion.TrySetResult();
        await click.WaitAsync(TimeSpan.FromSeconds(3));

        await _announcer.DidNotReceive()
            .AnnouncePoliteAsync("Address approved for tenant reuse.");
    }

    [Test]
    public async Task PendingApproval_DisablesSearchAndKeepsApprovedSelectionStable()
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion(
            "Governed result",
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.OrganizationScoped,
            canApprove: true);
        _service.Search = (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([suggestion]);
        var pending = PendingApproval.Create();
        _service.Approve = pending.Execute;
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        IElement input = cut.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "governed" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Task click = cut.Find("[data-testid='address-suggestion-approve']")
            .ClickAsync(new MouseEventArgs());
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await Assert.That(input.HasAttribute("disabled")).IsTrue();
        await input.InputAsync(new ChangeEventArgs { Value = "replacement" });
        pending.Completion.TrySetResult();
        await click.WaitAsync(TimeSpan.FromSeconds(3));

        await Assert.That(_service.Calls).IsEquivalentTo(["governed"]);
        await Assert.That(cut.Markup).Contains("Address approved for tenant reuse.");
        await Assert.That(cut.FindAll("[data-testid='address-suggestion-approve']"))
            .IsEmpty();
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<AddressSuggestionCombobox> Render(
        Action<HalResourceOfAddressSuggestionDto>? selected = null) =>
        _ctx.Render<AddressSuggestionCombobox>(parameters =>
        {
            parameters.Add(component => component.Label, "Find saved address");
            parameters.Add(
                component => component.SearchLink,
                new HalLink
                {
                    Href = "/api/geocoding/address-suggestions",
                    Method = "POST"
                });
            if (selected is not null)
            {
                parameters.Add(
                    component => component.SuggestionSelected,
                    EventCallback.Factory.Create(this, selected));
            }
        });

    private static HalResourceOfAddressSuggestionDto Suggestion(
        string name,
        LocationAddressSourceEnum source,
        LocationAddressVisibilityEnum visibility,
        bool canApprove = false)
    {
        Guid locationId = Guid.CreateVersion7();
        var suggestion = new HalResourceOfAddressSuggestionDto
        {
            LocationId = locationId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            DisplayName = name,
            Address = "Synthetic address",
            Postcode = "0000",
            Source = source,
            Visibility = visibility,
            _links = new Dictionary<string, HalLink>()
        };
        if (canApprove)
        {
            suggestion._links["approve-tenant-address"] = new HalLink
            {
                Href = $"/api/location/{locationId:D}/address-approval",
                Method = "POST"
            };
        }

        return suggestion;
    }

    private sealed class StubAddressSuggestionService : IAddressSuggestionService
    {
        public List<string> Calls { get; } = [];

        public List<(HalResourceOfAddressSuggestionDto Suggestion, HalLink Link)> Approvals
        {
            get;
        } = [];

        public Func<
            string,
            Guid?,
            int,
            CancellationToken,
            Task<IReadOnlyList<HalResourceOfAddressSuggestionDto>>> Search
        { get; set; } =
            (_, _, _, _) =>
                Task.FromResult<IReadOnlyList<HalResourceOfAddressSuggestionDto>>([]);

        public Func<CancellationToken, Task> Approve { get; set; } =
            _ => Task.CompletedTask;

        public async Task<AddressSuggestionSearchResult> SearchAsync(
            string searchText,
            Guid? organizationId,
            Guid? locationId,
            Guid? expectedConcurrencyStamp,
            int limit,
            HalLink searchLink,
            CancellationToken cancellationToken)
        {
            Calls.Add(searchText);
            IReadOnlyList<HalResourceOfAddressSuggestionDto> suggestions =
                await Search(searchText, organizationId, limit, cancellationToken);
            return new AddressSuggestionSearchResult
            {
                Suggestions = suggestions,
                ProviderOutcome = AddressProviderOutcome.None
            };
        }

        public Task ApproveAsync(
            HalResourceOfAddressSuggestionDto suggestion,
            HalLink link,
            CancellationToken cancellationToken)
        {
            Approvals.Add((suggestion, link));
            return Approve(cancellationToken);
        }
    }

    private sealed class PendingSearch
    {
        private PendingSearch()
        {
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<IReadOnlyList<HalResourceOfAddressSuggestionDto>>
            Completion
        { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Token { get; private set; }

        public static PendingSearch Create() => new();

        public Task<IReadOnlyList<HalResourceOfAddressSuggestionDto>> Execute(
            CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            return Completion.Task;
        }
    }

    private sealed class PendingApproval
    {
        private PendingApproval()
        {
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Token { get; private set; }

        public static PendingApproval Create() => new();

        public Task Execute(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            return Completion.Task;
        }
    }
}
