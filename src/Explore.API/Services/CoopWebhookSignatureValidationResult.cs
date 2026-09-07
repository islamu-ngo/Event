using Microsoft.AspNetCore.Http;

namespace Explore.API.Services;

public sealed record CoopWebhookSignatureValidationResult(
    bool Succeeded,
    string? Body,
    int StatusCode,
    string Title,
    string Type,
    string Detail,
    string Code)
{
    public static CoopWebhookSignatureValidationResult Success(string body) =>
        new(true, body, StatusCodes.Status200OK, string.Empty, string.Empty, string.Empty, string.Empty);

    public static CoopWebhookSignatureValidationResult Failure(
        int statusCode,
        string title,
        string type,
        string detail,
        string code) =>
        new(false, null, statusCode, title, type, detail, code);
}
