using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using MudBlazor.Interop;
using MudBlazor.Services;

namespace Explore.Blazor.Client.Tests.Common;

/// <summary>
/// Mock popover service that prevents mudPopover.* JS interop calls.
/// Based on MudBlazor's own <c>MockPopoverService</c> pattern.
/// </summary>
internal sealed class MockPopoverService : IPopoverService
{
    public PopoverOptions PopoverOptions { get; } = new();

    public IEnumerable<IMudPopoverHolder> ActivePopovers { get; } = [];

    public bool IsInitialized => false;

    public void Subscribe(IPopoverObserver observer) { }

    public void Unsubscribe(IPopoverObserver observer) { }

    public Task CreatePopoverAsync(IPopover popover) => Task.CompletedTask;

    public Task<bool> UpdatePopoverAsync(IPopover popover) => Task.FromResult(true);

    public Task<bool> DestroyPopoverAsync(IPopover popover) => Task.FromResult(true);

    public ValueTask<int> GetProviderCountAsync() => ValueTask.FromResult(0);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock resize observer factory that returns no-op observers.
/// Prevents mudResizeObserver.* JS interop calls.
/// </summary>
internal sealed class MockResizeObserverFactory : IResizeObserverFactory
{
    public IResizeObserver Create(ResizeObserverOptions options) => new MockResizeObserver();

    public IResizeObserver Create() => Create(new ResizeObserverOptions());
}

/// <summary>
/// Mock resize observer that tracks observed elements without JS interop.
/// Returns empty bounding rects for all observations.
/// </summary>
internal sealed class MockResizeObserver : IResizeObserver
{
    public event SizeChanged? OnResized;

    public Task<BoundingClientRect?> Observe(ElementReference element)
        => Task.FromResult<BoundingClientRect?>(new BoundingClientRect());

    public Task<IEnumerable<BoundingClientRect>> Observe(IEnumerable<ElementReference> elements)
        => Task.FromResult<IEnumerable<BoundingClientRect>>([]);

    public Task Unobserve(ElementReference element) => Task.CompletedTask;

    public BoundingClientRect GetSizeInfo(ElementReference reference) => new();

    public double GetHeight(ElementReference reference) => 0;

    public double GetWidth(ElementReference reference) => 0;

    public bool IsElementObserved(ElementReference reference) => false;

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Suppress unused event warning — required by interface
    private void SuppressWarning() => OnResized?.Invoke(new Dictionary<ElementReference, BoundingClientRect>());
}

/// <summary>
/// Mock key interceptor service that prevents mudKeyInterceptor.* JS interop calls.
/// </summary>
internal sealed class MockKeyInterceptorService : IKeyInterceptorService
{
    public Task SubscribeAsync(IKeyInterceptorObserver observer, KeyInterceptorOptions options) => Task.CompletedTask;

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, Action<KeyMapBuilder> configure) => Task.CompletedTask;

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, IKeyDownObserver? keyDown = null, IKeyUpObserver? keyUp = null) => Task.CompletedTask;

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, Action<KeyboardEventArgs>? keyDown = null, Action<KeyboardEventArgs>? keyUp = null) => Task.CompletedTask;

    public Task SubscribeAsync(string elementId, KeyInterceptorOptions options, Func<KeyboardEventArgs, Task>? keyDown = null, Func<KeyboardEventArgs, Task>? keyUp = null) => Task.CompletedTask;

    public Task DispatchAsync(string elementId, KeyEventKind kind, KeyboardEventArgs args) => Task.CompletedTask;

    public Task UpdateKeyAsync(IKeyInterceptorObserver observer, KeyOptions option) => Task.CompletedTask;

    public Task UpdateKeyAsync(string elementId, KeyOptions option) => Task.CompletedTask;

    public Task UnsubscribeAsync(IKeyInterceptorObserver observer) => Task.CompletedTask;

    public Task UnsubscribeAsync(string elementId) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock JS event factory that returns no-op event handlers.
/// Prevents mudJsEvent.* JS interop calls.
/// </summary>
internal sealed class MockJsEventFactory : IJsEventFactory
{
    public IJsEvent Create() => new MockJsEvent();
}

/// <summary>
/// Mock JS event handler that prevents JS interop during event management.
/// </summary>
internal sealed class MockJsEvent : IJsEvent
{
#pragma warning disable CS0067 // Events required by interface but never raised in mock
    public event Action<int>? CaretPositionChanged;
    public event Action<string>? Paste;
    public event Action<int, int>? Select;
#pragma warning restore CS0067

    public Task Connect(string element, JsEventOptions options) => Task.CompletedTask;

    public Task Disconnect() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// IJsApiService is NOT mocked here as a concrete class because MudBlazor v9 declares
// UpdateStyleProperty as internal without a default interface method accessible from
// external assemblies. NSubstitute (Castle.DynamicProxy) can implement the full interface
// at runtime. IJsApiService methods all return ValueTask — no NRE risk from null defaults.
// Registration: Services.AddTransient(_ => Substitute.For<IJsApiService>()) in BlazorTestContext.

/// <summary>
/// Mock scroll manager that prevents mudScrollManager.* JS interop calls.
/// </summary>
internal sealed class MockScrollManager : IScrollManager
{
    public string? Selector { get; set; }

    public ValueTask LockScrollAsync(string elementId, string cssClass) => ValueTask.CompletedTask;

    public Task ScrollTo(int left, int top, ScrollBehavior scrollBehavior) => Task.CompletedTask;

    public ValueTask ScrollToAsync(string? id, int left, int top, ScrollBehavior scrollBehavior) => ValueTask.CompletedTask;

    public ValueTask ScrollIntoViewAsync(string? selector, ScrollBehavior behavior) => ValueTask.CompletedTask;

    public Task ScrollToFragment(string id, ScrollBehavior behavior) => Task.CompletedTask;

    public ValueTask ScrollToFragmentAsync(string id, ScrollBehavior behavior) => ValueTask.CompletedTask;

    public ValueTask ScrollToListItemAsync(string elementId) => ValueTask.CompletedTask;

    public Task ScrollToTop(ScrollBehavior scrollBehavior = ScrollBehavior.Auto) => Task.CompletedTask;

    public ValueTask ScrollToTopAsync(string? id, ScrollBehavior scrollBehavior = ScrollBehavior.Auto) => ValueTask.CompletedTask;

    public ValueTask ScrollToBottomAsync(string id, ScrollBehavior scrollBehavior = ScrollBehavior.Auto) => ValueTask.CompletedTask;

    public ValueTask ScrollToYearAsync(string elementId) => ValueTask.CompletedTask;

    public ValueTask UnlockScrollAsync(string elementId, string cssClass) => ValueTask.CompletedTask;

    public ValueTask ScrollToVirtualizedItemAsync(string containerId, int itemIndex, double itemHeight, string targetItemId, ScrollBehavior scrollBehavior = ScrollBehavior.Auto)
        => ValueTask.CompletedTask;
}

/// <summary>
/// Mock scroll listener factory that returns no-op listeners.
/// </summary>
internal sealed class MockScrollListenerFactory : IScrollListenerFactory
{
    public IScrollListener Create(string? selector) => Create(selector, 100);

    public IScrollListener Create(string? selector, int reportRateMs) => new MockScrollListener { Selector = selector };
}

/// <summary>
/// Mock scroll listener that prevents scroll listener JS interop calls.
/// </summary>
internal sealed class MockScrollListener : IScrollListener
{
    public string? Selector { get; set; }

    public int ReportRateMs { get; set; } = 100;

#pragma warning disable CS0067 // Event required by interface but never raised in mock
    public event EventHandler<ScrollEventArgs>? OnScroll;
#pragma warning restore CS0067

    public ValueTask<ScrollEventArgs> GetCurrentScrollDataAsync()
        => ValueTask.FromResult(new ScrollEventArgs());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock scroll spy factory that returns no-op spy instances.
/// </summary>
internal sealed class MockScrollSpyFactory : IScrollSpyFactory
{
    public IScrollSpy Create() => new MockScrollSpy();
}

/// <summary>
/// Mock scroll spy that prevents scroll spy JS interop calls.
/// </summary>
internal sealed class MockScrollSpy : IScrollSpy
{
    public string? CenteredSection { get; set; }

#pragma warning disable CS0067 // Event required by interface but never raised in mock
    public event EventHandler<ScrollSectionCenteredEventArgs>? ScrollSectionSectionCentered;
#pragma warning restore CS0067

    public Task ScrollToSection(string id) => Task.CompletedTask;

    public Task ScrollToSection(Uri uri) => Task.CompletedTask;

    public Task SetSectionAsActive(string id) => Task.CompletedTask;

    public Task StartSpying(string containerSelector, string sectionClassSelector) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock browser viewport service that prevents viewport detection JS interop calls.
/// Returns desktop-sized viewport to avoid MudBlazor mobile mode in tests.
/// </summary>
internal sealed class MockBrowserViewportService : IBrowserViewportService
{
    public ResizeOptions ResizeOptions { get; } = new();

    public Task SubscribeAsync(IBrowserViewportObserver observer, bool fireImmediately = true) => Task.CompletedTask;

    public Task SubscribeAsync(Guid observerId, Action<BrowserViewportEventArgs> lambda, ResizeOptions? options = null, bool fireImmediately = true) => Task.CompletedTask;

    public Task SubscribeAsync(Guid observerId, Func<BrowserViewportEventArgs, Task> lambda, ResizeOptions? options = null, bool fireImmediately = true) => Task.CompletedTask;

    public Task UnsubscribeAsync(IBrowserViewportObserver observer) => Task.CompletedTask;

    public Task UnsubscribeAsync(Guid observerId) => Task.CompletedTask;

    public Task<bool> IsMediaQueryMatchAsync(string mediaQuery) => Task.FromResult(false);

    public Task<BrowserWindowSize> GetCurrentBrowserWindowSizeAsync()
        => Task.FromResult(new BrowserWindowSize { Width = 1920, Height = 1080 });

    public Task<bool> IsBreakpointWithinReferenceSizeAsync(Breakpoint breakpoint, Breakpoint referenceBreakpoint)
        => Task.FromResult(false);

    public Task<bool> IsBreakpointWithinWindowSizeAsync(Breakpoint breakpoint)
        => Task.FromResult(breakpoint == Breakpoint.Lg || breakpoint == Breakpoint.Xl || breakpoint == Breakpoint.Xxl);

    public Task<Breakpoint> GetCurrentBreakpointAsync()
        => Task.FromResult(Breakpoint.Lg);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
