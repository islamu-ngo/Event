using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Notifications;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Shared;

public partial class ActorSubscriptionButton
{
    private const string ActiveStatusCode = "ACTIVE";

    private ActorSubscriptionDto? _subscription;
    private bool _isLoading;
    private bool _isSaving;
    private string? _errorMessage;

    [Inject] public required IActorSubscriptionService ActorSubscriptionService { get; set; }
    [Inject] public required IAccessibilityAnnouncerService AccessibilityAnnouncer { get; set; }

    [Parameter] public Guid? TargetActorId { get; set; }
    [Parameter] public string TargetName { get; set; } = "this organizer";
    [Parameter] public bool CanSubscribe { get; set; }
    [Parameter] public bool CanViewSubscription { get; set; }
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public Size Size { get; set; } = Size.Medium;
    [Parameter] public EventCallback<bool> SubscriptionChanged { get; set; }

    private bool HasTarget => TargetActorId is { } id && id != Guid.Empty;
    private bool ShouldRenderButton => HasTarget && (CanSubscribe || CanViewSubscription);
    private bool IsSubscribed => string.Equals(_subscription?.StatusCode, ActiveStatusCode, StringComparison.OrdinalIgnoreCase);
    private bool IsDisabled => _isLoading || _isSaving || (!IsSubscribed && !CanSubscribe);
    private string ButtonText => _isLoading ? "Checking..." : _isSaving ? "Updating..." : IsSubscribed ? "Subscribed" : "Subscribe";
    private string ButtonIcon => IsSubscribed ? Icons.Material.Filled.NotificationsActive : Icons.Material.Filled.NotificationsNone;
    private Color ButtonColor => IsSubscribed ? Color.Success : Color.Primary;
    private Variant ButtonVariant => IsSubscribed ? Variant.Outlined : Variant.Filled;
    private string AccessibleLabel => IsSubscribed
        ? $"Unsubscribe from notifications for {TargetName}"
        : $"Subscribe to notifications for {TargetName}";

    protected override async Task OnParametersSetAsync()
    {
        if (!ShouldRenderButton || !CanViewSubscription)
        {
            _subscription = null;
            return;
        }

        _isLoading = true;
        _errorMessage = null;

        try
        {
            _subscription = await ActorSubscriptionService.GetSubscriptionAsync(TargetActorId!.Value);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task ToggleSubscriptionAsync()
    {
        if (!HasTarget || _isSaving || _isLoading)
        {
            return;
        }

        _isSaving = true;
        _errorMessage = null;

        try
        {
            var result = IsSubscribed ? await UnsubscribeAsync() : await SubscribeAsync();
            if (!result.Success)
            {
                _errorMessage = result.Message ?? "Subscription update failed.";
                await AccessibilityAnnouncer.AnnounceAssertiveAsync(_errorMessage);
                return;
            }

            if (CanViewSubscription)
            {
                _subscription = await ActorSubscriptionService.GetSubscriptionAsync(TargetActorId!.Value);
            }

            await SubscriptionChanged.InvokeAsync(IsSubscribed);
            await AccessibilityAnnouncer.AnnouncePoliteAsync(IsSubscribed
                ? $"Subscribed to notifications for {TargetName}."
                : $"Unsubscribed from notifications for {TargetName}.");
        }
        finally
        {
            _isSaving = false;
        }
    }

    private Task<ActorSubscriptionCommandResult> SubscribeAsync() =>
        CanSubscribe
            ? ActorSubscriptionService.SubscribeAsync(TargetActorId!.Value)
            : Task.FromResult(ActorSubscriptionCommandResult.Failed("Subscription action is not available."));

    private Task<ActorSubscriptionCommandResult> UnsubscribeAsync()
    {
        if (_subscription?.ConcurrencyStamp is not { } concurrencyStamp)
        {
            return Task.FromResult(ActorSubscriptionCommandResult.Failed("Refresh the page before changing this subscription."));
        }

        return ActorSubscriptionService.UnsubscribeAsync(TargetActorId!.Value, concurrencyStamp);
    }
}
