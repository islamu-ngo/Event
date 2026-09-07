using Explore.Application.DTOs.Webhooks;

namespace Explore.Application.Responses;

public sealed record WebhookProviderPortalAccessCommandResponse : BaseCommandResponse<WebhookProviderPortalAccessDto>
{
    private WebhookProviderPortalAccessCommandResponse(
        BaseCommandResponse<WebhookProviderPortalAccessDto> state,
        bool isRetryable) : base(state, true)
    {
        IsRetryable = isRetryable;
    }

    [System.Text.Json.Serialization.JsonConstructor]
    internal WebhookProviderPortalAccessCommandResponse(
        WebhookProviderPortalAccessDto? id,
        bool isSuccess,
        string? message,
        IReadOnlyList<string>? errors,
        string? failureCode,
        QuotaExceededDetails? quotaExceeded,
        bool isRetryable)
        : this(BaseCommandResponse.Restore(id, isSuccess, message, errors, failureCode, quotaExceeded), isRetryable)
    {
    }

    public bool IsRetryable { get; }

    public static WebhookProviderPortalAccessCommandResponse Success(
        WebhookProviderPortalAccessDto id,
        string? message,
        bool isRetryable) =>
        new(BaseCommandResponse.Success(id, message), isRetryable);

    public static WebhookProviderPortalAccessCommandResponse Failure(
        BaseCommandResponse<WebhookProviderPortalAccessDto> failure,
        bool isRetryable) =>
        new(WithoutCapabilityPayload(failure), isRetryable);

    private static BaseCommandResponse<WebhookProviderPortalAccessDto> WithoutCapabilityPayload(
        BaseCommandResponse<WebhookProviderPortalAccessDto> failure)
    {
        BaseCommandResponse<WebhookProviderPortalAccessDto> requiredFailure =
            BaseCommandResponse.RequireFailure(failure);
        return BaseCommandResponse.Restore<WebhookProviderPortalAccessDto>(
            null,
            false,
            requiredFailure.Message,
            requiredFailure.Errors,
            requiredFailure.FailureCode,
            requiredFailure.QuotaExceeded);
    }
}
