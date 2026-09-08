using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.EventReporting;

public sealed record ModerationReportActionResult(
    bool Success,
    Guid? Id,
    string Message,
    IReadOnlyList<string> Errors,
    string? FailureCode)
{
    public static ModerationReportActionResult Successful(BaseCommandResponseOfGuid response)
        => new(
            true,
            response.Id,
            string.IsNullOrWhiteSpace(response.Message) ? "Moderation report updated." : response.Message,
            [],
            response.FailureCode);

    public static ModerationReportActionResult Failed(
        string? message,
        IEnumerable<string>? errors = null,
        string? failureCode = null)
        => new(
            false,
            null,
            string.IsNullOrWhiteSpace(message) ? "Moderation report action failed." : message,
            errors?.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray() ?? [],
            failureCode);
}
