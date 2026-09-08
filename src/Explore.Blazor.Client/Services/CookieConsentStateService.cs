namespace Explore.Blazor.Client.Services;

public sealed class CookieConsentStateService
{
    public event Func<Task>? OnReopenRequested;

    public async Task RequestReopenAsync()
    {
        if (OnReopenRequested is not null)
        {
            await OnReopenRequested.Invoke();
        }
    }
}
