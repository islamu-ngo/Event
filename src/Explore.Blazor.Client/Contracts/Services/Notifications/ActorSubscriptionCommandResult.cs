namespace Explore.Blazor.Client.Contracts.Services.Notifications;

public sealed record ActorSubscriptionCommandResult(
    bool Success,
    Guid? SubscriptionId = null,
    string? Message = null,
    IReadOnlyList<string>? Errors = null)
{
    public static ActorSubscriptionCommandResult Failed(string message) =>
        new(false, Message: message, Errors: [message]);
}
