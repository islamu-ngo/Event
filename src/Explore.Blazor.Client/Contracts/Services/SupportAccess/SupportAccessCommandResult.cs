namespace Explore.Blazor.Client.Contracts.Services.SupportAccess;

public sealed record SupportAccessCommandResult(bool Success, string? ErrorMessage)
{
    public static SupportAccessCommandResult Succeeded() => new(true, null);

    public static SupportAccessCommandResult Failed(string errorMessage) => new(false, errorMessage);
}
