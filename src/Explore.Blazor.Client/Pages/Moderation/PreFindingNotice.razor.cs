using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Services.Moderation;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Pages.Moderation;

public partial class PreFindingNotice : ComponentBase, IDisposable
{
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    [Parameter] public Guid NoticeId { get; set; }
    [Parameter, SupplyParameterFromQuery(Name = "reviewResponse")] public bool ReviewResponse { get; set; }
    [Inject] private IPreFindingNoticeService NoticeService { get; set; } = null!;
    [Inject] private TimeProvider Clock { get; set; } = null!;
    [Inject] private IAccessibilityFocusService Focus { get; set; } = null!;

    private PreFindingNoticeView? _view;
    private CancellationTokenSource? _route;
    private ITimer? _expiry;
    private Guid _loadedId;
    private bool _loadedReview;
    private bool _disposed;
    private bool _busy;
    private bool _inputError;
    private bool _focusResponse;
    private string _statement = string.Empty;
    private PreFindingNoticeFailure _failure;

    private string FailureMessage => _failure switch
    {
        PreFindingNoticeFailure.SignInRequired => "Sign in again to request this protected notice.",
        PreFindingNoticeFailure.Conflict => "The notice changed. Reload before making a new explicit submission.",
        PreFindingNoticeFailure.RateLimited => "The owner limited this request. No automatic retry was made.",
        PreFindingNoticeFailure.Expired => "This notice has expired. Its private content has been cleared.",
        _ => "The notice or operation is unavailable. A write may have reached the owner; reload to check its recorded status before deciding what to do."
    };

    protected override async Task OnParametersSetAsync()
    {
        if (_route is not null && _loadedId == NoticeId && _loadedReview == ReviewResponse)
            return;
        _loadedId = NoticeId;
        _loadedReview = ReviewResponse;
        await ReloadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusResponse)
            return;
        _focusResponse = false;
        if (!_disposed && _view is not null && _inputError)
            await Focus.FocusByIdAsync("pre-finding-response", true);
    }

    private async Task ReloadAsync()
    {
        if (_disposed)
            return;
        ClearRoute();
        var route = _route = new CancellationTokenSource();
        _failure = PreFindingNoticeFailure.None;
        _busy = true;
        await ReadAsync(route, NoticeId, ReviewResponse);
    }

    private async Task ReadAsync(CancellationTokenSource route, Guid id, bool review)
    {
        try
        {
            var result = await NoticeService.GetAsync(id, review, route.Token);
            if (!IsCurrent(route))
                return;
            _view = result.View;
            _failure = result.Failure;
            if (_view is { } view)
            {
                var retainUntil = view.RetainUntil;
                var remaining = retainUntil - Clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                    Expire(route);
                else
                    _expiry = Clock.CreateTimer(state =>
                        { _ = InvokeAsync(() => CheckExpiry(route, retainUntil)); },
                        null, remaining > MaximumTimerDelay ? MaximumTimerDelay : remaining,
                        Timeout.InfiniteTimeSpan);
            }
        }
        catch (OperationCanceledException) when (route.IsCancellationRequested) { }
        finally
        {
            if (IsCurrent(route))
                _busy = false;
        }
    }

    private Task AcknowledgeAsync() => WriteAsync(false);
    private Task RespondAsync() => WriteAsync(true);

    private async Task WriteAsync(bool respond)
    {
        if (_busy || _route is not { } route || _view is not { } view
            || (respond ? !view.HasSubmitResponseLink : !view.HasAcknowledgeLink))
            return;
        if (view.RetainUntil <= Clock.GetUtcNow())
        {
            Expire(route);
            return;
        }
        if (respond && !PreFindingNoticeService.IsValidResponse(_statement))
        {
            _inputError = true;
            _focusResponse = true;
            return;
        }
        var statement = _statement;
        _view = null;
        _statement = string.Empty;
        _inputError = false;
        _busy = true;
        _failure = PreFindingNoticeFailure.None;
        try
        {
            var failure = respond
                ? await NoticeService.RespondAsync(view, statement, route.Token)
                : await NoticeService.AcknowledgeAsync(view, route.Token);
            if (!IsCurrent(route))
                return;
            _expiry?.Dispose();
            _expiry = null;
            _failure = failure;
            // Only a fresh GET supplies status and affordances. No acknowledgement is fabricated.
            if (failure == PreFindingNoticeFailure.None)
                await ReadAsync(route, view.NoticeId, ReviewResponse);
        }
        catch (OperationCanceledException) when (route.IsCancellationRequested) { }
        finally
        {
            if (IsCurrent(route))
                _busy = false;
        }
    }

    private bool IsCurrent(CancellationTokenSource route) =>
        !_disposed && ReferenceEquals(_route, route) && !route.IsCancellationRequested;

    private void CheckExpiry(CancellationTokenSource route, DateTimeOffset retainUntil)
    {
        if (!IsCurrent(route))
            return;
        var remaining = retainUntil - Clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            Expire(route);
        else
            _expiry?.Change(remaining > MaximumTimerDelay ? MaximumTimerDelay : remaining,
                Timeout.InfiniteTimeSpan);
    }

    private void Expire(CancellationTokenSource route)
    {
        if (!IsCurrent(route))
            return;
        ClearRoute();
        _failure = PreFindingNoticeFailure.Expired;
        _busy = false;
        StateHasChanged();
    }

    private void ClearRoute()
    {
        _expiry?.Dispose();
        _expiry = null;
        _route?.Cancel();
        _route?.Dispose();
        _route = null;
        _view = null;
        _statement = string.Empty;
        _inputError = false;
        _focusResponse = false;
    }

    public void Dispose()
    {
        _disposed = true;
        ClearRoute();
    }
}
