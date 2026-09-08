using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Components.Locations;

public partial class AddressSuggestionCombobox : ComponentBase, IAsyncDisposable
{
    private const int MinimumSearchLength = 2;
    private const string KeyboardModulePath = "/js/address-suggestion-combobox.js";
    private const string SearchFailureMessage =
        "Address suggestions are unavailable.";
    private const string ApprovalFailureMessage =
        "Address approval is unavailable.";
    private const string ApprovalSuccessMessage =
        "Address approved for tenant reuse.";
    private readonly CancellationTokenSource _disposal = new();
    private ElementReference _input;
    private IJSObjectReference? _keyboardModule;
    private IReadOnlyList<HalResourceOfAddressSuggestionDto> _suggestions = [];
    private CancellationTokenSource? _searchCancellation;
    private string _searchText = string.Empty;
    private string? _approvalMessage;
    private string? _errorMessage;
    private HalResourceOfAddressSuggestionDto? _selectedSuggestion;
    private AddressProviderOutcome _providerOutcome;
    private int _activeIndex = -1;
    private long _requestVersion;
    private bool _hasSearched;
    private bool _isLoading;
    private bool _isOpen;
    private bool _isApproving;
    private bool _disposed;

    [Inject]
    private IAddressSuggestionService Suggestions { get; set; } = default!;

    [Inject]
    private IAccessibilityAnnouncerService Announcer { get; set; } = default!;

    [Inject]
    private IAccessibilityFocusService Focus { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Parameter]
    public string Id { get; set; } = "address-suggestion";

    [Parameter]
    public string Label { get; set; } = "Find saved address";

    [Parameter]
    public Guid? OrganizationId { get; set; }

    [Parameter]
    public Guid? LocationId { get; set; }

    [Parameter]
    public Guid? ExpectedConcurrencyStamp { get; set; }

    [Parameter]
    [EditorRequired]
    public HalLink SearchLink { get; set; } = default!;

    [Parameter]
    public int Limit { get; set; } = 10;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<HalResourceOfAddressSuggestionDto> SuggestionSelected { get; set; }

    private string? ActiveDescendant =>
        _isOpen && _activeIndex >= 0 ? OptionId(_activeIndex) : null;

    private HalLink? ApprovalLink =>
        _selectedSuggestion?._links is { } links
        && links.TryGetValue("approve-tenant-address", out HalLink? link)
            ? link
            : null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed)
        {
            return;
        }

        _keyboardModule =
            await JsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                KeyboardModulePath);
        await _keyboardModule.InvokeVoidAsync(
            "bindComboboxNavigation",
            _input);
        await _keyboardModule.InvokeVoidAsync(
            "ensureContainingDialogModal",
            _input);
    }

    private async Task SearchAsync(ChangeEventArgs args)
    {
        if (_isApproving)
        {
            return;
        }

        _searchText = args.Value?.ToString() ?? string.Empty;
        long version = ++_requestVersion;
        CancelCurrentSearch();
        ResetResults();

        string searchText = _searchText.Trim();
        if (searchText.Length < MinimumSearchLength)
        {
            return;
        }

        _hasSearched = true;
        _isLoading = true;
        _searchCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token);
        CancellationToken cancellationToken = _searchCancellation.Token;

        try
        {
            AddressSuggestionSearchResult result =
                await Suggestions.SearchAsync(
                    searchText,
                    OrganizationId,
                    LocationId,
                    ExpectedConcurrencyStamp,
                    Limit,
                    SearchLink,
                    cancellationToken);
            if (version != _requestVersion || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _suggestions = result.Suggestions;
            _providerOutcome = result.ProviderOutcome;
            _isOpen = result.Suggestions.Count > 0;
            string resultAnnouncement = result.Suggestions.Count switch
            {
                0 => "No saved addresses found.",
                1 => "1 saved address found.",
                _ => $"{result.Suggestions.Count} saved addresses found."
            };
            string? providerAnnouncement =
                result.ProviderOutcome == AddressProviderOutcome.None
                    ? null
                    : ProviderOutcomeMessage(result.ProviderOutcome);
            await Announcer.AnnouncePoliteAsync(
                providerAnnouncement is null
                    ? resultAnnouncement
                    : $"{resultAnnouncement} {providerAnnouncement}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (version == _requestVersion && !cancellationToken.IsCancellationRequested)
            {
                _errorMessage = SearchFailureMessage;
                _isOpen = false;
                await Announcer.AnnounceAssertiveAsync(SearchFailureMessage);
            }
        }
        finally
        {
            if (!_disposed && version == _requestVersion)
            {
                _isLoading = false;
            }
        }
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.IsComposing)
        {
            return;
        }

        switch (args.Key)
        {
            case "Escape":
                Close();
                await Focus.FocusByIdAsync($"{Id}-input", preventScroll: true);
                return;
            case "Tab":
                Close();
                return;
            case "ArrowDown" when _suggestions.Count > 0:
                _isOpen = true;
                _activeIndex = _activeIndex < _suggestions.Count - 1
                    ? _activeIndex + 1
                    : 0;
                await ScrollActiveOptionIntoViewAsync();
                return;
            case "ArrowUp" when _suggestions.Count > 0:
                _isOpen = true;
                _activeIndex = _activeIndex > 0
                    ? _activeIndex - 1
                    : _suggestions.Count - 1;
                await ScrollActiveOptionIntoViewAsync();
                return;
            case "Home" when _suggestions.Count > 0:
                _isOpen = true;
                _activeIndex = 0;
                await ScrollActiveOptionIntoViewAsync();
                return;
            case "End" when _suggestions.Count > 0:
                _isOpen = true;
                _activeIndex = _suggestions.Count - 1;
                await ScrollActiveOptionIntoViewAsync();
                return;
            case "Enter" when _isOpen && _activeIndex >= 0:
                await SelectAsync(_activeIndex);
                return;
        }
    }

    private async Task SelectAsync(int index)
    {
        HalResourceOfAddressSuggestionDto selected = _suggestions[index];
        _searchText = selected.DisplayName;
        _selectedSuggestion = selected;
        _approvalMessage = null;
        Close();
        await SuggestionSelected.InvokeAsync(selected);
        if (!_disposed)
        {
            await Focus.FocusByIdAsync($"{Id}-input", preventScroll: true);
        }
    }

    private async Task ApproveAsync(HalLink link)
    {
        if (_selectedSuggestion is not { } selected || _isApproving)
        {
            return;
        }

        _isApproving = true;
        _approvalMessage = null;
        _errorMessage = null;
        try
        {
            await Announcer.AnnouncePoliteAsync("Approving address.");
            await Suggestions.ApproveAsync(
                selected,
                link,
                _disposal.Token);
            if (_disposed || !ReferenceEquals(_selectedSuggestion, selected))
            {
                return;
            }

            selected._links?.Remove("approve-tenant-address");
            _approvalMessage = ApprovalSuccessMessage;
            await Announcer.AnnouncePoliteAsync(ApprovalSuccessMessage);
        }
        catch (OperationCanceledException) when (_disposal.IsCancellationRequested)
        {
        }
        catch
        {
            if (!_disposed)
            {
                _errorMessage = ApprovalFailureMessage;
                await Announcer.AnnounceAssertiveAsync(ApprovalFailureMessage);
            }
        }
        finally
        {
            if (!_disposed)
            {
                _isApproving = false;
            }
        }
    }

    private void ResetResults()
    {
        _suggestions = [];
        _selectedSuggestion = null;
        _approvalMessage = null;
        _activeIndex = -1;
        _hasSearched = false;
        _isLoading = false;
        _isOpen = false;
        _errorMessage = null;
        _providerOutcome = AddressProviderOutcome.None;
    }

    private void Close()
    {
        _isOpen = false;
        _activeIndex = -1;
    }

    private ValueTask ScrollActiveOptionIntoViewAsync() =>
        _keyboardModule is null
            ? ValueTask.CompletedTask
            : _keyboardModule.InvokeVoidAsync(
                "scrollActiveOptionIntoView",
                _input);

    private void CancelCurrentSearch()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }

    private string OptionId(int index) => $"{Id}-option-{index}";

    private static string AriaBoolean(bool value) => value ? "true" : "false";

    private static string SourceLabel(LocationAddressSourceEnum source) =>
        source == LocationAddressSourceEnum.ProviderSelection ? "Provider" : "Local";

    private static string VisibilityLabel(LocationAddressVisibilityEnum visibility) =>
        visibility switch
        {
            LocationAddressVisibilityEnum.CreatorPrivate => "Mine",
            LocationAddressVisibilityEnum.OrganizationScoped => "Organization",
            LocationAddressVisibilityEnum.TenantApproved => "Tenant approved",
            _ => "Restricted"
        };

    private static string ProviderOutcomeValue(AddressProviderOutcome outcome) =>
        outcome.ToString().ToLowerInvariant();

    private static string ProviderOutcomeClass(AddressProviderOutcome outcome) =>
        outcome == AddressProviderOutcome.None
            ? "address-suggestion__provider-state"
            : "address-suggestion__status";

    private static string? ProviderOutcomeMessage(AddressProviderOutcome outcome) =>
        outcome switch
        {
            AddressProviderOutcome.None => "Using saved addresses only.",
            AddressProviderOutcome.Timeout =>
                "The address provider timed out; any saved addresses are shown.",
            AddressProviderOutcome.Unavailable =>
                "The address provider is unavailable; any saved addresses are shown.",
            AddressProviderOutcome.Limited =>
                "The address provider is temporarily limited; available results are shown.",
            _ => null
        };

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposal.Cancel();
        CancelCurrentSearch();

        if (_keyboardModule is not null)
        {
            try
            {
                await _keyboardModule.InvokeVoidAsync(
                    "unbindComboboxNavigation",
                    _input);
                await _keyboardModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _disposal.Dispose();
        GC.SuppressFinalize(this);
    }
}
